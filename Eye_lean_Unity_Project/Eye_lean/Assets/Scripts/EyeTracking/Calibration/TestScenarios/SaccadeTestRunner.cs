using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using EyeTracking.Core;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Runs saccade calibration test.
    /// Shows targets that light up in rapid succession, requiring quick eye movements.
    /// </summary>
    public class SaccadeTestRunner : CalibrationTestRunner
    {
        // Targets for saccade test
        private List<GameObject> targets = new List<GameObject>();

        // Current and previous target indices
        private int currentTargetIndex = 0;
        private int previousTargetIndex = -1;

        // Colors for target states
        private static readonly Color ActiveColor = Color.red;
        private static readonly Color CompletedColor = Color.green;
        private static readonly Color InactiveColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

        public override CalibrationTestType TestType => CalibrationTestType.Saccade;

        /// <summary>
        /// Current target index.
        /// </summary>
        public int CurrentTargetIndex => currentTargetIndex;

        /// <summary>
        /// Total number of targets.
        /// </summary>
        public int TotalTargets => targets.Count;

        protected override IEnumerator RunTestCoroutine()
        {
            // Create saccade targets (all hidden initially)
            CreateTargets();

            // Keep all targets hidden initially
            foreach (var target in targets)
            {
                target.SetActive(false);
            }

            currentTargetIndex = 0;
            previousTargetIndex = -1;
            int totalTargets = targets.Count;

            Debug.Log($"[SaccadeTest] Starting test with {totalTargets} targets (showing one at a time)");

            while (currentTargetIndex < totalTargets && isRunning)
            {
                // Hide previous target completely
                if (previousTargetIndex >= 0 && previousTargetIndex < targets.Count)
                {
                    targets[previousTargetIndex].SetActive(false);
                }

                // Show and activate current target
                GameObject target = targets[currentTargetIndex];
                target.SetActive(true);
                SetTargetColor(target, ActiveColor);

                // Mark target onset so the aggregator can identify the trailing
                // "landing" portion of this target's window as the period to score
                // (saccade flight is by definition off-target; only landing matters).
                MarkTargetOnset(target);

                Debug.Log($"[SaccadeTest] Target {currentTargetIndex + 1}/{totalTargets}");

                // Record samples during saccade interval
                float elapsed = 0f;
                float sampleInterval = 1f / settings.samplingRate;

                while (elapsed < settings.saccadeInterval && isRunning)
                {
                    RecordSample(target, "SACCADE");

                    elapsed += sampleInterval;
                    float overallProgress = (currentTargetIndex + elapsed / settings.saccadeInterval) / totalTargets;
                    ReportProgress(overallProgress);

                    yield return new WaitForSeconds(sampleInterval);
                }

                previousTargetIndex = currentTargetIndex;
                currentTargetIndex++;

                // Very brief pause (saccades should be fast)
                yield return new WaitForSeconds(0.1f);
            }

            // Hide last target
            if (previousTargetIndex >= 0 && previousTargetIndex < targets.Count)
            {
                targets[previousTargetIndex].SetActive(false);
            }

            // Test complete - RaiseTestCompleted handles cleanup and isRunning
            RaiseTestCompleted();

            Debug.Log($"[SaccadeTest] Completed.");
        }

        protected override void CleanupTest()
        {
            foreach (GameObject target in targets)
            {
                if (target != null)
                {
                    DestroyImmediate(target);
                }
            }
            targets.Clear();
            currentTargetIndex = 0;
            previousTargetIndex = -1;
        }

        /// <summary>
        /// Create saccade targets at positions requiring large eye movements.
        /// </summary>
        private void CreateTargets()
        {
            Vector3[] positions = CalculateTargetPositions();

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject target = CreateTargetSphere(
                    positions[i],
                    $"SaccadeTarget_{i}",
                    InactiveColor,
                    settings.fixationTargetSize
                );
                targets.Add(target);
            }

            Debug.Log($"[SaccadeTest] Created {targets.Count} targets");
        }

        /// <summary>
        /// Calculate positions that require large saccadic eye movements.
        /// Uses fixed ROOM coordinates in a radial pattern for unpredictable saccades.
        /// </summary>
        private Vector3[] CalculateTargetPositions()
        {
            List<Vector3> positions = new List<Vector3>();
            int targetCount = settings.saccadeTargetCount;

            // Fixed room coordinates for saccade targets
            // Positioned at ~4m depth with horizontal and vertical spread
            // Designed for large, unpredictable eye movements
            float baseDepth = 4f;
            float baseHeight = 1.5f;  // Eye level

            // Radial positions at fixed room coordinates
            // Spread is comfortable for FOV but requires significant eye movement
            Vector3[] predefinedPositions = new Vector3[]
            {
                // Primary cardinal directions
                new Vector3(0f, baseHeight, baseDepth),           // Center
                new Vector3(-1.2f, baseHeight, baseDepth),        // Left
                new Vector3(1.2f, baseHeight, baseDepth),         // Right
                new Vector3(0f, baseHeight + 0.6f, baseDepth),    // Up
                new Vector3(0f, baseHeight - 0.5f, baseDepth),    // Down

                // Diagonal positions
                new Vector3(-0.9f, baseHeight + 0.4f, baseDepth), // Upper-left
                new Vector3(0.9f, baseHeight + 0.4f, baseDepth),  // Upper-right
                new Vector3(-0.9f, baseHeight - 0.4f, baseDepth), // Lower-left
                new Vector3(0.9f, baseHeight - 0.4f, baseDepth),  // Lower-right

                // Outer ring (larger saccades)
                new Vector3(-1.5f, baseHeight, 4.5f),             // Far left
                new Vector3(1.5f, baseHeight, 4.5f),              // Far right
                new Vector3(0f, baseHeight + 0.8f, 3.5f),         // High center (closer)
            };

            // Use predefined positions up to target count
            for (int i = 0; i < Mathf.Min(targetCount, predefinedPositions.Length); i++)
            {
                positions.Add(predefinedPositions[i]);
            }

            // Shuffle positions to require unpredictable saccades
            ShufflePositions(positions);

            Debug.Log($"[SaccadeTest] Created {positions.Count} targets at FIXED ROOM coordinates");

            return positions.ToArray();
        }

        /// <summary>
        /// Shuffle positions to create unpredictable saccade patterns.
        /// </summary>
        private void ShufflePositions(List<Vector3> positions)
        {
            // Fisher-Yates shuffle
            System.Random rng = new System.Random(42); // Fixed seed for reproducibility
            int n = positions.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                Vector3 temp = positions[k];
                positions[k] = positions[n];
                positions[n] = temp;
            }
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
