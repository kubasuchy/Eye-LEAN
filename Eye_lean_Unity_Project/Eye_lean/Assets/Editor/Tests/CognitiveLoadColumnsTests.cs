using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using EyeTracking.Metrics;

namespace EyeLean.Tests.EditMode
{
    public class CognitiveLoadColumnsTests
    {
        private static List<string> Names(CognitiveLoadConfig cfg, bool bwRaw = true, string legacy = "LiveLoadIndex")
            => CognitiveLoadColumns.Plan(cfg, bwRaw, legacy).Select(c => c.Name).ToList();

        [Test]
        public void Default_AllOn_ProducesFullLegacyOrderedSchema()
        {
            var names = Names(CognitiveLoadConfig.Default);
            CollectionAssert.AreEqual(new[]
            {
                "LiveLoadIndex", "LiveLoadIndex_RIPA2", "LiveLoadIndex_BW",
                "LiveLoadIndex_BW_Raw", "LiveLoadIndex_FFT", "LiveLoadIndex_DWT"
            }, names);
        }

        [Test]
        public void MasterOff_ProducesNoColumns()
        {
            var cfg = CognitiveLoadConfig.Default;
            cfg.Collect = false;
            Assert.IsEmpty(Names(cfg));
        }

        [Test]
        public void MasterOn_AllMethodsOff_ProducesNoColumns()
        {
            var cfg = new CognitiveLoadConfig
            {
                Collect = true, Ripa2 = false, Butterworth = false, Fft = false, Dwt = false,
                DisplayedMethod = CognitiveLoadMethod.RIPA2
            };
            Assert.IsEmpty(Names(cfg));
        }

        [Test]
        public void OnlyRipa2_ProducesLegacyPlusRipa2()
        {
            var cfg = new CognitiveLoadConfig
            {
                Collect = true, Ripa2 = true, Butterworth = false, Fft = false, Dwt = false,
                DisplayedMethod = CognitiveLoadMethod.RIPA2
            };
            CollectionAssert.AreEqual(new[] { "LiveLoadIndex", "LiveLoadIndex_RIPA2" }, Names(cfg));
        }

        [Test]
        public void ButterworthDisabled_OmitsBwAndBwRaw_EvenWhenRawRequested()
        {
            var cfg = CognitiveLoadConfig.Default;
            cfg.Butterworth = false;
            var names = Names(cfg, bwRaw: true);
            CollectionAssert.DoesNotContain(names, "LiveLoadIndex_BW");
            CollectionAssert.DoesNotContain(names, "LiveLoadIndex_BW_Raw");
        }

        [Test]
        public void BwRawFlagOff_OmitsBwRawButKeepsBw()
        {
            var names = Names(CognitiveLoadConfig.Default, bwRaw: false);
            CollectionAssert.Contains(names, "LiveLoadIndex_BW");
            CollectionAssert.DoesNotContain(names, "LiveLoadIndex_BW_Raw");
        }

        [Test]
        public void EmptyLegacyName_OmitsLegacyColumn()
        {
            var names = Names(CognitiveLoadConfig.Default, legacy: "");
            CollectionAssert.DoesNotContain(names, "LiveLoadIndex");
            Assert.AreEqual("LiveLoadIndex_RIPA2", names.First());
        }

        [Test]
        public void IsEnabled_HonorsMasterSwitch()
        {
            var cfg = CognitiveLoadConfig.Default;
            cfg.Collect = false;
            Assert.IsFalse(cfg.IsEnabled(CognitiveLoadMethod.RIPA2));
            Assert.IsFalse(cfg.CollectsAnything);
        }
    }
}
