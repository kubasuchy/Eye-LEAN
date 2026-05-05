using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using EyeLean.Replay.SceneState;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// Write a synthetic events sidecar matching the format
    /// <see cref="EyeLean.SceneState.SceneEventRecorder"/> emits, parse it
    /// back with <see cref="SceneEventCSVParser"/>, and assert all rows
    /// survive intact: frame, timestamp, event type, object id, detail.
    ///
    /// <para>
    /// Coverage:
    /// </para>
    /// <list type="bullet">
    /// <item>Five distinct event types — internal (Spawn, Despawn) and
    /// researcher-defined (ShowInstruction, HideProgress, ChangeDetectionFeedback)</item>
    /// <item>Detail payloads with multi-line text content (recorder's CR/LF
    /// sanitization should keep parser happy)</item>
    /// <item>Empty objectId (researcher events typically omit it)</item>
    /// <item>RecordKV-style payload (k=v;k=v) round-trip via Detail column</item>
    /// <item>Locale-invariant number parsing (parser must use InvariantCulture)</item>
    /// </list>
    /// </summary>
    public class SceneEventCSVRoundTripTests
    {
        private string tmpPath;

        [SetUp]
        public void SetUp()
        {
            tmpPath = Path.Combine(Path.GetTempPath(), $"SceneEvents_RoundTrip_{System.Guid.NewGuid():N}.csv");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort */ }
        }

        [Test]
        public void FiveEventTypes_RoundTripIntact()
        {
            // Build the synthetic sidecar in the exact format SceneEventRecorder produces.
            var rows = new List<(int frame, float t, string type, string objectId, string detail)>
            {
                (10, 0.123456f, "Spawn",                    "abc123", "TargetCube_0"),
                (10, 0.123456f, "ShowInstruction",          "",       "FREE EXPLORATION  Look around the room."),
                (15, 0.249999f, "SetInstructionTextOnly",   "",       "Starting in 3"),
                (42, 0.700000f, "HideProgress",             "",       ""),
                (99, 1.650000f, "ChangeDetectionFeedback",  "",       "isCorrect=1;selectedId=def456;correctId=def456"),
                (123, 2.050000f, "Despawn",                  "abc123", "TargetCube_0"),
            };

            using (var w = new StreamWriter(tmpPath, append: false))
            {
                w.WriteLine("# Eye_lean Scene Events Sidecar");
                w.WriteLine("# FileVersion: 1.0");
                w.WriteLine("# SessionID: test_session");
                w.WriteLine("Frame,T,EventType,ObjectId,Detail");
                foreach (var r in rows)
                {
                    w.WriteLine(string.Concat(
                        r.frame.ToString(CultureInfo.InvariantCulture), ",",
                        r.t.ToString("F6", CultureInfo.InvariantCulture), ",",
                        r.type, ",",
                        r.objectId, ",",
                        r.detail));
                }
            }

            var parser = new SceneEventCSVParser();
            var timeline = parser.ParseFile(tmpPath);

            Assert.IsNotNull(timeline, "Parser returned null");
            Assert.AreEqual("test_session", timeline.SessionId);
            Assert.AreEqual(rows.Count, timeline.TotalEventCount);

            // Group expected rows by frame for parallel comparison.
            var byFrameExpected = new Dictionary<int, List<(string type, string objectId, string detail, float t)>>();
            foreach (var r in rows)
            {
                if (!byFrameExpected.TryGetValue(r.frame, out var list))
                {
                    list = new List<(string, string, string, float)>();
                    byFrameExpected[r.frame] = list;
                }
                list.Add((r.type, r.objectId, r.detail, r.t));
            }

            Assert.AreEqual(byFrameExpected.Count, timeline.FrameCount);
            foreach (var kv in byFrameExpected)
            {
                Assert.IsTrue(timeline.ByFrame.ContainsKey(kv.Key), $"Frame {kv.Key} missing from parsed timeline");
                var parsedBucket = timeline.ByFrame[kv.Key];
                Assert.AreEqual(kv.Value.Count, parsedBucket.Count, $"Row count mismatch at frame {kv.Key}");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var exp = kv.Value[i];
                    var got = parsedBucket[i];
                    Assert.AreEqual(exp.type, got.EventType, $"EventType mismatch at frame {kv.Key} idx {i}");
                    Assert.AreEqual(exp.objectId, got.ObjectId, $"ObjectId mismatch at frame {kv.Key} idx {i}");
                    Assert.AreEqual(exp.detail, got.Detail, $"Detail mismatch at frame {kv.Key} idx {i}");
                    Assert.That(got.T, Is.EqualTo(exp.t).Within(1e-5f), $"Timestamp drift at frame {kv.Key} idx {i}");
                    Assert.AreEqual(kv.Key, got.Frame);
                }
            }
        }

        [Test]
        public void EmptyFile_ReturnsEmptyTimelineNotNull()
        {
            // Recorder may legally produce an empty sidecar (header only).
            using (var w = new StreamWriter(tmpPath, append: false))
            {
                w.WriteLine("# Eye_lean Scene Events Sidecar");
                w.WriteLine("# FileVersion: 1.0");
                w.WriteLine("Frame,T,EventType,ObjectId,Detail");
            }

            var parser = new SceneEventCSVParser();
            var timeline = parser.ParseFile(tmpPath);

            Assert.IsNotNull(timeline);
            Assert.AreEqual(0, timeline.TotalEventCount);
            Assert.AreEqual(0, timeline.FrameCount);
        }

        [Test]
        public void MissingFile_ReturnsNull()
        {
            var parser = new SceneEventCSVParser();
            var timeline = parser.ParseFile(Path.Combine(Path.GetTempPath(), "definitely_does_not_exist_12345.csv"));
            Assert.IsNull(timeline);
        }

        [Test]
        public void DeriveSidecarPath_AppendsConventionalSuffix()
        {
            string main = Path.Combine(Path.GetTempPath(), "EyeTracking_20260503_120000.csv");
            string sidecar = SceneEventReplayer.DeriveSidecarPath(main);
            Assert.That(sidecar.EndsWith("_SceneEvents.csv"), $"Unexpected derived path: {sidecar}");
        }

        [Test]
        public void DeriveSidecarPath_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, SceneEventReplayer.DeriveSidecarPath(null));
            Assert.AreEqual(string.Empty, SceneEventReplayer.DeriveSidecarPath(string.Empty));
        }
    }
}
