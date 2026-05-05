# Calibration scene + EyeTrackingProfile

## What it is

A reference manual for `CalibrationScene.unity` (build-index 1) — a
five-test calibrator that runs every participant through fixation,
saccade, smooth pursuit, tuning (median-residual yaw/pitch fit), and
verification (re-test after applying the fit). The output is an
`EyeTrackingProfile` JSON saved next to the CSV. The factory
auto-loads `_default.json` on the next session start so the
correction is applied transparently to all downstream data.

## Audience

Researchers running the calibrator and reading or applying its output
profile.

## Prerequisites

- Unity 6000.3.9f1 with VIVE OpenXR 2.5.1 (for editor playback).
- Built APK on an HTC VIVE Focus Vision, or an OpenXR-compliant HMD
  with eye tracking, for hardware sessions.
- Hardware-level VIVE eye calibration completed at the OS level
  before launching the APK. The Eye_lean profile is a software-side
  correction on top of that.

---

## How a session runs

1. Launch the APK. The MainMenu scene loads (build-index 0).
2. Dwell START on the MainMenu. CalibrationScene loads.
3. Follow the on-screen prompts. Each test takes 30–60 seconds:
   - **Fixation** — accuracy on stationary targets.
   - **Saccade** — landing accuracy on jumping targets.
   - **Smooth Pursuit** — tracking gain on moving targets.
4. At the **Tuning** prompt, dwell START to save the fitted profile or
   NEXT to skip. Saving runs **Verification**: a brief fixation
   re-test with the new correction applied.
5. The Completion screen renders pre-fit and post-fit metrics
   side-by-side.

### Verify

In `adb logcat -s Unity` look for:

```
[CalibrationSessionManager] Verification passed; promoted profile to default: <path>
```

That line confirms the profile passed verification and was promoted
to `_default.json`. If verification regressed, the warning instead
reads:

```
[CalibrationSessionManager] Post-fit verification regressed: <reason>. Keeping previous default profile; new profile archived at <path>
```

In that case `_default.json` is untouched and the prior profile is
restored in memory.

---

## How the profile is fitted

`OffsetEstimator.FitCombinedOffset` (called from
`CalibrationSessionManager.HandleTuningPhase`,
`Assets/Scripts/EyeTracking/Calibration/CalibrationSessionManager.cs:544`)
takes the settled fixation samples plus the camera transform and
returns yaw/pitch offsets that minimize the median angular residual
between recorded gaze and ground-truth target direction.

The fit is composed cumulatively with the prior profile so each
saved profile means "the total correction the eye tracker needs from
raw gaze":

```
saved.yaw   = prior.yaw   + fitResult.yawOffsetDeg
saved.pitch = prior.pitch + fitResult.pitchOffsetDeg
```

(`CalibrationSessionManager.cs:585`–`590`.) Without this composition,
each save would overwrite the cumulative correction with only the
new residual and sessions would oscillate between over- and
under-correcting.

---

## EyeTrackingProfile JSON shape

```json
{
  "metadata": {
    "profileName": "...",
    "deviceModel": "HTC VIVE Focus Vision",
    "createdAt": "2026-04-29T..."
  },
  "combinedGaze": {
    "gazeYawOffsetDeg": 0.358,
    "gazePitchOffsetDeg": -0.956,
    "gainX": 1.0, "gainY": 1.0
  },
  "perEyeLeft":  { "...": "identity in v1.4" },
  "perEyeRight": { "...": "identity in v1.4" }
}
```

Source: `Assets/Scripts/EyeTracking/Configuration/EyeTrackingProfile.cs`.

---

## Applying a profile to a fresh session

The factory auto-loads `_default.json` if present. To apply a
specific profile manually:

1. Copy the chosen `<deviceModel>_<timestamp>.json` from
   `Application.persistentDataPath/EyeLeanProfiles/` on the device
   (pull via `adb`) to your machine, edit, push back, and rename to
   `_default.json`. Or use the in-app file selector if your scene
   exposes one.
2. Launch the next session. `EyeTrackerFactory.GetEyeTracker()` calls
   `TryAutoLoadDefaultProfile()` on first hardware-tracker selection.
3. Confirm in the CSV header that the active profile matches:

```
# Profile: <profileName>
# ProfileCombinedYawDeg: <yaw>
# ProfileCombinedPitchDeg: <pitch>
```

### Verify

The three `# Profile*` lines appear at the top of every recorded CSV
written by `SessionRecorder`. They identify which profile was active
during recording.

---

## When verification rejects a profile

`EvaluateAndCommitProfile`
(`CalibrationSessionManager.cs:749`) compares post-fit fixation to
pre-fit fixation. The new profile is rejected when either:

- Settled accuracy drops by more than
  `VerificationAccuracyDropThresholdPct` (5 percentage points), or
- Median angular error increases by more than
  `VerificationMedianRegressionThresholdDeg` (0.30°).

Otherwise the archived profile is copied over `_default.json` and
auto-loads on next launch. Rejection thresholds are constants at
`CalibrationSessionManager.cs:741`–`742`.

---

## API reference

| File | Role |
|---|---|
| `Assets/Scripts/EyeTracking/Calibration/CalibrationSessionManager.cs` | Orchestrates the five-test sequence + Tuning + Verification. |
| `Assets/Scripts/EyeTracking/Calibration/CalibrationTestRunner.cs` | Runs a single test (fixation / saccade / pursuit) and returns per-test metrics. |
| `Assets/Scripts/EyeTracking/Calibration/OffsetEstimator.cs` | Median-residual fitter that produces the yaw / pitch correction. |
| `Assets/Scripts/EyeTracking/Configuration/EyeTrackingProfile.cs` | Profile data model + JSON load/save. |
| `Assets/Scripts/EyeTracking/Configuration/ActiveProfile.cs` | Global accessor + cached correction quaternion. |
| `Assets/Scripts/EyeTracking/Configuration/EyeTrackingProfileApi.cs` | Facade for downstream-project use (`SaveActiveAs`, etc.). |

---

## How it integrates with the rest of the toolkit

- **Auto-load.** `EyeTrackerFactory.GetEyeTracker()` calls
  `TryAutoLoadDefaultProfile()` on first hardware-tracker selection.
  Path: `Application.persistentDataPath/EyeLeanProfiles/_default.json`.
- **Live application.** `OpenXREyeTrackerProvider.UpdatePerEyeData`
  applies the per-eye yaw/pitch correction *before* combined gaze is
  computed. Downstream consumers (`EyeTracker.SampleSnapshot`,
  `RIPAMonitor`, gaze-target dispatch) all see post-correction data.
- **CSV header.** `SessionRecorder` writes `# Profile:`,
  `# ProfileCombinedYawDeg:`, and `# ProfileCombinedPitchDeg:` so
  analysis can identify which profile was active.
- **Post-hoc correction.** Re-apply a *different* profile to a
  recorded CSV after the fact via Python — useful when the
  auto-loaded profile turned out to be wrong. See
  [POST_HOC_CORRECTION.md](POST_HOC_CORRECTION.md).

---

## Common patterns and gotchas

- **Hardware vs software calibration are different layers.** HTC's
  VIVE OS does its own per-user eye calibration (the spinning ring
  on session start). Eye_lean's profile is a software-side
  correction *on top* of that. Run both.
- **Settled vs transition samples.** Per-test metrics use only
  *settled* samples (after the target stops moving) for fixation;
  *landing* samples for saccade; *velocity-gain* for pursuit. Custom
  analysis that averages all samples will produce misleading
  numbers.
- **Verification exists for a reason.** Skipping verification leaves
  the Completion screen showing pre-fit numbers, which makes the
  participant think the calibration didn't help.
- **CSV `# CoordinateOrigin` is set in this scene.** The
  calibrator's `EnvironmentGenerator` calls `SetCoordinateOrigin()`
  after the user spawns. Subsequent recordings inherit the same
  convention via the experiment scene's own `EnvironmentGenerator`.

---

## References

- Source files listed in the API reference table above.
- Tests:
  - `Assets/Editor/Tests/EyeTrackingProfileTests.cs`
  - `Assets/Editor/Tests/OffsetEstimatorTests.cs`
- Holmqvist et al., *Eye Tracking* (OUP 2011) — the canonical
  methodology reference cited in `CalibrationSessionManager.cs:14`.
