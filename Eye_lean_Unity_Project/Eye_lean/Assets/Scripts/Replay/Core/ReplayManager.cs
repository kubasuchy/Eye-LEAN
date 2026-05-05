using UnityEngine;
using System.Collections.Generic;
using System.IO;
using EyeLean.Replay.Analysis;
using EyeLean.Replay.Visualization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EyeLean.Replay
{
    /// <summary>
    /// High-level coordinator for Eye_lean replay. Owns the
    /// <see cref="ReplayController"/>, file resolution, and playback
    /// filtering. Researcher scripts opt out of running during replay by
    /// gating on <c>EyeLean.Replay.SceneState.ReplayMode.IsActive</c>
    /// (set automatically by the controller's state machine). Environment
    /// generation is the researcher's responsibility — open the same scene
    /// used to record. Demo-environment recreation is opt-in via
    /// <c>DemoReplayBootstrapper</c> in the EyeLean.SampleDemo asmdef.
    /// </summary>
    public class ReplayManager : MonoBehaviour
    {
        #region Data Source

        [Header("Data Source")]
        [Tooltip("Path to the CSV file to replay. Can be absolute or relative to project folder.")]
        public string csvFilePath = "";

        [Tooltip("Auto-load the CSV file on Start")]
        public bool autoLoadOnStart = false;

        #endregion

        #region Playback Filtering

        [Header("Playback Filtering")]
        [Tooltip("Play entire recording or filter by phase/subtask")]
        public PlaybackScope playbackScope = PlaybackScope.EntireRecording;

        [Tooltip("Phase to filter by (when scope is SpecificPhase or SpecificSubtask)")]
        public string filterPhase = "";

        [Tooltip("Subtask to filter by (when scope is SpecificSubtask)")]
        public string filterSubtask = "";

        public enum PlaybackScope
        {
            EntireRecording,    // Play all frames
            SpecificPhase,      // Play only frames matching filterPhase
            SpecificSubtask     // Play only frames matching filterPhase AND filterSubtask
        }

        #endregion

        #region References

        [Header("Shared Components (from scene)")]
        [Tooltip("Reference to camera transform for positioning")]
        public Transform cameraTransform;

        [Header("Replay Components (auto-created if not set)")]
        [Tooltip("Reference to ReplayController")]
        public ReplayController replayController;

        [Tooltip("Eye movement classifier for replay analysis")]
        public EyeMovementClassifier classifier;

        [Tooltip("Gaze entropy calculator")]
        public GazeEntropyCalculator entropyCalculator;

        [Tooltip("Replay visualizer")]
        public ReplayVisualizer visualizer;

        #endregion

        #region Settings

        [Header("Settings")]
        [Tooltip("Auto-create analysis components on Start")]
        public bool autoCreateComponents = true;

        [Tooltip("Show debug messages")]
        public bool debugMode = true;

        #endregion

        #region Runtime State

        // Available phases/subtasks from loaded data (populated after loading)
        [HideInInspector] public List<string> availablePhases = new List<string>();
        [HideInInspector] public List<string> availableSubtasks = new List<string>();

        #endregion

        #region Events

        public System.Action<ReplaySession> OnDataLoaded;
        public System.Action<string> OnLoadError;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            FindSharedComponents();
            CreateReplayComponents();
        }

        private void Start()
        {
            if (autoLoadOnStart && !string.IsNullOrEmpty(csvFilePath))
            {
                LoadData();
            }
        }

        // ReplayManager does not disable specific experiment controllers;
        // researcher scripts opt out by gating Update on
        // ReplayMode.IsActive. See docs/REPLAY_SCENE_STATE.md.

        #endregion

        #region Component Setup

        private void FindSharedComponents()
        {
            if (cameraTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) cameraTransform = cam.transform;
            }
        }

        private void CreateReplayComponents()
        {
            if (!autoCreateComponents)
                return;

            // Create ReplayController
            if (replayController == null)
            {
                replayController = GetComponent<ReplayController>();
                if (replayController == null)
                {
                    replayController = gameObject.AddComponent<ReplayController>();
                    replayController.autoLoadOnStart = false;
                    replayController.debugMode = debugMode;
                }
            }

            // Create classifier
            if (classifier == null)
            {
                classifier = GetComponent<EyeMovementClassifier>();
                if (classifier == null)
                {
                    classifier = gameObject.AddComponent<EyeMovementClassifier>();
                }
            }

            // Create entropy calculator
            if (entropyCalculator == null)
            {
                entropyCalculator = GetComponent<GazeEntropyCalculator>();
                if (entropyCalculator == null)
                {
                    entropyCalculator = gameObject.AddComponent<GazeEntropyCalculator>();
                }
            }

            // Create visualizer
            if (visualizer == null)
            {
                visualizer = GetComponent<ReplayVisualizer>();
                if (visualizer == null)
                {
                    visualizer = gameObject.AddComponent<ReplayVisualizer>();
                    visualizer.replayController = replayController;
                    visualizer.classifier = classifier;
                    visualizer.entropyCalculator = entropyCalculator;
                }
            }

            // Subscribe to replay events
            if (replayController != null)
            {
                replayController.OnLoadComplete += HandleDataLoaded;
                replayController.OnLoadError += HandleLoadError;
            }
        }

        #endregion

        #region Data Loading

        /// <summary>
        /// Load the CSV file specified in csvFilePath
        /// </summary>
        public void LoadData()
        {
            if (string.IsNullOrEmpty(csvFilePath))
            {
                Debug.LogError("[ReplayManager] No CSV file path specified");
                OnLoadError?.Invoke("No CSV file path specified");
                return;
            }

            string fullPath = ResolveFilePath(csvFilePath);

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[ReplayManager] File not found: {fullPath}");
                OnLoadError?.Invoke($"File not found: {fullPath}");
                return;
            }

            if (debugMode)
            {
                Debug.Log($"[ReplayManager] Loading: {fullPath}");
            }

            replayController?.LoadData(fullPath);
        }

        /// <summary>
        /// Load a specific CSV file
        /// </summary>
        public void LoadData(string path)
        {
            csvFilePath = path;
            LoadData();
        }

        private string ResolveFilePath(string path)
        {
            // If absolute path, use as-is
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            // Try relative to project folder first
            string projectPath = Path.Combine(Application.dataPath, "..", path);
            if (File.Exists(projectPath))
            {
                return Path.GetFullPath(projectPath);
            }

            // Try relative to StreamingAssets
            string streamingPath = Path.Combine(Application.streamingAssetsPath, path);
            if (File.Exists(streamingPath))
            {
                return streamingPath;
            }

            // Try relative to persistent data path (where HMD saves data)
            string persistentPath = Path.Combine(Application.persistentDataPath, path);
            if (File.Exists(persistentPath))
            {
                return persistentPath;
            }

            // Return original path and let it fail with proper error message
            return path;
        }

        private void HandleDataLoaded(ReplaySession session)
        {
            if (debugMode)
            {
                Debug.Log($"[ReplayManager] Loaded {session.totalFrames} frames, duration: {session.totalDuration:F2}s");
            }

            PopulateFilterOptions(session);
            ApplyPlaybackFilter();

            OnDataLoaded?.Invoke(session);
        }

        private void HandleLoadError(string error)
        {
            Debug.LogError($"[ReplayManager] Load error: {error}");
            OnLoadError?.Invoke(error);
        }

        #endregion

        #region Playback Filtering

        private void PopulateFilterOptions(ReplaySession session)
        {
            availablePhases.Clear();
            availableSubtasks.Clear();

            HashSet<string> phases = new HashSet<string>();
            HashSet<string> subtasks = new HashSet<string>();

            foreach (var frame in session.frames)
            {
                if (!string.IsNullOrEmpty(frame.phase))
                    phases.Add(frame.phase);
                if (!string.IsNullOrEmpty(frame.subTask))
                    subtasks.Add(frame.subTask);
            }

            availablePhases.AddRange(phases);
            availableSubtasks.AddRange(subtasks);

            availablePhases.Sort();
            availableSubtasks.Sort();

            if (debugMode)
            {
                Debug.Log($"[ReplayManager] Available phases: {string.Join(", ", availablePhases)}");
                Debug.Log($"[ReplayManager] Available subtasks: {string.Join(", ", availableSubtasks)}");
            }
        }

        private void ApplyPlaybackFilter()
        {
            if (replayController == null || replayController.Session == null)
                return;

            // Set filter on controller
            switch (playbackScope)
            {
                case PlaybackScope.EntireRecording:
                    replayController.SetPlaybackFilter(null, null);
                    break;

                case PlaybackScope.SpecificPhase:
                    replayController.SetPlaybackFilter(filterPhase, null);
                    break;

                case PlaybackScope.SpecificSubtask:
                    replayController.SetPlaybackFilter(filterPhase, filterSubtask);
                    break;
            }
        }

        /// <summary>
        /// Set the playback filter at runtime
        /// </summary>
        public void SetFilter(PlaybackScope scope, string phase = null, string subtask = null)
        {
            playbackScope = scope;
            filterPhase = phase ?? "";
            filterSubtask = subtask ?? "";
            ApplyPlaybackFilter();

            if (debugMode)
            {
                Debug.Log($"[ReplayManager] Filter changed to: {playbackScope}, Phase='{filterPhase}', Subtask='{filterSubtask}'");
            }
        }

        /// <summary>
        /// Play entire recording (clear filter)
        /// </summary>
        public void PlayEntireRecording()
        {
            SetFilter(PlaybackScope.EntireRecording);
            Play();
        }

        /// <summary>
        /// Play only a specific phase
        /// </summary>
        public void PlayPhase(string phaseName)
        {
            SetFilter(PlaybackScope.SpecificPhase, phaseName);
            Play();
        }

        /// <summary>
        /// Play only a specific subtask within a phase
        /// </summary>
        public void PlaySubtask(string phaseName, string subtaskName)
        {
            SetFilter(PlaybackScope.SpecificSubtask, phaseName, subtaskName);
            Play();
        }

        #endregion

        #region Playback Control

        public void Play()
        {
            replayController?.Play();
        }

        public void Pause()
        {
            replayController?.Pause();
        }

        public void Stop()
        {
            replayController?.Stop();
        }

        public void TogglePlayPause()
        {
            replayController?.TogglePlayPause();
        }

        public bool IsPlaying => replayController != null && replayController.IsPlaying;
        public bool IsReady => replayController != null && replayController.IsReady;

        #endregion

        #region Editor Helpers

        #if UNITY_EDITOR
        /// <summary>
        /// Open file browser to select CSV file (Editor only)
        /// </summary>
        [ContextMenu("Browse for CSV File...")]
        public void BrowseForFile()
        {
            string startPath = string.IsNullOrEmpty(csvFilePath)
                ? Application.dataPath
                : Path.GetDirectoryName(csvFilePath);

            string selectedPath = EditorUtility.OpenFilePanel("Select Eye Tracking CSV", startPath, "csv");

            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Make path relative to project if possible
                if (selectedPath.StartsWith(Application.dataPath))
                {
                    selectedPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                }

                csvFilePath = selectedPath;
                EditorUtility.SetDirty(this);

                Debug.Log($"[ReplayManager] Selected file: {csvFilePath}");
            }
        }

        [ContextMenu("Load Selected File")]
        public void EditorLoadFile()
        {
            LoadData();
        }
        #endif

        #endregion
    }

    #if UNITY_EDITOR
    /// <summary>
    /// Custom editor for ReplayManager with file browser button
    /// </summary>
    [CustomEditor(typeof(ReplayManager))]
    public class ReplayManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            ReplayManager manager = (ReplayManager)target;

            // Draw default inspector
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            // File browser button
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse for CSV File...", GUILayout.Height(25)))
            {
                manager.BrowseForFile();
            }
            EditorGUILayout.EndHorizontal();

            // Load button (only in play mode)
            if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Load Data", GUILayout.Height(25)))
                {
                    manager.LoadData();
                }

                GUI.enabled = manager.IsReady;
                if (GUILayout.Button(manager.IsPlaying ? "Pause" : "Play", GUILayout.Height(25)))
                {
                    manager.TogglePlayPause();
                }
                if (GUILayout.Button("Stop", GUILayout.Height(25)))
                {
                    manager.Stop();
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                // Show available phases/subtasks after loading
                if (manager.availablePhases.Count > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Available Phases:", EditorStyles.boldLabel);
                    foreach (var phase in manager.availablePhases)
                    {
                        EditorGUILayout.LabelField($"  • {phase}");
                    }
                }

                if (manager.availableSubtasks.Count > 0)
                {
                    EditorGUILayout.LabelField("Available Subtasks:", EditorStyles.boldLabel);
                    foreach (var subtask in manager.availableSubtasks)
                    {
                        EditorGUILayout.LabelField($"  • {subtask}");
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Enter Play mode to load and replay data.\n\n" +
                    "Researcher scripts opt out of running during replay by checking " +
                    "EyeLean.Replay.SceneState.ReplayMode.IsActive in their Update().\n" +
                    "Environment generation is the researcher's responsibility — open the " +
                    "same scene used to record. Demo-environment recreation is opt-in via " +
                    "the DemoReplayBootstrapper component (EyeLean.SampleDemo).",
                    MessageType.Info);
            }
        }
    }
    #endif
}
