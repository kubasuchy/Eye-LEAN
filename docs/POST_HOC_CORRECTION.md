# Post-hoc Eye-Tracking Profile Correction

The Unity calibrator's `EyeTrackingProfile` JSON captures a per-user yaw/pitch
correction that is applied **live** to the gaze stream when the profile is
active. `eyelean_analysis.calibration.posthoc_correction` re-applies that same
correction to a CSV after the fact, so a recording made without the profile
loaded can still be analyzed in the same coordinate frame as a corrected one.

## Prerequisites

- `eyelean_analysis` installed (`pip install -e "./eyelean_analysis[all]"`).
- A recorded Eye_lean CSV.
- An `EyeTrackingProfile` JSON to apply.

## When to use it

- **Re-process old recordings** once a better profile fit becomes
  available. Often a researcher's first calibrator run is with a
  poorly-seated headset; the second run produces a better profile,
  and you want the better correction on the first session's data too.
- **Compare a recording under different profiles.** Apply each profile
  to a copy of the same CSV, run your analysis pipeline on each, and
  compare the metrics directly.
- **Apply a fit retroactively** to data captured before the calibrator
  was standard practice in your lab.

## When NOT to use it

- If the recording has `# Profile: <name>` in its metadata header (V8+,
  shipped 2026-04-30), the live calibrator already applied that profile.
  Applying a *second* correction on top will compound the offset, not
  override it. Check the header first via `read_csv_metadata(csv)["Profile"]`.
- If `# CoordinateOriginSet: True`, the `*Origin_*` and `HeadPos_*`
  columns are stored as offsets from a trial-start origin. The corrector
  doesn't touch those columns (it only modifies direction vectors), so
  it's safe — but the de-normalization for analysis still has to happen
  separately.

## Quick example

```python
from eyelean_analysis import (
    load_profile,
    apply_profile_to_csv,
    read_csv_metadata,
)

# Inspect the recording's header first.
meta = read_csv_metadata("Logs/EyeTracking_20260430_111621.csv")
print(meta.get("Profile", "none"))   # "none" → safe to apply post-hoc

stats = apply_profile_to_csv(
    profile_path="Logs/HTC VIVE Focus Vision_20260430_111559.json",
    csv_in="Logs/EyeTracking_20260430_111621.csv",
    csv_out="Logs/EyeTracking_20260430_111621_corrected.csv",
)
print(stats)
# {'samples_total': 1311,
#  'samples_corrected_combined': 1311,
#  'samples_corrected_left': 0,           # per-eye is identity by default
#  'samples_corrected_right': 0,
#  'samples_skipped_invalid': 0,
#  'profile_name': 'HTC VIVE Focus Vision_20260430_111559'}
```

## What changes in the corrected CSV

- `CombinedDir_X/Y/Z` are rewritten with the corrected world-space gaze
  unit vector. Always rewritten when the combined correction is non-identity.
- `LeftDir_X/Y/Z` and `RightDir_X/Y/Z` are rewritten when
  `apply_per_eye=True` (default) **and** the per-eye corrections are
  non-identity. v1.0 only ships combined fitting, so per-eye are usually
  pass-through.
- A new `# AppliedProfile: <name>` line is prepended to the metadata
  block so downstream consumers can see the corrected file is not a raw
  recording.
- All other columns and metadata lines are byte-equivalent to the input.
- Output respects `overwrite=False` by default: pass `overwrite=True` to
  replace an existing file.

## What changes in your analysis

For most metrics — fixation count, scanpath length, entropy, LHIPA —
nothing changes meaningfully because they're invariant to a constant
yaw/pitch offset. The correction matters when:

- You're computing **angular accuracy against world-space targets** (the
  calibrator's settled-fixation accuracy, the SampleExperiment's visual
  search target, etc.). A 1° pitch bias gets reported as "research-grade
  poor" without correction and "research-grade excellent" after.
- You're computing **vergence depth** from the two eye rays. The rays'
  directions are individually corrected (per-eye fields, when fit), so
  the convergence point shifts.
- You're comparing **multiple participants on the same target**. Each
  participant's profile compensates for their own residual offset; raw
  data conflates participant offset with task effect.

## Profile schema

```json
{
  "schemaVersion": "1.0",
  "metadata": {
    "profileName": "...",
    "participantID": "...",
    "createdAt": "ISO-8601 UTC",
    "deviceModel": "HTC VIVE Focus Vision",
    "viveDeviceCalibrationStatus": 0,
    "eyeleanAppVersion": "1.0.0"
  },
  "combinedGaze": {
    "gazeYawOffsetDeg": -0.262,
    "gazePitchOffsetDeg": -0.398,
    "gazeYawGain": 1.0,
    "gazePitchGain": 1.0
  },
  "leftEye":  { /* same fields, identity in v1.0 */ },
  "rightEye": { /* same fields, identity in v1.0 */ }
}
```

The loader rejects unknown majors (currently expects `1.x`). Adding new
optional fields with default values is non-breaking.

## Math

The Python implementation is a direct port of Unity's
`ActiveProfile.ApplyCombinedCorrection`. For a world-space gaze
direction `d_world` and recorded head rotation quaternion `q_head`:

1. Project to head-local: `d_local = q_head⁻¹ · d_world`
2. Decompose using Unity's convention:
   `yaw = atan2(d_local.x, d_local.z)`,
   `pitch = -asin(d_local.y)`
3. Apply gain then offset:
   `yaw' = yaw · yaw_gain + yaw_offset`,
   `pitch' = pitch · pitch_gain + pitch_offset`
4. Recompose: `d_local' = (sin(yaw')·cos(pitch'), sin(-pitch'), cos(yaw')·cos(pitch'))`
5. Project back to world: `d_world' = q_head · d_local'`

Quaternions are read from the CSV in `(x, y, z, w)` order, matching
both Unity's serialization and `scipy.spatial.transform.Rotation.from_quat`.

## Edge cases handled

- **Non-finite quaternions** (NaN / Inf / zero magnitude): the row is
  skipped, gaze direction left unchanged, count reported in
  `samples_skipped_invalid`. Common causes: tracking loss at the start
  or end of a session.
- **Identity correction** (offset=0, gain=1): fast path returns the
  input array unchanged — no scipy round-trip, no float drift.
- **Per-eye fields absent from the recording** (left-only or right-only
  data): the corresponding column set is silently skipped.

## Reproducibility

`apply_profile_to_csv` is fully deterministic. The output is
byte-equivalent for the same `(profile_path, csv_in)` pair across runs.
Float comparisons against the live Unity pipeline match within IEEE-754
single-precision rounding error.

The 11 unit tests in `eyelean_analysis/tests/test_posthoc_correction.py`
cover yaw-only, pitch-only, gain stretching, correction under a yawed
head, schema-version rejection, real-profile parsing, the CSV-rewrite
end-to-end path, the `overwrite` guard, and invalid-quaternion handling.
