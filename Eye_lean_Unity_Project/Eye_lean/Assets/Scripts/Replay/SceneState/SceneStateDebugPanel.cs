using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EyeLean.Replay.SceneState
{
    /// <summary>
    /// Diagnostic IMGUI HUD for the scene-state replay path. Independent of
    /// the (separate) ReplayDebugOverlay because that one focuses on eye/
    /// head data while this one cares about scene-state telemetry. Shows:
    /// sidecar path, recorded ids, matched / missing / extras (live but
    /// unrecorded), and per-frame applied / active counts.
    /// </summary>
    public class SceneStateDebugPanel : MonoBehaviour
    {
        [Header("References (auto-found if null)")]
        [SerializeField] private SceneStateReplayer replayer;

        [Header("Display")]
        [SerializeField] private bool startVisible = true;
#if ENABLE_INPUT_SYSTEM
        [SerializeField] private Key toggleKey = Key.J;
#endif
        [SerializeField] private OverlayCorner corner = OverlayCorner.TopRight;
        [Range(10, 24)] [SerializeField] private int fontSize = 12;
        [Range(280, 800)] [SerializeField] private int width = 380;

        public enum OverlayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        private bool visible;
        private GUIStyle labelStyle;
        private GUIStyle headerStyle;
        private bool stylesInit;

        private void Awake()
        {
            if (replayer == null) replayer = GetComponent<SceneStateReplayer>() ?? FindFirstObjectByType<SceneStateReplayer>();
        }

        private void OnEnable()
        {
            visible = startVisible;
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
                visible = !visible;
#endif
        }

        private void EnsureStyles()
        {
            if (stylesInit) return;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, normal = { textColor = Color.white } };
            headerStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize + 1, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.7f, 1f, 0.8f) } };
            stylesInit = true;
        }

        private void OnGUI()
        {
            if (!visible || replayer == null) return;
            EnsureStyles();

            const int pad = 8;
            int linePixels = fontSize + 4;
            int height = 10 * linePixels;
            float x, y;
            switch (corner)
            {
                case OverlayCorner.TopRight:    x = Screen.width - width - pad; y = pad; break;
                case OverlayCorner.BottomLeft:  x = pad; y = Screen.height - height - pad; break;
                case OverlayCorner.BottomRight: x = Screen.width - width - pad; y = Screen.height - height - pad; break;
                default:                        x = pad; y = pad; break;
            }
            GUI.Box(new Rect(x - 4, y - 4, width + 8, height + 8), GUIContent.none);
            GUILayout.BeginArea(new Rect(x, y, width, height));
            GUILayout.Label("Scene State Replay", headerStyle);
            GUILayout.Label($"Sidecar: {(string.IsNullOrEmpty(replayer.SidecarPath) ? "(none)" : System.IO.Path.GetFileName(replayer.SidecarPath))}", labelStyle);
            GUILayout.Label($"Loaded:  {replayer.TimelineLoaded}", labelStyle);
            GUILayout.Label($"Recorded objects: {replayer.RecordedObjectCount}", labelStyle);
            GUILayout.Label($"Matched in scene: {replayer.MatchedObjectCount}", labelStyle);
            GUILayout.Label($"Missing (warned once): {replayer.MissingObjectCount}", labelStyle);
            GUILayout.Label($"Last frame applied: {replayer.LastFrameAppliedCount} (active: {replayer.LastFrameActiveCount})", labelStyle);
            GUILayout.Label($"ReplayMode.IsActive: {ReplayMode.IsActive}", labelStyle);
            GUILayout.EndArea();
        }
    }
}
