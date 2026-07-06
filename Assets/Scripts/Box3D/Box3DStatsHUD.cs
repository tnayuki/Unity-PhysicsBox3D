using UnityEngine;

namespace PhysicsBox3D
{
    /// <summary>Small overlay showing Box3D step cost next to the frame rate.</summary>
    public sealed class Box3DStatsHUD : MonoBehaviour
    {
        private float _fps;

        private void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f), 0.1f);
        }

        private void OnGUI()
        {
            var sim = Box3DSimulation.Instance;
            if (sim == null) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            GUILayout.BeginArea(new Rect(12, 12, 420, 110), GUI.skin.box);
            GUILayout.Label($"FPS: <b>{_fps:0}</b>", style);
            GUILayout.Label($"<color=#FF9B4B>Box3D step+sync:</color> <b>{sim.LastStepMs:0.000} ms</b>  " +
                            $"(bodies {sim.World?.BodyCount ?? 0}, moved {sim.World?.LastMovedCount ?? 0})", style);
            GUILayout.EndArea();
        }
    }
}
