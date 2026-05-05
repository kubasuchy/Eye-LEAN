using NUnit.Framework;
using UnityEngine;
using EyeTracking.Components;
using EyeLean.Data;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage for SessionRecorder. The byte-identical-CSV
    /// regression is best exercised by a PlayMode integration test (where
    /// the full eye+hmd+metadata pipeline can run end-to-end against a
    /// stub IEyeTracker) — these EditMode tests pin the public metadata
    /// API contract that downstream callers rely on, plus the participant-ID
    /// + session-context behavior that round-9 hardware tests showed
    /// were the main failure surface.
    /// </summary>
    public class SessionRecorderTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SessionRecorderTestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void IsMetadataSchemaLocked_BeforeRecording_IsFalse()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            Assert.IsFalse(rec.IsMetadataSchemaLocked);
        }

        [Test]
        public void SetMetadata_String_RoundTripsThroughGetMetadata()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.SetMetadata("Condition", "Control");
            var meta = rec.GetMetadata();
            Assert.IsTrue(meta.HasField("Condition"));
        }

        [Test]
        public void SetMetadata_TypedOverloads_AllPersist()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.SetMetadata("BlockNumber", 3);
            rec.SetMetadata("ResponseTime", 1.234f);
            rec.SetMetadata("IsPractice", true);
            rec.SetMetadata("Stimulus", "stim_42");
            var meta = rec.GetMetadata();
            Assert.IsTrue(meta.HasField("BlockNumber"));
            Assert.IsTrue(meta.HasField("ResponseTime"));
            Assert.IsTrue(meta.HasField("IsPractice"));
            Assert.IsTrue(meta.HasField("Stimulus"));
        }

        [Test]
        public void DeclareMetadataField_BeforeRecording_AddsField()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.DeclareMetadataField("PreDeclaredField", MetadataValueType.Int);
            Assert.IsTrue(rec.GetMetadata().HasField("PreDeclaredField"));
        }

        [Test]
        public void RemoveMetadata_RemovesPreviouslySetField()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.SetMetadata("Tmp", "value");
            Assert.IsTrue(rec.GetMetadata().HasField("Tmp"));
            bool removed = rec.RemoveMetadata("Tmp");
            Assert.IsTrue(removed);
            Assert.IsFalse(rec.GetMetadata().HasField("Tmp"));
        }

        [Test]
        public void ClearMetadata_RemovesAllFields()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.SetMetadata("A", "1");
            rec.SetMetadata("B", "2");
            rec.ClearMetadata();
            Assert.IsFalse(rec.GetMetadata().HasField("A"));
            Assert.IsFalse(rec.GetMetadata().HasField("B"));
        }

        [Test]
        public void SetParticipantID_RoundTripsThroughGetParticipantID()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.SetParticipantID("P_2026_05_02_test");
            Assert.AreEqual("P_2026_05_02_test", rec.GetParticipantID());
        }

        [Test]
        public void SetParticipantID_Null_StoresEmptyString()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.SetParticipantID(null);
            Assert.AreEqual("", rec.GetParticipantID());
        }

        // ---- v1.3 RegisterMetric ----

        [Test]
        public void RegisterMetric_AppendsColumnInOrder()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.RegisterMetric("LiveLoadIndex", () => 0.42f, "F4");
            rec.RegisterMetric("CustomScore", () => "tag_a");
            Assert.AreEqual(2, rec.RegisteredMetrics.Count);
            Assert.AreEqual("LiveLoadIndex", rec.RegisteredMetrics[0].Name);
            Assert.AreEqual("CustomScore", rec.RegisteredMetrics[1].Name);
        }

        [Test]
        public void RegisterMetric_ReplaceExistingByName_KeepsCount()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.RegisterMetric("LiveLoadIndex", () => 0.1f);
            rec.RegisterMetric("LiveLoadIndex", () => 0.9f);
            Assert.AreEqual(1, rec.RegisteredMetrics.Count);
            Assert.AreEqual("0.9000", rec.RegisteredMetrics[0].Getter());
        }

        [Test]
        public void RegisterMetric_FloatGetter_FormatsWithCulture()
        {
            // Force-French invariant test: thousands/comma decimal separator
            // must NOT leak into CSV cells. RegisterMetric uses InvariantCulture.
            var prevCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                var rec = _go.AddComponent<SessionRecorder>();
                rec.RegisterMetric("Pi", () => 3.14159f, "F4");
                Assert.AreEqual("3.1416", rec.RegisteredMetrics[0].Getter());
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prevCulture;
            }
        }

        [Test]
        public void UnregisterMetric_RemovesByName()
        {
            var rec = _go.AddComponent<SessionRecorder>();
            rec.RegisterMetric("A", () => "1");
            rec.RegisterMetric("B", () => "2");
            Assert.IsTrue(rec.UnregisterMetric("A"));
            Assert.AreEqual(1, rec.RegisteredMetrics.Count);
            Assert.AreEqual("B", rec.RegisteredMetrics[0].Name);
            Assert.IsFalse(rec.UnregisterMetric("C")); // not present
        }
    }
}
