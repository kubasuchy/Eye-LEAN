# SceneEventRecorder

## What it is

`SceneEventRecorder` writes a **discrete-event sidecar CSV** that
captures named events at exact frames. Where `SceneStateRecorder`
captures *continuous* state (positions, rotations) per frame,
`SceneEventRecorder` captures *moments*: instruction shows, trial
boundaries, custom feedback events, audio cues, structured payloads.

It's the third leg of Eye_lean's recording stool: the main CSV is
per-frame eye/head state; the scene-state sidecar is per-frame object
transforms; the events sidecar is "what happened, exactly when."

## Audience

Developers writing or consuming the SceneEvents sidecar.

## Prerequisites

- A scene with `SessionRecorder` bootstrapped (Eye_lean scenes do
  this automatically).

## When you'd use it

- An instruction panel changes — `ExperimentUI` already wraps every
  show / hide call with a Record event.
- A trial starts / ends — `TrialManager` (Skeleton) auto-records.
- Your custom phase fires a stimulus, plays audio, applies a
  condition switch — record it explicitly so analysts can find the
  exact frame post hoc.
- You want to snapshot a complex configuration into the recording so
  replay sees the same parameters even if the inspector is later
  edited — use `RecordJson`.

## How to use it

```csharp
using EyeLean.SceneState;

// Plain string detail.
SceneEventRecorder.Record("CueOnset", objectId: "", detail: "tone_500hz_200ms");

// Structured key=value pairs (auto-encoded as `k1=v1;k2=v2`).
SceneEventRecorder.RecordKV("AnswerSelected", "",
    ("value", value.ToString()),
    ("rt", responseTime.ToString("F3")));

// JSON payload (Base64-encoded into the Detail column; decode with
// SceneEventReplayer.DecodeJson<T>(row) on read).
SceneEventRecorder.RecordJson("CustomConfig", "", myConfigStruct);
```

## API reference

File: `Assets/Scripts/EyeTracking/SceneState/SceneEventRecorder.cs`

| Static method | Line | Purpose |
|---|---|---|
| `Record(type, objectId, detail)` | 177 | Emit a named event at the current frame + timestamp. |
| `RecordKV(type, objectId, params (key, val)[])` | 189 | Same, with auto `k=v;k=v` formatting. |
| `RecordJson(type, objectId, payload)` | 263 | Same, with `JsonUtility` + Base64 encoding so commas / newlines don't break CSV alignment. |

Sidecar file format:
```
# Eye_lean Scene Events Sidecar
# FileVersion: 1.0
# SessionID: <same as main>
Frame,T,EventType,ObjectId,Detail
```

### Reserved event types — DO NOT emit these from researcher code

These are owned by the Eye_lean infrastructure and have specific
semantics on replay:

| Type | Owner | Purpose |
|---|---|---|
| `Spawn` | `SceneStateRecorder` | A `Recordable` was registered. |
| `Despawn` | `SceneStateRecorder` | A `Recordable` was destroyed. |
| `IdRegenerated` | `RecordableRegistry` | A GUID collision was resolved. |
| `RandomStateSnapshot` | infra | `UnityEngine.Random.state` snapshot at session start. |
| `ConfigExploration` / `ConfigVisualSearch` / `ConfigCounting` / `ConfigChangeDetection` | `SampleExperimentController` | Phase config snapshots for replay. |
| `ConfigTrials` | `Skeleton.TrialManager` | `TrialConfiguration` snapshot. |
| `RandomSeed` | `Skeleton.TrialManager` | Per-session seed used for `Random.InitState`. |
| `TrialStarted` / `TrialPhaseChanged` / `TrialCompleted` / `AllTrialsCompleted` | `Skeleton.TrialManager` | Trial-machine boundaries. |
| `AgentSpawn` | `Skeleton.AgentManager` | Avatar spawn with deterministic seed. |
| `ShowInstruction` / `HideInstruction` / `SetInstructionTextOnly` / `ShowProgress` / `HideProgress` | `ExperimentUI` | Auto-wrapped UI calls. |

## How it integrates with the rest of the toolkit

- **Auto-bootstrap.** `SceneEventRecorder` auto-attaches to the
  `SessionRecorder` GameObject. The static API works as soon as the
  bootstrap fires.
- **Frame counter shared with main CSV** so an analyst can join
  events to per-frame samples on `Frame`.
- **`SceneEventReplayer`** (replay system) reads back each row and
  either (a) lets the live experiment re-run the same UI / config
  call paths against recorded inputs, or (b) fires registered
  replay-side handlers for events the live experiment doesn't
  re-issue (e.g. researcher-only diagnostic events).
- **`ExperimentUI`** auto-records every `ShowInstruction` / etc.
  call. Toggle off via `autoRecordEvents` if you record yourself.

## Common patterns + gotchas

- **Detail column is sanitized.** A literal comma in a Detail string
  is replaced with `_` so it can't break CSV alignment. If you need
  arbitrary text, use `RecordJson`.
- **De-dupe per-frame events.** `ExperimentUI.ShowProgress` is
  called every frame in timed phases; the wrapper de-dupes on text
  change so the events sidecar doesn't bloat.
- **Replay-side handlers are optional.** Most researchers don't need
  to write any. The deterministic-replay path re-runs the live
  experiment code (your phase handler), which itself re-issues every
  recorded UI / config call at the same frame. Handlers are only for
  diagnostic events the live experiment doesn't naturally re-issue.
- **`RecordJson` is fire-and-forget.** Use for one-time snapshots
  (configs, baselines). Don't use for high-frequency data — the
  Base64 overhead bloats the sidecar.

## References

- Source: `Assets/Scripts/EyeTracking/SceneState/SceneEventRecorder.cs`
- Replay-side: `Assets/Scripts/Replay/SceneState/SceneEventReplayer.cs`
- Tests: `Assets/Editor/Tests/SceneEventCSVRoundTripTests.cs`
- Researcher Guide: "Optional: emit researcher-defined events" section
  in `Eye_lean_Unity_Project/Eye_lean/docs/RESEARCHER_GUIDE.md`.
