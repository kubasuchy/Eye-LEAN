# Architecture

System design and component documentation for the VR Eye Tracking Research Toolkit.

---

## Overview

The toolkit uses a **layered architecture** with clear separation between:
1. **Hardware abstraction** (device providers)
2. **Data collection** (tracking and export)
3. **Processing** (vergence, smoothing)
4. **Quality monitoring** (blinks, tracking loss, stuck rays)
5. **Visualization** (debug rendering)

---

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Application Layer                            │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │           EyeTracker  +  HMDDataCollector  +  SessionRecorder│    │
│  │  EyeTracker:                                                │    │
│  │   - Frame-rate gaze sampling via IEyeTracker                │    │
│  │   - Vergence calculation (Simple / Paper / DepthExtension)  │    │
│  │   - Debug visualization (gaze rays, vergence point)         │    │
│  │   - Auto-detects DataQualityMetrics sibling                 │    │
│  │  HMDDataCollector:                                          │    │
│  │   - Head pose snapshot, coordinate-origin management        │    │
│  │  SessionRecorder:                                           │    │
│  │   - CSV export with crash protection + lazy header          │    │
│  │   - Participant ID, session context, custom metadata        │    │
│  │   - RegisterMetric API for custom CSV columns               │    │
│  └─────────────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │                  DataQualityMetrics (Optional)               │    │
│  │  - Blink detection via eye openness                         │    │
│  │  - Tracking loss monitoring                                 │    │
│  │  - Stuck ray detection                                      │    │
│  │  - Quality rating (Excellent/Good/Acceptable/Poor/Unusable) │    │
│  └─────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       Abstraction Layer                              │
│  ┌──────────────────┐    ┌──────────────────────────────────────┐  │
│  │  EyeTrackerFactory│    │           IEyeTracker                 │  │
│  │  - Device detect  │───▶│  - IsAvailable                       │  │
│  │  - Provider cache │    │  - GetCombinedGaze*()                │  │
│  │  - Reinitialize() │    │  - GetLeftEye*() / GetRightEye*()    │  │
│  └──────────────────┘    └──────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Provider Layer                                │
│  ┌──────────────────────┐  ┌─────────────┐  ┌─────────────────┐    │
│  │ OpenXREyeTracker     │  │ VarjoProvider│  │ HoloLensProvider│    │
│  │ Provider (VIVE)      │  │ (Planned)    │  │ (Planned)       │    │
│  └──────────────────────┘  └─────────────┘  └─────────────────┘    │
│               │                                                      │
│               ▼                                                      │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │              OpenXREyeTracker (Low-Level)                     │  │
│  │  - Direct VIVE XR_HTC_eye_tracker.Interop API calls          │  │
│  │  - Singleton pattern                                          │  │
│  └──────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         Hardware Layer                               │
│  ┌──────────────────┐  ┌──────────────┐  ┌───────────────────┐     │
│  │ VIVE Focus Vision │  │ Varjo XR-3   │  │ HoloLens 2        │     │
│  │ (OpenXR)          │  │ (Native SDK) │  │ (Windows MR)      │     │
│  └──────────────────┘  └──────────────┘  └───────────────────┘     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### 1. IEyeTracker Interface

**Location**: `Assets/Scripts/EyeTracking/Core/IEyeTracker.cs`

The common interface implemented by all eye tracking device providers.

```csharp
public interface IEyeTracker
{
    bool IsAvailable { get; }
    string DeviceName { get; }
    float SamplingRateHz { get; }

    // Combined gaze
    bool GetCombinedGazeOrigin(out Vector3 origin);
    bool GetCombinedGazeDirection(out Vector3 direction);

    // Per-eye gaze
    bool GetLeftEyeOrigin(out Vector3 origin);
    bool GetLeftEyeDirection(out Vector3 direction);
    bool GetLeftEyeOpenness(out float openness);
    bool GetLeftPupilDiameter(out float diameterMm);
    bool GetLeftPupilPosition(out Vector2 position);

    // Same for right eye...
}
```

**Extended Interface** (`IEyeTrackerExtended`):
```csharp
public interface IEyeTrackerExtended : IEyeTracker
{
    EyeTrackerFeatures SupportedFeatures { get; }
    bool SupportsFeature(EyeTrackerFeatures feature);
}
```

**Feature Flags**:
```csharp
[Flags]
public enum EyeTrackerFeatures
{
    None = 0,
    CombinedGaze = 1 << 0,
    PerEyeGaze = 1 << 1,
    EyeOpenness = 1 << 2,
    PupilDiameter = 1 << 3,
    PupilPosition = 1 << 4,
    All = CombinedGaze | PerEyeGaze | EyeOpenness | PupilDiameter | PupilPosition
}
```

---

### 2. EyeTrackerFactory

**Location**: `Assets/Scripts/EyeTracking/Core/EyeTrackerFactory.cs`

Factory class for automatic device detection and provider management.

```csharp
public static class EyeTrackerFactory
{
    // Get cached eye tracker instance
    public static IEyeTracker GetEyeTracker();

    // Force re-detection (for runtime changes)
    public static void Reinitialize();

    // Quick availability check
    public static bool IsAnyTrackerAvailable();

    // Get device name
    public static string GetCurrentDeviceName();
}
```

**Detection Priority**:
1. VIVE OpenXR (`USE_OPENXR` define)
2. Varjo (`USE_VARJO` define) - Planned
3. HoloLens 2 (`USE_HOLOLENS` define) - Planned
4. NullEyeTracker (fallback)

**Null Object Pattern**:
```csharp
public class NullEyeTracker : IEyeTracker
{
    public bool IsAvailable => false;
    public string DeviceName => "None";
    // All methods return false/default values
}
```

---

### 3. OpenXREyeTrackerProvider

**Location**: `Assets/Scripts/EyeTracking/Providers/OpenXREyeTrackerProvider.cs`

Wraps the low-level `OpenXREyeTracker` singleton with the `IEyeTracker` interface.

**Key Features**:
- Singleton pattern with lazy initialization
- Exception-safe property access
- Automatic fallback on errors

```csharp
#if USE_OPENXR
public class OpenXREyeTrackerProvider : IEyeTrackerExtended
{
    public static OpenXREyeTrackerProvider Instance { get; }

    // Safe access to underlying tracker
    private OpenXREyeTracker Tracker { get; }

    public bool IsAvailable
    {
        get
        {
            try { return Tracker?.IsEyeTrackingAvailable() ?? false; }
            catch { return false; }
        }
    }
}
#endif
```

---

### 4. OpenXREyeTracker (Low-Level)

**Location**: `Assets/Scripts/EyeTracking/OpenXREyeTracker.cs`

Direct interface to VIVE OpenXR eye tracking hardware.

**API Calls**:
- `VIVE.OpenXR.EyeTracker.XR_HTC_eye_tracker.Interop.GetEyeGazeData()`
- `VIVE.OpenXR.EyeTracker.XR_HTC_eye_tracker.Interop.GetEyePupilData()`
- `VIVE.OpenXR.EyeTracker.XR_HTC_eye_tracker.Interop.GetEyeGeometricData()`

**Note**: This class is retained for backward compatibility. New code should use the `IEyeTracker` interface.

---

### 5. DataQualityMetrics

**Location**: `Assets/Scripts/EyeTracking/Data/DataQualityMetrics.cs`

Optional component for tracking aggregate data quality during a session.

**Features**:
- **Blink Detection**: Tracks when both eyes are below openness threshold (default 0.2)
- **Tracking Loss**: Counts frames where eye tracking data is invalid
- **Stuck Ray Detection**: Detects when gaze direction is unchanging for 60+ frames
- **Quality Rating**: Provides overall rating based on valid sample percentage and stuck ray events

**Quality Rating Criteria**:
| Rating | Valid % | Stuck Events |
|--------|---------|--------------|
| Excellent | ≥95% | 0 |
| Good | ≥85% | ≤2 |
| Acceptable | ≥70% | Any |
| Poor | ≥50% | Any |
| Unusable | <50% | Any |

**Integration**:
```csharp
// Auto-detected by EyeTracker if attached to the same GameObject
// (Assets/Scripts/EyeTracking/Components/EyeTracker.cs:190).
// No manual wiring required.

// Access via SessionRecorder's quality-metrics passthrough
// (Assets/Scripts/EyeTracking/Components/SessionRecorder.cs:770-783),
// which forwards to its EyeTracker sibling.
var metrics = sessionRecorder.GetQualityMetrics();
sessionRecorder.LogQualitySummary();
sessionRecorder.ResetQualityMetrics();
```

**Inspector Settings**:
- `blinkThreshold`: Eye openness below this is considered a blink (default: 0.2)
- `stuckRayThreshold`: Direction change below this suggests stuck ray (default: 0.001)
- `stuckRayFrames`: Frames without movement before stuck (default: 60)

---

### 6. EyeTracker + HMDDataCollector + SessionRecorder

The per-frame collection layer — three sibling MonoBehaviours that mount on the same GameObject. (Pre-v1.0-RC, this was a single ~2200-line monolith; the refactor split it into the components below for testability and for the deterministic-replay seam.)

#### 6a. EyeTracker

**Location**: `Assets/Scripts/EyeTracking/Components/EyeTracker.cs`

Frame-rate gaze sampling, vergence math, and debug visualization.

**Responsibilities**:
- Frame-rate data collection via `IEyeTracker`
- Vergence calculation (Simple, PaperAlgorithm, DepthExtension)
- Surface clamping via raycast
- Temporal smoothing (delegated to `VergenceSmoothingProcessor`, see 6d)
- Debug visualization (gaze rays, vergence point)
- Auto-detects `DataQualityMetrics` sibling (`EyeTracker.cs:188-191`)

**Vergence Depth Modes** (defined in `Assets/Scripts/EyeTracking/Vergence/VergenceTypes.cs`):
```csharp
public enum VergenceDepthMode
{
    TrueConvergence,  // Mathematical eye convergence (limited ~1.5-2.3m on VIVE)
    DepthExtension    // Ray intersection + surface clamping (recommended)
}
```

**DepthExtension Mode** (default, recommended for VIVE Focus Vision):
- Uses ray-ray intersection for depth sensitivity
- Raycasts to detect surface colliders
- Uses the CLOSER of: intersection point OR surface hit
- Vergence "catches" at walls, targets, and objects with colliders

Implementation: `EyeTracker.cs:449-544` (`CalculateVergenceWithDepthExtension`).

**Key Methods**:
```csharp
// Vergence settings + reset
void ResetVergenceSmoothing();           // Re-init smoothing processor

// Quality metrics getter (raw access; passthrough on SessionRecorder)
DataQualityMetrics GetQualityMetrics();  // EyeTracker.cs:776
```

#### 6b. HMDDataCollector

**Location**: `Assets/Scripts/EyeTracking/Components/HMDDataCollector.cs`

Head pose snapshot and coordinate-origin management.

**Key Methods**:
```csharp
bool SetCoordinateOrigin();                      // HMDDataCollector.cs:137
void SetCoordinateOrigin(Vector3 worldPosition); // HMDDataCollector.cs:152
void ResetCoordinateOrigin();
Vector3 CurrentTrialStartPosition { get; }       // HMDDataCollector.cs:37

// Fires when origin lands; SessionRecorder listens to write the lazy CSV header.
event Action<Vector3> OnCoordinateOriginSet;
```

#### 6c. SessionRecorder

**Location**: `Assets/Scripts/EyeTracking/Components/SessionRecorder.cs`

CSV writer, participant + session metadata, and the custom-metric extension API.

**Key Methods**:
```csharp
// Public API - Session Management
void SetSessionContext(int trialNumber, string phase = "Recording",
                        string config = "Default", string subTask = "");  // SessionRecorder.cs:676
void SetSubTask(string subTask);                                          // SessionRecorder.cs:685
void SetParticipantID(string participantID);                              // SessionRecorder.cs:691
void SetMetadata(string fieldName, string value);                         // SessionRecorder.cs:699

// Public API - Custom CSV columns (call BEFORE recording starts)
void RegisterMetric(string name, Func<string> getter);                    // SessionRecorder.cs:122
void RegisterMetric(string name, Func<float> getter, string format = "F4"); // 143
bool UnregisterMetric(string name);                                       // 150

// Public API - Quality Metrics (passthrough to EyeTracker sibling)
DataQualityMetrics GetQualityMetrics();   // SessionRecorder.cs:770
void LogQualitySummary();                 // SessionRecorder.cs:772
void ResetQualityMetrics();               // SessionRecorder.cs:779
```

#### 6d. VergenceSmoothingProcessor

**Location**: `Assets/Scripts/EyeTracking/Vergence/VergenceSmoothingProcessor.cs`

Temporal-smoothing helper used by `EyeTracker`. Implements WeightedEMA, 2nd-order Butterworth IIR, and Savitzky-Golay polynomial smoothing. See [ALGORITHMS.md](./ALGORITHMS.md) for the math.

---

## Data Flow

```
┌──────────────────┐
│  Eye Tracker HW  │
│  (120 Hz)        │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  OpenXREyeTracker│  ◄── Low-level API calls
│  (Singleton)     │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  Provider        │  ◄── IEyeTracker interface
│  (OpenXR/Varjo)  │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ EyeTrackerFactory│  ◄── Device detection & caching
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│   EyeTracker     │  ◄── Gaze sampling (frame rate)
│                  │      + vergence (Simple/Paper/DepthExt)
│  ┌────────────┐  │
│  │ Smoothing  │  │  ◄── VergenceSmoothingProcessor
│  │ Processor  │  │      (WeightedEMA / Butterworth / SG)
│  └────────────┘  │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ HMDDataCollector │  ◄── Head pose + coordinate origin
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  SessionRecorder │  ◄── CSV export (crash-protected)
│                  │      + participant/session metadata
│                  │      + RegisterMetric API
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│DataQualityMetrics│  ◄── Quality monitoring (optional)
│  - Blinks        │
│  - Tracking loss │
│  - Stuck rays    │
└──────────────────┘
         │
         ▼
┌──────────────────┐
│  CSV File        │
│  (84+ fields)    │
└──────────────────┘
```

---

## Calibration System (Phase 2)

The calibration system provides structured eye tracking validation tests with ground truth recording.

### Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                   CalibrationSessionManager                          │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  - Session flow: Setup → Instructions → Testing → Completion  │  │
│  │  - Creates test runners dynamically                           │  │
│  │  - Manages UI and participant ID                              │  │
│  │  - Aggregates results from all tests                          │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                              │                                       │
│            ┌─────────────────┼─────────────────┐                    │
│            ▼                 ▼                 ▼                    │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │
│  │ FixationTest │  │SmoothPursuit │  │  SaccadeTest │              │
│  │    Runner    │  │ TestRunner   │  │    Runner    │              │
│  └──────────────┘  └──────────────┘  └──────────────┘              │
│            │                 │                 │                    │
│            └─────────────────┼─────────────────┘                    │
│                              ▼                                       │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │              CalibrationTestRunner (Base Class)                │  │
│  │  - Target creation with VR-safe materials                     │  │
│  │  - Ground truth sample recording                              │  │
│  │  - Progress reporting                                         │  │
│  │  - Uses IEyeTracker interface                                 │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     CalibrationWorldUI                               │
│  - VR world-space floating panel                                    │
│  - Participant ID input                                              │
│  - Instructions display                                              │
│  - Progress bar during tests                                         │
│  - Results summary                                                   │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     GroundTruthValidator                             │
│  - Records gaze accuracy vs known target positions                  │
│  - Exports ground truth CSV with detailed error metrics             │
│  - Supports all test types (Fixation, Pursuit, Saccade)            │
│  - Uses IEyeTracker interface for SDK-agnostic access              │
└─────────────────────────────────────────────────────────────────────┘
```

### Test Types

| Test | Description | Targets | Duration |
|------|-------------|---------|----------|
| **Fixation** | Look at static targets for 2s each | 7 sequential (one at a time) | ~21s |
| **Smooth Pursuit** | Track figure-8 moving target | 1 moving | 30s |
| **Saccade** | Rapid gaze shifts between targets | 12 shuffled (one at a time) | ~18s |
| **Free Exploration** | Natural viewing (optional) | Scene objects | 60s |

### Target Positioning (World-Space Pinned)

Calibration targets are positioned at **fixed room coordinates**, not relative to the camera. This ensures:
- Consistent target positions regardless of where user stands
- Valid ground truth data for accuracy analysis
- Targets don't "float" when user moves head
- Only ONE target visible at a time (no visual clutter)

**Room Coordinate System**:
- Origin: (0, 0, 0) at user spawn point
- X: Left (-3m) to Right (+3m)
- Y: Floor (0m) to Ceiling (3m)
- Z: Behind user (0m) to Front wall (6m)

**Fixation Targets** (`FixationTestRunner.cs`):
- Pre-defined positions spread across depths (3.5m - 5.5m)
- Narrower horizontal spread to stay within FOV
- Center targets at multiple depths for vergence testing

**Saccade Targets** (`SaccadeTestRunner.cs`):
- Fixed room coordinates at ~4m depth
- Radial pattern for large eye movements
- Shuffled order for unpredictable saccades

**Smooth Pursuit Center** (`SmoothPursuitTestRunner.cs`):
```csharp
movementCenter = new Vector3(0f, 1.5f, 3f);  // Room center, eye height
movementRight = Vector3.right;                // World X axis
movementUp = Vector3.up;                      // World Y axis
```

### Validation System

Samples are marked **valid** if ANY of these conditions are met:
1. Angular error < accuracy threshold (default 2°)
2. Gaze raycast hits the target object directly
3. Vergence point is within target's collider bounds

This vergence-based validation accounts for cases where the user is clearly looking at the target (vergence depth is correct) even if angular measurements show offset.

### Key Components

**CalibrationSessionManager.cs**:
- Orchestrates session flow
- Creates test runners as child components
- Manages phase transitions
- Collects and aggregates results

**CalibrationTestRunner.cs** (Base Class):
- Abstract class for all test runners
- Common target creation (VR-safe spheres)
- Ground truth sample recording
- Progress event reporting

**GroundTruthValidator.cs**:
- Calculates gaze accuracy (angular error)
- Records ground truth samples to CSV
- Provides detailed validation metrics

**CalibrationWorldUI.cs**:
- World-space Canvas for VR
- Follows camera with smoothing
- Input fields, buttons, progress bar

---

## Directory Structure

```
Assets/Scripts/EyeTracking/
├── Core/
│   ├── IEyeTracker.cs           # Interface definition
│   └── EyeTrackerFactory.cs     # Factory & NullEyeTracker
├── Data/
│   ├── DataQualityMetrics.cs    # Quality tracking (blinks, stuck rays)
│   └── ExperimentMetadata.cs    # Custom metadata with schema locking
├── Configuration/
│   └── ExperimentMetadataSchema.cs  # ScriptableObject schema definitions
├── Calibration/                 # Phase 2
│   ├── CalibrationSessionManager.cs  # Session orchestration
│   ├── CalibrationTestRunner.cs      # Base class for tests
│   ├── CalibrationTypes.cs           # Enums, structs, settings
│   ├── GroundTruthValidator.cs       # Accuracy validation
│   ├── TestScenarios/
│   │   ├── FixationTestRunner.cs
│   │   ├── SmoothPursuitTestRunner.cs
│   │   └── SaccadeTestRunner.cs
│   └── UI/
│       └── CalibrationWorldUI.cs     # VR world-space UI
├── Providers/
│   └── OpenXREyeTrackerProvider.cs  # VIVE OpenXR provider
├── Environment/
│   ├── MovementPattern.cs       # Dynamic target patterns
│   ├── DynamicTarget.cs         # Target movement controller
│   ├── GazeTarget.cs            # Visual gaze feedback
│   ├── EnvironmentGenerator.cs  # Programmatic room generation
│   └── VRMaterialProvider.cs    # Reliable material creation
├── Components/
│   ├── EyeTracker.cs            # Gaze sampling + vergence math + visualizations
│   ├── HMDDataCollector.cs      # Head pose + coordinate origin
│   └── SessionRecorder.cs       # CSV writer + metadata + custom-metric API
├── Vergence/
│   ├── VergenceTypes.cs         # Settings structs / enums
│   ├── VergenceSettings.cs      # Inspector-facing settings
│   └── VergenceSmoothingProcessor.cs  # WeightedEMA / Butterworth / Savitzky-Golay
├── OpenXREyeTracker.cs          # Low-level VIVE API wrapper
├── ResearchDataStructure.cs     # Data structures & CSV
└── DebugFileLogger.cs           # File-based logging
```

---

## Scripting Define Symbols

| Symbol | Purpose |
|--------|---------|
| `USE_OPENXR` | Enable VIVE OpenXR provider |
| `USE_VARJO` | Enable Varjo provider (planned) |
| `USE_HOLOLENS` | Enable HoloLens 2 provider (planned) |

Set in: **Project Settings > Player > Other Settings > Scripting Define Symbols**

---

## Adding New Device Support

1. Create provider class implementing `IEyeTracker`:
   ```csharp
   #if USE_NEWDEVICE
   public class NewDeviceProvider : IEyeTracker { ... }
   #endif
   ```

2. Add detection logic to `EyeTrackerFactory.DetectAndCreateTracker()`:
   ```csharp
   #if USE_NEWDEVICE
   if (TryGetNewDeviceTracker(out IEyeTracker tracker))
   {
       return tracker;
   }
   #endif
   ```

3. Add scripting define symbol `USE_NEWDEVICE`

---

## Thread Safety

- All eye tracking operations run on the main Unity thread
- CSV writing uses buffered writes with periodic flush (every 60 frames)
- No multi-threading concerns in current implementation

---

## Memory Management

- `ResearchDataSample` is a struct (stack allocated)
- Object gaze intersection dictionary is reused (no GC allocation per frame)
- Raycast results array is pre-allocated (50 hits max)

---

*Last updated: 2025-12-21*
