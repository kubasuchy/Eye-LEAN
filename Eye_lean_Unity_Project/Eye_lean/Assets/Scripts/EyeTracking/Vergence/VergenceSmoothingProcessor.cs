using UnityEngine;
using System.Collections.Generic;

namespace EyeTracking.Vergence
{
    /// <summary>
    /// Handles smoothing of vergence calculations with multiple algorithm support.
    /// Supports WeightedEMA, Butterworth IIR, and Savitzky-Golay polynomial smoothing.
    /// </summary>
    public class VergenceSmoothingProcessor
    {
        private Queue<Vector3> gazePointHistory = new Queue<Vector3>();
        private Queue<float> qualityHistory = new Queue<float>();
        private Vector3 lastValidPoint = Vector3.zero;
        private VergenceSmoothingSettings settings;

        // Butterworth filter state (2nd order)
        private Vector3[] butterworthX = new Vector3[3]; // Input history
        private Vector3[] butterworthY = new Vector3[3]; // Output history
        private float[] butterworthA = new float[3];     // Denominator coefficients
        private float[] butterworthB = new float[3];     // Numerator coefficients
        private bool butterworthInitialized = false;

        // Savitzky-Golay coefficients (quadratic/cubic smoothing)
        private static readonly float[] savitzkyGolayCoeffs5 = { -0.086f, 0.343f, 0.486f, 0.343f, -0.086f };
        private static readonly float[] savitzkyGolayCoeffs7 = { -0.095f, 0.143f, 0.286f, 0.333f, 0.286f, 0.143f, -0.095f };
        private static readonly float[] savitzkyGolayCoeffs9 = { -0.091f, 0.061f, 0.168f, 0.234f, 0.255f, 0.234f, 0.168f, 0.061f, -0.091f };
        private static readonly float[] savitzkyGolayCoeffs11 = { -0.084f, 0.021f, 0.103f, 0.161f, 0.196f, 0.207f, 0.196f, 0.161f, 0.103f, 0.021f, -0.084f };

        public VergenceSmoothingProcessor(VergenceSmoothingSettings smoothingSettings)
        {
            settings = smoothingSettings;
            InitializeButterworthCoefficients();
        }

        public void UpdateSettings(VergenceSmoothingSettings newSettings)
        {
            bool cutoffChanged = settings.butterworth.cutoffFrequency != newSettings.butterworth.cutoffFrequency;
            settings = newSettings;

            // Recalculate Butterworth coefficients if cutoff changed
            if (cutoffChanged)
            {
                InitializeButterworthCoefficients();
            }

            // Adjust buffer size if needed (used by WeightedEMA)
            while (gazePointHistory.Count > settings.weightedEMA.bufferSize)
            {
                gazePointHistory.Dequeue();
                qualityHistory.Dequeue();
            }
        }

        private void InitializeButterworthCoefficients()
        {
            // 2nd order Butterworth low-pass filter coefficients.
            // The normalized cutoff is in (0, 0.5); clamping to 0.499 avoids
            // tan(pi/2) at the Nyquist limit, which would produce Infinity
            // coefficients and destroy the filter state on first use.
            float cutoff = Mathf.Clamp(settings.butterworth.cutoffFrequency, 0.01f, 0.499f);
            float c = 1.0f / Mathf.Tan(Mathf.PI * cutoff);
            float c2 = c * c;
            float sqrt2 = Mathf.Sqrt(2.0f);
            float denom = 1.0f + sqrt2 * c + c2;

            butterworthB[0] = 1.0f / denom;
            butterworthB[1] = 2.0f / denom;
            butterworthB[2] = 1.0f / denom;

            butterworthA[0] = 1.0f;
            butterworthA[1] = 2.0f * (1.0f - c2) / denom;
            butterworthA[2] = (1.0f - sqrt2 * c + c2) / denom;
        }

        public Vector3 ProcessPoint(Vector3 rawPoint, float quality, float distance)
        {
            // Reject NaN/Inf at the boundary so degenerate upstream samples
            // (e.g., a ray-intersection that returned NaN) don't poison the
            // IIR/EMA filter state forever. Returning the last good point
            // keeps the consumer rendering at a sane location.
            if (!IsFinite(rawPoint))
            {
                return lastValidPoint;
            }

            if (!settings.enableSmoothing)
            {
                lastValidPoint = rawPoint;
                return rawPoint;
            }

            // Add to history (used by WeightedEMA and SavitzkyGolay)
            gazePointHistory.Enqueue(rawPoint);
            qualityHistory.Enqueue(quality);

            // Maintain buffer size
            int requiredSize = settings.method == VergenceSmoothingMethod.SavitzkyGolay
                ? GetSavitzkyGolayWindowSize()  // SG needs samples matching window size
                : settings.weightedEMA.bufferSize;

            while (gazePointHistory.Count > requiredSize)
            {
                gazePointHistory.Dequeue();
                qualityHistory.Dequeue();
            }

            // Apply selected smoothing method
            Vector3 smoothedPoint;
            switch (settings.method)
            {
                case VergenceSmoothingMethod.Butterworth:
                    smoothedPoint = ApplyButterworthFilter(rawPoint);
                    break;

                case VergenceSmoothingMethod.SavitzkyGolay:
                    smoothedPoint = ApplySavitzkyGolayFilter();
                    break;

                case VergenceSmoothingMethod.WeightedEMA:
                default:
                    float effectiveSmoothingFactor = settings.weightedEMA.smoothingFactor;
                    if (settings.weightedEMA.adaptiveSmoothing)
                    {
                        effectiveSmoothingFactor = GetAdaptiveSmoothingFactor(distance, quality);
                    }
                    smoothedPoint = ApplyWeightedEMA(rawPoint, effectiveSmoothingFactor);
                    break;
            }

            // Belt-and-braces: if any filter produced a non-finite value
            // (e.g. due to coefficient drift or a NaN slipping through the
            // history buffer), don't propagate it and don't update state.
            if (!IsFinite(smoothedPoint))
            {
                return lastValidPoint;
            }

            lastValidPoint = smoothedPoint;
            return smoothedPoint;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private float GetAdaptiveSmoothingFactor(float distance, float quality)
        {
            float baseFactor = settings.weightedEMA.smoothingFactor;

            // Less smoothing for close objects (more responsive)
            if (distance < 2.0f)
                baseFactor *= 0.7f;
            // ALSO less smoothing for distant objects (need responsiveness for far wall)
            else if (distance > 4.0f)
                baseFactor *= 0.6f;

            // Quality-based adjustment (surface hits have quality near 0)
            if (quality < 0.1f)
                baseFactor *= 0.9f;  // High quality - slightly less smoothing
            else if (quality > 0.5f)
                baseFactor *= 0.8f;  // Low quality - less smoothing to stay responsive

            return Mathf.Clamp(baseFactor, 0.1f, 0.8f);
        }

        private Vector3 ApplyWeightedEMA(Vector3 rawPoint, float smoothingFactor)
        {
            if (gazePointHistory.Count == 0)
                return rawPoint;

            // Calculate weighted average
            Vector3 averagePoint = Vector3.zero;
            float totalWeight = 0f;

            var pointArray = gazePointHistory.ToArray();
            var qualityArray = qualityHistory.ToArray();

            for (int i = 0; i < pointArray.Length; i++)
            {
                float timeWeight = (float)(i + 1) / pointArray.Length;
                float qualityWeight = Mathf.Clamp01(1f - qualityArray[i]);
                float weight = timeWeight * qualityWeight;

                averagePoint += pointArray[i] * weight;
                totalWeight += weight;
            }

            // Tiny-positive totalWeight from float drift would still divide and
            // produce a near-Infinity result; require a meaningful weight.
            if (totalWeight > 1e-6f)
                averagePoint /= totalWeight;
            else
                averagePoint = rawPoint;

            // Apply exponential smoothing
            if (lastValidPoint != Vector3.zero)
            {
                float alpha = 1f - smoothingFactor;
                return alpha * averagePoint + smoothingFactor * lastValidPoint;
            }

            return averagePoint;
        }

        private Vector3 ApplyButterworthFilter(Vector3 rawPoint)
        {
            // Initialize filter state on first use
            if (!butterworthInitialized)
            {
                for (int i = 0; i < 3; i++)
                {
                    butterworthX[i] = rawPoint;
                    butterworthY[i] = rawPoint;
                }
                butterworthInitialized = true;
                return rawPoint;
            }

            // Shift history
            butterworthX[2] = butterworthX[1];
            butterworthX[1] = butterworthX[0];
            butterworthX[0] = rawPoint;

            butterworthY[2] = butterworthY[1];
            butterworthY[1] = butterworthY[0];

            // Apply IIR filter: y[0] = b[0]*x[0] + b[1]*x[1] + b[2]*x[2] - a[1]*y[1] - a[2]*y[2]
            butterworthY[0] = butterworthB[0] * butterworthX[0]
                            + butterworthB[1] * butterworthX[1]
                            + butterworthB[2] * butterworthX[2]
                            - butterworthA[1] * butterworthY[1]
                            - butterworthA[2] * butterworthY[2];

            return butterworthY[0];
        }

        private int GetSavitzkyGolayWindowSize()
        {
            // Ensure odd window size, clamp to supported range
            int size = settings.savitzkyGolay.windowSize;
            if (size < 5) size = 5;
            if (size > 11) size = 11;
            if (size % 2 == 0) size++; // Make odd
            return size;
        }

        private float[] GetSavitzkyGolayCoefficients(int windowSize)
        {
            switch (windowSize)
            {
                case 7: return savitzkyGolayCoeffs7;
                case 9: return savitzkyGolayCoeffs9;
                case 11: return savitzkyGolayCoeffs11;
                default: return savitzkyGolayCoeffs5;
            }
        }

        private Vector3 ApplySavitzkyGolayFilter()
        {
            var pointArray = gazePointHistory.ToArray();
            int count = pointArray.Length;

            // Get window size and coefficients based on user setting
            int windowSize = GetSavitzkyGolayWindowSize();
            float[] coeffs = GetSavitzkyGolayCoefficients(windowSize);

            // Need minimum points for selected filter
            if (count < windowSize)
                return count > 0 ? pointArray[count - 1] : Vector3.zero;

            // Get the most recent points for the window
            int startIdx = count - windowSize;
            Vector3 result = Vector3.zero;

            for (int i = 0; i < windowSize; i++)
            {
                result += coeffs[i] * pointArray[startIdx + i];
            }

            return result;
        }

        public void Reset()
        {
            gazePointHistory.Clear();
            qualityHistory.Clear();
            lastValidPoint = Vector3.zero;
            butterworthInitialized = false;
        }
    }
}
