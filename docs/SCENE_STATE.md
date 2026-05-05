# SceneStateRecorder + Recordable

## What it is

`SceneStateRecorder` writes a **per-frame sidecar CSV** that captures
the position, rotation, and active state of every GameObject tagged
with a `Recordable` component. Together with the main per-frame eye
CSV, this lets the deterministic-replay system reproduce both the
participant's gaze AND the scene around them — even runtime-spawned
stimuli, agents, and props.

`Recordable` is the marker component. Each instance has a stable
serialized GUID. Editor-authored objects auto-generate a GUID on
`Reset` / `OnValidate`. Runtime-spawned objects get a GUID via
`SceneStateRecorder.MarkRecordable` (random per-session) or
`MarkRecordableSeeded(go, seed)` (cross-session-stable, MD5-derived).

## Audience

Developers writing or consuming the SceneState sidecar.

## Prerequisites

- A scene with `SessionRecorder` bootstrapped (Eye_lean scenes do
  this automatically).

## When you'd use it

- You spawn stimuli at runtime and want them reproduced on replay
  (use `MarkRecordableSeeded` with a deterministic seed).
- You move pre-placed scene objects during a trial and want the
  motion preserved (just add `Recordable` in the editor).
- You're authoring a new manager that hides / shows objects per
  trial — `Recordable.SetActive(false)` lands in the sidecar.

## How to use it

```csharp
using EyeLean.SceneState;
using UnityEngine;

// Editor-authored: add the Recordable component in the Inspector.
// GUID is auto-generated on Reset / OnValidate.

// Runtime-spawned, deterministic (recommended):
var go = Instantiate(prefab);
SceneStateRecorder.MarkRecordableSeeded(go, $"MyStim_trial{trial}_slot{slot}");

// Runtime-spawned, non-deterministic:
SceneStateRecorder.MarkRecordable(go);
```

## API reference

File: `Assets/Scripts/EyeTracking/SceneState/SceneStateRecorder.cs`

| Method | Line | Purpose |
|---|---|---|
| `MarkRecordable(GameObject)` | 155 | Add a `Recordable` with a fresh per-session GUID. |
| `MarkRecordableSeeded(GameObject, string seed)` | 185 | Add a `Recordable` with an MD5-derived GUID seeded by `seed`. Same seed → same GUID across sessions, which is what makes deterministic replay reproduce spawns. |

File: `Assets/Scripts/EyeTracking/SceneState/Recordable.cs`

| Method / property | Line | Purpose |
|---|---|---|
| `SetUniqueId(string seed)` | 74 | Set the GUID from a deterministic seed. Must be called BEFORE `OnEnable`. |
| `RegenerateId()` | 55 | Mint a new random GUID (called by registry on collision). |
| `UniqueId` (prop) | — | Stable identifier written to the sidecar. |

Sidecar file format:
```
# Eye_lean Scene State Sidecar
# FileVersion: 1.0
# SessionID: <same as main CSV>
# CoordinateOrigin: x.xxxx,y.xxxx,z.xxxx
# CoordinateOriginSet: True | False
# SampleEveryNthFrame: 1
# Profile: <SceneRecordingProfile name>
Frame,T,ObjectId,Pos_X,Pos_Y,Pos_Z,Rot_X,Rot_Y,Rot_Z,Rot_W,Active[,ParentId]
```

Positions are normalized to the same `# CoordinateOrigin` as the main
CSV (de-normalize on apply during replay).

## How it integrates with the rest of the toolkit

- **Auto-bootstrap.** A `SceneStateRecorder` is auto-attached to the
  `SessionRecorder` GameObject at scene-load time. No researcher
  action needed.
- **Schema-locked in lockstep with the main CSV.** Subscribes to
  `SessionRecorder.OnHeaderWritten` so the sidecar's header lands at
  the same moment as the main CSV's column row.
- **Frame counter shared.** Sidecar rows use the same `Frame` value
  as the main CSV so analysts can join the two on `Frame`.
- **Replay reads it back.** `SceneStateReplayer` (in the replay
  system) reads each row and either (a) finds the same GUID in the
  active scene and writes the recorded transform, or (b) instantiates
  a placeholder if the GUID isn't present.
- **`MarkRecordableSeeded` + `Random.InitState` = deterministic
  spawns.** If your trial loop seeds `Random.InitState` from a known
  value and spawns objects with deterministic seeds, replay
  reproduces every spawn at the same scene-state row.

## Common patterns + gotchas

- **Seed collisions.** Two `MarkRecordableSeeded` calls with the same
  seed produce the same GUID. The registry detects collisions and
  calls `RegenerateId` — but that breaks determinism. Use unique
  per-trial / per-slot seeds (`$"Agent_{trial}_{slot}"`).
- **Don't seed editor-authored objects.** They already have stable
  GUIDs from `OnValidate`. Re-seeding at runtime breaks replay.
- **Sidecar is gigabyte-scale on long sessions.** Default cadence is
  every frame; configurable via `SampleEveryNthFrame` on
  `SceneRecordingProfile`. For 30-min sessions consider `2` or `3` to
  halve / third disk usage if frame-exact reproduction isn't needed.
- **Replay placeholder mode.** If the replay scene is missing some
  prefabs (e.g. researcher only kept the source CSV), the replayer
  spawns colored placeholder cubes for each unknown GUID so the
  visualizer keeps working.

## References

- Source:
  - `Assets/Scripts/EyeTracking/SceneState/SceneStateRecorder.cs`
  - `Assets/Scripts/EyeTracking/SceneState/Recordable.cs`
  - `Assets/Scripts/EyeTracking/SceneState/RecordableRegistry.cs`
  - `Assets/Scripts/EyeTracking/SceneState/SceneRecordingProfile.cs`
- Tests:
  - `Assets/Editor/Tests/RecordableTests.cs`
  - `Assets/Editor/Tests/SceneStateCSVRoundTripTests.cs`
- Doc: `Eye_lean_Unity_Project/Eye_lean/docs/REPLAY_SCENE_STATE.md`
