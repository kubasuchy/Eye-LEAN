// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Savitzky–Golay first-derivative filter with precomputed coefficients.
    ///
    /// A 2M+1-sample window is fit by ordinary least-squares to a polynomial
    /// of degree N, and the value of the first derivative of that polynomial
    /// at the window center is returned. Equivalent to convolution with a
    /// fixed kernel; coefficients are computed once at construction.
    ///
    /// Reference: Schäfer 2011, "What is a Savitzky–Golay filter?",
    /// IEEE Signal Processing Magazine 28(4), 111–117. The cutoff for a
    /// long filter is approximated by fc ≈ (N+1) / (3.2M − 4.6) (eq. 18),
    /// which is what RIPA2 uses to size M for a target frequency band.
    ///
    /// Convention: the returned value is the derivative per sample (i.e.
    /// dy/dn where n is the sample index). To convert to dy/dt where t
    /// is in seconds, multiply by the sample rate. RIPA2 uses the per-
    /// sample convention in its clip range.
    /// </summary>
    public sealed class SavitzkyGolayDerivative
    {
        public int HalfWidth { get; }
        public int PolyOrder { get; }
        public int WindowSize => 2 * HalfWidth + 1;
        public double[] Coefficients { get; }

        public SavitzkyGolayDerivative(int halfWidth, int polyOrder)
        {
            if (halfWidth < 1) throw new ArgumentOutOfRangeException(nameof(halfWidth), "halfWidth must be >= 1");
            if (polyOrder < 1) throw new ArgumentOutOfRangeException(nameof(polyOrder), "polyOrder must be >= 1 for a derivative");
            if (polyOrder > 2 * halfWidth) throw new ArgumentOutOfRangeException(nameof(polyOrder), "polyOrder must be <= 2*halfWidth");
            HalfWidth = halfWidth;
            PolyOrder = polyOrder;
            Coefficients = ComputeCoefficients(halfWidth, polyOrder);
        }

        /// <summary>
        /// Apply the filter at the center of a 2M+1 window starting at
        /// <paramref name="offset"/> in <paramref name="data"/>. The data
        /// array must contain at least 2M+1 samples from offset onward.
        /// </summary>
        public double Apply(double[] data, int offset)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            int W = WindowSize;
            if (offset < 0 || data.Length - offset < W)
                throw new ArgumentException($"Window of size {W} does not fit at offset {offset} in data of length {data.Length}");
            double sum = 0.0;
            var c = Coefficients;
            for (int i = 0; i < W; i++) sum += c[i] * data[offset + i];
            return sum;
        }

        /// <summary>
        /// Apply the filter to a window starting at <paramref name="startIndex"/>
        /// in a ring buffer, wrapping at the end. The ring must be at least
        /// 2M+1 samples large.
        /// </summary>
        public double ApplyRing(double[] ring, int startIndex)
        {
            if (ring == null) throw new ArgumentNullException(nameof(ring));
            int W = WindowSize;
            if (ring.Length < W) throw new ArgumentException("ring smaller than filter window");
            int n = ring.Length;
            int idx = startIndex;
            if (idx < 0) idx = ((idx % n) + n) % n;
            else if (idx >= n) idx %= n;
            double sum = 0.0;
            var c = Coefficients;
            for (int i = 0; i < W; i++)
            {
                sum += c[i] * ring[idx];
                idx++;
                if (idx == n) idx = 0;
            }
            return sum;
        }

        /// <summary>
        /// Schäfer's normalized-cutoff approximation, valid for long filters
        /// (M ≥ 25, N &lt; M):
        ///   fc ≈ (N + 1) / (3.2 M − 4.6)
        /// where fc = f_target / f_Nyquist. Returns the half-width M that
        /// best matches <paramref name="targetCutoffHz"/> at the given
        /// sample rate. Output is clamped to a sensible minimum.
        /// </summary>
        public static int DeriveHalfWidth(float sampleRateHz, float targetCutoffHz, int polyOrder)
        {
            if (sampleRateHz <= 0f) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (polyOrder < 1) throw new ArgumentOutOfRangeException(nameof(polyOrder));
            float nyquist = sampleRateHz * 0.5f;
            if (targetCutoffHz <= 0f || targetCutoffHz >= nyquist)
                throw new ArgumentOutOfRangeException(nameof(targetCutoffHz), "must be in (0, fs/2)");
            double fc = targetCutoffHz / (double)nyquist;
            // Solve fc = (N+1) / (3.2 M − 4.6) for M.
            double M = ((polyOrder + 1) / fc + 4.6) / 3.2;
            int Mi = (int)Math.Round(M);
            int minM = polyOrder + 1; // need at least polyOrder+1 distinct points
            if (Mi < minM) Mi = minM;
            return Mi;
        }

        // First-derivative SG coefficients via the normal-equations solution
        // of A u = e_1 where A_{j,k} = sum_{i=-M..M} i^(j+k), then h[i] = sum_j (i-M)^j u[j].
        // A is symmetric (N+1)×(N+1); odd moments vanish by symmetry but the
        // full matrix is retained for clarity. Single-precision scales fine:
        // for N≤4 and M≤500 the moment sums stay below ~1e15.
        private static double[] ComputeCoefficients(int M, int N)
        {
            int K = N + 1;
            // Even/odd moments. moments[p] = sum_{i=-M..M} i^p. Odd p => 0.
            var moments = new double[2 * N + 1];
            for (int p = 0; p <= 2 * N; p++)
            {
                if ((p & 1) != 0) { moments[p] = 0.0; continue; }
                double s = 0.0;
                for (int i = -M; i <= M; i++)
                {
                    double v = 1.0;
                    for (int q = 0; q < p; q++) v *= i;
                    s += v;
                }
                moments[p] = s;
            }
            var A = new double[K, K];
            for (int j = 0; j < K; j++)
                for (int k = 0; k < K; k++)
                    A[j, k] = moments[j + k];

            // Solve A u = e_unitIndex, where unitIndex = 1 picks out the
            // first-derivative coefficient row of (A^T A)^{-1} A^T.
            double[] u = SolveLinear(A, 1);

            int W = 2 * M + 1;
            var h = new double[W];
            for (int i = 0; i < W; i++)
            {
                int t = i - M;
                double s = 0.0;
                double tk = 1.0;
                for (int j = 0; j < K; j++)
                {
                    s += tk * u[j];
                    tk *= t;
                }
                h[i] = s;
            }
            return h;
        }

        // Gauss–Jordan elimination with partial pivoting, in-place on a copy.
        private static double[] SolveLinear(double[,] A, int unitIndex)
        {
            int n = A.GetLength(0);
            var M = new double[n, n + 1];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    M[i, j] = A[i, j];
            for (int i = 0; i < n; i++) M[i, n] = (i == unitIndex) ? 1.0 : 0.0;

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                double pivotMag = Math.Abs(M[col, col]);
                for (int r = col + 1; r < n; r++)
                {
                    double mag = Math.Abs(M[r, col]);
                    if (mag > pivotMag) { pivot = r; pivotMag = mag; }
                }
                if (pivotMag < 1e-300) throw new InvalidOperationException("SG normal-equations matrix is singular");
                if (pivot != col)
                {
                    for (int j = 0; j <= n; j++)
                    {
                        double tmp = M[col, j]; M[col, j] = M[pivot, j]; M[pivot, j] = tmp;
                    }
                }
                double inv = 1.0 / M[col, col];
                for (int j = col; j <= n; j++) M[col, j] *= inv;
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = M[r, col];
                    if (f == 0.0) continue;
                    for (int j = col; j <= n; j++) M[r, j] -= f * M[col, j];
                }
            }
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = M[i, n];
            return x;
        }
    }
}
