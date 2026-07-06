using System;
using System.Runtime.InteropServices;

namespace PhysicsBox3D
{
    /// <summary>
    /// P/Invoke surface over the Box3D + Unity shim native plugin (box3d.dylib).
    /// The shim exposes a blittable C ABI so no Box3D internal structs cross the
    /// managed boundary, and the batched transform read-back happens in native code.
    /// </summary>
    public static class Box3DNative
    {
        private const string Lib = "box3d";

        /// <summary>Body type matches b3BodyType.</summary>
        public enum BodyType
        {
            Static = 0,
            Kinematic = 1,
            Dynamic = 2,
        }

        /// <summary>
        /// Blittable pose record. Layout must match b3uPose in box3d_unity.c
        /// (7 contiguous floats: position xyz + rotation quaternion xyzw).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Pose
        {
            public float px, py, pz;
            public float qx, qy, qz, qw;
        }

        /// <summary>workerCount &gt; 1 enables Box3D's internal multithreaded scheduler.</summary>
        [DllImport(Lib)]
        public static extern uint b3u_CreateWorld(float gx, float gy, float gz, uint workerCount);

        [DllImport(Lib)]
        public static extern void b3u_DestroyWorld(uint world);

        [DllImport(Lib)]
        public static extern void b3u_Step(uint world, float dt, int subStepCount);

        [DllImport(Lib)]
        public static extern ulong b3u_CreateBox(
            uint world, int type,
            float px, float py, float pz,
            float qx, float qy, float qz, float qw,
            float hx, float hy, float hz,
            float density, float friction, float restitution, int slot);

        [DllImport(Lib)]
        public static extern ulong b3u_CreateSphere(
            uint world, int type,
            float px, float py, float pz,
            float qx, float qy, float qz, float qw,
            float radius,
            float density, float friction, float restitution, int slot);

        /// <summary>
        /// Writes the pose of every body that moved this step into outPoses[slot].
        /// Returns the number of bodies that moved. outPoses is a pointer to a
        /// contiguous NativeArray&lt;Pose&gt; buffer of length capacity.
        /// </summary>
        [DllImport(Lib)]
        public static extern int b3u_WritePoses(uint world, IntPtr outPoses, int capacity);

        /// <summary>
        /// Matches b3WorldId. The shim packs it into a uint via b3StoreWorldId
        /// ((index1 &lt;&lt; 16) | generation); unpack to call b3 APIs directly.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WorldId
        {
            public ushort index1;
            public ushort generation;

            public static WorldId FromPacked(uint packed) => new WorldId
            {
                index1 = (ushort)(packed >> 16),
                generation = (ushort)packed,
            };
        }

        /// <summary>Box3D core API (exported alongside the shim). Enables/disables sleeping world-wide.</summary>
        [DllImport(Lib)]
        public static extern void b3World_EnableSleeping(WorldId worldId, [MarshalAs(UnmanagedType.I1)] bool flag);
    }
}
