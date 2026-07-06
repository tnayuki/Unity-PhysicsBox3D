using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PhysicsBox3D
{
    /// <summary>
    /// Side-by-side comparison of Unity's built-in physics (PhysX) and the Box3D binding.
    /// Both engines are driven manually every FixedUpdate under Physics.simulationMode = Script,
    /// and each step is timed with the same Stopwatch so the numbers are comparable.
    ///
    /// Left pile  = PhysX  (Rigidbody + BoxCollider, stepped via Physics.Simulate)
    /// Right pile = Box3D  (native bodies, stepped via Box3DWorld.StepAndSync)
    /// </summary>
    public sealed class PhysicsComparison : MonoBehaviour
    {
        [Header("Scene")]
        public int countPerEngine = 512;
        public float boxSize = 0.5f;          // full edge length
        public int columnHeight = 4;          // boxes per column; short columns land the same in both engines
        public float spacing = 0.85f;         // column pitch; wide enough that columns never touch laterally
        public float dropHeight = 20f;        // height of the lowest box; higher = longer active phase
        public float pileSeparation = 32f;    // x distance between the two piles (grounds must not overlap)
        public int subSteps = 4;

        [Header("Auto benchmark")]
        [Tooltip("Body counts to run in order; each runs one 500-step sample window, then advances.")]
        public int[] autoSequence = { 512, 1024, 2048, 4096 };
        public float pauseBetween = 2f;

        [Header("Engines")]
        public bool physxEnabled = true;
        public bool box3dEnabled = true;

        [Tooltip("Keep every body awake so the whole sample window measures full solver " +
                 "load (pure solver benchmark). Off by default: sleeping is how real " +
                 "games run, and settling-to-sleep quality is part of the comparison.")]
        public bool disableSleeping = false;

        [Header("Material")]
        public float density = 1f;
        public float friction = 0.5f;
        public float restitution = 0.05f;

        private Box3DWorld _world;
        private Transform _physxRoot;
        private Transform _box3dRoot;
        private PhysicsMaterial _physxMaterial;

        // Smoothed timings (ms).
        private double _physxMs;
        private double _box3dMs;
        private readonly Stopwatch _sw = new Stopwatch();
        private float _fps;

        // Fixed-window benchmark: average the first SampleSteps raw step times after
        // each spawn, so both engines are measured over the same activity phase
        // (settling pile) instead of a drifting smoothed value.
        private const int SampleSteps = 500;
        private int _samplesLeft;
        private double _physxSum;
        private double _box3dSum;
        private string _report = "";

        // Auto-sequence state.
        private int _seqIndex;
        private bool _autoRun = true;
        private float _pauseUntil = -1f;
        private readonly System.Collections.Generic.List<string> _results =
            new System.Collections.Generic.List<string>();

        private void Awake()
        {
            // Take over stepping for both engines so timings are apples-to-apples.
            Physics.simulationMode = SimulationMode.Script;
            if (autoSequence != null && autoSequence.Length > 0)
                countPerEngine = autoSequence[0];
            _world = new Box3DWorld(new Vector3(0f, -9.81f, 0f), Mathf.NextPowerOfTwo(countPerEngine) + 8);
            BuildScene();
        }

        private void OnDestroy()
        {
            _world?.Dispose();
            _world = null;
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        private void BuildScene()
        {
            SetupCamera();

            // Match Box3D's surface material on the PhysX side. Without this, PhysX
            // colliders use the default material (bounciness 0, friction 0.6) and the
            // two piles bounce visibly differently on hard landings.
            // Box3D mixes restitution with max(a, b) by default, so use Maximum here too.
            _physxMaterial = new PhysicsMaterial("Bench")
            {
                bounciness = restitution,
                dynamicFriction = friction,
                staticFriction = friction,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Average,
            };

            // --- Ground (PhysX side: real collider; Box3D side: visual + native static) ---
            float half = boxSize * 0.5f;

            var physxGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            physxGround.name = "PhysX Ground";
            physxGround.transform.position = new Vector3(-pileSeparation * 0.5f, -0.5f, 0f);
            physxGround.transform.localScale = new Vector3(30f, 1f, 30f);
            physxGround.GetComponent<Collider>().sharedMaterial = _physxMaterial;
            Tint(physxGround, new Color(0.25f, 0.35f, 0.55f));

            var box3dGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box3dGround.name = "Box3D Ground (visual)";
            Destroy(box3dGround.GetComponent<Collider>()); // must NOT be a PhysX collider
            box3dGround.transform.position = new Vector3(pileSeparation * 0.5f, -0.5f, 0f);
            box3dGround.transform.localScale = new Vector3(30f, 1f, 30f);
            Tint(box3dGround, new Color(0.55f, 0.35f, 0.25f));
            _world.CreateStaticBox(box3dGround.transform.position, Quaternion.identity,
                new Vector3(15f, 0.5f, 15f), friction, restitution);

            // --- Roots for the falling cubes ---
            _physxRoot = new GameObject("PhysX Bodies").transform;
            _box3dRoot = new GameObject("Box3D Bodies").transform;

            Spawn();
        }

        /// <summary>Respawn both piles with a new body count (used by editor tooling).</summary>
        public void Respawn(int count)
        {
            countPerEngine = Mathf.Clamp(count, 8, 8192);
            Spawn();
        }

        private void Spawn()
        {
            ClearChildren(_physxRoot);
            ClearChildren(_box3dRoot);
            _samplesLeft = SampleSteps;
            _physxSum = 0;
            _box3dSum = 0;
            _physxMs = 0;
            _box3dMs = 0;
            // Rebuild the Box3D world to reset body slots. Worker threads scale with the
            // body count: small worlds lose more to fork/join overhead than they gain
            // (512 bodies measured fastest around 4 workers, 4096 with every core).
            int workers = Mathf.Clamp(countPerEngine / 128, 1, Box3DWorld.AutoWorkerCount);
            _world.Dispose();
            _world = new Box3DWorld(new Vector3(0f, -9.81f, 0f), Mathf.NextPowerOfTwo(countPerEngine) + 8, workers);
            if (disableSleeping) _world.EnableSleeping(false);
            _world.CreateStaticBox(new Vector3(pileSeparation * 0.5f, -0.5f, 0f), Quaternion.identity,
                new Vector3(15f, 0.5f, 15f), friction, restitution);

            float half = boxSize * 0.5f;
            var scale = Vector3.one * boxSize;

            for (int i = 0; i < countPerEngine; i++)
            {
                Vector3 offset = GridOffset(i);

                if (physxEnabled)
                {
                    // PhysX cube: standard Rigidbody + BoxCollider.
                    var px = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    px.transform.SetParent(_physxRoot);
                    px.transform.position = new Vector3(-pileSeparation * 0.5f, 0f, 0f) + offset;
                    px.transform.localScale = scale;
                    px.GetComponent<Collider>().sharedMaterial = _physxMaterial;
                    Tint(px, new Color(0.35f, 0.6f, 1f));
                    var rb = px.AddComponent<Rigidbody>();
                    rb.mass = density * boxSize * boxSize * boxSize;
                    // High-speed landings drive boxes deep into each other in one discrete
                    // step; PhysX then ejects overlap at up to 10 m/s by default, which reads
                    // as violent bouncing. Box3D caps overlap recovery at 3 m/s (contactSpeed),
                    // so cap PhysX the same for comparable landings.
                    rb.maxDepenetrationVelocity = 3f;
                    if (disableSleeping) rb.sleepThreshold = 0f;
                }

                if (box3dEnabled)
                {
                    // Box3D cube: rendered GameObject with NO PhysX collider; native body drives it.
                    var b3 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(b3.GetComponent<Collider>());
                    b3.transform.SetParent(_box3dRoot);
                    b3.transform.position = new Vector3(pileSeparation * 0.5f, 0f, 0f) + offset;
                    b3.transform.localScale = scale;
                    Tint(b3, new Color(1f, 0.6f, 0.3f));
                    _world.CreateDynamicBox(b3.transform, Vector3.one * half, density, friction, restitution);
                }
            }
        }

        // Isolated short columns on a square grid: bodies drop straight down, land on
        // each other, and settle in place. No jitter and no lateral contact, so both
        // engines produce visually near-identical motion while still solving
        // (columnHeight - 1) stacked contacts per column.
        private Vector3 GridOffset(int i)
        {
            int column = i / columnHeight;
            int level = i % columnHeight;
            int cols = Mathf.CeilToInt(Mathf.Sqrt((float)countPerEngine / columnHeight));
            int cx = column % cols;
            int cz = column / cols;
            return new Vector3(
                (cx - (cols - 1) * 0.5f) * spacing,
                dropHeight + level * (boxSize + 0.02f),
                (cz - (cols - 1) * 0.5f) * spacing);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Time PhysX.
            double physxRaw = 0;
            if (physxEnabled)
            {
                _sw.Restart();
                Physics.Simulate(dt);
                _sw.Stop();
                physxRaw = _sw.Elapsed.TotalMilliseconds;
                _physxMs = Smooth(_physxMs, physxRaw);
            }

            // Time Box3D (step + batched parallel transform write-back).
            double box3dRaw = 0;
            if (box3dEnabled)
            {
                _sw.Restart();
                _world.StepAndSync(dt, subSteps);
                _sw.Stop();
                box3dRaw = _sw.Elapsed.TotalMilliseconds;
                _box3dMs = Smooth(_box3dMs, box3dRaw);
            }

            if (_samplesLeft > 0 && (physxEnabled || box3dEnabled))
            {
                _physxSum += physxRaw;
                _box3dSum += box3dRaw;
                if (--_samplesLeft == 0)
                {
                    double pxAvg = _physxSum / SampleSteps;
                    double b3Avg = _box3dSum / SampleSteps;
                    var parts = new System.Collections.Generic.List<string>();
                    if (physxEnabled) parts.Add($"PhysX {pxAvg:0.000} ms");
                    if (box3dEnabled) parts.Add($"Box3D {b3Avg:0.000} ms");
                    if (physxEnabled && box3dEnabled) parts.Add($"{pxAvg / b3Avg:0.00}x");
                    _report = $"{countPerEngine} bodies:  " + string.Join("  |  ", parts);
                    Debug.Log($"[Bench] avg over {SampleSteps} steps @ {_report}");
                    _results.Add(_report);
                    if (_autoRun) _pauseUntil = Time.time + pauseBetween;
                }
            }
        }

        private void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f), 0.1f);

            // Advance the auto sequence once the pause after a completed sample elapses.
            if (_autoRun && _pauseUntil > 0f && Time.time >= _pauseUntil)
            {
                _pauseUntil = -1f;
                _seqIndex++;
                if (_seqIndex < autoSequence.Length)
                {
                    Respawn(autoSequence[_seqIndex]);
                }
                else
                {
                    _autoRun = false;
                    Debug.Log("[Bench] auto sequence finished:\n  " + string.Join("\n  ", _results));
                }
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame) RestartSequence();
            if (kb.upArrowKey.wasPressedThisFrame) ManualRespawn(countPerEngine * 2);
            if (kb.downArrowKey.wasPressedThisFrame) ManualRespawn(countPerEngine / 2);
        }

        private void RestartSequence()
        {
            _results.Clear();
            _seqIndex = 0;
            _autoRun = autoSequence != null && autoSequence.Length > 0;
            _pauseUntil = -1f;
            Respawn(_autoRun ? autoSequence[0] : countPerEngine);
        }

        private void ManualRespawn(int count)
        {
            _autoRun = false;
            _pauseUntil = -1f;
            Respawn(count);
        }

        private static double Smooth(double prev, double sample) =>
            prev <= 0 ? sample : prev * 0.9 + sample * 0.1;

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            GUILayout.BeginArea(new Rect(12, 12, 560, 360), GUI.skin.box);
            GUILayout.Label("<b>PhysX vs Box3D</b>  (Physics.simulationMode = Script)", style);
            GUILayout.Space(4);
            GUILayout.Label($"Bodies per engine: <b>{countPerEngine}</b>   FPS: <b>{_fps:0}</b>" +
                            (disableSleeping ? "   (no sleep)" : ""), style);
            GUILayout.Space(6);
            GUILayout.Label(physxEnabled
                ? $"<color=#5B9BFF>PhysX  step:</color> <b>{_physxMs:0.000} ms</b>"
                : "<color=#5B9BFF>PhysX:</color> disabled", style);
            GUILayout.Label(box3dEnabled
                ? $"<color=#FF9B4B>Box3D  step+sync:</color> <b>{_box3dMs:0.000} ms</b>  " +
                  $"(moved {_world.LastMovedCount})"
                : "<color=#FF9B4B>Box3D:</color> disabled", style);
            GUILayout.Space(6);
            if (_samplesLeft > 0)
            {
                string stage = _autoRun ? $"auto {_seqIndex + 1}/{autoSequence.Length}" : "manual";
                GUILayout.Label($"benchmarking ({stage})… {SampleSteps - _samplesLeft}/{SampleSteps}", style);
            }
            else if (_autoRun)
            {
                GUILayout.Label("next scale in a moment…", style);
            }
            foreach (var line in _results)
                GUILayout.Label(line, style);
            if (!_autoRun && _results.Count == (autoSequence?.Length ?? 0) && _results.Count > 0)
                GUILayout.Label("<b>sequence complete</b>", style);
            GUILayout.Space(8);
            GUILayout.Label("[R] restart sequence   [Up/Down] manual x2 / /2 bodies", style);
            GUILayout.EndArea();
        }

        // ----- helpers -----

        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(0f, 24f, -60f);
            cam.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
        }

        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = c;
        }

        private static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }
    }
}
