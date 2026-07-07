using System;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace PhysicsBox3D
{
    /// <summary>
    /// Runs Box3D's parallel tasks on Unity's existing C# Job System worker pool instead
    /// of Box3D's own threads. In this mode Box3D creates zero OS threads (external
    /// scheduler), so total live thread count stays at Unity's pool (~core count) — no
    /// oversubscription against the engine's other jobs.
    ///
    /// Box3D only ever enqueues from the b3World_Step thread (the Unity main thread) and
    /// does so serially, so the small handle table needs no locking. Box3D's solve is
    /// self-healing: the step thread runs worker 0 inline and can complete the whole step
    /// alone, so the jobs scheduled here are pure optional speedup — if Unity's pool is
    /// busy and never runs them, physics still finishes (no deadlock, no stall).
    ///
    /// Single-world: state is static, matching Box3DSimulation's singleton world.
    /// </summary>
    internal static unsafe class Box3DJobBridge
    {
        // Mirrors b3EnqueueTaskCallback: void*(b3TaskCallback*, void* taskContext, void* userContext, const char* name).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr EnqueueDelegate(IntPtr task, IntPtr taskContext, IntPtr userContext, IntPtr name);

        // Mirrors b3FinishTaskCallback: void(void* userTask, void* userContext).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FinishDelegate(IntPtr userTask, IntPtr userContext);

        // Held in static fields so the GC can't collect the delegates while native holds their pointers.
        private static readonly EnqueueDelegate s_enqueue = Enqueue;
        private static readonly FinishDelegate s_finish = Finish;

        public static readonly IntPtr EnqueuePtr = Marshal.GetFunctionPointerForDelegate(s_enqueue);
        public static readonly IntPtr FinishPtr = Marshal.GetFunctionPointerForDelegate(s_finish);

        // Outstanding tasks per step are bounded by workerCount plus a few parallel-for
        // fan-outs; 128 is comfortably above B3_MAX_WORKERS.
        private const int MaxOutstanding = 128;
        private static readonly JobHandle[] s_handles = new JobHandle[MaxOutstanding];
        private static readonly bool[] s_used = new bool[MaxOutstanding];
        private static int s_mainThreadId;

        /// <summary>Record the thread that drives b3World_Step (Unity's main thread).</summary>
        public static void CaptureMainThread() => s_mainThreadId = Thread.CurrentThread.ManagedThreadId;

        // Invokes one Box3D task (b3TaskCallback) on a Unity job worker.
        private struct TaskJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public IntPtr Task;
            [NativeDisableUnsafePtrRestriction] public IntPtr Context;

            public void Execute() => ((delegate* unmanaged[Cdecl]<IntPtr, void>)Task)(Context);
        }

        [MonoPInvokeCallback(typeof(EnqueueDelegate))]
        private static IntPtr Enqueue(IntPtr task, IntPtr taskContext, IntPtr userContext, IntPtr name)
        {
            // Unity forbids scheduling jobs off the main thread. Box3D never enqueues from a
            // worker today, but if it ever did we run the task inline: returning null tells
            // Box3D it was executed serially and needs no matching finish call.
            if (Thread.CurrentThread.ManagedThreadId != s_mainThreadId)
            {
                ((delegate* unmanaged[Cdecl]<IntPtr, void>)task)(taskContext);
                return IntPtr.Zero;
            }

            int slot = -1;
            for (int i = 0; i < MaxOutstanding; i++)
            {
                if (!s_used[i]) { slot = i; break; }
            }
            if (slot < 0)
            {
                // Table full (shouldn't happen): fall back to inline serial execution.
                ((delegate* unmanaged[Cdecl]<IntPtr, void>)task)(taskContext);
                return IntPtr.Zero;
            }

            s_handles[slot] = new TaskJob { Task = task, Context = taskContext }.Schedule();
            s_used[slot] = true;
            // Flush the batch so the worker starts NOW, overlapping the step thread's inline
            // worker 0. Without this Unity defers the job until the first Complete() — which
            // happens after worker 0 has already done everything — and the parallel solve
            // silently degrades to single-threaded.
            JobHandle.ScheduleBatchedJobs();
            return (IntPtr)(slot + 1); // token: +1 keeps 0 reserved for the null/inline case
        }

        [MonoPInvokeCallback(typeof(FinishDelegate))]
        private static void Finish(IntPtr userTask, IntPtr userContext)
        {
            int slot = (int)userTask - 1;
            if (slot < 0 || slot >= MaxOutstanding || !s_used[slot]) return;
            s_handles[slot].Complete();
            s_used[slot] = false;
        }
    }
}
