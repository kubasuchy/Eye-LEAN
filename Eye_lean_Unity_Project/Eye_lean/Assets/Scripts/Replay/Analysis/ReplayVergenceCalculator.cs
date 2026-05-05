using UnityEngine;
using System.Collections.Generic;

namespace EyeLean.Replay.Analysis
{
    /// <summary>
    /// Vergence calculator for replay analysis.
    /// Computes vergence points from binocular eye tracking data with optional smoothing.
    /// Based on research paper: "3D Gaze in Virtual Reality: Vergence, Calibration, Event Detection"
    /// </summary>
    public class ReplayVergenceCalculator : MonoBehaviour
    {
        #region Settings

        [Header("Vergence Settings")]
        [Tooltip("Minimum distance for valid vergence (meters)")]
        public float minVergenceDistance = 0.1f;

        [Tooltip("Maximum distance for valid vergence (meters)")]
        public float maxVergenceDistance = 100.0f;

        [Tooltip("Maximum allowed ray distance for valid vergence")]
        public float maxRayDistance = 1.0f;

        [Header("Smoothing")]
        [Tooltip("Smoothing method to use")]
        public SmoothingMethod smoothingMethod = SmoothingMethod.None;

        [Tooltip("Window size for moving average")]
        [Range(3, 15)]
        public int movingAverageWindow = 5;

        [Tooltip("Alpha factor for exponential smoothing (lower = smoother)")]
        [Range(0.05f, 0.5f)]
        public float exponentialAlpha = 0.2f;

        #endregion

        #region Types

        public enum SmoothingMethod
        {
            None,
            MovingAverage,
            Exponential
        }

        #endregion

        #region Private Fields

        private Queue<Vector3> vergenceHistory = new Queue<Vector3>();
        private Vector3 lastVergencePoint = Vector3.zero;
        private bool hasLastVergence = false;

        #endregion

        #region Public Methods

        /// <summary>
        /// Calculate vergence point from binocular eye rays.
        /// Returns true if a valid vergence point was computed.
        /// </summary>
        public bool CalculateVergencePoint(
            Vector3 leftEyeOrigin, Vector3 leftEyeDirection,
            Vector3 rightEyeOrigin, Vector3 rightEyeDirection,
            out Vector3 vergencePoint, out float rayDistance)
        {
            vergencePoint = Vector3.zero;
            rayDistance = 0f;

            // Normalize directions
            leftEyeDirection = leftEyeDirection.normalized;
            rightEyeDirection = rightEyeDirection.normalized;

            // Skip if directions are invalid
            if (leftEyeDirection.magnitude < 0.1f || rightEyeDirection.magnitude < 0.1f)
            {
                return false;
            }

            // Calculate intersection parameters
            if (!CalculateSkewRayIntersection(
                leftEyeOrigin, leftEyeDirection,
                rightEyeOrigin, rightEyeDirection,
                out float t1, out float t2))
            {
                return false;
            }

            // Check if intersection is in front of the eyes
            if (t1 < 0 || t2 < 0)
            {
                return false;
            }

            // Calculate closest points on each ray
            Vector3 leftPoint = leftEyeOrigin + t1 * leftEyeDirection;
            Vector3 rightPoint = rightEyeOrigin + t2 * rightEyeDirection;

            // Calculate ray distance (for confidence)
            rayDistance = Vector3.Distance(leftPoint, rightPoint);

            // Check ray distance threshold
            if (rayDistance > maxRayDistance)
            {
                return false;
            }

            // Calculate midpoint (vergence point)
            vergencePoint = (leftPoint + rightPoint) * 0.5f;

            // Validate vergence distance
            float avgDistance = ((leftEyeOrigin - vergencePoint).magnitude +
                                 (rightEyeOrigin - vergencePoint).magnitude) * 0.5f;

            if (avgDistance < minVergenceDistance || avgDistance > maxVergenceDistance)
            {
                return false;
            }

            // Apply smoothing if enabled
            vergencePoint = ApplySmoothing(vergencePoint);

            return true;
        }

        /// <summary>
        /// Calculate vergence point from a replay frame
        /// </summary>
        public bool CalculateVergenceFromFrame(ReplayFrame frame, out Vector3 vergencePoint, out float rayDistance)
        {
            return CalculateVergencePoint(
                frame.leftEyeOrigin, frame.leftEyeDirection,
                frame.rightEyeOrigin, frame.rightEyeDirection,
                out vergencePoint, out rayDistance);
        }

        /// <summary>
        /// Reset smoothing history
        /// </summary>
        public void ResetSmoothing()
        {
            vergenceHistory.Clear();
            hasLastVergence = false;
            lastVergencePoint = Vector3.zero;
        }

        /// <summary>
        /// Pre-process all frames in a session to compute vergence
        /// </summary>
        public void PreprocessSession(ReplaySession session)
        {
            if (session == null || session.frames == null)
                return;

            ResetSmoothing();

            foreach (var frame in session.frames)
            {
                if (CalculateVergenceFromFrame(frame, out Vector3 vergence, out float rayDist))
                {
                    frame.vergencePoint = vergence;
                    frame.vergenceQuality = 1.0f - Mathf.Clamp01(rayDist / maxRayDistance);
                    frame.hasValidVergence = true;
                }
                else
                {
                    frame.hasValidVergence = false;
                    frame.vergenceQuality = 0f;
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Calculate closest points between two skew rays in 3D space.
        /// Uses the algorithm from vergence research papers.
        /// </summary>
        private bool CalculateSkewRayIntersection(
            Vector3 origin1, Vector3 dir1,
            Vector3 origin2, Vector3 dir2,
            out float t1, out float t2)
        {
            t1 = t2 = 0f;

            // Vector from origin2 to origin1
            Vector3 w0 = origin1 - origin2;

            // Dot products
            float a = Vector3.Dot(dir1, dir1);  // |dir1|^2
            float b = Vector3.Dot(dir1, dir2);  // dir1 . dir2
            float c = Vector3.Dot(dir2, dir2);  // |dir2|^2
            float d = Vector3.Dot(dir1, w0);    // dir1 . w0
            float e = Vector3.Dot(dir2, w0);    // dir2 . w0

            // Denominator
            float denom = a * c - b * b;

            // Check for parallel rays
            if (Mathf.Abs(denom) < 1e-6f)
            {
                return false;
            }

            // Calculate parameters
            t1 = (b * e - c * d) / denom;
            t2 = (a * e - b * d) / denom;

            return true;
        }

        /// <summary>
        /// Apply selected smoothing method
        /// </summary>
        private Vector3 ApplySmoothing(Vector3 point)
        {
            switch (smoothingMethod)
            {
                case SmoothingMethod.MovingAverage:
                    return ApplyMovingAverage(point);

                case SmoothingMethod.Exponential:
                    return ApplyExponentialSmoothing(point);

                case SmoothingMethod.None:
                default:
                    return point;
            }
        }

        private Vector3 ApplyMovingAverage(Vector3 point)
        {
            vergenceHistory.Enqueue(point);

            while (vergenceHistory.Count > movingAverageWindow)
            {
                vergenceHistory.Dequeue();
            }

            Vector3 sum = Vector3.zero;
            foreach (var p in vergenceHistory)
            {
                sum += p;
            }

            return sum / vergenceHistory.Count;
        }

        private Vector3 ApplyExponentialSmoothing(Vector3 point)
        {
            if (!hasLastVergence)
            {
                lastVergencePoint = point;
                hasLastVergence = true;
                return point;
            }

            // Exponential smoothing: y[n] = alpha * x[n] + (1 - alpha) * y[n-1]
            lastVergencePoint = exponentialAlpha * point + (1f - exponentialAlpha) * lastVergencePoint;
            return lastVergencePoint;
        }

        #endregion
    }
}
