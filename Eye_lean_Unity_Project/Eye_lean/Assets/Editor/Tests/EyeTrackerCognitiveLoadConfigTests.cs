using NUnit.Framework;
using UnityEngine;
using EyeTracking.Components;
using EyeTracking.Metrics;

namespace EyeLean.Tests.EditMode
{
    public class EyeTrackerCognitiveLoadConfigTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("EyeTrackerCLRoot");

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        [Test]
        public void EyeTracker_IsCognitiveLoadConfigProvider()
        {
            var et = _go.AddComponent<EyeTracker>();
            Assert.IsInstanceOf<ICognitiveLoadConfigProvider>(et);
        }

        [Test]
        public void GetCognitiveLoadConfig_Defaults_AllOnDisplayedRipa2()
        {
            var et = _go.AddComponent<EyeTracker>();
            var cfg = ((ICognitiveLoadConfigProvider)et).GetCognitiveLoadConfig();
            Assert.IsTrue(cfg.Collect);
            Assert.IsTrue(cfg.Ripa2);
            Assert.IsTrue(cfg.Butterworth);
            Assert.IsTrue(cfg.Fft);
            Assert.IsTrue(cfg.Dwt);
            Assert.AreEqual(CognitiveLoadMethod.RIPA2, cfg.DisplayedMethod);
            Assert.IsTrue(cfg.CollectsAnything);
        }
    }
}
