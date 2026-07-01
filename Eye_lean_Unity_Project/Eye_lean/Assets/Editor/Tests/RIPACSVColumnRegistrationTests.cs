using System.Linq;
using NUnit.Framework;
using UnityEngine;
using EyeTracking.Components;
using EyeTracking.Metrics;

namespace EyeLean.Tests.EditMode
{
    public class RIPACSVColumnRegistrationTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("RIPACSVColumnRoot");

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        private static string[] RegisteredNames(SessionRecorder rec)
            => rec.RegisteredMetrics.Select(m => m.Name).ToArray();

        [Test]
        public void Default_RegistersFullOrderedSchema()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            var mon = _go.AddComponent<RIPAMonitor>();
            mon.ApplyConfig(CognitiveLoadConfig.Default);
            var col = _go.AddComponent<RIPACSVColumn>();

            col.RegisterColumnsFor(rec, mon);

            CollectionAssert.AreEqual(new[]
            {
                "LiveLoadIndex", "LiveLoadIndex_RIPA2", "LiveLoadIndex_BW",
                "LiveLoadIndex_BW_Raw", "LiveLoadIndex_FFT", "LiveLoadIndex_DWT"
            }, RegisteredNames(rec));
        }

        [Test]
        public void OnlyRipa2_RegistersLegacyPlusRipa2()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            var mon = _go.AddComponent<RIPAMonitor>();
            mon.ApplyConfig(new CognitiveLoadConfig
            {
                Collect = true, Ripa2 = true, Butterworth = false, Fft = false, Dwt = false,
                DisplayedMethod = CognitiveLoadMethod.RIPA2
            });
            var col = _go.AddComponent<RIPACSVColumn>();

            col.RegisterColumnsFor(rec, mon);

            CollectionAssert.AreEqual(
                new[] { "LiveLoadIndex", "LiveLoadIndex_RIPA2" }, RegisteredNames(rec));
        }

        [Test]
        public void MasterOff_RegistersNothing()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            var mon = _go.AddComponent<RIPAMonitor>();
            var cfg = CognitiveLoadConfig.Default;
            cfg.Collect = false;
            mon.ApplyConfig(cfg);
            var col = _go.AddComponent<RIPACSVColumn>();

            col.RegisterColumnsFor(rec, mon);

            Assert.IsEmpty(RegisteredNames(rec));
        }

        [Test]
        public void NullMonitor_RegistersNothing()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            var col = _go.AddComponent<RIPACSVColumn>();

            col.RegisterColumnsFor(rec, null);

            Assert.IsEmpty(RegisteredNames(rec));
        }
    }
}
