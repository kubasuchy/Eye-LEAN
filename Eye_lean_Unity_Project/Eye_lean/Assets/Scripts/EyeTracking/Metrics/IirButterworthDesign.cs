// SPDX-License-Identifier: MIT
using System;
using System.Numerics;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Pure-C# Butterworth IIR filter design via analog prototype + bilinear
    /// transform, factored into second-order sections (SOS) suitable for
    /// streaming sample-by-sample filtering. Matches the output of
    /// scipy.signal.butter(..., output='sos') sample-for-sample (modulo
    /// section ordering, which does not affect overall LTI response).
    ///
    /// Used by <see cref="ButterworthLfHfAnalyzer"/> (Duchowski 2026,
    /// "Real-Time Cognitive Load Measurement of Pupillary Oscillation",
    /// Proc. ACM CGIT 9(2) Article 23) to construct lowpass and bandpass
    /// filters for the LF (0-1.6 Hz) and HF (1.6-4 Hz) bands.
    ///
    /// Output convention: each SOS row is [b0, b1, b2, a0, a1, a2] with a0=1.
    /// Number of sections = ceil(order/2) for lowpass; = order for bandpass
    /// (since the LP→BP transform doubles the order).
    /// </summary>
    public static class IirButterworthDesign
    {
        /// <summary>
        /// Design a digital Butterworth lowpass filter. Returns SOS array of
        /// shape (nSections, 6), each row [b0,b1,b2,a0,a1,a2].
        /// </summary>
        public static double[,] LowpassSos(int order, double cutoffHz, double sampleRateHz)
        {
            if (order <= 0) throw new ArgumentOutOfRangeException(nameof(order));
            if (cutoffHz <= 0 || cutoffHz >= sampleRateHz / 2.0)
                throw new ArgumentOutOfRangeException(nameof(cutoffHz), $"cutoffHz must be in (0, {sampleRateHz/2}), got {cutoffHz}");

            // Pre-warp the digital cutoff frequency for the bilinear transform.
            // Bilinear maps s = (2*fs)*(z-1)/(z+1); pre-warping ensures the
            // digital filter's -3 dB point lands at the requested cutoffHz.
            double Wn = 2.0 * sampleRateHz * Math.Tan(Math.PI * cutoffHz / sampleRateHz);

            // Analog Butterworth prototype: N poles on the unit circle, LHP.
            Complex[] protoPoles = ButterworthPrototypePoles(order);

            // Lowpass-to-lowpass frequency transform: s → s / Wn, i.e. poles
            // scale by Wn. The N zeros remain at infinity.
            Complex[] poles = new Complex[order];
            for (int i = 0; i < order; i++) poles[i] = protoPoles[i] * Wn;
            Complex[] zeros = Array.Empty<Complex>();
            double gain = Math.Pow(Wn, order);

            return BilinearToSos(zeros, poles, gain, sampleRateHz);
        }

        /// <summary>
        /// Design a digital Butterworth bandpass filter. Order is the prototype
        /// lowpass order; the bandpass filter has 2N poles and 2N zeros.
        /// Returns SOS of shape (order, 6).
        /// </summary>
        public static double[,] BandpassSos(int order, double lowHz, double highHz, double sampleRateHz)
        {
            if (order <= 0) throw new ArgumentOutOfRangeException(nameof(order));
            double nyquist = sampleRateHz / 2.0;
            if (lowHz <= 0 || highHz <= lowHz || highHz >= nyquist)
                throw new ArgumentOutOfRangeException($"Need 0 < lowHz < highHz < {nyquist}, got [{lowHz},{highHz}]");

            // Pre-warp both band edges.
            double W1 = 2.0 * sampleRateHz * Math.Tan(Math.PI * lowHz  / sampleRateHz);
            double W2 = 2.0 * sampleRateHz * Math.Tan(Math.PI * highHz / sampleRateHz);
            double bw = W2 - W1;
            double w0Sq = W1 * W2;

            Complex[] protoPoles = ButterworthPrototypePoles(order);

            // Lowpass-to-bandpass: s → (s² + ω₀²)/(s·BW). Each prototype pole
            // p becomes two poles, roots of  s² − p·BW·s + ω₀² = 0:
            //   s = p·BW/2 ± sqrt((p·BW/2)² − ω₀²)
            // The N prototype zeros (at infinity) yield N new zeros at 0 (and
            // N more remain at infinity; those map to z=−1 after bilinear).
            Complex[] poles = new Complex[2 * order];
            for (int i = 0; i < order; i++)
            {
                Complex half = protoPoles[i] * (bw / 2.0);
                Complex disc = Complex.Sqrt(half * half - new Complex(w0Sq, 0));
                poles[2 * i]     = half + disc;
                poles[2 * i + 1] = half - disc;
            }
            Complex[] zeros = new Complex[order]; // zeros at s=0 (default-init = 0+0i)
            double gain = Math.Pow(bw, order);

            return BilinearToSos(zeros, poles, gain, sampleRateHz);
        }

        // Analog Butterworth prototype: cutoff 1 rad/s. Poles uniformly spaced
        // on the LHP unit circle at angles θ_k = π·(2k−1+N)/(2N), k=1..N.
        private static Complex[] ButterworthPrototypePoles(int order)
        {
            var p = new Complex[order];
            for (int k = 1; k <= order; k++)
            {
                double theta = Math.PI * (2 * k - 1 + order) / (2.0 * order);
                p[k - 1] = Complex.FromPolarCoordinates(1.0, theta);
            }
            return p;
        }

        // Bilinear transform of an analog zpk → digital zpk → SOS.
        //
        // Bilinear: s = 2·fs·(z−1)/(z+1), giving digital pole/zero
        //   z = (2·fs + s_analog) / (2·fs − s_analog).
        // Zeros at infinity in the analog plane map to z = −1.
        //
        // Gain transformation: k_d = k_a · ∏(2fs − zᵢ) / ∏(2fs − pⱼ).
        private static double[,] BilinearToSos(Complex[] zeros, Complex[] poles, double gain, double fs)
        {
            int nP = poles.Length;
            int nZ = zeros.Length;
            int nExtra = nP - nZ; // analog zeros at infinity → z = −1 after bilinear
            if (nExtra < 0) throw new InvalidOperationException("More zeros than poles is unsupported.");

            double twoFs = 2.0 * fs;

            // Digital poles + accumulating gain factor.
            var pd = new Complex[nP];
            Complex gainD = new Complex(gain, 0);
            for (int i = 0; i < nP; i++)
            {
                pd[i] = (new Complex(twoFs, 0) + poles[i]) / (new Complex(twoFs, 0) - poles[i]);
                gainD /= (new Complex(twoFs, 0) - poles[i]);
            }

            // Digital zeros: bilinear-mapped + nExtra at z = −1.
            var zd = new Complex[nP];
            for (int i = 0; i < nZ; i++)
            {
                zd[i] = (new Complex(twoFs, 0) + zeros[i]) / (new Complex(twoFs, 0) - zeros[i]);
                gainD *= (new Complex(twoFs, 0) - zeros[i]);
            }
            for (int i = nZ; i < nP; i++) zd[i] = new Complex(-1.0, 0);

            // Gain at this point should be real (within floating-point noise).
            double k = gainD.Real;

            return PairToSos(zd, pd, k);
        }

        // Group complex-conjugate pole pairs (and self-real poles in pairs of
        // two) into second-order sections. Pair the zeros similarly and emit
        // one biquad per pair.
        //
        // Section ordering: ascending |pole| ("least peaked first"), which
        // is scipy.signal.zpk2sos's default. The gain is absorbed into the
        // first (least-peaked) section so it cascades through the rest at
        // unity scale, minimizing internal overflow on streams with large
        // dynamic range. Overall LTI response is unchanged by this ordering;
        // matching scipy lets test fixtures use unordered SOS comparison.
        private static double[,] PairToSos(Complex[] zeros, Complex[] poles, double gain)
        {
            int nP = poles.Length;
            if (nP % 2 != 0) throw new NotSupportedException("Odd-order filters not supported (paper uses 4th-order)." );
            int nS = nP / 2;

            // Build pole pairs (each pair = {p, p*}); also keep a |p| for sort.
            var polePairs = new System.Collections.Generic.List<(Complex p1, Complex p2, double mag)>();
            var poleList = new System.Collections.Generic.List<Complex>(poles);
            while (poleList.Count > 0)
            {
                Complex pa = poleList[0];
                poleList.RemoveAt(0);
                int matchIdx = FindConjugateOrSelf(poleList, pa);
                Complex pb = poleList[matchIdx];
                poleList.RemoveAt(matchIdx);
                double mag = Math.Max(pa.Magnitude, pb.Magnitude);
                polePairs.Add((pa, pb, mag));
            }
            // Build zero pairs (no sort needed yet).
            var zeroPairs = new System.Collections.Generic.List<(Complex z1, Complex z2)>();
            var zeroList = new System.Collections.Generic.List<Complex>(zeros);
            while (zeroList.Count >= 2)
            {
                Complex za = zeroList[0];
                zeroList.RemoveAt(0);
                int matchIdx = FindConjugateOrSelf(zeroList, za);
                Complex zb = zeroList[matchIdx];
                zeroList.RemoveAt(matchIdx);
                zeroPairs.Add((za, zb));
            }
            // Pad zero pairs to match section count.
            while (zeroPairs.Count < nS) zeroPairs.Add((new Complex(0,0), new Complex(0,0)));

            // Sort pole pairs by ascending magnitude (least-peaked first).
            polePairs.Sort((a, b) => a.mag.CompareTo(b.mag));

            // For each pole pair, match the zero pair whose pair-magnitude is
            // closest to the pole-pair's magnitude. This minimizes peaking in
            // each individual biquad's magnitude response.
            var sos = new double[nS, 6];
            for (int s = 0; s < nS; s++)
            {
                var (p1, p2, pmag) = polePairs[s];
                int bestZ = 0;
                double bestDiff = double.PositiveInfinity;
                for (int z = 0; z < zeroPairs.Count; z++)
                {
                    double zmag = Math.Max(zeroPairs[z].z1.Magnitude, zeroPairs[z].z2.Magnitude);
                    double diff = Math.Abs(zmag - pmag);
                    if (diff < bestDiff) { bestDiff = diff; bestZ = z; }
                }
                var (z1, z2) = zeroPairs[bestZ];
                zeroPairs.RemoveAt(bestZ);

                double a1 = -(p1 + p2).Real;
                double a2 = (p1 * p2).Real;
                double b0 = 1.0;
                double b1 = -(z1 + z2).Real;
                double b2 = (z1 * z2).Real;

                // Apply the overall gain to the first (least-peaked) section.
                if (s == 0) { b0 *= gain; b1 *= gain; b2 *= gain; }

                sos[s, 0] = b0; sos[s, 1] = b1; sos[s, 2] = b2;
                sos[s, 3] = 1.0; sos[s, 4] = a1; sos[s, 5] = a2;
            }
            return sos;
        }

        // Find x's conjugate in the list within a small tolerance; if x is
        // real, find another real value. Returns the index to remove.
        private static int FindConjugateOrSelf(System.Collections.Generic.List<Complex> list, Complex x)
        {
            const double tol = 1e-9;
            // If x is essentially real, just pair with the next entry (real or
            // any). Otherwise find its conjugate.
            if (Math.Abs(x.Imaginary) < tol)
            {
                // Prefer another real
                for (int i = 0; i < list.Count; i++)
                    if (Math.Abs(list[i].Imaginary) < tol) return i;
                return 0;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (Math.Abs(list[i].Real - x.Real) < tol &&
                    Math.Abs(list[i].Imaginary + x.Imaginary) < tol) return i;
            }
            // Fallback: pair with whatever is next (shouldn't happen for our
            // well-behaved Butterworth poles).
            return 0;
        }
    }
}
