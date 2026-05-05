using UnityEngine;
using System.Collections;
using EyeTracking.Core;

namespace EyeTracking.Calibration
{
    /// <summary>
    /// Runs smooth pursuit calibration test.
    /// Shows a single target that moves smoothly, requiring participant to track it with their eyes.
    /// </summary>
    public class SmoothPursuitTestRunner : CalibrationTestRunner
    {
        // The moving target
        private GameObject pursuitTarget;

        // Movement parameters
        private Vector3 movementCenter;
        private Vector3 movementRight;  // Camera-relative right direction
        private Vector3 movementUp;     // Camera-relative up direction
        private float movementRadius = 1.0f;
        private float currentAngle = 0f;

        // Target color
        private static readonly Color PursuitColor = new Color(1f, 0.5f, 0f); // Orange

        public override CalibrationTestType TestType => CalibrationTestType.SmoothPursuit;

        /// <summary>
        /// Current position of the pursuit target.
        /// </summary>
        public Vector3 TargetPosition => pursuitTarget?.transform.position ?? Vector3.zero;

        protected override IEnumerator RunTestCoroutine()
        {
            // Create pursuit target at FIXED ROOM POSITION (not camera-relative)
            // Room is 6m x 6m x 3m, user at origin facing +Z
            // Center point at middle of room, eye height
            movementCenter = new Vector3(0f, 1.5f, 3f);  // Room center
            movementRight = Vector3.right;   // Fixed world X axis
            movementUp = Vector3.up;         // Fixed world Y axis
            movementRadius = settings.maxHorizontalSpread * 0.6f; // Reduced for tighter movement

            Debug.Log($"[SmoothPursuitTest] Using FIXED ROOM coordinates: center={movementCenter}");

            // Create pursuit target at the movement center
            CreatePursuitTargetAt(movementCenter);

            // Mark target onset once — the same target moves throughout, so
            // there's no "transition" in the saccade-test sense. RecordSample
            // will derive instantaneous target velocity from successive samples
            // for use in the pursuit-gain calculation.
            MarkTargetOnset(pursuitTarget);

            float elapsed = 0f;
            float sampleInterval = 1f / settings.samplingRate;
            float duration = settings.pursuitDuration;

            Debug.Log($"[SmoothPursuitTest] Starting {duration}s pursuit test");

            while (elapsed < duration && isRunning)
            {
                // Update target position (circular/figure-8 motion)
                UpdateTargetPosition(elapsed);

                // Record sample
                RecordSample(pursuitTarget, "SMOOTH_PURSUIT");

                // Update progress
                ReportProgress(elapsed / duration);

                elapsed += sampleInterval;
                yield return new WaitForSeconds(sampleInterval);
            }

            // Test complete - RaiseTestCompleted handles cleanup and isRunning
            RaiseTestCompleted();

            Debug.Log($"[SmoothPursuitTest] Completed.");
        }

        protected override void CleanupTest()
        {
            if (pursuitTarget != null)
            {
                DestroyImmediate(pursuitTarget);
                pursuitTarget = null;
            }
            currentAngle = 0f;
        }

        /// <summary>
        /// Create the pursuit target at the specified position.
        /// </summary>
        private void CreatePursuitTargetAt(Vector3 position)
        {
            pursuitTarget = CreateTargetSphere(
                position,
                "SmoothPursuitTarget",
                PursuitColor,
                settings.fixationTargetSize * 1.5f // Slightly larger for visibility
            );

            Debug.Log($"[SmoothPursuitTest] Created pursuit target at FIXED position {position}");
        }

        /// <summary>
        /// Update the target position based on elapsed time.
        /// Uses a figure-8 (lemniscate) pattern for natural eye movement testing.
        /// Movement is relative to the camera orientation captured at test start.
        /// </summary>
        private void UpdateTargetPosition(float time)
        {
            if (pursuitTarget == null) return;

            // Calculate figure-8 pattern (Lissajous curve)
            float angularSpeed = settings.pursuitSpeed;
            currentAngle = time * angularSpeed;

            // Figure-8 pattern - scaled for comfortable viewing (reduced vertical range)
            float x = movementRadius * Mathf.Sin(currentAngle);
            float y = movementRadius * 0.25f * Mathf.Sin(currentAngle * 2f);

            // Use camera-relative directions (captured at test start)
            // This keeps the target in front of the user regardless of room orientation
            Vector3 newPosition = movementCenter +
                                 movementRight * x +
                                 movementUp * y;

            pursuitTarget.transform.position = newPosition;
        }
    }
}
