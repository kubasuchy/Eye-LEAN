using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections;
using System.IO;

namespace EyeLean.Replay
{
    /// <summary>
    /// Main controller for replaying Eye_lean eye tracking recordings.
    /// Handles loading, playback control, and visualization. Execution
    /// order is set well below zero so this MonoBehaviour's Awake runs
    /// before any researcher script's Awake; when autoLoadOnStart is true
    /// and a data file is configured, ReplayMode.Begin() is called from
    /// Awake so researcher scripts that gate on
    /// <see cref="EyeLean.Replay.SceneState.ReplayMode.IsActive"/> see
    /// the flag set and short-circuit before doing side-effecting work.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class ReplayController : MonoBehaviour
    {
        #region Inspector Settings

        [Header("Data Source")]
        [Tooltip("CSV file path (relative to StreamingAssets or absolute path)")]
        public string dataFilePath = "";

        [Tooltip("Auto-load data on Start")]
        public bool autoLoadOnStart = false;

        [Tooltip("Print debug information")]
        public bool debugMode = true;

        [Header("Playback Settings")]
        [Tooltip("Playback speed multiplier (1.0 = real-time)")]
        [Range(0.1f, 5.0f)]
        public float playbackSpeed = 1.0f;

        [Tooltip("Loop playback when reaching end")]
        public bool loopPlayback = false;

        [Tooltip("Auto-start playback after loading")]
        public bool autoPlayOnLoad = false;

        [Tooltip("Enable frame interpolation for smoother playback")]
        public bool enableInterpolation = true;

        [Header("Deterministic Replay")]
        [Tooltip("ON: pin Time.captureDeltaTime to the recorded per-frame delta and inject Camera.main pose + IEyeTracker reads from the recording, so the live experiment's coroutines re-execute deterministically against playback inputs. OFF (legacy): only gaze rays + scene state replay; the live experiment is disabled.")]
        public bool deterministicReplay = true;

        [Header("Visualization Settings")]
        [Tooltip("Show avatar representation")]
        public bool showAvatar = true;

        [Tooltip("Show eye gaze rays")]
        public bool showGazeRays = true;

        [Tooltip("Show vergence point")]
        public bool showVergencePoint = true;

        [Tooltip("Gaze ray length in meters")]
        public float gazeRayLength = 10f;

        [Tooltip("Avatar (head) sphere size")]
        public float avatarSize = 0.2f;

        [Tooltip("Vergence point sphere size")]
        public float vergencePointSize = 0.08f;

        [Header("Eye Visibility")]
        [Tooltip("Force eyes always visible (ignore openness)")]
        public bool forceEyesVisible = true;

        [Tooltip("Eye openness threshold for visibility")]
        [Range(0.1f, 0.5f)]
        public float eyeOpennessThreshold = 0.2f;

        [Header("Camera Mode")]
        [Tooltip("ON (default): drive Camera.main with the recorded head pose every frame so the operator sees what the participant saw. OFF: leave the camera alone and just position the avatar sphere — third-person view.")]
        public bool firstPersonCamera = true;

        [Tooltip("Optional override. If null, uses Camera.main on Start. Auto-resolved if the cached camera dies (e.g. scene reload).")]
        public Transform replayCameraTransform;

        [Tooltip("Hide the avatar sphere when the first-person camera is active so the camera isn't sitting inside it. Has no effect if firstPersonCamera is OFF.")]
        public bool hideAvatarInFirstPerson = true;

        [Header("Ray Origin Mode")]
        [Tooltip("ON (default): draw rays from headPos ± rotation*right * lateralOffset, matching the live EyeTracker viz-offset trick. Immunizes Replay against the OpenXR coord-frame race that occasionally drops a frame's per-eye origin into tracking-space (floor-level) instead of world-space. OFF: use the raw LeftOrigin/RightOrigin values from the CSV — useful for raw-data debugging.")]
        public bool useHeadAnchoredRayOrigin = true;

        [Tooltip("Lateral offset (meters) from head center for head-anchored ray origins. 0.04 ≈ half the typical 70mm IPD. Mirrors the live EyeTracker's VizOriginLateralOffsetMeters constant (0.08m, but applied from camera position rather than head pos so the visual spacing differs).")]
        [Range(0f, 0.1f)]
        public float headAnchoredLateralOffset = 0.04f;

        [Tooltip("Warn when a recorded eye origin is more than this many meters away from the recorded head pos (likely coord-frame race). Set to 0 to disable. Logged at most once per warningThrottleFrames frames.")]
        public float originDeviationWarningMeters = 0.3f;

        [Tooltip("Throttle for the per-frame coord-frame-race warning above. 60 = roughly once per second at 60 fps.")]
        public int warningThrottleFrames = 60;

        [Header("Colors")]
        public Color leftEyeColor = Color.red;
        public Color rightEyeColor = Color.blue;
        public Color vergenceColor = Color.yellow;
        public Color avatarColor = new Color(0.2f, 0.6f, 0.2f, 1f); // Green for head

        #endregion

        #region Events

        /// <summary>Fired when loading state changes</summary>
        public event Action<ReplayState> OnStateChanged;

        /// <summary>Fired when a new frame is displayed</summary>
        public event Action<ReplayFrame> OnFrameDisplayed;

        /// <summary>Fired when playback reaches the end</summary>
        public event Action OnPlaybackComplete;

        /// <summary>Fired when loading completes</summary>
        public event Action<ReplaySession> OnLoadComplete;

        /// <summary>Fired on loading error</summary>
        public event Action<string> OnLoadError;

        /// <summary>Fired when phase changes during playback</summary>
        public event Action<string> OnPhaseChanged;

        #endregion

        #region Public Properties

        /// <summary>Current replay state</summary>
        public ReplayState State => currentState;

        /// <summary>Current session data (null if not loaded)</summary>
        public ReplaySession Session => session;

        /// <summary>Current frame index</summary>
        public int CurrentFrameIndex => currentFrameIndex;

        /// <summary>
        /// Current frame data (null until playback advances at least once).
        /// Used by deterministic-replay consumers — <see cref="ReplayingEyeTracker"/>
        /// reads recorded gaze samples from here.
        /// </summary>
        public ReplayFrame CurrentFrame => currentFrame;

        /// <summary>Current playback time in seconds</summary>
        public float CurrentTime => currentPlaybackTime;

        /// <summary>Total duration in seconds (filtered if filter is active)</summary>
        public float TotalDuration => filteredFrames != null ? filteredDuration : (session?.totalDuration ?? 0f);

        /// <summary>Total frame count (filtered if filter is active)</summary>
        public int TotalFrames => filteredFrames != null ? filteredFrames.Count : (session?.totalFrames ?? 0);

        /// <summary>Current progress (0-1)</summary>
        public float Progress => TotalFrames > 0 ? (float)currentFrameIndex / TotalFrames : 0f;

        /// <summary>Is a filter currently active</summary>
        public bool HasActiveFilter => filterPhase != null || filterSubtask != null;

        /// <summary>Current phase name</summary>
        public string CurrentPhase => currentPhase;

        /// <summary>Is data loaded and ready for playback</summary>
        public bool IsReady => currentState == ReplayState.Ready ||
                               currentState == ReplayState.Playing ||
                               currentState == ReplayState.Paused ||
                               currentState == ReplayState.Complete;

        /// <summary>Is currently playing</summary>
        public bool IsPlaying => currentState == ReplayState.Playing;

        #endregion

        #region Private Fields

        private ReplayState currentState = ReplayState.Uninitialized;
        private ReplaySession session;
        private EyeLeanCSVParser parser;

        // Playback control
        private int currentFrameIndex = 0;
        private float currentPlaybackTime = 0f;
        private float playbackStartTime = 0f;
        private string currentPhase = "";

        // Filtering
        private string filterPhase = null;
        private string filterSubtask = null;
        private System.Collections.Generic.List<ReplayFrame> filteredFrames = null;
        private float filteredDuration = 0f;
        private float filteredStartTime = 0f;

        // Visualization objects
        private GameObject avatarObject;
        private GameObject vergencePointObject;
        private LineRenderer leftEyeRay;
        private LineRenderer rightEyeRay;

        // Interpolation
        private ReplayFrame currentFrame;
        private ReplayingEyeTracker replayingTracker;
        private ReplayFrame nextFrame;
        private float interpolationFactor = 0f;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            parser = new EyeLeanCSVParser();

#if UNITY_EDITOR
            // Replay is editor-only. Force-enable autoPlay + deterministic
            // at runtime so replay scenes "just work" when the user hits
            // Play; the scene asset is not modified.
            autoPlayOnLoad = true;
            deterministicReplay = true;
#endif

            // Pre-arm the global ReplayMode flag in Awake so researcher
            // scripts that gate on it during their own Awake/Start see
            // IsActive=true and can short-circuit before any side-effecting
            // work. Without this, LoadData fires from Start and races them.
            if (autoLoadOnStart && !string.IsNullOrEmpty(dataFilePath))
            {
                EyeLean.Replay.SceneState.ReplayMode.Begin();
            }
        }

        private void Start()
        {
            CreateVisualizationObjects();
            HideVisualization();
            ResolveReplayCameraOnce();

            if (autoLoadOnStart && !string.IsNullOrEmpty(dataFilePath))
            {
                LoadData(dataFilePath);
            }
        }

        // First-person camera state: cache the original transform on first
        // resolve so it can be restored on disable / state-reset; without
        // this, toggling firstPersonCamera off mid-session would leave the
        // scene camera permanently displaced.
        private Transform cachedReplayCamera;
        private Vector3 cachedCameraOriginalPosition;
        private Quaternion cachedCameraOriginalRotation;
        private Transform cachedCameraOriginalParent;
        private bool cameraOriginalCaptured;
        // Detach the camera from its parent exactly once on entering Playing
        // and reattach exactly once on leaving Playing. Per-frame SetParent
        // fights any pose driver on the rig and can leave it in a degraded
        // state if this controller is killed mid-play.
        private bool cameraDetachedForReplay;

        private void ResolveReplayCameraOnce()
        {
            if (cachedReplayCamera != null && cachedReplayCamera) return;
            cachedReplayCamera = replayCameraTransform != null && replayCameraTransform
                ? replayCameraTransform
                : (Camera.main != null ? Camera.main.transform : null);
            if (cachedReplayCamera != null && !cameraOriginalCaptured)
            {
                cachedCameraOriginalPosition = cachedReplayCamera.position;
                cachedCameraOriginalRotation = cachedReplayCamera.rotation;
                cachedCameraOriginalParent = cachedReplayCamera.parent;
                cameraOriginalCaptured = true;
                if (debugMode) Debug.Log($"[ReplayController] Resolved replay camera: {cachedReplayCamera.name} (will drive in first-person mode)");
            }
        }

        private void ApplyHeadPoseToCamera(Vector3 headPos, Quaternion headRot)
        {
            if (!firstPersonCamera) return;
            ResolveReplayCameraOnce();
            if (cachedReplayCamera == null) return;
            // Detach from parent ONCE on entry to first-person playback,
            // not every frame. Subsequent frames just write world pose.
            if (!cameraDetachedForReplay && cachedReplayCamera.parent != null)
            {
                cachedReplayCamera.SetParent(null, true);
                cameraDetachedForReplay = true;
            }
            cachedReplayCamera.position = headPos;
            cachedReplayCamera.rotation = headRot;
        }

        private void RestoreCameraOriginalPose()
        {
            // Scene unload / OnDestroy ordering can leave a destroyed parent
            // transform; SetParent against that throws and skips
            // CleanupVisualization. Wrap in try/catch and null-check.
            if (!cameraOriginalCaptured || cachedReplayCamera == null) return;
            try
            {
                if (cachedCameraOriginalParent != null)
                {
                    cachedReplayCamera.SetParent(cachedCameraOriginalParent, false);
                }
                else
                {
                    cachedReplayCamera.SetParent(null, false);
                }
                cachedReplayCamera.position = cachedCameraOriginalPosition;
                cachedReplayCamera.rotation = cachedCameraOriginalRotation;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ReplayController] RestoreCameraOriginalPose failed (likely scene-unload race): {e.Message}");
            }
            finally
            {
                cameraDetachedForReplay = false;
            }
        }

        private void Update()
        {
            if (currentState == ReplayState.Playing)
            {
                UpdatePlayback();
            }

            HandleInput();
        }

        private void OnDestroy()
        {
            // Restore the scene camera to its original pose before tearing
            // down visualization. Otherwise the editor scene view would
            // permanently retain the last-played frame's head pose.
            RestoreCameraOriginalPose();
            CleanupVisualization();
            // Defensive: in case the controller dies mid-play, make sure the
            // global gating flag doesn't leak into the next session.
            EyeLean.Replay.SceneState.ReplayMode.End();
            // Drop the replay tracker override exactly once at controller
            // teardown (no longer tied to state transitions, which would
            // wipe it during the Processing → Ready hop).
            EyeTracking.Core.EyeTrackerFactory.SetReplayOverride(null);
        }

        #endregion

        #region Public Methods - Data Loading

        /// <summary>
        /// Load eye tracking data from CSV file
        /// </summary>
        public void LoadData(string filePath)
        {
            if (currentState == ReplayState.Loading)
            {
                Debug.LogWarning("[ReplayController] Already loading data");
                return;
            }

            StartCoroutine(LoadDataCoroutine(filePath));
        }

        /// <summary>
        /// Load data from StreamingAssets folder
        /// </summary>
        public void LoadFromStreamingAssets(string fileName)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, fileName);
            LoadData(fullPath);
        }

        #endregion

        #region Public Methods - Playback Control

        /// <summary>
        /// Start or resume playback
        /// </summary>
        public void Play()
        {
            if (!IsReady)
            {
                Debug.LogWarning("[ReplayController] Cannot play - data not loaded");
                return;
            }

            if (currentState == ReplayState.Complete)
            {
                // Restart from beginning
                SeekToFrame(0);
            }

            SetState(ReplayState.Playing);
            playbackStartTime = Time.time - currentPlaybackTime;

            ShowVisualization();

            if (debugMode)
            {
                Debug.Log($"[ReplayController] Playing from frame {currentFrameIndex}");
            }
        }

        /// <summary>
        /// Pause playback
        /// </summary>
        public void Pause()
        {
            if (currentState == ReplayState.Playing)
            {
                SetState(ReplayState.Paused);

                if (debugMode)
                {
                    Debug.Log($"[ReplayController] Paused at frame {currentFrameIndex}");
                }
            }
        }

        /// <summary>
        /// Toggle between play and pause
        /// </summary>
        public void TogglePlayPause()
        {
            if (currentState == ReplayState.Playing)
                Pause();
            else if (IsReady)
                Play();
        }

        /// <summary>
        /// Stop playback and reset to beginning
        /// </summary>
        public void Stop()
        {
            SetState(ReplayState.Ready);
            SeekToFrame(0);
            HideVisualization();

            if (debugMode)
            {
                Debug.Log("[ReplayController] Stopped");
            }
        }

        /// <summary>
        /// Seek to specific frame index
        /// </summary>
        public void SeekToFrame(int frameIndex)
        {
            var frames = GetActiveFrames();
            if (frames == null || frames.Count == 0)
                return;

            frameIndex = Mathf.Clamp(frameIndex, 0, frames.Count - 1);
            currentFrameIndex = frameIndex;

            float startTime = filteredFrames != null ? filteredStartTime : session.recordingStartTime;
            currentPlaybackTime = frames[frameIndex].timestamp - startTime;

            DisplayFrame(frames[frameIndex]);
        }

        /// <summary>
        /// Seek to specific time in seconds
        /// </summary>
        public void SeekToTime(float time)
        {
            var frames = GetActiveFrames();
            if (frames == null || frames.Count == 0)
                return;

            time = Mathf.Clamp(time, 0, TotalDuration);
            float startTime = filteredFrames != null ? filteredStartTime : session.recordingStartTime;
            float targetTime = startTime + time;

            // Find frame at target time
            for (int i = 0; i < frames.Count - 1; i++)
            {
                if (frames[i + 1].timestamp > targetTime)
                {
                    SeekToFrame(i);
                    return;
                }
            }
            SeekToFrame(frames.Count - 1);
        }

        /// <summary>
        /// Seek to specific progress (0-1)
        /// </summary>
        public void SeekToProgress(float progress)
        {
            var frames = GetActiveFrames();
            if (frames == null || frames.Count == 0)
                return;

            progress = Mathf.Clamp01(progress);
            int frameIndex = Mathf.RoundToInt(progress * (frames.Count - 1));
            SeekToFrame(frameIndex);
        }

        /// <summary>
        /// Set playback filter by phase and/or subtask.
        /// Pass null to clear a filter.
        /// </summary>
        public void SetPlaybackFilter(string phase, string subtask)
        {
            filterPhase = string.IsNullOrEmpty(phase) ? null : phase;
            filterSubtask = string.IsNullOrEmpty(subtask) ? null : subtask;

            ApplyFilter();

            if (debugMode)
            {
                if (filterPhase != null || filterSubtask != null)
                {
                    Debug.Log($"[ReplayController] Filter set: Phase='{filterPhase ?? "any"}', Subtask='{filterSubtask ?? "any"}' ({filteredFrames?.Count ?? 0} frames)");
                }
                else
                {
                    Debug.Log("[ReplayController] Filter cleared, showing all frames");
                }
            }

            // Reset to start of filtered range. Also reset when not yet
            // Ready (a researcher subscribed to OnLoadComplete may call
            // SetPlaybackFilter before the built-in ReplayManager runs its
            // handler); without this, playback would start from a stale
            // currentFrameIndex pointing into the unfiltered span.
            if (IsReady)
            {
                SeekToFrame(0);
            }
            else
            {
                currentFrameIndex = 0;
                currentPlaybackTime = 0f;
            }
        }

        /// <summary>
        /// Clear any active filter
        /// </summary>
        public void ClearFilter()
        {
            SetPlaybackFilter(null, null);
        }

        private void ApplyFilter()
        {
            if (session == null || session.frames == null)
            {
                filteredFrames = null;
                return;
            }

            if (filterPhase == null && filterSubtask == null)
            {
                filteredFrames = null;
                filteredDuration = 0f;
                filteredStartTime = 0f;
                return;
            }

            filteredFrames = new System.Collections.Generic.List<ReplayFrame>();

            foreach (var frame in session.frames)
            {
                bool matchesPhase = filterPhase == null || frame.phase == filterPhase;
                bool matchesSubtask = filterSubtask == null || frame.subTask == filterSubtask;

                if (matchesPhase && matchesSubtask)
                {
                    filteredFrames.Add(frame);
                }
            }

            if (filteredFrames.Count > 0)
            {
                filteredStartTime = filteredFrames[0].timestamp;
                filteredDuration = filteredFrames[filteredFrames.Count - 1].timestamp - filteredStartTime;
            }
            else
            {
                filteredDuration = 0f;
                filteredStartTime = 0f;
            }
        }

        /// <summary>
        /// Get the active frame list (filtered or all)
        /// </summary>
        private System.Collections.Generic.List<ReplayFrame> GetActiveFrames()
        {
            if (filteredFrames != null)
                return filteredFrames;
            return session?.frames;
        }

        /// <summary>
        /// Skip forward/backward by number of frames
        /// </summary>
        public void SkipFrames(int frameCount)
        {
            SeekToFrame(currentFrameIndex + frameCount);
        }

        /// <summary>
        /// Skip to next phase
        /// </summary>
        public void NextPhase()
        {
            if (session == null || session.phaseMarkers.Count == 0)
                return;

            // Find current phase marker
            for (int i = 0; i < session.phaseMarkers.Count; i++)
            {
                if (currentFrameIndex <= session.phaseMarkers[i].endFrameIndex)
                {
                    // Go to next phase if exists
                    if (i + 1 < session.phaseMarkers.Count)
                    {
                        SeekToFrame(session.phaseMarkers[i + 1].startFrameIndex);
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Skip to previous phase
        /// </summary>
        public void PreviousPhase()
        {
            if (session == null || session.phaseMarkers.Count == 0)
                return;

            // Find current phase marker
            for (int i = 0; i < session.phaseMarkers.Count; i++)
            {
                if (currentFrameIndex <= session.phaseMarkers[i].endFrameIndex)
                {
                    // Go to previous phase if exists
                    if (i > 0)
                    {
                        SeekToFrame(session.phaseMarkers[i - 1].startFrameIndex);
                    }
                    else
                    {
                        SeekToFrame(0);
                    }
                    return;
                }
            }
        }

        #endregion

        #region Private Methods - Data Loading

        private IEnumerator LoadDataCoroutine(string filePath)
        {
            SetState(ReplayState.Loading);

            // Check file exists
            if (!File.Exists(filePath))
            {
                string error = $"File not found: {filePath}";
                Debug.LogError($"[ReplayController] {error}");
                OnLoadError?.Invoke(error);
                SetState(ReplayState.Uninitialized);
                yield break;
            }

            if (debugMode)
            {
                Debug.Log($"[ReplayController] Loading: {filePath}");
            }

            // Parse file in background
            yield return null;

            SetState(ReplayState.Processing);
            ReplaySession loadedSession = null;

            // Use a fresh parser instance per load. EyeLeanCSVParser holds
            // mutable per-parse state (column map, debugMode), and if a load
            // times out below, the orphaned worker thread keeps running until
            // its ParseFile call completes. A shared parser would let that
            // orphan corrupt the next load's parsing state. A local parser
            // keeps each load fully isolated.
            var localParser = new EyeLeanCSVParser();
            bool parseComplete = false;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    loadedSession = localParser.ParseFile(filePath, debugMode);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ReplayController] Parse error: {e.Message}");
                }
                parseComplete = true;
            });

            // Wait for parsing with timeout
            float timeout = 30f;
            float startTime = Time.realtimeSinceStartup;

            while (!parseComplete)
            {
                if (Time.realtimeSinceStartup - startTime > timeout)
                {
                    string error = "Loading timeout exceeded";
                    Debug.LogError($"[ReplayController] {error}");
                    OnLoadError?.Invoke(error);
                    SetState(ReplayState.Uninitialized);
                    yield break;
                }
                yield return null;
            }

            // Check result
            if (loadedSession == null || loadedSession.frames.Count == 0)
            {
                string error = "Failed to parse CSV or no valid frames found";
                Debug.LogError($"[ReplayController] {error}");
                OnLoadError?.Invoke(error);
                SetState(ReplayState.Uninitialized);
                yield break;
            }

            // Store session
            session = loadedSession;

            // Initialize playback state
            currentFrameIndex = 0;
            currentPlaybackTime = 0f;
            currentFrame = session.frames[0];
            currentPhase = session.frames[0].phase ?? "";

            // Install the IEyeTracker override BEFORE OnLoadComplete fires
            // (and well before Playing starts) so the live experiment's
            // gaze-on-start dwell sees recorded gaze samples from frame 0.
            // ReplayingEyeTracker reports IsAvailable=true only once playback
            // advances, which is the right moment for gaze checks to begin.
            if (deterministicReplay)
            {
                if (replayingTracker == null) replayingTracker = new ReplayingEyeTracker(this);
                EyeTracking.Core.EyeTrackerFactory.SetReplayOverride(replayingTracker);
                Debug.Log("[ReplayController] Deterministic replay: installed ReplayingEyeTracker as factory override; recorded gaze will drive live experiment's gaze checks.");
            }
            else
            {
                Debug.Log("[ReplayController] deterministicReplay=false on inspector. Live experiment gaze checks will see whatever EyeTrackerFactory returns by default (Null on editor without OpenXR).");
            }

            SetState(ReplayState.Ready);

            if (debugMode)
            {
                Debug.Log($"[ReplayController] Loaded {session.totalFrames} frames, duration: {session.totalDuration:F2}s");
            }

            OnLoadComplete?.Invoke(session);

            if (autoPlayOnLoad)
            {
                Play();
            }
        }

        #endregion

        #region Private Methods - Playback

        private void UpdatePlayback()
        {
            var frames = GetActiveFrames();
            if (frames == null || frames.Count == 0)
                return;

            // Advance time
            currentPlaybackTime += Time.deltaTime * playbackSpeed;

            // Find target frame (use filtered start time if filter is active)
            float startTime = filteredFrames != null ? filteredStartTime : session.recordingStartTime;
            float targetTime = startTime + currentPlaybackTime;

            // Advance frame index
            while (currentFrameIndex < frames.Count - 1 &&
                   frames[currentFrameIndex + 1].timestamp <= targetTime)
            {
                currentFrameIndex++;
            }

            // Check for end (use TotalDuration which is filter-aware)
            if (currentPlaybackTime >= TotalDuration)
            {
                if (loopPlayback)
                {
                    currentPlaybackTime = 0f;
                    currentFrameIndex = 0;
                    playbackStartTime = Time.time;

                    if (debugMode)
                    {
                        Debug.Log("[ReplayController] Looping playback");
                    }
                }
                else
                {
                    SetState(ReplayState.Complete);
                    OnPlaybackComplete?.Invoke();

                    if (debugMode)
                    {
                        Debug.Log("[ReplayController] Playback complete");
                    }
                    return;
                }
            }

            // Display frame (with interpolation if enabled)
            if (enableInterpolation && currentFrameIndex < frames.Count - 1)
            {
                currentFrame = frames[currentFrameIndex];
                nextFrame = frames[currentFrameIndex + 1];

                float frameDuration = nextFrame.timestamp - currentFrame.timestamp;
                if (frameDuration > 0)
                {
                    interpolationFactor = (targetTime - currentFrame.timestamp) / frameDuration;
                    interpolationFactor = Mathf.Clamp01(interpolationFactor);
                    DisplayInterpolatedFrame(currentFrame, nextFrame, interpolationFactor);
                }
            }
            else
            {
                currentFrame = frames[currentFrameIndex];
                DisplayFrame(currentFrame);
            }

            // Check for phase change
            string newPhase = frames[currentFrameIndex].phase ?? "";
            if (newPhase != currentPhase)
            {
                currentPhase = newPhase;
                OnPhaseChanged?.Invoke(currentPhase);

                if (debugMode)
                {
                    Debug.Log($"[ReplayController] Phase changed to: {currentPhase}");
                }
            }
        }

        #endregion

        #region Private Methods - Visualization

        private void CreateVisualizationObjects()
        {
            // Create a container at world origin (not affected by ReplayController position)
            var container = new GameObject("ReplayVisualization");
            container.transform.position = Vector3.zero;
            container.transform.rotation = Quaternion.identity;

            // Create avatar - starts hidden, will be positioned when frame is displayed
            avatarObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            avatarObject.name = "ReplayAvatar";
            avatarObject.transform.SetParent(container.transform);
            avatarObject.transform.localScale = Vector3.one * avatarSize;

            // Disable collider
            var avatarCollider = avatarObject.GetComponent<Collider>();
            if (avatarCollider != null)
                Destroy(avatarCollider);

            // Apply material using VRMaterialProvider
            var avatarRenderer = avatarObject.GetComponent<Renderer>();
            if (avatarRenderer != null)
            {
                avatarRenderer.material = VRMaterialProvider.GetMaterial(avatarColor);
            }

            // Create vergence point - starts hidden, will be positioned when frame is displayed
            vergencePointObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vergencePointObject.name = "VergencePoint";
            vergencePointObject.transform.SetParent(container.transform);
            vergencePointObject.transform.localScale = Vector3.one * vergencePointSize;

            var vergenceCollider = vergencePointObject.GetComponent<Collider>();
            if (vergenceCollider != null)
                Destroy(vergenceCollider);

            var vergenceRenderer = vergencePointObject.GetComponent<Renderer>();
            if (vergenceRenderer != null)
            {
                vergenceRenderer.material = VRMaterialProvider.GetMaterial(vergenceColor);
            }

            // Create left eye ray
            GameObject leftRayObj = new GameObject("LeftEyeRay");
            leftRayObj.transform.SetParent(container.transform);
            leftEyeRay = leftRayObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(leftEyeRay, leftEyeColor);

            // Create right eye ray
            GameObject rightRayObj = new GameObject("RightEyeRay");
            rightRayObj.transform.SetParent(container.transform);
            rightEyeRay = rightRayObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(rightEyeRay, rightEyeColor);

            // Store container reference for cleanup
            visualizationContainer = container;
        }

        private GameObject visualizationContainer;

        private void ConfigureLineRenderer(LineRenderer lr, Color color)
        {
            lr.positionCount = 2;
            lr.startWidth = 0.005f;
            lr.endWidth = 0.005f;
            // Mirror EyeTracker's LineRenderer config so rays render
            // identically in Replay, calibrator, and experiment scenes.
            // VRMaterialProvider returns the canonical Unlit/Color material;
            // Sprites/Default is a fallback if it's unavailable.
            lr.material = VRMaterialProvider.GetMaterial(color, false)
                          ?? new Material(Shader.Find("Sprites/Default"));
            lr.startColor = color;
            lr.endColor = color;
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Tile;
            lr.sortingOrder = 100;
            lr.receiveShadows = false;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Resolve the ray origin for one eye. In head-anchored mode the
        // origin is `headPos + rotation * (right * ±lateralOffset)` — a
        // stable point relative to the avatar, immune to per-frame
        // coord-frame races on the recorded LeftOrigin/RightOrigin columns.
        // In raw mode the recorded eye origin is returned verbatim. Also
        // flags wildly anomalous recorded origins to logcat.
        private Vector3 ResolveRayOrigin(Vector3 headPos, Quaternion headRotation,
                                         Vector3 recordedEyeOrigin, bool isLeft)
        {
            if (originDeviationWarningMeters > 0f)
            {
                float deviation = Vector3.Distance(recordedEyeOrigin, headPos);
                if (deviation > originDeviationWarningMeters &&
                    (warningThrottleFrames <= 0 || frameCounterForWarnings % warningThrottleFrames == 0))
                {
                    Debug.LogWarning($"[ReplayController] Frame {currentFrameIndex}: recorded {(isLeft ? "left" : "right")} eye origin {recordedEyeOrigin} is {deviation:F2}m from head pos {headPos} — likely coord-frame race. Head-anchored mode {(useHeadAnchoredRayOrigin ? "ON (compensating)" : "OFF (using raw)")}.");
                }
            }

            if (!useHeadAnchoredRayOrigin) return recordedEyeOrigin;

            float side = isLeft ? -1f : 1f;
            Vector3 rightInHead = headRotation * Vector3.right;
            return headPos + rightInHead * (side * headAnchoredLateralOffset);
        }

        // Counter incremented in DisplayFrame so the warning throttle fires on
        // every Nth call regardless of frame rate / playback speed.
        private int frameCounterForWarnings = 0;

        private void ShowVisualization()
        {
            if (showAvatar && avatarObject != null)
                avatarObject.SetActive(true);
            if (showVergencePoint && vergencePointObject != null)
                vergencePointObject.SetActive(true);
            if (showGazeRays)
            {
                if (leftEyeRay != null) leftEyeRay.gameObject.SetActive(true);
                if (rightEyeRay != null) rightEyeRay.gameObject.SetActive(true);
            }
        }

        private void HideVisualization()
        {
            if (avatarObject != null) avatarObject.SetActive(false);
            if (vergencePointObject != null) vergencePointObject.SetActive(false);
            if (leftEyeRay != null) leftEyeRay.gameObject.SetActive(false);
            if (rightEyeRay != null) rightEyeRay.gameObject.SetActive(false);
        }

        private void CleanupVisualization()
        {
            // Destroy container (will also destroy all children)
            if (visualizationContainer != null)
            {
                Destroy(visualizationContainer);
                visualizationContainer = null;
            }

            // Clear references
            avatarObject = null;
            vergencePointObject = null;
            leftEyeRay = null;
            rightEyeRay = null;
        }

        private void DisplayFrame(ReplayFrame frame)
        {
            if (frame == null) return;

            // Debug first frame to verify positioning
            if (debugMode && currentFrameIndex == 0)
            {
                Debug.Log($"[ReplayController] Frame 0: HeadPos={frame.headPosition}, Vergence={frame.vergencePoint}");
            }

            // Update avatar position directly from CSV data. In first-person
            // mode hide the avatar so the camera isn't inside the head sphere.
            if (avatarObject != null)
            {
                bool avatarShouldShow = showAvatar && !(firstPersonCamera && hideAvatarInFirstPerson);
                avatarObject.SetActive(avatarShouldShow);
                if (avatarShouldShow)
                {
                    avatarObject.transform.position = frame.headPosition;
                    avatarObject.transform.rotation = frame.headRotation;
                }
            }

            // Drive the scene camera in first-person mode.
            ApplyHeadPoseToCamera(frame.headPosition, frame.headRotation);

            // Update gaze rays
            UpdateGazeRays(frame);

            // Update vergence point directly from CSV data
            if (showVergencePoint && vergencePointObject != null)
            {
                bool showVergence = frame.hasValidVergence;
                vergencePointObject.SetActive(showVergence);

                if (showVergence)
                {
                    vergencePointObject.transform.position = frame.vergencePoint;
                }
            }

            OnFrameDisplayed?.Invoke(frame);
        }

        private void DisplayInterpolatedFrame(ReplayFrame frame1, ReplayFrame frame2, float t)
        {
            if (frame1 == null || frame2 == null) return;

            // Interpolate position and rotation directly from CSV data
            Vector3 position = Vector3.Lerp(frame1.headPosition, frame2.headPosition, t);
            Quaternion rotation = Quaternion.Slerp(frame1.headRotation, frame2.headRotation, t);

            // Update avatar (hidden in first-person mode so the camera isn't
            // looking at the inside of its own head sphere).
            if (avatarObject != null)
            {
                bool avatarShouldShow = showAvatar && !(firstPersonCamera && hideAvatarInFirstPerson);
                avatarObject.SetActive(avatarShouldShow);
                if (avatarShouldShow)
                {
                    avatarObject.transform.position = position;
                    avatarObject.transform.rotation = rotation;
                }
            }

            // Drive the scene camera with the smoothly interpolated head pose.
            ApplyHeadPoseToCamera(position, rotation);

            // Use actual eye origins from the recorded data, interpolated
            // This preserves the real IPD from the recording session
            Vector3 worldLeftOrigin = Vector3.Lerp(frame1.leftEyeOrigin, frame2.leftEyeOrigin, t);
            Vector3 worldRightOrigin = Vector3.Lerp(frame1.rightEyeOrigin, frame2.rightEyeOrigin, t);

            // Slerp, not Lerp+normalize. Lerp+normalize collapses to the
            // bisector with shrinking magnitude near 90° saccades and
            // returns Vector3.zero at exactly opposite endpoints.
            Vector3 leftDir = Vector3.Slerp(frame1.leftEyeDirection, frame2.leftEyeDirection, t);
            Vector3 rightDir = Vector3.Slerp(frame1.rightEyeDirection, frame2.rightEyeDirection, t);

            // Update gaze rays. Pass the interpolated head pose so that
            // head-anchored mode uses the smoothly-interpolated head as the
            // ray-origin reference (matches the avatar's interpolated motion);
            // raw mode falls back to the interpolated recorded eye origins.
            UpdateGazeRaysInterpolated(worldLeftOrigin, leftDir, worldRightOrigin, rightDir,
                t < 0.5f ? frame1 : frame2, position, rotation);

            // Interpolate vergence directly from CSV data
            if (showVergencePoint && vergencePointObject != null)
            {
                bool showVergence = frame1.hasValidVergence || frame2.hasValidVergence;
                vergencePointObject.SetActive(showVergence);

                if (showVergence)
                {
                    Vector3 vergence = Vector3.Lerp(frame1.vergencePoint, frame2.vergencePoint, t);
                    vergencePointObject.transform.position = vergence;
                }
            }

            // Fire event with interpolated-towards frame
            OnFrameDisplayed?.Invoke(t < 0.5f ? frame1 : frame2);
        }

        private void UpdateGazeRays(ReplayFrame frame)
        {
            frameCounterForWarnings++;

            bool leftVisible = forceEyesVisible || frame.leftEyeOpenness > eyeOpennessThreshold;
            bool rightVisible = forceEyesVisible || frame.rightEyeOpenness > eyeOpennessThreshold;

            // Resolve ray origins via the head-anchored / raw mode switch.
            // Default head-anchored mode produces a stable visual ray-pair
            // tied to the avatar regardless of upstream recording quirks.
            Vector3 worldLeftOrigin = ResolveRayOrigin(frame.headPosition, frame.headRotation, frame.leftEyeOrigin, isLeft: true);
            Vector3 worldRightOrigin = ResolveRayOrigin(frame.headPosition, frame.headRotation, frame.rightEyeOrigin, isLeft: false);

            // Gaze directions are world-space already (recorded that way by
            // the live EyeTracker after the OpenXR provider applies the
            // tracking-space → world transform). Pass through verbatim.
            Vector3 leftDir = frame.leftEyeDirection;
            Vector3 rightDir = frame.rightEyeDirection;

            if (showGazeRays && leftEyeRay != null)
            {
                leftEyeRay.gameObject.SetActive(leftVisible);
                if (leftVisible)
                {
                    leftEyeRay.SetPosition(0, worldLeftOrigin);
                    leftEyeRay.SetPosition(1, worldLeftOrigin + leftDir * gazeRayLength);
                }
            }

            if (showGazeRays && rightEyeRay != null)
            {
                rightEyeRay.gameObject.SetActive(rightVisible);
                if (rightVisible)
                {
                    rightEyeRay.SetPosition(0, worldRightOrigin);
                    rightEyeRay.SetPosition(1, worldRightOrigin + rightDir * gazeRayLength);
                }
            }
        }

        private void UpdateGazeRaysInterpolated(Vector3 leftOrigin, Vector3 leftDir,
            Vector3 rightOrigin, Vector3 rightDir, ReplayFrame visibilityFrame,
            Vector3 interpolatedHeadPos, Quaternion interpolatedHeadRotation)
        {
            frameCounterForWarnings++;

            bool leftVisible = forceEyesVisible || visibilityFrame.leftEyeOpenness > eyeOpennessThreshold;
            bool rightVisible = forceEyesVisible || visibilityFrame.rightEyeOpenness > eyeOpennessThreshold;

            // Head-anchored mode uses the smoothly-interpolated head pose so
            // ray origins ride alongside the avatar; raw mode passes the
            // interpolated recorded origins through. The visibilityFrame is
            // still consulted for the deviation-warning baseline so the
            // anomaly heuristic compares like-with-like (recorded eye vs.
            // recorded head, not recorded eye vs. interpolated head).
            Vector3 leftOriginResolved = useHeadAnchoredRayOrigin
                ? ResolveRayOrigin(interpolatedHeadPos, interpolatedHeadRotation, leftOrigin, isLeft: true)
                : leftOrigin;
            Vector3 rightOriginResolved = useHeadAnchoredRayOrigin
                ? ResolveRayOrigin(interpolatedHeadPos, interpolatedHeadRotation, rightOrigin, isLeft: false)
                : rightOrigin;

            if (showGazeRays && leftEyeRay != null)
            {
                leftEyeRay.gameObject.SetActive(leftVisible);
                if (leftVisible)
                {
                    leftEyeRay.SetPosition(0, leftOriginResolved);
                    leftEyeRay.SetPosition(1, leftOriginResolved + leftDir * gazeRayLength);
                }
            }

            if (showGazeRays && rightEyeRay != null)
            {
                rightEyeRay.gameObject.SetActive(rightVisible);
                if (rightVisible)
                {
                    rightEyeRay.SetPosition(0, rightOriginResolved);
                    rightEyeRay.SetPosition(1, rightOriginResolved + rightDir * gazeRayLength);
                }
            }
        }

        #endregion

        #region Private Methods - Input

        private void HandleInput()
        {
            // Use new Input System - check if keyboard is available
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Space to toggle play/pause
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                TogglePlayPause();
            }

            // Arrow keys for navigation
            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                SkipFrames(30);
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                SkipFrames(-30);
            }

            // Page up/down for phase navigation
            if (keyboard.pageUpKey.wasPressedThisFrame)
            {
                PreviousPhase();
            }

            if (keyboard.pageDownKey.wasPressedThisFrame)
            {
                NextPhase();
            }

            // Home/End for start/end
            if (keyboard.homeKey.wasPressedThisFrame)
            {
                SeekToFrame(0);
            }

            if (keyboard.endKey.wasPressedThisFrame)
            {
                var frames = GetActiveFrames();
                if (frames != null && frames.Count > 0)
                {
                    SeekToFrame(frames.Count - 1);
                }
            }
        }

        #endregion

        #region Private Methods - State Management

        private void SetState(ReplayState newState)
        {
            if (currentState != newState)
            {
                currentState = newState;
                // Bracket researcher-script gating around the active replay
                // states. Loading/Processing/Playing/Paused = replay is in
                // charge; Ready/Complete/Uninitialized = scene is back to
                // researcher control.
                bool replayActive =
                    newState == ReplayState.Loading ||
                    newState == ReplayState.Processing ||
                    newState == ReplayState.Playing ||
                    newState == ReplayState.Paused;
                if (replayActive) EyeLean.Replay.SceneState.ReplayMode.Begin();
                else
                {
                    EyeLean.Replay.SceneState.ReplayMode.End();
                    // Restore the camera on leaving replay-active states so
                    // the editor scene-view isn't stuck on the last
                    // replayed head pose.
                    if (cameraDetachedForReplay) RestoreCameraOriginalPose();
                    // The EyeTrackerFactory override is NOT cleared here.
                    // Ready transitions briefly out of replayActive between
                    // Processing and Playing (LoadDataCoroutine fires
                    // SetState(Ready) right before autoPlayOnLoad triggers
                    // Play → SetState(Playing)); clearing on Ready would
                    // wipe the override before playback starts. The override
                    // lives for the controller's lifetime; OnDestroy clears.
                }
                // The EyeTrackerFactory override is installed eagerly at
                // LoadDataCoroutine completion so the live experiment's
                // gaze-on-start dwell sees recorded samples from the
                // moment playback begins. Installing on Playing would be
                // too late — the live controller's gaze-on-start loop has
                // already started by then.
                OnStateChanged?.Invoke(newState);

                if (debugMode)
                {
                    Debug.Log($"[ReplayController] State: {newState}");
                }
            }
        }

        #endregion

        #region Public Methods - Status

        /// <summary>
        /// Get formatted status text for UI display
        /// </summary>
        public string GetStatusText()
        {
            if (session == null)
            {
                return $"State: {currentState}\nNo data loaded";
            }

            string filterInfo = HasActiveFilter ? $"\nFilter: {filterPhase ?? "any"}/{filterSubtask ?? "any"}" : "";

            return $"State: {currentState}\n" +
                   $"Frame: {currentFrameIndex + 1}/{TotalFrames}\n" +
                   $"Time: {currentPlaybackTime:F2}s / {TotalDuration:F2}s\n" +
                   $"Phase: {currentPhase}\n" +
                   $"Speed: {playbackSpeed:F1}x{filterInfo}";
        }

        #endregion
    }
}
