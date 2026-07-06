using System.Diagnostics;
using UnityEngine;

namespace PhysicsBox3D
{
    /// <summary>
    /// Scene-level driver for a Box3D world. Place exactly one in a scene;
    /// Box3DBody components register themselves against it on Start.
    /// Steps the world every FixedUpdate (same cadence as built-in physics,
    /// so behavior and cost are directly comparable side by side).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Box3DSimulation : MonoBehaviour
    {
        [Tooltip("Gravity for the Box3D world. Defaults to Physics.gravity for comparability.")]
        public Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        [Tooltip("Box3D solver sub-steps per FixedUpdate.")]
        public int subSteps = 4;

        [Tooltip("Maximum number of transform-synced (non-static) bodies.")]
        public int capacity = 4096;

        [Tooltip("Box3D worker threads. 0 = auto (physical performance cores).")]
        public int workerCount = 0;

        public static Box3DSimulation Instance { get; private set; }

        public Box3DWorld World { get; private set; }

        /// <summary>Smoothed cost of Step + batched transform write-back, in ms.</summary>
        public double LastStepMs { get; private set; }

        private readonly Stopwatch _sw = new Stopwatch();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                UnityEngine.Debug.LogWarning("Multiple Box3DSimulation instances; disabling this one.", this);
                enabled = false;
                return;
            }
            Instance = this;
            World = new Box3DWorld(gravity, capacity, workerCount);
        }

        private void FixedUpdate()
        {
            _sw.Restart();
            World.StepAndSync(Time.fixedDeltaTime, subSteps);
            _sw.Stop();
            double ms = _sw.Elapsed.TotalMilliseconds;
            LastStepMs = LastStepMs <= 0 ? ms : LastStepMs * 0.9 + ms * 0.1;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            World?.Dispose();
            World = null;
        }
    }
}
