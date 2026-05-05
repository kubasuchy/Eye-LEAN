# HMDDataCollector

## What it is

`HMDDataCollector` is the per-frame head-pose snapshotter and the
keeper of the **coordinate origin** that normalizes every position
column in the recorded CSV. It runs `Camera.main` resolution, queries
position / rotation each `LateUpdate`, computes FPS, and exposes
`SetCoordinateOrigin()` for the environment generator (or your own
code) to pin a trial-start reference frame.

## Audience

Researchers reading the head-pose columns of an Eye_lean CSV.

## Prerequisites

- An Eye_lean scene with `HMDDataCollector` and `SessionRecorder`
  bootstrapped (automatic in shipped scenes).
- A recorded CSV to inspect.

## When you'd use it

- You want head pose included in the CSV (it's automatic once an
  `HMDDataCollector` is in the scene).
- You want recorded positions to be relative to the trial-start
  position rather than the absolute world. This makes
  cross-participant comparisons honest even if the user spawns at a
  different absolute coordinate each session.
- You want to read live FPS or `IsCameraReady` from your experiment
  controller.

## How to use it

```csharp
using EyeTracking.Components;

var hmd = FindFirstObjectByType<HMDDataCollector>();

// Pin the trial-start origin to wherever the camera is right now.
// All subsequent HeadPos_X/Y/Z and *Origin_X/Y/Z columns become
// (world - origin) so a researcher can compare across sessions.
bool ok = hmd.SetCoordinateOrigin();

// Or pin to a specific world position.
hmd.SetCoordinateOrigin(new Vector3(0, 1.6f, 0));

// Read snapshots manually if you need them.
HmdFrameSample s = hmd.SampleSnapshot();
```

## API reference

File: `Assets/Scripts/EyeTracking/Components/HMDDataCollector.cs`

| Method / property | Line | Purpose |
|---|---|---|
| `SetCoordinateOrigin()` | 137 | Pin origin to current `Camera.main.position`. Returns false if camera unavailable. |
| `SetCoordinateOrigin(Vector3)` | 152 | Pin origin to explicit world position. |
| `OnCoordinateOriginSet` (event) | 47 | Fires when origin lands; SceneStateRecorder hooks this to flush its grace-window buffer. |
| `HasTrialStartPosition` (prop) | — | True after the first SetCoordinateOrigin call. |
| `CurrentTrialStartPosition` (prop) | — | The pinned origin. |
| `SampleSnapshot()` | — | Per-frame `HmdFrameSample` consumed by `SessionRecorder`. |

## How it integrates with the rest of the toolkit

- **`SessionRecorder`** auto-finds an `HMDDataCollector` sibling in
  `Awake` and calls `SampleSnapshot()` each `LateUpdate`. The header
  block's `# CoordinateOrigin: x,y,z` line is sourced from
  `CurrentTrialStartPosition`.
- **`EnvironmentGenerator`** (in SampleExperiment) and the calibrator
  call `SetCoordinateOrigin()` once after the user is positioned so
  every recorded sample is normalized to the trial-start frame.
- **Coord-origin grace window:** `SessionRecorder` defers header
  write up to 2 s waiting for `SetCoordinateOrigin` to land. If the
  call comes after the grace window, samples buffered before will
  still be re-normalized at flush time so every CSV row matches the
  header's `CoordinateOriginSet:True` claim.
- **Replay** doesn't need to call `SetCoordinateOrigin` — replay
  reads the recorded `# CoordinateOrigin` line and de-normalizes
  positions as it applies them.

## Common patterns + gotchas

- **`SetCoordinateOrigin()` is idempotent within a session.** Calling
  it twice doesn't break anything but will overwrite the pinned
  origin; positions written *before* the second call are not
  re-normalized after.
- **Camera.main resolution is sticky but defensive.** The collector
  re-resolves `Camera.main` on Unity-null cache (e.g. after a scene
  transition that nulled the previous camera) so `SetCoordinateOrigin`
  doesn't silently no-op.
- **Per-scene scope.** `HMDDataCollector` is NOT
  `DontDestroyOnLoad` — round-9 / 2026-05-02 showed the monolith
  holding stale `cameraTransform` refs across scene loads caused 2493
  NREs. Each scene has its own collector.
- **`SetCoordinateOrigin()` returns bool.** Check it. A `false`
  return means `Camera.main` isn't ready; either retry next frame or
  use `VRReadinessService.WaitForCameraReady` to block until it is.

## References

- Source: `Assets/Scripts/EyeTracking/Components/HMDDataCollector.cs`
- Tests: `Assets/Editor/Tests/HMDDataCollectorTests.cs` (5 cases
  including SetCoordinateOrigin no-op regression).
- Memory: `feedback_csv_position_normalization.md` — the analyst-side
  reminder that CSV positions are normalized.
