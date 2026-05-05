using UnityEngine;
using UnityEngine.InputSystem;

namespace EyeLean.Replay.UI
{
    /// <summary>
    /// In-scene IMGUI HUD for the Replay system. Surfaces per-frame eye/head
    /// telemetry (frame index, head pose, recorded eye origins + deviation
    /// from head pos, gaze directions, vergence, convergence angle, blink
    /// state) plus aggregate session diagnostics (valid-sample %, blink /
    /// tracking-loss counts, coord origin, profile, mean/max eye-origin
    /// Y-deviation). Toggle with H (default) or via the inspector.
    /// </summary>
    public class ReplayDebugOverlay : MonoBehaviour
    {
        [Header("References (auto-found if null)")]
        [SerializeField] private ReplayController replayController;

        [Header("Display")]
        [Tooltip("Show the HUD on Start.")]
        [SerializeField] private bool startVisible = true;
        [Tooltip("Key that toggles the HUD on/off at runtime.")]
        [SerializeField] private Key toggleKey = Key.H;
        [Tooltip("Where to anchor the HUD on screen.")]
        [SerializeField] private OverlayCorner corner = OverlayCorner.TopLeft;
        [Tooltip("Font size for HUD lines. Increase for HMD readability.")]
        [Range(10, 24)]
        [SerializeField] private int fontSize = 12;
        [Tooltip("HUD width in pixels. The vertical layout grows downward.")]
        [Range(280, 800)]
        [SerializeField] private int width = 420;

        [Header("Anomaly thresholds")]
        [Tooltip("If |eye_origin - head_pos| exceeds this, color the row red — heuristic for the coord-frame race that drops eye origins to tracking-space.")]
        [SerializeField] private float anomalyThresholdMeters = 0.30f;

        public enum OverlayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        private bool visible;
        private GUIStyle labelStyle;
        private GUIStyle warnStyle;
        private GUIStyle headerStyle;
        private bool stylesInit;

        // Latest-frame snapshot (filled via ReplayController.OnFrameDisplayed).
        private ReplayFrame currentFrame;

        // Aggregate session diagnostics — computed once at load time so the
        // per-frame path stays free.
        private bool aggregateComputed;
        private float aggMeanEyeOriginYDeviation;
        private float aggMaxEyeOriginYDeviation;
        private int aggAnomalousFrames;

        private void Awake()
        {
            if (replayController == null) replayController = GetComponent<ReplayController>();
            if (replayController == null) replayController = FindFirstObjectByType<ReplayController>();
        }

        private void OnEnable()
        {
            visible = startVisible;
            if (replayController != null)
            {
                replayController.OnFrameDisplayed += HandleFrameDisplayed;
                replayController.OnLoadComplete += HandleLoadComplete;
            }
        }

        private void OnDisable()
        {
            if (replayController != null)
            {
                replayController.OnFrameDisplayed -= HandleFrameDisplayed;
                replayController.OnLoadComplete -= HandleLoadComplete;
            }
        }

        private void Update()
        {
            // Keyboard.current is null in headless CI / EditMode, so guard.
            if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            {
                visible = !visible;
            }
        }

        private void HandleFrameDisplayed(ReplayFrame frame)
        {
            currentFrame = frame;
        }

        private void HandleLoadComplete(ReplaySession session)
        {
            ComputeAggregateDiagnostics(session);
        }

        private void ComputeAggregateDiagnostics(ReplaySession session)
        {
            aggregateComputed = false;
            aggMeanEyeOriginYDeviation = 0f;
            aggMaxEyeOriginYDeviation = 0f;
            aggAnomalousFrames = 0;
            if (session == null || session.frames == null || session.frames.Count == 0) return;

            float sumDev = 0f;
            float maxDev = 0f;
            int anomalous = 0;
            int n = session.frames.Count;
            for (int i = 0; i < n; i++)
            {
                var f = session.frames[i];
                // LEFT eye's Y-deviation as a proxy. Coord-frame races
                // affect both eyes symmetrically, so one is sufficient.
                float dev = Mathf.Abs(f.leftEyeOrigin.y - f.headPosition.y);
                sumDev += dev;
                if (dev > maxDev) maxDev = dev;
                if (dev > anomalyThresholdMeters) anomalous++;
            }
            aggMeanEyeOriginYDeviation = sumDev / n;
            aggMaxEyeOriginYDeviation = maxDev;
            aggAnomalousFrames = anomalous;
            aggregateComputed = true;
        }

        private void EnsureStyles()
        {
            if (stylesInit) return;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, normal = { textColor = Color.white }, wordWrap = false };
            warnStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, normal = { textColor = new Color(1f, 0.45f, 0.45f) }, wordWrap = false };
            headerStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize + 1, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.65f, 0.85f, 1f) } };
            stylesInit = true;
        }

        private void OnGUI()
        {
            if (!visible) return;
            EnsureStyles();

            const int pad = 8;
            int linePixels = fontSize + 4;
            int height = Mathf.Min(Screen.height - 2 * pad, 28 * linePixels);

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

            GUILayout.Label($"Replay Debug Overlay  (toggle: {toggleKey})", headerStyle);

            DrawSessionBlock();
            GUILayout.Space(4);
            DrawFrameBlock();
            GUILayout.Space(4);
            DrawAnomalyBlock();

            GUILayout.EndArea();
        }

        private void DrawSessionBlock()
        {
            GUILayout.Label("— Session —", headerStyle);
            if (replayController == null || replayController.Session == null)
            {
                GUILayout.Label("No session loaded", labelStyle);
                return;
            }
            var s = replayController.Session;
            GUILayout.Label($"State: {replayController.State}    Frame: {replayController.CurrentFrameIndex}/{replayController.TotalFrames}", labelStyle);
            GUILayout.Label($"Time:  {replayController.CurrentTime:F2}s / {replayController.TotalDuration:F2}s   ({replayController.Progress * 100f:F1}%)", labelStyle);
            GUILayout.Label($"Phase: '{replayController.CurrentPhase}'", labelStyle);
            GUILayout.Label($"FPS:   {s.averageFrameRate:F1}    Valid: {s.validSamplePercentage:F1}%    Blinks: {s.blinkCount}    Loss: {s.trackingLossCount}", labelStyle);
            GUILayout.Label($"Coord origin: {s.coordinateOrigin}  set={s.coordinateOriginSet}", labelStyle);
            string profile = string.IsNullOrEmpty(s.activeProfileName) ? "(none)" : s.activeProfileName;
            GUILayout.Label($"Profile: {profile}", labelStyle);
        }

        private void DrawFrameBlock()
        {
            GUILayout.Label("— Frame —", headerStyle);
            if (currentFrame == null)
            {
                GUILayout.Label("(awaiting first frame)", labelStyle);
                return;
            }
            var f = currentFrame;

            GUILayout.Label($"#{f.frameNumber}  t={f.timestamp:F3}s  Δ={f.frameDuration * 1000f:F1}ms  task='{f.subTask}'", labelStyle);
            GUILayout.Label($"Head pos:  {Fmt(f.headPosition)}", labelStyle);
            GUILayout.Label($"Head fwd:  {Fmt(f.headForward)}", labelStyle);

            // Recorded eye origins + deviation from head position.
            float devL = Vector3.Distance(f.leftEyeOrigin, f.headPosition);
            float devR = Vector3.Distance(f.rightEyeOrigin, f.headPosition);
            GUIStyle leftStyle = devL > anomalyThresholdMeters ? warnStyle : labelStyle;
            GUIStyle rightStyle = devR > anomalyThresholdMeters ? warnStyle : labelStyle;
            GUILayout.Label($"Left  origin (rec):  {Fmt(f.leftEyeOrigin)}   Δhead={devL:F2}m", leftStyle);
            GUILayout.Label($"Right origin (rec):  {Fmt(f.rightEyeOrigin)}   Δhead={devR:F2}m", rightStyle);
            GUILayout.Label($"Left  dir:  {Fmt(f.leftEyeDirection)}   open={f.leftEyeOpenness:F2}  pupil={f.leftPupilDiameter:F2}mm", labelStyle);
            GUILayout.Label($"Right dir:  {Fmt(f.rightEyeDirection)}   open={f.rightEyeOpenness:F2}  pupil={f.rightPupilDiameter:F2}mm", labelStyle);

            // Convergence angle between recorded gaze directions (independent
            // of origin issues — sanity check on the directions themselves).
            float convAngleDeg = Vector3.Angle(f.leftEyeDirection.normalized, f.rightEyeDirection.normalized);
            GUILayout.Label($"Conv angle: {convAngleDeg:F2}°    IPD (rec): {Vector3.Distance(f.leftEyeOrigin, f.rightEyeOrigin) * 1000f:F1}mm", labelStyle);

            string vergeLabel = f.hasValidVergence ? $"{Fmt(f.vergencePoint)} q={f.vergenceQuality:F2}" : "(invalid)";
            GUILayout.Label($"Vergence: {vergeLabel}", labelStyle);
            GUILayout.Label($"Tracking: {(f.isTrackingValid ? "VALID" : "LOST")}    Blinking: {f.isBlinking}", labelStyle);
        }

        private void DrawAnomalyBlock()
        {
            GUILayout.Label("— Coord-frame race scan —", headerStyle);
            if (!aggregateComputed)
            {
                GUILayout.Label("(load a session to compute)", labelStyle);
                return;
            }
            int n = replayController?.Session?.frames?.Count ?? 0;
            if (n == 0)
            {
                GUILayout.Label("(no frames)", labelStyle);
                return;
            }
            float pct = aggAnomalousFrames * 100f / n;
            GUIStyle line2 = pct > 1f ? warnStyle : labelStyle;
            GUILayout.Label($"|eyeY - headY|  mean={aggMeanEyeOriginYDeviation * 1000f:F1}mm  max={aggMaxEyeOriginYDeviation * 1000f:F0}mm", labelStyle);
            GUILayout.Label($"Frames with deviation > {anomalyThresholdMeters:F2}m:  {aggAnomalousFrames} / {n}  ({pct:F2}%)", line2);
            if (replayController != null)
            {
                GUILayout.Label($"Ray-origin mode: {(replayController.useHeadAnchoredRayOrigin ? "head-anchored (compensating)" : "raw (debug)")}", labelStyle);
                GUILayout.Label($"Camera mode: {(replayController.firstPersonCamera ? "first-person (driving Camera.main)" : "third-person (camera fixed)")}", labelStyle);
            }
        }

        private static string Fmt(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    }
}
