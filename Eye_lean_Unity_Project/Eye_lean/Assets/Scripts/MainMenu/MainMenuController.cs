using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using EyeTracking.Core;
using EyeTracking.Calibration.UI;

namespace EyeLean.MainMenu
{
    /// <summary>
    /// Launcher scene for the Eye_lean APK. Lets the researcher pick
    /// between the calibrator and the sample experiment without rebuilding.
    /// Reuses CalibrationWorldUI's dwell-button system so the gesture
    /// vocabulary matches the calibrator's Tuning prompt. START launches
    /// the calibrator, NEXT launches the sample experiment. Replay and the
    /// Skeleton template are not exposed: replay is editor-only and the
    /// Skeleton is a developer-side scene template, not a build target.
    /// </summary>
    // DefaultExecutionOrder(-100) so this controller's Start runs before
    // CalibrationWorldUI's. ForceWorldSpaceMode sets the
    // worldSpaceEnforcerOwnsPlacement guard before CalibrationWorldUI's
    // PositionUIWhenReady coroutine fires PositionUI; on scene re-entry
    // VRReadinessService reports camera-ready immediately, and without
    // this ordering the eager placement would land on an unstable head
    // pose and the enforcer's stability re-place would jump the panel.
    [DefaultExecutionOrder(-100)]
    public class MainMenuController : MonoBehaviour
    {
        [Header("Scene Names (must match Build Settings)")]
        [SerializeField] private string calibratorSceneName = "CalibrationScene";
        [SerializeField] private string experimentSceneName = "SampleExperiment";

        [Header("UI")]
        [Tooltip("Reference to the CalibrationWorldUI in this scene. Auto-found if null.")]
        [SerializeField] private CalibrationWorldUI worldUI;

        private bool sceneLoadInFlight = false;

        void Start()
        {
            if (worldUI == null)
            {
                worldUI = FindObjectOfType<CalibrationWorldUI>();
            }
            if (worldUI == null)
            {
                Debug.LogError("[MainMenu] CalibrationWorldUI not found in scene. Add one to display the launcher.");
                return;
            }

            // The MainMenu uses CalibrationWorldUI.parentToCamera = true
            // for a HUD-locked panel; if a leftover MenuHeadAnchor is
            // present, disabling it here keeps it from fighting the parent
            // transform.
            var staleAnchor = worldUI.GetComponent<MenuHeadAnchor>();
            if (staleAnchor != null && staleAnchor.enabled)
            {
                staleAnchor.enabled = false;
                Debug.Log("[MainMenu] Disabled MenuHeadAnchor (HUD-locked mode is in effect).");
            }

            worldUI.OnStartClicked += HandleCalibratorClicked;
            worldUI.OnNextClicked += HandleExperimentClicked;

            // Force world-space mode so head rotation moves the user's gaze
            // relative to the buttons. The combined-gaze stream isn't
            // reliably warmed up at MainMenu start (~5-10s on hardware), so
            // HUD-locked + gaze-dwell would deadlock. World-space lets the
            // user disambiguate Start vs Next by turning their head before
            // the eye tracker is delivering fresh data.
            worldUI.ForceWorldSpaceMode();

            // Synchronous IsAvailable() in Start() races OpenXREyeTracker's
            // own Start. Defer the status read until VRReadinessService
            // confirms the camera is tracking. Show a placeholder
            // synchronously so the panel isn't blank during the wait.
            string title = "Eye_lean";
            worldUI.ShowInstructions(title, BuildStatusBody(false, null) + "\n\nDetecting eye tracker...");
            worldUI.ShowButtons(showStart: true, showNext: true);
            StartCoroutine(RefreshStatusWhenTrackerReady(title));
        }

        private IEnumerator RefreshStatusWhenTrackerReady(string title)
        {
            var readiness = VRReadinessService.Instance;
            if (readiness != null) yield return readiness.WaitForCameraReady(8f);

            // EyeTrackerFactory caches NullEyeTracker if the OpenXR provider
            // isn't ready when first queried, and once cached it stays
            // cached. Camera-ready means the provider's Start has fired;
            // invalidate the poisoned cache before re-querying so the panel
            // doesn't read "Eye tracker not detected" with a NullEyeTracker
            // when the hardware is fine. EyeTrackerFactoryBootstrapper
            // handles this on scene change but not on the first scene's
            // Start window.
            EyeTrackerFactory.Reinitialize();

            var tracker = EyeTrackerFactory.GetEyeTracker();
            bool trackerAvailable = tracker != null && tracker.IsAvailable;
            Debug.Log($"[MainMenu] Tracker status (post-readiness): device={tracker?.DeviceName ?? "None"}, available={trackerAvailable}");
            worldUI.ShowInstructions(title, BuildStatusBody(trackerAvailable, tracker?.DeviceName));
            worldUI.ShowButtons(showStart: true, showNext: true);
        }

        void OnDestroy()
        {
            if (worldUI != null)
            {
                worldUI.OnStartClicked -= HandleCalibratorClicked;
                worldUI.OnNextClicked -= HandleExperimentClicked;
            }
        }

        /// <summary>
        /// Render the status block: tracker availability, active profile,
        /// and a recommended next action. Both buttons stay enabled so the
        /// researcher can override the suggestion.
        /// </summary>
        private string BuildStatusBody(bool trackerAvailable, string deviceName)
        {
            // Keep body short. With gazeAngleThreshold=15°, more than ~3
            // lines of body push the dwell-buttons past ~25° below the
            // panel midline — outside the gaze cone, unreachable.
            var profile = EyeTracking.Configuration.ActiveProfile.Current;
            string deviceLine = trackerAvailable
                ? $"<b>Device:</b> {deviceName}"
                : "<b>Device:</b> not detected";
            string profileLine = (profile != null && profile.combinedGaze != null)
                ? $"<b>Profile:</b> {profile.metadata.profileName}"
                : (trackerAvailable
                    ? "<b>Profile:</b> none — run Calibrator first"
                    : "<b>Profile:</b> n/a");

            return "VR Eye-Tracking Toolkit • v1.0\n\n"
                 + deviceLine + "\n"
                 + profileLine + "\n\n"
                 + "<color=#7DC8FF>[START]</color> Calibrator     <color=#7DC8FF>[NEXT]</color> Sample Experiment";
        }

        private void HandleCalibratorClicked()
        {
            LoadScene(calibratorSceneName);
        }

        private void HandleExperimentClicked()
        {
            LoadScene(experimentSceneName);
        }

        private void LoadScene(string sceneName)
        {
            if (sceneLoadInFlight) return;
            sceneLoadInFlight = true;

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[MainMenu] Scene name is empty — check MainMenuController serialized fields.");
                sceneLoadInFlight = false;
                return;
            }

            Debug.Log($"[MainMenu] Loading scene: {sceneName}");
            SceneManager.LoadScene(sceneName);
        }
    }
}
