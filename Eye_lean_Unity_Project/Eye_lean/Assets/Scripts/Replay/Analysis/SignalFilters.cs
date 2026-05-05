using UnityEngine;
using System.Collections.Generic;

namespace EyeLean.Replay.Analysis
{
    /// <summary>
    /// Collection of signal processing filters for eye tracking data analysis.
    /// Includes Butterworth low-pass filter and Savitzky-Golay polynomial smoothing filter.
    /// </summary>
    public static class SignalFilters
    {
        #region Butterworth Filter

        /// <summary>
        /// 2nd-order Butterworth low-pass filter.
        /// Provides maximally flat frequency response in the passband.
        /// </summary>
        public class ButterworthFilter
        {
            private float cutoffFrequency;
            private float sampleRate;

            // Filter coefficients
            private float a0, a1, a2, b1, b2;

            // State variables for each axis
            private float[] x1 = new float[3]; // x[n-1] for each axis
            private float[] x2 = new float[3]; // x[n-2] for each axis
            private float[] y1 = new float[3]; // y[n-1] for each axis
            private float[] y2 = new float[3]; // y[n-2] for each axis

            /// <summary>
            /// Create a new Butterworth filter.
            /// </summary>
            /// <param name="cutoffFrequency">Cutoff frequency in Hz</param>
            /// <param name="sampleRate">Sample rate in Hz (typically ~90-120 for eye tracking)</param>
            public ButterworthFilter(float cutoffFrequency = 10f, float sampleRate = 90f)
            {
                this.cutoffFrequency = cutoffFrequency;
                this.sampleRate = sampleRate;
                CalculateCoefficients();
            }

            /// <summary>
            /// Update filter parameters
            /// </summary>
            public void SetParameters(float cutoffFrequency, float sampleRate)
            {
                this.cutoffFrequency = cutoffFrequency;
                this.sampleRate = sampleRate;
                CalculateCoefficients();
            }

            private void CalculateCoefficients()
            {
                // Pre-warped frequency
                float w0 = Mathf.Tan(Mathf.PI * cutoffFrequency / sampleRate);
                float w0sq = w0 * w0;
                float sqrt2 = Mathf.Sqrt(2f);

                // Denominator
                float k = 1f + sqrt2 * w0 + w0sq;

                // Calculate coefficients
                a0 = w0sq / k;
                a1 = 2f * a0;
                a2 = a0;
                b1 = 2f * (w0sq - 1f) / k;
                b2 = (1f - sqrt2 * w0 + w0sq) / k;
            }

            /// <summary>
            /// Filter a single Vector3 value
            /// </summary>
            public Vector3 Filter(Vector3 input)
            {
                Vector3 output = Vector3.zero;

                for (int axis = 0; axis < 3; axis++)
                {
                    float x = input[axis];

                    // Apply filter: y[n] = a0*x[n] + a1*x[n-1] + a2*x[n-2] - b1*y[n-1] - b2*y[n-2]
                    float y = a0 * x + a1 * x1[axis] + a2 * x2[axis] - b1 * y1[axis] - b2 * y2[axis];

                    // Update state
                    x2[axis] = x1[axis];
                    x1[axis] = x;
                    y2[axis] = y1[axis];
                    y1[axis] = y;

                    output[axis] = y;
                }

                return output;
            }

            /// <summary>
            /// Filter a single float value
            /// </summary>
            public float Filter(float input, int channel = 0)
            {
                float y = a0 * input + a1 * x1[channel] + a2 * x2[channel] - b1 * y1[channel] - b2 * y2[channel];

                x2[channel] = x1[channel];
                x1[channel] = input;
                y2[channel] = y1[channel];
                y1[channel] = y;

                return y;
            }

            /// <summary>
            /// Reset filter state
            /// </summary>
            public void Reset()
            {
                for (int i = 0; i < 3; i++)
                {
                    x1[i] = x2[i] = y1[i] = y2[i] = 0f;
                }
            }

            /// <summary>
            /// Filter an array of float values (batch processing)
            /// </summary>
            public float[] FilterBatch(float[] data)
            {
                Reset();
                float[] result = new float[data.Length];

                for (int i = 0; i < data.Length; i++)
                {
                    result[i] = Filter(data[i], 0);
                }

                return result;
            }

            /// <summary>
            /// Filter an array of Vector3 values (batch processing)
            /// </summary>
            public Vector3[] FilterBatch(Vector3[] data)
            {
                Reset();
                Vector3[] result = new Vector3[data.Length];

                for (int i = 0; i < data.Length; i++)
                {
                    result[i] = Filter(data[i]);
                }

                return result;
            }
        }

        #endregion

        #region Savitzky-Golay Filter

        /// <summary>
        /// Savitzky-Golay polynomial smoothing filter.
        /// Preserves signal features like peaks and edges better than moving average.
        /// </summary>
        public class SavitzkyGolayFilter
        {
            private int windowSize;
            private int polynomialOrder;
            private float[] coefficients;
            private Queue<Vector3> buffer;

            /// <summary>
            /// Create a new Savitzky-Golay filter.
            /// </summary>
            /// <param name="windowSize">Window size (must be odd, e.g., 5, 7, 9, 11)</param>
            /// <param name="polynomialOrder">Polynomial order (typically 2 or 3)</param>
            public SavitzkyGolayFilter(int windowSize = 5, int polynomialOrder = 2)
            {
                SetParameters(windowSize, polynomialOrder);
            }

            /// <summary>
            /// Update filter parameters
            /// </summary>
            public void SetParameters(int windowSize, int polynomialOrder)
            {
                // Ensure window size is odd
                if (windowSize % 2 == 0)
                    windowSize++;

                this.windowSize = windowSize;
                this.polynomialOrder = Mathf.Min(polynomialOrder, windowSize - 1);

                // Use precomputed coefficients for common configurations
                coefficients = GetPrecomputedCoefficients(windowSize, this.polynomialOrder);

                // Initialize buffer
                buffer = new Queue<Vector3>(windowSize);
            }

            /// <summary>
            /// Get precomputed coefficients for common window sizes
            /// </summary>
            private float[] GetPrecomputedCoefficients(int window, int order)
            {
                // Precomputed coefficients for quadratic (order 2) smoothing
                // These are symmetric and sum to 1
                switch (window)
                {
                    case 5:
                        return new float[] { -3f / 35f, 12f / 35f, 17f / 35f, 12f / 35f, -3f / 35f };

                    case 7:
                        return new float[] { -2f / 21f, 3f / 21f, 6f / 21f, 7f / 21f, 6f / 21f, 3f / 21f, -2f / 21f };

                    case 9:
                        return new float[] { -21f / 231f, 14f / 231f, 39f / 231f, 54f / 231f, 59f / 231f,
                                             54f / 231f, 39f / 231f, 14f / 231f, -21f / 231f };

                    case 11:
                        return new float[] { -36f / 429f, 9f / 429f, 44f / 429f, 69f / 429f, 84f / 429f, 89f / 429f,
                                             84f / 429f, 69f / 429f, 44f / 429f, 9f / 429f, -36f / 429f };

                    default:
                        // Fall back to simple moving average for unsupported window sizes
                        float[] ma = new float[window];
                        float weight = 1f / window;
                        for (int i = 0; i < window; i++)
                            ma[i] = weight;
                        return ma;
                }
            }

            /// <summary>
            /// Filter a single Vector3 value (real-time)
            /// </summary>
            public Vector3 Filter(Vector3 input)
            {
                buffer.Enqueue(input);

                while (buffer.Count > windowSize)
                {
                    buffer.Dequeue();
                }

                // Not enough samples yet - return input
                if (buffer.Count < windowSize)
                    return input;

                // Apply convolution
                Vector3 result = Vector3.zero;
                int i = 0;

                foreach (Vector3 sample in buffer)
                {
                    result += sample * coefficients[i];
                    i++;
                }

                return result;
            }

            /// <summary>
            /// Filter a single float value
            /// </summary>
            public float Filter(float input, Queue<float> buffer)
            {
                buffer.Enqueue(input);

                while (buffer.Count > windowSize)
                {
                    buffer.Dequeue();
                }

                if (buffer.Count < windowSize)
                    return input;

                float result = 0f;
                int i = 0;

                foreach (float sample in buffer)
                {
                    result += sample * coefficients[i];
                    i++;
                }

                return result;
            }

            /// <summary>
            /// Reset filter state
            /// </summary>
            public void Reset()
            {
                buffer.Clear();
            }

            /// <summary>
            /// Filter an array of float values (batch processing)
            /// Uses forward-backward filtering to eliminate phase shift.
            /// </summary>
            public float[] FilterBatch(float[] data)
            {
                if (data.Length < windowSize)
                    return (float[])data.Clone();

                float[] result = new float[data.Length];
                int halfWindow = windowSize / 2;

                // Forward pass
                for (int i = 0; i < data.Length; i++)
                {
                    if (i < halfWindow || i >= data.Length - halfWindow)
                    {
                        result[i] = data[i];
                        continue;
                    }

                    float sum = 0f;
                    for (int j = 0; j < windowSize; j++)
                    {
                        sum += data[i - halfWindow + j] * coefficients[j];
                    }
                    result[i] = sum;
                }

                return result;
            }

            /// <summary>
            /// Filter an array of Vector3 values (batch processing)
            /// </summary>
            public Vector3[] FilterBatch(Vector3[] data)
            {
                if (data.Length < windowSize)
                    return (Vector3[])data.Clone();

                Vector3[] result = new Vector3[data.Length];
                int halfWindow = windowSize / 2;

                for (int i = 0; i < data.Length; i++)
                {
                    if (i < halfWindow || i >= data.Length - halfWindow)
                    {
                        result[i] = data[i];
                        continue;
                    }

                    Vector3 sum = Vector3.zero;
                    for (int j = 0; j < windowSize; j++)
                    {
                        sum += data[i - halfWindow + j] * coefficients[j];
                    }
                    result[i] = sum;
                }

                return result;
            }

            /// <summary>
            /// Calculate velocity (1st derivative) using Savitzky-Golay coefficients
            /// </summary>
            public Vector3 CalculateVelocity(Vector3 current, Queue<Vector3> history, float sampleRate)
            {
                // Add current to history
                history.Enqueue(current);
                while (history.Count > windowSize)
                {
                    history.Dequeue();
                }

                if (history.Count < windowSize)
                    return Vector3.zero;

                // First derivative coefficients for quadratic fit
                float[] derivCoeffs = GetDerivativeCoefficients();

                Vector3 velocity = Vector3.zero;
                int i = 0;

                foreach (Vector3 sample in history)
                {
                    velocity += sample * derivCoeffs[i];
                    i++;
                }

                // Scale by sample rate to get degrees/second
                return velocity * sampleRate;
            }

            private float[] GetDerivativeCoefficients()
            {
                // First derivative coefficients for Savitzky-Golay (quadratic)
                switch (windowSize)
                {
                    case 5:
                        return new float[] { -2f / 10f, -1f / 10f, 0f, 1f / 10f, 2f / 10f };

                    case 7:
                        return new float[] { -3f / 28f, -2f / 28f, -1f / 28f, 0f, 1f / 28f, 2f / 28f, 3f / 28f };

                    case 9:
                        return new float[] { -4f / 60f, -3f / 60f, -2f / 60f, -1f / 60f, 0f, 1f / 60f, 2f / 60f, 3f / 60f, 4f / 60f };

                    default:
                        // Simple central difference
                        float[] diff = new float[windowSize];
                        int half = windowSize / 2;
                        float norm = 0f;
                        for (int i = 0; i < windowSize; i++)
                        {
                            diff[i] = (i - half);
                            norm += diff[i] * diff[i];
                        }
                        for (int i = 0; i < windowSize; i++)
                        {
                            diff[i] /= norm;
                        }
                        return diff;
                }
            }
        }

        #endregion

        #region Moving Average

        /// <summary>
        /// Simple moving average filter
        /// </summary>
        public class MovingAverageFilter
        {
            private int windowSize;
            private Queue<Vector3> buffer;

            public MovingAverageFilter(int windowSize = 5)
            {
                this.windowSize = windowSize;
                buffer = new Queue<Vector3>(windowSize);
            }

            public Vector3 Filter(Vector3 input)
            {
                buffer.Enqueue(input);

                while (buffer.Count > windowSize)
                {
                    buffer.Dequeue();
                }

                Vector3 sum = Vector3.zero;
                foreach (Vector3 v in buffer)
                {
                    sum += v;
                }

                return sum / buffer.Count;
            }

            public void Reset()
            {
                buffer.Clear();
            }
        }

        #endregion

        #region Exponential Smoothing

        /// <summary>
        /// Exponential moving average filter
        /// </summary>
        public class ExponentialFilter
        {
            private float alpha;
            private Vector3 lastValue;
            private bool initialized;

            /// <summary>
            /// Create exponential filter
            /// </summary>
            /// <param name="alpha">Smoothing factor (0-1). Lower = smoother</param>
            public ExponentialFilter(float alpha = 0.2f)
            {
                this.alpha = Mathf.Clamp01(alpha);
                initialized = false;
            }

            public Vector3 Filter(Vector3 input)
            {
                if (!initialized)
                {
                    lastValue = input;
                    initialized = true;
                    return input;
                }

                lastValue = alpha * input + (1f - alpha) * lastValue;
                return lastValue;
            }

            public void Reset()
            {
                initialized = false;
            }

            public void SetAlpha(float alpha)
            {
                this.alpha = Mathf.Clamp01(alpha);
            }
        }

        #endregion
    }
}
