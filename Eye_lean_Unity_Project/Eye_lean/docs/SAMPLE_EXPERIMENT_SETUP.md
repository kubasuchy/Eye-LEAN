# SampleExperiment Setup

## What it is

Step-by-step procedure to assemble `Assets/Scenes/SampleExperiment.unity`
from an empty Unity scene, or to verify the cloned repo's copy. The
scene wires `SampleExperimentController` against `EyeTracker`,
`HMDDataCollector`, `SessionRecorder`, and three task managers
(`VisualSearchManager`, `CountingTaskManager`, `ChangeDetectionManager`)
to run a 4-phase battery: FreeExploration → VisualSearch → CountingTask
→ ChangeDetection.

For the conceptual walkthrough of what the experiment exercises and how
to extend it, see
[`../../../docs/SAMPLE_EXPERIMENT.md`](../../../docs/SAMPLE_EXPERIMENT.md).

## Audience

Developers configuring a SampleExperiment scene from scratch (or
verifying the cloned repo's copy).

## Prerequisites

- Unity 6000.3.9f1
- VIVE OpenXR 2.5.1 (`com.htc.upm.vive.openxr`)
- Repo cloned from `https://github.com/kubasuchy/EYE-LEAN`
- `Eye_lean_Unity_Project/Eye_lean/` open in Unity Hub
- Eye-tracking-capable HMD for hardware verification (VIVE Focus Vision tested)

TextMeshPro is bundled with `com.unity.ugui` in Unity 6 — no separate
install step is needed.

---

## 1. Create or open the scene

If the cloned repo already contains
`Assets/Scenes/SampleExperiment.unity`, open it and skip to
[section 7 (Verify)](#7-verify-against-the-cloned-repo).

Otherwise:

1. **File > New Scene > Basic (Built-in) > Create**.
2. **File > Save As**, navigate to `Assets/Scenes/`, name it
   `SampleExperiment.unity`.
3. Delete the default `Main Camera` from the Hierarchy. Keep the
   `Directional Light`.

### Verify

Hierarchy contains only `Directional Light`. Project window shows
`Assets/Scenes/SampleExperiment.unity` selected.

---

## 2. Add the camera rig

`HmdPoseDriverBootstrap`
(`Assets/Scripts/EyeTracking/Core/HmdPoseDriverBootstrap.cs:33`) attaches
a `TrackedPoseDriver` to `Camera.main` automatically at scene load,
binding `<XRHMD>/centerEyePosition` and `<XRHMD>/centerEyeRotation`. The
scene only needs a tagged camera; a parent rig transform is optional.

1. Create an empty parent: right-click in Hierarchy > **Create Empty**,
   name it `CameraRig`. Position `(0, 0, 0)`, rotation `(0, 0, 0)`.
2. Right-click `CameraRig` > **Camera**. The new child camera ships with
   a `Camera` component and `MainCamera` tag.
3. On the camera component:
   - **Clear Flags**: Solid Color, black
   - **Clipping Planes**: Near `0.1`, Far `100`

Do not add a `TrackedPoseDriver` manually; the bootstrap attaches one
on its own and skips the camera if a driver is already present
(`HmdPoseDriverBootstrap.cs:132`).

### Verify

Camera child has tag `MainCamera` and no `TrackedPoseDriver` component
in the Inspector. Parent rig's local rotation is identity — the
bootstrap zeroes any non-identity parent rotation at scene load
(`HmdPoseDriverBootstrap.cs:81`).

---

## 3. Add the eye-tracking stack

The post-RC pipeline is three components on a single GameObject:
`EyeTracker` (gaze + vergence math),
`HMDDataCollector` (camera-pose snapshots), `SessionRecorder` (CSV
writer + metadata API). `DataQualityMetrics` is optional but enables
quality-flag columns — `EyeTracker` auto-detects it
(`Assets/Scripts/EyeTracking/Components/EyeTracker.cs:190`).

1. Right-click in Hierarchy > **Create Empty**, name it
   `EyeTrackingManager`. Position `(0, 0, 0)`.
2. With it selected, **Add Component** for each:
   - `EyeTracker`
   - `HMDDataCollector`
   - `SessionRecorder`
   - `DataQualityMetrics` (optional)

Inspector fields default-resolve via `Awake()` lookups; no manual wiring
is required between these three.

### Verify

Press Play in the editor. Console shows
`[EyeTracker] DataQualityMetrics detected — quality tracking enabled`
when the optional component is present
(`EyeTracker.cs:191`). No `CRITICAL` log lines from `SampleExperimentController`'s
component-validation block
(`Assets/Scripts/Experiment/SampleExperimentController.cs:96`).

---

## 4. Add the environment generator

1. Right-click in Hierarchy > **Create Empty**, name it
   `EnvironmentGenerator`. Position `(0, 0, 0)`.
2. **Add Component > EnvironmentGenerator**.
3. In the Inspector, under **Eye Tracking Setup**, **uncheck**:
   - Configure Eye Tracker On Start
   - Set Coordinate Origin On Start

   `SampleExperimentController` drives both at the right moment in the
   phase sequence; leaving them on causes a duplicate
   `SetCoordinateOrigin` call before the trial starts
   (`Assets/Scripts/Experiment/Environment/EnvironmentGenerator.cs:86`).

Default room geometry (6 m × 6 m × 3 m, 12 static + 4 dynamic objects)
is set in `EnvironmentGenerator.cs:18`–`24`. Adjust only if a custom
room layout is needed.

### Verify

Toggle the **Set Coordinate Origin On Start** checkbox off. Inspector
no longer logs
`[CoordinateOriginInitializer] No HMDDataCollector available — origin not set`
on Play.

---

## 5. Add the experiment controller

1. Right-click in Hierarchy > **Create Empty**, name it
   `ExperimentManager`. Position `(0, 0, 0)`.
2. With it selected, **Add Component** for each:
   - `SampleExperimentController`
   - `VisualSearchManager`
   - `CountingTaskManager`
   - `ChangeDetectionManager`
   - `ExperimentUI`

`SampleExperimentController` auto-resolves its task-manager and UI
references via `GetComponent` on the same GameObject; cross-scene
references (`EyeTracker`, `SessionRecorder`, `EnvironmentGenerator`,
`Camera.main`) are auto-found at `Awake()`
(`SampleExperimentController.cs:86`–`93`). Manual Inspector
assignment is only needed if auto-detection logs a warning.

### Verify

Press Play. Console shows no `[Experiment] CRITICAL:` lines
(`SampleExperimentController.cs:96`–`108`). Pressing **Space**
advances out of the Idle phase (Idle → Instructions → FreeExploration).

---

## 6. Configure phase parameters (optional)

Defaults match the values used in published Eye_lean recordings; only
override these to tune for hardware or pilot runs. Each config is a
`[Serializable] struct` exposed on the controller
(`Assets/Scripts/Experiment/ExperimentPhase.cs:105`–`297`).

### Free Exploration (`explorationConfig`)

| Field | Default | Source |
|---|---|---|
| `duration` | 30.0 s | `ExperimentPhase.cs:118` |
| `staticObjectCount` | 12 | `ExperimentPhase.cs:119` |
| `dynamicObjectCount` | 4 | `ExperimentPhase.cs:120` |

### Visual Search (`visualSearchConfig`)

| Field | Default | Source |
|---|---|---|
| `trialCount` | 3 | `ExperimentPhase.cs:153` |
| `distractorCount` | 15 | `ExperimentPhase.cs:154` |
| `maxTrialDuration` | 15.0 s | `ExperimentPhase.cs:155` |
| `targetAcquisitionTime` | 1.5 s | `ExperimentPhase.cs:156` |

### Counting Task (`countingConfig`)

Duration, min/max target counts in `ExperimentPhase.cs:206`–`240`.

### Change Detection (`changeDetectionConfig`)

| Field | Default | Source |
|---|---|---|
| `trialCount` | 3 | `ExperimentPhase.cs:294` |
| `objectCount` | 12 | `ExperimentPhase.cs:295` |
| `studyDuration` | 8.0 s | `ExperimentPhase.cs:296` |
| `blankDuration` | 0.5 s | `ExperimentPhase.cs:297` |

### Participant settings

- `participantID` (default `"P001"`) — written to every CSV row.
- `promptForParticipantID` (default `true`) — shows an in-VR keypad
  prompt before the first phase.

For multi-participant runs, leave the prompt enabled. For unattended
batch runs, uncheck it and assign the ID via
`SampleExperimentController.SetParticipantID(string)`
(`SampleExperimentController.cs:549`).

### General

- `instructionDisplayTime` — seconds the per-phase instruction panel
  stays visible.
- `interPhaseDelay` — pause between phases.
- `autoStart` — start without waiting for **Space** / trigger press.

### Verify

Inspector reflects the changes after Play stops. Press Play, press
**Space**: console logs four `[Experiment] Phase: <name>` entries with
`<name>` ∈ {`FreeExploration`, `VisualSearch`, `CountingTask`,
`ChangeDetection`}.

---

## 7. Verify against the cloned repo

If the scene was cloned rather than rebuilt, confirm parity with:

```
SampleExperiment
├── Directional Light
├── CameraRig
│   └── Main Camera (tag MainCamera)
├── EyeTrackingManager
│   ├── EyeTracker
│   ├── HMDDataCollector
│   ├── SessionRecorder
│   └── DataQualityMetrics (optional)
├── EnvironmentGenerator
└── ExperimentManager
    ├── SampleExperimentController
    ├── VisualSearchManager
    ├── CountingTaskManager
    ├── ChangeDetectionManager
    └── ExperimentUI
```

The rig parent's GameObject name is irrelevant —
`HmdPoseDriverBootstrap` resolves the camera through `Camera.main`,
not by name. Existing scenes that use a different rig name work
unchanged.

### Verify

`File > Build Profiles > Scene List` includes
`Assets/Scenes/SampleExperiment.unity` at build index 2 (preceded by
`MainMenu` at 0 and `CalibrationScene` at 1). The
`HmdPoseDriverBootstrap` log
`[HmdPoseDriverBootstrap] Auto-attached TrackedPoseDriver to 'Main Camera'`
appears once per scene load
(`HmdPoseDriverBootstrap.cs:150`).

---

## 8. Add custom metadata fields (optional)

Custom per-trial variables flow into the CSV header automatically when
declared in `Awake()` before `SessionRecorder` writes its first row.
The full API and lifecycle rules are in
[`CUSTOM_METADATA_TUTORIAL.md`](CUSTOM_METADATA_TUTORIAL.md). Minimal
example:

```csharp
void Awake()
{
    sessionRecorder.DeclareMetadataField("Condition", MetadataValueType.String);
    sessionRecorder.DeclareMetadataField("Block", MetadataValueType.Int);
}

void StartTrial()
{
    sessionRecorder.SetMetadata("Condition", "Experimental");
    sessionRecorder.SetMetadata("Block", 1);
}
```

The metadata API lives on `SessionRecorder`
(`Assets/Scripts/Experiment/SampleExperimentController.cs:441`).

### Verify

Open the generated `EyeTracking_*.csv` after a run. The header row
contains `Condition` and `Block` columns.

---

## 9. Build, deploy, retrieve data

These steps mirror every other Eye_lean scene; the canonical procedure
lives in [`../../../docs/BUILD_GUIDE.md`](../../../docs/BUILD_GUIDE.md):

- Switch to Android, configure XR Plug-in Management for OpenXR + VIVE
  XR Support + Eye Gaze Interaction.
- Build APK, deploy with `adb install -r`.
- Pull data from
  `/sdcard/Android/data/<package>/files/` after the run.

For schema details (`ParticipantID`, `CurrentPhase`, `SubTask`,
`SessionConfig`, gaze + pupil columns) see
[`DATA_SCHEMA.md`](DATA_SCHEMA.md).

For replay of recorded sessions in the editor see
[`REPLAY_SYSTEM.md`](REPLAY_SYSTEM.md).

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `[Experiment] CRITICAL: EyeTracker component not found` | `EyeTrackingManager` missing or has no `EyeTracker` | Add the component (section 3) |
| `[Experiment] CRITICAL: SessionRecorder not found` | Same GameObject missing `SessionRecorder` | Add the component (section 3) |
| Camera frozen at scene's serialized pose | `HmdPoseDriverBootstrap` failed to find `Camera.main` | Confirm camera has `MainCamera` tag (section 2) |
| Room visibly tilted | Rig parent has non-identity local rotation | Bootstrap auto-corrects on next scene load (`HmdPoseDriverBootstrap.cs:81`); check console for `Leveling rig parent` log |
| Eye-tracking data all zeros | OpenXR Eye Gaze Interaction not enabled in XR Plug-in Management | See `BUILD_GUIDE.md` XR settings section |
| CSV missing custom-metadata columns | Field declared after recording began | Move `DeclareMetadataField` calls into `Awake()` (section 8) |

For the broader troubleshooting matrix see
[`../../../docs/TROUBLESHOOTING.md`](../../../docs/TROUBLESHOOTING.md).

---

## Related documentation

- [`../../../docs/SAMPLE_EXPERIMENT.md`](../../../docs/SAMPLE_EXPERIMENT.md) — worked-example walkthrough of what the controller exercises.
- [`../../../docs/BUILD_GUIDE.md`](../../../docs/BUILD_GUIDE.md) — APK build + deploy + retrieval.
- [`RESEARCHER_GUIDE.md`](RESEARCHER_GUIDE.md) — end-to-end experiment lifecycle.
- [`CUSTOM_METADATA_TUTORIAL.md`](CUSTOM_METADATA_TUTORIAL.md) — metadata API + schema asset.
- [`DATA_SCHEMA.md`](DATA_SCHEMA.md) — CSV column reference.
- [`../../../docs/SKELETON.md`](../../../docs/SKELETON.md) — developer-side template for building a custom experiment from scratch.
