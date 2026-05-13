// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Streaming cascade-of-biquads filter (direct-form II transposed). One
    /// sample in, one sample out; per-section state persists across calls.
    /// Mirrors scipy.signal.sosfilt's per-call semantics for sample-by-sample
    /// real-time use.
    ///
    /// Filter state starts at zero by default — equivalent to
    /// <c>scipy.signal.sosfilt(sos, x, zi=None)</c>. For non-zero-mean input
    /// streams (e.g. pupil diameter ~4 mm), call
    /// <see cref="InitializeForConstantInput"/> with the first observed
    /// sample to prime the state to steady-state, removing the startup
    /// transient. This matches the
    /// <c>scipy.signal.sosfilt(sos, x, zi=sosfilt_zi(sos)*x[0])</c> idiom
    /// used in Duchowski 2026 Listing 3.
    ///
    /// SOS layout: 2D array of shape (nSections, 6) with each row
    /// [b0, b1, b2, a0, a1, a2]. a0 is assumed 1 (designer normalizes).
    /// </summary>
    public sealed class SosFilter
    {
        private readonly double[,] sos;             // (nSections, 6)
        private readonly double[,] state;           // (nSections, 2) — z1, z2 per section
        private readonly double[,] ziUnitStep;      // per-section steady-state for x=1, after cascade
        public int NumSections { get; }

        public SosFilter(double[,] sos)
        {
            if (sos == null) throw new ArgumentNullException(nameof(sos));
            if (sos.GetLength(1) != 6) throw new ArgumentException("SOS must have 6 columns: [b0,b1,b2,a0,a1,a2].", nameof(sos));
            NumSections = sos.GetLength(0);
            this.sos = sos;
            state = new double[NumSections, 2];
            ziUnitStep = ComputeUnitStepInitialState(sos);
        }

        public void Reset()
        {
            for (int s = 0; s < NumSections; s++) { state[s, 0] = 0.0; state[s, 1] = 0.0; }
        }

        /// <summary>
        /// Prime the per-section state to the cascade's steady-state response
        /// for a constant input equal to <paramref name="x0"/>. After this
        /// call, the filter's first <see cref="Push"/> with input ≈x0 produces
        /// the steady-state output (DC-gain · x0) with no transient ramp.
        /// </summary>
        public void InitializeForConstantInput(double x0)
        {
            for (int s = 0; s < NumSections; s++)
            {
                state[s, 0] = ziUnitStep[s, 0] * x0;
                state[s, 1] = ziUnitStep[s, 1] * x0;
            }
        }

        /// <summary>
        /// Push one sample through the cascade. Returns the filtered output.
        /// Each section uses direct-form II transposed:
        ///   y[n] = b0·x[n] + z1[n-1]
        ///   z1[n] = b1·x[n] − a1·y[n] + z2[n-1]
        ///   z2[n] = b2·x[n] − a2·y[n]
        /// </summary>
        public double Push(double x)
        {
            double v = x;
            for (int s = 0; s < NumSections; s++)
            {
                double b0 = sos[s, 0], b1 = sos[s, 1], b2 = sos[s, 2];
                double a1 = sos[s, 4], a2 = sos[s, 5];
                double z1 = state[s, 0], z2 = state[s, 1];
                double y = b0 * v + z1;
                state[s, 0] = b1 * v - a1 * y + z2;
                state[s, 1] = b2 * v - a2 * y;
                v = y;
            }
            return v;
        }

        // Compute the cascade-aware per-section initial state for a unit-step
        // input. Equivalent to scipy.signal.sosfilt_zi(sos).
        //
        // For one biquad (direct-form II transposed) at steady state with
        // constant input x and output y = g·x (g = DC gain = ΣbΣa⁻¹):
        //   z2 = (b2 − a2·g)·x
        //   z1 = (b1 + b2 − (a1+a2)·g)·x
        // For a cascade, section i sees the cumulative DC gain of sections
        // 0..i−1 as its input scale.
        private static double[,] ComputeUnitStepInitialState(double[,] sos)
        {
            int nS = sos.GetLength(0);
            var zi = new double[nS, 2];
            double scaleIn = 1.0; // cumulative DC gain entering this section
            for (int s = 0; s < nS; s++)
            {
                double b0 = sos[s, 0], b1 = sos[s, 1], b2 = sos[s, 2];
                double a1 = sos[s, 4], a2 = sos[s, 5];
                double sumB = b0 + b1 + b2;
                double sumA = 1.0 + a1 + a2;
                double g = (sumA == 0) ? 0 : sumB / sumA; // DC gain of this section
                double z1c = b1 + b2 - (a1 + a2) * g;
                double z2c = b2 - a2 * g;
                zi[s, 0] = z1c * scaleIn;
                zi[s, 1] = z2c * scaleIn;
                scaleIn *= g; // input scale into the next section
            }
            return zi;
        }
    }
}
