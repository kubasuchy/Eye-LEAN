using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using EyeTracking.Vergence;

namespace EyeTracking.Configuration
{
    /// <summary>
    /// Per-user eye-tracking calibration profile. Captures the residual
    /// software-side corrections needed AFTER the headset's own device-level
    /// eye calibration has been done — yaw/pitch offset, optional gain, and
    /// the vergence + accuracy settings the user prefers.
    ///
    /// Profiles are saved as plain JSON in
    /// <c>Application.persistentDataPath/EyeLeanProfiles/</c>. Other Unity
    /// projects (or Python analysis scripts) can load a profile and apply
    /// the same correction to gaze data captured later.
    ///
    /// Schema versioning: the loader rejects unknown majors. Adding new
    /// fields with default values is non-breaking; renaming or removing
    /// fields requires a major bump and a migration step.
    /// </summary>
    [Serializable]
    public class EyeTrackingProfile
    {
        public const string CurrentSchemaVersion = "1.0";

        public string schemaVersion = CurrentSchemaVersion;
        public ProfileMetadata metadata = new ProfileMetadata();
        public GazeCorrection combinedGaze = new GazeCorrection();
        // Per-eye structs are present in the v1.0 schema (so v1.1 can fit
        // them without a schema bump) but the v1.0 calibrator only fits
        // combinedGaze. Per-eye fitting requires either a monocular
        // calibration mode or a structured fixation pattern that lets us
        // separate left/right residuals — neither is in scope for v1.0.
        public GazeCorrection leftEye = new GazeCorrection();
        public GazeCorrection rightEye = new GazeCorrection();
        public VergenceCalculationSettings vergenceSettings = new VergenceCalculationSettings();
        public VergenceConstraintSettings constraintSettings = new VergenceConstraintSettings();
        public string vergenceDepthMode = "Adaptive";
        public float accuracyThresholdDeg = 2f;

        /// <summary>Default location for profile files.</summary>
        public static string DefaultDirectory =>
            Path.Combine(Application.persistentDataPath, "EyeLeanProfiles");

        /// <summary>The conventional filename for the auto-loaded default profile.</summary>
        public const string DefaultProfileFileName = "_default.json";

        /// <summary>
        /// Resolve a bare profile name (without path) to a full file path
        /// inside the default directory, sanitizing against invalid path chars.
        /// </summary>
        public static string DefaultPathFor(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                throw new ArgumentException("Profile name cannot be empty", nameof(profileName));

            string safeName = profileName;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(c, '_');
            }
            if (!safeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                safeName += ".json";
            }
            return Path.Combine(DefaultDirectory, safeName);
        }

        /// <summary>
        /// Load a profile from disk. The path may be absolute or a bare
        /// profile name (resolved via DefaultPathFor).
        /// </summary>
        public static EyeTrackingProfile Load(string pathOrName)
        {
            string path = Path.IsPathRooted(pathOrName) ? pathOrName : DefaultPathFor(pathOrName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Eye tracking profile not found: {path}", path);
            }

            string json = File.ReadAllText(path);
            // FromJsonOverwrite onto a fresh instance so any field missing
            // from the JSON keeps its default value — gives forward-compatible
            // tolerance for newer profiles read by older code.
            EyeTrackingProfile profile = new EyeTrackingProfile();
            JsonUtility.FromJsonOverwrite(json, profile);

            ValidateSchemaVersion(profile, path);
            return profile;
        }

        /// <summary>
        /// Save a profile to disk. The path may be absolute or a bare name.
        /// </summary>
        public static void Save(EyeTrackingProfile profile, string pathOrName)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            string path = Path.IsPathRooted(pathOrName) ? pathOrName : DefaultPathFor(pathOrName);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonUtility.ToJson(profile, true);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Enumerate the bare profile names (without extension) found in the
        /// default directory. Returns empty list if the directory does not
        /// exist.
        /// </summary>
        public static IReadOnlyList<string> ListProfiles(string directory = null)
        {
            string dir = directory ?? DefaultDirectory;
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            string[] files = Directory.GetFiles(dir, "*.json");
            var names = new List<string>(files.Length);
            foreach (var f in files)
            {
                names.Add(Path.GetFileNameWithoutExtension(f));
            }
            return names;
        }

        /// <summary>
        /// Deep clone via JSON round-trip. Used by the live-tune UI so the
        /// user can revert their nudges without mutating the original.
        /// </summary>
        public EyeTrackingProfile Clone()
        {
            string json = JsonUtility.ToJson(this);
            EyeTrackingProfile copy = new EyeTrackingProfile();
            JsonUtility.FromJsonOverwrite(json, copy);
            return copy;
        }

        /// <summary>
        /// Build the head-local correction quaternion that the eye-tracking
        /// pipeline applies to combined-gaze direction. Returns identity when
        /// all corrections are at their default values, so the pipeline can
        /// unconditionally pre-multiply without a runtime-quality cost.
        ///
        /// Note: the gain fields stretch the local yaw/pitch angles before
        /// the offset is added. A gain of 1.0 (default) is a no-op.
        /// </summary>
        public Quaternion BuildCombinedCorrectionQuaternion()
        {
            // For v1.0 we apply yaw and pitch offsets only; gain is captured
            // in the schema but the linear scaling of yaw/pitch needs the
            // current gaze direction to be applied per-frame (it isn't a
            // simple constant pre-rotation). The pipeline expands gain in
            // ActiveProfile.ApplyCombinedCorrection rather than baking it
            // into a static quaternion. Here we precompute the offset rotation.
            return Quaternion.Euler(combinedGaze.gazePitchOffsetDeg, combinedGaze.gazeYawOffsetDeg, 0f);
        }

        private static void ValidateSchemaVersion(EyeTrackingProfile profile, string path)
        {
            if (string.IsNullOrEmpty(profile.schemaVersion))
            {
                throw new EyeTrackingProfileException($"Profile {path} has no schemaVersion. File may be corrupt or hand-edited.");
            }

            string[] parts = profile.schemaVersion.Split('.');
            if (parts.Length < 1 || !int.TryParse(parts[0], out int major))
            {
                throw new EyeTrackingProfileException($"Profile {path} has malformed schemaVersion: '{profile.schemaVersion}'");
            }

            string[] currentParts = CurrentSchemaVersion.Split('.');
            int currentMajor = int.Parse(currentParts[0]);
            if (major != currentMajor)
            {
                throw new EyeTrackingProfileException(
                    $"Profile {path} schemaVersion {profile.schemaVersion} is incompatible with this version " +
                    $"({CurrentSchemaVersion}). Migrate the profile or re-run calibration.");
            }
        }
    }

    /// <summary>
    /// Identifying / traceability metadata for a profile.
    /// </summary>
    [Serializable]
    public class ProfileMetadata
    {
        public string profileName = "";
        public string participantID = "";
        /// <summary>ISO-8601 UTC timestamp.</summary>
        public string createdAt = "";
        public string deviceModel = "";
        /// <summary>Status of the headset's own eye-calibration step prior to fitting this profile.</summary>
        public ViveDeviceCalibrationStatus viveDeviceCalibrationStatus = ViveDeviceCalibrationStatus.Unknown;
        public string eyeleanAppVersion = "";
    }

    /// <summary>
    /// Software correction applied to a gaze direction. Default values
    /// (offsets = 0, gains = 1) are a no-op, so a freshly-constructed
    /// profile passes gaze through unchanged.
    /// </summary>
    [Serializable]
    public class GazeCorrection
    {
        public float gazeYawOffsetDeg = 0f;
        public float gazePitchOffsetDeg = 0f;
        public float gazeYawGain = 1f;
        public float gazePitchGain = 1f;
    }

    /// <summary>
    /// State of the headset's own eye-calibration step at the time this
    /// profile was fit. Helps researchers spot mis-calibrated profiles.
    /// </summary>
    public enum ViveDeviceCalibrationStatus
    {
        Unknown = 0,
        Skipped = 1,
        CompletedThisSession = 2,
        CompletedPreviously = 3,
    }

    /// <summary>Exception raised by EyeTrackingProfile loader on schema/version errors.</summary>
    public class EyeTrackingProfileException : Exception
    {
        public EyeTrackingProfileException(string message) : base(message) { }
        public EyeTrackingProfileException(string message, Exception inner) : base(message, inner) { }
    }
}
