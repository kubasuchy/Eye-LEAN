/*
 * OpenXREyeTracker.cs
 *
 * OpenXR eye-tracking wrapper for HTC VIVE devices, exposing a Wave-SDK-style
 * API on top of XR_HTC_eye_tracker (per-eye gaze, pupil, geometric data) and
 * XR_EXT_eye_gaze_interaction (combined gaze via Unity Input System).
 *
 * Requirements:
 * - Unity OpenXR Plugin (com.unity.xr.openxr)
 * - VIVE OpenXR Plugin (com.htc.upm.vive.openxr)
 * - OpenXR features enabled: VIVE XR Eye Tracker (Beta), VIVE XR Facial Tracking
 */

#if USE_OPENXR

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

// VIVE OpenXR APIs
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;
using VIVE.OpenXR.FacialTracking;

// Profile-based per-user calibration
using EyeTracking.Configuration;
using EyeTracking.Core;

namespace VRNavigation.Tracking
{
    /// <summary>
    /// OpenXR eye-tracking wrapper providing a Wave-SDK-compatible interface
    /// on top of the VIVE OpenXR plugin. Implements ISceneTransitionAware so
    /// cached scene-bound transforms (Camera.main, its parent rig) are nulled
    /// before the prior scene's OnDestroy commits the destruction flag — without
    /// this, the next-frame Update would NRE inside TransformPoint.
    /// </summary>
    public class OpenXREyeTracker : MonoBehaviour, ISceneTransitionAware
    {
        #region Singleton Pattern

        private static OpenXREyeTracker instance;
        public static OpenXREyeTracker Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("OpenXREyeTracker");
                    instance = go.AddComponent<OpenXREyeTracker>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        #endregion

        #region Configuration

        [Header("Eye Tracking Configuration")]
        [Tooltip("Enable/disable eye tracking (matches Wave SDK EnableEyeTracking property)")]
        [SerializeField] private bool enableEyeTracking = true;

        [Header("OpenXR Input Actions")]
        [Tooltip("Reference to combined eye gaze input action (XR_EXT_eye_gaze_interaction).")]
        public InputActionReference eyeGazeAction;

        [Header("Debug Options")]
        [Tooltip("Enable debug logging for OpenXR eye tracking.")]
        public bool debugLogging = false;

        #endregion

        #region Public Properties (Wave SDK Compatibility)

        /// <summary>
        /// Enable or disable eye tracking.
        /// </summary>
        public bool EnableEyeTracking
        {
            get => enableEyeTracking;
            set
            {
                enableEyeTracking = value;
                if (debugLogging)
                    Debug.Log($"[OpenXREyeTracker] Eye tracking {(value ? "enabled" : "disabled")}");
            }
        }

        #endregion

        #region Private State

        // VIVE OpenXR Feature References
        private ViveEyeTracker viveEyeTrackerFeature;
        private ViveFacialTracking viveFacialTrackingFeature;

        // Combined gaze data (from XR_EXT_eye_gaze_interaction via Input System)
        private Pose combinedGazePose;
        private bool combinedGazeValid = false;

        // Per-eye data (from VIVE XR Eye Tracker extension)
        private Vector3 leftEyeOrigin = Vector3.zero;
        private Vector3 leftEyeDirection = Vector3.forward;
        private float leftEyeOpenness = 1.0f;
        private float leftPupilDiameter = 4.0f; // mm
        private Vector2 leftPupilPosition = Vector2.zero;
        private bool leftEyeValid = false;

        private Vector3 rightEyeOrigin = Vector3.zero;
        private Vector3 rightEyeDirection = Vector3.forward;
        private float rightEyeOpenness = 1.0f;
        private float rightPupilDiameter = 4.0f; // mm
        private Vector2 rightPupilPosition = Vector2.zero;
        private bool rightEyeValid = false;

        // System status
        private bool isInitialized = false;
        private bool eyeTrackingAvailable = false;

        // Tracking-space → world-space anchor. VIVE OpenXR returns gaze pose
        // in the play-area / tracking reference; the camera rig's parent
        // (XR Origin / CameraOffset) is the transform Unity uses to position
        // tracked content in the world. Using the parent rather than the
        // camera itself means the transform is independent of the user's
        // actual head position. Resolved lazily and re-resolved when null
        // (scene reload, camera replaced).
        private Transform trackingSpaceTransform;

        // Head-local anchor for per-eye and combined-gaze profile corrections
        // (see ActiveProfile). Resolved alongside trackingSpaceTransform.
        private Transform headTransform;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            // Null cached scene-bound transforms before the prior scene destroys them.
            SceneTransitionCoordinator.Instance.Register(this);
        }

        private void Start()
        {
            InitializeEyeTracking();
            TryLoadDefaultProfile();
        }

        private void Update()
        {
            if (!enableEyeTracking || !isInitialized) return;

            UpdatePerEyeData();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                if (SceneTransitionCoordinator.Instance != null)
                {
                    SceneTransitionCoordinator.Instance.Unregister(this);
                }
                instance = null;
            }
        }

        /// <summary>
        /// ISceneTransitionAware. Nulls cached scene-bound transforms so the
        /// lazy resolver in UpdatePerEyeData re-fetches from the new scene's
        /// Camera.main. Without this, the prior scene's transforms still pass
        /// Unity's `==` alive check until destruction commits, and the next
        /// Update's TransformPoint/TransformDirection calls would NRE.
        /// </summary>
        public void OnSceneWillUnload(Scene from)
        {
            trackingSpaceTransform = null;
            headTransform = null;
        }

        public void OnSceneDidLoad(Scene to)
        {
            // Lazy resolver in UpdatePerEyeData picks up the new Camera.main.
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize OpenXR eye tracking system with VIVE extensions
        /// </summary>
        private void InitializeEyeTracking()
        {
            Debug.Log("[OpenXREyeTracker] === INITIALIZATION STARTING ===");

            // Check if OpenXR is active
            if (!OpenXRSettings.Instance)
            {
                Debug.LogError("[OpenXREyeTracker] ❌ OpenXRSettings.Instance is null! OpenXR not initialized.");
                isInitialized = false;
                return;
            }

            Debug.Log("[OpenXREyeTracker] ✅ OpenXRSettings.Instance found");

            // Get VIVE XR Eye Tracker feature
            viveEyeTrackerFeature = OpenXRSettings.Instance.GetFeature<ViveEyeTracker>();
            if (viveEyeTrackerFeature == null || !viveEyeTrackerFeature.enabled)
            {
                Debug.LogWarning("[OpenXREyeTracker] VIVE XR Eye Tracker feature not found or not enabled!");
                Debug.LogWarning("[OpenXREyeTracker] Enable it in: Edit > Project Settings > XR Plug-in Management > OpenXR > VIVE XR Eye Tracker (Beta)");
            }
            else
            {
                if (debugLogging)
                    Debug.Log("[OpenXREyeTracker] ✅ VIVE XR Eye Tracker feature found and enabled");
            }

            // Get VIVE XR Facial Tracking feature (for eye openness data)
            viveFacialTrackingFeature = OpenXRSettings.Instance.GetFeature<ViveFacialTracking>();
            if (viveFacialTrackingFeature == null || !viveFacialTrackingFeature.enabled)
            {
                Debug.LogWarning("[OpenXREyeTracker] VIVE XR Facial Tracking feature not found or not enabled!");
                Debug.LogWarning("[OpenXREyeTracker] Enable it in: Edit > Project Settings > XR Plug-in Management > OpenXR > VIVE XR Facial Tracking");
            }
            else
            {
                if (debugLogging)
                    Debug.Log("[OpenXREyeTracker] ✅ VIVE XR Facial Tracking feature found and enabled");
            }

            if (eyeGazeAction == null)
            {
                var eyeGazeAsset = UnityEngine.Resources.Load<InputActionAsset>("XREyeGaze");
                if (eyeGazeAsset != null)
                {
                    var gazeAction = eyeGazeAsset.FindAction("Eye Tracking/Gaze Pose");
                    if (gazeAction != null)
                    {
                        eyeGazeAction = InputActionReference.Create(gazeAction);
                        eyeGazeAction.action.Enable();
                        Debug.Log("[OpenXREyeTracker] ✅ Loaded XREyeGaze Input Action asset automatically");
                    }
                }

                if (eyeGazeAction == null)
                {
                    Debug.LogWarning("[OpenXREyeTracker] Eye gaze input action not assigned and could not be loaded.");
                    Debug.LogWarning("[OpenXREyeTracker] Eye tracking will not work without Input Action or direct VIVE API implementation.");
                }
            }
            else
            {
                eyeGazeAction.action.Enable();
                if (debugLogging)
                    Debug.Log("[OpenXREyeTracker] ✅ Eye gaze input action enabled");
            }

            eyeTrackingAvailable = (viveEyeTrackerFeature != null && viveEyeTrackerFeature.enabled) ||
                                   (eyeGazeAction != null && eyeGazeAction.action.enabled);

            isInitialized = true;

            Debug.Log($"[OpenXREyeTracker] === INITIALIZATION COMPLETE ===");
            Debug.Log($"[OpenXREyeTracker] Eye tracking available: {eyeTrackingAvailable}");
            Debug.Log($"[OpenXREyeTracker] VIVE Eye Tracker feature: {(viveEyeTrackerFeature != null && viveEyeTrackerFeature.enabled ? "ENABLED" : "DISABLED")}");
            Debug.Log($"[OpenXREyeTracker] VIVE Facial Tracking feature: {(viveFacialTrackingFeature != null && viveFacialTrackingFeature.enabled ? "ENABLED" : "DISABLED")}");
            Debug.Log($"[OpenXREyeTracker] Eye Gaze Input Action: {(eyeGazeAction != null ? "ASSIGNED" : "NULL")}");
        }

        #endregion

        #region Data Update Methods

        /// <summary>
        /// Update combined eye gaze from Unity Input System (XR_EXT_eye_gaze_interaction).
        /// </summary>
        private void UpdateCombinedGaze()
        {
            if (eyeGazeAction != null && eyeGazeAction.action.enabled)
            {
                try
                {
                    // PoseState (Unity 6+) exposes the isTracked flag.
                    var gaze = eyeGazeAction.action.ReadValue<UnityEngine.InputSystem.XR.PoseState>();
                    combinedGazePose = new Pose(gaze.position, gaze.rotation);
                    combinedGazeValid = gaze.isTracked;

                    if (debugLogging && Time.frameCount % 90 == 0) // Log once per second at 90fps
                    {
                        Debug.Log($"[OpenXREyeTracker] Combined gaze: pos={combinedGazePose.position}, rot={combinedGazePose.rotation.eulerAngles}, tracked={combinedGazeValid}");
                    }
                }
                catch (System.Exception e)
                {
                    combinedGazeValid = false;
                    if (Time.frameCount % 300 == 0)
                    {
                        Debug.LogWarning($"[OpenXREyeTracker] Failed to read combined gaze: {e.Message}");
                    }
                }
            }
            else
            {
                combinedGazeValid = false;
                if (Time.frameCount % 300 == 0)
                {
                    Debug.LogWarning($"[OpenXREyeTracker] Eye gaze action is null or disabled - cannot read combined gaze");
                }
            }
        }

        /// <summary>
        /// Update per-eye data via XR_HTC_eye_tracker.Interop (gaze, pupil,
        /// geometric data) and synthesize the combined gaze.
        /// </summary>
        private void UpdatePerEyeData()
        {
            const int LEFT = (int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC;
            const int RIGHT = (int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC;

            // Lazy (re)resolve of the tracking-space and head anchors. If the
            // rig has no parent (camera at world root) the anchor stays null
            // and TrackingTo*World* methods become identity passthroughs.
            if (!IsTransformAlive(trackingSpaceTransform) || !IsTransformAlive(headTransform))
            {
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    headTransform = mainCam.transform;
                    trackingSpaceTransform = mainCam.transform.parent;
                    if (debugLogging)
                    {
                        Debug.Log(IsTransformAlive(trackingSpaceTransform)
                            ? $"[OpenXREyeTracker] Tracking-space anchor: {trackingSpaceTransform.name} (Camera.main.parent); head: {headTransform.name}"
                            : "[OpenXREyeTracker] Camera.main has no parent; eye poses treated as world-space (no transform applied)");
                    }
                }
            }

            try
            {
                // The native API populates a 2-element array (left, right);
                // guard length to avoid IndexOutOfRangeException on a
                // misbehaving runtime.
                XR_HTC_eye_tracker.Interop.GetEyeGazeData(out XrSingleEyeGazeDataHTC[] out_gazes);
                if (out_gazes != null && out_gazes.Length > RIGHT)
                {
                    XrSingleEyeGazeDataHTC leftGaze = out_gazes[LEFT];
                    if (leftGaze.isValid)
                    {
                        Vector3 rawOriginL = leftGaze.gazePose.position.ToUnityVector();
                        Vector3 rawDirL = leftGaze.gazePose.orientation.ToUnityQuaternion() * Vector3.forward;
                        leftEyeOrigin = TrackingToWorldPoint(rawOriginL);
                        Vector3 worldDirL = SafeNormalize(TrackingToWorldDirection(rawDirL), Vector3.forward);
                        // Apply per-eye profile correction BEFORE combined-gaze averaging
                        // so the average reflects corrected per-eye geometry.
                        leftEyeDirection = ApplyPerEyeProfileCorrection(worldDirL, isLeftEye: true);
                        leftEyeValid = true;
                    }
                    else
                    {
                        leftEyeValid = false;
                    }

                    XrSingleEyeGazeDataHTC rightGaze = out_gazes[RIGHT];
                    if (rightGaze.isValid)
                    {
                        Vector3 rawOriginR = rightGaze.gazePose.position.ToUnityVector();
                        Vector3 rawDirR = rightGaze.gazePose.orientation.ToUnityQuaternion() * Vector3.forward;
                        rightEyeOrigin = TrackingToWorldPoint(rawOriginR);
                        Vector3 worldDirR = SafeNormalize(TrackingToWorldDirection(rawDirR), Vector3.forward);
                        rightEyeDirection = ApplyPerEyeProfileCorrection(worldDirR, isLeftEye: false);
                        rightEyeValid = true;
                    }
                    else
                    {
                        rightEyeValid = false;
                    }
                }
                else
                {
                    leftEyeValid = false;
                    rightEyeValid = false;
                }

                // Pupil data
                XR_HTC_eye_tracker.Interop.GetEyePupilData(out XrSingleEyePupilDataHTC[] out_pupils);
                if (out_pupils != null && out_pupils.Length > RIGHT)
                {
                    XrSingleEyePupilDataHTC leftPupil = out_pupils[LEFT];
                    if (leftPupil.isDiameterValid)
                    {
                        leftPupilDiameter = leftPupil.pupilDiameter;
                    }
                    if (leftPupil.isPositionValid)
                    {
                        leftPupilPosition = new Vector2(leftPupil.pupilPosition.x, leftPupil.pupilPosition.y);
                    }

                    XrSingleEyePupilDataHTC rightPupil = out_pupils[RIGHT];
                    if (rightPupil.isDiameterValid)
                    {
                        rightPupilDiameter = rightPupil.pupilDiameter;
                    }
                    if (rightPupil.isPositionValid)
                    {
                        rightPupilPosition = new Vector2(rightPupil.pupilPosition.x, rightPupil.pupilPosition.y);
                    }
                }

                // Geometric data (eye openness)
                XR_HTC_eye_tracker.Interop.GetEyeGeometricData(out XrSingleEyeGeometricDataHTC[] out_geometrics);
                if (out_geometrics != null && out_geometrics.Length > RIGHT)
                {
                    XrSingleEyeGeometricDataHTC leftGeometric = out_geometrics[LEFT];
                    if (leftGeometric.isValid)
                    {
                        leftEyeOpenness = leftGeometric.eyeOpenness;
                    }

                    XrSingleEyeGeometricDataHTC rightGeometric = out_geometrics[RIGHT];
                    if (rightGeometric.isValid)
                    {
                        rightEyeOpenness = rightGeometric.eyeOpenness;
                    }
                }

                // Combined gaze from individual eyes. SafeLookRotation drops
                // the valid flag on zero-magnitude input rather than letting
                // Quaternion.LookRotation silently return identity (which
                // would mask data corruption downstream).
                //
                // Combined-gaze profile correction targets residual bias
                // common to both eyes (e.g., headset seating tilt) on top
                // of any per-eye corrections already applied above.
                if (leftEyeValid && rightEyeValid)
                {
                    combinedGazePose.position = (leftEyeOrigin + rightEyeOrigin) * 0.5f;
                    Vector3 combinedDirection = (leftEyeDirection + rightEyeDirection) * 0.5f;
                    combinedDirection = ApplyCombinedProfileCorrection(combinedDirection);
                    combinedGazeValid = TrySetCombinedGazeRotation(combinedDirection);
                }
                else if (leftEyeValid)
                {
                    combinedGazePose.position = leftEyeOrigin;
                    Vector3 corrected = ApplyCombinedProfileCorrection(leftEyeDirection);
                    combinedGazeValid = TrySetCombinedGazeRotation(corrected);
                }
                else if (rightEyeValid)
                {
                    combinedGazePose.position = rightEyeOrigin;
                    Vector3 corrected = ApplyCombinedProfileCorrection(rightEyeDirection);
                    combinedGazeValid = TrySetCombinedGazeRotation(corrected);
                }
                else
                {
                    combinedGazeValid = false;
                }
            }
            catch (System.Exception e)
            {
                if (debugLogging && Time.frameCount % 300 == 0)
                {
                    Debug.LogWarning($"[OpenXREyeTracker] Failed to get eye tracking data: {e.Message}");
                }
                leftEyeValid = false;
                rightEyeValid = false;
                combinedGazeValid = false;
            }

            if (debugLogging && Time.frameCount % 300 == 0)
            {
                Debug.Log($"[OpenXREyeTracker] Frame {Time.frameCount} - VIVE API Data:");
                Debug.Log($"[OpenXREyeTracker]   Left eye valid: {leftEyeValid}, Right eye valid: {rightEyeValid}");
                Debug.Log($"[OpenXREyeTracker]   Combined gaze valid: {combinedGazeValid}");
                if (leftEyeValid)
                {
                    Debug.Log($"[OpenXREyeTracker]   Left: origin={leftEyeOrigin}, dir={leftEyeDirection}, openness={leftEyeOpenness:F2}");
                }
                if (rightEyeValid)
                {
                    Debug.Log($"[OpenXREyeTracker]   Right: origin={rightEyeOrigin}, dir={rightEyeDirection}, openness={rightEyeOpenness:F2}");
                }
            }
        }

        /// <summary>
        /// Normalize, falling back to a sentinel direction when the input has
        /// zero magnitude (Vector3.zero.normalized is Vector3.zero in Unity,
        /// which would propagate as a degenerate direction downstream).
        /// </summary>
        private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            return v.sqrMagnitude > 1e-12f ? v.normalized : fallback;
        }

        /// <summary>
        /// Convert a position from VIVE tracking-space to Unity world-space.
        /// Tracking-space is anchored at the play-area reference; the camera
        /// rig's parent (CameraOffset / XR Origin) is the same anchor in
        /// Unity coordinates, so applying its world transform is the correct
        /// mapping. If no anchor is available the input is returned unchanged
        /// (works for rigs placed at world origin with no offset).
        /// </summary>
        private Vector3 TrackingToWorldPoint(Vector3 trackingSpacePos)
        {
            return IsTransformAlive(trackingSpaceTransform)
                ? trackingSpaceTransform.TransformPoint(trackingSpacePos)
                : trackingSpacePos;
        }

        /// <summary>
        /// Convert a direction from VIVE tracking-space to Unity world-space.
        /// Uses TransformDirection (rotation only — translation does not
        /// apply to directions). Same fallback semantics as TrackingToWorldPoint.
        /// </summary>
        private Vector3 TrackingToWorldDirection(Vector3 trackingSpaceDir)
        {
            return IsTransformAlive(trackingSpaceTransform)
                ? trackingSpaceTransform.TransformDirection(trackingSpaceDir)
                : trackingSpaceDir;
        }

        /// <summary>
        /// Defensive null check that returns false for both real null AND for
        /// "fake null" (Unity-overloaded ==) AND for the destroyed-but-still-
        /// referenced case. Plain <c>t != null</c> alone misses the third case
        /// during the window between scene unload and the destroyed-flag commit.
        /// </summary>
        private static bool IsTransformAlive(Transform t)
        {
            return (object)t != null && t != null;
        }

        /// <summary>
        /// Apply the active profile's per-eye correction to a world-space
        /// gaze direction. Internally converts world → head-local, applies
        /// the correction, converts back. When no profile is active or the
        /// per-eye fields are at their identity defaults the result is
        /// mathematically equal to the input.
        /// </summary>
        private Vector3 ApplyPerEyeProfileCorrection(Vector3 worldDir, bool isLeftEye)
        {
            if (!IsTransformAlive(headTransform)) return worldDir;
            Vector3 localDir = headTransform.InverseTransformDirection(worldDir);
            Vector3 correctedLocal = ActiveProfile.ApplyPerEyeCorrection(localDir, isLeftEye);
            return headTransform.TransformDirection(correctedLocal);
        }

        /// <summary>
        /// Apply the active profile's combined-gaze correction to a
        /// world-space combined direction. See ApplyPerEyeProfileCorrection
        /// for the head-local conversion rationale.
        /// </summary>
        private Vector3 ApplyCombinedProfileCorrection(Vector3 worldDir)
        {
            if (!IsTransformAlive(headTransform)) return worldDir;
            Vector3 localDir = headTransform.InverseTransformDirection(worldDir);
            Vector3 correctedLocal = ActiveProfile.ApplyCombinedCorrection(localDir);
            return headTransform.TransformDirection(correctedLocal);
        }

        /// <summary>
        /// Try to load the default profile from disk. Called once at Start
        /// (after InitializeEyeTracking). If the file doesn't exist or is
        /// invalid this is a silent no-op so first-run sessions still work.
        /// </summary>
        private void TryLoadDefaultProfile()
        {
            string path = System.IO.Path.Combine(
                EyeTrackingProfile.DefaultDirectory,
                EyeTrackingProfile.DefaultProfileFileName);
            if (!System.IO.File.Exists(path)) return;

            try
            {
                EyeTrackingProfile profile = EyeTrackingProfile.Load(path);
                ActiveProfile.Apply(profile);
                Debug.Log($"[OpenXREyeTracker] Auto-loaded default profile: {profile.metadata.profileName}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[OpenXREyeTracker] Default profile at {path} could not be loaded: {e.Message}");
            }
        }

        /// <summary>
        /// Set combinedGazePose.rotation from a direction vector iff the
        /// direction has non-zero magnitude. Returns false if the rotation
        /// could not be derived, so the caller can mark the combined sample
        /// invalid rather than silently storing identity.
        /// </summary>
        private bool TrySetCombinedGazeRotation(Vector3 direction)
        {
            if (direction.sqrMagnitude <= 1e-12f)
            {
                return false;
            }
            combinedGazePose.rotation = Quaternion.LookRotation(direction.normalized);
            return true;
        }

        #endregion

        #region Public API (Wave SDK Compatible Interface)

        /// <summary>
        /// Check if eye tracking is available on this device
        /// </summary>
        public bool IsEyeTrackingAvailable()
        {
            return eyeTrackingAvailable && isInitialized;
        }

        // ===== COMBINED EYE DATA =====

        /// <summary>
        /// Get combined eye gaze origin in world space
        /// </summary>
        public bool GetCombinedEyeOrigin(out Vector3 origin)
        {
            origin = combinedGazePose.position;
            return combinedGazeValid;
        }

        /// <summary>
        /// Get combined eye gaze direction (normalized) in world space
        /// </summary>
        public bool GetCombinedEyeDirectionNormalized(out Vector3 direction)
        {
            direction = (combinedGazePose.rotation * Vector3.forward).normalized;
            return combinedGazeValid;
        }

        // ===== LEFT EYE DATA =====

        /// <summary>
        /// Get left eye origin in world space (Wave SDK compatible)
        /// </summary>
        public bool GetLeftEyeOrigin(out Vector3 origin)
        {
            origin = leftEyeOrigin;
            return leftEyeValid && enableEyeTracking;
        }

        /// <summary>
        /// Get left eye gaze direction normalized (Wave SDK compatible)
        /// </summary>
        public bool GetLeftEyeDirectionNormalized(out Vector3 direction)
        {
            direction = leftEyeDirection.normalized;
            return leftEyeValid && enableEyeTracking;
        }

        /// <summary>
        /// Get left eye openness value 0-1 (Wave SDK compatible)
        /// Uses VIVE XR_HTC_eye_tracker.Interop.GetEyeGeometricData for actual data
        /// </summary>
        public bool GetLeftEyeOpenness(out float openness)
        {
            openness = leftEyeOpenness;
            return leftEyeValid && enableEyeTracking;
        }

        /// <summary>
        /// Get left pupil diameter in millimeters (Wave SDK compatible)
        /// Uses VIVE XR_HTC_eye_tracker.Interop.GetEyePupilData for actual data
        /// </summary>
        public bool GetLeftEyePupilDiameter(out float diameter)
        {
            diameter = leftPupilDiameter;
            return leftEyeValid && enableEyeTracking;
        }

        /// <summary>
        /// Get left pupil position in sensor area 0-1 normalized (Wave SDK compatible)
        /// Uses VIVE XR_HTC_eye_tracker.Interop.GetEyePupilData for actual data
        /// </summary>
        public bool GetLeftEyePupilPositionInSensorArea(out Vector2 position)
        {
            position = leftPupilPosition;
            return leftEyeValid && enableEyeTracking;
        }

        // ===== RIGHT EYE DATA =====

        /// <summary>
        /// Get right eye origin in world space (Wave SDK compatible)
        /// </summary>
        public bool GetRightEyeOrigin(out Vector3 origin)
        {
            origin = rightEyeOrigin;
            return rightEyeValid && enableEyeTracking;
        }

        /// <summary>
        /// Get right eye gaze direction normalized (Wave SDK compatible)
        /// </summary>
        public bool GetRightEyeDirectionNormalized(out Vector3 direction)
        {
            direction = rightEyeDirection.normalized;
            return rightEyeValid && enableEyeTracking;
        }

        /// <summary>
        /// Get right eye openness value 0-1 (Wave SDK compatible)
        /// Uses VIVE XR_HTC_eye_tracker.Interop.GetEyeGeometricData for actual data
        /// </summary>
        public bool GetRightEyeOpenness(out float openness)
        {
            openness = rightEyeOpenness;
            return rightEyeValid && enableEyeTracking;
        }

        /// <summary>
        /// Get right pupil diameter in millimeters (Wave SDK compatible)
        /// Uses VIVE XR_HTC_eye_tracker.Interop.GetEyePupilData for actual data
        /// </summary>
        public bool GetRightEyePupilDiameter(out float diameter)
        {
            diameter = rightPupilDiameter;
            return rightEyeValid && enableEyeTracking;
        }

        /// <summary>
        /// Get right pupil position in sensor area 0-1 normalized (Wave SDK compatible)
        /// Uses VIVE XR_HTC_eye_tracker.Interop.GetEyePupilData for actual data
        /// </summary>
        public bool GetRightEyePupilPositionInSensorArea(out Vector2 position)
        {
            position = rightPupilPosition;
            return rightEyeValid && enableEyeTracking;
        }

        #endregion

        #region Developer Tools

        /// <summary>
        /// Get diagnostic information about eye tracking status
        /// </summary>
        public string GetDiagnosticInfo()
        {
            string info = "OpenXR Eye Tracking Status:\n";
            info += $"- Initialized: {isInitialized}\n";
            info += $"- Available: {eyeTrackingAvailable}\n";
            info += $"- Enabled: {enableEyeTracking}\n";
            info += $"- Combined Gaze Valid: {combinedGazeValid}\n";
            info += $"- Left Eye Valid: {leftEyeValid}\n";
            info += $"- Right Eye Valid: {rightEyeValid}\n";
            info += $"- Input Action Assigned: {eyeGazeAction != null}\n";
            info += $"- Input Action Enabled: {eyeGazeAction?.action.enabled ?? false}\n";
            info += $"\nVIVE Features:\n";
            info += $"- Eye Tracker Feature: {(viveEyeTrackerFeature != null && viveEyeTrackerFeature.enabled ? "✅ Enabled" : "❌ Not Available")}\n";
            info += $"- Facial Tracking Feature: {(viveFacialTrackingFeature != null && viveFacialTrackingFeature.enabled ? "✅ Enabled" : "❌ Not Available")}\n";
            info += $"\nData Source:\n";
            info += $"- Currently using: VIVE XR_HTC_eye_tracker.Interop API (direct native calls)\n";
            info += $"- Per-eye gaze: GetEyeGazeData()\n";
            info += $"- Pupil data: GetEyePupilData()\n";
            info += $"- Eye openness: GetEyeGeometricData()\n";
            return info;
        }

        #endregion
    }
}

#endif // USE_OPENXR
