using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using EyeTracking.Core;
using EyeTracking.Components;

namespace EyeTracking.Calibration.UI
{
    /// <summary>
    /// VR world-space UI for calibration session instructions, progress, and interaction.
    /// Uses gaze-based selection (dwell-to-select) for VR interaction.
    /// </summary>
    public class CalibrationWorldUI : MonoBehaviour
    {
        [Header("UI Configuration")]
        [Tooltip("Distance the UI panel sits from the user, in meters. 1.5–2 m is a comfortable VR reading distance.")]
        [SerializeField] private float distanceFromCamera = 2f;
        [Tooltip("Vertical offset of the panel relative to eye height, in meters. Negative = below eye level (comfortable for reading); positive = above.")]
        [SerializeField] private float verticalOffset = -0.15f;
        [Tooltip("Panel width in canvas units (multiplied by uiScale to get meters). 900 ≈ 1.8 m at the default uiScale.")]
        [SerializeField] private float panelWidth = 900f;
        [Tooltip("Panel height in canvas units. 800 ≈ 1.6 m at the default uiScale.")]
        [SerializeField] private float panelHeight = 800f;
        [Tooltip("Canvas-units → meters scale. 0.002 = 1 canvas unit per 2 mm.")]
        [SerializeField] private float uiScale = 0.002f;
        [Tooltip("When true, UI follows the head (HUD lock). When false, UI is pinned to a fixed world position. HUD-lock is fine for short prompts; world-lock is better when the user must look around.")]
        [SerializeField] private bool parentToCamera = false;

        // When true, GetCurrentGaze ignores the eye tracker and uses
        // cameraTransform.forward exclusively (head-pointing). Set by
        // ForceWorldSpaceMode for callers that don't want eye-gaze
        // selection (e.g. before calibration when the eye-tracker stream
        // may not be warmed up).
        private bool useHeadDirectionOnly;

        /// <summary>
        /// Runtime override applied at Start by callers that need the
        /// canvas detached from the camera, the gaze threshold tightened,
        /// and the clear-margin guard disabled. Overrides serialized
        /// inspector defaults so MainMenu-style scenes can opt out of
        /// HUD-lock without editing every scene asset.
        /// </summary>
        public void ForceWorldSpaceMode(float overrideThresholdDegrees = 8f, float overrideRequireClearMargin = 0f)
        {
            parentToCamera = false;
            isPositioned = false;
            gazeAngleThreshold = overrideThresholdDegrees;
            requireClearMargin = overrideRequireClearMargin;
            armedAtRealtime = -1f; // re-arm the dwell guard at the new threshold
            // The enforcer coroutine becomes the SOLE placement authority for
            // the next few seconds. Eager PositionUI() calls from elsewhere
            // (ShowInstructions, ShowButtons, PositionUIWhenReady) early-return
            // while this flag is set — they'd otherwise place the canvas at
            // the pre-readiness camera pose, then the enforcer would re-place
            // it at the post-readiness pose, producing the visible "jumps"
            // researchers reported on MainMenu startup. Single placement at a
            // stable HMD pose is the goal.
            worldSpaceEnforcerOwnsPlacement = true;
            // Hide the canvas until the enforcer places it; otherwise on
            // re-entry the panel briefly renders at the canvas's default
            // world origin (0, 0, 0) — at the user's feet — before the
            // stable placement snaps it to its correct distance. ShowInstructions
            // / ShowButtons re-enable canvas as needed once the enforcer
            // releases ownership.
            if (canvas != null) canvas.gameObject.SetActive(false);
            StartCoroutine(EnforceWorldSpaceModeForSeconds(3f));

            // Eye-gaze drives selection (head pose now reaches Camera.main
            // via HmdPoseDriverBootstrap, so eye gaze is reliable here).
            useHeadDirectionOnly = false;
            // Reset smoothing so the first frame doesn't average a stale
            // direction from before the override.
            smoothedGazeDirection = Vector3.zero;
            if (canvas != null && canvas.transform.parent != null)
            {
                // Detach canvas from any prior parent (camera, in HUD-locked
                // scenes). With cameraTransform null we can still detach
                // since canvas.transform.parent is the actual parent.
                canvas.transform.SetParent(null, false);
            }
            Debug.Log($"[CalibrationUI] ForceWorldSpaceMode applied: threshold={gazeAngleThreshold}°, requireClearMargin={requireClearMargin}°, parentToCamera={parentToCamera}, useHeadDirectionOnly={useHeadDirectionOnly}");
        }

        /// <summary>
        /// Keep enforcing the world-space invariants for a few seconds
        /// after ForceWorldSpaceMode is called, so any race that re-parents
        /// the canvas to the camera (or re-positions before VR is ready)
        /// gets corrected on the next frame instead of leaving the menu
        /// HUD-locked for the rest of the session.
        /// </summary>
        private System.Collections.IEnumerator EnforceWorldSpaceModeForSeconds(float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            int loggedCorrections = 0;
            // Positioning policy:
            //
            // Phase 1 — keep the canvas detached from any parent (some
            // scenes try to re-parent it to the camera on load).
            //
            // Phase 2 — wait until the HMD pose has been stable (<3 cm of
            // motion over ~0.4 s) before doing a single re-anchor. Naive
            // "reposition whenever the camera drifted" produces a jumping
            // panel because the user's head naturally moves as they look
            // around. Stability-anchored single placement avoids that.
            const float stabilityWindowSeconds = 0.4f;
            const float stabilityThresholdMeters = 0.03f;
            Vector3 windowStartPos = Vector3.positiveInfinity;
            float windowStartTime = -1f;
            bool placed = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (canvas != null)
                {
                    // Detach if anything re-parented us to the camera.
                    if (canvas.transform.parent != null)
                    {
                        if (loggedCorrections < 3)
                        {
                            Debug.Log($"[CalibrationUI] EnforceWorldSpaceMode: re-detaching canvas from parent '{canvas.transform.parent.name}'.");
                            loggedCorrections++;
                        }
                        canvas.transform.SetParent(null, false);
                        isPositioned = false;
                        placed = false; // Re-arm a place after detach.
                        // Re-claim placement authority for this stability window
                        // so unrelated callers don't slip in an interim placement
                        // at an unstable pose.
                        worldSpaceEnforcerOwnsPlacement = true;
                    }

                    if (!placed && cameraTransform != null && cameraTransform.position.magnitude > 0.1f)
                    {
                        Vector3 nowPos = cameraTransform.position;
                        // Require the camera to be at a plausible
                        // standing/seated height before treating its pose
                        // as stable. An HMD resting on a desk reports
                        // near-floor height; without this guard the
                        // enforcer would lock the panel to that pose and
                        // produce a "panel at the user's feet" pop on
                        // re-entry. 1.0 m clears typical seated research
                        // postures (eye height 1.1-1.3 m) without
                        // false-positiving a desk-resting HMD (~0.7 m).
                        const float minPlausibleHeadHeight = 1.0f;
                        if (nowPos.y < minPlausibleHeadHeight)
                        {
                            // Reset the window so we don't accept this pose.
                            windowStartTime = -1f;
                            yield return null;
                            continue;
                        }
                        // Restart the stability window if the head moved
                        // beyond the threshold from the window's anchor.
                        if (windowStartTime < 0f
                            || (nowPos - windowStartPos).sqrMagnitude > stabilityThresholdMeters * stabilityThresholdMeters)
                        {
                            windowStartPos = nowPos;
                            windowStartTime = Time.realtimeSinceStartup;
                        }
                        else if (Time.realtimeSinceStartup - windowStartTime >= stabilityWindowSeconds)
                        {
                            // Pose has been stable long enough — place the
                            // panel here, once.
                            isPositioned = false;
                            PlaceFromEnforcer();
                            placed = true;
                            // Reveal the canvas now that it's at the correct
                            // location — it was hidden by ForceWorldSpaceMode
                            // to suppress the brief default-origin flash.
                            if (canvas != null) canvas.gameObject.SetActive(true);
                            // Hand placement authority back to normal callers
                            // now that we've anchored once; further re-detach
                            // events still re-place via this same coroutine.
                            worldSpaceEnforcerOwnsPlacement = false;
                            Debug.Log($"[CalibrationUI] EnforceWorldSpaceMode: anchored panel at stable head pose {nowPos:F2}.");
                        }
                    }
                }
                yield return null;
            }
            // Safety net: if we never observed a stable pose (e.g. user is
            // moving constantly), place the panel once at the final pose so
            // it lands somewhere reasonable rather than at the spawn-frame
            // projection.
            if (!placed && canvas != null && cameraTransform != null && cameraTransform.position.magnitude > 0.1f)
            {
                isPositioned = false;
                PlaceFromEnforcer();
                Debug.Log("[CalibrationUI] EnforceWorldSpaceMode: stability window never met; placing at final pose anyway.");
            }
            // Always reveal the canvas at the end of the window, even if we
            // never placed (e.g. user kept moving) — better to show the panel
            // at default coordinates than leave the menu hidden indefinitely.
            if (canvas != null && !canvas.gameObject.activeSelf)
            {
                canvas.gameObject.SetActive(true);
            }
            // Always release placement authority when the enforcement window
            // ends, so subsequent ShowInstructions calls (status refresh,
            // dwell completion → next-scene transition prompt) work normally.
            worldSpaceEnforcerOwnsPlacement = false;
        }

        // Bypass the enforcer-ownership early-return inside PositionUI. Used
        // exclusively by EnforceWorldSpaceModeForSeconds.
        private void PlaceFromEnforcer()
        {
            bool prev = _inEnforcerPlacement;
            _inEnforcerPlacement = true;
            try { PositionUI(); }
            finally { _inEnforcerPlacement = prev; }
        }
        [Tooltip("When true, skip the FOV-based auto-resize and use the panelWidth/panelHeight inspector values directly. Use for small HUD-style panels (e.g., MainMenu) where the auto-resize would push buttons outside the gaze cone.")]
        [SerializeField] private bool useFixedPanelSize = false;
        [Tooltip("Seconds after the panel first becomes visible before gaze-dwell selections are armed. Prevents the user's natural forward gaze at scene load from immediately triggering whichever button is centered. 0 = armed instantly (legacy behavior).")]
        [SerializeField] private float gazeArmingDelaySeconds = 0f;
        private float armedAtRealtime = -1f;

        [Header("Gaze Interaction")]
        [Tooltip("Seconds the user must dwell on a button before it activates. 1.5 s balances accidental selection against tedium.")]
        [SerializeField] private float dwellTime = 1.5f;
        [Tooltip("Enable gaze-driven button selection. Off = controller / keyboard only.")]
        [SerializeField] private bool useGazeSelection = true;
        [Tooltip("Highlight tint applied to the button being gazed at. Should contrast with the resting buttonColor.")]
        [SerializeField] private Color gazeHighlightColor = new Color(0.5f, 0.85f, 1f);
        [Tooltip("Angular threshold in degrees - how close gaze must be to button center")]
        // 8° is tight enough that the head-forward fallback (no eye-tracker
        // data yet) doesn't auto-activate either button from the default
        // pose — user must rotate toward the button they want. Calibrated
        // gaze regularly reaches sub-5° during fixation.
        [SerializeField] private float gazeAngleThreshold = 8f;

        [Tooltip("Minimum angular margin (degrees) by which the closest button must beat the runner-up before it counts as a clear pick. Default 0 (disabled): with the head-forward fallback and a HUD-locked panel, button angles don't change and the margin guard locks the user out. Raise this for stricter disambiguation when stable eye-tracker data + non-HUD-locked panels are available.")]
        [SerializeField] private float requireClearMargin = 0f;
        [Tooltip("Seconds to keep button highlighted after gaze leaves (prevents flickering)")]
        [SerializeField] private float gazeHysteresis = 0.5f;
        [Tooltip("Gaze smoothing factor (0.1 = very smooth, 1.0 = no smoothing)")]
        [SerializeField] private float gazeSmoothingFactor = 0.15f;
        [Tooltip("How close vergence depth must be to button depth (meters). Set to 0 to disable depth check.")]
        [SerializeField] private float depthTolerance = 0f; // Disabled until vergence is more reliable

        [Header("Colors")]
        // Deep indigo background with warm amber accent stripe. High
        // contrast for VR body text legibility at 2 m without going pure
        // white.
        [SerializeField] private Color backgroundColor = new Color(0.07f, 0.08f, 0.13f, 0.96f);
        [SerializeField] private Color accentStripeColor = new Color(0.85f, 0.55f, 0.20f, 1f);
        [SerializeField] private Color titleColor = new Color(0.97f, 0.97f, 1.00f);
        [SerializeField] private Color textColor = new Color(0.86f, 0.88f, 0.92f);
        [SerializeField] private Color buttonColor = new Color(0.18f, 0.48f, 0.82f);
        [SerializeField] private Color buttonGazedColor = new Color(0.30f, 0.68f, 1.00f);
        [SerializeField] private Color progressBarColor = new Color(0.30f, 0.78f, 0.55f);

        // UI Elements
        private Canvas canvas;
        private RectTransform canvasRect;
        private GameObject mainPanel;
        private Text titleText;
        private Text instructionText;
        private GameObject buttonPanel;
        private Button startButton;
        private Button nextButton;
        private Image startButtonImage;
        private Image nextButtonImage;
        private Image startDwellIndicator;
        private Image nextDwellIndicator;
        private GameObject progressPanel;
        private Text progressText;
        private Image progressBar;
        private RectTransform progressBarFill;
        private GameObject countdownPanel;
        private Text countdownText;

        // References
        private Transform cameraTransform;
        private IEyeTracker eyeTracker;
        private EyeTracker simpleEyeTracker;

        // Gaze interaction state
        private Button currentGazedButton;
        private float gazeStartTime;
        private float gazeExitTime;
        private bool isInHysteresis;
        private Vector3 smoothedGazeDirection;
        private Vector3 lastGazeOrigin;
        private Dictionary<Button, Image> buttonImages = new Dictionary<Button, Image>();
        private Dictionary<Button, Image> dwellIndicators = new Dictionary<Button, Image>();

        // Debug logging
        private int frameCount = 0;

        // Events
        public event System.Action OnStartClicked;
        public event System.Action OnNextClicked;

        void Awake()
        {
            FindCamera();
            CreateUI();
        }

        void Start()
        {
            // Try to find camera again if not found in Awake
            if (cameraTransform == null)
            {
                FindCamera();
            }

            // Defer PositionUI until VRReadinessService confirms the camera is
            // actually tracking (camPos.y > 0.5 && magnitude > 0.1). Without
            // this, an early PositionUI puts the panel at floor level on
            // first scene load. The readiness service waits up to 5s, then
            // the coroutine continues regardless.
            StartCoroutine(PositionUIWhenReady());
            eyeTracker = EyeTrackerFactory.GetEyeTracker();

            // Find EyeTracker for vergence depth validation
            simpleEyeTracker = FindObjectOfType<EyeTracker>();
            if (simpleEyeTracker == null && depthTolerance > 0)
            {
                Debug.LogWarning("[CalibrationUI] EyeTracker not found - depth validation disabled");
            }

            Debug.Log($"[CalibrationUI] Start - Camera: {(cameraTransform != null ? cameraTransform.name : "NULL")}, Canvas: {(canvas != null ? "EXISTS" : "NULL")}, EyeTracker: {(simpleEyeTracker != null ? "FOUND" : "NULL")}");
        }

        private IEnumerator PositionUIWhenReady()
        {
            var readiness = VRReadinessService.Instance;
            if (readiness != null) yield return readiness.WaitForCameraReady(5f);
            PositionUI();
        }

        private void FindCamera()
        {
            // Try Camera.main first
            cameraTransform = Camera.main?.transform;

            // If not found, search for any camera tagged MainCamera
            if (cameraTransform == null)
            {
                var mainCam = GameObject.FindGameObjectWithTag("MainCamera");
                if (mainCam != null)
                {
                    cameraTransform = mainCam.transform;
                }
            }

            // If still not found, find any active camera
            if (cameraTransform == null)
            {
                var anyCam = FindObjectOfType<Camera>();
                if (anyCam != null)
                {
                    cameraTransform = anyCam.transform;
                    Debug.LogWarning($"[CalibrationUI] Camera.main not found, using camera: {anyCam.name}");
                }
            }

            if (cameraTransform == null)
            {
                Debug.LogError("[CalibrationUI] No camera found! UI will not be positioned correctly.");
            }
        }

        void Update()
        {
            frameCount++;

            // Gaze-based interaction only
            if (useGazeSelection && canvas.gameObject.activeInHierarchy)
            {
                // Arming window: when the panel first becomes visible we
                // give the user a moment to read it before any button can
                // start its dwell timer. Otherwise a HUD-locked menu fires
                // the centered button on scene load, since the user's
                // initial gaze is straight ahead.
                if (armedAtRealtime < 0f)
                {
                    armedAtRealtime = Time.realtimeSinceStartup + gazeArmingDelaySeconds;
                }
                if (Time.realtimeSinceStartup < armedAtRealtime) return;

                UpdateGazeInteraction();
            }
            else
            {
                armedAtRealtime = -1f;
            }
        }

        void LateUpdate()
        {
            // When parented to camera, no manual update needed - Unity handles transform hierarchy
            // This method kept for potential future use (e.g., smooth transitions)
        }

        #region Gaze Interaction

        private void UpdateGazeInteraction()
        {
            // Get current gaze
            Vector3 gazeOrigin, gazeDirection;
            GetCurrentGaze(out gazeOrigin, out gazeDirection);

            // Find which button (if any) the gaze is on
            Button gazedButton = FindGazedButton(gazeOrigin, gazeDirection);

            if (gazedButton != null)
            {
                // We are gazing at a button
                isInHysteresis = false;

                if (gazedButton != currentGazedButton)
                {
                    // Gaze moved to a different button
                    if (currentGazedButton != null)
                    {
                        OnButtonGazeExit(currentGazedButton);
                    }

                    currentGazedButton = gazedButton;
                    OnButtonGazeEnter(currentGazedButton);
                }
                else
                {
                    // Continue gazing at same button - update dwell
                    UpdateDwellProgress(currentGazedButton);
                }
            }
            else if (currentGazedButton != null)
            {
                // Gaze left the button - apply hysteresis
                if (!isInHysteresis)
                {
                    isInHysteresis = true;
                    gazeExitTime = Time.time;
                }
                else if (Time.time - gazeExitTime >= gazeHysteresis)
                {
                    // Hysteresis period elapsed - actually exit
                    OnButtonGazeExit(currentGazedButton);
                    currentGazedButton = null;
                    isInHysteresis = false;
                }
                else
                {
                    // Still in hysteresis - continue dwell progress
                    UpdateDwellProgress(currentGazedButton);
                }
            }
        }

        // Track which path supplied the most recent gaze sample so the
        // per-frame diagnostic can tell us whether eye-tracker data is
        // actually flowing or whether we're stuck on the head fallback.
        private string lastGazeSource = "init";

        private void GetCurrentGaze(out Vector3 origin, out Vector3 direction)
        {
            Vector3 rawOrigin;
            Vector3 rawDirection;

            // If the cached eyeTracker reports unavailable (scene loaded
            // before OpenXR provider initialized; factory cached a
            // NullEyeTracker), re-resolve from the factory. Otherwise this
            // component is stuck on the stale null tracker forever even
            // though the rest of the app sees a working eye tracker.
            if (eyeTracker == null || !eyeTracker.IsAvailable)
            {
                var fresh = EyeTrackerFactory.GetEyeTracker();
                if (fresh != null && fresh.IsAvailable)
                {
                    eyeTracker = fresh;
                }
            }

            if (useHeadDirectionOnly && cameraTransform != null)
            {
                // Opt-in head-pointing mode (not currently used — kept for
                // diagnostic/debug paths).
                rawOrigin = cameraTransform.position;
                rawDirection = cameraTransform.forward;
                lastGazeSource = "head-forced";
            }
            else if (eyeTracker != null && eyeTracker.IsAvailable)
            {
                if (!eyeTracker.GetCombinedGazeOrigin(out rawOrigin) ||
                    !eyeTracker.GetCombinedGazeDirection(out rawDirection))
                {
                    // Fallback to head direction
                    rawOrigin = cameraTransform.position;
                    rawDirection = cameraTransform.forward;
                    lastGazeSource = "head-fallback(GetCombined returned false)";
                }
                else
                {
                    // Direction is already world-space from OpenXR; for the
                    // origin, use camera position to match EyeTracker's
                    // gaze-ray semantics.
                    rawOrigin = cameraTransform.position;
                    // Direction is already world-space from OpenXR
                    lastGazeSource = "eye-tracker";
                }
            }
            else
            {
                // Use head direction as gaze
                rawOrigin = cameraTransform.position;
                rawDirection = cameraTransform.forward;
                lastGazeSource = eyeTracker == null ? "head(no-tracker)" : "head(tracker-unavailable)";
            }

            // Apply gaze smoothing to reduce jitter
            if (smoothedGazeDirection == Vector3.zero)
            {
                smoothedGazeDirection = rawDirection;
            }
            else
            {
                smoothedGazeDirection = Vector3.Slerp(smoothedGazeDirection, rawDirection, gazeSmoothingFactor);
            }

            lastGazeOrigin = rawOrigin;
            origin = rawOrigin;
            direction = smoothedGazeDirection.normalized;
        }

        private Button FindGazedButton(Vector3 gazeOrigin, Vector3 gazeDirection)
        {
            // Get all active buttons
            List<Button> activeButtons = new List<Button>();

            if (startButton != null && startButton.gameObject.activeInHierarchy && startButton.interactable)
                activeButtons.Add(startButton);
            if (nextButton != null && nextButton.gameObject.activeInHierarchy && nextButton.interactable)
                activeButtons.Add(nextButton);

            // Get vergence depth for depth validation
            float gazeDepth = float.MaxValue;
            bool hasVergenceData = false;
            if (simpleEyeTracker != null && depthTolerance > 0)
            {
                var vergenceResult = simpleEyeTracker.GetCurrentVergenceResult();
                if (vergenceResult.isValid)
                {
                    gazeDepth = Vector3.Distance(gazeOrigin, vergenceResult.finalVergencePoint);
                    hasVergenceData = true;
                }
            }

            // Find the button with smallest angular distance within threshold
            Button closestButton = null;
            float smallestAngle = float.MaxValue;

            // Build debug info
            string debugInfo = "";

            // Track second-best for the clear-margin check. With a HUD-locked
            // panel, head rotation moves the buttons WITH the user, so eye
            // fixation is the only way to disambiguate which button to pick.
            // If two buttons are within ~1° of each other angle-wise, the
            // user can't reliably select one — fall through to "no pick" and
            // make them look more deliberately.
            float secondAngle = float.MaxValue;
            foreach (Button button in activeButtons)
            {
                float angle = GetAngularDistanceToButton(gazeOrigin, gazeDirection, button);

                // Get button depth for depth validation
                RectTransform buttonRect = button.GetComponent<RectTransform>();
                float buttonDepth = buttonRect != null ?
                    Vector3.Distance(gazeOrigin, buttonRect.position) : distanceFromCamera;

                // Check depth tolerance if vergence data is available
                float depthDiff = Mathf.Abs(gazeDepth - buttonDepth);
                bool depthOk = !hasVergenceData || depthTolerance <= 0 || depthDiff <= depthTolerance;

                debugInfo += $"{button.name}:{angle:F1}°";
                if (hasVergenceData)
                    debugInfo += $"(d:{depthDiff:F2}m)";
                debugInfo += " ";

                if (angle < gazeAngleThreshold && depthOk)
                {
                    if (angle < smallestAngle)
                    {
                        secondAngle = smallestAngle;
                        smallestAngle = angle;
                        closestButton = button;
                    }
                    else if (angle < secondAngle)
                    {
                        secondAngle = angle;
                    }
                }
            }

            // Enforce a clear-margin requirement so the panel's first-pick
            // on scene load doesn't lock to whichever button is marginally
            // closer to the user's default head-forward direction.
            if (requireClearMargin > 0f && closestButton != null && secondAngle < float.MaxValue)
            {
                if ((secondAngle - smallestAngle) < requireClearMargin)
                {
                    debugInfo += $"[ambiguous: margin {(secondAngle - smallestAngle):F1}° < {requireClearMargin:F1}°]";
                    closestButton = null;
                }
            }

            // Log every 60 frames (~1 second at 60fps)
            if (frameCount % 60 == 0 && activeButtons.Count > 0)
            {
                string result = closestButton != null ? $"HIT: {closestButton.name}" : "NO HIT";
                string depthInfo = hasVergenceData ? $"gazeDepth:{gazeDepth:F2}m tol:{depthTolerance:F1}m" : "no vergence";
                // Diagnostic: surface camera pose + gaze direction +
                // canvas parent so the log alone reveals whether the head
                // is tracking, the gaze direction is stale, or the canvas
                // is HUD-locked.
                string camInfo = cameraTransform != null
                    ? $"camPos={cameraTransform.position:F2} camFwd={cameraTransform.forward:F2}"
                    : "camNULL";
                string gazeInfo = $"gazeDir={gazeDirection:F3}";
                string canvasInfo = canvas != null
                    ? $"canvasParent={(canvas.transform.parent != null ? canvas.transform.parent.name : "null")} canvasPos={canvas.transform.position:F2}"
                    : "canvasNULL";
                string modeInfo = $"useHead={useHeadDirectionOnly} parentToCam={parentToCamera} src={lastGazeSource}";
                Debug.Log($"[CalibrationUI] Gaze: {debugInfo}| {depthInfo} | {result} | {camInfo} | {gazeInfo} | {canvasInfo} | {modeInfo}");
            }

            return closestButton;
        }

        /// <summary>
        /// Calculate angular distance (in degrees) from gaze ray to button center.
        /// This is much more robust than plane intersection math.
        /// </summary>
        private float GetAngularDistanceToButton(Vector3 gazeOrigin, Vector3 gazeDirection, Button button)
        {
            if (button == null) return float.MaxValue;

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            if (buttonRect == null) return float.MaxValue;

            // Get button center in world space
            Vector3 buttonCenter = buttonRect.position;

            // Vector from gaze origin to button center
            Vector3 toButton = (buttonCenter - gazeOrigin).normalized;

            // Angular distance in degrees
            float angle = Vector3.Angle(gazeDirection, toButton);

            return angle;
        }

        private void OnButtonGazeEnter(Button button)
        {
            gazeStartTime = Time.time;
            lastProgressLog = 0f; // Reset progress logging

            // Highlight using buttonGazedColor; fall back to the legacy
            // gazeHighlightColor if it hasn't been set in the inspector.
            if (buttonImages.TryGetValue(button, out Image image))
            {
                image.color = buttonGazedColor.a > 0f ? buttonGazedColor : gazeHighlightColor;
            }

            // Reset and show dwell indicator
            if (dwellIndicators.TryGetValue(button, out Image indicator))
            {
                indicator.fillAmount = 0f;
                indicator.gameObject.SetActive(true);
            }

            Debug.Log($"[CalibrationUI] Gaze ENTERED: {button.name} - Starting dwell timer (threshold: {gazeAngleThreshold}°)");
        }

        private void OnButtonGazeExit(Button button)
        {
            Debug.Log($"[CalibrationUI] Gaze EXITED: {button.name}");

            // Restore button color
            if (buttonImages.TryGetValue(button, out Image image))
            {
                image.color = buttonColor;
            }

            // Hide dwell indicator
            if (dwellIndicators.TryGetValue(button, out Image indicator))
            {
                indicator.fillAmount = 0f;
                indicator.gameObject.SetActive(false);
            }

            lastProgressLog = 0f;
        }

        private float lastProgressLog = 0f;

        private void UpdateDwellProgress(Button button)
        {
            float elapsed = Time.time - gazeStartTime;
            float progress = elapsed / dwellTime;

            // Update dwell indicator
            if (dwellIndicators.TryGetValue(button, out Image indicator))
            {
                indicator.fillAmount = Mathf.Clamp01(progress);
            }

            // Log progress periodically (every 25%)
            if (progress >= lastProgressLog + 0.25f)
            {
                lastProgressLog = Mathf.Floor(progress * 4) / 4f;
                Debug.Log($"[CalibrationUI] Dwell progress: {progress:P0} on {button.name}");
            }

            // Activate button when dwell complete
            if (progress >= 1f)
            {
                Debug.Log($"[CalibrationUI] DWELL COMPLETE - Activating {button.name}!");
                lastProgressLog = 0f;
                ActivateButton(button);
                currentGazedButton = null;
                gazeStartTime = Time.time + 1f; // Cooldown
            }
        }

        private void ActivateButton(Button button)
        {
            Debug.Log($"[CalibrationUI] Button activated via gaze: {button.name}");

            // Reset visuals
            OnButtonGazeExit(button);

            // Trigger click
            button.onClick.Invoke();
        }

        #endregion

        /// <summary>
        /// Create all UI elements.
        /// </summary>
        private void CreateUI()
        {
            Debug.Log("[CalibrationUI] CreateUI starting...");

            // Create Canvas
            GameObject canvasObj = new GameObject("CalibrationCanvas");
            // Spawn the canvas far below the scene so any frame that
            // momentarily activates it before PositionUI runs is invisible
            // to the user. PositionUI / the enforcer move it to the
            // correct location once a stable head pose is observed.
            // Without this, the canvas can be visible at world (0, 0, 0)
            // for the few frames between Start and the enforcer's
            // stability-anchored placement, producing a "panel pops at
            // user's feet" flash.
            canvasObj.transform.position = new Vector3(0f, -1000f, 0f);
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            canvasRect = canvas.GetComponent<RectTransform>();

            // Calculate size to fill FOV at given distance, unless the
            // caller has opted into a fixed inspector-driven size.
            Camera cam = Camera.main;
            if (cam != null && !useFixedPanelSize)
            {
                float fovRadians = cam.fieldOfView * Mathf.Deg2Rad;
                float heightAtDistance = 2f * distanceFromCamera * Mathf.Tan(fovRadians / 2f);
                float widthAtDistance = heightAtDistance * cam.aspect;

                // Fill 85% of FOV for comfortable viewing
                panelWidth = widthAtDistance * 0.85f / uiScale;
                panelHeight = heightAtDistance * 0.85f / uiScale;

                Debug.Log($"[CalibrationUI] FOV-based size: {panelWidth}x{panelHeight} at {distanceFromCamera}m");
            }
            else if (useFixedPanelSize)
            {
                Debug.Log($"[CalibrationUI] Fixed panel size: {panelWidth}x{panelHeight} (FOV auto-resize skipped)");
            }

            canvasRect.sizeDelta = new Vector2(panelWidth, panelHeight);
            canvasRect.localScale = Vector3.one * uiScale;

            // Add raycaster for VR interaction
            canvasObj.AddComponent<GraphicRaycaster>();

            Debug.Log($"[CalibrationUI] Canvas created - Size: {panelWidth}x{panelHeight}, Scale: {uiScale}");

            // Create main panel
            CreateMainPanel();

            // Create progress panel
            CreateProgressPanel();

            // Create dedicated countdown panel (small centered panel with
            // large digits) so calibration targets remain visible around it.
            CreateCountdownPanel();

            // Hide panels initially
            HideAll();
        }

        private void CreateMainPanel()
        {
            // Main panel fills the canvas
            mainPanel = CreatePanel("MainPanel", canvasRect);
            RectTransform mainRect = mainPanel.GetComponent<RectTransform>();
            mainRect.anchorMin = Vector2.zero;
            mainRect.anchorMax = Vector2.one;
            mainRect.offsetMin = Vector2.zero;
            mainRect.offsetMax = Vector2.zero;

            // Add background image
            Image bg = mainPanel.AddComponent<Image>();
            bg.color = backgroundColor;

            // === MANUAL POSITIONING (no layout groups) ===

            // Title - top 12% of panel.
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(mainPanel.transform, false);
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.86f);
            titleRect.anchorMax = new Vector2(1f, 0.98f);
            titleRect.offsetMin = new Vector2(48f, 0f);
            titleRect.offsetMax = new Vector2(-48f, 0f);

            titleText = titleObj.AddComponent<Text>();
            titleText.text = "Calibration";
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (titleText.font == null) titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 56;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = titleColor;
            titleText.alignment = TextAnchor.MiddleCenter;

            // Thin warm-amber accent line under the title; visual only.
            GameObject stripeObj = new GameObject("AccentStripe");
            stripeObj.transform.SetParent(mainPanel.transform, false);
            RectTransform stripeRect = stripeObj.AddComponent<RectTransform>();
            stripeRect.anchorMin = new Vector2(0.30f, 0.845f);
            stripeRect.anchorMax = new Vector2(0.70f, 0.855f);
            stripeRect.offsetMin = Vector2.zero;
            stripeRect.offsetMax = Vector2.zero;
            Image stripe = stripeObj.AddComponent<Image>();
            stripe.color = accentStripeColor;

            // Instructions - middle area. Bottom anchor leaves visible
            // breathing room between body text and the button row.
            GameObject instructionObj = new GameObject("Instructions");
            instructionObj.transform.SetParent(mainPanel.transform, false);
            RectTransform instrRect = instructionObj.AddComponent<RectTransform>();
            instrRect.anchorMin = new Vector2(0f, 0.32f);
            instrRect.anchorMax = new Vector2(1f, 0.84f);
            instrRect.offsetMin = new Vector2(48f, 0f);
            instrRect.offsetMax = new Vector2(-48f, 0f);

            instructionText = instructionObj.AddComponent<Text>();
            instructionText.text = "";
            instructionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (instructionText.font == null) instructionText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            instructionText.fontSize = 32;
            instructionText.color = textColor;
            instructionText.alignment = TextAnchor.UpperLeft;

            // Button panel - bottom of panel; taller band fits bigger
            // buttons with horizontal padding so they sit toward the
            // left/right edges instead of crowding the center.
            buttonPanel = new GameObject("ButtonPanel");
            buttonPanel.transform.SetParent(mainPanel.transform, false);
            RectTransform buttonPanelRect = buttonPanel.AddComponent<RectTransform>();
            buttonPanelRect.anchorMin = new Vector2(0f, 0.04f);
            buttonPanelRect.anchorMax = new Vector2(1f, 0.24f);
            buttonPanelRect.offsetMin = new Vector2(60f, 0f);
            buttonPanelRect.offsetMax = new Vector2(-60f, 0f);

            // HorizontalLayoutGroup for centered placement; spacing keeps
            // the two buttons clearly apart.
            HorizontalLayoutGroup buttonLayout = buttonPanel.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.spacing = 180f;
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonLayout.childControlWidth = false;
            buttonLayout.childControlHeight = false;

            // Start button
            startButton = CreateButton("StartButton", buttonPanel.transform, "START", OnStartButtonClicked);

            // Next button
            nextButton = CreateButton("NextButton", buttonPanel.transform, "NEXT", OnNextButtonClicked);
        }

        private void CreateProgressPanel()
        {
            // Position at BOTTOM of screen so it doesn't block calibration targets
            progressPanel = CreatePanel("ProgressPanel", canvasRect);
            RectTransform progRect = progressPanel.GetComponent<RectTransform>();
            progRect.anchorMin = new Vector2(0.1f, 0f);
            progRect.anchorMax = new Vector2(0.9f, 0.15f);
            progRect.offsetMin = Vector2.zero;
            progRect.offsetMax = Vector2.zero;

            // Semi-transparent background
            Image bg = progressPanel.AddComponent<Image>();
            bg.color = new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 0.7f);

            // Vertical layout
            VerticalLayoutGroup layout = progressPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 10, 10);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            // Progress text (smaller for compact panel)
            GameObject progressTextObj = CreateTextObject("ProgressText", progressPanel.transform, "Test in Progress", 28, titleColor);
            progressText = progressTextObj.GetComponent<Text>();
            LayoutElement textLayout = progressTextObj.AddComponent<LayoutElement>();
            textLayout.preferredHeight = 35f;

            // Progress bar background
            GameObject barBg = CreatePanel("ProgressBarBg", progressPanel.transform);
            Image barBgImage = barBg.AddComponent<Image>();
            barBgImage.color = new Color(0.2f, 0.2f, 0.25f);
            LayoutElement barLayout = barBg.AddComponent<LayoutElement>();
            barLayout.preferredHeight = 20f;

            // Progress bar fill
            GameObject barFill = CreatePanel("ProgressBarFill", barBg.transform);
            progressBar = barFill.AddComponent<Image>();
            progressBar.color = progressBarColor;
            progressBarFill = barFill.GetComponent<RectTransform>();
            progressBarFill.anchorMin = Vector2.zero;
            progressBarFill.anchorMax = new Vector2(0f, 1f);
            progressBarFill.offsetMin = Vector2.zero;
            progressBarFill.offsetMax = Vector2.zero;

            progressPanel.SetActive(false);
        }

        private void CreateCountdownPanel()
        {
            // Small centered panel sized for big digits. Anchored at canvas
            // center, ~32% width / 38% height so it stays compact and doesn't
            // sprawl across the calibration targets.
            countdownPanel = CreatePanel("CountdownPanel", canvasRect);
            RectTransform rect = countdownPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.34f, 0.31f);
            rect.anchorMax = new Vector2(0.66f, 0.69f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image bg = countdownPanel.AddComponent<Image>();
            bg.color = new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 0.92f);

            // Single big centered digit / "Go!" string.
            GameObject textObj = new GameObject("CountdownText");
            textObj.transform.SetParent(countdownPanel.transform, false);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 20f);
            textRect.offsetMax = new Vector2(-20f, -20f);

            countdownText = textObj.AddComponent<Text>();
            countdownText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (countdownText.font == null)
            {
                countdownText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            countdownText.fontSize = 220;
            countdownText.color = titleColor;
            countdownText.alignment = TextAnchor.MiddleCenter;
            countdownText.horizontalOverflow = HorizontalWrapMode.Overflow;
            countdownText.verticalOverflow = VerticalWrapMode.Overflow;
            countdownText.text = "";

            countdownPanel.SetActive(false);
        }

        private GameObject CreatePanel(string name, Transform parent)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return panel;
        }

        private GameObject CreateTextObject(string name, Transform parent, string text, int fontSize, Color color)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);

            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text textComponent = textObj.AddComponent<Text>();
            textComponent.text = text;
            textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (textComponent.font == null)
            {
                textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            textComponent.fontSize = fontSize;
            textComponent.color = color;
            textComponent.alignment = TextAnchor.MiddleCenter;
            textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComponent.verticalOverflow = VerticalWrapMode.Overflow;

            return textObj;
        }

        private Button CreateButton(string name, Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObj = new GameObject(name);
            buttonObj.transform.SetParent(parent, false);

            RectTransform rect = buttonObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 130f);

            Image image = buttonObj.AddComponent<Image>();
            image.color = buttonColor;

            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            // Inset darker rectangle for a 'card' feel without a 9-sliced
            // sprite asset.
            GameObject borderObj = new GameObject("InnerBorder");
            borderObj.transform.SetParent(buttonObj.transform, false);
            RectTransform borderRect = borderObj.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(4f, 4f);
            borderRect.offsetMax = new Vector2(-4f, -4f);
            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.color = new Color(buttonColor.r * 0.85f, buttonColor.g * 0.85f, buttonColor.b * 0.85f, 1f);
            borderImg.raycastTarget = false;

            // Button text (on top of inner border)
            GameObject textObj = CreateTextObject("Text", buttonObj.transform, text, 36, Color.white);
            Text buttonText = textObj.GetComponent<Text>();
            buttonText.fontStyle = FontStyle.Bold;

            // Dwell indicator: inset border + thick fill ring using
            // progressBarColor so the affordance reads as 'progress toward
            // selection'.
            GameObject dwellObj = new GameObject("DwellIndicator");
            dwellObj.transform.SetParent(buttonObj.transform, false);
            RectTransform dwellRect = dwellObj.AddComponent<RectTransform>();
            dwellRect.anchorMin = Vector2.zero;
            dwellRect.anchorMax = Vector2.one;
            dwellRect.offsetMin = new Vector2(-12f, -12f);
            dwellRect.offsetMax = new Vector2(12f, 12f);

            Image dwellImage = dwellObj.AddComponent<Image>();
            dwellImage.color = new Color(progressBarColor.r, progressBarColor.g, progressBarColor.b, 0.85f);
            dwellImage.type = Image.Type.Filled;
            dwellImage.fillMethod = Image.FillMethod.Radial360;
            dwellImage.fillOrigin = (int)Image.Origin360.Top;
            dwellImage.fillAmount = 0f;
            dwellImage.raycastTarget = false;
            dwellObj.SetActive(false);

            // Register button for gaze tracking
            buttonImages[button] = image;
            dwellIndicators[button] = dwellImage;

            return button;
        }

        private bool isPositioned = false;

        // Cached IRoomFrameProvider so PositionUI doesn't FindObjectsByType
        // every placement; resolved lazily on first call.
        private Transform cachedRoomFrame;

        /// <summary>
        /// Find the active <see cref="EyeTracking.Core.IRoomFrameProvider"/>
        /// in the scene (cached after first resolve). Returns the room's
        /// transform when one is present, null otherwise — MainMenu and
        /// other room-less scenes get a null result and fall back to
        /// camera-derived placement.
        /// </summary>
        private Transform ResolveRoomFrame()
        {
            if (cachedRoomFrame != null && cachedRoomFrame) return cachedRoomFrame;
            var providers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < providers.Length; i++)
            {
                if (providers[i] is EyeTracking.Core.IRoomFrameProvider rfp && rfp.RoomTransform != null)
                {
                    cachedRoomFrame = rfp.RoomTransform;
                    return cachedRoomFrame;
                }
            }
            return null;
        }

        // Suppression flag for the MainMenu single-placement path. Set true by
        // ForceWorldSpaceMode; cleared by EnforceWorldSpaceModeForSeconds once
        // it has performed its single stability-anchored placement. While true,
        // PositionUI calls that are NOT coming from the enforcer early-return
        // — preventing the visible "jumps" caused by ShowInstructions and
        // PositionUIWhenReady racing the enforcer's stable placement.
        private bool worldSpaceEnforcerOwnsPlacement = false;
        private bool _inEnforcerPlacement = false;

        private void PositionUI()
        {
            // MainMenu single-placement guard: while the enforcer coroutine is
            // running, only let it call PositionUI. Eager calls from other
            // call sites would place at the pre-stability head pose and the
            // enforcer's later placement would visibly jump.
            if (worldSpaceEnforcerOwnsPlacement && !_inEnforcerPlacement) return;
            if (isPositioned) return;

            // Try to find camera if not found yet
            if (cameraTransform == null)
            {
                FindCamera();
            }

            if (cameraTransform == null)
            {
                Debug.LogError("[CalibrationUI] PositionUI - No camera found!");
                return;
            }

            // Wait for VR to initialize (camera not at origin)
            Vector3 camPos = cameraTransform.position;
            if (camPos.magnitude < 0.1f)
            {
                Debug.Log($"[CalibrationUI] Waiting for VR to initialize, camPos: {camPos}");
                StartCoroutine(DelayedPositionUI());
                return;
            }

            if (parentToCamera)
            {
                // Parent canvas to camera - UI follows head rotation
                canvas.transform.SetParent(cameraTransform, false);
                canvas.transform.localPosition = new Vector3(0, verticalOffset, distanceFromCamera);
                canvas.transform.localRotation = Quaternion.identity;

                Debug.Log($"[CalibrationUI] UI PARENTED to camera at local position: (0, {verticalOffset}, {distanceFromCamera})");
            }
            else
            {
                // World-space positioning. Prefer the room frame so the
                // panel sits parallel to the back wall and its facing
                // direction does NOT track the participant's head heading
                // at placement time. Camera-derived yaw-only is the
                // fallback for scenes without an IRoomFrameProvider
                // (MainMenu, smoke tests).
                Vector3 fwd;
                bool roomAnchored = false;
                Transform roomFrame = ResolveRoomFrame();
                if (roomFrame != null)
                {
                    fwd = roomFrame.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                    fwd.Normalize();
                    roomAnchored = true;
                }
                else
                {
                    // Camera fallback: yaw-only forward (head-tilt
                    // independent for roll, but panel still faces the
                    // participant's heading at placement).
                    fwd = cameraTransform.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                    fwd.Normalize();
                }

                // Position uses camera XZ so the panel appears in front of
                // wherever the participant is standing — only orientation
                // is detached from head heading. Y from camera so the
                // panel sits at eye level.
                Vector3 targetPos = camPos + fwd * distanceFromCamera;
                targetPos.y = camPos.y + verticalOffset;
                Quaternion targetRot = Quaternion.LookRotation(fwd, Vector3.up);

                canvas.transform.position = targetPos;
                canvas.transform.rotation = targetRot;

                Debug.Log($"[CalibrationUI] UI positioned in world space at: {targetPos} (anchor={(roomAnchored ? "room-frame" : "camera-yaw")})");
            }

            isPositioned = true;
        }

        private System.Collections.IEnumerator DelayedPositionUI()
        {
            yield return new WaitForSeconds(0.2f);
            PositionUI();
        }

        /// <summary>
        /// Force reposition the UI (useful after scene changes or if camera changes).
        /// </summary>
        public void RepositionUI()
        {
            isPositioned = false;
            PositionUI();
        }

        #region Event Handlers

        private void OnStartButtonClicked()
        {
            OnStartClicked?.Invoke();
        }

        private void OnNextButtonClicked()
        {
            OnNextClicked?.Invoke();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Show instructions with title and content.
        /// </summary>
        public void ShowInstructions(string title, string content)
        {
            Debug.Log($"[CalibrationUI] ShowInstructions called: {title}");
            HideAll();
            titleText.text = title;

            // Add gaze interaction hint
            string interactionHint = "";
            if (useGazeSelection)
            {
                interactionHint = $"\n\n[Look at button for {dwellTime:F1}s to select]";
            }

            instructionText.text = content + interactionHint;
            mainPanel.SetActive(true);
            canvas.gameObject.SetActive(true);
            PositionUI();
        }

        /// <summary>
        /// Text-only update — does NOT call HideAll or PositionUI. Use
        /// during countdowns / score updates where the panel is already
        /// shown and shouldn't flicker or snap to a new spot.
        /// </summary>
        public void SetInstructionsTextOnly(string title, string content)
        {
            if (mainPanel != null && !mainPanel.activeSelf) mainPanel.SetActive(true);
            if (canvas != null && !canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
            if (titleText != null) titleText.text = title;
            if (instructionText != null) instructionText.text = content;
        }

        /// <summary>
        /// Show or hide start/next buttons. Optional <paramref name="startLabel"/>
        /// and <paramref name="nextLabel"/> override the button text per-screen
        /// (e.g. "Save &amp; Verify" / "Don't Save" on the Tuning prompt). Pass
        /// null to keep the previous text — the button widgets persist across
        /// phase transitions, so a null here leaves whatever the last call set.
        /// </summary>
        public void ShowButtons(bool showStart, bool showNext, string startLabel = null, string nextLabel = null)
        {
            startButton.gameObject.SetActive(showStart);
            nextButton.gameObject.SetActive(showNext);
            buttonPanel.SetActive(showStart || showNext);
            if (startLabel != null) SetButtonLabel(startButton, startLabel);
            if (nextLabel != null) SetButtonLabel(nextButton, nextLabel);
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }

        /// <summary>
        /// Show the progress bar.
        /// </summary>
        public void ShowProgressBar(string testName, float duration)
        {
            HideAll();
            progressText.text = $"{testName}\nTime remaining: {duration:F0}s";
            progressBarFill.anchorMax = new Vector2(0f, 1f);
            progressPanel.SetActive(true);
            canvas.gameObject.SetActive(true);
            PositionUI();
        }

        /// <summary>
        /// Update the progress bar.
        /// </summary>
        public void UpdateProgress(float progress, float remainingTime)
        {
            if (progressPanel.activeInHierarchy)
            {
                progressBarFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
                string testName = progressText.text.Split('\n')[0];
                progressText.text = $"{testName}\nTime remaining: {remainingTime:F0}s";
            }
        }

        /// <summary>
        /// Hide the progress bar.
        /// </summary>
        public void HideProgressBar()
        {
            progressPanel.SetActive(false);
        }

        /// <summary>
        /// Show the dedicated countdown panel with a single big centered
        /// glyph (e.g. "3", "2", "1", "Go!"). This is intentionally NOT the
        /// full instruction panel — small centered presentation matches the
        /// SampleExperiment scene and keeps calibration targets visible
        /// around it.
        /// </summary>
        public void ShowCountdown(string text)
        {
            HideAll();
            if (countdownText != null) countdownText.text = text;
            if (countdownPanel != null) countdownPanel.SetActive(true);
            if (canvas != null) canvas.gameObject.SetActive(true);
            PositionUI();
        }

        /// <summary>
        /// Update the countdown digit without re-positioning the panel.
        /// </summary>
        public void SetCountdownText(string text)
        {
            if (countdownPanel != null && !countdownPanel.activeSelf) countdownPanel.SetActive(true);
            if (canvas != null && !canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
            if (countdownText != null) countdownText.text = text;
        }

        /// <summary>
        /// Hide the countdown panel.
        /// </summary>
        public void HideCountdown()
        {
            if (countdownPanel != null) countdownPanel.SetActive(false);
        }

        /// <summary>
        /// Hide all UI panels.
        /// </summary>
        public void HideAll()
        {
            mainPanel.SetActive(false);
            progressPanel.SetActive(false);
            if (countdownPanel != null) countdownPanel.SetActive(false);
        }

        /// <summary>
        /// Show/hide the entire UI.
        /// </summary>
        public void SetVisible(bool visible)
        {
            canvas.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Whether the UI is currently visible.
        /// </summary>
        public bool IsVisible => canvas.gameObject.activeInHierarchy;

        #endregion
    }
}
