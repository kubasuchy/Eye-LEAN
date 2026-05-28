# Deterministic Replay

## What it is

Eye_lean's replay system **re-runs the live experiment code** against recorded
inputs (HMD pose, eye gaze, `Random.state`, config snapshots). The same scripts
that drove the original session drive the replay, so:

- Phase coroutines run again — no separate replay-side re-implementation.
- `ExperimentUI.ShowInstruction(...)` fires at the same frame as it did
  originally.
- Spawn / despawn happen at the same frames because randomness was seeded from
  the recorded `RandomStateSnapshot`.
- A `ReplayingEyeTracker` pulls gaze from the CSV instead of hardware;
  everything downstream (vergence, gaze-target dispatch, RIPA monitor) sees
  recorded data and behaves identically.

Replay is **editor-only** and is not a separate scene. You open the same
experiment scene you recorded in, drop the replay components onto a GameObject,
and press Play. There's no value in shipping replay to the headset.

## Prerequisites

- The Eye_lean Unity project open in the editor.
- A recorded CSV plus its `_scenestate.csv` and `_sceneevents.csv` sidecars.

## When you'd use it

- A participant produced an unusual recording and you want to see frame-accurate
  what they saw.
- You're debugging a bug that only manifests with a specific participant's gaze
  pattern — replay is the deterministic harness.
- You want to render a video of a session for a paper or talk.

## How to use it

1. Open the same scene used to record (e.g. `SampleExperiment.unity`).
2. Add a GameObject with `ReplayManager` + `ReplayController`. For the bundled
   demo, also add `DemoReplayBootstrapper` so the procedural room anchors to
   the recording's coordinate origin. (The SampleExperiment scene ships with
   these already; just enable the object.)
3. Set the CSV path on the `ReplayController` (or use the Inspector picker).
4. Press Play. The controller restores `Random.InitState`, installs the
   `ReplayingEyeTracker`, and re-runs the live experiment against recorded
   inputs.
5. Use the on-screen scrub bar to seek; speed slider to adjust playback rate.

### Verify

The Game view renders the original scene with the recorded gaze ray moving
frame-for-frame against the source recording, and the Console shows
`[ReplayController] Loaded <csv-path>` with no errors.

## API reference (for extending replay-side handlers)

File: `Assets/Scripts/Replay/SceneState/SceneEventReplayer.cs`

| Static method | Purpose |
|---|---|
| `RegisterHandler(eventType, Action<EventRow>)` | Subscribe a delegate that fires whenever the named event row is reached during replay. Use for diagnostics that the live experiment doesn't naturally re-issue. |
| `UnregisterHandler(eventType, delegate)` | Mirror unsub. |
| `DecodeJson<T>(EventRow)` | Decode a `RecordJson`-encoded payload back to a typed struct. |

File: `Assets/Scripts/Replay/Core/ReplayingEyeTracker.cs`
- `IEyeTracker` implementation that returns recorded gaze data. Installed on
  the factory at replay-time via `EyeTrackerFactory.SetReplayOverride`.

File: `Assets/Scripts/Replay/Core/ReplayGazeRaycaster.cs`
- Drives `GazeTarget.IsBeingGazedAt` per-frame from recorded gaze.
  Auto-bootstrapped via `[RuntimeInitializeOnLoadMethod]`.

## How it integrates with the rest of the toolkit

- **`ReplayMode.IsActive`** is the global flag. `SessionRecorder`,
  `SceneStateRecorder`, and `SceneEventRecorder` check it in `Start` and
  disable recording when true, so live output doesn't overwrite the recording
  you're replaying. `RIPAMonitorBootstrap` does NOT skip on replay (v1.0.1+):
  the monitor spawns and recomputes its detectors from
  `ReplayingEyeTracker`'s recorded pupil stream, giving the HUD a live readout
  during playback. CSV writes are still suppressed by `SessionRecorder`.
- **`HmdPoseDriverBootstrap`** does NOT attach a TrackedPoseDriver during
  replay — the replay system writes `Camera.main` pose directly from the
  recorded HMD column each frame.
- **`SampleExperimentController` / `Skeleton.TrialManager`** are ALIVE during
  replay. Their `Random.InitState` seeds are restored from the recorded
  `RandomStateSnapshot` / `RandomSeed` event so block / trial randomization
  reproduces.
- **`ExperimentUI` auto-records** every show / hide call. On replay, the live
  UI methods fire at the same frame because the experiment re-runs
  deterministically.

## Common patterns + gotchas

- **Determinism is your contract.** Replay reproduces accurately iff your
  experiment is deterministic w.r.t. recorded inputs:
  - Use only `UnityEngine.Random` (not `System.Random`).
  - Use `WaitForSeconds` / `WaitForEndOfFrame` in coroutines (not wall-clock
    `DateTime.Now`).
  - Tag every runtime spawn with `MarkRecordableSeeded` (stable seed).
  - No network, file I/O, or multi-threading in gameplay logic.
- **Drift is normal.** A 134.7 s recording typically replays in 124–130 s in
  the editor (~5–7 % faster) because the editor renders at its native rate
  rather than the headset's 90 Hz cap. Trial ORDER and frame-relative behavior
  are preserved; absolute clock isn't.
- **Placeholder spawns.** If a runtime spawn's prefab isn't in the active scene
  at replay time (e.g. the asset bundle got pruned), the scene-state replayer
  drops a colored placeholder cube at the recorded transform. The visualizer
  keeps working.

## Universal Replay Contract (for new experiments)

If you're building a new experiment scene on Eye-LEAN and want it to be
replay-compatible out of the box, your controller scripts must follow the
seven rules below. Following them makes replay "just work" — no replay-
specific code paths needed, no special handlers. The proof: SampleExperiment
and the N-back experiment both follow this contract and share the same
universal replay infrastructure with no code paths that say `if (NBack)…`.

The toggle the researcher uses to switch a scene between live recording and
editor replay is **EyeTracker vs ReplayController**:

| Component active | Mode |
|---|---|
| EyeTracker enabled | Live recording — build to HMD |
| ReplayController enabled | Editor replay — deterministic playback |

When `ReplayController` is in the scene and active, `ReplayMode.IsActive`
becomes `true` before any researcher script wakes (the controller has
`[DefaultExecutionOrder(-1000)]`). All seven rules below assume this gating
mechanism.

### Rule 1: Do NOT disable yourself during replay

Your experiment controller must stay **enabled** during replay. Its coroutines
ARE the replay — they reproduce the participant's experience by running the
same code paths against recorded inputs. Never do this:

```csharp
// WRONG — disabling breaks deterministic re-execution
private void Awake() {
    if (ReplayMode.IsActive) { enabled = false; return; }
}
```

### Rule 2: Gate live input, not logic

The participant isn't there to press buttons during replay. Gate input
handlers on `ReplayMode.IsActive`:

```csharp
private void OnRespondPerformed(InputAction.CallbackContext _)
{
    if (EyeLean.Replay.SceneState.ReplayMode.IsActive) return;
    // ...accept response...
}
```

Logic (stimulus presentation, phase transitions, scoring) runs unchanged.

### Rule 3: Gate recording, not execution

Don't skip `SetMetadata`, `SetSessionContext`, or `RecordKV` calls during
replay. `SessionRecorder` already suppresses CSV writes when
`ReplayMode.IsActive` is true. Skipping these calls would create divergent
state between recording and replay paths — exactly what we're trying to
avoid.

### Rule 4: Use EyeTrackerFactory for gaze queries

Never cache a direct reference to a specific `IEyeTracker` implementation.
Always go through:

```csharp
var tracker = EyeTracking.Core.EyeTrackerFactory.GetEyeTracker();
```

During replay, the factory transparently returns `ReplayingEyeTracker` (which
serves recorded samples). This is the single substitution point that makes
gaze-driven gameplay work identically in live and replay modes.

### Rule 5: Use UnityEngine.Random (not System.Random) for stimulus generation

`SceneEventReplayer` restores `UnityEngine.Random.state` from the recorded
snapshot before any phase coroutine fires. `System.Random` instances are NOT
restored — they use their own seed, which is fine for things like block
order shuffling (config-deterministic from a seed in your config asset) but
breaks anything that relies on per-frame randomness.

### Rule 6: Defer world-space UI placement until replay camera is ready

During replay, `Camera.main` starts at the scene-default position and only
jumps to the recorded HMD pose once `ReplayController` advances past the
first few frames. If you place world-space panels before that, they'll
anchor to the wrong location.

The canonical pattern: wait for `ReplayController.IsPlaying` and skip the
first ~30 frames before placing world-space UI:

```csharp
private IEnumerator PlacePanelsThenShow()
{
    if (ReplayMode.IsActive)
    {
        var rc = FindFirstObjectByType<EyeLean.Replay.ReplayController>();
        if (rc != null)
        {
            float timeout = Time.realtimeSinceStartup + 30f;
            while (!rc.IsPlaying && Time.realtimeSinceStartup < timeout)
                yield return null;
            for (int i = 0; i < 30; i++) yield return null;
        }
    }
    else
    {
        var readiness = EyeTracking.Core.VRReadinessService.Instance;
        if (readiness != null) yield return readiness.WaitForCameraReady(8f);
    }
    // ...place panels now...
}
```

### Rule 7: Auto-start your experiment during replay

The participant isn't there to press the start trigger. If your controller
normally waits for input to begin, bypass that wait when replaying:

```csharp
if (ReplayMode.IsActive || autoStart)
    StartExperiment();
else
    ShowIdleMessage();
```

### Verifying contract compliance

Drop a `ReplayController` into your scene, point it at a recorded CSV, and
press Play in the editor. You should see:

- Your panels appearing at the participant's actual recorded head position
- Stimulus/phase coroutines firing at the recorded cadence
- The cognitive-load gauge (if present) updating from recorded pupil data
- The `ReplayUI` control panel responding to play/pause/scrub
- Pressing `Stop` reloads the scene and waits for `Play` — universal across
  all experiments because it's implemented via `SceneManager.LoadScene`

If any of those fail, check the rules above — most issues trace back to one
of them.

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
- Deeper docs: `Eye_lean_Unity_Project/Eye_lean/docs/REPLAY_SYSTEM.md`,
  `REPLAY_SCENE_STATE.md`.
