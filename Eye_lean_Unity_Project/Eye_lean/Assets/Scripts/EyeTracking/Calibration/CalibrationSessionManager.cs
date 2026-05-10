using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using EyeTracking.Core;
using EyeTracking.Components;
using EyeTracking.Calibration.UI;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Manages the complete calibration/validation session flow.
    /// Coordinates test runners, UI, and data collection.
    ///
    /// The five-test design (fixation, saccade, smooth pursuit,
    /// tuning, verification) follows accepted eye-tracking
    /// methodology — see Holmqvist et al. 2011 (Oxford University
    /// Press) for the canonical reference, and `ACKNOWLEDGMENTS.md`
    /// at the project root for the full citation list.
    /// </summary>
    public class CalibrationSessionManager : MonoBehaviour
    {
        [Header("Session Configuration")]
        [Tooltip("Participant identifier written into the calibration CSV header. Leave blank to prompt at start (when requireParticipantID is true).")]
        [SerializeField] private string participantID = "";
        [Tooltip("Session number for this participant. Increments across multiple calibration sittings; written into the CSV header.")]
        [SerializeField] private int sessionNumber = 1;
        [Tooltip("If true, the session won't start until participantID is non-empty. Prevents accidental data loss from unnamed runs.")]
        [SerializeField] private bool requireParticipantID = true;

        [Header("Test Settings")]
        [Tooltip("Per-test thresholds and dwell timings. See CalibrationSettings for the field-level breakdown of accuracy windows, sample counts, and target geometry.")]
        [SerializeField] private CalibrationSettings settings;

        [Header("Test Selection")]
        [Tooltip("Run the fixation accuracy test (5×5 grid + center). Skip only for quick check-ins; the post-fit Verification phase reuses the fixation runner.")]
        [SerializeField] private bool includeFixationTest = true;
        [Tooltip("Run the smooth-pursuit test (gain + lag against a moving target).")]
        [SerializeField] private bool includeSmoothPursuitTest = true;
        [Tooltip("Run the saccade test (landing accuracy on a sequence of targets).")]
        [SerializeField] private bool includeSaccadeTest = true;
        [Tooltip("Append a free-exploration block at the end (no ground truth, just gaze trace). Off by default — most labs treat this as a separate session.")]
        [SerializeField] private bool includeFreeExplorationTest = false;

        [Header("UI Configuration")]
        [Tooltip("Build a CalibrationWorldUI from code on Start. Off if you've authored your own world UI and assigned it below.")]
        [SerializeField] private bool createUIAutomatically = true;
        [Tooltip("Hand-authored CalibrationWorldUI to use instead of an auto-built one. Leave empty to auto-build.")]
        [SerializeField] private CalibrationWorldUI existingUI;

        [Header("References")]
        [Tooltip("EyeTracker component (gaze + vergence). Auto-found if null.")]
        [SerializeField] private EyeTracker eyeTrackerComponent;
        [Tooltip("SessionRecorder (CSV + session context + metadata). Auto-found if null.")]
        [SerializeField] private SessionRecorder sessionRecorder;
        [Tooltip("Override for the user's HMD camera. Auto-resolved from Camera.main when left empty.")]
        [SerializeField] private Transform cameraTransform;

        // Eye tracker interface
        private IEyeTracker eyeTracker;

        // Test runners (created dynamically)
        private Dictionary<CalibrationTestType, CalibrationTestRunner> testRunners;
        private List<CalibrationScenario> scenarios;
        private int currentScenarioIndex = 0;

        // UI
        private CalibrationWorldUI worldUI;

        // Session state
        private CalibrationPhase currentPhase = CalibrationPhase.Setup;
        private bool sessionActive = false;
        private float sessionStartTime;
        private bool waitingForUserInput = false;

        // Results
        private CalibrationResults aggregatedResults;
        private List<GroundTruthSample> allSamples = new List<GroundTruthSample>();
        // Cache of the most recent per-test results (filled as each scenario
        // completes). Keyed by test type so re-runs of the same scenario
        // overwrite earlier results, and the aggregator can merge each test's
        // extended metrics into the final report without re-implementing
        // them at this layer.
        private Dictionary<CalibrationTestType, CalibrationResults> resultsByTestType
            = new Dictionary<CalibrationTestType, CalibrationResults>();

        // Events
        public event System.Action<CalibrationPhase> OnPhaseChanged;
        public event System.Action<CalibrationScenario> OnScenarioStarted;
        public event System.Action<CalibrationScenario, CalibrationResults> OnScenarioCompleted;
        public event System.Action<CalibrationResults> OnSessionCompleted;

        /// <summary>
        /// Current session phase.
        /// </summary>
        public CalibrationPhase CurrentPhase => currentPhase;

        /// <summary>
        /// Whether session is active.
        /// </summary>
        public bool IsSessionActive => sessionActive;

        /// <summary>
        /// Participant identifier.
        /// </summary>
        public string ParticipantID => participantID;

        /// <summary>
        /// Session number.
        /// </summary>
        public int SessionNumber => sessionNumber;

        void Awake()
        {
            if (settings == null)
            {
                settings = new CalibrationSettings();
            }

            testRunners = new Dictionary<CalibrationTestType, CalibrationTestRunner>();
        }

        void Start()
        {
            Initialize();
        }

        /// <summary>
        /// Initialize the session manager.
        /// </summary>
        private void Initialize()
        {
            // Get camera reference
            if (cameraTransform == null)
            {
                cameraTransform = Camera.main?.transform;
            }

            // Get eye tracker
            InitializeEyeTracker();

            // Create test runners
            CreateTestRunners();

            // Setup scenarios
            SetupScenarios();

            // Setup UI
            SetupUI();

            // Start in setup phase
            TransitionToPhase(CalibrationPhase.Setup);

            Debug.Log($"[CalibrationSessionManager] Initialized with {scenarios.Count} scenarios");
        }

        /// <summary>
        /// Resolve EyeTracker + SessionRecorder + IEyeTracker references.
        /// </summary>
        private void InitializeEyeTracker()
        {
            if (eyeTrackerComponent == null) eyeTrackerComponent = FindFirstObjectByType<EyeTracker>();
            if (sessionRecorder == null) sessionRecorder = FindFirstObjectByType<SessionRecorder>();
            if (eyeTrackerComponent == null) Debug.LogWarning("[CalibrationSessionManager] EyeTracker component not found in scene.");
            if (sessionRecorder == null) Debug.LogWarning("[CalibrationSessionManager] SessionRecorder component not found in scene.");

            eyeTracker = EyeTrackerFactory.GetEyeTracker();
            if (eyeTracker == null)
            {
                Debug.LogWarning("[CalibrationSessionManager] No eye tracker found. Tests will run without tracking data.");
            }
        }

        /// <summary>
        /// Create test runner components.
        /// </summary>
        private void CreateTestRunners()
        {
            // Create a child object for test runners
            GameObject runnersParent = new GameObject("TestRunners");
            runnersParent.transform.SetParent(transform);

            // Create each enabled test runner
            if (includeFixationTest)
            {
                var runner = runnersParent.AddComponent<FixationTestRunner>();
                runner.Initialize(eyeTracker, settings);
                runner.OnTestCompleted += HandleTestCompleted;
                runner.OnProgressUpdated += HandleProgressUpdated;
                testRunners[CalibrationTestType.Fixation] = runner;
            }

            if (includeSmoothPursuitTest)
            {
                var runner = runnersParent.AddComponent<SmoothPursuitTestRunner>();
                runner.Initialize(eyeTracker, settings);
                runner.OnTestCompleted += HandleTestCompleted;
                runner.OnProgressUpdated += HandleProgressUpdated;
                testRunners[CalibrationTestType.SmoothPursuit] = runner;
            }

            if (includeSaccadeTest)
            {
                var runner = runnersParent.AddComponent<SaccadeTestRunner>();
                runner.Initialize(eyeTracker, settings);
                runner.OnTestCompleted += HandleTestCompleted;
                runner.OnProgressUpdated += HandleProgressUpdated;
                testRunners[CalibrationTestType.Saccade] = runner;
            }

            // Note: FreeExploration runner would need to be created similarly

            Debug.Log($"[CalibrationSessionManager] Created {testRunners.Count} test runners");
        }

        /// <summary>
        /// Setup test scenarios based on configuration.
        /// </summary>
        private void SetupScenarios()
        {
            scenarios = new List<CalibrationScenario>();
            CalibrationScenario[] defaultScenarios = settings.CreateDefaultScenarios();

            foreach (var scenario in defaultScenarios)
            {
                bool include = false;
                switch (scenario.type)
                {
                    case CalibrationTestType.Fixation:
                        include = includeFixationTest;
                        break;
                    case CalibrationTestType.SmoothPursuit:
                        include = includeSmoothPursuitTest;
                        break;
                    case CalibrationTestType.Saccade:
                        include = includeSaccadeTest;
                        break;
                    case CalibrationTestType.FreeExploration:
                        include = includeFreeExplorationTest;
                        break;
                }

                if (include)
                {
                    scenarios.Add(scenario);
                }
            }
        }

        /// <summary>
        /// Setup UI components.
        /// </summary>
        private void SetupUI()
        {
            if (existingUI != null)
            {
                worldUI = existingUI;
            }
            else if (createUIAutomatically)
            {
                GameObject uiObject = new GameObject("CalibrationWorldUI");
                uiObject.transform.SetParent(transform);
                worldUI = uiObject.AddComponent<CalibrationWorldUI>();
            }

            if (worldUI != null)
            {
                worldUI.OnStartClicked += HandleStartClicked;
                worldUI.OnNextClicked += HandleNextClicked;
            }
        }

        /// <summary>
        /// Transition to a new session phase.
        /// </summary>
        private void TransitionToPhase(CalibrationPhase newPhase)
        {
            CalibrationPhase previousPhase = currentPhase;
            currentPhase = newPhase;

            Debug.Log($"[CalibrationSessionManager] Phase: {previousPhase} -> {newPhase}");

            OnPhaseChanged?.Invoke(newPhase);

            switch (newPhase)
            {
                case CalibrationPhase.Setup:
                    HandleSetupPhase();
                    break;
                case CalibrationPhase.Instructions:
                    HandleInstructionsPhase();
                    break;
                case CalibrationPhase.Testing:
                    HandleTestingPhase();
                    break;
                case CalibrationPhase.Tuning:
                    HandleTuningPhase();
                    break;
                case CalibrationPhase.Verification:
                    HandleVerificationPhase();
                    break;
                case CalibrationPhase.Completion:
                    HandleCompletionPhase();
                    break;
                case CalibrationPhase.DataValidation:
                    HandleDataValidationPhase();
                    break;
            }
        }

        #region Phase Handlers

        private void HandleSetupPhase()
        {
            // Auto-generate participant ID if not pre-set in Inspector
            if (string.IsNullOrEmpty(participantID))
            {
                participantID = $"P_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                Debug.Log($"[CalibrationSessionManager] Auto-generated ID: {participantID}");
            }

            // Show welcome screen directly (skip ID input - not usable in VR)
            if (worldUI != null)
            {
                ShowWelcomeScreen();
            }
            else
            {
                TransitionToPhase(CalibrationPhase.Instructions);
            }

            waitingForUserInput = true;
        }

        private void ShowWelcomeScreen()
        {
            if (worldUI == null) return;

            string welcomeText = "Welcome to the Eye Tracking Calibration Session.\n\n" +
                                "This session will include:\n";

            foreach (var scenario in scenarios)
            {
                welcomeText += $"• {scenario.name}\n";
            }

            welcomeText += "\nPlease ensure you are comfortable and can see clearly.\n\n";

            if (!string.IsNullOrEmpty(participantID))
            {
                welcomeText += $"Participant: {participantID}\n";
            }

            welcomeText += "\nPress START when ready to begin.";

            worldUI.ShowInstructions("Eye Tracking Calibration", welcomeText);
            worldUI.ShowButtons(showStart: true, showNext: false);
        }

        private void HandleInstructionsPhase()
        {
            if (worldUI != null)
            {
                string instructions = "During this session:\n\n" +
                                     "1. Keep your head as still as possible\n" +
                                     "2. Follow targets with your eyes only\n" +
                                     "3. Look at targets when they appear\n" +
                                     "4. Blink naturally when needed\n\n" +
                                     "Press NEXT to begin the first test.";

                worldUI.ShowInstructions("Instructions", instructions);
                worldUI.ShowButtons(showStart: false, showNext: true);
            }
            else
            {
                // No UI - auto-proceed
                TransitionToPhase(CalibrationPhase.Testing);
            }

            waitingForUserInput = true;
        }

        private void HandleTestingPhase()
        {
            if (currentScenarioIndex >= scenarios.Count)
            {
                TransitionToPhase(CalibrationPhase.Completion);
                return;
            }

            CalibrationScenario scenario = scenarios[currentScenarioIndex];

            if (worldUI != null && waitingForUserInput)
            {
                // Show scenario instructions
                string text = scenario.instructions + "\n\n" +
                             $"Duration: {scenario.duration:F0} seconds\n" +
                             $"Test {currentScenarioIndex + 1} of {scenarios.Count}\n\n" +
                             "Press NEXT when ready to start.";

                worldUI.ShowInstructions(scenario.name, text);
                worldUI.ShowButtons(showStart: false, showNext: true);
            }
            else
            {
                // Start the test with the 3-2-1-Go countdown.
                StartCoroutine(StartCurrentScenarioWithCountdown());
            }
        }

        // Brief 3-2-1-Go countdown before each calibrator test fires. Renders
        // on a small centered countdownPanel (parallel to SampleExperiment)
        // so calibration targets remain visible around it.
        private System.Collections.IEnumerator StartCurrentScenarioWithCountdown()
        {
            if (worldUI != null)
            {
                // Render countdown on the small centered countdownPanel
                // (large digits) so calibration targets remain visible
                // around it.
                worldUI.ShowButtons(false, false);
                worldUI.ShowCountdown("3");
                yield return new WaitForSeconds(1f);
                worldUI.SetCountdownText("2");
                yield return new WaitForSeconds(1f);
                worldUI.SetCountdownText("1");
                yield return new WaitForSeconds(1f);
                worldUI.SetCountdownText("Go!");
                yield return new WaitForSeconds(0.4f);
                worldUI.HideCountdown();
            }
            else
            {
                yield return new WaitForSeconds(3.4f);
            }
            StartCurrentScenario();
        }

        private void StartCurrentScenario()
        {
            if (currentScenarioIndex >= scenarios.Count) return;

            CalibrationScenario scenario = scenarios[currentScenarioIndex];

            if (!testRunners.TryGetValue(scenario.type, out CalibrationTestRunner runner))
            {
                Debug.LogError($"[CalibrationSessionManager] No runner for test type: {scenario.type}");
                currentScenarioIndex++;
                HandleTestingPhase();
                return;
            }

            // Show the small progress panel (not SetVisible(false), which would
            // hide the progress bar too) so the participant sees a live "Time
            // remaining: Ns" countdown without the larger instruction panel
            // occluding the calibration targets. Progress is ticked by
            // HandleProgressUpdated for runners that emit it (SmoothPursuit)
            // and by RunFallbackProgressTimer for runners that don't (Fixation
            // / Saccade — discrete event-driven).
            if (worldUI != null)
            {
                worldUI.ShowProgressBar(scenario.name, scenario.duration);
            }
            if (fallbackProgressCoroutine != null) StopCoroutine(fallbackProgressCoroutine);
            fallbackProgressCoroutine = StartCoroutine(RunFallbackProgressTimer(scenario.duration));

            // Update logging context
            if (sessionRecorder != null)
            {
                sessionRecorder.SetSessionContext(currentScenarioIndex + 1, scenario.name, "Testing");
            }

            OnScenarioStarted?.Invoke(scenario);
            runner.StartTest();
            waitingForUserInput = false;

            Debug.Log($"[CalibrationSessionManager] Started scenario: {scenario.name}");
        }

        private void HandleCompletionPhase()
        {
            // Calculate aggregated results
            CalculateAggregatedResults();

            if (worldUI != null)
            {
                // Append the Return-to-Menu hint so the dwell-button affordance
                // is discoverable; the START handler at this phase loads the
                // MainMenu scene if it's in the build.
                string body = BuildResultsReport() + "\n[START] Return to Main Menu";
                worldUI.ShowInstructions("Session Complete", body);
                worldUI.ShowButtons(showStart: true, showNext: false);
            }

            OnSessionCompleted?.Invoke(aggregatedResults);
            sessionActive = false;

            Debug.Log($"[CalibrationSessionManager] Session completed. {aggregatedResults.GetSummary()}");
        }

        private void HandleDataValidationPhase()
        {
            // Optional post-processing and validation
            Debug.Log("[CalibrationSessionManager] Data validation complete");
        }

        // Holds the auto-fit profile while the user is on the Tuning prompt.
        // Cleared on save / skip.
        private EyeTracking.Configuration.EyeTrackingProfile draftProfile;
        private EyeTracking.Calibration.OffsetEstimator.Result draftFitResult;

        // Snapshot of the active profile BEFORE the auto-fit applied draftProfile.
        // Used to roll back if post-fit verification regresses vs pre-fit so we
        // never overwrite a better default with a worse one. Populated in
        // HandleTuningPhase, consulted in EvaluateAndCommitProfile.
        private EyeTracking.Configuration.EyeTrackingProfile preTuningProfile;
        // Path of the just-saved (but not yet promoted-to-default) profile.
        // Set in AcceptDraftProfileAndAdvance; consumed in
        // EvaluateAndCommitProfile after verification reports.
        private string pendingProfilePath;
        // Set true when post-fit verification regressed vs pre-fit and we
        // chose to keep the prior default. Surfaced on the Completion screen
        // so the researcher can see why the accuracy headline didn't change.
        private bool verificationRejected = false;

        // Verification phase state. Samples collected during the brief post-Save
        // re-test land in their own list (not allSamples) so the pre-fit
        // metrics in BuildResultsReport stay grounded in the original test.
        // verificationFixationResults holds the runner's per-test report from
        // the verification pass; the Completion screen renders it side-by-side
        // with the original pre-fit numbers when it's non-null.
        private bool inVerificationMode = false;
        private List<GroundTruthSample> verificationSamples = new List<GroundTruthSample>();
        private CalibrationResults? verificationFixationResults = null;
        // Saved for restoration after the verification's reduced fixation
        // settings — the verification temporarily shrinks the test so the
        // re-run is brief, then puts the originals back.
        private int savedVerificationTargetCount;
        private float savedVerificationDwellTime;

        /// <summary>
        /// Run the offset estimator on the collected fixation samples,
        /// build a draft EyeTrackingProfile, apply it (so any subsequent
        /// gaze readout is corrected), and prompt the user to save it.
        /// If the auto-fit cannot run (no samples, no head transform),
        /// skip straight to Completion.
        /// </summary>
        private void HandleTuningPhase()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null || allSamples.Count == 0)
            {
                Debug.Log("[CalibrationSessionManager] Tuning skipped (no head transform or no samples)");
                TransitionToPhase(CalibrationPhase.Completion);
                return;
            }

            draftFitResult = EyeTracking.Calibration.OffsetEstimator.FitCombinedOffset(
                allSamples, mainCam.transform);

            if (!draftFitResult.converged || draftFitResult.samplesUsed == 0)
            {
                Debug.Log("[CalibrationSessionManager] Tuning skipped (offset fit did not converge)");
                draftProfile = null;
                TransitionToPhase(CalibrationPhase.Completion);
                return;
            }

            // Build a draft profile from the fit + the live vergence config.
            //
            // CUMULATIVE CORRECTION: OffsetEstimator measures the residual
            // against the gaze direction *after* ActiveProfile's correction
            // has been applied (`s.actualGazeDirection` is post-correction),
            // so the fit's yaw/pitch are the *additional* offset needed on
            // top of whatever was already loaded. Compose by adding the fit
            // residual to the currently-active profile's offset; otherwise
            // each save overwrites the cumulative correction with just the
            // new residual, causing sessions to alternate between over- and
            // under-correcting.
            //
            // Additive composition matches a proper quaternion compose to
            // within rounding error for sub-degree corrections.
            var prior = EyeTracking.Configuration.ActiveProfile.Current;
            float priorYaw = prior?.combinedGaze != null ? prior.combinedGaze.gazeYawOffsetDeg : 0f;
            float priorPitch = prior?.combinedGaze != null ? prior.combinedGaze.gazePitchOffsetDeg : 0f;
            draftProfile = new EyeTracking.Configuration.EyeTrackingProfile();
            draftProfile.combinedGaze.gazeYawOffsetDeg = priorYaw + draftFitResult.yawOffsetDeg;
            draftProfile.combinedGaze.gazePitchOffsetDeg = priorPitch + draftFitResult.pitchOffsetDeg;
            draftProfile.accuracyThresholdDeg = settings != null ? settings.accuracyThreshold : 2f;
            draftProfile.metadata.profileName = $"{SystemInfo.deviceModel}_{System.DateTime.Now:yyyyMMdd_HHmmss}";
            draftProfile.metadata.participantID = participantID ?? "";
            draftProfile.metadata.createdAt = System.DateTime.UtcNow.ToString("o");
            draftProfile.metadata.deviceModel = SystemInfo.deviceModel;
            draftProfile.metadata.eyeleanAppVersion = Application.version;

            // Snapshot the active profile BEFORE applying the draft so we can
            // roll back if post-fit verification turns out worse than pre-fit.
            // ActiveProfile.Apply(null) restores the no-correction state, so
            // a null snapshot here is a valid rollback target on first
            // calibration runs (no _default.json existed).
            preTuningProfile = prior;

            // Apply immediately so the user's subsequent test (if any) sees
            // the correction. Saving to disk is gated on the user's choice
            // at the prompt below.
            EyeTracking.Configuration.ActiveProfile.Apply(draftProfile);

            if (worldUI != null)
            {
                string body = BuildTuningPromptBody();
                worldUI.ShowInstructions("Calibration Complete", body);
                worldUI.ShowButtons(showStart: true, showNext: true);
            }
            else
            {
                // Headless path (editor without UI): auto-save and continue.
                Debug.LogWarning("[CalibrationSessionManager] No worldUI; auto-saving draft profile and continuing.");
                AcceptDraftProfileAndAdvance();
            }
        }

        private string BuildTuningPromptBody()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Auto-fit complete from {draftFitResult.samplesUsed} fixation samples.");
            sb.AppendLine();
            sb.AppendLine("This session's residual (additional correction needed):");
            sb.AppendLine($"  • Yaw: {draftFitResult.yawOffsetDeg:+0.00;-0.00;0.00}°");
            sb.AppendLine($"  • Pitch: {draftFitResult.pitchOffsetDeg:+0.00;-0.00;0.00}°");
            sb.AppendLine();
            sb.AppendLine("Cumulative correction in saved profile:");
            sb.AppendLine($"  • Yaw: {draftProfile.combinedGaze.gazeYawOffsetDeg:+0.00;-0.00;0.00}°");
            sb.AppendLine($"  • Pitch: {draftProfile.combinedGaze.gazePitchOffsetDeg:+0.00;-0.00;0.00}°");
            sb.AppendLine();
            sb.AppendLine($"Median angular error:");
            sb.AppendLine($"  Before: {draftFitResult.preFitMedianErrorDeg:F2}°");
            sb.AppendLine($"  After:  {draftFitResult.postFitMedianErrorDeg:F2}°");
            sb.AppendLine();
            sb.AppendLine("[START button] Save profile and apply");
            sb.AppendLine("[NEXT button]  Skip — continue without saving");
            return sb.ToString();
        }

        private void AcceptDraftProfileAndAdvance()
        {
            if (draftProfile == null)
            {
                TransitionToPhase(CalibrationPhase.Completion);
                return;
            }

            try
            {
                // Archive to disk under a timestamped filename, but DEFER
                // promoting to _default.json until post-fit verification
                // confirms the new profile is at least as good as the
                // pre-fit baseline (handled in EvaluateAndCommitProfile).
                // Required to avoid overwriting a good default with a
                // verification-regressed profile.
                pendingProfilePath = EyeTracking.Configuration.EyeTrackingProfileApi
                    .SaveActiveAs(draftProfile.metadata.profileName, makeDefault: false);
                Debug.Log($"[CalibrationSessionManager] Profile archived (pending verification): {pendingProfilePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[CalibrationSessionManager] Failed to save profile: {e.Message}");
                pendingProfilePath = null;
            }

            verificationRejected = false;
            draftProfile = null;
            // Re-run a brief fixation test with the new profile applied so the
            // Completion screen shows post-correction metrics, not the
            // pre-correction ones the user just saw.
            TransitionToPhase(CalibrationPhase.Verification);
        }

        private void RejectDraftProfileAndAdvance()
        {
            // Clear the in-memory active profile so subsequent runs don't
            // see corrections the user explicitly skipped.
            EyeTracking.Configuration.ActiveProfile.Clear();
            draftProfile = null;
            // No profile -> no point in a verification re-test; the original
            // pre-fit metrics are already what the Completion screen needs.
            TransitionToPhase(CalibrationPhase.Completion);
        }

        /// <summary>
        /// Run a brief fixation re-test with the freshly-saved profile applied,
        /// so the Completion screen can show post-correction metrics instead
        /// of the pre-correction ones the user already saw on Tuning.
        ///
        /// Reduces fixationTargetCount and fixationDwellTime to verification-
        /// scale values for the duration of this run, then restores them in
        /// HandleTestCompleted. Reuses the existing FixationTestRunner so we
        /// don't duplicate target-placement / sample-recording logic.
        /// </summary>
        private void HandleVerificationPhase()
        {
            if (!testRunners.TryGetValue(CalibrationTestType.Fixation, out var runner))
            {
                Debug.LogWarning("[CalibrationSessionManager] Verification skipped: no fixation runner registered");
                TransitionToPhase(CalibrationPhase.Completion);
                return;
            }

            // Override fixation settings to a brief schedule for the re-test.
            // Restored at the verification's HandleTestCompleted hook.
            savedVerificationTargetCount = settings.fixationTargetCount;
            savedVerificationDwellTime = settings.fixationDwellTime;
            settings.fixationTargetCount = settings.verificationTargetCount;
            settings.fixationDwellTime = settings.verificationDwellTime;

            inVerificationMode = true;
            verificationSamples.Clear();
            verificationFixationResults = null;

            if (sessionRecorder != null)
            {
                sessionRecorder.SetSessionContext(0, "Verification", "PostFitVerification");
            }

            Debug.Log($"[CalibrationSessionManager] Verification: re-running {settings.verificationTargetCount}-target fixation test with new profile applied");
            // Show the same 3-2-1-Go countdown the pre-fit scenarios get so the
            // participant has time to re-fixate the center marker before
            // targets appear.
            StartCoroutine(StartVerificationWithCountdown(runner));
        }

        // The post-fit verification samples a different randomized subset of
        // targets than the pre-fit fixation test, so a small drop in measured
        // accuracy / median error is normal noise. Only roll back the new
        // profile when the regression exceeds these thresholds, which is well
        // above the run-to-run variance measured on VIVE Focus Vision
        // (~1-2 pp / ~0.1°).
        private const float VerificationAccuracyDropThresholdPct = 5f;
        private const float VerificationMedianRegressionThresholdDeg = 0.30f;

        // After post-fit verification: compare to pre-fit fixation results.
        // If post-fit is at least as good (within thresholds), promote the
        // archived profile to _default.json. Otherwise, leave _default.json
        // untouched and re-apply the pre-tuning active profile in memory so
        // subsequent scenes load with the prior (better) correction.
        private void EvaluateAndCommitProfile()
        {
            if (string.IsNullOrEmpty(pendingProfilePath))
            {
                // No archived profile to promote (save failed earlier or
                // user skipped). Nothing to commit, nothing to roll back.
                return;
            }

            bool regressed = false;
            string reason = null;

            if (!verificationFixationResults.HasValue)
            {
                regressed = true;
                reason = "verification produced no fixation result";
            }
            else if (!resultsByTestType.TryGetValue(CalibrationTestType.Fixation, out var preFitR)
                     || preFitR.fixationSettledSampleCount == 0)
            {
                regressed = true;
                reason = "no pre-fit fixation baseline available for comparison";
            }
            else
            {
                var post = verificationFixationResults.Value;
                bool accDropped = post.fixationSettledAccuracyPct
                                  < preFitR.fixationSettledAccuracyPct - VerificationAccuracyDropThresholdPct;
                bool medianRegressed = post.fixationMedianErrorDeg
                                       > preFitR.fixationMedianErrorDeg + VerificationMedianRegressionThresholdDeg;
                if (accDropped || medianRegressed)
                {
                    regressed = true;
                    reason = $"pre-fit {preFitR.fixationSettledAccuracyPct:F1}% / median {preFitR.fixationMedianErrorDeg:F2}° → " +
                             $"post-fit {post.fixationSettledAccuracyPct:F1}% / median {post.fixationMedianErrorDeg:F2}° " +
                             $"(thresholds: {VerificationAccuracyDropThresholdPct}pp accuracy drop, " +
                             $"+{VerificationMedianRegressionThresholdDeg:F2}° median)";
                }
            }

            // Apples-to-apples cross-check before rolling back. The
            // verification re-test uses verificationTargetCount targets at
            // verificationDwellTime — a much smaller sample than the pre-fit
            // fixation pass, with a different randomized target subset. Real
            // corrections can read as "regressed" by sampling noise alone.
            // OffsetEstimator already computed pre- and post-correction
            // medians on the SAME pre-fit fixation samples (same targets,
            // same trials). When that comparison shows the new profile
            // clearly reduces error on the pre-fit data, trust the fit over
            // the noisy verification.
            if (regressed && draftFitResult != null && draftFitResult.converged
                && draftFitResult.samplesUsed > 0
                && draftFitResult.postFitMedianErrorDeg
                   < draftFitResult.preFitMedianErrorDeg - 0.05f)
            {
                Debug.Log($"[CalibrationSessionManager] Post-fit verification flagged regression ({reason}), " +
                          $"but apples-to-apples fit reduces median on pre-fit samples " +
                          $"{draftFitResult.preFitMedianErrorDeg:F2}° → {draftFitResult.postFitMedianErrorDeg:F2}° " +
                          $"({draftFitResult.samplesUsed} samples). Trusting the fit; promoting profile.");
                regressed = false;
            }

            if (regressed)
            {
                verificationRejected = true;
                Debug.LogWarning($"[CalibrationSessionManager] Post-fit verification regressed: {reason}. " +
                                 "Keeping previous default profile; new profile archived at " + pendingProfilePath);
                // Restore the in-memory active profile to whatever was active
                // before the user accepted the draft. _default.json was never
                // overwritten, so subsequent scenes' EyeTrackerFactory bootstrap
                // will auto-load the same prior default.
                EyeTracking.Configuration.ActiveProfile.Apply(preTuningProfile);
            }
            else
            {
                // Post-fit met the bar. Promote the archive to _default.json so
                // it auto-loads on the next session.
                try
                {
                    string defaultPath = System.IO.Path.Combine(
                        EyeTracking.Configuration.EyeTrackingProfile.DefaultDirectory,
                        EyeTracking.Configuration.EyeTrackingProfile.DefaultProfileFileName);
                    System.IO.File.Copy(pendingProfilePath, defaultPath, overwrite: true);
                    Debug.Log($"[CalibrationSessionManager] Verification passed; promoted profile to default: {defaultPath}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CalibrationSessionManager] Failed to promote profile to default: {e.Message}. " +
                                   "Profile remains archived at " + pendingProfilePath);
                }
            }

            pendingProfilePath = null;
            preTuningProfile = null;
        }

        // 3-2-1-Go countdown wrapper around a verification (post-fit) re-test.
        // Mirrors StartCurrentScenarioWithCountdown's pacing so pre-fit and
        // post-fit feel the same to the participant.
        private System.Collections.IEnumerator StartVerificationWithCountdown(CalibrationTestRunner runner)
        {
            if (worldUI != null)
            {
                // ShowCountdown re-activates the canvas (HideAll + countdownPanel
                // SetActive(true)), so we don't need to SetVisible(true)
                // separately. The full main panel stays hidden behind it.
                worldUI.ShowButtons(false, false);
                worldUI.ShowCountdown("3");
                yield return new WaitForSeconds(1f);
                worldUI.SetCountdownText("2");
                yield return new WaitForSeconds(1f);
                worldUI.SetCountdownText("1");
                yield return new WaitForSeconds(1f);
                worldUI.SetCountdownText("Go!");
                yield return new WaitForSeconds(0.4f);
                worldUI.HideCountdown();
                worldUI.SetVisible(false);
            }
            else
            {
                yield return new WaitForSeconds(3.4f);
            }
            runner.StartTest();
        }

        /// <summary>
        /// Render the extended per-test metrics report shown on the
        /// in-headset completion screen. See CalibrationResults xmldoc for
        /// the metric definitions.
        /// </summary>
        private string BuildResultsReport()
        {
            var r = aggregatedResults;
            var sb = new System.Text.StringBuilder();

            // The headline Quality Rating reflects whichever pass best
            // represents the eye-tracker's *current* state: when verification
            // was accepted (post-fit ≥ pre-fit within thresholds), that's the
            // post-fit rating. When verification regressed and we rolled
            // back to the prior default, the pre-fit rating is what the
            // researcher will actually be using next session.
            CalibrationResults headline = (verificationFixationResults.HasValue && !verificationRejected)
                ? verificationFixationResults.Value
                : r;
            sb.AppendLine($"Session completed with {scenarios.Count} tests.");
            sb.AppendLine();
            sb.AppendLine($"Quality Rating: {headline.GetQualityRating()}");
            if (verificationFixationResults.HasValue && !verificationRejected)
            {
                sb.AppendLine("(post-fit verification)");
            }
            else if (verificationRejected)
            {
                sb.AppendLine("(post-fit regressed — kept previous profile)");
            }
            sb.AppendLine();

            float threshold = settings != null ? settings.accuracyThreshold : 2f;

            if (r.fixationSettledSampleCount > 0)
            {
                sb.AppendLine("Fixation (steady-state)");
                if (verificationFixationResults.HasValue)
                {
                    var v = verificationFixationResults.Value;
                    sb.AppendLine($"  Pre-fit:  {r.fixationSettledAccuracyPct:F1}% within {threshold:F1}°, median {r.fixationMedianErrorDeg:F2}°, P95 {r.fixationP95ErrorDeg:F2}° ({r.fixationSettledSampleCount} samples)");
                    sb.AppendLine($"  Post-fit: {v.fixationSettledAccuracyPct:F1}% within {threshold:F1}°, median {v.fixationMedianErrorDeg:F2}°, P95 {v.fixationP95ErrorDeg:F2}° ({v.fixationSettledSampleCount} samples)");
                }
                else
                {
                    sb.AppendLine($"  • Within {threshold:F1}°: {r.fixationSettledAccuracyPct:F1}% of {r.fixationSettledSampleCount} settled samples");
                    sb.AppendLine($"  • Median error: {r.fixationMedianErrorDeg:F2}°    P95: {r.fixationP95ErrorDeg:F2}°");
                    int transition = r.fixationTotalSampleCount - r.fixationSettledSampleCount;
                    if (transition > 0)
                    {
                        sb.AppendLine($"  • ({transition} transition samples excluded)");
                    }
                }
                sb.AppendLine();
            }

            if (r.saccadeLandingSampleCount > 0)
            {
                sb.AppendLine("Saccade (landing accuracy)");
                sb.AppendLine($"  • Within {threshold:F1}°: {r.saccadeLandingAccuracyPct:F1}% of {r.saccadeLandingSampleCount} landing samples");
                sb.AppendLine($"  • Median error: {r.saccadeLandingMedianErrorDeg:F2}°    P95: {r.saccadeLandingP95ErrorDeg:F2}°");
                int flight = r.saccadeTotalSampleCount - r.saccadeLandingSampleCount;
                if (flight > 0)
                {
                    sb.AppendLine($"  • ({flight} in-flight samples excluded)");
                }
                sb.AppendLine();
            }

            if (r.pursuitSampleCount > 0)
            {
                sb.AppendLine("Smooth pursuit (lag and gain)");
                sb.AppendLine($"  • Median lag: {r.pursuitMedianLagDeg:F2}°    P95: {r.pursuitP95LagDeg:F2}°");
                sb.AppendLine($"  • Velocity gain: {r.pursuitGainEstimate:F2}  (1.00 = perfect tracking)");
                sb.AppendLine($"  • {r.pursuitSampleCount} samples");
                sb.AppendLine();
            }

            sb.AppendLine($"Session Duration: {Time.time - sessionStartTime:F0} seconds");
            sb.AppendLine();
            sb.AppendLine("Thank you for your participation!");

            return sb.ToString();
        }

        #endregion

        #region Event Handlers

        private void HandleStartClicked()
        {
            if (currentPhase == CalibrationPhase.Setup)
            {
                sessionActive = true;
                sessionStartTime = Time.time;
                TransitionToPhase(CalibrationPhase.Instructions);
            }
            else if (currentPhase == CalibrationPhase.Tuning)
            {
                // Tuning phase: Start = "Save profile and apply"
                AcceptDraftProfileAndAdvance();
            }
            else if (currentPhase == CalibrationPhase.Completion)
            {
                // Completion: Start = "Return to Main Menu". No-op if MainMenu
                // isn't in the build (e.g., editor-only single-scene runs).
                ReturnToMainMenuIfBuilt();
            }
        }

        /// <summary>
        /// Load the MainMenu scene if it's in the build. Falls back to a
        /// no-op log when only this scene is built (editor smoke tests).
        /// </summary>
        private void ReturnToMainMenuIfBuilt()
        {
            const string menuName = "MainMenu";
            if (UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(
                    "Assets/Scenes/" + menuName + ".unity") < 0)
            {
                Debug.Log("[CalibrationSessionManager] MainMenu scene not in build; staying on Completion screen.");
                return;
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(menuName);
        }

        private void HandleNextClicked()
        {
            switch (currentPhase)
            {
                case CalibrationPhase.Instructions:
                    currentScenarioIndex = 0;
                    TransitionToPhase(CalibrationPhase.Testing);
                    break;

                case CalibrationPhase.Testing:
                    if (waitingForUserInput)
                    {
                        waitingForUserInput = false;
                        // Use the coroutine wrapper so the press-Next branch
                        // also goes through the 3-2-1-Go countdown, matching
                        // the auto-proceed branch in HandleTestingPhase.
                        StartCoroutine(StartCurrentScenarioWithCountdown());
                    }
                    break;

                case CalibrationPhase.Tuning:
                    // Tuning phase: Next = "Skip — continue without saving"
                    RejectDraftProfileAndAdvance();
                    break;
            }
        }

        private void HandleTestCompleted(CalibrationTestRunner runner, List<GroundTruthSample> samples)
        {
            if (inVerificationMode)
            {
                // Brief post-Save re-test: route samples + results into the
                // verification fields so the Completion screen can compare
                // pre-fit and post-fit metrics. Restore the fixation-test
                // settings we shrank in HandleVerificationPhase.
                verificationSamples.AddRange(samples);
                verificationFixationResults = runner.GetResults();

                settings.fixationTargetCount = savedVerificationTargetCount;
                settings.fixationDwellTime = savedVerificationDwellTime;
                inVerificationMode = false;

                Debug.Log($"[CalibrationSessionManager] Verification complete: {samples.Count} samples, " +
                          $"settled fixation accuracy {verificationFixationResults.Value.fixationSettledAccuracyPct:F1}%, " +
                          $"median {verificationFixationResults.Value.fixationMedianErrorDeg:F2}°");

                // Decide: is the freshly-fit profile actually an improvement?
                // If post-fit regressed, roll the active profile back to the
                // pre-tuning snapshot and leave _default.json untouched.
                EvaluateAndCommitProfile();

                TransitionToPhase(CalibrationPhase.Completion);
                return;
            }

            // Collect samples
            allSamples.AddRange(samples);

            // Get results and cache per-test-type so CalculateAggregatedResults
            // can merge the runner's extended metrics into the final report.
            CalibrationResults results = runner.GetResults();
            resultsByTestType[runner.TestType] = results;

            // Find current scenario
            CalibrationScenario scenario = currentScenarioIndex < scenarios.Count
                ? scenarios[currentScenarioIndex]
                : default;

            OnScenarioCompleted?.Invoke(scenario, results);

            Debug.Log($"[CalibrationSessionManager] Scenario completed: {scenario.name}. " +
                     $"Samples: {samples.Count}, Accuracy: {results.accuracy:F1}%");

            // Move to next scenario
            currentScenarioIndex++;
            waitingForUserInput = true;

            if (currentScenarioIndex < scenarios.Count)
            {
                // Show next scenario instructions
                HandleTestingPhase();
            }
            else
            {
                // All scenarios complete — go through Tuning so the user
                // can review the auto-fit and save a profile before seeing
                // the final report. Tuning fast-forwards to Completion if
                // the auto-fit cannot run (e.g., no fixation samples).
                TransitionToPhase(CalibrationPhase.Tuning);
            }
        }

        private void HandleProgressUpdated(float progress)
        {
            if (worldUI != null)
            {
                CalibrationScenario scenario = currentScenarioIndex < scenarios.Count
                    ? scenarios[currentScenarioIndex]
                    : default;

                float remainingTime = scenario.duration * (1f - progress);
                worldUI.UpdateProgress(progress, remainingTime);
            }
        }

        // Fallback progress timer for runners that don't emit
        // OnProgressUpdated each frame (Fixation, Saccade — they report only
        // at discrete events). Ticks the same UpdateProgress channel based on
        // wall-clock so the participant sees a continuous countdown for every
        // scenario type. Cancelled and restarted at each scenario boundary.
        private Coroutine fallbackProgressCoroutine;
        private System.Collections.IEnumerator RunFallbackProgressTimer(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (worldUI != null)
                {
                    float progress = Mathf.Clamp01(elapsed / duration);
                    float remainingTime = Mathf.Max(0f, duration - elapsed);
                    worldUI.UpdateProgress(progress, remainingTime);
                }
            }
            fallbackProgressCoroutine = null;
        }

        #endregion

        #region Results

        /// <summary>
        /// Merge each test runner's extended per-test metrics into the
        /// session-level aggregatedResults. Each runner's GetResults already
        /// computes the right metric for its test type (fixation: settled-only
        /// accuracy + median/p95 error; saccade: landing accuracy + median;
        /// pursuit: median lag + gain). This method just copies the relevant
        /// fields from each cached per-test result into the aggregate.
        /// </summary>
        private void CalculateAggregatedResults()
        {
            aggregatedResults = new CalibrationResults();

            if (allSamples.Count == 0) return;

            aggregatedResults.totalSamples = allSamples.Count;
            aggregatedResults.sessionDuration = Time.time - sessionStartTime;
            aggregatedResults.completedScenarios = scenarios.Count;

            // Legacy isValid count for backward compatibility (one number).
            int legacyValid = 0;
            foreach (var s in allSamples) if (s.isValid) legacyValid++;
            aggregatedResults.validSamples = legacyValid;

            // Pull extended metrics from each cached per-test result.
            if (resultsByTestType.TryGetValue(CalibrationTestType.Fixation, out var fixR))
            {
                aggregatedResults.fixationSettledAccuracyPct = fixR.fixationSettledAccuracyPct;
                aggregatedResults.fixationMedianErrorDeg = fixR.fixationMedianErrorDeg;
                aggregatedResults.fixationP95ErrorDeg = fixR.fixationP95ErrorDeg;
                aggregatedResults.fixationSettledSampleCount = fixR.fixationSettledSampleCount;
                aggregatedResults.fixationTotalSampleCount = fixR.fixationTotalSampleCount;
                aggregatedResults.fixationAccuracy = fixR.fixationAccuracy; // legacy mirror
            }

            if (resultsByTestType.TryGetValue(CalibrationTestType.Saccade, out var sacR))
            {
                aggregatedResults.saccadeLandingAccuracyPct = sacR.saccadeLandingAccuracyPct;
                aggregatedResults.saccadeLandingMedianErrorDeg = sacR.saccadeLandingMedianErrorDeg;
                aggregatedResults.saccadeLandingP95ErrorDeg = sacR.saccadeLandingP95ErrorDeg;
                aggregatedResults.saccadeLandingSampleCount = sacR.saccadeLandingSampleCount;
                aggregatedResults.saccadeTotalSampleCount = sacR.saccadeTotalSampleCount;
                aggregatedResults.saccadeAccuracy = sacR.saccadeAccuracy; // legacy mirror
            }

            if (resultsByTestType.TryGetValue(CalibrationTestType.SmoothPursuit, out var purR))
            {
                aggregatedResults.pursuitMedianLagDeg = purR.pursuitMedianLagDeg;
                aggregatedResults.pursuitP95LagDeg = purR.pursuitP95LagDeg;
                aggregatedResults.pursuitGainEstimate = purR.pursuitGainEstimate;
                aggregatedResults.pursuitSampleCount = purR.pursuitSampleCount;
                aggregatedResults.pursuitAccuracy = purR.pursuitAccuracy; // legacy surrogate
            }

            // Compose the legacy single "% accuracy" field by averaging the
            // headline numbers from whichever tests ran. Kept only for the
            // small number of callers that read .accuracy directly; the
            // displayed report uses the per-test fields above.
            int parts = 0; float sum = 0f;
            if (aggregatedResults.fixationSettledSampleCount > 0)
            { sum += aggregatedResults.fixationSettledAccuracyPct; parts++; }
            if (aggregatedResults.saccadeLandingSampleCount > 0)
            { sum += aggregatedResults.saccadeLandingAccuracyPct; parts++; }
            if (aggregatedResults.pursuitSampleCount > 0)
            { sum += aggregatedResults.pursuitAccuracy; parts++; }
            aggregatedResults.accuracy = parts > 0 ? sum / parts : 0f;
            aggregatedResults.dataCompleteness = aggregatedResults.accuracy;
        }

        /// <summary>
        /// Get all collected ground truth samples.
        /// </summary>
        public IReadOnlyList<GroundTruthSample> GetAllSamples() => allSamples;

        /// <summary>
        /// Get aggregated results.
        /// </summary>
        public CalibrationResults GetResults() => aggregatedResults;

        #endregion

        #region Public API

        /// <summary>
        /// Set participant ID before starting session.
        /// </summary>
        public void SetParticipantID(string id)
        {
            participantID = id;
        }

        /// <summary>
        /// Set session number.
        /// </summary>
        public void SetSessionNumber(int number)
        {
            sessionNumber = number;
        }

        /// <summary>
        /// Start the calibration session programmatically.
        /// </summary>
        public void StartSession()
        {
            if (sessionActive)
            {
                Debug.LogWarning("[CalibrationSessionManager] Session already active");
                return;
            }

            HandleStartClicked();
        }

        /// <summary>
        /// Stop the current session.
        /// </summary>
        public void StopSession()
        {
            if (!sessionActive) return;

            // Stop any running tests
            foreach (var runner in testRunners.Values)
            {
                if (runner.IsRunning)
                {
                    runner.StopTest();
                }
            }

            sessionActive = false;
            CalculateAggregatedResults();
            OnSessionCompleted?.Invoke(aggregatedResults);

            Debug.Log("[CalibrationSessionManager] Session stopped");
        }

        /// <summary>
        /// Skip to the next scenario.
        /// </summary>
        public void SkipCurrentScenario()
        {
            if (!sessionActive || currentPhase != CalibrationPhase.Testing) return;

            // Stop current runner
            if (currentScenarioIndex < scenarios.Count)
            {
                var scenario = scenarios[currentScenarioIndex];
                if (testRunners.TryGetValue(scenario.type, out var runner) && runner.IsRunning)
                {
                    runner.StopTest();
                }
            }
        }

        #endregion

        void OnDestroy()
        {
            // Clean up event subscriptions
            if (worldUI != null)
            {
                worldUI.OnStartClicked -= HandleStartClicked;
                worldUI.OnNextClicked -= HandleNextClicked;
            }

            foreach (var runner in testRunners.Values)
            {
                runner.OnTestCompleted -= HandleTestCompleted;
                runner.OnProgressUpdated -= HandleProgressUpdated;
            }
        }
    }
}
