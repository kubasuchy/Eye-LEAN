using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using EyeTracking.Core;
using EyeTracking.Components;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Base class for calibration test runners.
    /// Provides common functionality for target creation, ground truth recording, and test execution.
    /// </summary>
    public abstract class CalibrationTestRunner : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Accuracy thresholds, dwell times and target geometry for this test. Normally driven by the parent CalibrationSessionManager — leave empty unless you're running this runner standalone.")]
        [SerializeField] protected CalibrationSettings settings;

        [Header("References")]
        [Tooltip("Override for the user's HMD camera. Auto-resolved from Camera.main when left empty.")]
        [SerializeField] protected Transform cameraTransform;

        // Eye tracker reference (set by CalibrationSessionManager)
        protected IEyeTracker eyeTracker;

        // EyeTracker for vergence point access. Per-scene component; do not
        // mark DontDestroyOnLoad — gaze rays must rebind on scene transitions.
        private EyeTracker simpleEyeTracker;

        // Ground truth samples collected during test
        protected List<GroundTruthSample> samples = new List<GroundTruthSample>();

        // I-VT fixation gate threshold (degrees/second). Samples whose
        // inter-sample angular gaze velocity exceeds this are saccades or
        // microsaccade bursts and are excluded from settled-fixation
        // aggregation. 30°/s is the conventional saccade threshold; settled
        // fixations run far below it.
        public const float FixationVelocityCapDegPerSec = 30f;

        // Test state
        protected bool isRunning = false;
        protected float testStartTime;
        protected Coroutine activeTestCoroutine;

        // Active-target tracking — used by RecordSample to stamp each sample
        // with how long the current target has been on screen and its
        // instantaneous velocity. Derived runners call MarkTargetOnset when
        // they swap to a new target (or when the moving pursuit target
        // begins moving).
        protected GameObject currentActiveTarget;
        protected float currentTargetOnsetSessionTime;
        protected Vector3 lastTargetSampledPosition;
        protected float lastTargetSampledTime;
        protected Vector3 lastTargetVelocity;

        // Events
        public event System.Action<CalibrationTestRunner> OnTestStarted;
        public event System.Action<CalibrationTestRunner, List<GroundTruthSample>> OnTestCompleted;
        public event System.Action<float> OnProgressUpdated;

        /// <summary>
        /// The type of test this runner executes.
        /// </summary>
        public abstract CalibrationTestType TestType { get; }

        /// <summary>
        /// Whether the test is currently running.
        /// </summary>
        public bool IsRunning => isRunning;

        /// <summary>
        /// Samples collected during the test.
        /// </summary>
        public IReadOnlyList<GroundTruthSample> Samples => samples;

        protected virtual void Awake()
        {
            if (settings == null)
            {
                settings = new CalibrationSettings();
            }
        }

        protected virtual void Start()
        {
            if (cameraTransform == null)
            {
                cameraTransform = Camera.main?.transform;
            }

            // Find EyeTracker for vergence point access
            simpleEyeTracker = FindFirstObjectByType<EyeTracker>();
        }

        /// <summary>
        /// Initialize the test runner with required references.
        /// </summary>
        public virtual void Initialize(IEyeTracker tracker, CalibrationSettings testSettings)
        {
            eyeTracker = tracker;

            if (testSettings != null)
            {
                settings = testSettings;
            }

            Debug.Log($"[{GetType().Name}] Initialized");
        }

        /// <summary>
        /// Start running the test.
        /// </summary>
        public virtual void StartTest()
        {
            if (isRunning)
            {
                Debug.LogWarning($"[{GetType().Name}] Test already running");
                return;
            }

            if (eyeTracker == null)
            {
                Debug.LogError($"[{GetType().Name}] Cannot start test - no eye tracker assigned");
                return;
            }

            samples.Clear();
            sampleAttempts = 0;
            sampleFailures = 0;
            testStartTime = Time.time;
            currentActiveTarget = null;
            currentTargetOnsetSessionTime = 0f;
            lastTargetSampledPosition = Vector3.zero;
            lastTargetSampledTime = 0f;
            lastTargetVelocity = Vector3.zero;
            isRunning = true;

            OnTestStarted?.Invoke(this);
            activeTestCoroutine = StartCoroutine(RunTestCoroutine());

            Debug.Log($"[{GetType().Name}] Test started. EyeTracker available: {eyeTracker?.IsAvailable ?? false}");
        }

        /// <summary>
        /// Stop the test early.
        /// </summary>
        public virtual void StopTest()
        {
            if (!isRunning) return;

            if (activeTestCoroutine != null)
            {
                StopCoroutine(activeTestCoroutine);
                activeTestCoroutine = null;
            }

            CleanupTest();
            isRunning = false;

            OnTestCompleted?.Invoke(this, samples);

            Debug.Log($"[{GetType().Name}] Test stopped. Collected {samples.Count} samples.");
        }

        /// <summary>
        /// Main test execution coroutine. Override in derived classes.
        /// </summary>
        protected abstract IEnumerator RunTestCoroutine();

        /// <summary>
        /// Cleanup test resources. Override in derived classes.
        /// </summary>
        protected abstract void CleanupTest();

        // Track sample recording failures for debugging
        private int sampleAttempts = 0;
        private int sampleFailures = 0;

        /// <summary>
        /// Mark the start of a new target's on-screen window. Subsequent
        /// samples will be stamped with `targetSettleSeconds` measured from
        /// this moment, and target velocity will be sampled afresh.
        /// Derived runners must call this each time they swap which target
        /// the participant is meant to look at; for smooth pursuit, call
        /// once at the start of the test (the same target moves throughout).
        /// </summary>
        protected void MarkTargetOnset(GameObject newTarget)
        {
            currentActiveTarget = newTarget;
            currentTargetOnsetSessionTime = Time.time - testStartTime;
            lastTargetSampledPosition = newTarget != null ? newTarget.transform.position : Vector3.zero;
            lastTargetSampledTime = Time.time;
            lastTargetVelocity = Vector3.zero;
            // Reset per-target gaze-velocity tracking so the first sample on
            // a new target reads velocity = 0 instead of a huge value
            // representing the saccade from the previous target's location.
            lastGazeDirection = Vector3.zero;
            lastGazeSampleTime = 0f;
        }

        // Tracking state for per-sample gaze angular velocity (R8 I-VT gate).
        // Reset by MarkTargetOnset so saccades between targets aren't
        // counted as in-fixation velocity.
        private Vector3 lastGazeDirection = Vector3.zero;
        private float lastGazeSampleTime = 0f;

        /// <summary>
        /// Record a ground truth sample for the given target.
        /// </summary>
        protected void RecordSample(GameObject target, string eventType)
        {
            sampleAttempts++;

            if (eyeTracker == null)
            {
                if (sampleFailures++ < 5)
                    Debug.LogWarning($"[{GetType().Name}] RecordSample: eyeTracker is null");
                return;
            }

            if (target == null)
            {
                if (sampleFailures++ < 5)
                    Debug.LogWarning($"[{GetType().Name}] RecordSample: target is null");
                return;
            }

            // Get current gaze data
            Vector3 gazeOrigin, gazeDirection;
            GazeSource gazeSource;
            bool hasValidGaze = GetCurrentGazeData(out gazeOrigin, out gazeDirection, out gazeSource);

            if (!hasValidGaze)
            {
                if (sampleFailures++ < 5)
                    Debug.LogWarning($"[{GetType().Name}] RecordSample: No valid gaze data (attempt {sampleAttempts})");
                return;
            }

            // Calculate intended gaze direction. Anchored at the eye-tracker-
            // reported gaze origin (not cameraTransform.position) so that the
            // stored intendedGazeDirection is consistent with how gazeError is
            // computed downstream (CalculateGazeError also uses gazeOrigin)
            // and with how OffsetEstimator recomputes residuals from
            // (s.targetPosition - s.actualGazeOrigin) at fit time.
            Vector3 targetPosition = target.transform.position;
            Vector3 intendedDirection = (targetPosition - gazeOrigin).normalized;

            // Calculate surface intersection
            Vector3 surfacePoint = targetPosition;
            bool hasSurfaceHit = false;
            bool raycastHitTarget = false;
            float surfaceError = 0f;

            RaycastHit hit;
            if (Physics.Raycast(gazeOrigin, gazeDirection, out hit, 100f))
            {
                surfacePoint = hit.point;
                hasSurfaceHit = true;
                surfaceError = CalculateGazeError(gazeOrigin, gazeDirection, surfacePoint);

                // Check if raycast hit the actual target object
                raycastHitTarget = (hit.collider.gameObject == target);
            }

            // Check if vergence point is within target bounds
            bool vergenceHitTarget = false;
            if (simpleEyeTracker != null)
            {
                var vergenceResult = simpleEyeTracker.GetCurrentVergenceResult();
                if (vergenceResult.isValid)
                {
                    Collider targetCollider = target.GetComponent<Collider>();
                    if (targetCollider != null)
                    {
                        vergenceHitTarget = targetCollider.bounds.Contains(vergenceResult.finalVergencePoint);
                    }
                }
            }

            // Calculate gaze error to target center
            float gazeError = CalculateGazeError(gazeOrigin, gazeDirection, targetPosition);

            // Track validation reasons separately
            bool validByAngularError = gazeError < settings.accuracyThreshold;

            // Valid if: angular error is low OR gaze raycast hit target OR vergence point hit target
            bool isValidSample = validByAngularError || raycastHitTarget || vergenceHitTarget;

            float now = Time.time;
            float sessionTime = now - testStartTime;

            // Settle time: how long this target has been the active one.
            // Falls back to sessionTime when no target onset has been marked
            // (defensive default — makes settle-filtering still work for runners
            // that haven't yet been updated to call MarkTargetOnset).
            float settleSeconds = currentActiveTarget == target && currentTargetOnsetSessionTime > 0f
                ? sessionTime - currentTargetOnsetSessionTime
                : sessionTime;

            // Target velocity: numerical derivative of the target's world
            // position between successive samples. Zero for stationary
            // targets (they advance one sample with dt=0 contribution).
            Vector3 velocity = Vector3.zero;
            if (currentActiveTarget == target && lastTargetSampledTime > 0f)
            {
                float dt = now - lastTargetSampledTime;
                if (dt > 1e-4f)
                {
                    velocity = (targetPosition - lastTargetSampledPosition) / dt;
                }
                else
                {
                    velocity = lastTargetVelocity;
                }
            }
            lastTargetSampledPosition = targetPosition;
            lastTargetSampledTime = now;
            lastTargetVelocity = velocity;

            // Inter-sample angular gaze velocity (degrees/second). Used by
            // the I-VT fixation gate downstream. First sample on a target
            // (lastGazeDirection unset by MarkTargetOnset) reads 0.
            float gazeVelocityDegPerSec = 0f;
            if (lastGazeDirection.sqrMagnitude > 1e-6f && lastGazeSampleTime > 0f)
            {
                float gdt = now - lastGazeSampleTime;
                if (gdt > 1e-4f)
                {
                    float dot = Vector3.Dot(gazeDirection.normalized, lastGazeDirection.normalized);
                    float angleDeg = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
                    gazeVelocityDegPerSec = angleDeg / gdt;
                }
            }
            lastGazeDirection = gazeDirection;
            lastGazeSampleTime = now;

            // Create sample
            GroundTruthSample sample = new GroundTruthSample
            {
                timestamp = now,
                sessionTime = sessionTime,
                testType = eventType,
                targetID = target.name,
                targetPosition = targetPosition,
                surfaceIntersectionPoint = surfacePoint,
                intendedGazeDirection = intendedDirection,
                actualGazeOrigin = gazeOrigin,
                actualGazeDirection = gazeDirection,
                gazeError = gazeError,
                surfaceError = surfaceError,
                isFixating = TestType == CalibrationTestType.Fixation,
                isValid = isValidSample,
                isValidByAngularError = validByAngularError,
                hasSurfaceIntersection = hasSurfaceHit,
                targetSettleSeconds = settleSeconds,
                targetVelocity = velocity,
                gazeSource = gazeSource,
                gazeAngularVelocityDegPerSec = gazeVelocityDegPerSec
            };

            samples.Add(sample);
        }

        /// <summary>
        /// Get current gaze data from eye tracker. Reports which path
        /// produced the data so downstream filters can reject mid-test
        /// source switches.
        /// </summary>
        protected bool GetCurrentGazeData(out Vector3 origin, out Vector3 direction, out GazeSource source)
        {
            origin = Vector3.zero;
            direction = Vector3.forward;
            source = GazeSource.Unknown;

            // Try to get a working eye tracker if current one isn't available
            if (eyeTracker == null || !eyeTracker.IsAvailable)
            {
                // Force re-detection of eye tracking hardware
                EyeTrackerFactory.Reinitialize();
                eyeTracker = EyeTrackerFactory.GetEyeTracker();

                if (eyeTracker == null || !eyeTracker.IsAvailable)
                {
                    return false;
                }
            }

            // Try combined gaze first
            bool hasOrigin = eyeTracker.GetCombinedGazeOrigin(out origin);
            bool hasDirection = eyeTracker.GetCombinedGazeDirection(out direction);

            if (hasOrigin && hasDirection)
            {
                source = GazeSource.Combined;
                return true;
            }

            // Fall back to individual eyes
            Vector3 leftOrigin, rightOrigin;
            Vector3 leftDir = Vector3.forward;
            Vector3 rightDir = Vector3.forward;
            bool hasLeft = eyeTracker.GetLeftEyeOrigin(out leftOrigin) && eyeTracker.GetLeftEyeDirection(out leftDir);
            bool hasRight = eyeTracker.GetRightEyeOrigin(out rightOrigin) && eyeTracker.GetRightEyeDirection(out rightDir);

            if (hasLeft && hasRight)
            {
                origin = (leftOrigin + rightOrigin) * 0.5f;
                direction = ((leftDir + rightDir) * 0.5f).normalized;
                source = GazeSource.BinocularAverage;
                return true;
            }

            if (hasLeft)
            {
                origin = leftOrigin;
                direction = leftDir;
                source = GazeSource.LeftMonocular;
                return true;
            }

            if (hasRight)
            {
                origin = rightOrigin;
                direction = rightDir;
                source = GazeSource.RightMonocular;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Complete the test, cleanup, and invoke the OnTestCompleted event.
        /// Call this from derived classes when the test finishes.
        /// </summary>
        protected void RaiseTestCompleted()
        {
            // Log sample collection statistics
            Debug.Log($"[{GetType().Name}] Test completing. " +
                     $"Sample attempts: {sampleAttempts}, Failures: {sampleFailures}, " +
                     $"Collected: {samples.Count}");

            // Clean up test resources BEFORE raising the event
            CleanupTest();

            // Destroy cached materials to prevent memory leaks
            DestroyCreatedMaterials();

            isRunning = false;

            OnTestCompleted?.Invoke(this, samples);
        }

        /// <summary>
        /// Calculate angular error between gaze and target.
        /// </summary>
        protected float CalculateGazeError(Vector3 gazeOrigin, Vector3 gazeDirection, Vector3 targetPosition)
        {
            Vector3 intendedDirection = (targetPosition - gazeOrigin).normalized;
            float dot = Vector3.Dot(gazeDirection.normalized, intendedDirection);
            float angleRadians = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
            return angleRadians * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Report progress (0-1) to listeners.
        /// </summary>
        protected void ReportProgress(float progress)
        {
            OnProgressUpdated?.Invoke(Mathf.Clamp01(progress));
        }

        /// <summary>
        /// Create a target sphere at the specified position. Position is
        /// interpreted as ROOM-LOCAL when an IRoomFrameProvider is present
        /// (calibration scenarios author target positions in the user-spawn-
        /// relative frame); otherwise placed in world space. Required so an
        /// off-axis user spawn (non-zero world XZ or yaw) does not push
        /// targets outside the rotated room.
        /// </summary>
        protected GameObject CreateTargetSphere(Vector3 position, string name, Color color, float size = 0.2f)
        {
            GameObject target = new GameObject(name);
            // Depend on the IRoomFrameProvider interface (Core), not a
            // concrete env-builder, so researchers can plug in their own.
            var roomFrame = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            Transform roomT = null;
            for (int i = 0; i < roomFrame.Length; i++)
            {
                if (roomFrame[i] is EyeTracking.Core.IRoomFrameProvider rfp)
                {
                    roomT = rfp.RoomTransform;
                    if (roomT != null) break;
                }
            }
            if (roomT != null)
            {
                target.transform.SetParent(roomT, worldPositionStays: false);
                target.transform.localPosition = position;
            }
            else
            {
                target.transform.position = position;
            }
            target.transform.localScale = Vector3.one * size;

            // Add mesh
            MeshFilter meshFilter = target.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = target.AddComponent<MeshRenderer>();
            meshFilter.mesh = CreateSphereMesh();

            // Add collider
            SphereCollider collider = target.AddComponent<SphereCollider>();
            collider.radius = 0.5f;

            // Set material
            Material material = CreateTargetMaterial(color);
            meshRenderer.material = material;

            return target;
        }

        /// <summary>
        /// Create a sphere mesh for targets.
        /// </summary>
        protected Mesh CreateSphereMesh()
        {
            GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh sourceMesh = tempSphere.GetComponent<MeshFilter>().mesh;

            Mesh meshCopy = new Mesh
            {
                vertices = sourceMesh.vertices,
                triangles = sourceMesh.triangles,
                normals = sourceMesh.normals,
                uv = sourceMesh.uv,
                name = "CalibrationTargetMesh"
            };

            DestroyImmediate(tempSphere);
            return meshCopy;
        }

        // Cache materials to prevent memory leaks
        private List<Material> createdMaterials = new List<Material>();

        /// <summary>
        /// Create a material for targets with fallback shaders.
        /// Materials are cached and destroyed during cleanup.
        /// </summary>
        protected Material CreateTargetMaterial(Color color)
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");

            // Safety check - if no shader found, use a basic material
            if (shader == null)
            {
                Debug.LogWarning($"[{GetType().Name}] No shader found for target material, using default");
                Material fallback = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("UI/Default"));
                fallback.color = color;
                createdMaterials.Add(fallback);
                return fallback;
            }

            Material material = new Material(shader);
            material.color = color;
            createdMaterials.Add(material);
            return material;
        }

        /// <summary>
        /// Destroy all cached materials to prevent memory leaks.
        /// Called automatically during cleanup.
        /// </summary>
        protected void DestroyCreatedMaterials()
        {
            foreach (var mat in createdMaterials)
            {
                if (mat != null)
                {
                    DestroyImmediate(mat);
                }
            }
            createdMaterials.Clear();
        }

        /// <summary>
        /// Compute extended per-test metrics from the collected samples.
        /// Different tests measure fundamentally different things and so
        /// produce different headline numbers; this dispatches to the
        /// appropriate aggregator based on the runner's TestType.
        /// </summary>
        public CalibrationResults GetResults()
        {
            CalibrationResults results = new CalibrationResults();

            if (samples.Count == 0) return results;

            results.totalSamples = samples.Count;
            results.sessionDuration = samples[samples.Count - 1].sessionTime;
            results.completedScenarios = 1;

            // Legacy aggregate (kept for any caller still reading .accuracy).
            int legacyValid = 0;
            foreach (var s in samples) if (s.isValid) legacyValid++;
            results.validSamples = legacyValid;

            switch (TestType)
            {
                case CalibrationTestType.Fixation:
                    AggregateFixation(ref results);
                    break;
                case CalibrationTestType.Saccade:
                    AggregateSaccade(ref results);
                    break;
                case CalibrationTestType.SmoothPursuit:
                    AggregatePursuit(ref results);
                    break;
            }

            // Compose a legacy single-number "accuracy" for backwards compat.
            // Average of whichever test-specific headline numbers were filled.
            int parts = 0; float sum = 0f;
            if (results.fixationSettledSampleCount > 0) { sum += results.fixationSettledAccuracyPct; parts++; }
            if (results.saccadeLandingSampleCount > 0)  { sum += results.saccadeLandingAccuracyPct; parts++; }
            if (results.pursuitSampleCount > 0)
            {
                // Map pursuit lag to a 0-100 "accuracy" surrogate so the legacy
                // field is at least monotonic in pursuit quality. 0° lag = 100,
                // 5° lag = 50, 10° lag = 0. Not a real metric — only use the
                // explicit pursuitMedianLagDeg / pursuitGainEstimate.
                float surrogate = Mathf.Clamp(100f - results.pursuitMedianLagDeg * 10f, 0f, 100f);
                results.pursuitAccuracy = surrogate;
                sum += surrogate; parts++;
            }
            results.accuracy = parts > 0 ? sum / parts : 0f;
            results.dataCompleteness = results.accuracy;

            return results;
        }

        // -----------------------------------------------------------------
        // Per-test aggregators
        // -----------------------------------------------------------------

        private void AggregateFixation(ref CalibrationResults results)
        {
            float settleThreshold = settings != null ? settings.fixationSettleSeconds : 0.5f;
            float angularThreshold = settings != null ? settings.accuracyThreshold : 2f;

            results.fixationTotalSampleCount = samples.Count;

            // Settled samples (target has been on long enough that the saccade
            // to it has completed). Outlier rejection is applied after
            // settling but before headline-stat computation, mirroring the
            // policy OffsetEstimator already uses on its training data — a
            // verification re-test that retains catastrophic outliers (blinks,
            // off-target glances) while the fit drops them is structurally
            // unfair to the new profile.
            //
            // Velocity gate (I-VT, Salvucci & Goldberg 2000): drop samples
            // whose inter-sample angular gaze velocity > FixationVelocityCapDegPerSec.
            // 30°/s is a standard saccade threshold; settled fixation should
            // run well under this. This catches microsaccade bursts and
            // post-blink recoveries that the per-target settle filter alone
            // does not exclude.
            var settled = new List<float>(samples.Count);
            foreach (var s in samples)
            {
                if (s.targetSettleSeconds < settleThreshold) continue;
                if (s.gazeAngularVelocityDegPerSec > FixationVelocityCapDegPerSec) continue;
                settled.Add(s.gazeError);
            }

            // Hard ceiling matching OffsetEstimator.Options.maxResidualDeg
            // (30°): anything past this is mechanically impossible for a
            // settled fixation and must be a tracking dropout.
            settled.RemoveAll(e => e > 30f);

            // Iterative one-sided MAD filter for moderate outliers. Asymmetric
            // because gazeError is bounded below at 0 — only the upper tail
            // contains the catastrophic-blink / off-target samples we want
            // to drop. Threshold 3 × 1.4826 × MAD ≈ 3σ for normal data
            // (Holmqvist et al. 2011, eye-tracking methodology).
            if (settled.Count >= 10)
            {
                var working = new List<float>(settled);
                for (int iter = 0; iter < 3; iter++)
                {
                    working.Sort();
                    float median = working[working.Count / 2];
                    var deviations = new List<float>(working.Count);
                    foreach (float e in working) deviations.Add(Mathf.Abs(e - median));
                    deviations.Sort();
                    float mad = deviations[deviations.Count / 2];
                    if (mad <= 1e-4f) break;
                    float upperCutoff = median + 3f * 1.4826f * mad;
                    int before = working.Count;
                    working.RemoveAll(e => e > upperCutoff);
                    if (working.Count == before) break;
                }
                settled = working;
            }

            results.fixationSettledSampleCount = settled.Count;
            if (settled.Count == 0) return;

            int settledPass = 0;
            foreach (float e in settled)
            {
                if (e < angularThreshold) settledPass++;
            }

            settled.Sort();
            results.fixationMedianErrorDeg = settled[settled.Count / 2];
            results.fixationP95ErrorDeg = settled[Mathf.Clamp((int)(settled.Count * 0.95f), 0, settled.Count - 1)];
            results.fixationSettledAccuracyPct = (float)settledPass / settled.Count * 100f;
            results.fixationAccuracy = results.fixationSettledAccuracyPct; // legacy mirror
        }

        private void AggregateSaccade(ref CalibrationResults results)
        {
            float landingFraction = settings != null ? settings.saccadeLandingFraction : 0.4f;
            float angularThreshold = settings != null ? settings.accuracyThreshold : 2f;

            results.saccadeTotalSampleCount = samples.Count;

            // Group by target ID; per group, the trailing `landingFraction` of
            // the per-target window counts as "landing" (the eye has arrived).
            // Time bounds per group are derived from the samples' settle times:
            // settle-time of 0 is target onset, max is the moment before the
            // next target was activated.
            var perTarget = new Dictionary<string, List<GroundTruthSample>>();
            foreach (var s in samples)
            {
                if (!perTarget.TryGetValue(s.targetID, out var list))
                {
                    list = new List<GroundTruthSample>();
                    perTarget[s.targetID] = list;
                }
                list.Add(s);
            }

            var landingErrors = new List<float>();
            int landingPass = 0;
            foreach (var kv in perTarget)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;
                // settle time spans this group; the landing window is the
                // trailing fraction of that span.
                float maxSettle = 0f;
                foreach (var s in list) if (s.targetSettleSeconds > maxSettle) maxSettle = s.targetSettleSeconds;
                float landingStart = maxSettle * (1f - landingFraction);

                foreach (var s in list)
                {
                    if (s.targetSettleSeconds >= landingStart)
                    {
                        landingErrors.Add(s.gazeError);
                        if (s.gazeError < angularThreshold) landingPass++;
                    }
                }
            }

            results.saccadeLandingSampleCount = landingErrors.Count;
            if (landingErrors.Count == 0) return;

            landingErrors.Sort();
            results.saccadeLandingMedianErrorDeg = landingErrors[landingErrors.Count / 2];
            results.saccadeLandingP95ErrorDeg = landingErrors[Mathf.Clamp((int)(landingErrors.Count * 0.95f), 0, landingErrors.Count - 1)];
            results.saccadeLandingAccuracyPct = (float)landingPass / landingErrors.Count * 100f;
            results.saccadeAccuracy = results.saccadeLandingAccuracyPct; // legacy mirror
        }

        private void AggregatePursuit(ref CalibrationResults results)
        {
            results.pursuitSampleCount = samples.Count;
            if (samples.Count == 0) return;

            // Lag distribution (instantaneous angular error; small lag = good).
            var errors = new List<float>(samples.Count);
            foreach (var s in samples) errors.Add(s.gazeError);
            errors.Sort();
            results.pursuitMedianLagDeg = errors[errors.Count / 2];
            results.pursuitP95LagDeg = errors[Mathf.Clamp((int)(errors.Count * 0.95f), 0, errors.Count - 1)];

            // Gain: ratio of eye angular speed to target angular speed.
            // Computed from successive samples, working in head-relative
            // angular space so head motion doesn't pollute the estimate.
            float totalEyeSpeed = 0f, totalTargetSpeed = 0f;
            int speedPairs = 0;
            for (int i = 1; i < samples.Count; i++)
            {
                var prev = samples[i - 1];
                var curr = samples[i];
                float dt = curr.timestamp - prev.timestamp;
                if (dt < 1e-3f) continue;

                // Eye angular speed: angle between successive gaze direction
                // vectors, divided by dt.
                float eyeAngle = Vector3.Angle(prev.actualGazeDirection, curr.actualGazeDirection);
                // Target angular speed: angle subtended by the target's
                // displacement at the eye's origin between samples.
                Vector3 prevDir = (prev.targetPosition - prev.actualGazeOrigin).normalized;
                Vector3 currDir = (curr.targetPosition - curr.actualGazeOrigin).normalized;
                float targetAngle = Vector3.Angle(prevDir, currDir);

                totalEyeSpeed += eyeAngle / dt;
                totalTargetSpeed += targetAngle / dt;
                speedPairs++;
            }

            if (speedPairs > 0 && totalTargetSpeed > 1e-3f)
            {
                results.pursuitGainEstimate = totalEyeSpeed / totalTargetSpeed;
            }
            else
            {
                results.pursuitGainEstimate = 0f;
            }
        }
    }
}
