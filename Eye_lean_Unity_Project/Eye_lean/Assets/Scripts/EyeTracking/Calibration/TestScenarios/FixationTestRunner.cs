using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using EyeTracking.Core;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Runs fixation calibration test.
    /// Shows targets sequentially, requiring participant to fixate on each for a set duration.
    /// </summary>
    public class FixationTestRunner : CalibrationTestRunner
    {
        // Targets created for this test
        private List<GameObject> targets = new List<GameObject>();

        // Current target being fixated
        private int currentTargetIndex = 0;

        // Colors for target states
        private static readonly Color ActiveColor = Color.yellow;
        private static readonly Color CompletedColor = Color.green;
        private static readonly Color InactiveColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        public override CalibrationTestType TestType => CalibrationTestType.Fixation;

        // When non-null, CalculateTargetPositions returns predefinedPositions[i]
        // for each i in this array instead of the leading [0..count-1] slice.
        // Used by post-fit verification to sample a stratified subset that
        // covers the same eccentricity range the fit was trained on, instead
        // of the four near-axis targets the leading slice happens to contain.
        private int[] targetIndexOverride;

        public void SetTargetIndexOverride(int[] indices) => targetIndexOverride = indices;
        public void ClearTargetIndexOverride() => targetIndexOverride = null;

        /// <summary>
        /// Current target index (for UI display).
        /// </summary>
        public int CurrentTargetIndex => currentTargetIndex;

        /// <summary>
        /// Total number of targets.
        /// </summary>
        public int TotalTargets => targets.Count;

        protected override IEnumerator RunTestCoroutine()
        {
            // Create fixation targets (all hidden initially)
            CreateTargets();

            // Run through each target (one at a time)
            currentTargetIndex = 0;
            int totalTargets = targets.Count;

            Debug.Log($"[FixationTest] Starting test with {totalTargets} targets (showing one at a time)");

            while (currentTargetIndex < totalTargets && isRunning)
            {
                GameObject target = targets[currentTargetIndex];

                // Activate current target (shows it)
                ActivateTarget(target);

                // Mark this as the new active target so RecordSample can stamp
                // each sample with its time-since-onset (used by the aggregator
                // to filter out target-to-target saccade transition samples).
                MarkTargetOnset(target);

                Debug.Log($"[FixationTest] Target {currentTargetIndex + 1}/{totalTargets}: {target.name}");

                // Wait for fixation duration while recording samples
                float fixationTime = 0f;
                float sampleInterval = 1f / settings.samplingRate;

                while (fixationTime < settings.fixationDwellTime && isRunning)
                {
                    RecordSample(target, "FIXATION");

                    fixationTime += sampleInterval;
                    float overallProgress = (currentTargetIndex + fixationTime / settings.fixationDwellTime) / totalTargets;
                    ReportProgress(overallProgress);

                    yield return new WaitForSeconds(sampleInterval);
                }

                // Hide completed target (not just color change)
                target.SetActive(false);

                currentTargetIndex++;

                // Brief pause between targets
                yield return new WaitForSeconds(0.3f);
            }

            // Per-target settled-sample breakdown — surfaces uneven
            // sampling (a target the participant blinked through, or one
            // that was clipped by an early test abort) before the
            // aggregator collapses everything into a single median.
            float settleThreshold = settings != null ? settings.fixationSettleSeconds : 0.5f;
            var perTargetSettled = new Dictionary<string, int>();
            var perTargetTotal = new Dictionary<string, int>();
            foreach (var s in samples)
            {
                if (!perTargetTotal.ContainsKey(s.targetID))
                {
                    perTargetTotal[s.targetID] = 0;
                    perTargetSettled[s.targetID] = 0;
                }
                perTargetTotal[s.targetID]++;
                if (s.targetSettleSeconds >= settleThreshold) perTargetSettled[s.targetID]++;
            }

            // Test complete - RaiseTestCompleted handles cleanup and isRunning
            RaiseTestCompleted();

            Debug.Log($"[FixationTest] Completed. Collected {samples.Count} samples across {totalTargets} targets.");
            foreach (var kv in perTargetTotal)
            {
                Debug.Log($"[FixationTest]   {kv.Key}: {perTargetSettled[kv.Key]} settled / {kv.Value} total");
            }
        }

        protected override void CleanupTest()
        {
            // Destroy all targets
            foreach (GameObject target in targets)
            {
                if (target != null)
                {
                    DestroyImmediate(target);
                }
            }
            targets.Clear();
            currentTargetIndex = 0;
        }

        /// <summary>
        /// Create fixation targets at calculated positions.
        /// </summary>
        private void CreateTargets()
        {
            Vector3[] positions = CalculateTargetPositions();

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject target = CreateTargetSphere(
                    positions[i],
                    $"FixationTarget_{i}",
                    InactiveColor,
                    settings.fixationTargetSize
                );
                target.SetActive(false);
                targets.Add(target);

                Debug.Log($"[FixationTest] Created target {i} at {positions[i]}");
            }
        }

        /// <summary>
        /// Calculate positions for fixation targets.
        /// Creates targets at fixed ROOM coordinates (world space) spread across different depths.
        /// Room is 6m x 6m x 3m (depth x width x height), user spawns at origin facing +Z.
        /// </summary>
        private Vector3[] CalculateTargetPositions()
        {
            List<Vector3> positions = new List<Vector3>();
            int targetCount = settings.fixationTargetCount;

            // Pre-defined positions that ensure depth variation and comfortable viewing angles
            // Targets are further from user (3-5.5m) and spread across depths
            // Horizontal spread is narrower at closer distances to stay within FOV
            Vector3[] predefinedPositions = new Vector3[]
            {
                // Center targets at different depths (primary vergence test)
                new Vector3(0f, 1.5f, 3.5f),      // Center, mid-depth
                new Vector3(0f, 1.5f, 5f),        // Center, far
                new Vector3(0f, 1.5f, 4.25f),     // Center, mid-far

                // Horizontal spread at mid-depth (within comfortable FOV)
                new Vector3(-0.8f, 1.5f, 4f),     // Left, mid
                new Vector3(0.8f, 1.5f, 4f),      // Right, mid

                // Vertical spread at far depth
                new Vector3(0f, 1.0f, 4.5f),      // Center-low, mid-far
                new Vector3(0f, 2.0f, 4.5f),      // Center-high, mid-far

                // Diagonal positions at different depths
                new Vector3(-0.6f, 1.2f, 3.5f),   // Lower-left, near
                new Vector3(0.6f, 1.8f, 5f),      // Upper-right, far
                new Vector3(-0.6f, 1.8f, 4.5f),   // Upper-left, mid
                new Vector3(0.6f, 1.2f, 3.8f),    // Lower-right, near-mid

                // Additional spread
                new Vector3(0f, 1.3f, 5.5f),      // Center-low, very far

                // Wide-eccentricity targets (≈ ±21° horizontal at 3.5 m).
                // Past the comfortable-FOV horizontal spread above; only
                // exercised when fixationTargetCount is bumped or when a
                // gain-fit run requests them via index override. Provides
                // the lever arm a yaw-gain fit needs.
                new Vector3(-1.4f, 1.5f, 3.5f),   // Wide left
                new Vector3(1.4f, 1.5f, 3.5f),    // Wide right
                new Vector3(0f, 0.7f, 3.5f),      // Wide low
                new Vector3(0f, 2.3f, 3.5f),      // Wide high
            };

            if (targetIndexOverride != null && targetIndexOverride.Length > 0)
            {
                // Verification path: keep the override's deliberate
                // stratified order; do not shuffle (the order encodes the
                // eccentricity classes the fit was trained on).
                foreach (int idx in targetIndexOverride)
                {
                    if (idx >= 0 && idx < predefinedPositions.Length)
                        positions.Add(predefinedPositions[idx]);
                }
            }
            else
            {
                int n = Mathf.Min(targetCount, predefinedPositions.Length);
                for (int i = 0; i < n; i++)
                {
                    positions.Add(predefinedPositions[i]);
                }
                // Fisher-Yates shuffle to decorrelate target eccentricity
                // from session timeline (otherwise the participant fatigues
                // through a fixed ordering and anticipatory saccades bias
                // late-trial samples). Uses UnityEngine.Random so the
                // sequence is captured by Random.state and reproducible
                // under the deterministic-replay system.
                for (int i = positions.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (positions[i], positions[j]) = (positions[j], positions[i]);
                }
            }

            Debug.Log($"[FixationTest] Created {positions.Count} targets at FIXED ROOM coordinates (depths: 3.5-5.5m)");

            return positions.ToArray();
        }

        /// <summary>
        /// Activate a target for fixation.
        /// </summary>
        private void ActivateTarget(GameObject target)
        {
            target.SetActive(true);
            SetTargetColor(target, ActiveColor);
        }

        /// <summary>
        /// Mark a target as completed.
        /// </summary>
        private void CompleteTarget(GameObject target)
        {
            SetTargetColor(target, CompletedColor);
        }

        /// <summary>
        /// Set the color of a target.
        /// </summary>
        private void SetTargetColor(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = color;
            }
        }
    }
}
