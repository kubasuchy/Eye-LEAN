# Calibration scene + EyeTrackingProfile

## What it is

A reference manual for `CalibrationScene.unity` (build-index 1) — a
five-test calibrator that runs every participant through fixation,
saccade, smooth pursuit, tuning (Theil-Sen joint yaw/pitch offset+gain
fit), and verification (re-test after applying the fit). The output
is an `EyeTrackingProfile` JSON saved next to the CSV. The factory
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
`Assets/Scripts/EyeTracking/Calibration/CalibrationSessionManager.cs:561`)
takes the settled fixation samples plus the camera transform and
returns a per-axis `gain * measuredAngle + offset` correction fit by
Theil-Sen median-of-pairwise-slopes regression. Theil-Sen is robust
to ~29% outliers (blinks, mid-window saccades) while remaining as
efficient as least squares on Gaussian data (Sen 1968; Theil 1950).

### Gain estimation gates

Gain is held at 1 (offset-only fit) unless the per-axis data has
enough leverage to identify a slope:

- `n >= 30` settled-and-velocity-gated samples.
- `span(measuredAxis) >= minEccentricityDeg` (default 10°).
- `iqr(measuredAxis) >= minInterquartileEccentricityDeg` (default 4°).
- `|slope - 1| >= minGainDeviation` (default 0.05).

The IQR gate matters: the default 7-target fixation set has the
bulk of samples clustered near one pitch with single-target leverage
points at each extreme. Total span passes by a hair but Theil-Sen's
slope is dominated by ~2 cross-cluster pairs, producing noise-driven
gains far from 1. The IQR check requires the middle 50% of
measurements to span a useful range before the slope is trusted.
(`Assets/Scripts/EyeTracking/Calibration/OffsetEstimator.cs:39`–`66`.)

### Cumulative composition

The fit operates on already-corrected gaze (the prior profile is
applied per-frame before sampling), so it produces *additional*
correction. The saved profile composes the two affine transforms:

```
saved.gain   = prior.gain   * fit.gain
saved.offset = fit.gain     * prior.offset + fit.offset
```

(`CalibrationSessionManager.cs:602`–`608`.) When both gains stay at
1 (the common case), this reduces to additive offset composition.

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
    "gazeYawOffsetDeg":   0.358,
    "gazePitchOffsetDeg": -0.956,
    "gazeYawGain":        1.0,
    "gazePitchGain":      1.0
  },
  "leftEye":  { "...": "identity in v1.0" },
  "rightEye": { "...": "identity in v1.0" }
}
```

Source: `Assets/Scripts/EyeTracking/Configuration/EyeTrackingProfile.cs`.

At application time `[ActiveProfile] Applied profile '<name>':
yawOffset=...°, pitchOffset=...°, yawGain=×..., pitchGain=×...`
prints in logcat so any non-unit gain is visible without opening
the JSON.

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
(`CalibrationSessionManager.cs:805`) compares post-fit fixation
median error to pre-fit fixation median. The new profile is rejected
when post-fit median exceeds pre-fit median by more than
`VerificationMedianRegressionThresholdDeg` (0.30°,
`CalibrationSessionManager.cs:784`). An auto-trust override accepts
the fit when the verification re-test has very few settled samples
relative to the fit's own training residuals, since small-n re-tests
can regress purely by sampling noise.

The earlier accuracy-percentage gate was removed: at typical
verification sample sizes the binomial standard error on a
within-2° pass rate is ~3.8pp, which is wider than the gate's 5pp
margin, so the verdict was flipping on noise.

Otherwise the archived profile is copied over `_default.json` and
auto-loads on next launch.

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
- **A previously-saved profile can become stale.** Eye_lean's
  correction was fit against whatever the HMD's onboard
  eye-calibration was producing at the time the profile was saved.
  If the participant re-runs the headset-level calibration, the
  saved Eye_lean profile may now be correcting a bias that no
  longer exists — measured as a low pre-fit accuracy at session
  start and a verification regression after the new fit. Delete
  (or move aside) `EyeLeanProfiles/_default.json` on the device to
  fall back to identity correction and re-fit from scratch.
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
