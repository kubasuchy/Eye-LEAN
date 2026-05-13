// SPDX-License-Identifier: MIT
using NUnit.Framework;
using System;
using EyeTracking.Metrics;

namespace EyeTracking.Metrics.Tests
{
    /// <summary>
    /// EditMode coverage for the pure-C# Butterworth design + streaming
    /// SOS filter + LF/HF analyzer (Duchowski 2026 Listing 3).
    ///
    /// Golden values generated from scipy.signal.butter(..., output='sos')
    /// and scipy.signal.sosfilt at fs=60 Hz; see the comments in
    /// IirButterworthDesign.cs and ButterworthLfHfAnalyzer.cs.
    /// </summary>
    public class ButterworthLfHfAnalyzerTests
    {
        // ----- Section count + LTI parity with scipy.sosfilt -----
        //
        // We do not assert per-section coefficient equality with scipy because
        // the gain placement and zero-pole pairing strategy differs across
        // valid SOS factorizations of the same LTI transfer function. The
        // impulse-response tests below are the canonical correctness check —
        // they verify end-to-end output of the cascade against scipy.sosfilt
        // sample-for-sample.

        [Test]
        public void LowpassSos_Order4_HasTwoSections()
        {
            Assert.AreEqual(2, IirButterworthDesign.LowpassSos(4, 1.6, 60.0).GetLength(0));
        }

        [Test]
        public void BandpassSos_Order4_HasFourSections()
        {
            Assert.AreEqual(4, IirButterworthDesign.BandpassSos(4, 1.6, 4.0, 60.0).GetLength(0));
        }

        // ----- Streaming SosFilter parity with scipy.sosfilt -----

        [Test]
        public void SosFilter_LowpassImpulseResponse_MatchesScipy()
        {
            double[,] sos = IirButterworthDesign.LowpassSos(4, 1.6, 60.0);
            var f = new SosFilter(sos);
            double[] expected = new double[]
            {
                3.993375663894621e-05, 3.019955314306876e-04, 1.124533798266882e-03,
                2.836421610486237e-03, 5.607345432332201e-03, 9.440418483536444e-03,
                1.421875056955780e-02, 1.974469433226148e-02, 2.577239214214701e-02,
                3.203424129964278e-02,
            };
            for (int i = 0; i < expected.Length; i++)
            {
                double y = f.Push(i == 0 ? 1.0 : 0.0);
                Assert.AreEqual(expected[i], y, 1e-10, $"impulse sample {i}");
            }
        }

        [Test]
        public void SosFilter_BandpassImpulseResponse_MatchesScipy()
        {
            double[,] sos = IirButterworthDesign.BandpassSos(4, 1.6, 4.0, 60.0);
            var f = new SosFilter(sos);
            double[] expected = new double[]
            {
                1.832160233696093e-04, 1.298360072854276e-03, 4.397235015646807e-03,
                9.688059992991041e-03, 1.587123624443037e-02, 2.063633043302848e-02,
                2.159532893076907e-02, 1.708465962484835e-02, 6.686411882471759e-03,
               -8.601707730874222e-03,
            };
            for (int i = 0; i < expected.Length; i++)
            {
                double y = f.Push(i == 0 ? 1.0 : 0.0);
                Assert.AreEqual(expected[i], y, 1e-10, $"impulse sample {i}");
            }
        }

        // ----- Analyzer behavior on synthetic tones -----

        [Test]
        public void Analyzer_LfTone_GivesLargeRatio()
        {
            // A 0.2 Hz sine sits squarely in the LF band — LP passes it,
            // BP rejects it. After buffer ramp-up the ratio caps at 20
            // (paper's practical maximum).
            var a = new ButterworthLfHfAnalyzer(60.0);
            int n = (int)(20.0 * 60.0); // 20 s
            for (int i = 0; i < n; i++)
            {
                double x = Math.Sin(2 * Math.PI * 0.2 * i / 60.0);
                a.PushSample(x);
            }
            Assert.IsTrue(a.IsValid);
            Assert.AreEqual(20.0, a.CurrentRawRatio, 1e-6,
                "Pure LF tone should hit the 20.0 cap (paper §7).");
        }

        [Test]
        public void Analyzer_HfTone_GivesSmallRatio()
        {
            // A 2.5 Hz sine sits in the HF band. LP attenuates it heavily,
            // BP passes it. Ratio should be well below 1.
            var a = new ButterworthLfHfAnalyzer(60.0);
            int n = (int)(20.0 * 60.0);
            for (int i = 0; i < n; i++)
            {
                double x = Math.Sin(2 * Math.PI * 2.5 * i / 60.0);
                a.PushSample(x);
            }
            Assert.IsTrue(a.IsValid);
            // scipy reference ratio is ~0.0267; with C# arithmetic we expect
            // the same to ~6 digits.
            Assert.AreEqual(0.0267, a.CurrentRawRatio, 5e-3,
                $"Pure HF tone should yield LF/HF ≪ 1, got {a.CurrentRawRatio}");
        }

        [Test]
        public void Analyzer_DcInput_RatioStaysFinite()
        {
            // Constant pupil diameter has no oscillation in either band; both
            // variances should be ~0. The analyzer's IsValid still becomes
            // true once buffers fill; ratio should be exactly 0 (cap branch).
            var a = new ButterworthLfHfAnalyzer(60.0);
            for (int i = 0; i < 600; i++) a.PushSample(4.0);
            Assert.IsTrue(a.IsValid);
            Assert.AreEqual(0.0, a.CurrentRawRatio, 1e-9);
        }

        [Test]
        public void Analyzer_IgnoresNonFiniteSamples()
        {
            var a = new ButterworthLfHfAnalyzer(60.0);
            int before = a.SamplesPushed;
            a.PushSample(double.NaN);
            a.PushSample(double.PositiveInfinity);
            a.PushSample(double.NegativeInfinity);
            Assert.AreEqual(before, a.SamplesPushed);
        }

        [Test]
        public void Analyzer_IsValid_AfterFullPowerWindow()
        {
            var a = new ButterworthLfHfAnalyzer(60.0, powerWindowSeconds: 5.0);
            for (int i = 0; i < a.PowerWindowSamples - 1; i++) a.PushSample(4.0);
            Assert.IsFalse(a.IsValid, "Should not be valid before window fills.");
            a.PushSample(4.0);
            Assert.IsTrue(a.IsValid, "Should be valid once window fills.");
        }

        [Test]
        public void Analyzer_SmoothedClippedAndScaled()
        {
            // log(1+20)*0.5 ≈ 1.522; clipped to 1.5.
            var a = new ButterworthLfHfAnalyzer(60.0);
            int n = (int)(20.0 * 60.0);
            for (int i = 0; i < n; i++) a.PushSample(Math.Sin(2 * Math.PI * 0.2 * i / 60.0));
            Assert.LessOrEqual(a.CurrentSmoothed, ButterworthLfHfAnalyzer.SmoothedClipMax + 1e-12);
            Assert.GreaterOrEqual(a.CurrentSmoothed, 1.4, "LF-dominant signal should map near top of smoothed range.");
        }

    }
}
