# Eye_lean Replay System

> **Purpose**: Replay recorded eye tracking data from Eye_lean CSV files
> by **re-running the live experiment** against recorded HMD pose, gaze,
> and `UnityEngine.Random.state`.
>
> **Location**: `Assets/Scripts/Replay/`
>
> **As of v1.2 the replay system uses the deterministic re-execution
> model.** This document covers that mode. For the legacy
> gaze-rays-only / event-driven mode, set
> `ReplayController.deterministicReplay = false`.

---

## Overview

The Replay System allows researchers to:

- **Faithfully reproduce** the participant's experience pixel-for-pixel
  by re-running the same scripts that produced the recording.
- Visualize gaze rays, vergence points, and head position.
- Analyze eye movements (fixations/saccades) in real-time against the
  recorded data.
- Calculate attention metrics (K-coefficient, gaze entropy).
- Filter playback by **phase** or **subtask**.

The system is **editor-only** — replay isn't a feature of the deployed
APK.

---

## Quick Start

### Setup

1. Open the **same scene** that produced the recording (typically your
   experiment scene, `SampleExperiment.unity` for the demo).
2. Create an empty GameObject named "ReplayController".
3. Add the **`ReplayController`** component. Set
   `Data File Path` to the main CSV (the `_SceneState.csv` and
   `_SceneEvents.csv` sidecars are auto-discovered).
4. Add the **`DemoReplayBootstrapper`** component on the same
   GameObject. Leave `Anchor To Recording = ON` and
   `Auto Play After Bootstrap = ON`.
5. Press Play.

The following sibling components auto-attach at scene load (no
inspector setup needed):

- `SceneStateReplayer` — applies recorded transforms to live
  Recordables per frame.
- `SceneEventReplayer` — restores `Random.state`, applies recorded
  configs, fires researcher-registered event handlers.
- `ReplayGazeRaycaster` — drives `GazeTarget.IsBeingGazedAt` from
  recorded gaze.

The live `SampleExperimentController` (or your equivalent) **runs
during replay** with its inputs replaced. Recorders (`SessionRecorder`,
`SceneStateRecorder`, `SceneEventRecorder`) check `ReplayMode.IsActive`
in their `Start` and disable themselves so the replay doesn't write a
new file on top of the original.

### Controls

| Key | Action |
|-----|--------|
| Space | Play/Pause |
| Left/Right Arrow | Skip 30 frames |
| Page Up/Down | Previous/Next phase |
| Home/End | Jump to start/end |

---

## Deterministic replay (v1.2)

The default mode. With `ReplayController.deterministicReplay = true`
(forced ON in editor):

| Input | Replay source |
| --- | --- |
| HMD pose | `ReplayController.ApplyHeadPoseToCamera` writes recorded head pose to `Camera.main` each frame. `HmdPoseDriverBootstrap` skips the live `TrackedPoseDriver` attach during replay so it doesn't fight the writes. |
| Eye gaze | `EyeTrackerFactory.SetReplayOverride(replayingTracker)` is set at `LoadDataCoroutine` completion. Every `IEyeTracker` consumer in the scene (calibration UI, gameplay, your code) gets recorded gaze. The override lives for the controller's lifetime; cleared in `OnDestroy`. |
| Gaze hit-detection | `ReplayGazeRaycaster` does a `Physics.Raycast` from recorded gaze each `LateUpdate` and drives `GazeTarget.OnGazeEnter`/`OnGazeExit`. This unblocks every gaze-dwell consumer (visual-search target acquisition, counting answer dwell, change-detection picks). |
| `UnityEngine.Random` | `SceneEventReplayer` finds the `RandomStateSnapshot` event (frame 2 of the recording), decodes it via reflection, and assigns it to `UnityEngine.Random.state` BEFORE any phase coroutine starts. Every `Random.Range` call thereafter produces the recorded sequence. |
| `Time.time` / `Time.deltaTime` | Real-time editor clock. Coroutines run at native speed; recordings were made at native speed too. |

The live experiment's coroutines (`RunFreeExploration`,
`RunVisualSearch`, etc.) run **as if recording**. The result is that
all participant-facing UI, all task spawns, all gaze-driven feedback
re-fire at the recorded cadence.

### What deterministic replay requires from your code

- Tag every visually relevant object as `Recordable` (or use
  `MarkRecordableSeeded` for runtime-spawned objects, with stable
  seed strings).
- Wrap UI calls in a class like `ExperimentUI` so they record
  themselves via `SceneEventRecorder.Record`. Or call `Record(...)`
  manually from your own UI methods.
- Use `UnityEngine.Random` (not `System.Random` or
  `DateTime.Now.Ticks`-derived seeds).
- Use `WaitForSeconds`/`Time.deltaTime`-based coroutines (not
  wall-clock-derived flow control).

Full contract + caveats in
[`RESEARCHER_GUIDE.md`](./RESEARCHER_GUIDE.md#the-deterministic-replay-contract).

### Disabling deterministic replay (legacy mode)

Set `ReplayController.deterministicReplay = false` to fall back to
the v1.1 behavior: only gaze rays + scene-state visualization, the
live experiment is disabled, placeholders fill in for missing
Recordable ids. Useful for replaying recordings made before seeded
ids landed (v1.1 and earlier).

---

## Components

### ReplayManager (Recommended Entry Point)

Manages replay functionality with Inspector-based configuration. **Add this to your scene first** - it auto-creates other components.

**Inspector Settings:**

| Section | Setting | Description |
|---------|---------|-------------|
| **Data Source** | CSV File Path | Path to CSV file (use Browse button) |
| | Auto Load On Start | Load file automatically on Start() |
| **Playback Filtering** | Playback Scope | EntireRecording, SpecificPhase, or SpecificSubtask |
| | Filter Phase | Phase name to filter by |
| | Filter Subtask | Subtask name to filter by |
| **Shared Components** | Environment Generator | Reference to scene's EnvironmentGenerator |
| | Camera Transform | Reference to main camera |
| **Settings** | Auto Create Components | Auto-create ReplayController, Classifier, etc. |
| | Debug Mode | Show debug messages |

**Public API:**
```csharp
// Load data
replayManager.LoadData();                    // Load from csvFilePath
replayManager.LoadData("path/to/file.csv");  // Load specific file

// Playback control
replayManager.Play();
replayManager.Pause();
replayManager.Stop();
replayManager.TogglePlayPause();

// Filtering (runtime)
replayManager.PlayEntireRecording();
replayManager.PlayPhase("experimental");
replayManager.PlaySubtask("experimental", "task_visual_search");
replayManager.SetFilter(PlaybackScope.SpecificSubtask, "experimental", "task_counting");

// Available phases/subtasks (populated after loading)
List<string> phases = replayManager.availablePhases;
List<string> subtasks = replayManager.availableSubtasks;
```

---

### ReplayController

Main replay engine that loads CSV data and controls playback.

**Inspector Settings:**
| Setting | Default | Description |
|---------|---------|-------------|
| Data File Path | "" | CSV file path |
| Auto Load On Start | false | Load file on Start() |
| Playback Speed | 1.0 | Speed multiplier (0.1-5.0) |
| Loop Playback | false | Loop when reaching end |
| Enable Interpolation | true | Smooth frame interpolation |
| Show Avatar | true | Show head position sphere |
| Show Gaze Rays | true | Show left/right eye rays |
| Show Vergence Point | true | Show vergence intersection |

**Important:** The ReplayManager/ReplayController GameObject should be positioned at (0,0,0) in the scene to ensure replay coordinates match the recorded world positions.

**Public API:**
```csharp
// Loading
replayController.LoadData(filePath);
replayController.LoadFromStreamingAssets(fileName);

// Playback
replayController.Play();
replayController.Pause();
replayController.TogglePlayPause();
replayController.Stop();

// Navigation
replayController.SeekToFrame(frameIndex);
replayController.SeekToTime(seconds);
replayController.SeekToProgress(0.5f); // 0-1
replayController.SkipFrames(30);
replayController.NextPhase();
replayController.PreviousPhase();

// Filtering
replayController.SetPlaybackFilter("phase_name", "subtask_name");
replayController.SetPlaybackFilter("phase_name", null);  // Phase only
replayController.ClearFilter();
bool hasFilter = replayController.HasActiveFilter;

// Properties (filter-aware)
float progress = replayController.Progress;
float time = replayController.CurrentTime;
float duration = replayController.TotalDuration;  // Filtered duration
int frames = replayController.TotalFrames;        // Filtered frame count
string phase = replayController.CurrentPhase;
bool isPlaying = replayController.IsPlaying;
```

**Events:**
```csharp
replayController.OnStateChanged += (state) => { };
replayController.OnFrameDisplayed += (frame) => { };
replayController.OnPhaseChanged += (phaseName) => { };
replayController.OnLoadComplete += (session) => { };
replayController.OnPlaybackComplete += () => { };
```

---

### EyeMovementClassifier

Classifies eye movements and calculates attention metrics.

**Classification Types:**
- **Fixation**: Gaze velocity < threshold (default 50°/s)
- **Saccade**: Gaze velocity > threshold

**Attention Types (K-coefficient):**
- **Focal** (K > 0.4): Detailed inspection, long fixations
- **Ambient** (K < -0.4): Scanning, short fixations
- **Neutral**: Balanced attention

**Usage:**
```csharp
var result = classifier.ClassifyFrame(frame);
Debug.Log($"Movement: {result.movementType}");
Debug.Log($"Attention: {result.attentionType}");
Debug.Log($"K-coefficient: {result.kCoefficient}");

// Process entire session
var summary = classifier.ProcessSession(session);
Debug.Log($"Fixation %: {summary.fixationPercentage}");
Debug.Log($"Avg K: {summary.averageKCoefficient}");
```

---

### GazeEntropyCalculator

Calculates Shannon entropy of gaze distribution.

**Interpretation:**
| Normalized Entropy | Interpretation |
|-------------------|----------------|
| < 0.3 | Highly Focused |
| 0.3 - 0.5 | Focused |
| 0.5 - 0.7 | Moderate Exploration |
| 0.7 - 0.85 | Active Exploration |
| > 0.85 | Highly Dispersed |

**Usage:**
```csharp
// Add samples during playback
entropyCalculator.AddSampleFromFrame(frame);

// Get current entropy
var result = entropyCalculator.CalculateEntropy();
Debug.Log($"Entropy: {result.entropy}");
Debug.Log($"Normalized: {result.normalizedEntropy}");
Debug.Log($"Interpretation: {entropyCalculator.InterpretEntropy(result.normalizedEntropy)}");

// Get heatmap
float[,] heatmap = entropyCalculator.GetHeatmap();
```

---

### ReplayVisualizer

Visual feedback during replay with analysis overlays.

**Features:**
- Color vergence point by attention type (green=focal, orange=ambient, cyan=neutral)
- Scale vergence point by confidence
- Optional gaze trail
- Color rays by movement type

---

### ReplayUI

Screen-space UI for controlling replay.

**Controls:**
- File dropdown: Select CSV file
- Play/Pause/Stop buttons
- Progress slider: Seek through recording
- Speed slider: Adjust playback speed (0.1x - 3x)
- Loop toggle

**Keyboard Shortcuts:**
| Key | Action |
|-----|--------|
| Space | Play/Pause |
| Left Arrow | Skip back 30 frames |
| Right Arrow | Skip forward 30 frames |
| Page Up | Previous phase |
| Page Down | Next phase |
| Home | Go to start |
| End | Go to end |

---

## Playback Filtering

The replay system supports filtering playback to specific phases or subtasks, allowing targeted analysis of particular segments.

### Filter Scopes

| Scope | Description |
|-------|-------------|
| **EntireRecording** | Play all frames (no filter) |
| **SpecificPhase** | Play only frames matching a phase name |
| **SpecificSubtask** | Play only frames matching both phase and subtask |

### Inspector Configuration

1. Set **Playback Scope** to your desired filter type
2. Enter the **Filter Phase** (e.g., "experimental", "control")
3. If using SpecificSubtask, also enter **Filter Subtask** (e.g., "task_visual_search")
4. Available phases/subtasks are shown in the Inspector after loading data

### Runtime API

```csharp
// Via ReplayManager (recommended)
replayManager.PlayEntireRecording();
replayManager.PlayPhase("experimental");
replayManager.PlaySubtask("experimental", "task_counting");

// Via ReplayController (direct)
replayController.SetPlaybackFilter("control", null);     // Phase only
replayController.SetPlaybackFilter("control", "task_1"); // Phase + subtask
replayController.ClearFilter();                          // Clear filter
```

### Filter-Aware Properties

When a filter is active, these properties reflect the filtered data:
- `TotalDuration` - Duration of filtered segment
- `TotalFrames` - Number of frames in filtered segment
- `Progress` - Progress within filtered segment
- `CurrentTime` - Time relative to filtered segment start

The status display shows active filter information.

---

## Signal Processing Filters

Located in `SignalFilters.cs`:

### Butterworth Filter
2nd-order IIR low-pass filter for smooth signals.
```csharp
var filter = new SignalFilters.ButterworthFilter(cutoffHz: 10f, sampleRate: 90f);
Vector3 filtered = filter.Filter(rawDirection);
```

### Savitzky-Golay Filter
Polynomial smoothing that preserves peaks.
```csharp
var filter = new SignalFilters.SavitzkyGolayFilter(windowSize: 5, order: 2);
Vector3 smoothed = filter.Filter(rawDirection);

// Calculate velocity
Vector3 velocity = filter.CalculateVelocity(direction, history, sampleRate);
```

### Moving Average
Simple average over window.
```csharp
var filter = new SignalFilters.MovingAverageFilter(windowSize: 5);
Vector3 averaged = filter.Filter(rawDirection);
```

### Exponential Smoothing
Low-pass with adjustable responsiveness.
```csharp
var filter = new SignalFilters.ExponentialFilter(alpha: 0.2f);
Vector3 smoothed = filter.Filter(rawDirection);
```

---

## CSV Format Compatibility

The parser supports Eye_lean CSV format with these columns:

**Required:**
- UnityTimestamp (or timestamp)
- HeadPos_X/Y/Z (or head_x/y/z, pos_x/y/z)

**Recommended:**
- LeftDir_X/Y/Z, RightDir_X/Y/Z
- LeftOrigin_X/Y/Z, RightOrigin_X/Y/Z
- LeftOpenness, RightOpenness
- VergencePoint_X/Y/Z
- CurrentPhase, SubTask, ParticipantID

The parser uses flexible column aliases to handle different naming conventions.

---

## Architecture

```
ReplayController
├── EyeLeanCSVParser (loads CSV data)
├── ReplaySession (preprocessed frames)
├── ReplayFrame[] (individual samples)
│
├── Visualization
│   ├── Avatar sphere
│   ├── Left/Right gaze rays
│   └── Vergence point
│
└── Events → ReplayVisualizer
             ├── EyeMovementClassifier
             └── GazeEntropyCalculator
```

**Data Flow:**
1. CSV file loaded by EyeLeanCSVParser
2. Frames preprocessed into ReplaySession
3. ReplayController advances through frames
4. OnFrameDisplayed event triggers visualization updates
5. Analysis components process each frame

---

## Best Practices

1. **File Organization**: Use consistent naming like `P001_Session1.csv`
2. **Phase Markers**: Ensure CSV has CurrentPhase column for navigation
3. **Performance**: Large files (>100k frames) may need longer load times
4. **Memory**: Each frame uses ~200 bytes; 100k frames ≈ 20MB RAM

---

## Troubleshooting

**No files in dropdown:**
- Check that CSV files are in `Assets/StreamingAssets/`
- Click "Refresh" button

**Playback stuttering:**
- Enable interpolation
- Reduce playback speed

**Visualization not showing:**
- Check that Show Avatar/Rays/Vergence are enabled
- Verify CSV has eye tracking data columns

**K-coefficient always zero:**
- Ensure classifier is processing frames
- Check that velocity threshold is appropriate for your data

**Avatar/rays outside the room:**
- Ensure ReplayManager GameObject is at position (0, 0, 0)
- Verify CSV has valid HeadPos_X/Y/Z values
- Check that the room was generated before playback started

**Environment not changing on task selection:**
- Environment regenerates when task filter changes
- Check ReplayManager has reference to EnvironmentGenerator
- Verify task filter dropdown is connected to ReplayManager

---

## References

- Savitzky, A., & Golay, M.J.E. (1964). *Analytical Chemistry*, 36(8), 1627-1639.
- Duchowski, A.T., et al. (2022). 3D Gaze in VR. *CHI 2022*.
- Unema, P.J., et al. (2005). K-coefficient. *Visual Cognition*, 12(6), 1019-1034.
