# Data Schema

Complete documentation of all data fields collected by the VR Eye Tracking Research Toolkit.

---

## Overview

The toolkit collects **84+ fields per sample** at frame rate (typically 72-90 Hz for VR, with eye tracking at up to 120 Hz on supported devices). Data is exported in CSV format with dynamic columns for scene object gaze intersections.

**Source**: `ResearchDataStructure.cs`

---

## CSV File Format

### File Naming Convention
```
EyeTracking_YYYYMMDD_HHMMSS.csv
```

Example: `EyeTracking_20241212_143052.csv`

### File Location (Android/VIVE Focus)
```
/storage/emulated/0/Android/data/com.RutgersVCL.Eye_lean/files/
```

---

## Field Categories

### 1. Timing Data (5 fields)

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `UnityTimestamp` | float | seconds | Unity's `Time.time` - time since scene loaded |
| `RealTimeSinceStartup` | float | seconds | Unity's `Time.realtimeSinceStartup` - unaffected by time scale |
| `SystemTimestamp` | long | ticks | `System.DateTime.UtcNow.Ticks` - 100-nanosecond intervals since epoch |
| `FrameNumber` | int | count | Frame count since application start |
| `DeltaTime` | float | seconds | Time since previous sample |

**Usage Notes**:
- Use `SystemTimestamp` for synchronization with external systems
- Use `UnityTimestamp` for in-session timing
- `DeltaTime` useful for detecting frame drops

---

### 2. Session Context (6 fields)

| Field | Type | Values | Description |
|-------|------|--------|-------------|
| `ParticipantID` | string | "P001", "participant_12", etc. | Unique participant identifier |
| `TrialNumber` | int | 0, 1, 2, 3... | Current trial/block number |
| `CurrentPhase` | string | "CalibrationCheck", "VisualSearch", etc. | Main experimental phase |
| `SubTask` | string | "point_0", "trial_1", etc. | Specific sub-task within the phase |
| `SessionConfig` | string | "SampleExperiment", etc. | General session configuration string |
| `IsDebugMode` | bool | true/false | Debug mode status (affects visualization) |

**Usage Notes**:
- Set participant ID via `SessionRecorder.SetParticipantID("P001")`
- Set session context via `SessionRecorder.SetSessionContext(trialNum, phase, config, subTask)`
- Set just the sub-task via `SessionRecorder.SetSubTask("trial_1")`
- `CurrentPhase` identifies the main experimental phase (e.g., "VisualSearch")
- `SubTask` identifies specific tasks within that phase (e.g., "trial_1", "trial_2")

**Example CSV Values**:
```
ParticipantID,TrialNumber,CurrentPhase,SubTask,SessionConfig,IsDebugMode
P001,0,CalibrationCheck,point_0,SampleExperiment,false
P001,0,CalibrationCheck,point_1,SampleExperiment,false
P001,0,VisualSearch,trial_1,SampleExperiment,false
P001,0,VisualSearch,trial_2,SampleExperiment,false
```

---

### 3. Head Tracking (16 fields)

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `HeadPos_X` | float | meters | Head X position in world space |
| `HeadPos_Y` | float | meters | Head Y position in world space |
| `HeadPos_Z` | float | meters | Head Z position in world space |
| `HeadRot_X` | float | - | Quaternion X component |
| `HeadRot_Y` | float | - | Quaternion Y component |
| `HeadRot_Z` | float | - | Quaternion Z component |
| `HeadRot_W` | float | - | Quaternion W component |
| `HeadForward_X` | float | - | Forward vector X (normalized) |
| `HeadForward_Y` | float | - | Forward vector Y (normalized) |
| `HeadForward_Z` | float | - | Forward vector Z (normalized) |
| `HeadRight_X` | float | - | Right vector X (normalized) |
| `HeadRight_Y` | float | - | Right vector Y (normalized) |
| `HeadRight_Z` | float | - | Right vector Z (normalized) |
| `HeadUp_X` | float | - | Up vector X (normalized) |
| `HeadUp_Y` | float | - | Up vector Y (normalized) |
| `HeadUp_Z` | float | - | Up vector Z (normalized) |

**Coordinate System**: Unity left-handed (Y-up, Z-forward)

---

### 4. Combined Eye Data (8 fields)

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `CombinedOrigin_X` | float | meters | Cyclops gaze origin X |
| `CombinedOrigin_Y` | float | meters | Cyclops gaze origin Y |
| `CombinedOrigin_Z` | float | meters | Cyclops gaze origin Z |
| `CombinedDir_X` | float | - | Cyclops gaze direction X (normalized) |
| `CombinedDir_Y` | float | - | Cyclops gaze direction Y (normalized) |
| `CombinedDir_Z` | float | - | Cyclops gaze direction Z (normalized) |
| `HasCombinedOrigin` | bool | true/false | Data validity flag |
| `HasCombinedDirection` | bool | true/false | Data validity flag |

**Usage Notes**:
- Combined gaze is typically more stable for basic gaze tracking
- Use per-eye data for vergence analysis

---

### 5. Left Eye Data (15 fields)

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `LeftOrigin_X` | float | meters | Left eye gaze origin X |
| `LeftOrigin_Y` | float | meters | Left eye gaze origin Y |
| `LeftOrigin_Z` | float | meters | Left eye gaze origin Z |
| `LeftDir_X` | float | - | Left eye gaze direction X (normalized) |
| `LeftDir_Y` | float | - | Left eye gaze direction Y (normalized) |
| `LeftDir_Z` | float | - | Left eye gaze direction Z (normalized) |
| `LeftOpenness` | float | 0.0-1.0 | Eye openness (0=closed, 1=fully open) |
| `LeftPupilDiameter` | float | mm | Pupil diameter in millimeters |
| `LeftPupilPos_X` | float | 0.0-1.0 | Pupil X position in sensor area |
| `LeftPupilPos_Y` | float | 0.0-1.0 | Pupil Y position in sensor area |
| `HasLeftOrigin` | bool | true/false | Data validity flag |
| `HasLeftDirection` | bool | true/false | Data validity flag |
| `HasLeftOpenness` | bool | true/false | Data validity flag |
| `HasLeftPupilDiameter` | bool | true/false | Data validity flag |
| `HasLeftPupilPosition` | bool | true/false | Data validity flag |

---

### 6. Right Eye Data (15 fields)

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `RightOrigin_X` | float | meters | Right eye gaze origin X |
| `RightOrigin_Y` | float | meters | Right eye gaze origin Y |
| `RightOrigin_Z` | float | meters | Right eye gaze origin Z |
| `RightDir_X` | float | - | Right eye gaze direction X (normalized) |
| `RightDir_Y` | float | - | Right eye gaze direction Y (normalized) |
| `RightDir_Z` | float | - | Right eye gaze direction Z (normalized) |
| `RightOpenness` | float | 0.0-1.0 | Eye openness (0=closed, 1=fully open) |
| `RightPupilDiameter` | float | mm | Pupil diameter in millimeters |
| `RightPupilPos_X` | float | 0.0-1.0 | Pupil X position in sensor area |
| `RightPupilPos_Y` | float | 0.0-1.0 | Pupil Y position in sensor area |
| `HasRightOrigin` | bool | true/false | Data validity flag |
| `HasRightDirection` | bool | true/false | Data validity flag |
| `HasRightOpenness` | bool | true/false | Data validity flag |
| `HasRightPupilDiameter` | bool | true/false | Data validity flag |
| `HasRightPupilPosition` | bool | true/false | Data validity flag |

---

### 7. System Status (2 fields)

| Field | Type | Values | Description |
|-------|------|--------|-------------|
| `IsEyeTrackingAvailable` | bool | true/false | Hardware available and initialized |
| `IsTrackingValid` | bool | true/false | Current frame has valid tracking |

---

### 8. Vergence Data (5 fields)

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `VergencePoint_X` | float | meters | Calculated vergence point X |
| `VergencePoint_Y` | float | meters | Calculated vergence point Y |
| `VergencePoint_Z` | float | meters | Calculated vergence point Z |
| `VergenceQuality` | float | meters | Distance between ray intersections (lower = better) |
| `HasValidVergence` | bool | true/false | Vergence calculation validity |

**Usage Notes**:
- Quality < 0.1m indicates high confidence
- Quality > 1.0m suggests tracking issues
- See [ALGORITHMS.md](ALGORITHMS.md) for calculation methods

---

### 9. Dynamic Object Gaze Intersections (Variable)

| Field Pattern | Type | Values | Description |
|---------------|------|--------|-------------|
| `Gaze_{ObjectName}` | bool | true/false | Whether gaze ray intersects object |

**Examples**:
- `Gaze_Door_01`
- `Gaze_Table_02`
- `Gaze_Target_Center`

**Notes**:
- Columns are generated dynamically based on scene objects with colliders
- Uses combined gaze ray for intersection testing
- Max raycast distance: 50 meters

---

### 10. Performance Data (3 fields)

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `CurrentFPS` | float | frames/sec | Current frame rate |
| `FrameTimeMs` | float | milliseconds | Time to render current frame |
| `DataSampleCount` | int | count | Total samples collected in session |

---

### 11. Cognitive Load (6 fields, v1.0.1+)

Registered between `DataSampleCount` and `GazedObjectName` by
`RIPACSVColumn`. All values smoothed and clipped to `[0, 1.5]` except
`LiveLoadIndex_BW_Raw` (raw LF/HF ratio).

| Field | Type | Description |
|-------|------|-------------|
| `LiveLoadIndex` | float | Alias of the displayed detector's smoothed value (back-compat with v1.0–v1.3 tooling). |
| `LiveLoadIndex_RIPA2` | float | RIPA2 smoothed (Jayawardena, Jayawardana & Gwizdka 2025). Default HUD method. |
| `LiveLoadIndex_BW` | float | Butterworth IIR LF/HF smoothed (Duchowski 2026 Listing 3). |
| `LiveLoadIndex_BW_Raw` | float | Butterworth raw LF/HF power ratio (uncapped within `[0, lfHfMaxRatio]`). |
| `LiveLoadIndex_FFT` | float | FFT periodogram LF/HF smoothed (Duchowski 2026 Listing 1). Zero for the first ~34 s of a session (warm-up). |
| `LiveLoadIndex_DWT` | float | db4 DWT LF/HF energy ratio smoothed (Duchowski 2026 Listing 2). Same warm-up as FFT. |

See [docs/RIPA_MONITOR.md](../../../docs/RIPA_MONITOR.md) for the
detector pipelines, the device-bandwidth caveat for LF/HF methods on
HMD eye trackers, and the inspector knobs for the cap / smoothing
scale.

---

## Data Quality Indicators

### Validity Flags

Each data category has associated `Has*` boolean flags indicating data validity. Always check these before using the corresponding values.

```python
# Example: Python filtering for valid vergence data
df_valid = df[df['HasValidVergence'] == True]
```

### Missing Data

- Invalid data points retain their previous values (no interpolation)
- Validity flags should be used to filter data
- Recommended: Discard samples where critical flags are `false`

---

## Coordinate Systems

### Unity World Space
- **Handedness**: Left-handed
- **Up axis**: Y
- **Forward axis**: Z
- **Units**: Meters

### Pupil Position
- **Range**: 0.0 to 1.0 (normalized)
- **Origin**: Top-left of sensor area
- **X**: Horizontal (0=left, 1=right)
- **Y**: Vertical (0=top, 1=bottom)

---

## Sample Rate

| Device | Eye Tracking Rate | Typical Frame Rate |
|--------|------------------|-------------------|
| VIVE Focus Vision | 120 Hz | 72-90 Hz |
| Varjo XR-3 | 200 Hz | 90 Hz |
| HoloLens 2 | 30 Hz | 60 Hz |

**Note**: CSV samples are written at Unity frame rate, not eye tracker native rate.

---

## Example Data Row

```csv
1.234,1.234,637800123456789,100,0.0111,1,Navigation,AgentsLeft,false,-0.5,1.6,0.2,0.0,0.707,0.0,0.707,...
```

---

## Loading Data

### Python (pandas)

```python
import pandas as pd

df = pd.read_csv('EyeTracking_20241212_143052.csv')

# Filter valid samples
df_valid = df[df['IsTrackingValid'] == True]

# Convert timestamp to datetime
df['datetime'] = pd.to_datetime(df['SystemTimestamp'], unit='100ns')
```

### R

```r
library(readr)

df <- read_csv('EyeTracking_20241212_143052.csv')

# Filter valid samples
df_valid <- df[df$IsTrackingValid == TRUE, ]
```

---

## Ground Truth Validation Data (Calibration System)

The calibration system exports separate ground truth CSV files for validating eye tracking accuracy.

### File Naming Convention
```
{ParticipantID}_GroundTruth_S{SessionNumber}_{timestamp}.csv
```

Example: `P001_GroundTruth_S1_2024-12-13_14-30-52.csv`

### Ground Truth Fields (24 fields)

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `Timestamp` | float | seconds | Unity Time.time |
| `SessionTime` | float | seconds | Time since test started |
| `TestType` | string | - | "FIXATION", "SMOOTH_PURSUIT", "SACCADE" |
| `TargetID` | string | - | Target object name |
| `TargetPosX` | float | meters | Target world position X |
| `TargetPosY` | float | meters | Target world position Y |
| `TargetPosZ` | float | meters | Target world position Z |
| `SurfaceX` | float | meters | Gaze-surface intersection X |
| `SurfaceY` | float | meters | Gaze-surface intersection Y |
| `SurfaceZ` | float | meters | Gaze-surface intersection Z |
| `IntendedDirX` | float | - | Expected gaze direction X |
| `IntendedDirY` | float | - | Expected gaze direction Y |
| `IntendedDirZ` | float | - | Expected gaze direction Z |
| `GazeOriginX` | float | meters | Actual gaze origin X |
| `GazeOriginY` | float | meters | Actual gaze origin Y |
| `GazeOriginZ` | float | meters | Actual gaze origin Z |
| `GazeDirX` | float | - | Actual gaze direction X |
| `GazeDirY` | float | - | Actual gaze direction Y |
| `GazeDirZ` | float | - | Actual gaze direction Z |
| `GazeError` | float | degrees | Angular error to target center |
| `SurfaceError` | float | degrees | Angular error to surface intersection |
| `IsFixating` | bool | - | Whether in fixation test |
| `IsValid` | bool | - | Sample meets accuracy threshold |
| `HasSurfaceHit` | bool | - | Whether gaze ray hit surface |

### Metadata Header

Ground truth files include a metadata header:
```csv
# GroundTruthValidation
# ParticipantID: P001
# SessionNumber: 1
# RecordedAt: 2024-12-13 14:30:52
# AccuracyThreshold: 2.0
# SamplingRate: 60
#
Timestamp,SessionTime,TestType,TargetID,...
```

### Accuracy Metrics

**Gaze Error Calculation**:
```
GazeError = arccos(dot(actualDirection, intendedDirection)) × (180/π)
```
Result is in degrees. Lower values indicate better accuracy.

**Quality Thresholds**:
| Rating | Accuracy | Mean Error |
|--------|----------|------------|
| Excellent | ≥95% valid | <1° |
| Good | ≥85% valid | <2° |
| Acceptable | ≥70% valid | <3° |
| Poor | ≥50% valid | ≥3° |
| Unusable | <50% valid | - |

### Loading Ground Truth Data

```python
import pandas as pd

# Skip metadata lines starting with #
df = pd.read_csv('P001_GroundTruth_S1_2024-12-13_14-30-52.csv',
                  comment='#')

# Filter by test type
fixation_data = df[df['TestType'] == 'FIXATION']
pursuit_data = df[df['TestType'] == 'SMOOTH_PURSUIT']
saccade_data = df[df['TestType'] == 'SACCADE']

# Calculate mean error per test type
print(f"Fixation mean error: {fixation_data['GazeError'].mean():.2f}°")
print(f"Pursuit mean error: {pursuit_data['GazeError'].mean():.2f}°")
print(f"Saccade mean error: {saccade_data['GazeError'].mean():.2f}°")
```

---

## Scene-state and event sidecars (v1.2+)

When scene recording is enabled (default for the SampleExperiment), each
session writes two sidecar CSVs alongside the main gaze CSV. They share
the same `# Key: value` metadata-block convention as the main file.

### `EyeTracking_<session>_SceneState.csv`

One row per (frame, Recordable) pair — captures the world-space pose of
every Recordable in the scene at every frame the recorder samples.

| Column | Type | Description |
|---|---|---|
| `Frame` | int | Unity frame number, joins to the main CSV's `FrameNumber`. |
| `T` | float | `Time.realtimeSinceStartup` at sample time, seconds. |
| `ObjectId` | string | Stable MD5-derived id from the seeded-id system. Identical across replay runs of the same recording. |
| `Pos_X`, `Pos_Y`, `Pos_Z` | float | World-space position, metres. |
| `Rot_X`, `Rot_Y`, `Rot_Z`, `Rot_W` | float | World-space rotation as a quaternion (Unity / scipy convention). |
| `Active` | bool | GameObject active state at sample time. |

Header lines include `SampleEveryNthFrame` (recorder write-rate divider)
and a `Profile: <SceneRecordingProfile name>` field.

### `EyeTracking_<session>_SceneEvents.csv`

One row per discrete event emitted by the experiment controller or any
caller of `SceneEventRecorder.Record(...)` / `RecordKV(...)` /
`RecordJson(...)`.

| Column | Type | Description |
|---|---|---|
| `Frame` | int | Unity frame number when the event was emitted. |
| `T` | float | `Time.realtimeSinceStartup`, seconds. |
| `EventType` | string | Free-text type tag — see taxonomy below. |
| `ObjectId` | string | Optional Recordable id the event refers to. May be empty. |
| `Detail` | string | Free-text payload. For `Config*` events this is base64-encoded JSON; for everything else it's plain text. |

#### Event-type taxonomy (SampleExperiment v1.2)

| EventType | Meaning |
|---|---|
| `RandomStateSnapshot` | `UnityEngine.Random.state` encoded as `s0;s1;s2;s3`. Replay restores it before phase coroutines run. |
| `ConfigExploration`, `ConfigVisualSearch`, `ConfigCounting`, `ConfigChangeDetection` | Per-phase config snapshots. `Detail` is base64(JSON). |
| `Spawn` | Object instantiated. `ObjectId` carries the seeded id. |
| `Despawn` | Object destroyed. |
| `ShowInstruction`, `HideInstruction`, `SetInstructionTextOnly` | Experiment UI text changes. `Detail` is the instruction string. |
| `ShowProgress`, `HideProgress` | Progress HUD changes. |
| `ShowCountingAnswerUI`, `HideCountingAnswerUI`, `CountingAnswerSelected` | Counting-task answer flow. |
| `ChangeDetectionHideScene`, `ChangeDetectionShowChangedScene`, `ChangeDetectionFeedback` | Change-detection trial timeline. |
| `ClearTestObjects`, `SpawnTestEnvironment`, `SpawnVisualSearchScene`, `SpawnCountingScene`, `SpawnChangeDetectionScene` | Phase-boundary spawns. |

Researchers building custom experiments are free to emit additional
event types; the loader passes them through unchanged.

### Loading

```python
import eyelean_analysis as ela

state, events = ela.load_scene_sidecars(
    "Logs/EyeTracking_<session>.csv",
    decode_config=True,                   # base64-decode Config* Detail
)
joined = ela.merge_gaze_with_scene_state(data.df, state)
```

Notebook `07_scene_sidecars.ipynb` walks through Config payload
inspection, Spawn/Despawn lifetime, gaze × scene-state join, and
event-type histograms.

---

*Last updated: 2026-05-03*
