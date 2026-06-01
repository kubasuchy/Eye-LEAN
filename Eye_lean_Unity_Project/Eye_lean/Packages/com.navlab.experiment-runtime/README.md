# com.navlab.experiment-runtime

ScriptableObject-driven experiment authoring + competitive virtual agents for
the navlab platform.

## What's in this package

- **`EnvironmentConfig`** (ScriptableObject) — static room geometry: walls,
  circular obstacles, spawn points, goals. Optionally backed by a
  `ProceduralEnvironmentDelegate`.
- **`ExperimentConfig`** (ScriptableObject) — experiment definition: agents,
  trial sequence, environment reference, random seed.
- **`AgentSpec` + `AgentSkin`** — per-agent definition with swappable prefabs.
- **`SceneAdapter`** — rasterizes EnvironmentConfig into an `OccupancyGrid`
  for the planner; also bridges tracked Unity transforms to `DynamicObstacle`
  list.
- **`CompetitiveNavAgent`** — MonoBehaviour driving an NPC along planner
  waypoints, replanning at a configurable rate (default 5 Hz).
- **`HumanObstacleTracker`** — exposes the XR camera as a dynamic obstacle so
  NPCs avoid the participant.
- **`PlannerLogger` / `ExperimentConfigExporter`** — write `_PlannerLog.csv`
  and `_ExperimentConfig.json` sidecars alongside Eye-LEAN's main CSV. The
  workbench reads both.
- **`ExperimentRunner`** — orchestrates trial sequence, resolves procedural
  environments, spawns agents, writes sidecars.
- **`EyeLeanProceduralDelegate`** — stub that integrates with Eye-LEAN's
  `RoomGenerator`. Replace its `Generate()` body with a call into Eye-LEAN.

## Install

In Unity Package Manager → Add package from git URL:

```
git+https://github.com/<org>/navlab.git?path=/unity-component
```

This automatically pulls `com.navlab.planners` and Newtonsoft.Json.

## Quick start

1. Create an `EnvironmentConfig` asset (right-click in Project: `Create > Navlab > Environment Config`).
2. Create an `ExperimentConfig` asset and link the environment.
3. Add agents + trials in the Inspector.
4. In your scene, add an empty GameObject with an `ExperimentRunner` component.
5. Assign the ExperimentConfig to the runner.
6. (Optional) Add a `HumanObstacleTracker` component on your XR camera and
   link it to the ExperimentRunner.
7. Press Play.

See `Samples~/EyeLeanNavScene` for a working example.

## Integration with Eye-LEAN

This package writes two sidecars alongside Eye-LEAN's main CSV:

- `EyeTracking_<session>_ExperimentConfig.json` — the experiment definition,
  read post-hoc by the workbench.
- `EyeTracking_<session>_PlannerLog.csv` — one row per replan, used for
  field-parity verification in the workbench.

To use a procedural environment driven by Eye-LEAN's `RoomGenerator`:

1. Add Eye-LEAN's assembly to this package's `Runtime/navlab.experiment-runtime.asmdef`
   under `references`.
2. Replace the body of `EyeLeanProceduralDelegate.Generate()` with calls into
   Eye-LEAN's room generator using the provided `randomSeed`.

## Building experiments without code

Most experiments are authored entirely through ScriptableObject assets:
EnvironmentConfig defines the room, ExperimentConfig defines who races whom
and where. No new C# needed unless the procedural delegate is being changed.
