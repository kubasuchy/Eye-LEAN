using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EyeTracking.Core;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Validates eye tracking accuracy by comparing gaze against known target positions.
    /// Uses the IEyeTracker interface for SDK-agnostic eye tracking access.
    /// Records ground truth data for offline analysis.
    /// </summary>
    public class GroundTruthValidator : MonoBehaviour
    {
        [Header("Validation Settings")]
        [SerializeField] private float accuracyThreshold = 2f;
        [SerializeField] private float samplingRate = 60f;
        [SerializeField] private bool logToFile = true;

        [Header("Session Info")]
        [SerializeField] private string participantID = "";
        [SerializeField] private int sessionNumber = 1;

        // Eye tracker reference
        private IEyeTracker eyeTracker;
        private Transform cameraTransform;

        // Data collection
        private List<GroundTruthSample> samples = new List<GroundTruthSample>();
        private StreamWriter fileWriter;
        private string filePath;
        private float validationStartTime;
        private bool isValidating = false;

        // Current scenario tracking
        private CalibrationScenario currentScenario;
        private CalibrationTestType currentTestType;

        /// <summary>
        /// Whether validation is currently active.
        /// </summary>
        public bool IsValidating => isValidating;

        /// <summary>
        /// All collected samples.
        /// </summary>
        public IReadOnlyList<GroundTruthSample> Samples => samples;

        /// <summary>
        /// Initialize the validator with an eye tracker.
        /// </summary>
        public void Initialize(IEyeTracker tracker)
        {
            eyeTracker = tracker;
            cameraTransform = Camera.main?.transform;

            if (cameraTransform == null)
            {
                Debug.LogError("[GroundTruthValidator] No main camera found");
            }

            if (logToFile)
            {
                InitializeFileLogging();
            }

            Debug.Log("[GroundTruthValidator] Initialized");
        }

        /// <summary>
        /// Initialize with automatic eye tracker detection.
        /// </summary>
        public void Initialize()
        {
            eyeTracker = EyeTrackerFactory.GetEyeTracker();
            Initialize(eyeTracker);
        }

        /// <summary>
        /// Set participant ID for file naming and metadata.
        /// </summary>
        public void SetParticipantID(string id)
        {
            participantID = id;

            // Reinitialize file logging with new participant ID
            if (logToFile && fileWriter != null)
            {
                CloseFileWriter();
                InitializeFileLogging();
            }

            Debug.Log($"[GroundTruthValidator] Participant ID set: {id}");
        }

        /// <summary>
        /// Set session number for metadata.
        /// </summary>
        public void SetSessionNumber(int number)
        {
            sessionNumber = number;
        }

        #region File Logging

        private void InitializeFileLogging()
        {
            string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string prefix = string.IsNullOrEmpty(participantID) ? "" : $"{participantID}_";
            string fileName = $"{prefix}GroundTruth_S{sessionNumber}_{timestamp}.csv";

            // Use persistent data path for Android compatibility
            filePath = Path.Combine(Application.persistentDataPath, fileName);

            try
            {
                fileWriter = new StreamWriter(filePath, false);
                WriteFileHeader();
                Debug.Log($"[GroundTruthValidator] Logging to: {filePath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GroundTruthValidator] Failed to create log file: {ex.Message}");
                fileWriter = null;
            }
        }

        private void WriteFileHeader()
        {
            if (fileWriter == null) return;

            // Write metadata
            fileWriter.WriteLine($"# GroundTruthValidation");
            fileWriter.WriteLine($"# ParticipantID: {participantID}");
            fileWriter.WriteLine($"# SessionNumber: {sessionNumber}");
            fileWriter.WriteLine($"# RecordedAt: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            fileWriter.WriteLine($"# AccuracyThreshold: {accuracyThreshold}");
            fileWriter.WriteLine($"# SamplingRate: {samplingRate}");
            fileWriter.WriteLine("#");

            // Write CSV header
            string header = "Timestamp,SessionTime,TestType,TargetID," +
                           "TargetPosX,TargetPosY,TargetPosZ," +
                           "SurfaceX,SurfaceY,SurfaceZ," +
                           "IntendedDirX,IntendedDirY,IntendedDirZ," +
                           "GazeOriginX,GazeOriginY,GazeOriginZ," +
                           "GazeDirX,GazeDirY,GazeDirZ," +
                           "GazeError,SurfaceError,IsFixating,IsValid,HasSurfaceHit";

            fileWriter.WriteLine(header);
            fileWriter.Flush();
        }

        private void WriteSampleToFile(GroundTruthSample sample)
        {
            if (fileWriter == null) return;

            string line = $"{sample.timestamp:F4},{sample.sessionTime:F4},{sample.testType},{sample.targetID}," +
                         $"{sample.targetPosition.x:F4},{sample.targetPosition.y:F4},{sample.targetPosition.z:F4}," +
                         $"{sample.surfaceIntersectionPoint.x:F4},{sample.surfaceIntersectionPoint.y:F4},{sample.surfaceIntersectionPoint.z:F4}," +
                         $"{sample.intendedGazeDirection.x:F4},{sample.intendedGazeDirection.y:F4},{sample.intendedGazeDirection.z:F4}," +
                         $"{sample.actualGazeOrigin.x:F4},{sample.actualGazeOrigin.y:F4},{sample.actualGazeOrigin.z:F4}," +
                         $"{sample.actualGazeDirection.x:F4},{sample.actualGazeDirection.y:F4},{sample.actualGazeDirection.z:F4}," +
                         $"{sample.gazeError:F4},{sample.surfaceError:F4},{sample.isFixating},{sample.isValid},{sample.hasSurfaceIntersection}";

            fileWriter.WriteLine(line);

            // Flush periodically to prevent data loss
            if (samples.Count % 60 == 0)
            {
                fileWriter.Flush();
            }
        }

        private void CloseFileWriter()
        {
            if (fileWriter != null)
            {
                fileWriter.Flush();
                fileWriter.Close();
                fileWriter = null;
            }
        }

        #endregion

        #region Scenario Control

        /// <summary>
        /// Start validation for a calibration scenario.
        /// </summary>
        public void StartScenario(CalibrationScenario scenario)
        {
            currentScenario = scenario;
            currentTestType = scenario.type;
            validationStartTime = Time.time;
            isValidating = true;
            samples.Clear();

            Debug.Log($"[GroundTruthValidator] Started scenario: {scenario.name}");
        }

        /// <summary>
        /// Start validation for a specific test type.
        /// </summary>
        public void StartValidation(CalibrationTestType testType)
        {
            currentTestType = testType;
            validationStartTime = Time.time;
            isValidating = true;
            samples.Clear();

            Debug.Log($"[GroundTruthValidator] Started validation: {testType}");
        }

        /// <summary>
        /// End the current validation/scenario.
        /// </summary>
        public void EndScenario()
        {
            if (!isValidating) return;

            isValidating = false;

            // Flush remaining data
            if (fileWriter != null)
            {
                fileWriter.Flush();
            }

            // Log summary
            var results = GetResults();
            Debug.Log($"[GroundTruthValidator] Ended scenario. {results.GetSummary()}");
        }

        #endregion

        #region Sample Recording

        /// <summary>
        /// Record a ground truth sample for the specified target.
        /// </summary>
        public void RecordSample(GameObject target, string eventType = null)
        {
            if (!isValidating || eyeTracker == null || target == null) return;

            // Get gaze data
            Vector3 gazeOrigin, gazeDirection;
            if (!GetGazeData(out gazeOrigin, out gazeDirection)) return;

            Vector3 targetPosition = target.transform.position;
            Vector3 intendedDirection = (targetPosition - cameraTransform.position).normalized;

            // Calculate surface intersection
            Vector3 surfacePoint = targetPosition;
            bool hasSurfaceHit = false;
            float surfaceError = 0f;

            RaycastHit hit;
            if (Physics.Raycast(gazeOrigin, gazeDirection, out hit, 100f))
            {
                surfacePoint = hit.point;
                hasSurfaceHit = true;
                surfaceError = CalculateAngularError(gazeOrigin, gazeDirection, surfacePoint);
            }

            // Calculate gaze error to target center
            float gazeError = CalculateAngularError(gazeOrigin, gazeDirection, targetPosition);

            // Determine if fixating (for fixation tests)
            bool isFixating = currentTestType == CalibrationTestType.Fixation;

            // Build test type string
            string testTypeStr = eventType ?? currentTestType.ToString().ToUpper();

            // Create sample
            GroundTruthSample sample = new GroundTruthSample
            {
                timestamp = Time.time,
                sessionTime = Time.time - validationStartTime,
                testType = testTypeStr,
                targetID = target.name,
                targetPosition = targetPosition,
                surfaceIntersectionPoint = surfacePoint,
                intendedGazeDirection = intendedDirection,
                actualGazeOrigin = gazeOrigin,
                actualGazeDirection = gazeDirection,
                gazeError = gazeError,
                surfaceError = surfaceError,
                isFixating = isFixating,
                isValid = gazeError < accuracyThreshold,
                hasSurfaceIntersection = hasSurfaceHit
            };

            samples.Add(sample);
            WriteSampleToFile(sample);
        }

        /// <summary>
        /// Record a sample with explicit gaze data (for external callers).
        /// </summary>
        public void RecordSample(Vector3 targetPosition, string targetID, Vector3 gazeOrigin, Vector3 gazeDirection, string eventType = null)
        {
            if (!isValidating) return;

            Vector3 intendedDirection = (targetPosition - cameraTransform.position).normalized;

            // Calculate surface intersection
            Vector3 surfacePoint = targetPosition;
            bool hasSurfaceHit = false;
            float surfaceError = 0f;

            RaycastHit hit;
            if (Physics.Raycast(gazeOrigin, gazeDirection, out hit, 100f))
            {
                surfacePoint = hit.point;
                hasSurfaceHit = true;
                surfaceError = CalculateAngularError(gazeOrigin, gazeDirection, surfacePoint);
            }

            float gazeError = CalculateAngularError(gazeOrigin, gazeDirection, targetPosition);
            bool isFixating = currentTestType == CalibrationTestType.Fixation;
            string testTypeStr = eventType ?? currentTestType.ToString().ToUpper();

            GroundTruthSample sample = new GroundTruthSample
            {
                timestamp = Time.time,
                sessionTime = Time.time - validationStartTime,
                testType = testTypeStr,
                targetID = targetID,
                targetPosition = targetPosition,
                surfaceIntersectionPoint = surfacePoint,
                intendedGazeDirection = intendedDirection,
                actualGazeOrigin = gazeOrigin,
                actualGazeDirection = gazeDirection,
                gazeError = gazeError,
                surfaceError = surfaceError,
                isFixating = isFixating,
                isValid = gazeError < accuracyThreshold,
                hasSurfaceIntersection = hasSurfaceHit
            };

            samples.Add(sample);
            WriteSampleToFile(sample);
        }

        #endregion

        #region Gaze Data

        /// <summary>
        /// Get current gaze data from the eye tracker.
        /// </summary>
        private bool GetGazeData(out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.zero;
            direction = Vector3.forward;

            if (eyeTracker == null) return false;

            // Try combined gaze first
            bool hasOrigin = eyeTracker.GetCombinedGazeOrigin(out origin);
            bool hasDirection = eyeTracker.GetCombinedGazeDirection(out direction);

            if (hasOrigin && hasDirection) return true;

            // Fallback to individual eyes
            Vector3 leftOrigin, rightOrigin;
            Vector3 leftDir = Vector3.forward;
            Vector3 rightDir = Vector3.forward;
            bool hasLeft = eyeTracker.GetLeftEyeOrigin(out leftOrigin) &&
                          eyeTracker.GetLeftEyeDirection(out leftDir);
            bool hasRight = eyeTracker.GetRightEyeOrigin(out rightOrigin) &&
                           eyeTracker.GetRightEyeDirection(out rightDir);

            if (hasLeft && hasRight)
            {
                origin = (leftOrigin + rightOrigin) * 0.5f;
                direction = ((leftDir + rightDir) * 0.5f).normalized;
                return true;
            }

            if (hasLeft)
            {
                origin = leftOrigin;
                direction = leftDir;
                return true;
            }

            if (hasRight)
            {
                origin = rightOrigin;
                direction = rightDir;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get current gaze data (public API).
        /// </summary>
        public bool GetCurrentGazeData(out Vector3 origin, out Vector3 direction)
        {
            return GetGazeData(out origin, out direction);
        }

        #endregion

        #region Error Calculation

        /// <summary>
        /// Calculate angular error between gaze and target position.
        /// </summary>
        private float CalculateAngularError(Vector3 gazeOrigin, Vector3 gazeDirection, Vector3 targetPosition)
        {
            Vector3 intendedDirection = (targetPosition - gazeOrigin).normalized;
            float dot = Vector3.Dot(gazeDirection.normalized, intendedDirection);
            float angleRadians = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
            return angleRadians * Mathf.Rad2Deg;
        }

        #endregion

        #region Results

        /// <summary>
        /// Get validation results from collected samples.
        /// </summary>
        public CalibrationResults GetResults()
        {
            CalibrationResults results = new CalibrationResults();

            if (samples.Count == 0) return results;

            results.totalSamples = samples.Count;
            results.sessionDuration = samples.Count > 0 ? samples[samples.Count - 1].sessionTime : 0f;

            // Count valid samples and calculate per-test accuracy
            int validCount = 0;
            int fixationValid = 0, saccadeValid = 0, pursuitValid = 0;
            int fixationTotal = 0, saccadeTotal = 0, pursuitTotal = 0;

            foreach (var sample in samples)
            {
                if (sample.isValid)
                {
                    validCount++;
                }

                // Track by test type - count valid and total for each
                if (sample.testType.Contains("FIXATION"))
                {
                    fixationTotal++;
                    if (sample.isValid) fixationValid++;
                }
                else if (sample.testType.Contains("SACCADE"))
                {
                    saccadeTotal++;
                    if (sample.isValid) saccadeValid++;
                }
                else if (sample.testType.Contains("PURSUIT"))
                {
                    pursuitTotal++;
                    if (sample.isValid) pursuitValid++;
                }
            }

            results.validSamples = validCount;
            results.accuracy = samples.Count > 0 ? (float)validCount / samples.Count * 100f : 0f;
            results.dataCompleteness = results.accuracy;
            results.completedScenarios = 1;

            // Calculate per-test accuracy percentages
            if (fixationTotal > 0) results.fixationAccuracy = (float)fixationValid / fixationTotal * 100f;
            if (saccadeTotal > 0) results.saccadeAccuracy = (float)saccadeValid / saccadeTotal * 100f;
            if (pursuitTotal > 0) results.pursuitAccuracy = (float)pursuitValid / pursuitTotal * 100f;

            return results;
        }

        /// <summary>
        /// Get detailed validation metrics.
        /// </summary>
        public ValidationMetrics GetDetailedMetrics()
        {
            ValidationMetrics metrics = new ValidationMetrics();

            if (samples.Count == 0) return metrics;

            var validSamples = samples.Where(s => s.isValid).ToList();
            var allErrors = samples.Select(s => s.gazeError).ToList();
            var validErrors = validSamples.Select(s => s.gazeError).ToList();

            metrics.totalSamples = samples.Count;
            metrics.validSamples = validSamples.Count;
            metrics.accuracy = (float)validSamples.Count / samples.Count * 100f;

            if (allErrors.Count > 0)
            {
                metrics.meanError = allErrors.Average();
                metrics.maxError = allErrors.Max();
                metrics.minError = allErrors.Min();
                metrics.stdDevError = CalculateStandardDeviation(allErrors);
            }

            if (validErrors.Count > 0)
            {
                metrics.meanValidError = validErrors.Average();
            }

            // Percentiles
            allErrors.Sort();
            if (allErrors.Count > 0)
            {
                int p50Index = (int)(allErrors.Count * 0.5f);
                int p95Index = (int)(allErrors.Count * 0.95f);
                metrics.medianError = allErrors[Mathf.Clamp(p50Index, 0, allErrors.Count - 1)];
                metrics.p95Error = allErrors[Mathf.Clamp(p95Index, 0, allErrors.Count - 1)];
            }

            return metrics;
        }

        private float CalculateStandardDeviation(List<float> values)
        {
            if (values.Count < 2) return 0f;

            float mean = values.Average();
            float sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
            return Mathf.Sqrt(sumSquaredDiff / (values.Count - 1));
        }

        /// <summary>
        /// Clear all collected samples.
        /// </summary>
        public void ClearSamples()
        {
            samples.Clear();
        }

        #endregion

        void OnDestroy()
        {
            CloseFileWriter();
        }

        void OnApplicationQuit()
        {
            CloseFileWriter();
        }
    }

    /// <summary>
    /// Detailed validation metrics.
    /// </summary>
    [System.Serializable]
    public struct ValidationMetrics
    {
        public int totalSamples;
        public int validSamples;
        public float accuracy;
        public float meanError;
        public float meanValidError;
        public float stdDevError;
        public float minError;
        public float maxError;
        public float medianError;
        public float p95Error;

        public string GetSummary()
        {
            return $"Samples: {validSamples}/{totalSamples} ({accuracy:F1}% valid) | " +
                   $"Error: {meanError:F2}° ± {stdDevError:F2}° | " +
                   $"Range: [{minError:F2}°, {maxError:F2}°] | " +
                   $"Median: {medianError:F2}° | P95: {p95Error:F2}°";
        }
    }
}
