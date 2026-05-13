// SPDX-License-Identifier: MIT
using NUnit.Framework;
using System;
using EyeTracking.Metrics;

namespace EyeTracking.Metrics.Tests
{
    /// <summary>
    /// EditMode coverage for the FFT and DWT cognitive-load detectors
    /// (Duchowski 2026 Listings 1 and 2). The reference Python port for
    /// detailed band-power comparisons lives outside this repo; here we
    /// verify correctness via:
    ///   • FFT impulse → flat spectrum (Parseval-style check on a unit-impulse)
    ///   • Pure LF tone → ratio saturates at the 20.0 cap
    ///   • Pure HF tone → ratio ≪ 1
    ///   • DC input → ratio = 0 (noise-floor branch)
    ///   • Non-finite samples ignored
    ///   • IsValid transitions on buffer fill
    /// </summary>
    public class FftDwtAnalyzerTests
    {
        // -------------------- FFT primitive --------------------

        [Test]
        public void Fft_OfImpulse_GivesFlatSpectrum()
        {
            // DFT of [1, 0, 0, ..., 0] is [1, 1, 1, ...] (real part) with
            // zero imag part.
            int n = 16;
            var re = new double[n]; re[0] = 1.0;
            var im = new double[n];
            Fft.TransformInPlace(re, im);
            for (int k = 0; k < n; k++)
            {
                Assert.AreEqual(1.0, re[k], 1e-12, $"re[{k}]");
                Assert.AreEqual(0.0, im[k], 1e-12, $"im[{k}]");
            }
        }

        [Test]
        public void Fft_OfCosine_PutsEnergyAtCorrectBin()
        {
            // cos(2π·k₀·n/N) maps to spectral spikes at bins k₀ and N−k₀.
            int n = 64;
            int k0 = 5;
            var re = new double[n];
            var im = new double[n];
            for (int i = 0; i < n; i++) re[i] = Math.Cos(2 * Math.PI * k0 * i / n);
            Fft.TransformInPlace(re, im);
            for (int k = 0; k < n; k++)
            {
                double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                if (k == k0 || k == n - k0)
                    Assert.AreEqual(n / 2.0, mag, 1e-9, $"bin {k} should have magnitude N/2");
                else
                    Assert.AreEqual(0.0, mag, 1e-9, $"bin {k} should be ~0");
            }
        }

        [Test]
        public void LinearDetrend_OnAffine_GivesZero()
        {
            // y = a + b·t → linear detrend should leave residuals ~0.
            int n = 100;
            var y = new double[n];
            for (int i = 0; i < n; i++) y[i] = 2.5 + 0.7 * i;
            Fft.LinearDetrendInPlace(y);
            for (int i = 0; i < n; i++) Assert.AreEqual(0.0, y[i], 1e-9, $"y[{i}]");
        }

        // -------------------- FFT analyzer behavior --------------------

        [Test]
        public void Fft_LfTone_GivesHighRatio()
        {
            var a = new FftLfHfAnalyzer(60.0);
            int n = a.BufferSamples + 10;
            for (int i = 0; i < n; i++) a.PushSample(Math.Sin(2 * Math.PI * 0.2 * i / 60.0));
            Assert.IsTrue(a.IsValid);
            Assert.AreEqual(20.0, a.CurrentRawRatio, 1e-6,
                "Pure LF tone should hit the 20.0 cap.");
        }

        [Test]
        public void Fft_HfTone_GivesLowRatio()
        {
            var a = new FftLfHfAnalyzer(60.0);
            int n = a.BufferSamples + 10;
            for (int i = 0; i < n; i++) a.PushSample(Math.Sin(2 * Math.PI * 2.5 * i / 60.0));
            Assert.IsTrue(a.IsValid);
            Assert.Less(a.CurrentRawRatio, 0.01, $"Pure HF tone should yield LF/HF ≪ 1, got {a.CurrentRawRatio}");
        }

        [Test]
        public void Fft_DcInput_RatioIsZero()
        {
            var a = new FftLfHfAnalyzer(60.0);
            for (int i = 0; i < a.BufferSamples + 10; i++) a.PushSample(4.0);
            Assert.IsTrue(a.IsValid);
            Assert.AreEqual(0.0, a.CurrentRawRatio, 1e-9);
        }

        [Test]
        public void Fft_IgnoresNonFiniteSamples()
        {
            var a = new FftLfHfAnalyzer(60.0);
            int before = a.SamplesPushed;
            a.PushSample(double.NaN);
            a.PushSample(double.PositiveInfinity);
            Assert.AreEqual(before, a.SamplesPushed);
        }

        [Test]
        public void Fft_IsValid_AfterBufferFills()
        {
            var a = new FftLfHfAnalyzer(60.0);
            for (int i = 0; i < a.BufferSamples - 1; i++) a.PushSample(4.0);
            Assert.IsFalse(a.IsValid);
            a.PushSample(4.0);
            Assert.IsTrue(a.IsValid);
        }

        // -------------------- DWT analyzer behavior --------------------

        [Test]
        public void Dwt_LfTone_GivesHighRatio()
        {
            var a = new DwtLfHfAnalyzer(60.0);
            int n = a.BufferSamples + 10;
            for (int i = 0; i < n; i++) a.PushSample(Math.Sin(2 * Math.PI * 0.2 * i / 60.0));
            Assert.IsTrue(a.IsValid);
            Assert.Greater(a.CurrentRawRatio, 10.0,
                $"Pure LF tone should produce a large LF/HF ratio, got {a.CurrentRawRatio}");
        }

        [Test]
        public void Dwt_HfTone_GivesLowRatio()
        {
            var a = new DwtLfHfAnalyzer(60.0);
            int n = a.BufferSamples + 10;
            for (int i = 0; i < n; i++) a.PushSample(Math.Sin(2 * Math.PI * 2.5 * i / 60.0));
            Assert.IsTrue(a.IsValid);
            // DWT's dyadic band boundaries mean a tone near the LF/HF edge
            // splits energy between adjacent levels — the practical HF-tone
            // ratio is well under 1 but not as small as FFT's.
            Assert.Less(a.CurrentRawRatio, 1.0, $"Pure HF tone should yield LF/HF < 1, got {a.CurrentRawRatio}");
        }

        [Test]
        public void Dwt_DcInput_RatioIsZero()
        {
            var a = new DwtLfHfAnalyzer(60.0);
            // Mean-subtraction zeros the signal entirely → all coefficients 0.
            for (int i = 0; i < a.BufferSamples + 10; i++) a.PushSample(4.0);
            Assert.IsTrue(a.IsValid);
            Assert.AreEqual(0.0, a.CurrentRawRatio, 1e-9);
        }

        [Test]
        public void Dwt_IgnoresNonFiniteSamples()
        {
            var a = new DwtLfHfAnalyzer(60.0);
            int before = a.SamplesPushed;
            a.PushSample(double.NaN);
            a.PushSample(double.PositiveInfinity);
            Assert.AreEqual(before, a.SamplesPushed);
        }

        [Test]
        public void Dwt_IsValid_AfterBufferFills()
        {
            var a = new DwtLfHfAnalyzer(60.0);
            for (int i = 0; i < a.BufferSamples - 1; i++) a.PushSample(4.0);
            Assert.IsFalse(a.IsValid);
            a.PushSample(4.0);
            Assert.IsTrue(a.IsValid);
        }

        // -------------------- db4 filter coefficients --------------------

        [Test]
        public void Db4Filters_HaveLengthEight()
        {
            Assert.AreEqual(8, Db4DwtDecomposer.DecLo.Length);
            Assert.AreEqual(8, Db4DwtDecomposer.DecHi.Length);
        }

        [Test]
        public void Db4DecLo_SumsToSqrtTwo()
        {
            // Daubechies analysis low-pass filter has Σh = √2 (energy
            // normalization for the orthogonal scaling function).
            double sum = 0;
            for (int i = 0; i < Db4DwtDecomposer.DecLo.Length; i++) sum += Db4DwtDecomposer.DecLo[i];
            Assert.AreEqual(Math.Sqrt(2.0), sum, 1e-12);
        }

        [Test]
        public void Db4DecHi_SumsToZero()
        {
            // Daubechies analysis high-pass filter has Σg = 0 (zero DC gain).
            double sum = 0;
            for (int i = 0; i < Db4DwtDecomposer.DecHi.Length; i++) sum += Db4DwtDecomposer.DecHi[i];
            Assert.AreEqual(0.0, sum, 1e-12);
        }
    }
}
