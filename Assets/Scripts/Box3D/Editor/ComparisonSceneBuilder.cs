using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicsBox3D.EditorTools
{
    /// <summary>
    /// Builds the two comparison scenes:
    ///   Benchmark.unity           — timed PhysX vs Box3D piles (PhysicsComparison drives both).
    ///   ComponentComparison.unity — identical towers, left PhysX components / right Box3DBody
    ///                               components, a heavy ball dropped on each.
    /// </summary>
    public static class ComparisonSceneBuilder
    {
        // Half of this is each pile's x offset; grounds are 12 wide so they must not overlap.
        private const float Separation = 8f;

        [MenuItem("Tools/Box3D/Build Comparison Scenes")]
        public static void BuildAll()
        {
            BuildBenchmarkScene();
            BuildComponentScene();
            Debug.Log("[Box3D] Built Assets/Scenes/Benchmark.unity and ComponentComparison.unity");
        }

        private static void AddLight()
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1.1f;
        }

        private static void BuildBenchmarkScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddLight();
            new GameObject("Physics Comparison").AddComponent<PhysicsComparison>();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Benchmark.unity");
        }

        private static Material MakeMaterial(Color color, string name)
        {
            string path = $"Assets/Settings/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static void BuildComponentScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddLight();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.transform.position = new Vector3(0f, 10f, -26f);
            cam.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);

            var simGo = new GameObject("Box3D Simulation");
            var sim = simGo.AddComponent<Box3DSimulation>();
            sim.capacity = 1024;
            simGo.AddComponent<Box3DStatsHUD>();

            var blue = MakeMaterial(new Color(0.35f, 0.6f, 1f), "PhysXBlue");
            var orange = MakeMaterial(new Color(1f, 0.6f, 0.3f), "Box3DOrange");
            var gray = MakeMaterial(new Color(0.45f, 0.45f, 0.5f), "GroundGray");

            // PhysX surface material matching Box3DBody defaults (friction 0.5,
            // restitution 0.05, max restitution mixing) so both sides bounce alike.
            const string physMatPath = "Assets/Settings/BenchSurface.asset";
            var physMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physMatPath);
            if (physMat == null)
            {
                physMat = new PhysicsMaterial("BenchSurface")
                {
                    bounciness = 0.05f,
                    dynamicFriction = 0.5f,
                    staticFriction = 0.5f,
                    bounceCombine = PhysicsMaterialCombine.Maximum,
                    frictionCombine = PhysicsMaterialCombine.Average,
                };
                AssetDatabase.CreateAsset(physMat, physMatPath);
            }

            // Grounds: PhysX side keeps its BoxCollider; Box3D side swaps it for a static Box3DBody.
            var pg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pg.name = "PhysX Ground";
            pg.transform.position = new Vector3(-Separation, -0.5f, 0f);
            pg.transform.localScale = new Vector3(12f, 1f, 12f);
            pg.GetComponent<Renderer>().sharedMaterial = gray;
            pg.GetComponent<Collider>().sharedMaterial = physMat;

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "Box3D Ground";
            Object.DestroyImmediate(bg.GetComponent<Collider>());
            bg.transform.position = new Vector3(Separation, -0.5f, 0f);
            bg.transform.localScale = new Vector3(12f, 1f, 12f);
            bg.GetComponent<Renderer>().sharedMaterial = gray;
            var groundBody = bg.AddComponent<Box3DBody>();
            groundBody.motion = Box3DBody.MotionKind.Static;

            // Identical 4x4 grids of isolated 3-high columns. Bodies fall straight down,
            // stack, and settle in place, so both engines look near-identical in motion —
            // this scene demonstrates behavior parity, not solver chaos.
            var pxRoot = new GameObject("PhysX Columns").transform;
            var b3Root = new GameObject("Box3D Columns").transform;
            for (int cx = 0; cx < 4; cx++)
            for (int cz = 0; cz < 4; cz++)
            for (int level = 0; level < 3; level++)
            {
                var local = new Vector3((cx - 1.5f) * 1.0f, 15f + level * 0.7f, (cz - 1.5f) * 1.0f);

                var px = GameObject.CreatePrimitive(PrimitiveType.Cube);
                px.name = $"px_{cx}_{cz}_{level}";
                px.transform.SetParent(pxRoot);
                px.transform.position = new Vector3(-Separation, 0f, 0f) + local;
                px.transform.localScale = Vector3.one * 0.5f;
                px.GetComponent<Renderer>().sharedMaterial = blue;
                px.GetComponent<Collider>().sharedMaterial = physMat;
                var rb = px.AddComponent<Rigidbody>();
                rb.mass = 0.125f; // density 1 * 0.5^3, matching Box3DBody's default density
                rb.maxDepenetrationVelocity = 3f; // match Box3D's contactSpeed overlap-recovery cap

                var b3 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b3.name = $"b3_{cx}_{cz}_{level}";
                Object.DestroyImmediate(b3.GetComponent<Collider>());
                b3.transform.SetParent(b3Root);
                b3.transform.position = new Vector3(Separation, 0f, 0f) + local;
                b3.transform.localScale = Vector3.one * 0.5f;
                b3.GetComponent<Renderer>().sharedMaterial = orange;
                b3.AddComponent<Box3DBody>();
            }

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/ComponentComparison.unity");
        }
    }
}
