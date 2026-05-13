// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Pure-C# Cooley–Tukey radix-2 in-place FFT and supporting helpers.
    /// Used by <see cref="FftLfHfAnalyzer"/> (Duchowski 2026 §2 / Listing 1).
    ///
    /// Sized for power-of-two input only (2048-point at 60 Hz covers the
    /// paper's 30 s minimum window with one cache-friendly transform per
    /// update). The transform writes the spectrum back into the input arrays.
    /// </summary>
    public static class Fft
    {
        /// <summary>
        /// Forward DFT computed in-place via Cooley–Tukey radix-2. The arrays
        /// must have the same length, which must be a power of two.
        /// </summary>
        public static void TransformInPlace(double[] real, double[] imag)
        {
            if (real == null) throw new ArgumentNullException(nameof(real));
            if (imag == null) throw new ArgumentNullException(nameof(imag));
            int n = real.Length;
            if (imag.Length != n) throw new ArgumentException("real and imag must have the same length.");
            if (n <= 0 || (n & (n - 1)) != 0) throw new ArgumentException("length must be a power of two.", nameof(real));

            // Bit-reversal permutation.
            for (int i = 0, j = 0; i < n; i++)
            {
                if (i < j)
                {
                    double tr = real[i]; real[i] = real[j]; real[j] = tr;
                    double ti = imag[i]; imag[i] = imag[j]; imag[j] = ti;
                }
                int m = n >> 1;
                while (m > 0 && j >= m) { j -= m; m >>= 1; }
                j += m;
            }

            // Decimation-in-time butterflies.
            for (int size = 2; size <= n; size <<= 1)
            {
                int half = size >> 1;
                double angle = -2.0 * Math.PI / size;
                double wReal = Math.Cos(angle);
                double wImag = Math.Sin(angle);
                for (int start = 0; start < n; start += size)
                {
                    double cr = 1.0, ci = 0.0;
                    for (int k = 0; k < half; k++)
                    {
                        int p = start + k;
                        int q = p + half;
                        double tr = cr * real[q] - ci * imag[q];
                        double ti = cr * imag[q] + ci * real[q];
                        real[q] = real[p] - tr;
                        imag[q] = imag[p] - ti;
                        real[p] += tr;
                        imag[p] += ti;
                        double newCr = cr * wReal - ci * wImag;
                        ci = cr * wImag + ci * wReal;
                        cr = newCr;
                    }
                }
            }
        }

        /// <summary>
        /// Subtract the best linear fit from <paramref name="x"/> in place
        /// (slope and intercept by ordinary least squares). Matches
        /// <c>scipy.signal.detrend(x, type='linear')</c>.
        /// </summary>
        public static void LinearDetrendInPlace(double[] x)
        {
            if (x == null) throw new ArgumentNullException(nameof(x));
            int n = x.Length;
            if (n < 2) return;
            // t_i = i; closed form for OLS slope/intercept.
            double sumT = (n - 1) * n / 2.0;
            double sumT2 = (n - 1) * n * (2 * n - 1) / 6.0;
            double sumX = 0, sumTX = 0;
            for (int i = 0; i < n; i++) { sumX += x[i]; sumTX += i * x[i]; }
            double denom = n * sumT2 - sumT * sumT;
            double slope = (n * sumTX - sumT * sumX) / denom;
            double intercept = (sumX - slope * sumT) / n;
            for (int i = 0; i < n; i++) x[i] -= intercept + slope * i;
        }
    }
}
