using NUnit.Framework;
using EyeLean.Replay.SceneState;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// Pin the sidecar-path derivation rule. Both <c>SceneStateRecorder</c>
    /// (writer side) and <c>SceneStateReplayer.DeriveSidecarPath</c> (reader
    /// side) must agree, otherwise sessions don't auto-pair.
    /// </summary>
    public class SidecarPathDerivationTests
    {
        [Test]
        public void Derive_StandardCsvPath()
        {
            string main = "/Users/foo/Logs/EyeTracking_20260502_160451.csv";
            string sidecar = SceneStateReplayer.DeriveSidecarPath(main);
            Assert.AreEqual("/Users/foo/Logs/EyeTracking_20260502_160451_SceneState.csv", sidecar);
        }

        [Test]
        public void Derive_NoExtensionPath()
        {
            string main = "/tmp/data/recording";
            string sidecar = SceneStateReplayer.DeriveSidecarPath(main);
            Assert.AreEqual("/tmp/data/recording_SceneState.csv", sidecar);
        }

        [Test]
        public void Derive_EmptyOrNull_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, SceneStateReplayer.DeriveSidecarPath(null));
            Assert.AreEqual(string.Empty, SceneStateReplayer.DeriveSidecarPath(""));
        }

        [Test]
        public void Derive_DoubleDotFilename()
        {
            // Path.ChangeExtension strips only the LAST extension, so
            // "session.run.csv" -> "session.run" + "_SceneState.csv".
            string main = "/data/session.run.csv";
            string sidecar = SceneStateReplayer.DeriveSidecarPath(main);
            Assert.AreEqual("/data/session.run_SceneState.csv", sidecar);
        }
    }
}
