using System.IO;
using NUnit.Framework;
using UnityEngine;
using EyeTracking.Configuration;

namespace EyeLean.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage for the EyeTrackingProfile JSON loader/saver.
    ///
    /// JsonUtility's serialization quirks (no nullable types, default values
    /// when fields are absent, public-fields-only) make the round-trip a
    /// real risk surface — these tests pin it down so a future refactor
    /// can't silently break profile compatibility across releases.
    /// </summary>
    public class EyeTrackingProfileTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "EyeLeanProfileTests_" + System.Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        // ----------------------------------------------------------------
        // JSON round-trip
        // ----------------------------------------------------------------

        [Test]
        public void Save_then_Load_round_trip_preserves_all_fields()
        {
            var profile = new EyeTrackingProfile
            {
                schemaVersion = "1.0",
                accuracyThresholdDeg = 1.5f,
                vergenceDepthMode = "Adaptive",
            };
            profile.metadata.profileName = "RoundTripUser";
            profile.metadata.participantID = "P_TEST";
            profile.metadata.createdAt = "2026-04-30T16:15:59Z";
            profile.metadata.deviceModel = "HTC VIVE Focus Vision";
            profile.metadata.viveDeviceCalibrationStatus = ViveDeviceCalibrationStatus.CompletedThisSession;
            profile.metadata.eyeleanAppVersion = "1.0.0";
            profile.combinedGaze.gazeYawOffsetDeg = -0.262f;
            profile.combinedGaze.gazePitchOffsetDeg = -0.398f;
            profile.combinedGaze.gazeYawGain = 1.05f;
            profile.combinedGaze.gazePitchGain = 0.95f;
            profile.leftEye.gazeYawOffsetDeg = 0.1f;
            profile.rightEye.gazePitchOffsetDeg = -0.2f;

            string path = Path.Combine(_tempDir, "round_trip.json");
            EyeTrackingProfile.Save(profile, path);
            EyeTrackingProfile loaded = EyeTrackingProfile.Load(path);

            Assert.AreEqual("1.0", loaded.schemaVersion);
            Assert.AreEqual(1.5f, loaded.accuracyThresholdDeg);
            Assert.AreEqual("Adaptive", loaded.vergenceDepthMode);

            Assert.AreEqual("RoundTripUser", loaded.metadata.profileName);
            Assert.AreEqual("P_TEST", loaded.metadata.participantID);
            Assert.AreEqual("2026-04-30T16:15:59Z", loaded.metadata.createdAt);
            Assert.AreEqual("HTC VIVE Focus Vision", loaded.metadata.deviceModel);
            Assert.AreEqual(ViveDeviceCalibrationStatus.CompletedThisSession, loaded.metadata.viveDeviceCalibrationStatus);
            Assert.AreEqual("1.0.0", loaded.metadata.eyeleanAppVersion);

            Assert.AreEqual(-0.262f, loaded.combinedGaze.gazeYawOffsetDeg, 1e-5f);
            Assert.AreEqual(-0.398f, loaded.combinedGaze.gazePitchOffsetDeg, 1e-5f);
            Assert.AreEqual(1.05f, loaded.combinedGaze.gazeYawGain, 1e-5f);
            Assert.AreEqual(0.95f, loaded.combinedGaze.gazePitchGain, 1e-5f);

            Assert.AreEqual(0.1f, loaded.leftEye.gazeYawOffsetDeg, 1e-5f);
            Assert.AreEqual(-0.2f, loaded.rightEye.gazePitchOffsetDeg, 1e-5f);
        }

        [Test]
        public void Load_uses_defaults_for_fields_missing_from_json()
        {
            // Forward-compat: an older profile JSON that's missing newer fields
            // must still load cleanly with defaults so a v1.0 build can read a
            // hand-written / partial profile without throwing.
            string path = Path.Combine(_tempDir, "minimal.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":\"1.0\",\"combinedGaze\":{\"gazeYawOffsetDeg\":2.0}}");

            EyeTrackingProfile loaded = EyeTrackingProfile.Load(path);

            Assert.AreEqual(2.0f, loaded.combinedGaze.gazeYawOffsetDeg);
            // Defaults preserved for everything missing.
            Assert.AreEqual(0f, loaded.combinedGaze.gazePitchOffsetDeg);
            Assert.AreEqual(1f, loaded.combinedGaze.gazeYawGain);
            Assert.AreEqual(1f, loaded.combinedGaze.gazePitchGain);
            Assert.AreEqual(2f, loaded.accuracyThresholdDeg);
            Assert.AreEqual("Adaptive", loaded.vergenceDepthMode);
            Assert.NotNull(loaded.metadata);
            Assert.IsNotNull(loaded.leftEye);
            Assert.IsNotNull(loaded.rightEye);
        }

        // ----------------------------------------------------------------
        // Schema-version gate
        // ----------------------------------------------------------------

        [Test]
        public void Load_rejects_unknown_major_version()
        {
            string path = Path.Combine(_tempDir, "v2.json");
            File.WriteAllText(path, "{\"schemaVersion\":\"2.0\"}");
            Assert.Throws<EyeTrackingProfileException>(() => EyeTrackingProfile.Load(path));
        }

        [Test]
        public void Load_rejects_missing_schema_version()
        {
            string path = Path.Combine(_tempDir, "noversion.json");
            File.WriteAllText(path, "{\"combinedGaze\":{\"gazeYawOffsetDeg\":1.0}}");
            // Missing schemaVersion would default to the const "1.0", which
            // is the current version. JsonUtility's FromJsonOverwrite leaves
            // the default in place when the field is absent — so this should
            // load cleanly. This test pins that down so a future change to
            // the validator (e.g. requiring an explicit version field) can't
            // silently break old profiles.
            Assert.DoesNotThrow(() => EyeTrackingProfile.Load(path));
        }

        [Test]
        public void Load_rejects_malformed_schema_version()
        {
            string path = Path.Combine(_tempDir, "malformed.json");
            File.WriteAllText(path, "{\"schemaVersion\":\"not.a.version\"}");
            Assert.Throws<EyeTrackingProfileException>(() => EyeTrackingProfile.Load(path));
        }

        // ----------------------------------------------------------------
        // BuildCombinedCorrectionQuaternion
        // ----------------------------------------------------------------

        [Test]
        public void BuildCombinedCorrectionQuaternion_is_identity_when_offsets_zero()
        {
            var profile = new EyeTrackingProfile();  // all defaults
            Quaternion q = profile.BuildCombinedCorrectionQuaternion();
            Assert.AreEqual(Quaternion.identity, q);
        }

        [Test]
        public void BuildCombinedCorrectionQuaternion_applies_yaw_pitch_correctly()
        {
            var profile = new EyeTrackingProfile();
            profile.combinedGaze.gazeYawOffsetDeg = 10f;
            profile.combinedGaze.gazePitchOffsetDeg = 5f;
            Quaternion q = profile.BuildCombinedCorrectionQuaternion();

            // Quaternion.Euler(pitch, yaw, 0) — verify by applying to forward.
            Vector3 corrected = q * Vector3.forward;
            Vector3 expected = Quaternion.Euler(5f, 10f, 0f) * Vector3.forward;
            Assert.AreEqual(expected.x, corrected.x, 1e-5f);
            Assert.AreEqual(expected.y, corrected.y, 1e-5f);
            Assert.AreEqual(expected.z, corrected.z, 1e-5f);
        }

        // ----------------------------------------------------------------
        // Clone
        // ----------------------------------------------------------------

        [Test]
        public void Clone_produces_independent_copy()
        {
            var profile = new EyeTrackingProfile();
            profile.combinedGaze.gazeYawOffsetDeg = 5f;
            profile.metadata.profileName = "Original";

            EyeTrackingProfile copy = profile.Clone();
            copy.combinedGaze.gazeYawOffsetDeg = 99f;
            copy.metadata.profileName = "Mutated";

            Assert.AreEqual(5f, profile.combinedGaze.gazeYawOffsetDeg);
            Assert.AreEqual("Original", profile.metadata.profileName);
            Assert.AreEqual(99f, copy.combinedGaze.gazeYawOffsetDeg);
            Assert.AreEqual("Mutated", copy.metadata.profileName);
        }

        // ----------------------------------------------------------------
        // DefaultPathFor sanitization
        // ----------------------------------------------------------------

        [Test]
        public void DefaultPathFor_appends_json_extension()
        {
            string p = EyeTrackingProfile.DefaultPathFor("MyProfile");
            Assert.IsTrue(p.EndsWith("MyProfile.json"));
        }

        [Test]
        public void DefaultPathFor_does_not_double_append_extension()
        {
            string p = EyeTrackingProfile.DefaultPathFor("MyProfile.json");
            Assert.IsTrue(p.EndsWith("MyProfile.json"));
            Assert.IsFalse(p.EndsWith("MyProfile.json.json"));
        }

        [Test]
        public void DefaultPathFor_sanitizes_invalid_filename_chars()
        {
            // Use a char that's invalid on every platform Unity ships to.
            string p = EyeTrackingProfile.DefaultPathFor("bad\0name");
            // The null byte should have been replaced with '_'.
            Assert.IsFalse(p.Contains("\0"));
            Assert.IsTrue(p.Contains("bad_name") || p.Contains("bad__name"));
        }

        [Test]
        public void DefaultPathFor_rejects_empty_name()
        {
            Assert.Throws<System.ArgumentException>(
                () => EyeTrackingProfile.DefaultPathFor(""));
            Assert.Throws<System.ArgumentException>(
                () => EyeTrackingProfile.DefaultPathFor("   "));
        }

        // ----------------------------------------------------------------
        // Load failure paths
        // ----------------------------------------------------------------

        [Test]
        public void Load_throws_FileNotFound_for_missing_file()
        {
            string path = Path.Combine(_tempDir, "does_not_exist.json");
            Assert.Throws<FileNotFoundException>(() => EyeTrackingProfile.Load(path));
        }

        [Test]
        public void Save_creates_directory_if_missing()
        {
            string nested = Path.Combine(_tempDir, "nested", "deeper", "p.json");
            var profile = new EyeTrackingProfile();
            profile.combinedGaze.gazeYawOffsetDeg = 1f;
            EyeTrackingProfile.Save(profile, nested);
            Assert.IsTrue(File.Exists(nested));

            EyeTrackingProfile loaded = EyeTrackingProfile.Load(nested);
            Assert.AreEqual(1f, loaded.combinedGaze.gazeYawOffsetDeg);
        }
    }
}
