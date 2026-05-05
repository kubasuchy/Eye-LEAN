using System;
using UnityEngine;

namespace EyeTracking.Configuration
{
    /// <summary>
    /// Global accessor for the currently-active <see cref="EyeTrackingProfile"/>.
    /// The eye-tracking pipeline reads from this in its hot path; the
    /// calibrator and downstream applications write to it via Apply / Clear.
    ///
    /// When no profile is active, <see cref="CombinedGazeCorrection"/>
    /// returns identity, so the pipeline can pre-multiply unconditionally
    /// and the no-profile case is byte-equivalent to the prior behavior.
    /// </summary>
    public static class ActiveProfile
    {
        private static EyeTrackingProfile _current;
        private static Quaternion _cachedCombinedCorrection = Quaternion.identity;

        /// <summary>The currently-applied profile, or null when none is set.</summary>
        public static EyeTrackingProfile Current => _current;

        /// <summary>
        /// Pre-built quaternion that rotates a head-local gaze direction by
        /// the profile's combined yaw/pitch offset. Identity when no profile
        /// is active. The eye-tracking pipeline pulls this once per frame
        /// rather than reconstructing it from raw fields, so the hot path
        /// stays branch-free.
        /// </summary>
        public static Quaternion CombinedGazeCorrection => _cachedCombinedCorrection;

        /// <summary>Fires when the active profile is replaced (or cleared).</summary>
        public static event Action<EyeTrackingProfile> OnChanged;

        /// <summary>
        /// Activate a profile. Subsequent gaze samples flowing through
        /// OpenXREyeTracker will have its corrections applied. Pass null
        /// to clear (equivalent to <see cref="Clear"/>).
        /// </summary>
        public static void Apply(EyeTrackingProfile profile)
        {
            _current = profile;
            _cachedCombinedCorrection = profile != null
                ? profile.BuildCombinedCorrectionQuaternion()
                : Quaternion.identity;
            OnChanged?.Invoke(profile);

            if (profile != null)
            {
                Debug.Log($"[ActiveProfile] Applied profile '{profile.metadata.profileName}': " +
                          $"yawOffset={profile.combinedGaze.gazeYawOffsetDeg:F2}°, " +
                          $"pitchOffset={profile.combinedGaze.gazePitchOffsetDeg:F2}°");
            }
            else
            {
                Debug.Log("[ActiveProfile] Profile cleared (gaze pass-through restored)");
            }
        }

        /// <summary>
        /// Clear the active profile. Pipeline reverts to identity correction.
        /// </summary>
        public static void Clear()
        {
            Apply(null);
        }

        /// <summary>
        /// Apply the active profile's combined-gaze correction to a
        /// head-local gaze direction. When no profile is active OR when
        /// gain == 1 and offset == 0, this is mathematically the identity
        /// (per-eye and combined offsets converge on no-op defaults).
        ///
        /// Gain (yaw and pitch separately) is applied multiplicatively to
        /// the input direction's local yaw/pitch BEFORE the offset rotation,
        /// because gain stretches the eye's reported rotation (it's a
        /// per-direction scalar, not a constant pre-rotation).
        /// </summary>
        public static Vector3 ApplyCombinedCorrection(Vector3 headLocalDirection)
        {
            if (_current == null) return headLocalDirection;

            var c = _current.combinedGaze;
            // Fast path: if everything is at defaults, return input unchanged.
            if (Mathf.Approximately(c.gazeYawOffsetDeg, 0f)
                && Mathf.Approximately(c.gazePitchOffsetDeg, 0f)
                && Mathf.Approximately(c.gazeYawGain, 1f)
                && Mathf.Approximately(c.gazePitchGain, 1f))
            {
                return headLocalDirection;
            }

            // Decompose to local yaw/pitch, apply gain, then offset rotation.
            float localYaw = Mathf.Atan2(headLocalDirection.x, headLocalDirection.z) * Mathf.Rad2Deg;
            float localPitch = -Mathf.Asin(Mathf.Clamp(headLocalDirection.y, -1f, 1f)) * Mathf.Rad2Deg;

            localYaw *= c.gazeYawGain;
            localPitch *= c.gazePitchGain;
            localYaw += c.gazeYawOffsetDeg;
            localPitch += c.gazePitchOffsetDeg;

            // Recompose as a unit direction in head-local space.
            float yawRad = localYaw * Mathf.Deg2Rad;
            float pitchRad = -localPitch * Mathf.Deg2Rad;
            float cosP = Mathf.Cos(pitchRad);
            return new Vector3(
                Mathf.Sin(yawRad) * cosP,
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * cosP
            ).normalized;
        }

        /// <summary>
        /// Apply the per-eye correction (left or right) to a head-local
        /// per-eye direction. Same defaulting and math pattern as
        /// <see cref="ApplyCombinedCorrection"/>, just sourcing the
        /// correction from <see cref="EyeTrackingProfile.leftEye"/> or
        /// <see cref="EyeTrackingProfile.rightEye"/>.
        /// </summary>
        public static Vector3 ApplyPerEyeCorrection(Vector3 headLocalDirection, bool isLeftEye)
        {
            if (_current == null) return headLocalDirection;

            var c = isLeftEye ? _current.leftEye : _current.rightEye;
            if (Mathf.Approximately(c.gazeYawOffsetDeg, 0f)
                && Mathf.Approximately(c.gazePitchOffsetDeg, 0f)
                && Mathf.Approximately(c.gazeYawGain, 1f)
                && Mathf.Approximately(c.gazePitchGain, 1f))
            {
                return headLocalDirection;
            }

            float localYaw = Mathf.Atan2(headLocalDirection.x, headLocalDirection.z) * Mathf.Rad2Deg;
            float localPitch = -Mathf.Asin(Mathf.Clamp(headLocalDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
            localYaw *= c.gazeYawGain;
            localPitch *= c.gazePitchGain;
            localYaw += c.gazeYawOffsetDeg;
            localPitch += c.gazePitchOffsetDeg;
            float yawRad = localYaw * Mathf.Deg2Rad;
            float pitchRad = -localPitch * Mathf.Deg2Rad;
            float cosP = Mathf.Cos(pitchRad);
            return new Vector3(
                Mathf.Sin(yawRad) * cosP,
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * cosP
            ).normalized;
        }
    }
}
