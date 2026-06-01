# EyeLeanNavScene Sample

Demonstrates a competitive-navigation trial: one virtual NPC racing toward a
goal alongside a human participant. The NPC uses D* Lite via the shared
`com.navlab.planners` package and treats the participant as a dynamic obstacle.

## IMPORTANT — Assets that must be created in the Unity Editor

The following files **cannot be created as text files** and must be assembled
manually in the Unity Editor. Steps are provided below:

| File | Type | Must create manually |
|---|---|---|
| `EyeLeanNavScene.unity` | Unity scene | Yes — see Step 1 |
| `Configs/SampleEnvironment.asset` | EnvironmentConfig ScriptableObject | Yes — see Step 2 |
| `Configs/SampleExperiment.asset` | ExperimentConfig ScriptableObject | Yes — see Step 3 |

---

## Setup

### Step 1 — Create the scene

1. In the Unity Project window, right-click inside
   `Packages/Navlab Experiment Runtime/Samples~/EyeLeanNavScene/` →
   **Create → Scene**, name it `EyeLeanNavScene`.
2. Open the scene and add:
   - A **floor**: `GameObject → 3D Object → Plane`, scale to `(0.6, 1, 0.6)`
     to produce a roughly 6 m × 6 m surface.
   - **Perimeter walls** (4 thin Cubes):
     - North wall: position `(0, 0.5, 6)`, scale `(6, 1, 0.1)`
     - South wall: position `(0, 0.5, 0)`, scale `(6, 1, 0.1)`
     - East wall:  position `(3, 0.5, 3)`, scale `(0.1, 1, 6)`
     - West wall:  position `(-3, 0.5, 3)`, scale `(0.1, 1, 6)`
   - A **MainCamera** GameObject (tag it `MainCamera`) as a placeholder for
     the XR rig.
   - An empty GameObject named **ExperimentRunner**; attach the
     `ExperimentRunner` component to it.
   - On the MainCamera (or a child), attach a **`HumanObstacleTracker`**
     component and link it to the `ExperimentRunner`'s `humanTracker` field.

### Step 2 — Create `Configs/SampleEnvironment.asset`

1. Create a `Configs/` folder inside the sample directory.
2. Right-click → **Create → Navlab → Environment Config**, name it
   `SampleEnvironment`.
3. In the Inspector, set:
   - `boundsMin`: `(-3, 0)`
   - `boundsMax`: `(3, 6)`
   - **Spawn Points** (add 2 entries):
     - `S_human` — positionXZ `(0, 0.3)`, headingDeg `0`
     - `S_npc`   — positionXZ `(-1.5, 0.3)`, headingDeg `0`
   - **Goals** (add 1 entry):
     - `G` — positionXZ `(0, 5.5)`, headingDeg `180`

### Step 3 — Create `Configs/SampleExperiment.asset`

1. Right-click → **Create → Navlab → Experiment Config**, name it
   `SampleExperiment`.
2. In the Inspector, set:
   - `experimentName`: `EyeLeanNavSample`
   - `environment`: drag `SampleEnvironment.asset` here
   - `randomSeed`: `42`
   - **Agents** (add 2 entries):
     - Agent 0 — `name`: `P_human`, `agentType`: `Human`,
       `planner`: `None`, `spawnRef`: `S_human`, `goalRef`: `G`
     - Agent 1 — `name`: `NPC_1`, `agentType`: `Npc`,
       `planner`: `DStarLite`, `spawnRef`: `S_npc`, `goalRef`: `G`
   - **Trials** (add 1 entry):
     - `trialId`: `t1`, `activeAgentNames`: `[P_human, NPC_1]`,
       `durationSeconds`: `30`, `trialSeed`: `1`
3. Drag `SampleExperiment.asset` onto the `ExperimentRunner` component's
   `experiment` field in the scene.
4. Save the scene (**Ctrl+S / Cmd+S**).

---

## What it does

- Loads `SampleEnvironment.asset` → a 6×6 m room with one goal.
- Loads `SampleExperiment.asset` → one trial, 30 s, P_human + NPC_1.
- Spawns NPC_1 as a Capsule (or the assigned skin) at S_npc.
- The NPC computes a path to G via D* Lite, replanning at 5 Hz.
- The participant (MainCamera) is registered as a dynamic obstacle, so the
  NPC avoids walking through them.
- At session start, `_ExperimentConfig.json` is written to `Logs/`.
- At each replan, `_PlannerLog.csv` is appended.

## Optional: add a Rocketbox skin

1. Right-click → **Create → Navlab → Agent Skin**, name it `HumanoidSkin`.
2. Set `prefab` to a Rocketbox humanoid prefab.
3. Assign `HumanoidSkin` to NPC_1's `AgentSpec.skin` field in
   `SampleExperiment.asset`.

## Optional: XR Rig

Replace the placeholder `MainCamera` with an XR Origin from your VR SDK
(OpenXR / Meta XR / etc.). The `HumanObstacleTracker` will automatically
track the `Camera.main` pose, or you can assign a specific camera in the
`headCamera` field.

## Viewing in the workbench

After running the scene, the workbench can import the `Logs/` directory:
either the main Eye-LEAN CSV will be there (if Eye-LEAN's SessionRecorder is
running) or, in this sample, only the sidecars. The workbench's Live tab will
detect the new session and import it.
