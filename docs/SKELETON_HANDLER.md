# Skeleton — IExperimentPhaseHandler contract

## What it is

`IExperimentPhaseHandler` is the four-method interface where your
experiment's task logic lives. The Skeleton's `TrialManager` calls
into your handler at the right moments; everything else (recording,
trial sequencing, Random seeding, replay) is automatic.

## Audience

Developers implementing `IExperimentPhaseHandler` for a custom phase.

## Prerequisites

- A materialized Skeleton scene (`VR Experiment > New Skeleton Scene`).
- Familiarity with C# coroutines and MonoBehaviours.

## When you'd use it

- You're building any Skeleton-based experiment. Your task code goes
  into one or more handler implementations.

## How to use it

```csharp
using System.Collections.Generic;
using UnityEngine;
using EyeLean.Skeleton;

public class MyExperimentPhase : MonoBehaviour, IExperimentPhaseHandler
{
    private float startTime;
    private bool participantResponded;
    private float responseTime;

    private void Awake()
    {
        var trialMgr = FindFirstObjectByType<TrialManager>();
        trialMgr.SetPhaseHandler(this);
    }

    public void OnPhaseStart()
    {
        startTime = Time.time;
        participantResponded = false;
        // Show your stimulus, spawn agents, start audio, etc.
    }

    public void OnPhaseEnd()
    {
        // Tear down your stimulus.
    }

    public bool IsPhaseComplete()
    {
        // Return true when the phase should end. Polled every frame.
        return participantResponded || (Time.time - startTime > 5f);
    }

    public Dictionary<string, object> GetPhaseData()
    {
        // Return values to flush into the CSV's per-trial metadata
        // columns. Each key becomes a column on the trial's row(s).
        return new Dictionary<string, object>
        {
            { "responseReceived", participantResponded },
            { "responseTime", responseTime },
            { "phaseDuration", Time.time - startTime },
        };
    }
}
```

A heavily-commented worked example lives at
`Assets/Scripts/Skeleton/Examples/EyeleanDemoPhaseHandler.cs`. Copy
it as a starting point.

## API reference

File: `Assets/Scripts/Skeleton/Core/IExperimentPhaseHandler.cs`

| Method | Called by `TrialManager` when | Purpose |
|---|---|---|
| `OnPhaseStart()` | ExperimentalPhase begins (after FixationCross). | Show stimulus, spawn agents, start timers. |
| `OnPhaseEnd()` | ExperimentalPhase ends — either because `IsPhaseComplete` returned true, or because `TrialManager.CompleteExperimentalPhase()` was called externally. | Clean up. |
| `IsPhaseComplete()` | Polled every frame while the phase is active. | Return true to advance to TrialComplete. |
| `GetPhaseData()` | Right after `OnPhaseEnd`, on the way to TrialComplete. | Return per-trial data. Each key becomes a CSV metadata column + a row in the events sidecar. |

## How it integrates with the rest of the toolkit

`TrialManager` does the wiring. Specifically:

- **Per-frame CSV.** While your handler runs, `SessionRecorder` is
  writing every frame with `CurrentPhase = ExperimentalPhase`,
  `SubTask` cycling through the trial states, and your
  `TrialBlockName` + `TrialIndexInBlock` metadata columns set.
- **`GetPhaseData()` flush.** Each key/value you return is fed
  through `SessionRecorder.SetMetadata` (typed by runtime value:
  `bool` → `SetMetadata(name, bool)`, `int` → `(name, int)`, etc.).
  The corresponding column appears for that row and subsequent rows
  unless overwritten on the next trial.
- **Events sidecar.** A `TrialCompleted` row is written with all
  your `GetPhaseData` keys flattened into the `Detail` column as
  `key1=value1;key2=value2`. This lets analysts find trial summaries
  without scanning the per-frame CSV.
- **Replay.** Your handler runs again under deterministic replay.
  Provided you use only `UnityEngine.Random`, `WaitForSeconds`-based
  coroutines, and `MarkRecordableSeeded` for runtime spawns,
  reproduction is frame-accurate.

## Common patterns + gotchas

- **One handler per scene.** `TrialManager.SetPhaseHandler` replaces
  the previous handler. If you want multiple "phases" within a
  single trial, stay in one handler and use internal substates.
- **Don't rely on `Update` for completion.** Use `IsPhaseComplete`
  (cleaner) or call `TrialManager.CompleteExperimentalPhase()`
  directly from event callbacks (e.g. button-press handlers).
- **`GetPhaseData()` is the only path to per-trial data.** Don't
  manually call `SessionRecorder.SetMetadata` from your handler —
  TrialManager flushes `GetPhaseData()` for you, and double-writes
  cause column-count surprises.
- **Custom CSV columns from your handler:** use
  `SessionRecorder.RegisterMetric` in your handler's `Awake` for
  *per-frame* metric columns (e.g. "stimulus visible" Boolean).
  Those are independent of `GetPhaseData()`'s per-trial columns.
- **Handler can fail trials.** Call
  `TrialManager.FailCurrentTrial("reason")` to mark a trial failed
  (e.g. participant collided with a wall, didn't respond). The
  reason lands in the per-frame `TrialFailureReason` column AND the
  events sidecar.

## References

- Source: `Assets/Scripts/Skeleton/Core/IExperimentPhaseHandler.cs`
- Reference implementation:
  `Assets/Scripts/Skeleton/Examples/EyeleanDemoPhaseHandler.cs`
- Related: [SKELETON_TRIALS.md](SKELETON_TRIALS.md),
  [SESSION_RECORDER.md](SESSION_RECORDER.md).
