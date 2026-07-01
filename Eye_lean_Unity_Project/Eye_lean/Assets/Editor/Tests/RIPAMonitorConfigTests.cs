using NUnit.Framework;
using UnityEngine;
using EyeTracking.Metrics;

namespace EyeLean.Tests.EditMode
{
    public class RIPAMonitorConfigTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("RIPAMonitorConfigRoot");

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        [Test]
        public void ApplyConfig_Default_EnablesMonitorAndAllMethods()
        {
            var m = _go.AddComponent<RIPAMonitor>();
            m.ApplyConfig(CognitiveLoadConfig.Default);
            Assert.IsTrue(m.Enabled);
            Assert.IsTrue(m.IsMethodEnabled(CognitiveLoadMethod.RIPA2));
            Assert.IsTrue(m.IsMethodEnabled(CognitiveLoadMethod.Butterworth));
            Assert.IsTrue(m.IsMethodEnabled(CognitiveLoadMethod.FFT));
            Assert.IsTrue(m.IsMethodEnabled(CognitiveLoadMethod.DWT));
        }

        [Test]
        public void ApplyConfig_MasterOff_DisablesEverything()
        {
            var m = _go.AddComponent<RIPAMonitor>();
            var cfg = CognitiveLoadConfig.Default;
            cfg.Collect = false;
            m.ApplyConfig(cfg);
            Assert.IsFalse(m.Enabled);
            Assert.IsFalse(m.IsMethodEnabled(CognitiveLoadMethod.RIPA2));
        }

        [Test]
        public void ApplyConfig_OnlyFft_EnablesFftOnly()
        {
            var m = _go.AddComponent<RIPAMonitor>();
            m.ApplyConfig(new CognitiveLoadConfig
            {
                Collect = true, Ripa2 = false, Butterworth = false, Fft = true, Dwt = false,
                DisplayedMethod = CognitiveLoadMethod.FFT
            });
            Assert.IsTrue(m.IsMethodEnabled(CognitiveLoadMethod.FFT));
            Assert.IsFalse(m.IsMethodEnabled(CognitiveLoadMethod.RIPA2));
            Assert.IsFalse(m.IsMethodEnabled(CognitiveLoadMethod.Butterworth));
            Assert.IsFalse(m.IsMethodEnabled(CognitiveLoadMethod.DWT));
        }

        [Test]
        public void CurrentConfig_RoundTripsAppliedFlags()
        {
            var m = _go.AddComponent<RIPAMonitor>();
            var cfg = new CognitiveLoadConfig
            {
                Collect = true, Ripa2 = true, Butterworth = false, Fft = false, Dwt = true,
                DisplayedMethod = CognitiveLoadMethod.DWT
            };
            m.ApplyConfig(cfg);
            var got = m.CurrentConfig;
            Assert.IsTrue(got.Collect);
            Assert.IsTrue(got.Ripa2);
            Assert.IsFalse(got.Butterworth);
            Assert.IsFalse(got.Fft);
            Assert.IsTrue(got.Dwt);
            Assert.AreEqual(CognitiveLoadMethod.DWT, got.DisplayedMethod);
        }
    }
}
