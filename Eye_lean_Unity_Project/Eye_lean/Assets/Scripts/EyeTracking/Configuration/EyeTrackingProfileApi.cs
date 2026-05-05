using System.IO;
using UnityEngine;

namespace EyeTracking.Configuration
{
    /// <summary>
    /// Convenience facade for loading, saving, and applying eye-tracking
    /// profiles. Downstream Unity projects can import the Eye_lean toolkit
    /// and add a single line to their Start() to apply a calibration
    /// profile saved by the calibrator.
    ///
    /// Example downstream usage:
    /// <code>
    ///     EyeTracking.Configuration.EyeTrackingProfileApi.LoadAndApply("ParticipantA");
    /// </code>
    /// or zero-config (loads "_default.json" if present):
    /// <code>
    ///     EyeTracking.Configuration.EyeTrackingProfileApi.LoadAndApplyDefault();
    /// </code>
    /// </summary>
    public static class EyeTrackingProfileApi
    {
        /// <summary>
        /// Load the named profile and set it as the active correction. The
        /// argument may be a bare profile name (resolved against
        /// <see cref="EyeTrackingProfile.DefaultDirectory"/>) or an absolute
        /// path. Returns the loaded profile so the caller can inspect it.
        /// </summary>
        public static EyeTrackingProfile LoadAndApply(string profileNameOrPath)
        {
            EyeTrackingProfile profile = EyeTrackingProfile.Load(profileNameOrPath);
            ActiveProfile.Apply(profile);
            return profile;
        }

        /// <summary>
        /// Load and apply the default profile (<c>_default.json</c> in the
        /// default directory) if it exists. Returns null if no default
        /// profile is available — callers should treat that as a "no
        /// calibration applied" situation rather than an error.
        /// </summary>
        public static EyeTrackingProfile LoadAndApplyDefault()
        {
            string path = Path.Combine(
                EyeTrackingProfile.DefaultDirectory,
                EyeTrackingProfile.DefaultProfileFileName);
            if (!File.Exists(path))
            {
                Debug.Log($"[EyeTrackingProfileApi] No default profile at {path}; pipeline runs without correction.");
                return null;
            }
            return LoadAndApply(path);
        }

        /// <summary>
        /// Persist the current active profile under the given name. If
        /// <paramref name="makeDefault"/> is true the file is also copied
        /// to <c>_default.json</c> so it auto-loads on subsequent runs.
        /// Returns the path that was written.
        /// </summary>
        public static string SaveActiveAs(string profileName, bool makeDefault = false)
        {
            if (ActiveProfile.Current == null)
            {
                throw new System.InvalidOperationException(
                    "Cannot save: no profile is currently active. Apply a profile first.");
            }

            string path = EyeTrackingProfile.DefaultPathFor(profileName);
            EyeTrackingProfile.Save(ActiveProfile.Current, path);
            Debug.Log($"[EyeTrackingProfileApi] Saved profile to {path}");

            if (makeDefault)
            {
                string defaultPath = Path.Combine(
                    EyeTrackingProfile.DefaultDirectory,
                    EyeTrackingProfile.DefaultProfileFileName);
                File.Copy(path, defaultPath, overwrite: true);
                Debug.Log($"[EyeTrackingProfileApi] Also wrote default profile: {defaultPath}");
            }

            return path;
        }

        /// <summary>Clear the active profile (pipeline reverts to no correction).</summary>
        public static void ClearActive()
        {
            ActiveProfile.Clear();
        }
    }
}
