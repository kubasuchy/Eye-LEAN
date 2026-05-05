using System.IO;
using NUnit.Framework;
using UnityEngine;
using EyeLean.SceneState;
using EyeLean.Replay.SceneState;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// Write a synthetic sidecar with N frames × M objects via
    /// <see cref="SceneStateCSVWriter"/>, parse it back with
    /// <see cref="SceneStateCSVParser"/>, and assert positions / rotations /
    /// active state survive within the F6-formatting precision floor (1e-5).
    /// Catches:
    /// <list type="bullet">
    /// <item>column ordering changes that would silently mis-align fields,</item>
    /// <item>locale-dependent number parsing (writer always invariant; parser must too),</item>
    /// <item>off-by-one in the frame/object indexing.</item>
    /// </list>
    /// </summary>
    public class SceneStateCSVRoundTripTests
    {
        private string tmpPath;

        [SetUp]
        public void SetUp()
        {
            tmpPath = Path.Combine(Path.GetTempPath(), $"SceneState_RoundTrip_{System.Guid.NewGuid():N}.csv");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort */ }
        }

        [Test]
        public void RoundTrip_PreservesPosesWithinF6Precision()
        {
            const int frames = 100;
            const int objects = 5;
            string[] ids = new string[objects];
            for (int o = 0; o < objects; o++) ids[o] = $"obj_{o:D2}_{System.Guid.NewGuid():N}";
            var origin = new Vector3(1f, 2f, 3f);

            // Write
            using (var sw = new StreamWriter(tmpPath, false))
            {
                var writer = new SceneStateCSVWriter(sw, includeParentId: false);
                writer.WriteHeader("test_session", origin, coordinateOriginSet: true, sampleEveryNthFrame: 1, profileName: "TestProfile");
                for (int f = 0; f < frames; f++)
                {
                    for (int o = 0; o < objects; o++)
                    {
                        var row = new SceneStateRow
                        {
                            Frame = f,
                            T = f * 0.0111111f,
                            ObjectId = ids[o],
                            Position = new Vector3(o + f * 0.001f, o * 0.5f, f * 0.01f),
                            Rotation = Quaternion.Euler(o * 5f, f * 1f, 0f),
                            Active = (f % 2 == 0) || (o == 0),
                        };
                        writer.WriteRow(row);
                    }
                }
                writer.Flush();
            }

            // Read
            var parser = new SceneStateCSVParser { DebugMode = false };
            var timeline = parser.ParseFile(tmpPath);
            Assert.NotNull(timeline);
            Assert.AreEqual(frames, timeline.FrameCount);
            Assert.AreEqual(objects, timeline.ObjectCount);
            Assert.IsTrue(timeline.CoordinateOriginSet);
            Assert.AreEqual(origin, timeline.CoordinateOrigin);
            Assert.AreEqual("TestProfile", timeline.ProfileName);
            Assert.AreEqual("test_session", timeline.SessionId);

            // Spot-check pose preservation.
            const float tol = 1e-4f;
            for (int o = 0; o < objects; o++)
            {
                Assert.IsTrue(timeline.ByObject.TryGetValue(ids[o], out var keys));
                Assert.AreEqual(frames, keys.Count);
                for (int f = 0; f < frames; f++)
                {
                    var k = keys[f];
                    Assert.AreEqual(f, k.Frame);
                    Assert.AreEqual(o + f * 0.001f, k.Position.x, tol);
                    Assert.AreEqual(o * 0.5f, k.Position.y, tol);
                    Assert.AreEqual(f * 0.01f, k.Position.z, tol);
                    var expectedRot = Quaternion.Euler(o * 5f, f * 1f, 0f);
                    Assert.AreEqual(expectedRot.x, k.Rotation.x, tol);
                    Assert.AreEqual(expectedRot.y, k.Rotation.y, tol);
                    Assert.AreEqual(expectedRot.z, k.Rotation.z, tol);
                    Assert.AreEqual(expectedRot.w, k.Rotation.w, tol);
                    Assert.AreEqual((f % 2 == 0) || (o == 0), k.Active);
                }
            }
        }

        [Test]
        public void RoundTrip_HonorsCoordinateOriginSetFalse()
        {
            using (var sw = new StreamWriter(tmpPath, false))
            {
                var writer = new SceneStateCSVWriter(sw, includeParentId: false);
                writer.WriteHeader("s", Vector3.zero, coordinateOriginSet: false, sampleEveryNthFrame: 3, profileName: null);
                writer.WriteRow(new SceneStateRow { Frame = 0, T = 0, ObjectId = "x", Position = new Vector3(1, 2, 3), Rotation = Quaternion.identity, Active = true });
                writer.Flush();
            }
            var parser = new SceneStateCSVParser();
            var t = parser.ParseFile(tmpPath);
            Assert.IsFalse(t.CoordinateOriginSet);
            Assert.AreEqual(3, t.SampleEveryNthFrame);
            Assert.AreEqual(string.Empty, t.ProfileName);
        }
    }
}
