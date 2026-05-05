using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EyeTracking.Metrics;

namespace EyeLean.Experiment
{
    /// <summary>
    /// Corner positions for UI pinning.
    /// </summary>
    public enum UICorner
    {
        Center,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Handles world-space UI for experiment instructions and progress display.
    /// Supports corner pinning to reduce gaze interference during experiments.
    /// </summary>
    public class ExperimentUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Override for the user's HMD camera. Auto-resolved from Camera.main when left empty.")]
        [SerializeField] private Transform cameraTransform;

        [Header("UI Panels")]
        [Tooltip("Hand-authored instruction panel. Leave empty to let the component build a canvas + panel from code (autoCreateUI).")]
        [SerializeField] private GameObject instructionPanel;
        [Tooltip("Hand-authored progress panel. Leave empty to auto-build alongside the instruction panel.")]
        [SerializeField] private GameObject progressPanel;

        [Header("Text Components")]
        [Tooltip("TMP text element bound to ShowInstruction. Auto-created when instructionPanel is auto-built.")]
        [SerializeField] private TextMeshProUGUI instructionText;
        [Tooltip("TMP text element bound to ShowProgress. Auto-created when progressPanel is auto-built.")]
        [SerializeField] private TextMeshProUGUI progressText;

        [Header("Settings")]
        [Tooltip("Distance the experiment panel sits from the user, in meters.")]
        [SerializeField] private float panelDistance = 1.8f;
        [Tooltip("Vertical offset of the panel above eye level, in meters. Negative = below.")]
        [SerializeField] private float panelHeight = 0.3f;
        [Tooltip("If true, the panel lerps with head motion (HUD-lock-ish). For VR experiments leave this off — a fixed-in-room panel is easier to read and doesn't compete with task gaze.")]
        [SerializeField] private bool followCamera = false;
        [Tooltip("Lerp speed used when followCamera is enabled. Higher = snappier panel; lower = smoother but laggier.")]
        [SerializeField] private float followSpeed = 2.0f;

        [Header("Corner Pinning")]
        [Tooltip("Pin UI to a corner instead of center (reduces gaze interference)")]
        [SerializeField] private bool pinToCorner = false;  // DISABLED - centered is more reliable
        [SerializeField] private UICorner pinnedCorner = UICorner.TopRight;
        [Tooltip("Horizontal offset from center (meters) - positive is right")]
        [SerializeField] private float cornerOffsetHorizontal = 0.6f;
        [Tooltip("Vertical offset from eye level (meters) - positive is up")]
        [SerializeField] private float cornerOffsetVertical = 0.35f;

        [Header("Auto-Create UI")]
        [Tooltip("Build a default world-space canvas + panels from code on Awake when no instructionPanel is assigned. Researchers who author their own UI prefab should turn this off and assign instructionPanel / progressPanel above.")]
        [SerializeField] private bool autoCreateUI = true;

        [Header("Event Recording")]
        [Tooltip("When true, every ShowInstruction/HideInstruction/SetInstructionTextOnly/ShowProgress/HideProgress call automatically writes a row to the SceneEventRecorder sidecar so the same UI flow can be replayed back. Turn off if your experiment manages its own event recording.")]
        [SerializeField] private bool autoRecordEvents = true;

        [Header("Cognitive Load HUD")]
        [Tooltip("Show the RIPA cognitive-load gauge as a vertical strip on the left edge of the experiment panel. RIPAMonitor still records the LiveLoadIndex CSV column when this is off — only the on-screen indicator is hidden.")]
        [SerializeField] private bool showRipaHud = true;
        [Tooltip("Width of the auto-built panel in meters.")]
        [SerializeField] private float panelWidth = 1.2f;
        [Tooltip("Height of the auto-built panel in meters.")]
        [SerializeField] private float panelHeightSize = 0.6f;
        [Tooltip("Tint of the auto-built panel background.")]
        [SerializeField] private Color backgroundColor = new Color(0.05f, 0.05f, 0.15f, 0.95f);
        [Tooltip("Default text tint for instructionText / progressText when auto-built.")]
        [SerializeField] private Color textColor = Color.white;
        [Tooltip("Default font size in points for the auto-built instruction text. Tuned large for VR legibility at the default panel distance.")]
        [SerializeField] private int fontSize = 72;

        private Canvas worldCanvas;
        private Vector3 targetPosition;
        private Quaternion targetRotation;

        // Corner phase indicator pinned to the panel's top-right edge.
        // Shows "Phase X / Y · <PhaseName>" plus N progress dots; updates
        // when SampleExperimentController.OnPhaseChanged fires.
        private GameObject phaseIndicatorPanel;
        private TextMeshProUGUI phaseIndicatorText;
        private GameObject[] phaseDots;
        private SampleExperimentController boundController;

        // Cognitive-load HUD, top-left corner. Auto-binds to RIPAMonitor.Instance
        // if one is in the scene; hides itself otherwise. Bar fills 0..1 of
        // (load / displayMax), tinted green->yellow->red. RIPA2 is clipped to
        // [0, 1.5]; displayMax matches.
        private GameObject loadHudPanel;
        private TextMeshProUGUI loadHudText;
        private RectTransform loadHudBarFill;
        private Image loadHudBarFillImage;
        private RIPAMonitor boundLoadMonitor;
        private const float kLoadHudDisplayMax = 1.5f; // matches RIPA2 clip range

        private void Awake()
        {
            if (cameraTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) cameraTransform = cam.transform;
            }

            if (autoCreateUI && instructionPanel == null)
            {
                CreateUI();
            }
        }

        private void Start()
        {
            Debug.Log("[ExperimentUI] Start() called");
            Debug.Log($"[ExperimentUI] cameraTransform: {(cameraTransform != null ? cameraTransform.name : "NULL")}");
            Debug.Log($"[ExperimentUI] instructionPanel: {(instructionPanel != null ? "SET" : "NULL")}");
            Debug.Log($"[ExperimentUI] worldCanvas: {(worldCanvas != null ? "SET" : "NULL")}");

            // Initialize hidden
            HideInstruction();
            HideProgress();
            if (phaseIndicatorPanel != null) phaseIndicatorPanel.SetActive(false);

            // Position in front of camera
            UpdatePanelPosition(true);

            // Subscribe to phase changes for the corner indicator.
            boundController = FindFirstObjectByType<SampleExperimentController>();
            if (boundController != null) boundController.OnPhaseChanged += HandlePhaseChanged;

            // Auto-bind to RIPAMonitor.Instance if present in scene. The
            // bootstrap auto-spawns one in every scene unless opted out, so
            // the HUD lights up by default. When showRipaHud is off the
            // monitor still runs (LiveLoadIndex still records), but the
            // panel stays hidden and we skip the subscription.
            if (showRipaHud)
            {
                boundLoadMonitor = RIPAMonitor.Instance;
                if (boundLoadMonitor != null && loadHudPanel != null)
                {
                    boundLoadMonitor.OnLoadChanged.AddListener(HandleLoadUpdated);
                    loadHudPanel.SetActive(boundLoadMonitor.Enabled);
                }
                else if (loadHudPanel != null)
                {
                    loadHudPanel.SetActive(false);
                }
            }
            else if (loadHudPanel != null)
            {
                loadHudPanel.SetActive(false);
            }

            Debug.Log($"[ExperimentUI] Initial position: {transform.position}, rotation: {transform.rotation.eulerAngles}");
        }

        private void OnDestroy()
        {
            if (boundController != null) boundController.OnPhaseChanged -= HandlePhaseChanged;
            if (boundLoadMonitor != null) boundLoadMonitor.OnLoadChanged.RemoveListener(HandleLoadUpdated);
        }

        private void HandleLoadUpdated(float load)
        {
            if (loadHudPanel == null) return;
            // Show the panel lazily on first valid reading so we don't flash
            // an empty bar.
            if (!loadHudPanel.activeSelf) loadHudPanel.SetActive(true);
            float t = Mathf.Clamp01(load / kLoadHudDisplayMax);
            if (loadHudText != null) loadHudText.text = $"Load\n{load:F2}";
            if (loadHudBarFill != null)
            {
                // Vertical strip — fill rises from the bottom.
                loadHudBarFill.anchorMax = new Vector2(1f, t);
            }
            if (loadHudBarFillImage != null)
            {
                // green at 0 → yellow at 0.5 → red at 1.0
                Color c = t < 0.5f
                    ? Color.Lerp(new Color(0.30f, 0.78f, 0.55f, 1f), new Color(0.95f, 0.85f, 0.30f, 1f), t * 2f)
                    : Color.Lerp(new Color(0.95f, 0.85f, 0.30f, 1f), new Color(0.92f, 0.40f, 0.30f, 1f), (t - 0.5f) * 2f);
                loadHudBarFillImage.color = c;
            }
        }

        // Phase order matches SampleExperimentController's runtime sequence.
        // Instructions / Idle / Complete don't show a numbered indicator;
        // only the four runtime phases do.
        private static readonly ExperimentPhase[] OrderedRuntimePhases = new[]
        {
            ExperimentPhase.FreeExploration,
            ExperimentPhase.VisualSearch,
            ExperimentPhase.CountingTask,
            ExperimentPhase.ChangeDetection,
        };

        private void HandlePhaseChanged(ExperimentPhase phase)
        {
            if (phaseIndicatorPanel == null) return;
            int index = System.Array.IndexOf(OrderedRuntimePhases, phase);
            if (index < 0)
            {
                // Instructions / Idle / Complete — hide the numbered indicator.
                phaseIndicatorPanel.SetActive(false);
                return;
            }
            phaseIndicatorPanel.SetActive(true);
            int total = OrderedRuntimePhases.Length;
            string label = $"Phase {index + 1} / {total} · {PrettyPhaseName(phase)}";
            if (phaseIndicatorText != null) phaseIndicatorText.text = label;
            // Update dots: filled = passed (≤ index), accent = current (index), empty = upcoming.
            if (phaseDots != null)
            {
                for (int i = 0; i < phaseDots.Length; i++)
                {
                    var img = phaseDots[i].GetComponent<Image>();
                    if (img == null) continue;
                    if (i < index)       img.color = new Color(0.55f, 0.78f, 1.00f, 0.85f); // passed
                    else if (i == index) img.color = new Color(0.95f, 0.62f, 0.22f, 1.00f); // current (warm amber)
                    else                 img.color = new Color(0.60f, 0.62f, 0.70f, 0.40f); // upcoming
                }
            }
        }

        private static string PrettyPhaseName(ExperimentPhase phase)
        {
            switch (phase)
            {
                case ExperimentPhase.FreeExploration: return "Free Exploration";
                case ExperimentPhase.VisualSearch:    return "Visual Search";
                case ExperimentPhase.CountingTask:    return "Counting";
                case ExperimentPhase.ChangeDetection: return "Change Detection";
                default: return phase.ToString();
            }
        }

        private void LateUpdate()
        {
            if (followCamera && cameraTransform != null)
            {
                UpdatePanelPosition(false);
            }

            // Lazy-resolve the monitor every LateUpdate AND poll CurrentLoad
            // so the gauge is always live. boundLoadMonitor captured once at
            // Start can be null if the bootstrap hadn't spawned the monitor
            // yet, and OnLoadChanged events are gated by Mathf.Approximately
            // so a calm session with near-zero deltas wouldn't update the bar.
            // The OnLoadChanged subscription stays for first-show; this poll
            // is the always-live update path. Skipped entirely when the HUD
            // is hidden — RIPAMonitor still computes for the CSV column.
            if (showRipaHud && loadHudPanel != null)
            {
                if (boundLoadMonitor == null || !boundLoadMonitor)
                {
                    var m = RIPAMonitor.Instance;
                    if (m != null)
                    {
                        boundLoadMonitor = m;
                        m.OnLoadChanged.AddListener(HandleLoadUpdated);
                    }
                }
                if (boundLoadMonitor != null && boundLoadMonitor)
                {
                    if (boundLoadMonitor.IsValid && !loadHudPanel.activeSelf)
                    {
                        loadHudPanel.SetActive(true);
                    }
                    if (loadHudPanel.activeSelf)
                    {
                        HandleLoadUpdated(boundLoadMonitor.CurrentLoad);
                    }
                }
            }
        }

        private void UpdatePanelPosition(bool immediate)
        {
            if (cameraTransform == null) return;

            if (pinToCorner && pinnedCorner != UICorner.Center)
            {
                UpdatePinnedPosition(immediate);
            }
            else
            {
                UpdateCenteredPosition(immediate);
            }
        }

        /// <summary>
        /// Update position when pinned to a corner.
        /// </summary>
        private void UpdatePinnedPosition(bool immediate)
        {
            // Calculate offsets based on corner
            float hOffset = cornerOffsetHorizontal;
            float vOffset = cornerOffsetVertical;

            switch (pinnedCorner)
            {
                case UICorner.TopLeft:
                    hOffset = -cornerOffsetHorizontal;
                    vOffset = cornerOffsetVertical;
                    break;
                case UICorner.TopRight:
                    hOffset = cornerOffsetHorizontal;
                    vOffset = cornerOffsetVertical;
                    break;
                case UICorner.BottomLeft:
                    hOffset = -cornerOffsetHorizontal;
                    vOffset = -cornerOffsetVertical;
                    break;
                case UICorner.BottomRight:
                    hOffset = cornerOffsetHorizontal;
                    vOffset = -cornerOffsetVertical;
                    break;
            }

            // Position relative to camera forward direction
            Vector3 forward = cameraTransform.forward;
            forward.y = 0;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            targetPosition = cameraTransform.position + forward * panelDistance;
            targetPosition += right * hOffset + Vector3.up * vOffset;

            // Explicit world-up keeps the panel strictly vertical (no roll)
            // even if the user's head is tilted at the moment LookRotation
            // resolves.
            targetRotation = Quaternion.LookRotation(forward, Vector3.up);

            // Always immediate when pinned - no lerp
            transform.position = targetPosition;
            transform.rotation = targetRotation;

            // Log position periodically (every 5 seconds)
            if (Time.frameCount % 300 == 0)
            {
                Debug.Log($"[ExperimentUI] Pinned position: pos={targetPosition}, camera={cameraTransform.position}");
            }
        }

        /// <summary>
        /// Update position when centered (lerped follow). Orients the panel
        /// using the room's forward axis (via
        /// <see cref="EyeTracking.Core.IRoomFrameProvider"/>) when one is
        /// available, so the panel sits parallel to the back wall regardless
        /// of camera facing; falls back to camera forward otherwise. Room
        /// frame is cached to avoid a FindObjectsByType scan every LateUpdate.
        /// </summary>
        private void UpdateCenteredPosition(bool immediate)
        {
            Vector3 forward = ResolveCenteringForward();

            // Anchor XZ to the room frame so the panel doesn't slide when the
            // user steps sideways, but take Y from the camera so the panel
            // sits at eye level — the room frame's origin is at the floor, so
            // anchoring Y to it would put the panel at ground level. Falls
            // back to camera anchor entirely when no room frame exists
            // (researcher-built scenes without an EnvironmentGenerator).
            Vector3 anchorPos = (cachedRoomFrame != null && cachedRoomFrame)
                ? cachedRoomFrame.position
                : cameraTransform.position;
            targetPosition = anchorPos + forward * panelDistance;
            // Latch eye-level Y on the first valid camera pose and reuse it
            // for the lifetime of the scene. Re-sampling cameraTransform.y on
            // every ShowInstruction would jump the panel whenever the user
            // crouches or stands between phases. Y < 1.0 m is treated as a
            // not-yet-tracked HMD (matches the CalibrationWorldUI stability
            // guard).
            if (!cachedAnchorYValid && cameraTransform.position.y >= 1.0f)
            {
                cachedAnchorY = cameraTransform.position.y + panelHeight;
                cachedAnchorYValid = true;
            }
            targetPosition.y = cachedAnchorYValid
                ? cachedAnchorY
                : cameraTransform.position.y + panelHeight;

            // Explicit world-up so head pitch/roll never leaks into the panel
            // orientation (same upright lock as UpdatePinnedPosition).
            targetRotation = Quaternion.LookRotation(forward, Vector3.up);

            if (immediate)
            {
                transform.position = targetPosition;
                transform.rotation = targetRotation;
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * followSpeed);
            }
        }

        private Transform cachedRoomFrame;
        private float cachedAnchorY;
        private bool cachedAnchorYValid;

        private Vector3 ResolveCenteringForward()
        {
            // Try the room frame first — gives a wall-parallel panel.
            if (cachedRoomFrame == null || !cachedRoomFrame)
            {
                var providers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (int i = 0; i < providers.Length; i++)
                {
                    if (providers[i] is EyeTracking.Core.IRoomFrameProvider rfp && rfp.RoomTransform != null)
                    {
                        cachedRoomFrame = rfp.RoomTransform;
                        break;
                    }
                }
            }
            if (cachedRoomFrame != null && cachedRoomFrame)
            {
                Vector3 roomForward = cachedRoomFrame.forward;
                roomForward.y = 0f;
                if (roomForward.sqrMagnitude > 0.01f) return roomForward.normalized;
            }
            // Fallback: camera's horizontal forward.
            Vector3 camForward = cameraTransform.forward;
            camForward.y = 0f;
            return camForward.sqrMagnitude > 0.01f ? camForward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Show instruction text.
        /// Repositions panel in front of camera so user can see it.
        /// </summary>
        public void ShowInstruction(string text)
        {
            if (autoRecordEvents) EyeLean.SceneState.SceneEventRecorder.Record("ShowInstruction", "", text);

            Debug.Log($"[ExperimentUI] ShowInstruction called with: '{text}'");
            Debug.Log($"[ExperimentUI] instructionPanel: {(instructionPanel != null ? "SET" : "NULL")}, instructionText: {(instructionText != null ? "SET" : "NULL")}");

            if (instructionPanel != null)
            {
                instructionPanel.SetActive(true);
                Debug.Log("[ExperimentUI] Instruction panel activated");
            }

            if (instructionText != null)
            {
                instructionText.text = text;
                Debug.Log($"[ExperimentUI] Text set, font: {(instructionText.font != null ? instructionText.font.name : "NULL")}");
            }

            // ALWAYS reposition panel in front of camera when showing new instruction
            // This ensures user can see it regardless of followCamera setting
            UpdatePanelPosition(true);
            Debug.Log($"[ExperimentUI] Panel positioned at {transform.position} (anchorY latched={cachedAnchorYValid}, value={cachedAnchorY:F3}, cameraY={cameraTransform.position.y:F3})");
        }

        /// <summary>
        /// Text-only instruction update — does NOT reposition the panel. Use
        /// this when swapping text mid-sequence (countdowns, score updates)
        /// so the panel doesn't snap to a new spot on each text change.
        /// </summary>
        public void SetInstructionTextOnly(string text)
        {
            if (autoRecordEvents) EyeLean.SceneState.SceneEventRecorder.Record("SetInstructionTextOnly", "", text);
            if (instructionPanel != null && !instructionPanel.activeSelf) instructionPanel.SetActive(true);
            if (instructionText != null) instructionText.text = text;
        }

        /// <summary>
        /// Hide instruction panel.
        /// </summary>
        public void HideInstruction()
        {
            if (autoRecordEvents) EyeLean.SceneState.SceneEventRecorder.Record("HideInstruction");
            if (instructionPanel != null)
            {
                instructionPanel.SetActive(false);
            }
        }

        /// <summary>
        /// Show progress text (smaller, less obtrusive).
        /// </summary>
        public void ShowProgress(string text)
        {
            // De-dupe: ShowProgress is typically called every frame in timed
            // phases. Only emit a recording event when the text actually
            // changes so the sidecar stays small while still capturing every
            // distinct UI state.
            if (autoRecordEvents && text != lastProgressText)
            {
                EyeLean.SceneState.SceneEventRecorder.Record("ShowProgress", "", text);
                lastProgressText = text;
            }
            if (progressPanel != null)
            {
                progressPanel.SetActive(!string.IsNullOrEmpty(text));
            }

            if (progressText != null)
            {
                progressText.text = text;
            }
        }
        private string lastProgressText = null;

        /// <summary>
        /// Hide progress panel.
        /// </summary>
        public void HideProgress()
        {
            if (autoRecordEvents) EyeLean.SceneState.SceneEventRecorder.Record("HideProgress");
            if (progressPanel != null)
            {
                progressPanel.SetActive(false);
            }
        }

        /// <summary>
        /// Set whether UI is pinned to a corner or centered.
        /// </summary>
        public void SetPinToCorner(bool pinned, UICorner corner = UICorner.TopRight)
        {
            pinToCorner = pinned;
            pinnedCorner = corner;
            UpdatePanelPosition(true);
        }

        /// <summary>
        /// Create UI elements programmatically using world-space canvas.
        /// </summary>
        private void CreateUI()
        {
            // Create world space canvas
            var canvasObj = new GameObject("ExperimentCanvas");
            canvasObj.transform.SetParent(transform);
            canvasObj.transform.localPosition = Vector3.zero;
            canvasObj.transform.localRotation = Quaternion.identity;

            worldCanvas = canvasObj.AddComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 300;  // Higher for sharper text

            canvasObj.AddComponent<GraphicRaycaster>();

            // Set canvas to use real-world meters directly
            // panelWidth and panelHeightSize are in meters
            var rectTransform = canvasObj.GetComponent<RectTransform>();
            float pixelsPerMeter = 1000f;  // 1000 pixels = 1 meter
            rectTransform.sizeDelta = new Vector2(panelWidth * pixelsPerMeter, panelHeightSize * pixelsPerMeter);
            rectTransform.localScale = Vector3.one / pixelsPerMeter;  // Convert pixels to meters

            Debug.Log($"[ExperimentUI] Canvas created: sizeDelta={rectTransform.sizeDelta}, localScale={rectTransform.localScale}");
            Debug.Log($"[ExperimentUI] Canvas world size: {panelWidth}m x {panelHeightSize}m");

            // Create instruction panel (fills canvas)
            instructionPanel = CreatePanel(canvasObj.transform, "InstructionPanel",
                new Vector2(0, 0), new Vector2(panelWidth * pixelsPerMeter, panelHeightSize * pixelsPerMeter));

            // Use larger font size for readability in VR
            instructionText = CreateText(instructionPanel.transform, "InstructionText",
                new Vector2(0, 0), new Vector2(panelWidth * pixelsPerMeter * 0.9f, panelHeightSize * pixelsPerMeter * 0.9f), fontSize);

            // Create progress panel (smaller, at bottom of main panel)
            float progressHeight = 80f;
            progressPanel = CreatePanel(canvasObj.transform, "ProgressPanel",
                new Vector2(0, -(panelHeightSize * pixelsPerMeter / 2 + progressHeight / 2 + 20)),
                new Vector2(panelWidth * pixelsPerMeter * 0.8f, progressHeight));

            progressText = CreateText(progressPanel.transform, "ProgressText",
                new Vector2(0, 0), new Vector2(panelWidth * pixelsPerMeter * 0.75f, progressHeight - 10), fontSize - 4);

            // Top-right corner phase indicator sits outside (above) the main
            // instruction panel so it stays glanceable without competing for
            // the user's attention during a phase.
            float indicatorWidth = panelWidth * pixelsPerMeter * 0.55f;
            float indicatorHeight = 110f;
            phaseIndicatorPanel = CreatePanel(canvasObj.transform, "PhaseIndicatorPanel",
                new Vector2(panelWidth * pixelsPerMeter * 0.5f - indicatorWidth * 0.5f - 20f,
                            panelHeightSize * pixelsPerMeter * 0.5f + indicatorHeight * 0.5f + 30f),
                new Vector2(indicatorWidth, indicatorHeight));
            // Slightly different background tint for visual separation.
            var indicatorBg = phaseIndicatorPanel.GetComponent<Image>();
            if (indicatorBg != null) indicatorBg.color = new Color(0.05f, 0.05f, 0.13f, 0.92f);

            phaseIndicatorText = CreateText(phaseIndicatorPanel.transform, "PhaseIndicatorText",
                new Vector2(0, 18f),
                new Vector2(indicatorWidth - 24f, 50f), fontSize - 28);
            phaseIndicatorText.alignment = TextAlignmentOptions.Center;
            phaseIndicatorText.text = "";

            // Progress dots — one per runtime phase.
            int dotCount = OrderedRuntimePhases.Length;
            phaseDots = new GameObject[dotCount];
            float dotSize = 18f;
            float dotSpacing = 14f;
            float dotsTotalWidth = dotCount * dotSize + (dotCount - 1) * dotSpacing;
            float dotsStartX = -dotsTotalWidth / 2f + dotSize / 2f;
            for (int i = 0; i < dotCount; i++)
            {
                var dot = new GameObject($"PhaseDot_{i}");
                dot.transform.SetParent(phaseIndicatorPanel.transform, false);
                var dotRect = dot.AddComponent<RectTransform>();
                dotRect.anchoredPosition = new Vector2(dotsStartX + i * (dotSize + dotSpacing), -28f);
                dotRect.sizeDelta = new Vector2(dotSize, dotSize);
                var dotImg = dot.AddComponent<Image>();
                dotImg.color = new Color(0.60f, 0.62f, 0.70f, 0.40f); // default: upcoming
                dotImg.raycastTarget = false;
                phaseDots[i] = dot;
            }
            phaseIndicatorPanel.SetActive(false);

            // Vertical strip on the left side of the experiment panel: sits
            // flush with the main panel's left edge, runs the full panel
            // height, with the numeric readout above the bar and a bottom-up
            // fill. Built only when the researcher leaves showRipaHud on —
            // otherwise we skip the panel entirely (RIPAMonitor itself still
            // runs and writes the CSV column).
            if (!showRipaHud)
            {
                Debug.Log($"[ExperimentUI] UI created: panelWidth={panelWidth}m, panelHeight={panelHeightSize}m, fontSize={fontSize}");
                Debug.Log($"[ExperimentUI] Pinning config: pinToCorner={pinToCorner}, corner={pinnedCorner}, offset=({cornerOffsetHorizontal}m, {cornerOffsetVertical}m)");
                Debug.Log($"[ExperimentUI] Panel distance: {panelDistance}m");
                Debug.Log("[ExperimentUI] Cognitive-load HUD strip suppressed (showRipaHud=false). RIPAMonitor still records LiveLoadIndex.");
                return;
            }
            float loadHudWidth = 110f;
            float loadHudHeight = panelHeightSize * pixelsPerMeter;
            loadHudPanel = CreatePanel(canvasObj.transform, "LoadHudPanel",
                new Vector2(-(panelWidth * pixelsPerMeter * 0.5f + loadHudWidth * 0.5f + 20f), 0f),
                new Vector2(loadHudWidth, loadHudHeight));
            var loadBg = loadHudPanel.GetComponent<Image>();
            if (loadBg != null) loadBg.color = new Color(0.05f, 0.05f, 0.13f, 0.92f);

            // Two-line "Load / 0.42" readout above the bar so the strip stays
            // narrow.
            float labelHeight = 90f;
            loadHudText = CreateText(loadHudPanel.transform, "LoadHudText",
                new Vector2(0, loadHudHeight * 0.5f - labelHeight * 0.5f - 10f),
                new Vector2(loadHudWidth - 12f, labelHeight), fontSize - 32);
            loadHudText.alignment = TextAlignmentOptions.Center;
            loadHudText.text = "Load\n--";

            // Vertical bar background — narrow column filling the rest of
            // the strip below the label.
            float barWidth = 28f;
            float barTopPadding = labelHeight + 24f;
            float barBottomPadding = 16f;
            float barHeight = loadHudHeight - barTopPadding - barBottomPadding;
            float barCenterY = (barBottomPadding - barTopPadding) * 0.5f;
            var barBgObj = new GameObject("LoadHudBarBg");
            barBgObj.transform.SetParent(loadHudPanel.transform, false);
            var barBgRect = barBgObj.AddComponent<RectTransform>();
            barBgRect.anchoredPosition = new Vector2(0, barCenterY);
            barBgRect.sizeDelta = new Vector2(barWidth, barHeight);
            var barBgImg = barBgObj.AddComponent<Image>();
            barBgImg.color = new Color(0.18f, 0.18f, 0.22f, 1f);
            barBgImg.raycastTarget = false;

            // Bar fill (anchored bottom, animated via anchorMax.y).
            var barFillObj = new GameObject("LoadHudBarFill");
            barFillObj.transform.SetParent(barBgObj.transform, false);
            loadHudBarFill = barFillObj.AddComponent<RectTransform>();
            loadHudBarFill.anchorMin = new Vector2(0f, 0f);
            loadHudBarFill.anchorMax = new Vector2(1f, 0f);
            loadHudBarFill.offsetMin = Vector2.zero;
            loadHudBarFill.offsetMax = Vector2.zero;
            loadHudBarFillImage = barFillObj.AddComponent<Image>();
            loadHudBarFillImage.color = new Color(0.30f, 0.78f, 0.55f, 1f);
            loadHudBarFillImage.raycastTarget = false;
            loadHudPanel.SetActive(false);

            Debug.Log($"[ExperimentUI] UI created: panelWidth={panelWidth}m, panelHeight={panelHeightSize}m, fontSize={fontSize}");
            Debug.Log($"[ExperimentUI] Pinning config: pinToCorner={pinToCorner}, corner={pinnedCorner}, offset=({cornerOffsetHorizontal}m, {cornerOffsetVertical}m)");
            Debug.Log($"[ExperimentUI] Panel distance: {panelDistance}m");
        }

        private GameObject CreatePanel(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent);

            var rect = panel.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            var image = panel.AddComponent<Image>();
            image.color = backgroundColor;

            // Rounded corners would require a custom sprite, using solid for now
            return panel;
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            var textObj = new GameObject(name);
            textObj.transform.SetParent(parent);

            var rect = textObj.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = fontSize;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;

            return tmp;
        }

        /// <summary>
        /// Set panel distance from camera.
        /// </summary>
        public void SetPanelDistance(float distance)
        {
            panelDistance = distance;
            UpdatePanelPosition(true);
        }
    }
}
