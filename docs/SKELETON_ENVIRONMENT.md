# Skeleton — EnvironmentManager + EnvironmentBuilder

## What it is

Procedural environment system for the Skeleton template. Generates a
floor + four walls + fences + lamps + lighting at scene start, and
optionally clears / regenerates between trials. Researchers extend
`BaseEnvironment` (override `CreateCustomElements()`) to add
experiment-specific props.

## Audience

Researchers building or customizing the room around a Skeleton
experiment.

## Prerequisites

- A materialized Skeleton scene (`VR Experiment > New Skeleton Scene`).

## When you'd use it

- You don't have time to author a hand-modeled scene and want a
  sensible default room out of the box.
- Your task fits a simple "rectangular room with start platform at
  one end" shape.
- You want the room to clear / regenerate per trial (different
  layouts, different prop placements, different difficulty).

## How to use it

```csharp
using EyeLean.Skeleton;

// Configure via Inspector or programmatically:
var cfg = new EnvironmentConfiguration
{
    width = 8f,           // X span
    length = 12f,         // Z span
    wallHeight = 3f,
    floorMaterial = ..., wallMaterial = ...,
};

var mgr = FindFirstObjectByType<EnvironmentManager>();
mgr.GenerateEnvironment(cfg);  // build at trial start
mgr.ClearEnvironment();        // tear down at trial end

// Custom elements: subclass BaseEnvironment and override
// CreateCustomElements() to add your stimuli.
public class MyEnvironment : BaseEnvironment
{
    protected override void CreateCustomElements()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.parent = environmentRoot;
        cube.transform.position = new Vector3(0, 1, 5);
        // Tag for replay reproducibility:
        EyeLean.SceneState.SceneStateRecorder.MarkRecordableSeeded(
            cube, "MyTargetCube_block1_trial5");
    }
}
```

## API reference

Files (all under `Assets/Scripts/Skeleton/Environment/`):

| File | Purpose |
|---|---|
| `EnvironmentManager.cs` | Lifecycle coordinator; subscribes to `TrialManager.OnPhaseChanged` to generate / clear at the right moments. |
| `BaseEnvironment.cs` | Override `CreateCustomElements` here for your custom props. |
| `EnvironmentBuilder.cs` | Procedural floor / walls / fences / lamps. |
| `EnvironmentConfiguration.cs` | Inspector-editable config struct (dimensions, materials, lighting). |
| `EnvironmentGeometry.cs` | Pure-function helpers for spawn-position math. |

| Public method (EnvironmentManager) | Purpose |
|---|---|
| `GenerateEnvironment(EnvironmentConfiguration)` | Build the room. |
| `ClearEnvironment()` | Tear down. |
| `GetBaseEnvironment()` | Access the live `BaseEnvironment` for runtime tweaks. |
| `OnExitReached` (event, on BaseEnvironment) | Fires when participant crosses the exit trigger. EnvironmentManager forwards this to `TrialManager.SetPhase(TrialComplete)` by default. |

## How it integrates with the rest of the toolkit

- **Per-trial generation.** Subscribes to
  `TrialManager.OnPhaseChanged` — generates on `ExperimentalPhase`
  entry, clears on `TrialComplete` (configurable in Inspector).
- **Coordinate origin.** When the environment is built, the
  `EnvironmentManager` typically calls
  `HMDDataCollector.SetCoordinateOrigin()` so all subsequent CSV
  rows are normalized relative to the trial's spawn position. Make
  sure your override does the same if you build a custom environment.
- **Replay.** Static room geometry doesn't need replay tagging
  (it's the same every trial). Custom props you spawn in
  `CreateCustomElements` MUST be tagged with `MarkRecordableSeeded`
  if you want replay to reproduce them.
- **Materials.** `EnvironmentBuilder` uses `VRMaterialProvider` for
  Android shader stripping safety. URP/Lit / Standard / Mobile/Diffuse
  all unreliable in the Eye_lean Android build pipeline; the
  primitive-extracted `Unlit/Color` material from `VRMaterialProvider`
  is the reliable path.

## Common patterns + gotchas

- **Spawn yaw is head-tilt-independent.** Use
  `atan2(forward.x, forward.z)` on the XZ-projected forward vector,
  not `cameraTransform.eulerAngles.y` (gimbal-flips on tilted
  heads). The base `EnvironmentBuilder` does this; preserve the
  pattern in your subclass.
- **Wall corners need overlap on ALL THREE axes.** RC2.2 fixed XZ;
  RC2.3 added Y. For 6 cube primitives forming a room, each wall
  extends `2*wallThickness` on each axis where it meets another
  surface.
- **Decorative props must clear the spawn lane.** Don't put
  decorations in front of the user's spawn position; calibration /
  task targets need that lane.
- **Lighting clobbering.** `EnvironmentBuilder.ConfigureAmbientLighting`
  changes `RenderSettings.ambientMode` + sky / equator / ground
  colors. If your downstream code wants different lighting, call
  it AFTER EnvironmentManager runs (or set
  `EnvironmentManager.configureLightingOnStart = false`).
- **Unity 6.3 Find APIs.** `FindObjectOfType` is deprecated; use
  `FindFirstObjectByType` (the migration script already did this).

## References

- Source: `Assets/Scripts/Skeleton/Environment/`
- Memory: `project_v1_rc2_polish.md` (RC2 hardware-test fixes for
  wall corners, lighting, spawn yaw).
- Related: [SCENE_STATE.md](SCENE_STATE.md) for tagging custom
  props.
