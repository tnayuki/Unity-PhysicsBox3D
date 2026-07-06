using UnityEngine;

namespace PhysicsBox3D
{
    /// <summary>
    /// Authoring component for a Box3D rigid body — the Box3D counterpart of
    /// Rigidbody + BoxCollider/SphereCollider. Shape size is derived from the
    /// transform's lossy scale (a default Unity Cube/Sphere maps 1:1).
    ///
    /// Notes for this comparison prototype:
    /// - Bodies register on Start and live for the lifetime of the world;
    ///   per-body destruction is not exposed by the native shim yet.
    /// - Dynamic bodies drive the Transform via the simulation's batched
    ///   parallel write-back; do not move their Transform manually.
    /// </summary>
    public sealed class Box3DBody : MonoBehaviour
    {
        public enum ShapeKind
        {
            Box,
            Sphere,
        }

        public enum MotionKind
        {
            Static,
            Dynamic,
        }

        public ShapeKind shape = ShapeKind.Box;
        public MotionKind motion = MotionKind.Dynamic;

        [Tooltip("kg/m^3. Water = 1000. Default Unity Rigidbody mass(1) on a unit cube = density 1.")]
        public float density = 1f;
        [Range(0f, 1f)] public float friction = 0.5f;
        [Range(0f, 1f)] public float restitution = 0.05f;

        private bool _registered;

        private void Start()
        {
            var sim = Box3DSimulation.Instance;
            if (sim == null || sim.World == null)
            {
                Debug.LogError("Box3DBody needs a Box3DSimulation in the scene.", this);
                return;
            }
            Register(sim.World);
        }

        private void Register(Box3DWorld world)
        {
            if (_registered) return;
            _registered = true;

            Vector3 s = transform.lossyScale;
            switch (shape)
            {
                case ShapeKind.Box:
                    Vector3 half = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) * 0.5f;
                    if (motion == MotionKind.Dynamic)
                        world.CreateDynamicBox(transform, half, density, friction, restitution);
                    else
                        world.CreateStaticBox(transform.position, transform.rotation, half, friction, restitution);
                    break;

                case ShapeKind.Sphere:
                    float radius = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) * 0.5f;
                    if (motion == MotionKind.Dynamic)
                        world.CreateDynamicSphere(transform, radius, density, friction, restitution);
                    else
                        world.CreateStaticSphere(transform.position, radius, friction, restitution);
                    break;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = motion == MotionKind.Dynamic
                ? new Color(1f, 0.6f, 0.3f)
                : new Color(0.5f, 0.8f, 0.5f);
            Vector3 s = transform.lossyScale;
            if (shape == ShapeKind.Box)
            {
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, s);
            }
            else
            {
                float radius = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) * 0.5f;
                Gizmos.DrawWireSphere(transform.position, radius);
            }
        }
    }
}
