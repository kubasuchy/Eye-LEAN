// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Multi-level discrete wavelet transform using the Daubechies-4 (db4)
    /// filter pair, with periodization-mode boundary handling on a
    /// power-of-two input length. Produces a hierarchy of detail and
    /// approximation bands suitable for energy-per-band analysis
    /// (Duchowski 2026 §3 / Listing 2 — PupilDWTRatioDetector).
    ///
    /// Boundary mode differs from <c>pywt</c>'s default 'symmetric': we use
    /// periodization on a power-of-two signal so each level halves exactly,
    /// which keeps the cascade simple and the coefficient counts predictable.
    /// For energy summation across many coefficients the choice of boundary
    /// mode shifts only a handful of edge values, leaving LF/HF ratios
    /// quantitatively close to <c>pywt</c>'s output and qualitatively
    /// identical (both detect the same band-power asymmetries).
    /// </summary>
    public static class Db4DwtDecomposer
    {
        // pywt.Wavelet('db4').dec_lo / dec_hi (analysis filters).
        public static readonly double[] DecLo = new double[]
        {
            -0.010597401785069032,
             0.03288301166688520,
             0.030841381835560764,
            -0.18703481171909309,
            -0.027983769416859854,
             0.6308807679298589,
             0.7148465705529157,
             0.2303778133088965,
        };
        public static readonly double[] DecHi = new double[]
        {
            -0.2303778133088965,
             0.7148465705529157,
            -0.6308807679298589,
            -0.027983769416859854,
             0.18703481171909309,
             0.030841381835560764,
            -0.0328830116668852,
            -0.010597401785069032,
        };

        /// <summary>
        /// Decompose <paramref name="signal"/> (length = power of two) into
        /// <paramref name="maxLevel"/> levels. Returns an array where
        /// index 0 holds the approximation at the deepest level
        /// (lowest-frequency band: [0, fs / 2^(maxLevel+1)]) and indices
        /// 1..maxLevel hold detail bands ordered from deepest to shallowest
        /// (matching <c>pywt.wavedec</c>'s return order).
        /// </summary>
        public static double[][] Decompose(double[] signal, int maxLevel)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            int n = signal.Length;
            if (n <= 0 || (n & (n - 1)) != 0) throw new ArgumentException("signal length must be a power of two.", nameof(signal));
            if (maxLevel <= 0) throw new ArgumentOutOfRangeException(nameof(maxLevel));
            if ((n >> maxLevel) < 1) throw new ArgumentException("maxLevel too large for signal length.", nameof(maxLevel));

            var coeffs = new double[maxLevel + 1][];
            double[] cur = (double[])signal.Clone();
            for (int level = 1; level <= maxLevel; level++)
            {
                int m = cur.Length / 2;
                var approx = new double[m];
                var detail = new double[m];
                ConvolveAndDownsample(cur, DecLo, approx);
                ConvolveAndDownsample(cur, DecHi, detail);
                // Detail at this level corresponds to the *shallowest*
                // remaining frequency band; pywt orders coeffs deepest-first,
                // so this detail's slot is (maxLevel - level + 1).
                coeffs[maxLevel - level + 1] = detail;
                cur = approx;
            }
            coeffs[0] = cur; // final approximation
            return coeffs;
        }

        /// <summary>
        /// One level of DWT: convolve <paramref name="x"/> with filter
        /// <paramref name="h"/> (length 8 for db4) and downsample by 2 using
        /// periodization boundary mode. Result has length x.Length / 2.
        /// </summary>
        private static void ConvolveAndDownsample(double[] x, double[] h, double[] y)
        {
            int n = x.Length;
            int L = h.Length;
            int m = y.Length; // n/2
            for (int i = 0; i < m; i++)
            {
                double acc = 0.0;
                for (int k = 0; k < L; k++)
                {
                    int idx = (2 * i + k) % n;
                    if (idx < 0) idx += n;
                    acc += h[k] * x[idx];
                }
                y[i] = acc;
            }
        }
    }
}
