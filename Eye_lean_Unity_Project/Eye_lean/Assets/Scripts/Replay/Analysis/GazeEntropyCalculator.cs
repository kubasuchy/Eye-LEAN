using UnityEngine;
using System.Collections.Generic;

namespace EyeLean.Replay.Analysis
{
    /// <summary>
    /// Calculates Shannon entropy of gaze distribution to measure visual exploration patterns.
    /// High entropy indicates dispersed/random gaze, low entropy indicates focused attention.
    /// </summary>
    public class GazeEntropyCalculator : MonoBehaviour
    {
        #region Settings

        [Header("Grid Settings")]
        [Tooltip("Number of horizontal bins for discretization")]
        [Range(4, 24)]
        public int horizontalBins = 12;

        [Tooltip("Number of vertical bins for discretization")]
        [Range(4, 24)]
        public int verticalBins = 12;

        [Header("Analysis Settings")]
        [Tooltip("Time window for entropy calculation (seconds)")]
        [Range(0.5f, 10f)]
        public float timeWindow = 3.0f;

        [Tooltip("Maximum samples to keep in buffer")]
        [Range(100, 5000)]
        public int maxSamples = 1000;

        [Tooltip("Angular range for horizontal gaze (degrees from center)")]
        [Range(30f, 90f)]
        public float horizontalRange = 60f;

        [Tooltip("Angular range for vertical gaze (degrees from center)")]
        [Range(20f, 60f)]
        public float verticalRange = 40f;

        [Header("Debug")]
        public bool debugMode = false;

        #endregion

        #region Types

        /// <summary>
        /// Result of entropy calculation
        /// </summary>
        [System.Serializable]
        public class EntropyResult
        {
            public float entropy;           // Shannon entropy value
            public float normalizedEntropy; // 0-1 normalized
            public float maxEntropy;        // Maximum possible entropy
            public int sampleCount;         // Number of samples used
            public int occupiedBins;        // Number of bins with samples
            public int totalBins;           // Total number of bins
            public float coverage;          // Percentage of bins occupied
        }

        /// <summary>
        /// Gaze sample with timestamp and direction
        /// </summary>
        private struct GazeSample
        {
            public float timestamp;
            public Vector3 direction;
        }

        #endregion

        #region Private Fields

        private Queue<GazeSample> sampleBuffer = new Queue<GazeSample>();
        private int[,] binCounts;
        private int totalBins;
        private float maxPossibleEntropy;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeBins();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                InitializeBins();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Add a gaze sample for entropy calculation
        /// </summary>
        public void AddSample(Vector3 gazeDirection, float timestamp)
        {
            // Remove old samples outside time window
            float cutoffTime = timestamp - timeWindow;
            while (sampleBuffer.Count > 0 && sampleBuffer.Peek().timestamp < cutoffTime)
            {
                sampleBuffer.Dequeue();
            }

            // Remove samples if buffer is full
            while (sampleBuffer.Count >= maxSamples)
            {
                sampleBuffer.Dequeue();
            }

            // Add new sample
            sampleBuffer.Enqueue(new GazeSample
            {
                timestamp = timestamp,
                direction = gazeDirection.normalized
            });
        }

        /// <summary>
        /// Add a gaze sample from a replay frame
        /// </summary>
        public void AddSampleFromFrame(ReplayFrame frame)
        {
            Vector3 direction;

            if (frame.hasCombinedGaze && frame.combinedDirection.magnitude > 0.1f)
            {
                direction = frame.combinedDirection;
            }
            else if (frame.hasLeftEye && frame.hasRightEye)
            {
                direction = (frame.leftEyeDirection + frame.rightEyeDirection) * 0.5f;
            }
            else if (frame.hasLeftEye)
            {
                direction = frame.leftEyeDirection;
            }
            else if (frame.hasRightEye)
            {
                direction = frame.rightEyeDirection;
            }
            else
            {
                return;
            }

            AddSample(direction, frame.timestamp);
        }

        /// <summary>
        /// Calculate current entropy from buffered samples
        /// </summary>
        public EntropyResult CalculateEntropy()
        {
            var result = new EntropyResult
            {
                sampleCount = sampleBuffer.Count,
                totalBins = totalBins,
                maxEntropy = maxPossibleEntropy
            };

            if (sampleBuffer.Count < 2)
            {
                return result;
            }

            // Clear bin counts
            System.Array.Clear(binCounts, 0, binCounts.Length);

            // Count samples in each bin
            foreach (var sample in sampleBuffer)
            {
                int binX, binY;
                DirectionToBin(sample.direction, out binX, out binY);

                if (binX >= 0 && binX < horizontalBins && binY >= 0 && binY < verticalBins)
                {
                    binCounts[binX, binY]++;
                }
            }

            // Calculate entropy
            float entropy = 0f;
            int occupiedBins = 0;
            int totalSamples = sampleBuffer.Count;

            for (int x = 0; x < horizontalBins; x++)
            {
                for (int y = 0; y < verticalBins; y++)
                {
                    int count = binCounts[x, y];
                    if (count > 0)
                    {
                        occupiedBins++;
                        float probability = (float)count / totalSamples;
                        entropy -= probability * Mathf.Log(probability, 2);
                    }
                }
            }

            result.entropy = entropy;
            result.normalizedEntropy = maxPossibleEntropy > 0 ? entropy / maxPossibleEntropy : 0;
            result.occupiedBins = occupiedBins;
            result.coverage = (float)occupiedBins / totalBins;

            return result;
        }

        /// <summary>
        /// Calculate entropy for a specific time range in a session
        /// </summary>
        public EntropyResult CalculateEntropyForRange(ReplaySession session, int startFrame, int endFrame)
        {
            Reset();

            startFrame = Mathf.Max(0, startFrame);
            endFrame = Mathf.Min(session.frames.Count - 1, endFrame);

            for (int i = startFrame; i <= endFrame; i++)
            {
                AddSampleFromFrame(session.frames[i]);
            }

            return CalculateEntropy();
        }

        /// <summary>
        /// Calculate entropy for each phase in a session
        /// </summary>
        public Dictionary<string, EntropyResult> CalculateEntropyByPhase(ReplaySession session)
        {
            var results = new Dictionary<string, EntropyResult>();

            foreach (var marker in session.phaseMarkers)
            {
                var result = CalculateEntropyForRange(session, marker.startFrameIndex, marker.endFrameIndex);
                results[marker.phaseName] = result;
            }

            return results;
        }

        /// <summary>
        /// Get 2D heatmap of gaze distribution
        /// </summary>
        public float[,] GetHeatmap()
        {
            float[,] heatmap = new float[horizontalBins, verticalBins];
            int totalSamples = sampleBuffer.Count;

            if (totalSamples == 0)
                return heatmap;

            // Clear bin counts
            System.Array.Clear(binCounts, 0, binCounts.Length);

            // Count samples
            foreach (var sample in sampleBuffer)
            {
                int binX, binY;
                DirectionToBin(sample.direction, out binX, out binY);

                if (binX >= 0 && binX < horizontalBins && binY >= 0 && binY < verticalBins)
                {
                    binCounts[binX, binY]++;
                }
            }

            // Normalize to 0-1
            int maxCount = 1;
            for (int x = 0; x < horizontalBins; x++)
            {
                for (int y = 0; y < verticalBins; y++)
                {
                    if (binCounts[x, y] > maxCount)
                        maxCount = binCounts[x, y];
                }
            }

            for (int x = 0; x < horizontalBins; x++)
            {
                for (int y = 0; y < verticalBins; y++)
                {
                    heatmap[x, y] = (float)binCounts[x, y] / maxCount;
                }
            }

            return heatmap;
        }

        /// <summary>
        /// Reset the calculator
        /// </summary>
        public void Reset()
        {
            sampleBuffer.Clear();
            if (binCounts != null)
            {
                System.Array.Clear(binCounts, 0, binCounts.Length);
            }
        }

        /// <summary>
        /// Get entropy interpretation
        /// </summary>
        public string InterpretEntropy(float normalizedEntropy)
        {
            if (normalizedEntropy < 0.3f)
                return "Highly Focused";
            else if (normalizedEntropy < 0.5f)
                return "Focused";
            else if (normalizedEntropy < 0.7f)
                return "Moderate Exploration";
            else if (normalizedEntropy < 0.85f)
                return "Active Exploration";
            else
                return "Highly Dispersed";
        }

        /// <summary>
        /// Get color for entropy value (for visualization)
        /// </summary>
        public Color GetEntropyColor(float normalizedEntropy)
        {
            // Blue (focused) to Red (dispersed)
            return Color.Lerp(Color.blue, Color.red, normalizedEntropy);
        }

        #endregion

        #region Private Methods

        private void InitializeBins()
        {
            binCounts = new int[horizontalBins, verticalBins];
            totalBins = horizontalBins * verticalBins;
            maxPossibleEntropy = Mathf.Log(totalBins, 2);
        }

        /// <summary>
        /// Convert gaze direction to bin indices
        /// </summary>
        private void DirectionToBin(Vector3 direction, out int binX, out int binY)
        {
            // Convert direction to yaw (horizontal) and pitch (vertical) angles
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;

            // Normalize to 0-1 range based on configured angular range
            float normalizedX = (yaw + horizontalRange) / (2f * horizontalRange);
            float normalizedY = (pitch + verticalRange) / (2f * verticalRange);

            // Clamp to valid range
            normalizedX = Mathf.Clamp01(normalizedX);
            normalizedY = Mathf.Clamp01(normalizedY);

            // Convert to bin indices
            binX = Mathf.FloorToInt(normalizedX * horizontalBins);
            binY = Mathf.FloorToInt(normalizedY * verticalBins);

            // Clamp bin indices
            binX = Mathf.Clamp(binX, 0, horizontalBins - 1);
            binY = Mathf.Clamp(binY, 0, verticalBins - 1);
        }

        #endregion
    }
}
