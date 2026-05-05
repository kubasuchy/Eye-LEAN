# Skeleton — TrialManager + TrialConfiguration

## What it is

`TrialManager` runs the per-trial state machine that sits at the
core of the Skeleton template. Each trial cycles through five
phases:

```
InterTrialInterval → WaitingOnPlatform → FixationCross →
ExperimentalPhase → TrialComplete
```

The **ExperimentalPhase** is where your experiment lives — it
delegates to the registered `IExperimentPhaseHandler` (see
[SKELETON_HANDLER.md](SKELETON_HANDLER.md)).

`TrialConfiguration` is a ScriptableObject describing the trial /
block design. It's Inspector-editable so a researcher can configure
their experimental design without code changes.

## Audience

Developers configuring trials with `TrialManager` +
`TrialConfiguration`.

## Prerequisites

- A materialized Skeleton scene (`VR Experiment > New Skeleton Scene`).
- An `IExperimentPhaseHandler` implementation (see
  [SKELETON_HANDLER.md](SKELETON_HANDLER.md)).

## When you'd use it

- You want trial / block randomization without writing it yourself —
  `TrialConfiguration.randomizeTrialOrder` /
  `randomizeBlockOrder` flags.
- You want the per-trial / per-block recording context auto-flushed
  to the CSV's `TrialNumber` + `CurrentPhase` + `SubTask` +
  `SessionConfig` columns — this happens automatically.
- You want a deterministic seed for `Random.InitState` so block /
  trial randomization reproduces on replay — set the `randomSeed`
  field on `TrialManager`.

## How to use it

```csharp
// In your IExperimentPhaseHandler MonoBehaviour:
private void Awake()
{
    var trialMgr = FindFirstObjectByType<TrialManager>();
    trialMgr.SetPhaseHandler(this);
}

public void OnPhaseStart() { /* show stimulus */ }
public void OnPhaseEnd()   { /* clean up */ }
public bool IsPhaseComplete() => participantResponded || timedOut;
public Dictionary<string, object> GetPhaseData() => new() {
    { "responseTime", rt },
    { "accuracy", correct },
};
```

To configure trials at edit time:
1. **Assets > Create > Eye_lean > Skeleton Trial Configuration**.
2. Set `experimentName` and add blocks (each block has a name +
   trials count + per-block parameters like agent speed).
3. Drag the asset into `TrialManager.experimentConfiguration`.

### Verify

Press Play; Console shows `[TrialManager] Initialized N trials`
where N matches the configured trial count, and the events sidecar
records `TrialStarted` rows with the configured `experimentName`.

## API reference

File: `Assets/Scripts/Skeleton/Managers/TrialManager.cs`

| Method / property | Purpose |
|---|---|
| `SetPhaseHandler(IExperimentPhaseHandler)` | Register the phase handler. Call in your handler's `Awake`. |
| `CompleteExperimentalPhase()` | Advance from ExperimentalPhase to TrialComplete from your code. |
| `FailCurrentTrial(string reason)` | Mark the current trial failed and skip to TrialComplete. The reason is written to the events sidecar + per-frame `TrialFailureReason` column. |
| `OnPlatformActivated()` | Call when the participant steps on the starting platform; advances ITI → WaitingOnPlatform → FixationCross. |
| `GetCurrentPhase()` / `GetCurrentTrial()` / `GetCurrentBlock()` | Read live state. |
| `GetProgressInfo()` | "Trial X / Y (Block A / B)" string for HUDs. |
| `GetPhaseRemainingTime()` | Seconds left in the current timed phase. |

| Event | Fires when |
|---|---|
| `OnPhaseChanged(TrialPhase)` | Phase transition. |
| `OnTrialStarted(TrialData)` | A trial enters FixationCross. |
| `OnTrialCompleted(TrialData)` | A trial reaches TrialComplete. |
| `OnAllTrialsCompleted()` | The whole sequence is done. |

File: `Assets/Scripts/Skeleton/Data/TrialConfiguration.cs`

| Field | Purpose |
|---|---|
| `experimentName` | Becomes the CSV's `SessionConfig` column. |
| `description` | Free text. |
| `version` | Track config changes over time. |
| `blocks` | List of `TrialBlock`s. |
| `randomizeTrialOrder` | Shuffle within each block. |
| `randomizeBlockOrder` | Shuffle blocks (counterbalancing). |
| `IsValid(out msg)` | Validate (rejects empty / duplicate-named blocks). |

`TrialBlock` ships with `agentMeanSpeed`, `agentSpeedVariation`,
`restrictToWalkingState`. Subclass it for experiment-specific
parameters (stimulus colors, condition tags, etc.).

## How it integrates with the rest of the toolkit

Every state transition writes to one or more Eye_lean recorders.
See the integration table in [SKELETON.md](SKELETON.md). Highlights:

- **`SessionRecorder.SetSessionContext`** is called on
  `OnTrialStarted` so the per-frame CSV's session-context columns
  always reflect the live trial.
- **`SetSubTask`** is called on every phase transition so
  per-frame `SubTask` cycles through the five phases.
- **`SetMetadata`** is called for every key returned from
  `GetPhaseData()` so per-trial data lands in the CSV without you
  having to wire columns manually.
- **`SceneEventRecorder.RecordKV`** logs every phase transition,
  trial start, and trial complete to the events sidecar with the
  trial number + block name embedded.
- **`ConfigTrials` JSON snapshot** is emitted at session start so
  replay sees the same trial layout regardless of post-hoc
  inspector tweaks.

## Common patterns + gotchas

- **`fixationCrossDuration = 0` means "no auto-advance from
  FixationCross."** The `FixationCross` UI component itself signals
  completion via its own audio-driven timing; if you want a fixed
  duration set this field.
- **`SetSessionContext` is auto-called on FixationCross entry.** If
  you also call `SetSessionContext` from your handler you'll
  overwrite the auto-set values. Use `SetMetadata` for additional
  fields instead.
- **TrialPhase ↔ ExperimentPhase distinction.** Skeleton's
  `TrialPhase` is *within-trial* (5 phases). Eye_lean's
  `ExperimentPhase` is *across-experiment* (Idle / Instructions /
  FreeExploration / etc., used by SampleExperiment). They're
  orthogonal — Skeleton writes its TrialPhase to the `SubTask`
  column and lets `CurrentPhase` carry the high-level phase if you
  want to set one via `SetSessionContext` from your handler.
- **Random determinism contract.** `TrialManager.Start` calls
  `UnityEngine.Random.InitState(randomSeed)` BEFORE shuffling. Any
  use of `UnityEngine.Random` in your handler is part of the same
  deterministic stream. Don't use `System.Random` (replay will
  diverge).

## References

- Source: `Assets/Scripts/Skeleton/Managers/TrialManager.cs`,
  `Assets/Scripts/Skeleton/Data/TrialConfiguration.cs`
- Tests: `Assets/Editor/Tests/SkeletonTrialManagerTests.cs`
- Related: [SKELETON_HANDLER.md](SKELETON_HANDLER.md) for the
  `IExperimentPhaseHandler` contract;
  [SCENE_EVENTS.md](SCENE_EVENTS.md) for the events sidecar format.
