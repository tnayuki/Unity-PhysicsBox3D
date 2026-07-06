using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Jobs;

namespace PhysicsBox3D
{
    /// <summary>
    /// Owns a Box3D world and drives the data-oriented transform sync:
    ///   Step -> WritePoses (contiguous native buffer of only-what-moved) ->
    ///   IJobParallelForTransform writes poses back to Unity Transforms in parallel.
    /// This mirrors Unity Physics Core 2D's bodyUpdateEvents + IJobParallelForTransform design.
    /// </summary>
    public sealed class Box3DWorld : IDisposable
    {
        private uint _worldId;
        private readonly int _capacity;
        private NativeArray<Box3DNative.Pose> _poses;
        private TransformAccessArray _transforms;
        private int _count;
        private bool _disposed;

        /// <summary>Bodies that reported movement on the last step (for the HUD).</summary>
        public int LastMovedCount { get; private set; }

        /// <summary>Number of dynamic bodies whose transform is synced.</summary>
        public int BodyCount => _count;

        /// <summary>
        /// Box3D's docs suggest performance cores only, but a measured sweep on an
        /// Apple-silicon 4P+6E machine showed all 10 cores beating any subset
        /// (4096 bodies: 5 workers 4.22 ms vs 10 workers 3.70 ms), so use every core.
        /// </summary>
        public static int AutoWorkerCount => Mathf.Clamp(SystemInfo.processorCount, 1, 16);

        public Box3DWorld(Vector3 gravity, int capacity, int workerCount = 0)
        {
            if (workerCount <= 0) workerCount = AutoWorkerCount;
            _capacity = capacity;
            _worldId = Box3DNative.b3u_CreateWorld(gravity.x, gravity.y, gravity.z, (uint)workerCount);
            _poses = new NativeArray<Box3DNative.Pose>(capacity, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            _transforms = new TransformAccessArray(capacity);
        }

        /// <summary>
        /// Registers a dynamic body whose Unity Transform should be synced each step,
        /// returning its slot index (used as the native body's userData).
        /// </summary>
        private int AllocateSlot(Transform t)
        {
            if (_count >= _capacity)
                throw new InvalidOperationException($"Box3DWorld capacity {_capacity} exceeded.");
            int slot = _count;
            _transforms.Add(t);
            // Seed the pose buffer with the current transform so slot data is valid
            // even before the first move event arrives.
            _poses[slot] = new Box3DNative.Pose
            {
                px = t.position.x, py = t.position.y, pz = t.position.z,
                qx = t.rotation.x, qy = t.rotation.y, qz = t.rotation.z, qw = t.rotation.w,
            };
            _count++;
            return slot;
        }

        public void CreateDynamicBox(Transform t, Vector3 halfExtents, float density,
            float friction, float restitution)
        {
            int slot = AllocateSlot(t);
            var p = t.position;
            var q = t.rotation;
            Box3DNative.b3u_CreateBox(_worldId, (int)Box3DNative.BodyType.Dynamic,
                p.x, p.y, p.z, q.x, q.y, q.z, q.w,
                halfExtents.x, halfExtents.y, halfExtents.z,
                density, friction, restitution, slot);
        }

        public void CreateDynamicSphere(Transform t, float radius, float density,
            float friction, float restitution)
        {
            int slot = AllocateSlot(t);
            var p = t.position;
            var q = t.rotation;
            Box3DNative.b3u_CreateSphere(_worldId, (int)Box3DNative.BodyType.Dynamic,
                p.x, p.y, p.z, q.x, q.y, q.z, q.w,
                radius, density, friction, restitution, slot);
        }

        /// <summary>Static bodies never move, so they get slot -1 (skipped by WritePoses).</summary>
        public void CreateStaticBox(Vector3 center, Quaternion rotation, Vector3 halfExtents,
            float friction, float restitution)
        {
            Box3DNative.b3u_CreateBox(_worldId, (int)Box3DNative.BodyType.Static,
                center.x, center.y, center.z,
                rotation.x, rotation.y, rotation.z, rotation.w,
                halfExtents.x, halfExtents.y, halfExtents.z,
                1f, friction, restitution, -1);
        }

        /// <summary>Disable to keep every body active — used for fair full-load benchmarking.</summary>
        public void EnableSleeping(bool enabled)
        {
            Box3DNative.b3World_EnableSleeping(Box3DNative.WorldId.FromPacked(_worldId), enabled);
        }

        public void CreateStaticSphere(Vector3 center, float radius, float friction, float restitution)
        {
            Box3DNative.b3u_CreateSphere(_worldId, (int)Box3DNative.BodyType.Static,
                center.x, center.y, center.z, 0f, 0f, 0f, 1f,
                radius, 1f, friction, restitution, -1);
        }

        private struct WritePosesJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<Box3DNative.Pose> Poses;

            public void Execute(int index, TransformAccess transform)
            {
                var p = Poses[index];
                var pos = new Vector3(p.px, p.py, p.pz);
                var rot = new Quaternion(p.qx, p.qy, p.qz, p.qw);
                // Sleeping bodies keep their last pose; writing an identical value still
                // dirties the Transform (change events, renderer updates), so skip them.
                if ((transform.position - pos).sqrMagnitude < 1e-12f &&
                    Mathf.Abs(Quaternion.Dot(transform.rotation, rot)) > 1f - 1e-9f)
                    return;
                transform.position = pos;
                transform.rotation = rot;
            }
        }

        /// <summary>Advance the simulation and write moved bodies back to Transforms.</summary>
        public unsafe void StepAndSync(float dt, int subSteps)
        {
            Box3DNative.b3u_Step(_worldId, dt, subSteps);
            LastMovedCount = Box3DNative.b3u_WritePoses(
                _worldId, (IntPtr)_poses.GetUnsafePtr(), _poses.Length);

            if (_transforms.length > 0)
            {
                var job = new WritePosesJob { Poses = _poses };
                job.Schedule(_transforms).Complete();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_transforms.isCreated) _transforms.Dispose();
            if (_poses.IsCreated) _poses.Dispose();
            if (_worldId != 0) Box3DNative.b3u_DestroyWorld(_worldId);
            _worldId = 0;
        }
    }
}
