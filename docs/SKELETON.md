# Skeleton — researcher template

## What it is

The **Skeleton** is Eye_lean's clean trial-loop scaffold for
researchers building their own VR experiment. It is a
**developer-side tool**, not part of the Eye_lean APK build flow:
the MainMenu doesn't launch into it, and the wizard does NOT add the
generated scene to Build Settings. You use it in the Unity editor as
a starting point for your own experiment, add it to your own build
settings when you're ready to ship, and Eye_lean's recording / replay
/ RIPA layer comes along for the ride.

[SampleExperiment](SAMPLE_EXPERIMENT.md) (build-2 in the shipped
APK) remains the worked example you can read end-to-end. The
Skeleton is the *minimum viable scaffold* for when you'd rather
start from a blank canvas.

The Skeleton's value is **shape**: a trial state machine
(ITI → Platform → Fixation → ExperimentalPhase → TrialComplete) plus
an `IExperimentPhaseHandler` interface where your code lives. You
implement four methods, you're done.

## Audience

Researchers building a custom experiment from the Skeleton template.

## Prerequisites

- The Eye_lean Unity project open in the editor.
- Familiarity with C# MonoBehaviours.

## When you'd use it

- You want a fresh scene with minimum scaffolding.
- Your task fits a "trials-with-phases" loop.
- You want full Eye_lean recording / replay / RIPA out of the box
  with zero wiring.
- You're new to VR experiment design and want a less-overwhelming
  starting point than reading SampleExperiment's full source.

## How to use it

1. Open Eye_lean in Unity.
2. Run **VR Experiment > New Skeleton Scene** from the Unity menu.
   - Creates `Assets/Scenes/Skeleton.unity` and populates it with
     Managers + Eye_lean recorder rig + a demo phase handler.
   - Does **not** add the scene to EditorBuildSettings — Skeleton
     is a developer tool, not a shipped APK target. Add it to your
     build manually when you're ready to deploy your experiment.
3. Implement `IExperimentPhaseHandler` in a new MonoBehaviour
   (start by copying
   `Assets/Scripts/Skeleton/Examples/EyeleanDemoPhaseHandler.cs`).
4. Press Play in the editor.

### Verify

Console shows `[RIPAMonitor] Active`, `[SessionRecorder] CSV
initialized`, and `[TrialManager] Initialized N trials`. The CSV
writes with phase / trial / metadata columns. The events sidecar
logs `TrialStarted`, `TrialPhaseChanged`, and `TrialCompleted` rows.

## How the wizard's scene is laid out

```
Skeleton.unity
├── Main Camera (HMD-driven via HmdPoseDriverBootstrap)
├── Directional Light
├── Managers
│   ├── TrialManager        — trial state machine
│   ├── ExperimentManager   — session-level coordinator
│   ├── AgentManager        — avatar pool (cube fallback if no Rocketbox)
│   └── EnvironmentManager  — procedural room
├── EyeTrackingSystem
│   ├── EyeTracker
│   ├── HMDDataCollector
│   └── SessionRecorder
└── DemoPhaseHandler        — example IExperimentPhaseHandler

(auto-bootstrapped at runtime, not visible in editor:)
├── [HmdPoseDriverBootstrap] (DDOL)
├── [RIPAMonitorBootstrap]   (DDOL)
└── [RIPAMonitor]            (per-scene)
```

## Eye_lean integration table

This is the full wiring map between Skeleton's events and Eye_lean's
recording surfaces. It happens automatically — no researcher
action required. Knowing it helps when reading the recorded CSVs.

| Skeleton trigger | Eye_lean recorder call | CSV / sidecar effect |
|---|---|---|
| `TrialManager.Start` | `RecordJson("ConfigTrials", ...)` + `RecordKV("RandomSeed", ...)` | Events sidecar gets the trial-config snapshot + the seed for `Random.InitState`. |
| `OnPhaseChanged(phase)` | `SessionRecorder.SetSubTask(phase)` + `RecordKV("TrialPhaseChanged", ...)` | Per-frame `SubTask` column tracks the live phase; events sidecar logs the boundary. |
| `OnTrialStarted(trial)` (FixationCross entry) | `SessionRecorder.SetSessionContext(trial, ...)` + `SetMetadata("TrialBlockName", ...)` + `RecordKV("TrialStarted", ...)` | `TrialNumber` / `CurrentPhase` / `SessionConfig` columns track the live trial. |
| `OnTrialCompleted(trial)` | `SetMetadata` for every key in `GetPhaseData()` + `RecordKV("TrialCompleted", ...)` | Per-frame metadata columns get the trial's per-trial data; events sidecar logs the boundary + summary. |
| `OnAllTrialsCompleted` | `Record("AllTrialsCompleted")` | Events sidecar logs end-of-session. |
| `AgentManager.SpawnAgent` | `SceneStateRecorder.MarkRecordableSeeded(go, "Agent_{trial}_{slot}")` + `RecordKV("AgentSpawn", ...)` | Scene-state sidecar records the spawn at a deterministic GUID; replay reproduces. |
| `EyeleanDemoPhaseHandler.OnPhaseStart` | `RecordKV("StimulusOnset", ...)` + `MarkRecordableSeeded("DemoStimulus_{trial}")` | Per-trial event + scene-state row. |
| `EyeleanDemoPhaseHandler.GetPhaseData` | `SetMetadata` for every returned key | Per-trial metadata columns: `responseReceived`, `responseTime`, `phaseDuration`, `ripaAtResponse`. |

## Skeleton component manuals

| Topic | Manual |
|---|---|
| Trial machine + TrialConfiguration | [SKELETON_TRIALS.md](SKELETON_TRIALS.md) |
| Phase handler contract | [SKELETON_HANDLER.md](SKELETON_HANDLER.md) |
| Avatar pool (Rocketbox install) | [SKELETON_AGENTS.md](SKELETON_AGENTS.md) |
| Environment / procedural rooms | [SKELETON_ENVIRONMENT.md](SKELETON_ENVIRONMENT.md) |

## Common patterns + gotchas

- **The wizard overwrites if you re-run it.** Back up authored scene
  content first.
- **No need to add `RIPAMonitor` to the scene yourself.** The
  bootstrap (`RIPAMonitorBootstrap`) auto-spawns it. If you want to
  opt out, set `RIPAMonitorBootstrap.DisableAutoSpawn = true` before
  scene load.
- **`Random.InitState` is seeded by `TrialManager`.** Don't
  re-seed elsewhere or replay will diverge.
- **Per-trial spawns must use `MarkRecordableSeeded`.** Editor-
  authored objects are auto-tagged via their inspector-assigned
  `Recordable`. Runtime spawns are NOT auto-tagged; if you want them
  reproduced on replay, you must call `MarkRecordableSeeded` with a
  per-trial-stable seed.

## References

- Source: `Assets/Scripts/Skeleton/`
- Tests: `Assets/Editor/Tests/SkeletonTrialManagerTests.cs`
- Provenance: forked from
  `https://github.com/.../VR_Experiments_Skeleton_Starter` and
  re-namespaced into `EyeLean.Skeleton`. Stale fork of pre-RC
  Eye_lean's `EyeTracking/*` discarded; recording goes through the
  current `SessionRecorder` / `SceneStateRecorder` /
  `SceneEventRecorder` trio.
