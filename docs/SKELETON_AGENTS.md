# Skeleton — AgentManager + Microsoft Rocketbox

## What it is

`AgentManager` is the Skeleton's avatar pool: deterministic-replay-
friendly humanoid spawn / despawn API with vision-cone collision
avoidance and an editor-toggle visualization. It's designed for
**Microsoft Rocketbox** avatars (Gonzalez-Franco et al. 2020) but
ships in Eye_lean with a **procedural cube fallback** so the
Skeleton scene runs end-to-end without any external assets.

## Audience

Researchers spawning humanoid agents in a Skeleton experiment.

## Prerequisites

- A materialized Skeleton scene (`VR Experiment > New Skeleton Scene`).
- Optional: Microsoft Rocketbox avatars on disk (see "Installing
  Microsoft Rocketbox" below).

## When you'd use it

- Your experiment puts virtual humans in front of the participant —
  social-cognition studies, navigation in crowds, gaze-following
  tasks.
- You want pooled spawn / despawn (instantiation cost amortized at
  scene start).
- You want the spawned agents preserved in deterministic replay —
  `AgentManager` calls `MarkRecordableSeeded` for you.

## How to use it

```csharp
using EyeLean.Skeleton;

var mgr = AgentManager.Instance;
mgr.StartAgentSystem();

GameObject agent = mgr.SpawnAgent(
    position: new Vector3(2, 0, 4),
    direction: Vector3.left,
    speed: 1.2f);
// ... later
mgr.DespawnAgent(agent);
mgr.StopAgentSystem();
```

Without Rocketbox installed, `SpawnAgent` returns a tinted capsule
(no animation). Behavior code that expects `AgentBehavior` /
`Animator` should null-check.

## API reference

File: `Assets/Scripts/Skeleton/Managers/AgentManager.cs`

| Method / property | Purpose |
|---|---|
| `Instance` (static) | Singleton accessor (set in Awake). |
| `InitializePool(int)` | Pre-instantiate a pool of size N per gender. |
| `StartAgentSystem()` / `StopAgentSystem()` | Toggle the per-frame update loop. |
| `SpawnAgent(pos, dir, speed)` | Spawn an agent. Returns the GameObject. Auto-tags with `MarkRecordableSeeded` for replay. |
| `DespawnAgent(go)` / `DespawnAllAgents()` | Return to pool. |
| `PauseAllAgents()` / `ResumeAllAgents()` | Set speed to 0 / restore. |
| `GetNearestAgentDistance(pos)` | Useful for CSV per-frame metric columns. |
| `GetAgentPositions()` | List of active positions. |
| `IsUsingFallbackPrefabs` (prop) | True if cube fallback is active (i.e. Rocketbox not installed). |

## Installing Microsoft Rocketbox

1. **Clone the Rocketbox repository.**
   ```bash
   cd /path/to/some/staging/folder
   git clone https://github.com/microsoft/Microsoft-Rocketbox.git
   ```
   The repo is ~12 GB. Microsoft released the library under CC-BY 4.0
   in 2020.

2. **Move the avatar assets into your Eye_lean project.**
   Copy or symlink the relevant subfolders into
   `Eye_lean_Unity_Project/Eye_lean/Assets/RocketBox/`. At minimum
   you want the `Avatars` and `Animations` folders.

3. **Author or extract Resources prefabs.** `AgentManager` looks
   for prefabs at:
   - `Resources/Prefabs/Agents/Male_Adult_01` / `_02` / `_03`
   - `Resources/Prefabs/Agents/Female_Adult_01` / `_02` / `_03`

   Either name your prefabs to match those paths or copy the canonical
   Rocketbox prefabs into a `Resources/` folder under your Assets.

4. **Animator controllers.** AgentManager loads
   `Resources/Animations/MaleAgentAnimatorController` /
   `FemaleAgentAnimatorController` (with fallback to
   `AgentAnimatorController`). Build a basic walk-cycle controller
   from the Rocketbox `Animations/` clips and drop it at one of
   those paths.

5. **`.gitignore` exclusion.** Add `Eye_lean_Unity_Project/Eye_lean/Assets/RocketBox/`
   to `.gitignore` so the 12 GB doesn't end up in your repository.

6. **Restart Unity.** AgentManager picks up the prefabs on next
   scene load. Console message changes from "Falling back to
   procedural cube agents" to "Loaded prefabs - Males: 3, Females: 3".

7. **Cite the original paper** when publishing data collected with
   Rocketbox avatars:

   > Gonzalez-Franco, M., Ofek, E., Pan, Y., Antley, A., Steed, A.,
   > Spanlang, B., Maselli, A., Banakou, D., Pelechano Gomez, N.,
   > Orts-Escolano, S., Orvalho, V., Trutoiu, L., Wojcik, M.,
   > Sanchez-Vives, M. V., Bailenson, J., Slater, M., & Lanier, J.
   > (2020). The Rocketbox Library and the Utility of Freely
   > Available Rigged Avatars. *Frontiers in Virtual Reality*, 1, 20.
   > <https://doi.org/10.3389/frvir.2020.561558>

### Verify

Console at scene load reports `[AgentManager] Loaded prefabs -
Males: 3, Females: 3` instead of `Falling back to procedural cube
agents`, and `SpawnAgent()` returns a rigged humanoid rather than
a capsule.

## How it integrates with the rest of the toolkit

- **Replay.** Every `SpawnAgent` call routes through
  `SceneStateRecorder.MarkRecordableSeeded(go,
  $"Agent_{trialNumber}_{agentId}")`. Same trial number + same
  `Random.InitState` seed → same GUIDs across sessions. Replay
  reproduces.
- **Events sidecar.** Each spawn writes a `RecordKV("AgentSpawn",
  seed, ("trial", ...), ("position", ...), ("speed", ...))` row.
- **Custom CSV columns.** Researchers commonly call
  `SessionRecorder.RegisterMetric("ActiveAgentCount", () =>
  AgentManager.Instance.ActiveAgentCount)` and similar. See
  [SESSION_RECORDER.md](SESSION_RECORDER.md).
- **Vision-cone TTC** (time-to-collision) avoidance is implemented
  in `VisionConeScript.cs` and wired up in `AgentBehavior.cs`. With
  the cube fallback both are absent — agents stand still where
  spawned. Real Rocketbox avatars walk in the spawn direction at the
  configured speed and slow / stop when the participant gets within
  TTC threshold.

## Common patterns + gotchas

- **Cube fallback is NOT animated.** It exists so the Skeleton
  scene runs without crashing during early development. Don't run a
  real study with it.
- **Pool exhaustion warning.** `SpawnAgent` returns null when the
  pool is empty AND no prefab is available to instantiate fresh.
  The default `poolSizePerGender = 20` is enough for most studies;
  bump if you see warnings.
- **Despawn before scene change.** `AgentManager.OnDestroy` doesn't
  recycle agents; if you change scene mid-run, leftover agents may
  linger. Call `StopAgentSystem()` first.
- **Random spawn positions need their own determinism.** AgentManager
  doesn't choose spawn positions for you — your handler does.
  Make sure your position-picking code uses `UnityEngine.Random`
  (which is seeded by `TrialManager`) so positions reproduce on
  replay.

## References

- Source: `Assets/Scripts/Skeleton/Managers/AgentManager.cs`,
  `Assets/Scripts/Skeleton/Agent/AgentBehavior.cs`,
  `Assets/Scripts/Skeleton/Agent/VisionConeScript.cs`
- Microsoft Rocketbox repo:
  <https://github.com/microsoft/Microsoft-Rocketbox>
- Citation: Gonzalez-Franco et al. 2020 (above).
