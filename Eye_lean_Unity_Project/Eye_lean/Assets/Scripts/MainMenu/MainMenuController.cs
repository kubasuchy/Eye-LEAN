// SPDX-License-Identifier: MIT
using UnityEngine;
using UnityEngine.SceneManagement;
using EyeTracking.Core;

namespace EyeLean.MainMenu
{
    [DefaultExecutionOrder(-100)]
    public class MainMenuController : MonoBehaviour
    {
        [Header("Scene Names (must match Build Settings)")]
        [SerializeField] private string calibratorSceneName = "CalibrationScene";

        [System.Serializable]
        public struct ExperimentEntry
        {
            [Tooltip("Scene name in Build Settings.")]
            public string sceneName;
            [Tooltip("Label shown in the MainMenu panel.")]
            public string displayName;
        }

        [Header("Experiment scenes")]
        [SerializeField] private ExperimentEntry[] experiments = new ExperimentEntry[]
        {
            new ExperimentEntry { sceneName = "SampleExperiment", displayName = "Sample Experiment" },
            new ExperimentEntry { sceneName = "NBackScene",       displayName = "Working Memory (N-back)" },
            new ExperimentEntry { sceneName = "MazeScene",        displayName = "Navigation (Maze)" },
        };

        private MainMenuPanel panel;
        private bool sceneLoadInFlight;

        void Start()
        {
            var legacyUI = FindFirstObjectByType<EyeTracking.Calibration.UI.CalibrationWorldUI>();
            if (legacyUI != null)
            {
                legacyUI.gameObject.SetActive(false);
                Debug.Log("[MainMenu] Disabled legacy CalibrationWorldUI — using MainMenuPanel instead.");
            }

            panel = gameObject.AddComponent<MainMenuPanel>();

            string[] labels = new string[1 + experiments.Length];
            labels[0] = "Calibrator";
            for (int i = 0; i < experiments.Length; i++)
                labels[i + 1] = experiments[i].displayName;

            panel.Build("Eye-LEAN", BuildStatusLine(), labels);
            panel.OnButtonActivated += HandleButtonActivated;

            StartCoroutine(RefreshStatusWhenTrackerReady());
        }

        private System.Collections.IEnumerator RefreshStatusWhenTrackerReady()
        {
            var readiness = VRReadinessService.Instance;
            if (readiness != null) yield return readiness.WaitForCameraReady(8f);

            EyeTrackerFactory.Reinitialize();
            panel.SetSubtitle(BuildStatusLine());
        }

        private string BuildStatusLine()
        {
            var tracker = EyeTrackerFactory.GetEyeTracker();
            bool available = tracker != null && tracker.IsAvailable;
            string device = available ? tracker.DeviceName : "not detected";

            var profile = EyeTracking.Configuration.ActiveProfile.Current;
            string profileName = (profile != null && profile.combinedGaze != null)
                ? profile.metadata.profileName
                : (available ? "none — run Calibrator first" : "n/a");

            return $"Device: {device}  |  Profile: {profileName}";
        }

        void OnDestroy()
        {
            if (panel != null) panel.OnButtonActivated -= HandleButtonActivated;
        }

        private void HandleButtonActivated(int index)
        {
            if (sceneLoadInFlight) return;

            if (index == 0)
            {
                LoadScene(calibratorSceneName);
            }
            else
            {
                int expIdx = index - 1;
                if (expIdx >= 0 && expIdx < experiments.Length)
                    LoadScene(experiments[expIdx].sceneName);
            }
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
