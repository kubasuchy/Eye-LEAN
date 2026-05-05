# Deterministic Replay

## What it is

Eye_lean's replay system **re-runs the live experiment code** against
recorded inputs (HMD pose, eye gaze, `Random.state`, config
snapshots). The same scripts that drove the original session drive
the replay. This means:

- Your phase coroutines run again — no separate replay-side
  re-implementation.
- `ExperimentUI.ShowInstruction("Look at the X")` fires at the same
  frame as it did originally.
- Spawn / despawn happen at the same frames because randomness was
  seeded from the recorded `RandomStateSnapshot`.
- Eye gaze is read by a `ReplayingEyeTracker` that pulls from the
  recorded CSV instead of hardware; everything downstream
  (vergence, gaze-target dispatch, RIPA monitor) sees recorded data
  and behaves identically.

Replay is **editor-only**. The replay scene exists on disk but is
not in the APK build — there's no value in running replay on the
headset. Researchers run replay in the Unity editor against a
recorded CSV to spot-check trials, debug failures, or render
visualizations.

## Audience

Researchers replaying a recorded session in the editor.

## Prerequisites

- The Eye_lean Unity project open in the editor.
- A recorded CSV plus its `_scenestate.csv` and `_sceneevents.csv`
  sidecars.

## When you'd use it

- A participant produced an unusual recording — you want to see
  exactly what they saw, frame-accurate.
- You're debugging a bug that only manifests with a specific
  participant's gaze pattern — replay is the deterministic harness.
- You want to render a video of a session for a paper / talk.

## How to use it

1. Open Unity.
2. Open `Assets/Scenes/ReplayScene.unity`.
3. Drop a recorded `EyeTracking_<timestamp>.csv` into the
   `ReplayController` field, or browse via the Inspector picker.
4. Press Play. The scene reloads the original experiment scene
   underneath, restores `Random.InitState`, and re-runs the live
   experiment against the recorded inputs.
5. Use the on-screen scrub bar to seek; speed slider to adjust
   playback rate.

### Verify

The Game view renders the original scene with the recorded gaze ray
moving frame-for-frame against the source recording, and the Console
shows `[ReplayController] Loaded <csv-path>` with no errors.

## API reference (for extending replay-side handlers)

File: `Assets/Scripts/Replay/SceneState/SceneEventReplayer.cs`

| Static method | Purpose |
|---|---|
| `RegisterHandler(eventType, Action<EventRow>)` | Subscribe a delegate that fires whenever the named event row is reached during replay. Use for diagnostics that the live experiment doesn't naturally re-issue. |
| `UnregisterHandler(eventType, delegate)` | Mirror unsub. |
| `DecodeJson<T>(EventRow)` | Decode a `RecordJson`-encoded payload back to a typed struct. |

File: `Assets/Scripts/Replay/Core/ReplayingEyeTracker.cs`
- `IEyeTracker` implementation that returns recorded gaze data.
  Installed on the factory at replay-time via
  `EyeTrackerFactory.SetReplayOverride`.

File: `Assets/Scripts/Replay/Core/ReplayGazeRaycaster.cs`
- Drives `GazeTarget.IsBeingGazedAt` per-frame from recorded gaze.
  Auto-bootstrapped via `[RuntimeInitializeOnLoadMethod]`.

## How it integrates with the rest of the toolkit

- **`ReplayMode.IsActive`** is the global flag. `SessionRecorder`,
  `SceneStateRecorder`, `SceneEventRecorder`, `RIPAMonitorBootstrap`
  all check it in `Start` and disable recording when true (so live
  output doesn't overwrite the recording you're replaying).
- **`HmdPoseDriverBootstrap`** does NOT attach a TrackedPoseDriver
  during replay — the replay system writes Camera.main pose directly
  from the recorded HMD column each frame.
- **`SampleExperimentController` / `Skeleton.TrialManager`** are
  ALIVE during replay. Their `Random.InitState` seeds are restored
  from the recorded `RandomStateSnapshot` / `RandomSeed` event so
  block / trial randomization reproduces.
- **`ExperimentUI` auto-records** every show / hide call. On
  replay, the live UI methods fire at the same frame because the
  experiment re-runs deterministically.

## Common patterns + gotchas

- **Determinism is your contract.** Replay reproduces accurately
  iff your experiment is deterministic w.r.t. recorded inputs:
  - Use only `UnityEngine.Random` (not `System.Random`).
  - Use `WaitForSeconds` / `WaitForEndOfFrame` in coroutines (not
    wall-clock `DateTime.Now`).
  - Tag every runtime spawn with `MarkRecordableSeeded` (stable
    seed).
  - No network, file I/O, multi-threading in gameplay logic.
- **No HMD regression of replay.** Replay is verified in editor
  against a recorded CSV. Don't propose APK regression of replay
  workflows; it doesn't make sense.
- **Drift is normal.** A 134.7 s recording typically replays in
  124 – 130 s in editor (~5–7 % faster) because the editor renders
  at its native rate rather than the headset's 90 Hz cap. Trial
  ORDER and frame-relative behavior are preserved; absolute clock
  isn't.
- **Placeholder spawns.** If a runtime spawn's prefab isn't in the
  active scene at replay time (e.g. researcher pruned the asset
  bundle), the scene-state replayer drops a colored placeholder
  cube at the recorded transform. The visualizer keeps working.

## References

- Source:
  - `Assets/Scripts/Replay/Core/ReplayController.cs`
  - `Assets/Scripts/Replay/Core/ReplayingEyeTracker.cs`
  - `Assets/Scripts/Replay/Core/ReplayGazeRaycaster.cs`
  - `Assets/Scripts/Replay/SceneState/SceneStateReplayer.cs`
  - `Assets/Scripts/Replay/SceneState/SceneEventReplayer.cs`
  - `Assets/Scripts/EyeTracking/Core/ReplayMode.cs`
- Tests: `Assets/Editor/Tests/ReplayModeTests.cs`,
  `SceneEventCSVRoundTripTests.cs`, `SidecarPathDerivationTests.cs`.
- Doc: `Eye_lean_Unity_Project/Eye_lean/docs/REPLAY_SYSTEM.md`,
  `REPLAY_SCENE_STATE.md`.
- Memory: `feedback_replay_editor_only.md`.
