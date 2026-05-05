using NUnit.Framework;
using System;
using EyeTracking.Metrics;

namespace EyeTracking.Metrics.Tests
{
    /// <summary>
    /// EditMode coverage for the SG derivative filter and RIPA2 analyzer
    /// that replace the v1.0–v1.2 sym4 LHIPA path.
    ///
    /// Golden values are derived analytically: for a polynomial order N=2
    /// SG first-derivative filter, the coefficients reduce to
    ///   h[i] = (i - M) / sum_{k=-M..M} k^2
    /// which has a closed form in M.
    /// </summary>
    public class RIPA2AnalyzerTests
    {
        // ---- SG filter coefficient correctness ----

        [Test]
        public void SG_M2_N2_MatchesAnalyticForm()
        {
            // For M=2, N=2: h[i] = (i-2) / 10 → [-0.2, -0.1, 0, 0.1, 0.2]
            var f = new SavitzkyGolayDerivative(2, 2);
            Assert.AreEqual(5, f.WindowSize);
            double[] expected = { -0.2, -0.1, 0.0, 0.1, 0.2 };
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(expected[i], f.Coefficients[i], 1e-12, $"coef[{i}]");
        }

        [Test]
        public void SG_M5_N2_MatchesAnalyticForm()
        {
            // sum_{i=-5..5} i^2 = 110. h[i] = (i-5)/110.
            var f = new SavitzkyGolayDerivative(5, 2);
            Assert.AreEqual(11, f.WindowSize);
            for (int i = 0; i < 11; i++)
                Assert.AreEqual((i - 5) / 110.0, f.Coefficients[i], 1e-12, $"coef[{i}]");
        }

        [Test]
        public void SG_DerivativeOfLinearSignal_RecoversSlope()
        {
            // y[i] = 3*i + 7. SG first-derivative at center = 3.
            var f = new SavitzkyGolayDerivative(5, 2);
            var y = new double[11];
            for (int i = 0; i < 11; i++) y[i] = 3.0 * (i - 5) + 7.0;
            double d = f.Apply(y, 0);
            Assert.AreEqual(3.0, d, 1e-10);
        }

        [Test]
        public void SG_DerivativeOfQuadraticAtCenter_IsZero()
        {
            // For y = i^2 centered at 0, dy/dt at t=0 is 0.
            var f = new SavitzkyGolayDerivative(5, 2);
            var y = new double[11];
            for (int i = 0; i < 11; i++) y[i] = (i - 5) * (i - 5);
            double d = f.Apply(y, 0);
            Assert.AreEqual(0.0, d, 1e-10);
        }

        [Test]
        public void SG_DerivativeOfCubic_RequiresOrder3OrHigher()
        {
            // y = i^3. First derivative at center should be 0 for a cubic
            // around 0, but a quadratic SG fit (N=2) under-fits and gives
            // a biased estimate. Order N=3 fits exactly and gives 0.
            var y = new double[11];
            for (int i = 0; i < 11; i++) y[i] = Math.Pow(i - 5, 3);
            var f3 = new SavitzkyGolayDerivative(5, 3);
            Assert.AreEqual(0.0, f3.Apply(y, 0), 1e-9, "order-3 fit on cubic at center should give 0");
            // Sanity: order-3 SG recovers exact derivative at the center
            // for any polynomial of degree ≤ 3.
            for (int i = 0; i < 11; i++) y[i] = 2.0 * Math.Pow(i - 5, 3) + 4.0 * (i - 5) + 11.0;
            // d/dt[2 t^3 + 4 t + 11] at t=0 = 4
            Assert.AreEqual(4.0, f3.Apply(y, 0), 1e-9);
        }

        [Test]
        public void DeriveHalfWidth_60Hz_VlfBand_HitsPaperFormula()
        {
            // Schäfer fc = (N+1)/(3.2M − 4.6).
            // fs=60, target=0.29Hz → fc = 0.29/30 = 0.00966...
            // N=2 → M ≈ (3 / 0.00966 + 4.6) / 3.2 ≈ 98.4 → 98
            int M = SavitzkyGolayDerivative.DeriveHalfWidth(60f, 0.29f, 2);
            Assert.That(M, Is.InRange(96, 100));
        }

        [Test]
        public void DeriveHalfWidth_60Hz_LfBand_HitsPaperFormula()
        {
            // fs=60, target=4Hz → fc=0.1333. N=4 → M ≈ (5/0.1333 + 4.6)/3.2 ≈ 13.16 → 13
            int M = SavitzkyGolayDerivative.DeriveHalfWidth(60f, 4f, 4);
            Assert.That(M, Is.InRange(12, 15));
        }

        // ---- Analyzer state machine ----

        [Test]
        public void Analyzer_NotValidUntilBufferFills()
        {
            var a = new RIPA2Analyzer(60f, 30, 2, 5, 4, bufferSize: 200, smoothingSize: 30);
            Assert.IsFalse(a.IsValid);
            for (int i = 0; i < a.SgVlf.WindowSize - 1; i++)
            {
                a.PushSample(3.0); // arbitrary constant; we only care about the warmup gate
                Assert.IsFalse(a.IsValid, $"should not be valid at {i+1} samples");
            }
            a.PushSample(3.0);
            Assert.IsTrue(a.IsValid);
        }

        [Test]
        public void Analyzer_ConstantSignal_GivesZeroLoad()
        {
            var a = new RIPA2Analyzer(60f, 30, 2, 5, 4, bufferSize: 200, smoothingSize: 30);
            for (int i = 0; i < 200; i++) a.PushSample(3.5);
            Assert.AreEqual(0.0, a.CurrentRaw, 1e-12);
            Assert.AreEqual(0.0, a.CurrentSmoothed, 1e-12);
        }

        [Test]
        public void Analyzer_NaNAndInf_AreIgnored()
        {
            var a = new RIPA2Analyzer(60f, 30, 2, 5, 4, bufferSize: 200, smoothingSize: 30);
            for (int i = 0; i < 50; i++) a.PushSample(3.0);
            int filledBefore = a.SamplesBuffered;
            a.PushSample(double.NaN);
            a.PushSample(double.PositiveInfinity);
            a.PushSample(double.NegativeInfinity);
            Assert.AreEqual(filledBefore, a.SamplesBuffered, "NaN/Inf samples should not advance the buffer");
        }

        [Test]
        public void Analyzer_OutputAlwaysClippedToPaperRange()
        {
            // Random-walk pupil signal pushed through the analyzer; assert
            // the output remains in [0, 1.5] (paper clip range).
            var rng = new Random(42);
            var a = new RIPA2Analyzer(60f, 30, 2, 5, 4, bufferSize: 200, smoothingSize: 30);
            double pupil = 3.5;
            for (int i = 0; i < 800; i++)
            {
                pupil += (rng.NextDouble() - 0.5) * 0.05;
                if (pupil < 1.5) pupil = 1.5;
                if (pupil > 6.0) pupil = 6.0;
                a.PushSample(pupil);
                if (a.IsValid)
                {
                    Assert.That(a.CurrentRaw, Is.InRange(0.0, 1.5));
                    Assert.That(a.CurrentSmoothed, Is.InRange(0.0, 1.5));
                }
            }
        }

        [Test]
        public void Analyzer_Reset_ClearsState()
        {
            var a = new RIPA2Analyzer(60f, 30, 2, 5, 4, bufferSize: 200, smoothingSize: 30);
            for (int i = 0; i < 200; i++) a.PushSample(3.0 + 0.1 * Math.Sin(i * 0.1));
            Assert.IsTrue(a.IsValid);
            a.Reset();
            Assert.IsFalse(a.IsValid);
            Assert.AreEqual(0, a.SamplesBuffered);
            Assert.AreEqual(0.0, a.CurrentRaw);
            Assert.AreEqual(0.0, a.CurrentSmoothed);
        }

        [Test]
        public void Analyzer_OneShotMatchesStreamingOutput()
        {
            // Push a fixed sinusoid through the streaming path; compute the
            // raw output one-shot from the same final window. Should match.
            var a = new RIPA2Analyzer(60f, 30, 2, 5, 4, bufferSize: 200, smoothingSize: 1);
            int N = 200;
            var data = new double[N];
            for (int i = 0; i < N; i++) data[i] = 3.5 + 0.05 * Math.Sin(i * 0.3);
            for (int i = 0; i < N; i++) a.PushSample(data[i]);

            // The streaming computation always uses the most-recent
            // SgVlf.WindowSize samples; one-shot path takes the same.
            int W = a.SgVlf.WindowSize;
            var window = new double[W];
            Array.Copy(data, N - W, window, 0, W);
            double oneShot = a.ComputeRawFromWindow(window);
            Assert.AreEqual(oneShot, a.CurrentRaw, 1e-12);
        }

        [Test]
        public void Analyzer_FromCutoffs_BuildsValidSizes()
        {
            var a = RIPA2Analyzer.FromCutoffs(
                sampleRateHz: 60f,
                vlfCutoffHz: 0.29f, vlfPolyOrder: 2,
                lfCutoffHz: 4f, lfPolyOrder: 4,
                bufferSeconds: 4f, smoothingSeconds: 1.5f);
            Assert.GreaterOrEqual(a.SgVlf.WindowSize, a.SgLf.WindowSize);
            Assert.GreaterOrEqual(a.BufferSize, a.SgVlf.WindowSize);
            Assert.GreaterOrEqual(a.SmoothingSize, 1);
        }

        [Test]
        public void Analyzer_RejectsLfLargerThanVlf()
        {
            // The LF window must fit inside the VLF window for time-aligned
            // evaluation. Constructor must reject the inverted case.
            Assert.Throws<ArgumentException>(() =>
                new RIPA2Analyzer(60f, 5, 2, 30, 4, 200, 30));
        }
    }
}
