"""Post-hoc calibration utilities for Eye_lean CSVs.

The Unity calibrator's `EyeTrackingProfile` JSON file captures per-user
yaw/pitch offsets + gain for the combined and (optionally) per-eye gaze
directions. This package re-applies the same correction to a recorded
CSV after the fact, which lets researchers:

  - Re-process old CSVs once a better profile fit becomes available.
  - Compare the same CSV under different profiles.
  - Apply a profile that wasn't yet active when the recording was made.

The math mirrors `ActiveProfile.ApplyCombinedCorrection` in
`Assets/Scripts/EyeTracking/Configuration/ActiveProfile.cs` so a
post-hoc-corrected CSV is byte-equivalent (modulo float rounding) to a
CSV recorded live with the same profile loaded.
"""

from .posthoc_correction import (
    EyeTrackingProfile,
    GazeCorrection,
    apply_profile_to_csv,
    apply_combined_correction,
    load_profile,
)

__all__ = [
    "EyeTrackingProfile",
    "GazeCorrection",
    "apply_profile_to_csv",
    "apply_combined_correction",
    "load_profile",
]
