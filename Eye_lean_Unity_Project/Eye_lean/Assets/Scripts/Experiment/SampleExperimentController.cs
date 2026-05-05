using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using EyeLean.Experiment;
using EyeTracking.Components;

namespace EyeLean.Experiment
{
    /// <summary>
    /// Main controller for the sample experiment demonstrating eye tracking analysis.
    /// Manages the flow through all experimental phases and coordinates with task managers.
    /// </summary>
    public class SampleExperimentController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("EyeTracker component (gaze + vergence). Auto-found if null.")]
        [SerializeField] private EyeTracker eyeTracker;
        [Tooltip("SessionRecorder (CSV + session context + metadata API). Auto-found if null.")]
        [SerializeField] private SessionRecorder sessionRecorder;
        [SerializeField] private EnvironmentGenerator environmentGenerator;
        [SerializeField] private Transform cameraTransform;

        [Header("Task Managers")]
        [SerializeField] private VisualSearchManager visualSearchManager;
        [SerializeField] private CountingTaskManager countingTaskManager;
        [SerializeField] private ChangeDetectionManager changeDetectionManager;

        [Header("UI")]
        [SerializeField] private ExperimentUI experimentUI;

        [Header("Phase Configurations")]
        public CalibrationCheckConfig calibrationConfig = CalibrationCheckConfig.Default;
        public FreeExplorationConfig explorationConfig = FreeExplorationConfig.Default;
        public VisualSearchConfig visualSearchConfig = VisualSearchConfig.Default;
        public SmoothPursuitConfig pursuitConfig = SmoothPursuitConfig.Default;
        public CountingTaskConfig countingConfig = CountingTaskConfig.Default;
        public ChangeDetectionConfig changeDetectionConfig = ChangeDetectionConfig.Default;

        [Header("Participant Settings")]
        [Tooltip("Default participant ID written into the CSV when no override is provided. Overridden by MainMenu / ParticipantSession when launched from the menu.")]
        [SerializeField] private string participantID = "P001";
        [Tooltip("Show a participant-ID entry prompt before starting. Off = use the default above without asking.")]
        [SerializeField] private bool promptForParticipantID = true;

        [Header("General Settings")]
        [Tooltip("Seconds each instruction screen stays up before advancing automatically.")]
        [SerializeField] private float instructionDisplayTime = 3.0f;
        [Tooltip("Pause between phases, in seconds. Lets the user reset gaze before the next phase begins.")]
        [SerializeField] private float interPhaseDelay = 2.0f;
        [Tooltip("Start the experiment automatically on Play. Off = wait for an external StartExperiment() call (the normal MainMenu-driven flow).")]
        [SerializeField] private bool autoStart = false;

        // State
        private ExperimentPhase currentPhase = ExperimentPhase.Idle;
        private ExperimentResults results;
        private List<VisualSearchResult> searchResults = new List<VisualSearchResult>();
        private List<ChangeDetectionResult> changeResults = new List<ChangeDetectionResult>();
        private bool isRunning = false;

        // Calibration targets
        private List<GameObject> calibrationTargets = new List<GameObject>();
        private GameObject pursuitTarget;

        // Events
        public event Action<ExperimentPhase> OnPhaseChanged;
        public event Action<ExperimentResults> OnExperimentComplete;

        public ExperimentPhase CurrentPhase => currentPhase;
        public bool IsRunning => isRunning;

        private void Awake()
        {
            // The controller RUNS during deterministic replay. It re-executes
            // its own coroutines against ReplayClock-pinned Time, recorded
            // Camera.main pose, IEyeTracker reads routed to ReplayingEyeTracker,
            // and UnityEngine.Random.state restored from a recorded snapshot —
            // so ShowInstruction / countdown / spawn / gaze-driven gameplay /
            // cleanup all reproduce the participant's experience by running
            // the same code paths. Recorders are suppressed during replay so
            // this run doesn't overwrite the original CSV.

            // Auto-find references if not assigned
            if (eyeTracker == null) eyeTracker = FindFirstObjectByType<EyeTracker>();
            if (sessionRecorder == null) sessionRecorder = FindFirstObjectByType<SessionRecorder>();
            if (environmentGenerator == null) environmentGenerator = FindFirstObjectByType<EnvironmentGenerator>();
            if (cameraTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) cameraTransform = cam.transform;
            }

            // Validate required components
            if (eyeTracker == null)
            {
                Debug.LogError("[Experiment] CRITICAL: EyeTracker component not found in scene. Gaze-driven features will be disabled.");
            }
            if (sessionRecorder == null)
            {
                Debug.LogError("[Experiment] CRITICAL: SessionRecorder not found in scene. CSV recording + experiment metadata will be disabled.");
            }
            if (environmentGenerator == null)
            {
                Debug.LogWarning("[Experiment] EnvironmentGenerator not found in scene. Environment generation features will be disabled.");
            }
            if (cameraTransform == null)
            {
                Debug.LogWarning("[Experiment] Main camera not found. Some features may not work correctly.");
            }

            // Check for mutual exclusivity with ReplayManager
            ValidateMutualExclusivity();

            // IMPORTANT: Declare all metadata fields BEFORE recording starts so
            // the CSV header includes all columns. Recorder must be present.
            if (sessionRecorder != null)
            {
                DeclareAllMetadataFields();
            }

            // Auto-find task managers on same GameObject
            if (visualSearchManager == null)
                visualSearchManager = GetComponent<VisualSearchManager>();

            if (countingTaskManager == null)
                countingTaskManager = GetComponent<CountingTaskManager>();

            if (changeDetectionManager == null)
                changeDetectionManager = GetComponent<ChangeDetectionManager>();

            if (experimentUI == null)
                experimentUI = GetComponent<ExperimentUI>();

            // Initialize results
            results = new ExperimentResults
            {
                sessionId = Guid.NewGuid().ToString("N").Substring(0, 8)
            };
        }

        private void Start()
        {
            Debug.Log("[Experiment] SampleExperimentController.Start() called");
            Debug.Log($"[Experiment] References - eyeTracker: {(eyeTracker != null ? "SET" : "NULL")}, environmentGenerator: {(environmentGenerator != null ? "SET" : "NULL")}, experimentUI: {(experimentUI != null ? "SET" : "NULL")}");

            // Snapshot inspector-baked phase configs into the events sidecar
            // at session start. Replay handlers apply them before any spawn
            // fires, so a researcher that tunes config values between
            // recording and replay still gets a faithful reproduction.
            EyeLean.SceneState.SceneEventRecorder.RecordJson("ConfigExploration", "", explorationConfig);
            EyeLean.SceneState.SceneEventRecorder.RecordJson("ConfigVisualSearch", "", visualSearchConfig);
            EyeLean.SceneState.SceneEventRecorder.RecordJson("ConfigCounting", "", countingConfig);
            EyeLean.SceneState.SceneEventRecorder.RecordJson("ConfigChangeDetection", "", changeDetectionConfig);

            if (autoStart)
            {
                StartExperiment();
            }
            else
            {
                // Use coroutine to wait for XR system before showing start target
                StartCoroutine(ShowIdleMessageDelayed());
            }
        }


        /// <summary>
        /// Wait for XR system to initialize before creating the start target.
        /// </summary>
        private IEnumerator ShowIdleMessageDelayed()
        {
            Debug.Log("[Experiment] Waiting for XR system before creating start target...");

            // Wait for XR system to initialize (same delay as EnvironmentGenerator)
            yield return null;
            yield return null;
            yield return new WaitForSeconds(0.6f); // Slightly longer than EnvironmentGenerator

            // Now show the idle message and create start target
            ShowIdleMessage();
        }

        /// <summary>
        /// Check that ReplayManager is not also active - they are mutually exclusive.
        /// </summary>
        private void ValidateMutualExclusivity()
        {
            // Find any ReplayManager in the scene
            var allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allBehaviours)
            {
                if (mb != null && mb.GetType().Name == "ReplayManager" && mb.enabled)
                {
                    Debug.LogWarning($"[Experiment] ReplayManager is active on '{mb.gameObject.name}'. " +
                        $"SampleExperimentController and ReplayManager are mutually exclusive.\n" +
                        $"For RECORDING: Disable '{mb.gameObject.name}/ReplayManager'\n" +
                        $"For REPLAY: Disable '{gameObject.name}/SampleExperimentController'");

                    // Optionally disable this controller to prevent conflicts
                    // Uncomment the following line to auto-disable when ReplayManager is active:
                    // this.enabled = false;
                }
            }
        }

        // Gaze-to-start state tracking
        private GameObject startTarget;
        private float gazeOnStartTargetTime = 0f;
        private bool isGazingAtStartTarget = false;
        private int diagnosticFrameCounter = 0;

        [Header("Gaze Start Settings")]
        [SerializeField] private float gazeStartDwellTime = 2.0f;
        [SerializeField] private float startTargetDistance = 2.5f;
        [SerializeField] private float startTargetSize = 0.3f;

        private void Update()
        {
            // Check for start input
            if (!isRunning)
            {
                // Check keyboard (Editor only)
                #if UNITY_EDITOR
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                {
                    CleanupStartTarget();
                    StartExperiment();
                    return;
                }
                #endif

                // Check gaze on start target (HMD and Editor)
                UpdateGazeStart();
            }
        }

        /// <summary>
        /// Update gaze-based start detection using the standard GazeTarget system.
        /// </summary>
        private void UpdateGazeStart()
        {
            diagnosticFrameCounter++;

            if (startTarget == null)
            {
                if (diagnosticFrameCounter % 300 == 0)
                {
                    Debug.LogWarning("[Experiment] UpdateGazeStart: startTarget is NULL!");
                }
                return;
            }

            // Use the standard GazeTarget component for gaze detection.
            var gazeTarget = startTarget.GetComponent<GazeTarget>();
            bool isLookingAtTarget = gazeTarget != null && gazeTarget.IsBeingGazedAt;

            // Deterministic-replay fallback: when ReplayMode is active the
            // live EyeTracker MonoBehaviour may not be running its Update
            // (e.g. SampleExperiment relies on a DontDestroyOnLoad EyeTracker
            // from the calibration scene that doesn't exist in editor-only
            // replay), so GazeTarget.IsBeingGazedAt never updates. Do our own
            // raycast against the IEyeTracker so the gaze-on-start dwell fires
            // from recorded gaze samples regardless.
            if (!isLookingAtTarget && EyeLean.Replay.SceneState.ReplayMode.IsActive)
            {
                var tracker = EyeTracking.Core.EyeTrackerFactory.GetEyeTracker();
                if (tracker != null && tracker.IsAvailable
                    && tracker.GetCombinedGazeOrigin(out Vector3 origin)
                    && tracker.GetCombinedGazeDirection(out Vector3 direction))
                {
                    if (UnityEngine.Physics.Raycast(origin, direction, out RaycastHit hit, 10f, -1)
                        && hit.collider != null && hit.collider.gameObject == startTarget)
                    {
                        isLookingAtTarget = true;
                    }
                }
            }

            // Periodic diagnostic logging every 5 seconds (300 frames at 60fps)
            if (diagnosticFrameCounter % 300 == 0)
            {
                string gazeTargetStatus = gazeTarget != null ? $"exists, IsBeingGazedAt={gazeTarget.IsBeingGazedAt}" : "NULL";
                Debug.Log($"[Experiment] GAZE DIAGNOSTIC Frame {diagnosticFrameCounter}: GazeTarget={gazeTargetStatus}, isLookingAtTarget={isLookingAtTarget}");
                Debug.Log($"[Experiment] GAZE DIAGNOSTIC: startTarget.position={startTarget.transform.position}, isGazingAtStartTarget={isGazingAtStartTarget}, dwellTime={gazeOnStartTargetTime:F2}s");

                // Also check what eye tracker is currently looking at
                if (eyeTracker != null)
                {
                    var currentGazed = eyeTracker.GetCurrentGazedObject();
                    string gazedName = currentGazed != null ? currentGazed.name : "null";
                    Debug.Log($"[Experiment] GAZE DIAGNOSTIC: EyeTracker currently looking at: '{gazedName}'");
                }
                // Log the factory tracker (replaying or live) to confirm the
                // deterministic-replay override is serving valid gaze data
                // even when the EyeTracker MonoBehaviour's Update isn't running.
                var factoryTracker = EyeTracking.Core.EyeTrackerFactory.GetEyeTracker();
                if (factoryTracker != null)
                {
                    bool hasOrigin = factoryTracker.GetCombinedGazeOrigin(out Vector3 fOrigin);
                    bool hasDir = factoryTracker.GetCombinedGazeDirection(out Vector3 fDir);
                    Debug.Log($"[Experiment] GAZE DIAGNOSTIC: factory tracker='{factoryTracker.DeviceName}' available={factoryTracker.IsAvailable} origin={(hasOrigin ? fOrigin.ToString("F3") : "no")} dir={(hasDir ? fDir.ToString("F3") : "no")}");
                }
            }

            if (isLookingAtTarget)
            {
                if (!isGazingAtStartTarget)
                {
                    isGazingAtStartTarget = true;
                    gazeOnStartTargetTime = 0f;
                    Debug.Log("[Experiment] Started gazing at start target");
                }

                gazeOnStartTargetTime += Time.deltaTime;

                // Update progress display
                float progress = gazeOnStartTargetTime / gazeStartDwellTime;
                UpdateStartTargetProgress(progress);

                // Check if dwell time reached
                if (gazeOnStartTargetTime >= gazeStartDwellTime)
                {
                    Debug.Log("[Experiment] Gaze dwell complete - starting experiment");
                    CleanupStartTarget();
                    StartExperiment();
                }
            }
            else
            {
                // Reset if gaze leaves target
                if (isGazingAtStartTarget)
                {
                    isGazingAtStartTarget = false;
                    gazeOnStartTargetTime = 0f;
                    UpdateStartTargetProgress(0f);
                    Debug.Log("[Experiment] Gaze left start target - resetting");
                }
            }
        }

        /// <summary>
        /// Update visual feedback on start target based on gaze progress.
        /// </summary>
        private void UpdateStartTargetProgress(float progress)
        {
            if (startTarget == null) return;

            var renderer = startTarget.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Lerp from green to yellow as progress increases
                Color startColor = Color.green;
                Color endColor = Color.yellow;
                renderer.material.color = Color.Lerp(startColor, endColor, progress);

                // Scale up slightly as progress increases
                float scale = startTargetSize * (1f + progress * 0.3f);
                startTarget.transform.localScale = Vector3.one * scale;
            }

            // Update UI with countdown
            if (experimentUI != null && progress > 0)
            {
                float remaining = gazeStartDwellTime - gazeOnStartTargetTime;
                experimentUI.ShowInstruction($"Look at the GREEN sphere to start\n\nStarting in {remaining:F1}s...");
            }
        }

        /// <summary>
        /// Create the gaze-activated start target.
        /// </summary>
        private void CreateStartTarget()
        {
            if (startTarget != null) return;

            // Position in front of camera
            Vector3 position = Vector3.zero;
            if (cameraTransform != null)
            {
                position = cameraTransform.position + cameraTransform.forward * startTargetDistance;
                Debug.Log($"[Experiment] Camera found at {cameraTransform.position}, forward: {cameraTransform.forward}");
            }
            else
            {
                position = new Vector3(0, 1.6f, startTargetDistance);
                Debug.LogWarning("[Experiment] No camera transform! Using default position.");
            }

            // Create sphere
            startTarget = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            startTarget.name = "StartTarget_GazeToBegin";
            startTarget.transform.position = position;
            startTarget.transform.localScale = Vector3.one * startTargetSize;

            // REPLACE material for Android compatibility (same as vergence point)
            startTarget.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(Color.green);

            // Add GazeTarget for gaze detection
            var gazeTarget = startTarget.AddComponent<GazeTarget>();

            // Verify collider exists (needed for raycast detection)
            var collider = startTarget.GetComponent<Collider>();
            bool isTrigger = collider != null && collider.isTrigger;

            Debug.Log($"[Experiment] Created gaze-to-start target at position {position}");
            Debug.Log($"[Experiment] Start target: collider={collider != null}, isTrigger={isTrigger}, GazeTarget={gazeTarget != null}");
            Debug.Log($"[Experiment] Start target size: {startTargetSize}, distance from camera: {startTargetDistance}m");
        }

        /// <summary>
        /// Clean up the start target.
        /// </summary>
        private void CleanupStartTarget()
        {
            if (startTarget != null)
            {
                Destroy(startTarget);
                startTarget = null;
            }
            gazeOnStartTargetTime = 0f;
            isGazingAtStartTarget = false;
        }

        /// <summary>
        /// Declare all custom metadata fields used by this experiment.
        /// MUST be called before recording starts (in Awake) to ensure CSV header is correct.
        /// </summary>
        private void DeclareAllMetadataFields()
        {
            if (sessionRecorder == null) return;

            // Session-level metadata
            sessionRecorder.DeclareMetadataField("SessionType", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("ExperimentVersion", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("PhaseNumber", EyeLean.Data.MetadataValueType.Int);

            // Calibration check metadata
            sessionRecorder.DeclareMetadataField("CalibrationPointIndex", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("CalibrationRow", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("CalibrationCol", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("CalibrationTargetX", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("CalibrationTargetY", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("CalibrationTargetZ", EyeLean.Data.MetadataValueType.Float);

            // Visual search metadata
            sessionRecorder.DeclareMetadataField("SearchTrialNumber", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("DistractorCount", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("TargetFound", EyeLean.Data.MetadataValueType.Bool);
            sessionRecorder.DeclareMetadataField("SearchTime", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("SearchTargetX", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("SearchTargetY", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("SearchTargetZ", EyeLean.Data.MetadataValueType.Float);

            // Smooth pursuit metadata: target position is updated every Update()
            // during the pursuit phase so post-hoc analysis can compute pursuit
            // gain / lag without knowing the figure-8 trajectory parameters.
            sessionRecorder.DeclareMetadataField("PursuitTargetX", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("PursuitTargetY", EyeLean.Data.MetadataValueType.Float);
            sessionRecorder.DeclareMetadataField("PursuitTargetZ", EyeLean.Data.MetadataValueType.Float);

            // Counting task metadata
            sessionRecorder.DeclareMetadataField("CountingActualCount", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("CountingReportedCount", EyeLean.Data.MetadataValueType.Int);

            // Change detection metadata
            sessionRecorder.DeclareMetadataField("ChangeDetectionTrialNumber", EyeLean.Data.MetadataValueType.Int);
            sessionRecorder.DeclareMetadataField("ChangeType", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("ChangedObjectName", EyeLean.Data.MetadataValueType.String);
            sessionRecorder.DeclareMetadataField("ChangeDetected", EyeLean.Data.MetadataValueType.Bool);
            sessionRecorder.DeclareMetadataField("DetectionTime", EyeLean.Data.MetadataValueType.Float);

            Debug.Log("[Experiment] Declared all metadata fields for CSV schema");
        }

        private void ShowIdleMessage()
        {
            Debug.Log($"[Experiment] ShowIdleMessage() - experimentUI is {(experimentUI != null ? "SET" : "NULL")}");

            // Create the gaze-to-start target
            CreateStartTarget();

            if (experimentUI != null)
            {
                #if UNITY_EDITOR
                experimentUI.ShowInstruction("Look at the GREEN sphere to start\n\n(or press SPACE in Editor)");
                #else
                experimentUI.ShowInstruction("Look at the GREEN sphere to start");
                #endif
            }
            else
            {
                Debug.LogWarning("[Experiment] Cannot show idle message - ExperimentUI reference not set!");
            }
        }

        /// <summary>
        /// Start the full experiment sequence.
        /// </summary>
        public void StartExperiment()
        {
            if (isRunning) return;

            // Refuse to start with an empty participant ID — that produces
            // CSVs the researcher cannot match back to a participant. Warn
            // (but proceed) on the literal default placeholder, since it
            // may be a deliberate test value in some labs.
            if (string.IsNullOrWhiteSpace(participantID))
            {
                Debug.LogError("[Experiment] Cannot start: participantID is empty. Set it via the Inspector or SetParticipantID() before calling StartExperiment().");
                return;
            }
            if (participantID == "P001")
            {
                Debug.LogWarning("[Experiment] Starting with the default placeholder participantID 'P001'. If this is real data, set a real ID before recording.");
            }
            Debug.Log($"[Experiment] === Starting experiment for participant: '{participantID}' ===");

            isRunning = true;
            results.startTime = DateTime.Now;
            results.participantID = participantID;
            searchResults.Clear();
            changeResults.Clear();

            // Set participant ID in eye tracker for CSV output
            if (eyeTracker != null)
            {
                sessionRecorder.SetParticipantID(participantID);

                // Demonstrate custom metadata API - these values will appear in CSV output
                // Set experiment-level metadata that persists across all phases
                sessionRecorder.SetMetadata("SessionType", "SampleExperiment");
                sessionRecorder.SetMetadata("ExperimentVersion", "1.0");
            }

            StartCoroutine(RunExperimentSequence());
        }

        /// <summary>
        /// Set participant ID before starting the experiment.
        /// </summary>
        public void SetParticipantID(string id)
        {
            participantID = id;
        }

        /// <summary>
        /// Get current participant ID.
        /// </summary>
        public string GetParticipantID()
        {
            return participantID;
        }

        /// <summary>
        /// Stop the experiment early.
        /// </summary>
        public void StopExperiment()
        {
            if (!isRunning) return;

            StopAllCoroutines();
            CleanupCurrentPhase();
            isRunning = false;
            SetPhase(ExperimentPhase.Idle);
        }

        private IEnumerator RunExperimentSequence()
        {
            Debug.Log("[Experiment] Starting experiment sequence");

            // CalibrationCheck and SmoothPursuit phases are intentionally not
            // run here — the dedicated calibrator scene's Fixation / Tuning /
            // Verification / SmoothPursuit tests run immediately before this
            // scene loads in the standard MainMenu flow. The phase enum values
            // (and their CSV metadata columns) stay intact so historical CSVs
            // still parse.

            // Phase 1: Free Exploration
            yield return RunPhase(ExperimentPhase.FreeExploration,
                "FREE EXPLORATION\n\nLook around the room freely.\nExplore everything you see.",
                RunFreeExploration());

            yield return new WaitForSeconds(interPhaseDelay);

            // Phase 2: Visual Search
            yield return RunPhase(ExperimentPhase.VisualSearch,
                "VISUAL SEARCH\n\nFind the RED sphere among the blue ones.\nLook at it when you find it.",
                RunVisualSearch());

            yield return new WaitForSeconds(interPhaseDelay);

            // Phase 3: Counting Task
            yield return RunPhase(ExperimentPhase.CountingTask,
                "COUNTING TASK\n\nCount the RED cubes in the room.\nRemember the number.",
                RunCountingTask());

            yield return new WaitForSeconds(interPhaseDelay);

            // Phase 4: Change Detection.
            yield return RunPhase(ExperimentPhase.ChangeDetection,
                "CHANGE DETECTION\n\nStudy the scene carefully.\nAfter a brief blank, find what changed.",
                RunChangeDetection());

            // Complete
            CompleteExperiment();
        }

        private IEnumerator RunPhase(ExperimentPhase phase, string instructions, IEnumerator phaseRoutine)
        {
            // Show instructions
            SetPhase(ExperimentPhase.Instructions);
            if (experimentUI != null)
            {
                // Defensive: hide any progress banner before showing the next
                // phase's instructions. Per-frame ShowProgress calls in timed
                // phases leave the panel active across phase boundaries, so
                // hide here to keep the boundary clean regardless of what the
                // previous Run* method did.
                experimentUI.HideProgress();
                experimentUI.ShowInstruction(instructions);
            }
            yield return new WaitForSeconds(instructionDisplayTime);

            // 3-2-1-Go countdown between instructions and phase content.
            // SetInstructionTextOnly avoids repositioning the panel at every
            // digit; position is set once by the ShowInstruction call above,
            // then stays put through the countdown.
            if (experimentUI != null)
            {
                for (int i = 3; i >= 1; i--)
                {
                    experimentUI.SetInstructionTextOnly($"Starting in\n\n<size=200>{i}</size>");
                    yield return new WaitForSeconds(1f);
                }
                experimentUI.SetInstructionTextOnly("<size=180>Go!</size>");
                yield return new WaitForSeconds(0.4f);
            }
            else
            {
                yield return new WaitForSeconds(3.4f);
            }

            // Run the phase
            SetPhase(phase);
            if (experimentUI != null)
            {
                experimentUI.HideInstruction();
            }

            yield return phaseRoutine;
        }

        private void SetPhase(ExperimentPhase phase)
        {
            currentPhase = phase;
            Debug.Log($"[Experiment] Phase: {phase}");

            // Update eye tracker session context
            // Phase = main experimental phase (CalibrationCheck, FreeExploration, VisualSearch, etc.)
            // SubTask = more specific identifier within the phase (set by individual phase methods)
            if (eyeTracker != null)
            {
                string phaseName = phase.ToString();  // Keep original case for readability
                string defaultSubTask = GetDefaultSubTask(phase);  // Use phase-specific default, never empty
                sessionRecorder.SetSessionContext(0, phaseName, "SampleExperiment", defaultSubTask);

                // Record phase number as metadata
                sessionRecorder.SetMetadata("PhaseNumber", (int)phase);
            }

            OnPhaseChanged?.Invoke(phase);
        }

        /// <summary>
        /// Get the default SubTask name for a phase when no specific sub-task is active.
        /// This ensures SubTask is never empty/NaN in CSV output.
        /// </summary>
        private string GetDefaultSubTask(ExperimentPhase phase)
        {
            return phase switch
            {
                ExperimentPhase.Idle => "idle",
                ExperimentPhase.Instructions => "instructions",
                ExperimentPhase.CalibrationCheck => "calibration_start",
                ExperimentPhase.FreeExploration => "exploration",
                ExperimentPhase.VisualSearch => "search_start",
                ExperimentPhase.SmoothPursuit => "pursuit",
                ExperimentPhase.CountingTask => "counting",
                ExperimentPhase.ChangeDetection => "detection_start",
                ExperimentPhase.Complete => "complete",
                _ => phase.ToString().ToLower()
            };
        }

        #region Phase Implementations

        private IEnumerator RunCalibrationCheck()
        {
            // Create static calibration panel with all crosshairs
            CreateCalibrationPanel();

            // Run post-creation diagnostics after one frame (when renderers have updated)
            StartCoroutine(RunCalibrationDiagnostics());

            int totalPoints = calibrationConfig.gridRows * calibrationConfig.gridColumns;

            // Highlight each target in sequence
            for (int i = 0; i < totalPoints; i++)
            {
                int row = i / calibrationConfig.gridColumns;
                int col = i % calibrationConfig.gridColumns;

                // Update sub-task to identify which calibration point we're on
                if (eyeTracker != null)
                {
                    sessionRecorder.SetSubTask($"point_{i}_row{row}_col{col}");
                    sessionRecorder.SetMetadata("CalibrationPointIndex", i);
                    sessionRecorder.SetMetadata("CalibrationRow", row);
                    sessionRecorder.SetMetadata("CalibrationCol", col);

                    // Stamp the world-space target position so post-hoc analysis
                    // can compute angular error to each crosshair without
                    // re-deriving the panel layout.
                    if (i < calibrationTargets.Count && calibrationTargets[i] != null)
                    {
                        Vector3 tp = calibrationTargets[i].transform.position;
                        sessionRecorder.SetMetadata("CalibrationTargetX", tp.x);
                        sessionRecorder.SetMetadata("CalibrationTargetY", tp.y);
                        sessionRecorder.SetMetadata("CalibrationTargetZ", tp.z);
                    }
                }

                // Highlight current crosshair
                HighlightCalibrationTarget(i, true);

                // Wait for highlight duration
                yield return new WaitForSeconds(calibrationConfig.highlightDuration);

                // Return to default color
                HighlightCalibrationTarget(i, false);

                // Wait remaining duration for viewing
                float remainingDuration = calibrationConfig.targetDuration - calibrationConfig.highlightDuration;
                if (remainingDuration > 0)
                {
                    yield return new WaitForSeconds(remainingDuration);
                }
            }

            // Cleanup
            DestroyCalibrationTargets();
        }

        private GameObject calibrationPanel;

        /// <summary>
        /// Create the static calibration panel with black background and white crosses.
        /// Panel is positioned at a fixed location in the room (on front wall area).
        /// </summary>
        private void CreateCalibrationPanel()
        {
            // Create parent object for calibration panel
            calibrationPanel = new GameObject("CalibrationPanel");

            // Position panel at fixed location in front of typical user position
            // Panel faces toward Z = 0 (where user starts)
            float panelZ = calibrationConfig.targetDistance;
            float panelY = cameraTransform != null ? cameraTransform.position.y : 1.6f;
            calibrationPanel.transform.position = new Vector3(0, panelY, panelZ);
            calibrationPanel.transform.rotation = Quaternion.LookRotation(-Vector3.forward);  // Face toward origin

            Debug.Log($"[Calibration] Panel created at world position: {calibrationPanel.transform.position}");
            Debug.Log($"[Calibration] Panel rotation (euler): {calibrationPanel.transform.rotation.eulerAngles}");
            Debug.Log($"[Calibration] Panel forward direction: {calibrationPanel.transform.forward}");
            Debug.Log($"[Calibration] Camera position: {(cameraTransform != null ? cameraTransform.position.ToString() : "NULL")}");

            // Create black background panel with collider for vergence detection
            float bgWidth = calibrationConfig.gridWidth + 0.4f;
            float bgHeight = calibrationConfig.gridHeight + 0.4f;

            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "CalibrationBackground";
            background.transform.SetParent(calibrationPanel.transform);
            background.transform.localPosition = Vector3.zero;
            background.transform.localRotation = Quaternion.identity;
            background.transform.localScale = new Vector3(bgWidth, bgHeight, 1f);

            // REPLACE material for Android compatibility
            var bgMaterial = VRMaterialProvider.GetMaterial(Color.black);
            background.GetComponent<Renderer>().material = bgMaterial;

            Debug.Log($"[Calibration] Background quad: size={bgWidth}x{bgHeight}m, world pos={background.transform.position}");
            Debug.Log($"[Calibration] Background material: {(bgMaterial != null ? bgMaterial.name : "NULL")}, shader: {(bgMaterial != null ? bgMaterial.shader.name : "NULL")}");

            // Keep the collider on background for vergence point detection
            background.AddComponent<GazeTarget>();

            // Create crosshairs on the panel
            int gridRows = calibrationConfig.gridRows;
            int gridColumns = calibrationConfig.gridColumns;

            float cellWidth = gridColumns > 1 ? calibrationConfig.gridWidth / (gridColumns - 1) : 0;
            float cellHeight = gridRows > 1 ? calibrationConfig.gridHeight / (gridRows - 1) : 0;

            Debug.Log($"[Calibration] Grid: {gridRows}x{gridColumns}, cellSize: {cellWidth}x{cellHeight}m");
            Debug.Log($"[Calibration] Crosshair config: size={calibrationConfig.crosshairSize}m, thickness={calibrationConfig.lineThickness}m");
            Debug.Log($"[Calibration] Colors: default={calibrationConfig.defaultColor}, highlight={calibrationConfig.highlightColor}");

            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridColumns; col++)
                {
                    float x = -calibrationConfig.gridWidth / 2f + col * cellWidth;
                    float y = calibrationConfig.gridHeight / 2f - row * cellHeight;
                    // Crosshairs in FRONT of the panel: local +Z maps to world -Z (toward user)
                    Vector3 localPos = new Vector3(x, y, 0.05f);

                    var crosshair = CreateCrosshair(localPos, $"CalibrationCrosshair_{row}_{col}");
                    crosshair.transform.SetParent(calibrationPanel.transform);
                    crosshair.transform.localPosition = localPos;
                    crosshair.transform.localRotation = Quaternion.identity;

                    // Log first, last, and center crosshairs
                    int index = row * gridColumns + col;
                    if (index == 0 || index == gridRows * gridColumns - 1 || (row == gridRows/2 && col == gridColumns/2))
                    {
                        Debug.Log($"[Calibration] Crosshair[{row},{col}]: localPos={localPos}, worldPos={crosshair.transform.position}");
                    }

                    calibrationTargets.Add(crosshair);
                }
            }

            Debug.Log($"[Calibration] Created {calibrationTargets.Count} crosshairs total");
            Debug.Log($"[Calibration] Distance from camera to panel: {(cameraTransform != null ? Vector3.Distance(cameraTransform.position, calibrationPanel.transform.position) : panelZ)}m");
        }

        /// <summary>
        /// Create a single crosshair (white cross).
        /// </summary>
        private GameObject CreateCrosshair(Vector3 localPosition, string name)
        {
            GameObject crosshair = new GameObject(name);

            float armLength = calibrationConfig.crosshairSize;
            float thickness = calibrationConfig.lineThickness;

            // Horizontal line (white) - REPLACE material for Android compatibility
            GameObject hLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hLine.name = "HorizontalLine";
            hLine.transform.SetParent(crosshair.transform);
            hLine.transform.localPosition = Vector3.zero;
            hLine.transform.localScale = new Vector3(armLength * 2, thickness, thickness);
            var hMaterial = VRMaterialProvider.GetMaterial(calibrationConfig.defaultColor);
            hLine.GetComponent<Renderer>().material = hMaterial;
            Destroy(hLine.GetComponent<Collider>());

            // Vertical line (white) - REPLACE material
            GameObject vLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vLine.name = "VerticalLine";
            vLine.transform.SetParent(crosshair.transform);
            vLine.transform.localPosition = Vector3.zero;
            vLine.transform.localScale = new Vector3(thickness, armLength * 2, thickness);
            vLine.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(calibrationConfig.defaultColor);
            Destroy(vLine.GetComponent<Collider>());

            // Small center point for gaze detection - REPLACE material
            GameObject centerPoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            centerPoint.name = "CenterPoint";
            centerPoint.transform.SetParent(crosshair.transform);
            centerPoint.transform.localPosition = new Vector3(0, 0, 0.02f);  // Slightly in front of crosshair
            centerPoint.transform.localScale = Vector3.one * thickness * 5;  // Larger for visibility
            centerPoint.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(calibrationConfig.defaultColor);
            centerPoint.AddComponent<GazeTarget>();

            // Log details for first crosshair only
            if (name.EndsWith("_0_0"))
            {
                var hRenderer = hLine.GetComponent<Renderer>();
                Debug.Log($"[Calibration] First crosshair '{name}' details:");
                Debug.Log($"[Calibration]   H-Line scale: {hLine.transform.localScale} ({armLength * 2}m x {thickness}m)");
                Debug.Log($"[Calibration]   V-Line scale: {vLine.transform.localScale} ({thickness}m x {armLength * 2}m)");
                Debug.Log($"[Calibration]   CenterPoint scale: {centerPoint.transform.localScale}");
                Debug.Log($"[Calibration]   Material: {(hMaterial != null ? hMaterial.name : "NULL")}, color: {(hMaterial != null ? hMaterial.color.ToString() : "N/A")}");
                Debug.Log($"[Calibration]   Shader: {(hMaterial != null && hMaterial.shader != null ? hMaterial.shader.name : "NULL")}");
                Debug.Log($"[Calibration]   H-Line renderer enabled: {hRenderer.enabled}, visible: {hRenderer.isVisible}");
            }

            return crosshair;
        }

        /// <summary>
        /// Highlight or unhighlight a calibration crosshair.
        /// </summary>
        private void HighlightCalibrationTarget(int index, bool highlight)
        {
            if (index < 0 || index >= calibrationTargets.Count) return;

            Color targetColor = highlight ? calibrationConfig.highlightColor : calibrationConfig.defaultColor;
            var crosshair = calibrationTargets[index];

            foreach (var renderer in crosshair.GetComponentsInChildren<Renderer>())
            {
                if (renderer.material != null)
                {
                    renderer.material.color = targetColor;

                    // Also try common color properties for URP compatibility
                    if (renderer.material.HasProperty("_BaseColor"))
                    {
                        renderer.material.SetColor("_BaseColor", targetColor);
                    }
                    if (renderer.material.HasProperty("_Color"))
                    {
                        renderer.material.SetColor("_Color", targetColor);
                    }
                }
            }
        }

        /// <summary>
        /// Run diagnostic checks on calibration objects after they're created.
        /// This coroutine waits one frame for renderers to update visibility status.
        /// </summary>
        private IEnumerator RunCalibrationDiagnostics()
        {
            // Wait one frame for renderers to be processed
            yield return null;

            Debug.Log("=== [Calibration Diagnostics] Post-Creation Check ===");

            // Check camera
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Debug.Log($"[Diagnostics] Main camera: pos={mainCam.transform.position}, fwd={mainCam.transform.forward}");
                Debug.Log($"[Diagnostics] Camera near={mainCam.nearClipPlane}, far={mainCam.farClipPlane}, FOV={mainCam.fieldOfView}");
            }
            else
            {
                Debug.LogWarning("[Diagnostics] Main camera is NULL!");
            }

            // Check calibration panel
            if (calibrationPanel != null)
            {
                Debug.Log($"[Diagnostics] Panel: active={calibrationPanel.activeSelf}, layer={calibrationPanel.layer}");

                // Check background
                Transform bgTransform = calibrationPanel.transform.Find("CalibrationBackground");
                if (bgTransform != null)
                {
                    var bgRenderer = bgTransform.GetComponent<Renderer>();
                    if (bgRenderer != null)
                    {
                        Debug.Log($"[Diagnostics] Background: enabled={bgRenderer.enabled}, visible={bgRenderer.isVisible}, bounds={bgRenderer.bounds}");
                        Debug.Log($"[Diagnostics] Background material: {bgRenderer.material?.name ?? "NULL"}, color={bgRenderer.material?.color}");
                    }
                }
                else
                {
                    Debug.LogWarning("[Diagnostics] CalibrationBackground not found!");
                }

                // Check crosshairs
                int visibleCount = 0;
                int totalRenderers = 0;
                foreach (var crosshair in calibrationTargets)
                {
                    if (crosshair == null) continue;

                    foreach (var renderer in crosshair.GetComponentsInChildren<Renderer>())
                    {
                        totalRenderers++;
                        if (renderer.isVisible) visibleCount++;
                    }
                }
                Debug.Log($"[Diagnostics] Crosshairs: {calibrationTargets.Count} crosshairs, {totalRenderers} renderers, {visibleCount} visible");

                // Check first crosshair in detail
                if (calibrationTargets.Count > 0)
                {
                    var firstCrosshair = calibrationTargets[0];
                    Debug.Log($"[Diagnostics] First crosshair: worldPos={firstCrosshair.transform.position}, active={firstCrosshair.activeSelf}");

                    foreach (var renderer in firstCrosshair.GetComponentsInChildren<Renderer>())
                    {
                        Debug.Log($"[Diagnostics]   - {renderer.gameObject.name}: enabled={renderer.enabled}, visible={renderer.isVisible}, bounds={renderer.bounds}");

                        // Check if in camera frustum
                        if (mainCam != null)
                        {
                            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCam);
                            bool inFrustum = GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
                            Debug.Log($"[Diagnostics]   - {renderer.gameObject.name}: inCameraFrustum={inFrustum}");
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("[Diagnostics] Calibration panel is NULL!");
            }

            Debug.Log("=== [Calibration Diagnostics] End ===");
        }

        private void DestroyCalibrationTargets()
        {
            foreach (var target in calibrationTargets)
            {
                if (target != null) Destroy(target);
            }
            calibrationTargets.Clear();

            if (calibrationPanel != null)
            {
                Destroy(calibrationPanel);
                calibrationPanel = null;
            }
        }

        private IEnumerator RunFreeExploration()
        {
            // Stamp a phase-specific SubTask so post-hoc analysis can filter
            // free-viewing samples without inheriting the prior phase's value.
            if (eyeTracker != null)
            {
                sessionRecorder.SetSubTask("free_exploration");
            }

            // Generate interesting environment
            if (environmentGenerator != null)
            {
                environmentGenerator.StaticObjectCount = explorationConfig.staticObjectCount;
                environmentGenerator.DynamicObjectCount = explorationConfig.dynamicObjectCount;
                environmentGenerator.GenerateTestEnvironment();
            }

            // Show countdown timer in UI
            float elapsed = 0f;
            while (elapsed < explorationConfig.duration)
            {
                if (experimentUI != null)
                {
                    float remaining = explorationConfig.duration - elapsed;
                    experimentUI.ShowProgress($"Explore freely: {remaining:F0}s remaining");
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Clear environment for next phase
            if (environmentGenerator != null)
            {
                environmentGenerator.ClearTestObjects();
            }
            // Hide the progress banner so it doesn't bleed into the next
            // phase. RunPhase also calls HideProgress at the start of the
            // next phase; clearing here keeps the per-phase contract local.
            if (experimentUI != null)
            {
                experimentUI.HideProgress();
            }
        }

        private IEnumerator RunVisualSearch()
        {
            if (visualSearchManager == null)
            {
                Debug.LogWarning("[Experiment] VisualSearchManager not assigned, skipping visual search");
                yield break;
            }

            visualSearchManager.Configure(visualSearchConfig);

            for (int trial = 0; trial < visualSearchConfig.trialCount; trial++)
            {
                // Update sub-task to identify which trial we're on
                if (eyeTracker != null)
                {
                    sessionRecorder.SetSubTask($"trial_{trial + 1}");

                    // Record trial-level metadata for visual search
                    sessionRecorder.SetMetadata("SearchTrialNumber", trial + 1);
                    sessionRecorder.SetMetadata("DistractorCount", visualSearchConfig.distractorCount);
                    sessionRecorder.SetMetadata("TargetFound", false);  // Updated when found
                }

                // Run trial
                var result = new VisualSearchResult { trialNumber = trial + 1 };
                yield return visualSearchManager.RunTrial(trial, (found, time, pos) =>
                {
                    result.targetFound = found;
                    result.searchTimeSeconds = time;
                    result.targetPosition = pos;

                    // Update metadata with search result + world-space target
                    // position so analysis can compute "did the participant
                    // look at the target?" without joining the JSON results.
                    if (eyeTracker != null)
                    {
                        sessionRecorder.SetMetadata("TargetFound", found);
                        sessionRecorder.SetMetadata("SearchTime", time);
                        sessionRecorder.SetMetadata("SearchTargetX", pos.x);
                        sessionRecorder.SetMetadata("SearchTargetY", pos.y);
                        sessionRecorder.SetMetadata("SearchTargetZ", pos.z);
                        // Mark the trial as completed so analysis can find the
                        // response boundary from the SubTask transition.
                        sessionRecorder.SetSubTask($"trial_{trial + 1}_complete");
                    }
                });

                searchResults.Add(result);
                Debug.Log($"[Experiment] Visual search trial {trial + 1}: Found={result.targetFound}, Time={result.searchTimeSeconds:F2}s");

                // Brief pause between trials
                if (trial < visualSearchConfig.trialCount - 1)
                {
                    yield return new WaitForSeconds(1.0f);
                }
            }

            visualSearchManager.Cleanup();
        }

        private IEnumerator RunSmoothPursuit()
        {
            // Create pursuit target - REPLACE material for Android compatibility
            pursuitTarget = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pursuitTarget.name = "PursuitTarget";
            pursuitTarget.transform.localScale = Vector3.one * pursuitConfig.targetSize;

            // REPLACE material for reliable Android rendering
            pursuitTarget.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(pursuitConfig.targetColor);

            // Add dynamic target component for Figure-8 movement
            var dynamicTarget = pursuitTarget.AddComponent<DynamicTarget>();
            dynamicTarget.ChangeMovementPattern(MovementPattern.Figure8);
            dynamicTarget.speed = pursuitConfig.speed;
            dynamicTarget.radius = pursuitConfig.horizontalRadius;

            // Position center relative to camera
            float eyeHeight = cameraTransform != null ? cameraTransform.position.y : 1.6f;
            Vector3 center;
            if (cameraTransform != null)
            {
                Vector3 cameraForward = cameraTransform.forward;
                cameraForward.y = 0;
                // Check magnitude BEFORE normalizing (after normalize it's always 1)
                if (cameraForward.sqrMagnitude < 0.01f) cameraForward = Vector3.forward;
                else cameraForward.Normalize();
                center = cameraTransform.position + cameraForward * pursuitConfig.distance;
                center.y = eyeHeight;
            }
            else
            {
                center = new Vector3(0, eyeHeight, pursuitConfig.distance);
            }
            pursuitTarget.transform.position = center;
            dynamicTarget.SetCenter(center);

            // Add gaze target
            pursuitTarget.AddComponent<GazeTarget>();

            // Stamp the pursuit phase + initial target metadata. The position
            // is refreshed every frame below so post-hoc pursuit analysis can
            // compute lag/gain against the actual figure-8 trajectory.
            if (eyeTracker != null)
            {
                sessionRecorder.SetSubTask("pursuit_active");
            }

            // Run for duration with pulsing animation for better visibility
            float elapsed = 0f;
            float baseSize = pursuitConfig.targetSize;
            while (elapsed < pursuitConfig.duration)
            {
                if (experimentUI != null)
                {
                    float remaining = pursuitConfig.duration - elapsed;
                    experimentUI.ShowProgress($"Follow the target: {remaining:F0}s remaining");
                }

                // Pulsing animation - gently oscillate size for better visibility
                if (pursuitTarget != null)
                {
                    float pulse = 1f + 0.15f * Mathf.Sin(elapsed * 3f);
                    pursuitTarget.transform.localScale = Vector3.one * baseSize * pulse;

                    // Per-frame target position so each CSV row carries the
                    // ground truth needed for pursuit gain/lag analysis.
                    if (eyeTracker != null)
                    {
                        Vector3 tp = pursuitTarget.transform.position;
                        sessionRecorder.SetMetadata("PursuitTargetX", tp.x);
                        sessionRecorder.SetMetadata("PursuitTargetY", tp.y);
                        sessionRecorder.SetMetadata("PursuitTargetZ", tp.z);
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Cleanup
            if (pursuitTarget != null)
            {
                Destroy(pursuitTarget);
                pursuitTarget = null;
            }
        }

        private IEnumerator RunCountingTask()
        {
            if (countingTaskManager == null)
            {
                Debug.LogWarning("[Experiment] CountingTaskManager not assigned, skipping counting task");
                yield break;
            }

            // Set sub-task for counting phase
            if (eyeTracker != null)
            {
                sessionRecorder.SetSubTask("counting_objects");
            }

            countingTaskManager.Configure(countingConfig);
            int actualCount = countingTaskManager.GenerateScene();

            // Stamp the ground truth so analysis can score correctness from
            // CSV alone without joining the JSON results file.
            if (eyeTracker != null)
            {
                sessionRecorder.SetMetadata("CountingActualCount", actualCount);
            }

            // Show countdown during counting
            float elapsed = 0f;
            while (elapsed < countingConfig.duration)
            {
                if (experimentUI != null)
                {
                    float remaining = countingConfig.duration - elapsed;
                    experimentUI.ShowProgress($"Count the RED cubes: {remaining:F0}s remaining");
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Hide scene objects
            countingTaskManager.Cleanup();

            // Collect answer if enabled
            int reportedCount = -1;
            float responseTime = 0f;
            bool timedOut = true;

            if (countingConfig.collectAnswer)
            {
                if (eyeTracker != null)
                {
                    sessionRecorder.SetSubTask("answering");
                }

                if (experimentUI != null)
                {
                    experimentUI.ShowInstruction("How many RED cubes did you count?\n\nLook at your answer to select it.");
                }

                // Create answer UI
                var countingAnswerUI = gameObject.AddComponent<CountingAnswerUI>();

                // Position answer options in front of camera
                Vector3 answerPosition;
                if (cameraTransform != null)
                {
                    Vector3 forward = cameraTransform.forward;
                    forward.y = 0;
                    // Check magnitude BEFORE normalizing (after normalize it's always 1)
                    if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                    else forward.Normalize();
                    answerPosition = cameraTransform.position + forward * 2.5f;
                    answerPosition.y = cameraTransform.position.y;
                }
                else
                {
                    answerPosition = new Vector3(0, 1.6f, 2.5f);
                }

                bool answered = false;

                countingAnswerUI.ShowOptions(
                    actualCount,
                    countingConfig.answerOptionsRange,
                    answerPosition,
                    countingConfig.answerDwellTime,
                    (answer, time) =>
                    {
                        reportedCount = answer;
                        responseTime = time;
                        answered = true;
                        timedOut = false;

                        if (eyeTracker != null)
                        {
                            sessionRecorder.SetMetadata("CountingReportedCount", answer);
                        }
                    }
                );

                // Wait for answer or timeout
                float answerStartTime = Time.time;
                while (!answered && (Time.time - answerStartTime) < countingConfig.answerInputTimeout)
                {
                    yield return null;
                }

                // Cleanup answer UI
                if (countingAnswerUI != null)
                {
                    countingAnswerUI.Cleanup();
                    Destroy(countingAnswerUI);
                }

                // Show result briefly
                if (experimentUI != null)
                {
                    if (answered)
                    {
                        string resultText = reportedCount == actualCount
                            ? $"Correct! The answer was {actualCount}."
                            : $"Your answer: {reportedCount}\nActual count: {actualCount}";
                        experimentUI.ShowInstruction(resultText);
                    }
                    else
                    {
                        experimentUI.ShowInstruction($"Time's up!\nActual count: {actualCount}");
                    }
                }
                yield return new WaitForSeconds(2.0f);

                if (experimentUI != null)
                {
                    experimentUI.HideInstruction();
                }
            }

            // Store result
            results.countingResult = new CountingResult
            {
                actualCount = actualCount,
                reportedCount = reportedCount,
                isCorrect = reportedCount == actualCount,
                responseTimeSeconds = responseTime,
                timedOut = timedOut
            };

            Debug.Log($"[Experiment] Counting result: Actual={actualCount}, Reported={reportedCount}, Correct={reportedCount == actualCount}");
        }

        private IEnumerator RunChangeDetection()
        {
            if (changeDetectionManager == null)
            {
                Debug.LogWarning("[Experiment] ChangeDetectionManager not assigned, skipping change detection");
                yield break;
            }

            changeDetectionManager.Configure(changeDetectionConfig);

            for (int trial = 0; trial < changeDetectionConfig.trialCount; trial++)
            {
                // Trial-level metadata is stamped once; SubTask is updated at
                // each sub-phase boundary below so analysis can separate
                // encoding (study) from blank from retrieval (test) gaze.
                if (eyeTracker != null)
                {
                    sessionRecorder.SetMetadata("ChangeDetectionTrialNumber", trial + 1);
                }

                var result = new ChangeDetectionResult { trialNumber = trial + 1 };

                // Study phase
                if (eyeTracker != null)
                {
                    sessionRecorder.SetSubTask($"trial_{trial + 1}_study");
                }
                if (experimentUI != null)
                {
                    experimentUI.ShowProgress("Study the scene...");
                }
                changeDetectionManager.GenerateScene(trial);
                yield return new WaitForSeconds(changeDetectionConfig.studyDuration);

                // Blank phase
                if (eyeTracker != null)
                {
                    sessionRecorder.SetSubTask($"trial_{trial + 1}_blank");
                }
                if (experimentUI != null)
                {
                    experimentUI.ShowProgress("");
                }
                changeDetectionManager.HideScene();
                yield return new WaitForSeconds(changeDetectionConfig.blankDuration);

                // Test phase - show changed scene
                if (eyeTracker != null)
                {
                    sessionRecorder.SetSubTask($"trial_{trial + 1}_test");
                }
                if (experimentUI != null)
                {
                    experimentUI.ShowProgress("Find the change!");
                }

                yield return changeDetectionManager.RunTestPhase(trial, (detected, time, changeType, objectName) =>
                {
                    result.changeDetected = detected;
                    result.detectionTimeSeconds = time;
                    result.changeType = changeType;
                    result.changedObjectName = objectName;

                    // Outcome metadata so the CSV row at the moment of response
                    // captures the trial result without a JSON join.
                    if (eyeTracker != null)
                    {
                        sessionRecorder.SetMetadata("ChangeType", changeType ?? "");
                        sessionRecorder.SetMetadata("ChangedObjectName", objectName ?? "");
                        sessionRecorder.SetMetadata("ChangeDetected", detected);
                        sessionRecorder.SetMetadata("DetectionTime", time);
                    }
                });

                changeResults.Add(result);
                Debug.Log($"[Experiment] Change detection trial {trial + 1}: Detected={result.changeDetected}, Time={result.detectionTimeSeconds:F2}s");

                changeDetectionManager.Cleanup();

                // Brief pause between trials
                if (trial < changeDetectionConfig.trialCount - 1)
                {
                    yield return new WaitForSeconds(1.0f);
                }
            }
        }

        #endregion

        [Tooltip("Seconds to display the completion screen before auto-returning to MainMenu (when MainMenu is in the build). Set to 0 to disable auto-return and stay on the completion screen.")]
        [SerializeField] private float returnToMenuDelaySeconds = 8f;

        private void CompleteExperiment()
        {
            results.endTime = DateTime.Now;
            results.totalDurationSeconds = (float)(results.endTime - results.startTime).TotalSeconds;
            results.visualSearchResults = searchResults.ToArray();
            results.changeDetectionResults = changeResults.ToArray();

            // Save results
            SaveResults();

            // Show completion
            SetPhase(ExperimentPhase.Complete);

            isRunning = false;
            OnExperimentComplete?.Invoke(results);

            Debug.Log($"[Experiment] Complete. Duration: {results.totalDurationSeconds:F1}s");

            const string menuName = "MainMenu";
            bool menuInBuild = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(
                "Assets/Scenes/" + menuName + ".unity") >= 0;

            if (menuInBuild && returnToMenuDelaySeconds > 0f)
            {
                StartCoroutine(ReturnToMenuAfterCountdown(menuName));
            }
            else if (experimentUI != null)
            {
                experimentUI.ShowInstruction($"EXPERIMENT COMPLETE\n\nDuration: {results.totalDurationSeconds:F1} seconds\n\nThank you!");
            }
        }

        /// <summary>
        /// Display a live countdown on the completion screen, then load the
        /// MainMenu scene. Decoupled from CompleteExperiment so the timing
        /// is configurable via returnToMenuDelaySeconds and the no-MainMenu
        /// editor case can fall through to the static "Thank you" message.
        /// </summary>
        private IEnumerator ReturnToMenuAfterCountdown(string menuSceneName)
        {
            float remaining = returnToMenuDelaySeconds;
            while (remaining > 0f)
            {
                if (experimentUI != null)
                {
                    experimentUI.ShowInstruction(
                        $"EXPERIMENT COMPLETE\n\n" +
                        $"Duration: {results.totalDurationSeconds:F1} seconds\n\n" +
                        $"Thank you!\n\n" +
                        $"Returning to Main Menu in {Mathf.CeilToInt(remaining)}s...");
                }
                remaining -= Time.deltaTime;
                yield return null;
            }

            UnityEngine.SceneManagement.SceneManager.LoadScene(menuSceneName);
        }

        private void SaveResults()
        {
            try
            {
                string filename = $"experiment_results_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string directory = Application.persistentDataPath;

#if UNITY_ANDROID && !UNITY_EDITOR
                directory = Path.Combine(Application.persistentDataPath, "ExperimentResults");
#endif

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string path = Path.Combine(directory, filename);
                File.WriteAllText(path, results.ToJson());

                Debug.Log($"[Experiment] Results saved to: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Experiment] Failed to save results: {e.Message}");
            }
        }

        private void CleanupCurrentPhase()
        {
            DestroyCalibrationTargets();

            if (pursuitTarget != null)
            {
                Destroy(pursuitTarget);
                pursuitTarget = null;
            }

            if (environmentGenerator != null)
            {
                environmentGenerator.ClearTestObjects();
            }

            if (visualSearchManager != null) visualSearchManager.Cleanup();
            if (countingTaskManager != null) countingTaskManager.Cleanup();
            if (changeDetectionManager != null) changeDetectionManager.Cleanup();
        }

        private void OnDestroy()
        {
            CleanupCurrentPhase();
        }
    }
}
