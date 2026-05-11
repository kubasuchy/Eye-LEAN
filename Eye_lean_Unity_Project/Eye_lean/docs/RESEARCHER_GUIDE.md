# Eye_lean Researcher Guide

## What it is

An end-to-end manual for building, recording, and replaying VR
eye-tracking experiments with Eye_lean. The guide walks through
installation, a first recorded session, deterministic replay, building
a custom experiment on top of the toolkit, and the full output schema.

## Audience

Researchers running their first VR eye-tracking session. Some Unity
familiarity is assumed (creating scenes, scripts, components, building
APKs). For pure APK install + run flow with no Unity work, see
[`../../docs/BUILD_GUIDE.md`](../../docs/BUILD_GUIDE.md) and the
prebuilt-APK section there.

## Prerequisites

- Unity 6000.3.9f1
- VIVE OpenXR 2.5.1
- HTC VIVE Focus Vision (or another OpenXR-compliant headset that
  exposes `<XRHMD>/centerEyePosition` and the OpenXR eye-tracking
  action set)
- Android Build Support module installed in Unity Hub
- ADB on the development machine

---

## Contents

1. [Concepts at a glance](#concepts-at-a-glance)
2. [Installation & project setup](#installation--project-setup)
3. [Recording an experiment](#recording-an-experiment)
4. [Replaying a recording (deterministic)](#replaying-a-recording-deterministic)
5. [Building your own experiment on top of Eye_lean](#building-your-own-experiment-on-top-of-eye_lean)
6. [Output files and their schemas](#output-files-and-their-schemas)
7. [The deterministic-replay contract](#the-deterministic-replay-contract)
8. [Troubleshooting](#troubleshooting)
9. [Reference: components and assemblies](#reference-components-and-assemblies)

---

## Concepts at a glance

Eye_lean produces three CSV files per recorded session and replays
them inside the same Unity scene that produced them:

| File | Sidecar | What it carries |
| --- | --- | --- |
| `EyeTracking_<timestamp>.csv` | main | Per-frame head pose, eye gaze, vergence, gazed-object name, phase/sub-task labels, plus any researcher-declared metadata columns. |
| `EyeTracking_<timestamp>_SceneState.csv` | sidecar | Per-frame world-space transform + active-state for every `Recordable`-tagged GameObject. Long-format. |
| `EyeTracking_<timestamp>_SceneEvents.csv` | sidecar | Discrete events: instruction text shown/hidden, scenes spawned/cleared, results, plus an internal `RandomStateSnapshot` and per-phase `Config*` snapshots. |

On replay the toolkit re-runs the live experiment script against
recorded inputs:

- `Camera.main` is driven by recorded HMD pose.
- `EyeTrackerFactory.GetEyeTracker()` returns a `ReplayingEyeTracker`
  that serves recorded gaze samples.
- `UnityEngine.Random.state` is restored to the recorded snapshot
  before any phase coroutine runs.
- `GazeTarget.IsBeingGazedAt` is driven by a per-frame raycast over
  the recorded gaze, so dwell-driven gameplay (target acquisition,
  answer selection, change-detection picks) re-fires at the recorded
  cadence.

This is the deterministic-replay contract. An experiment that uses
only `UnityEngine.Random`, no wall-clock state, no network, and no
threads replays exactly. For the full constraints, see
[The deterministic-replay contract](#the-deterministic-replay-contract).

---

## Installation and project setup

### 1. Clone and open the project

1.1. Clone the repository:

```sh
git clone https://github.com/kubasuchy/EYE-LEAN.git
```

1.2. Open `Eye_lean_Unity_Project/Eye_lean` in Unity 6000.3.9f1 from
Unity Hub.

#### Verify

The Unity console reports no compile errors after the import finishes, and the
build-target scenes (`MainMenu`, `CalibrationScene`, `SampleExperiment`) appear
under `Assets/Scenes/`.

### 2. Confirm required Unity packages

The project's `Packages/manifest.json` already declares everything
needed. For reference, these packages must be present:

- `com.unity.xr.openxr` >= 1.16
- `com.unity.inputsystem` >= 1.18
- `com.htc.upm.vive.openxr` (VIVE Focus Vision integration)
- `com.unity.render-pipelines.universal` (URP)

#### Verify

Open **Window > Package Manager** and confirm each package is listed
under **In Project**.

### 3. Confirm project settings

1. Open **Edit > Project Settings > XR Plug-in Management**.
2. Enable **OpenXR** for Android (and for PC if testing in the editor
   with a tethered headset).
3. Open **Edit > Project Settings > Player > Other Settings**.
4. Set **Active Input Handling** to **Input System Package (New)**.
   Eye_lean uses the new Input System for HMD pose binding.
5. Open **Edit > Project Settings > Tags and Layers** and confirm the
   `Recordable` tag is present (shipped with the project).

#### Verify

The OpenXR runtime selector shows VIVE OpenXR and the **Eye Gaze
Interaction** feature is checked.

### 4. Confirm build settings

The default build flow ships three scenes, in this order:

| Build index | Scene | Purpose |
| --- | --- | --- |
| 0 | `MainMenu.unity` | Launcher: pick "Calibrate" or "Run experiment". |
| 1 | `CalibrationScene.unity` | Five-test eye-tracking calibration with per-axis offsets saved to a JSON profile. |
| 2 | `SampleExperiment.unity` | The four-phase reference experiment (FreeExploration / VisualSearch / CountingTask / ChangeDetection). |

For a custom experiment, swap scene 2 with the new scene. Do not move
`MainMenu` or `CalibrationScene` — the launcher and calibrator are the
gaze-quality safety net every recording starts from.

#### Verify

Open **File > Build Settings** and confirm the three scenes appear in
the listed order with the correct build indexes.

### 5. Confirm the eye-tracking infrastructure

Three components run on a persistent `EyeTrackingManager` GameObject
that is created in the calibration scene and carried through to the
experiment scene via `DontDestroyOnLoad`:

| Component | Role |
| --- | --- |
| `EyeTracker` (`Assets/Scripts/EyeTracking/Components/EyeTracker.cs`) | Polls the active `IEyeTracker` (live or replaying) each frame, computes vergence, owns the gaze raycast that sets `GazeTarget.IsBeingGazedAt`. |
| `HMDDataCollector` (`Assets/Scripts/EyeTracking/Components/HMDDataCollector.cs`) | Captures head pose and coord-frame origin per frame. |
| `SessionRecorder` (`Assets/Scripts/EyeTracking/Components/SessionRecorder.cs`) | Owns the main CSV writer, the session id, the frame counter, and the metadata schema. |

Two sibling components auto-bootstrap at scene load (no inspector
setup needed):

- `SceneEventRecorder` — subscribes to `SessionRecorder` and writes
  the `_SceneEvents.csv` sidecar.
- `SceneStateRecorder` — writes the `_SceneState.csv` sidecar; also
  requires a `SceneRecordingProfile` ScriptableObject (see step 6).

#### Verify

In `CalibrationScene`, the Hierarchy contains an `EyeTrackingManager`
GameObject with `EyeTracker`, `HMDDataCollector`, and
`SessionRecorder` as components.

### 6. Configure the `SceneRecordingProfile` ScriptableObject

A `SceneRecordingProfile` ScriptableObject tells `SceneStateRecorder`
which objects to capture per frame.

1. Create a profile via **Assets > Create > Eye_lean >
   SceneRecordingProfile**, or use the shipped default at
   `Assets/Settings/SceneRecordingProfile_SampleExperiment.asset`.
2. Set **Discovery mode** to `OrTag` and the tag to `Recordable` for
   "any object marked Recordable" (recommended default).
3. Set **`sampleEveryNthFrame`**: `1` = every frame (~90 Hz),
   `3` = ~30 Hz (smaller files, smooth enough for replay).
4. Toggle **`recordParentId`** ON to include parent transform id in
   each row (required if Recordables are parented under a moving rig).
5. Drop the profile asset on the `SceneStateRecorder` component's
   `Profile` slot.

#### Verify

The `SceneStateRecorder` component's `Profile` field shows the asset
name in the Inspector and is not `None`.

---

## Recording an experiment

A recording produces all three CSVs under
`/storage/emulated/0/Android/data/com.RutgersVCL.Eye_lean/files/` (on
Android) or the project's `DebugLogs/` folder (in editor).

### Workflow

1. Build the APK (or press Play in the editor with a connected HMD).
   For the build process, see
   [`../../docs/BUILD_GUIDE.md`](../../docs/BUILD_GUIDE.md).
2. Put on the headset. The app starts on `MainMenu`. Gaze-dwell on
   **Calibrate** to enter the calibrator.
3. Run all five calibration tests in order: Fixation, Smooth Pursuit,
   Saccade, Tuning, Verification. The calibrator writes a per-device
   offset profile to
   `<persistentDataPath>/EyeLeanProfiles/<device>_<timestamp>.json`
   and marks it the default profile.
4. Return to `MainMenu` and gaze-dwell on **Start Experiment**.
5. Run the experiment. CSVs flush periodically and on app quit or
   scene change.
6. Pull the files off the headset:

   ```sh
   adb pull /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/ ./eye_lean_data/
   ```

#### Verify

The pulled directory contains a matching trio:
`EyeTracking_<timestamp>.csv`, `EyeTracking_<timestamp>_SceneState.csv`,
and `EyeTracking_<timestamp>_SceneEvents.csv`. Each main CSV has a
`# CoordinateOriginSet: True` header line (set when the experiment
anchors the world origin).

### What is recorded

The three CSVs share the same `<timestamp>` prefix and are joinable on
the `Frame` column. Schemas are documented in
[`DATA_SCHEMA.md`](./DATA_SCHEMA.md); the high-level summary:

- **Main CSV** has 90+ columns including `UnityTimestamp`, `Frame`,
  `Phase`, `SubTask`, head pose (XYZ + quaternion), per-eye gaze
  origins/directions, vergence point, blink state, gaze velocity,
  fixation/saccade flags, K-coefficient, and any `Gaze_<ObjectName>`
  boolean columns for objects in the `ObjectTrackingRegistry`.
- **`_SceneState.csv`** is long-format: `Frame, T, ObjectId, Pos_X/Y/Z,
  Rot_X/Y/Z/W, Active[, ParentId]`. One row per Recordable per sampled
  frame.
- **`_SceneEvents.csv`** is also long-format: `Frame, T, EventType,
  ObjectId, Detail`. EventType is a free-form string. Reserved internal
  types: `Spawn`, `Despawn`, `IdRegenerated`, `RandomStateSnapshot`,
  `ConfigExploration`, `ConfigVisualSearch`, `ConfigCounting`,
  `ConfigChangeDetection`. Anything else is researcher-emitted.

### Setting a participant ID

Before starting an experiment, set the participant ID one of two ways:

1. In the Inspector, fill the `SampleExperimentController.participantID`
   field
   (`Assets/Scripts/Experiment/SampleExperimentController.cs`).
2. From code, call `sessionRecorder.SetParticipantID("P042")` from the
   controller's `Awake`.

If neither is set, the recording uses `"P001"` as a placeholder and
logs a warning.

#### Verify

The main CSV's `# ParticipantID:` header line matches the configured
ID.

### Adding metadata columns

Researcher-defined columns join the main CSV via:

```csharp
sessionRecorder.DeclareMetadataField("MyMetricName"); // before recording
sessionRecorder.SetMetadata("MyMetricName", value);   // any time during
```

`DeclareMetadataField` must be called before the first sample lands
in the CSV — typically from the controller's `Awake`. For details, see
[`CUSTOM_METADATA_TUTORIAL.md`](./CUSTOM_METADATA_TUTORIAL.md).

#### Verify

The main CSV's column header includes `MyMetricName` and rows after
the first `SetMetadata` call carry the value.

---

## Replaying a recording (deterministic)

### Editor-only

Replay runs only in the Unity Editor. The replay path is not packaged
into the APK.

### 1. Open the experiment scene and add the replay rig

Open the same scene you recorded in (e.g. `SampleExperiment.unity`). Replay is
not a separate scene — it re-runs the live experiment in place, so the
controller, environment generator, UI, and task managers all need to be present
exactly as they were during recording.

Add a single GameObject at the scene root with these components:

- `ReplayController` (`Assets/Scripts/Replay/ReplayController.cs`) — the
  playback engine; auto-attaches the siblings listed below.
- `DemoReplayBootstrapper` (`Assets/Scripts/Replay/DemoReplayBootstrapper.cs`)
  — anchors the room to the recording's coord-origin and auto-plays after load.

The SampleExperiment scene ships with this rig already wired; for your own
scene, drop it in once and it persists.

Auto-attached siblings (no manual setup):

- `SceneStateReplayer` — drives transforms of live Recordables from
  `_SceneState.csv` per frame.
- `SceneEventReplayer` — applies `RandomStateSnapshot` and any
  researcher-registered event handlers.
- `ReplayGazeRaycaster` — drives `GazeTarget.IsBeingGazedAt` from recorded
  gaze.

### 2. Configure the inspector

On `ReplayController`:

- **Data File Path** — absolute path or `StreamingAssets`-relative
  path to the main CSV (`EyeTracking_<timestamp>.csv`). Sidecars are
  found automatically by appending `_SceneState.csv` and
  `_SceneEvents.csv` to the basename.
- **Auto Load On Start** — ON (forced on in the editor).
- **Auto Play On Load** — ON (forced on in the editor).
- **Deterministic Replay** — ON (forced on in the editor).
- **First Person Camera** — ON, to drive `Camera.main` from recorded
  HMD pose.

On `DemoReplayBootstrapper`:

- **Anchor To Recording** — ON. Without this, the room generates
  against the editor-camera frame and recorded gaze rays do not
  match the scene geometry.
- **Auto Play After Bootstrap** — ON.

### 3. Press Play

The expected log sequence is:

```
[ReplayController] Loaded N frames, duration: T.TTs
[ReplayController] Deterministic replay: installed ReplayingEyeTracker as factory override; ...
[DemoReplayBootstrapper] Anchored env to recording: pos=..., yaw=...
[DemoReplayBootstrapper] Room rebuilt at recorded anchor (anchored=True). ...
[SceneEventReplayer] Loaded M events ...
[SceneEventReplayer] Applied recorded UnityEngine.Random.state snapshot.
[ReplayController] State: Playing
[Experiment] Started gazing at start target
[Experiment] === Starting experiment for participant: '...' ===
[Experiment] Phase: Instructions
[Experiment] Phase: FreeExploration
... (each phase runs its full coroutine, ending when the recorded participant ended it)
[Experiment] Phase: Complete
[Experiment] Complete. Duration: X.Xs
```

#### Verify

The replay duration matches the original recording duration to within
a few percent (clock drift between the recording's ~90 Hz cadence and
the editor's native rate). The console shows
`[Experiment] Complete. Duration: ...` at the end.

---

## Building a custom experiment on top of Eye_lean

The goal: drop a custom scene into the build at index 2 in place of
`SampleExperiment`, and recording and replay work without further
glue. Two rules are required.

### Rule 1: Tag every relevant object as Recordable

Any object whose position or visibility should be captured (and
reproduced on replay) needs a `Recordable` component. Two ways to add
it:

```csharp
// Edit-time: add the Recordable component in the inspector. Each
// instance auto-generates a stable serialized GUID.

// Runtime spawn:
var go = Instantiate(prefab);
EyeLean.SceneState.SceneStateRecorder.MarkRecordable(go);

// Runtime spawn with cross-session-stable id (RECOMMENDED for replay):
EyeLean.SceneState.SceneStateRecorder.MarkRecordableSeeded(go, $"MyTarget_{trial}_{slot}");
```

The seeded variant is required for deterministic replay of
runtime-spawned objects: same seed produces the same MD5-derived GUID
across sessions. The `SampleExperiment` task managers already follow
this pattern — copy it.

### Rule 2: Route participant-facing UI through `ExperimentUI` or an equivalent auto-recording class

`ExperimentUI`
(`Assets/Scripts/Experiment/UI/ExperimentUI.cs`) is the reference. Every
public UI method (`ShowInstruction`, `HideInstruction`,
`SetInstructionTextOnly`, `ShowProgress`, `HideProgress`) records
itself via `SceneEventRecorder.Record(...)` automatically when the
`autoRecordEvents` toggle is ON (default).

For a custom UI class, mirror the pattern:

```csharp
public void MyUI_ShowInstruction(string text)
{
    EyeLean.SceneState.SceneEventRecorder.Record("ShowInstruction", "", text);
    // ...live UI update...
}
```

On replay, the live experiment runs the same call paths against
recorded inputs and reproduces the recording. Replay-side handlers
are not required — the live methods are the replay handlers.

### Optional: emit researcher-defined events

For events that should be explicitly preserved (audio cues, custom
feedback moments, anything not driven by standard UI calls), emit
them directly:

```csharp
// Plain string detail
SceneEventRecorder.Record("CueOnset", objectId: "", detail: "tone_500hz_200ms");

// Structured key-value payload (survives CSV sanitization via auto-encoded format)
SceneEventRecorder.RecordKV("AnswerSelected", "",
    ("value", value.ToString()),
    ("rt", responseTime.ToString("F3")));

// JSON payload (Base64-encoded into the Detail column, decode with DecodeJson<T>)
SceneEventRecorder.RecordJson("CustomConfig", "", myConfigStruct);
```

Reserved internal types — do not emit these manually:
`Spawn`, `Despawn`, `IdRegenerated`, `RandomStateSnapshot`,
`ConfigExploration`, `ConfigVisualSearch`, `ConfigCounting`,
`ConfigChangeDetection`.

### Optional: real-time cognitive-load monitor (RIPA2)

Eye_lean ships a `RIPAMonitor`
(`Assets/Scripts/EyeTracking/Metrics/RIPAMonitor.cs`) that computes a
per-frame cognitive-load index from the live pupil signal. It
auto-spawns in every scene, so a `LiveLoadIndex` column appears in
the recorded CSV with no additional setup. To read it from custom
code:

```csharp
if (EyeTracking.Metrics.RIPAMonitor.Instance is { IsValid: true } m)
{
    float load = m.CurrentLoad; // smoothed RIPA2, clipped to [0, 1.5]
}
```

For the gauge prefab, opting out of the auto-bootstrap, custom CSV
columns via `SessionRecorder.RegisterMetric`, and the Python
`eyelean_analysis.metrics.ripa2` parity layer, see
[`docs/RIPA_MONITOR.md`](../../../docs/RIPA_MONITOR.md).

### Optional: register replay-side handlers

Most researchers will not need this — deterministic replay handles
reproduction by re-running the live code. The exception is replay-only
diagnostics or a non-deterministic experiment that needs explicit
event playback:

```csharp
private void OnEnable()
{
    SceneEventReplayer.RegisterHandler("CueOnset",
        row => myAudioSource.PlayOneShot(/* lookup by row.Detail */));
}

private void OnDisable()
{
    SceneEventReplayer.UnregisterHandler("CueOnset", /* same delegate */);
}
```

Handlers fire on the recording's frame, so the audio plays at exactly
the recorded cue moment.

---

## Output files and their schemas

### `EyeTracking_<timestamp>.csv` (main)

Comment header lines:

```
# Eye_lean Research Data Export
# FileVersion: 1.1
# SessionID: <yyyymmdd_hhmmss_xxxxxxxx>
# ParticipantID: <id>
# Profile: <ProfileName | "none">
# CoordinateOrigin: <x>,<y>,<z>
# CoordinateOriginSet: True | False
```

Then a column header line, then one data row per frame. Full column
catalog in [`DATA_SCHEMA.md`](./DATA_SCHEMA.md).

### `EyeTracking_<timestamp>_SceneState.csv`

```
# Eye_lean Scene State Sidecar
# FileVersion: 1.0
# SessionID: <same as main>
# CoordinateOrigin: <x>,<y>,<z>
# CoordinateOriginSet: True | False
# SampleEveryNthFrame: 1
# Profile: <SceneRecordingProfile name>
Frame,T,ObjectId,Pos_X,Pos_Y,Pos_Z,Rot_X,Rot_Y,Rot_Z,Rot_W,Active[,ParentId]
```

Positions are normalized to `CoordinateOrigin` (subtract origin from
the recorded camera's hardware-world position). Replay de-normalizes
on apply.

### `EyeTracking_<timestamp>_SceneEvents.csv`

```
# Eye_lean Scene Events Sidecar
# FileVersion: 1.0
# SessionID: <same>
Frame,T,EventType,ObjectId,Detail
```

Detail format depends on event type:

- Plain text events: `ShowInstruction`, `Spawn`, `Despawn` use the
  Detail column for human-readable content (instruction text, GameObject
  name).
- KV events: `AnswerSelected`, `ChangeDetectionFeedback`,
  `Spawn*Scene` use `key1=value1;key2=value2` format.
- JSON events: `Config*`, `RandomStateSnapshot` use Base64-encoded
  payloads.

---

## The deterministic-replay contract

For replay to faithfully reproduce a recording, the experiment must be
deterministic with respect to:

| Input | Replay source |
| --- | --- |
| HMD pose | Recorded head pose, written directly to `Camera.main` each frame. |
| Eye gaze | `IEyeTracker` is replaced by `ReplayingEyeTracker`. |
| `UnityEngine.Random` | `Random.state` is restored to a recorded snapshot at session start. |
| `Time.time` / `Time.deltaTime` | Real-time editor clock (the recording was real-time too, so coroutines run at matching cadence). |
| `GazeTarget.IsBeingGazedAt` | Driven by `ReplayGazeRaycaster` from recorded gaze. |

### What will NOT replay deterministically

- Code that uses `System.Random`, `System.DateTime.Now.Ticks`, or
  external entropy as a seed (a different sequence each session).
- Code that uses wall-clock-derived `Time.time` math without
  `WaitForSeconds` in a way that depends on editor framerate (rare;
  coroutines are fine).
- Multi-threaded code with non-deterministic ordering.
- Network calls, file I/O races, third-party SDK internal state.
- Animator state machines whose transitions depend on timing-sensitive
  exit-time conditions (subtle drift can fire transitions on different
  frames).

### What WILL replay deterministically

- Coroutines using `WaitForSeconds`, `WaitForEndOfFrame`, and similar
  yields.
- `UnityEngine.Random.Range`, `Random.value`, `Random.insideUnitSphere`,
  and any `UnityEngine.Random` API.
- Physics raycasts, given the same scene geometry (which
  `_SceneState.csv` ensures).
- All UI code that goes through `ExperimentUI` or any class that calls
  `SceneEventRecorder.Record`.
- `GazeTarget`-driven gameplay: target acquisition, dwell selection,
  pick detection.

### Rule of thumb

If the experiment is reproducible by re-running it locally with the
same participant input (for example via a unit test), it replays
deterministically.

---

## Troubleshooting

### "EyeTracker currently looking at: 'null'" forever in replay

The scene is missing the live `EyeTracker` MonoBehaviour (normally carried over
from the calibration scene via `DontDestroyOnLoad`, which editor-only replay
does not load). `ReplayGazeRaycaster` auto-bootstraps to drive
`GazeTarget.IsBeingGazedAt` directly. Confirm in the log:

```
[ReplayGazeRaycaster] Auto-attached to '<GameObject>' (sibling of ReplayController).
```

If that line is absent, the `ReplayController` GameObject is not in
the scene at scene-load time. Move it to the scene root and confirm
no inactive parent.

### Replay desyncs — instructions appear at wrong moments

Common causes:

- `Deterministic Replay` is OFF on `ReplayController`. The editor
  forces it ON; if that path is bypassed, real-time playback drifts.
- The `dataFilePath` points at the wrong recording; the participant
  ID or session ID does not match. Double-check the path.
- `Random.state` was not snapshotted during recording (occurs when
  `SceneEventRecorder` was disabled at recording time). Check the
  events sidecar for a `RandomStateSnapshot` row at frame 2.

### Placeholder spheres flicker during replay

This should not happen in deterministic mode (it is hard-gated off).
If it does:

- `ReplayController.deterministicReplay` is `false`. Confirm the
  toggle is ON.
- The recording predates seeded ids (v1.1 or earlier). Re-record
  under v1.2+.

To intentionally show placeholders for legacy recordings, set
`Deterministic Replay = false` and `Spawn Placeholders For Missing
Ids = true` on `SceneStateReplayer`.

### "factory tracker='None' available=False" in GAZE DIAGNOSTIC

The `ReplayingEyeTracker` override did not install. Confirm the log
contains:

```
[ReplayController] Deterministic replay: installed ReplayingEyeTracker as factory override; ...
```

If the line is absent, either `ReplayController.Deterministic Replay`
is OFF or `autoLoadOnStart` is OFF (the override only installs when a
recording is loaded).

### Camera does not move with recorded head pose

`HmdPoseDriverBootstrap`
(`Assets/Scripts/EyeTracking/Core/HmdPoseDriverBootstrap.cs`) skips
installing a TrackedPoseDriver in replay mode (otherwise it would
race the recorded-pose writes).
`ReplayController.firstPersonCamera` must be ON for the controller to
write camera pose itself. Toggle it on the Replay scene's
`ReplayController`.

### Experiment hangs on "Look at the GREEN sphere to start"

The recorded participant's gaze is not reaching the start target's
collider. Diagnose with the `[Experiment] GAZE DIAGNOSTIC` log,
which prints both the live raycast result and the factory-tracker's
reported gaze. Common causes:

- The start target was created at the live camera position while the
  camera was still at default `(0, 1.5, 0)` instead of the recorded
  position. The controller's `ShowIdleMessageDelayed` waits for VR
  readiness — if `DemoReplayBootstrapper` has not anchored before the
  wait completes, the target spawns at the wrong place. Confirm
  `Anchor To Recording = ON`.
- The start target is on a layer the raycast does not hit. The default
  layer works; custom layers should be on the `-1` (everything) mask.

### Replay file paths do not auto-resolve

`ReplayController.dataFilePath` accepts:

- Absolute paths (`/Users/.../EyeTracking_xxx.csv`)
- Project-relative paths (`Assets/StreamingAssets/EyeTracking_xxx.csv`)
- Paths relative to `Application.streamingAssetsPath` (the filename
  alone if it lives in `StreamingAssets`)
- Paths relative to `Application.persistentDataPath`

Sidecar paths are derived by appending `_SceneState.csv` and
`_SceneEvents.csv` to the basename.

---

## Reference: components and assemblies

### Recording side (`EyeLean.Core` assembly)

| File | Role |
| --- | --- |
| `EyeTrackerFactory.cs` | Static factory returning the active `IEyeTracker`. Replay installs an override here. |
| `IEyeTracker.cs` | Per-frame gaze-data interface implemented by all tracker providers. |
| `OpenXREyeTracker.cs`, `Providers/OpenXREyeTrackerProvider.cs` | VIVE OpenXR backend. |
| `EyeTracker.cs` (Components/) | High-level MonoBehaviour: polls `IEyeTracker`, computes vergence, raycasts to set `currentGazedObject` + `GazeTarget.IsBeingGazedAt`. |
| `HMDDataCollector.cs` | Per-frame head-pose snapshot, coord-origin event. |
| `SessionRecorder.cs` | Main CSV writer + frame counter + session id + metadata schema. |
| `Recordable.cs` | Marker component carrying a stable id; subscribes to `RecordableRegistry`. |
| `RecordableRegistry.cs` | Static registry of live Recordables. Fires `Changed` events on enable/disable/id-collision. |
| `SceneStateRecorder.cs` | Per-frame transform sidecar writer. |
| `SceneEventRecorder.cs` | Discrete-event sidecar writer + `Record / RecordKV / RecordJson` static API + `EncodeRandomState` / `TryDecodeAndApplyRandomState` for deterministic replay. |
| `HmdPoseDriverBootstrap.cs` | Auto-attaches `TrackedPoseDriver` to `Camera.main` (suppresses in replay mode). |

### Replay side (`EyeLean.Replay` assembly)

| File | Role |
| --- | --- |
| `ReplayController.cs` | Main playback engine: load CSV, advance frames, drive camera pose, fire `OnLoadComplete` / `OnFrameDisplayed` / `OnPhaseChanged`. |
| `ReplayManager.cs` | High-level wrapper for filtering by phase/sub-task. |
| `EyeLeanCSVParser.cs` | Main-CSV parser with column aliasing. |
| `ReplayingEyeTracker.cs` | `IEyeTracker` impl reading from `ReplayController.CurrentFrame`. Installed as factory override at LoadDataCoroutine end. |
| `ReplayGazeRaycaster.cs` | Per-frame raycast that drives `GazeTarget.OnGazeEnter/OnGazeExit` from recorded gaze. |
| `ReplayClock.cs` | Time-pinning scaffolding (currently unused — replay runs at real time). |
| `SceneStateReplayer.cs` | Per-frame transform application from `_SceneState.csv` to live Recordables. Placeholders disabled in deterministic mode. |
| `SceneEventReplayer.cs` | Parses `_SceneEvents.csv`. Applies `RandomStateSnapshot` eagerly. Dispatches researcher-defined events to registered handlers. |

### Demo / sample experiment (`EyeLean.SampleDemo` assembly)

| File | Role |
| --- | --- |
| `SampleExperimentController.cs` | The 4-phase reference experiment. Read this for the canonical "wrap your UI calls + spawn calls + record events" pattern. |
| `EnvironmentGenerator.cs` | Builds the demo room + spawns gaze targets. Calls `MarkRecordableSeeded` for each spawn. |
| `Tasks/CountingTaskManager.cs`, `Tasks/VisualSearchManager.cs`, `Tasks/ChangeDetectionManager.cs` | Per-task scene managers. Each emits a `Spawn*Scene` event from its `GenerateScene` method. |
| `Tasks/CountingAnswerUI.cs` | Gaze-dwell answer-selection UI. Records `ShowCountingAnswerUI`, `CountingAnswerSelected`, `HideCountingAnswerUI`. |
| `UI/ExperimentUI.cs` | Instruction / progress / phase-indicator world-space panel. Auto-records every public method when `autoRecordEvents = true`. |
| `DemoReplayBootstrapper.cs` | Anchors the room to the recording's coord-origin and auto-plays after CSV load. |

### Scene flow

```
build 0   MainMenu.unity
build 1   CalibrationScene.unity   (carries EyeTrackingManager → SampleExperiment via DontDestroyOnLoad)
build 2   SampleExperiment.unity   (or your scene; also where replay runs in the editor
                                   via ReplayController + DemoReplayBootstrapper)
```

---

## Where to go from here

- **Schema reference**: [`DATA_SCHEMA.md`](./DATA_SCHEMA.md)
- **Scene-state replay deep-dive**: [`REPLAY_SCENE_STATE.md`](./REPLAY_SCENE_STATE.md)
- **Replay system architecture**: [`REPLAY_SYSTEM.md`](./REPLAY_SYSTEM.md)
- **Custom metadata columns**: [`CUSTOM_METADATA_TUTORIAL.md`](./CUSTOM_METADATA_TUTORIAL.md)
- **Sample experiment walkthrough**: [`SAMPLE_EXPERIMENT_SETUP.md`](./SAMPLE_EXPERIMENT_SETUP.md)
- **Architecture overview**: [`ARCHITECTURE.md`](./ARCHITECTURE.md)
- **Algorithms (vergence, fixation, K-coefficient)**: [`ALGORITHMS.md`](./ALGORITHMS.md)
- **Bibliography (the science behind the metrics)**: [`BIBLIOGRAPHY.md`](./BIBLIOGRAPHY.md)

To report a bug or request a feature, open an issue on GitHub. Pull
requests are welcome — see [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md).
