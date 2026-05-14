# SampleExperiment — the worked example

## What it is

`SampleExperiment.unity` (build-index 2) is the experiment shipped
in the Eye_lean APK. The MainMenu's NEXT button launches into it.
Every Eye_lean integration point — recording, replay, RIPA, custom
metadata, scene state, event sidecars, profile correction — is
exercised end-to-end in this scene, so reading its source is a fast
way to learn the toolkit's conventions. The
[Skeleton template](SKELETON.md) is a separate developer-side
scaffold for researchers building their own experiment from scratch
and is not part of the APK build flow.

## Audience

Developers reading the `SampleExperimentController` to copy patterns
into a custom experiment.

## Prerequisites

- Unity 6000.3.9f1 with VIVE OpenXR 2.5.1 (for editor playback).
- Familiarity with Unity coroutines and `MonoBehaviour` lifecycle.
- Read [SESSION_RECORDER.md](SESSION_RECORDER.md),
  [SCENE_STATE.md](SCENE_STATE.md), and
  [SCENE_EVENTS.md](SCENE_EVENTS.md) first if you have not already.

---

## The four phases

1. **FreeExploration** — 30 s of unconstrained roaming in the
   procedurally generated room.
2. **VisualSearch** — 3 trials, find a red sphere among distractors.
3. **CountingTask** — 10 s, count red cubes; optional answer
   collection via a world-space option panel.
4. **ChangeDetection** — 3 trials, detect a scene-state change
   between two viewings.

---

## How to run it

From a built APK:

1. Calibrate (CalibrationScene at build-index 1).
2. From MainMenu, dwell NEXT.
3. Follow the on-screen prompts.
4. The CSV writes to
   `Application.persistentDataPath/EyeTracking_<timestamp>.csv` plus
   the two sidecar files (`_state.csv`, `_events.csv`).

In the editor:

1. Open `SampleExperiment.unity`.
2. Press Play. Same flow runs without an HMD (head-pointing fallback
   for navigation; spacebar for participant inputs).

To inspect the recorded session in Python:

```python
import eyelean_analysis as ela
ctx = ela.notebook_context()  # auto-finds the most recent CSV
print(ctx.data.get_phase_summary())
```

### Verify

In `adb logcat -s Unity` after the participant finishes the four
phases, look for:

```
[SampleExperimentController] Experiment complete.
```

The CSV's `Phase` column should contain rows tagged
`exploration`, `visual_search`, `counting`, and
`change_detection` in that order.

---

## How to read the controller

`SampleExperimentController.cs` orchestrates the phase sequence as a
single coroutine. The relevant entry points are:

| Member | Line | Role |
|---|---|---|
| `class SampleExperimentController` | `:16` | Top-level orchestrator. |
| `event Action<ExperimentPhase> OnPhaseChanged` | `:63` | Fires whenever the current phase advances. `ExperimentUI` subscribes for the corner phase indicator. |
| `DeclareAllMetadataFields()` | `:436` | Declares 20+ custom CSV columns (Block, Stimulus, ResponseTime, etc.) up front in `Awake` so the recorder header is final before the first row writes. |
| `RunExperimentSequence()` | `:575` | The phase coroutine. Each phase block calls `RunPhase(ExperimentPhase.<Name>, …)`. |
| `RunFreeExploration()` | `:1039` | Inline free-exploration handler. |

File: `Assets/Scripts/Experiment/SampleExperimentController.cs`.

---

## How phases register their handlers

The controller does not use the Skeleton's
`IExperimentPhaseHandler` interface; phase logic lives in dedicated
managers attached to the same scene. Each phase block in
`RunExperimentSequence` (`:575`) wraps its work with a `RunPhase`
call that fires `OnPhaseChanged`, then drives the phase manager via
its public coroutine.

| Phase | Manager (file) | Notes |
|---|---|---|
| FreeExploration | `SampleExperimentController.cs:1039` (`RunFreeExploration`) | Inline timer with inactivity prompt. |
| VisualSearch | `Assets/Scripts/Experiment/Tasks/VisualSearchManager.cs` | Per-trial spawn + target selection + result reporting. |
| CountingTask | `Assets/Scripts/Experiment/Tasks/CountingTaskManager.cs` | Object spawn + answer collection via `CountingAnswerUI`. |
| ChangeDetection | `Assets/Scripts/Experiment/Tasks/ChangeDetectionManager.cs` | Two-presentation change-detection logic. |
| UI | `Assets/Scripts/Experiment/UI/ExperimentUI.cs` | Instructions, progress, phase indicator, RIPA HUD. |
| Environment | `Assets/Scripts/Experiment/Environment/EnvironmentGenerator.cs` | Procedural room + props + `SetCoordinateOrigin`. |

To copy this pattern into a custom experiment: implement a
phase-manager component per phase, expose a coroutine the
controller can drive, and call `OnPhaseChanged` (or the equivalent
in your own controller) so `ExperimentUI` can react.

---

## How it integrates with the rest of the toolkit

- **Recording layer.** `EyeTracker`, `HMDDataCollector`, and
  `SessionRecorder` are attached in the scene. `RIPAMonitor` and
  `RIPACSVColumn` auto-bootstrap. No researcher action needed.
- **Custom metadata.** `DeclareAllMetadataFields`
  (`SampleExperimentController.cs:436`) declares all phase-specific
  columns in `Awake` so the CSV header is final before the first
  row writes.
- **Replay.** Each phase manager spawns objects via
  `MarkRecordableSeeded($"Target_{trial}_{slot}")` so deterministic
  replay reproduces the same scene each time. `Config*` snapshots
  are written into the events sidecar via `RecordJson` at session
  start, so post-hoc Inspector tweaks do not corrupt replay of
  older recordings.
- **Cognitive-load monitor.** `ExperimentUI` binds the top-LEFT
  HUD bar to `RIPAMonitor.Instance.OnLoadChanged`. The displayed
  detector is selected by `ExperimentUI.hudMethod` (RIPA2 by default;
  Butterworth / FFT / DWT also available since v1.0.1 — see
  [RIPA_MONITOR.md](RIPA_MONITOR.md)).
- **Skeleton comparison.** Each task manager is, in effect, an
  inline `IExperimentPhaseHandler` implementation. If you prefer
  the Skeleton's interface-driven shape, those files are reference
  material for what a fleshed-out handler looks like. See
  [SKELETON_HANDLER.md](SKELETON_HANDLER.md).

---

## Common patterns and gotchas

- **Phase enum kept for backward compatibility.** `CalibrationCheck`
  and `SmoothPursuit` still exist in `ExperimentPhase` but no
  longer fire at runtime (RC2 trim). Do not add new code that
  depends on them firing.
- **Per-scene EyeTracker setting drift.** SampleExperiment and
  CalibrationScene have independent serialized `EyeTracker` fields.
  Unify them field by field, or the calibrator and experiment will
  *look* different even though they share the same script.
- **Spawn yaw is head-tilt independent.**
  `cameraTransform.eulerAngles.y` gimbal-flips for tilted heads;
  use `atan2(forward.x, forward.z)` on the XZ-projected forward
  vector. Both task managers do this — copy the pattern.

---

## References

- Source: `Assets/Scripts/Experiment/SampleExperimentController.cs`
  and the task managers listed above.
- Schema:
  [DATA_SCHEMA.md](../Eye_lean_Unity_Project/Eye_lean/docs/DATA_SCHEMA.md).
- Notebook:
  `eyelean_analysis/notebooks/examples/06_sample_experiment.ipynb`.
