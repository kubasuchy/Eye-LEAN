# Eye_lean (Unity)

**EYE-LEAN** (**L**ocomotion, **E**xploration, **A**ction, and **N**avigation with **Eye** Tracking): a Behavioral Research Toolkit for Data Rich Virtual Reality Experiments.

## What it is

The Unity half of Eye_lean. It collects per-frame binocular gaze,
pupil, head-tracking, and session-context data on Android-based VIVE
Focus Vision headsets and writes the data to CSV for offline analysis.
The Python half lives in
[`../../eyelean_analysis/`](../../eyelean_analysis/). The
[repository root README](../../README.md) covers the project overview.

## Audience

Developers extending the Unity project.

## Prerequisites

- Unity 6000.3.9f1
- VIVE OpenXR package 2.5.1
- HTC VIVE Focus Vision (or another OpenXR eye-tracking headset)

## Capabilities

- Device abstraction (`IEyeTracker` + `EyeTrackerFactory`) currently
  implementing VIVE Focus Vision via OpenXR. Adding a device requires
  one interface implementation and a factory registration.
- Three vergence-calculation methods: Simple (closest-point-of-approach
  between left and right eye rays), Paper Algorithm
  (Duchowski et al. 2022), and DepthExtension (mathematical vergence
  for short range, raycast extension for distances beyond the
  hardware's reliable depth limit).
- Three smoothing options for the vergence point: WeightedEMA,
  second-order Butterworth IIR, and Savitzky-Golay polynomial.
- Per-user calibration profile (`EyeTrackingProfile`) that fits a
  yaw/pitch correction from the calibrator's settled fixation samples
  and persists it as JSON next to the CSV. Profiles auto-load on
  subsequent sessions; the live pipeline applies the correction in
  head-local space.
- Data quality tracking (blinks, tracking loss, stuck-ray events) with
  a four-level rating ("Excellent" through "Unusable") that the
  calibrator's Verification phase reports post-fit.
- CSV export with periodic flush, on-pause flush (Android can suspend
  the app without firing OnApplicationQuit), and unconditional metadata
  header lines that record the world-space coordinate origin and the
  active profile.

## Build targets

The APK ships four scenes (the fourth is editor-only):

1. `MainMenu.unity` — gaze-dwell launcher that routes to the calibrator
   or the experiment.
2. `CalibrationScene.unity` — calibrator with Fixation, Saccade, and
   SmoothPursuit test runners, followed by Tuning (auto-fit profile)
   and Verification (post-fit re-test) phases. The procedural
   research-room environment renders around the user on entry.
3. `SampleExperiment.unity` — four-phase battery: FreeExploration,
   VisualSearch, CountingTask, ChangeDetection. The
   [`RIPAMonitor`](../../docs/RIPA_MONITOR.md) drop-in component
   (RIPA2 algorithm — Jayawardena et al. 2025) auto-bootstraps and
   writes a `LiveLoadIndex` cognitive-load column to every recorded
   CSV.
4. `ReplayScene.unity` — editor-only playback of recorded CSVs.

A second researcher template, **Skeleton**, ships as a developer-side
tool (not part of the APK build flow). Run **VR Experiment > New
Skeleton Scene** in the Unity editor to materialize it; see
[`../../docs/SKELETON.md`](../../docs/SKELETON.md).

## Quick start

1. Clone the repository.
2. Open `Eye_lean_Unity_Project/Eye_lean/` in Unity 6000.3.9f1.
3. Confirm OpenXR is enabled under **Project Settings > XR Plug-in
   Management** and that the Eye Tracker feature is on.
4. Build to your VR device.

For a step-by-step environment guide, see
[`docs/SETUP.md`](docs/SETUP.md). For the APK build process, see
[`../../docs/BUILD_GUIDE.md`](../../docs/BUILD_GUIDE.md).

### Verify

The Unity console reports no compile errors after the project finishes
importing, and **File > Build Settings** lists `MainMenu`,
`CalibrationScene`, and `SampleExperiment` as build indexes 0, 1, 2.

## Documentation

**Start here**: [**Researcher Guide**](docs/RESEARCHER_GUIDE.md) — end-to-end manual covering installation, recording an experiment, building a custom experiment on top of Eye_lean, deterministic replay, troubleshooting, and the full output schema.

| Document | Topic |
|----------|-------|
| [Documentation hub](../../docs/README.md) | Top-level component index covering recording, replay, calibration, experiments, and the Skeleton template (v1.4+) |
| [Researcher guide](docs/RESEARCHER_GUIDE.md) | End-to-end narrative |
| [Setup](docs/SETUP.md) | Hardware and software setup |
| [Architecture](docs/ARCHITECTURE.md) | System design |
| [Algorithms](docs/ALGORITHMS.md) | Vergence and smoothing math |
| [Data schema](docs/DATA_SCHEMA.md) | CSV column definitions |
| [Sample experiment](docs/SAMPLE_EXPERIMENT_SETUP.md) | Four-phase battery setup |
| [Custom metadata](docs/CUSTOM_METADATA_TUTORIAL.md) | Adding experiment variables |
| [Replay system](docs/REPLAY_SYSTEM.md) | Deterministic replay engine |
| [Replay scene state](docs/REPLAY_SCENE_STATE.md) | Per-object transform replay |
| [Calibration system](../../docs/CALIBRATION.md) | Calibrator + per-user profile JSON |
| [Bibliography](docs/BIBLIOGRAPHY.md) | Citations |
| [Acknowledgments](../../ACKNOWLEDGMENTS.md) | Full citation surface — every algorithm / library / paper Eye_lean builds on |

## Project structure

```
Assets/Scripts/EyeTracking/
  Core/                      Device-agnostic interfaces
    IEyeTracker.cs
    EyeTrackerFactory.cs
  Providers/                 Per-device implementations
    OpenXREyeTrackerProvider.cs
  Configuration/             Profile + active-state plumbing
    EyeTrackingProfile.cs
    ActiveProfile.cs
    EyeTrackingProfileApi.cs
  Calibration/               Test runners + offset estimator
    CalibrationSessionManager.cs
    FixationTestRunner.cs
    SaccadeTestRunner.cs
    SmoothPursuitTestRunner.cs
    OffsetEstimator.cs
    GroundTruthValidator.cs
  Vergence/                  Vergence math + smoothing
  Data/                      Quality metrics
  Components/                Per-frame collection (split RC2)
    EyeTracker.cs            Gaze + vergence + visualizations
    HMDDataCollector.cs      Head pose + coordinate origin
    SessionRecorder.cs       CSV writer + metadata + flush
  Metrics/                   On-device cognitive load (v1.3+)
    SavitzkyGolayDerivative.cs SG first-derivative filter
    RIPA2Analyzer.cs           Pure-C# RIPA2 algorithm
    RIPAMonitor.cs             Scene-singleton MonoBehaviour
    RIPAMonitorBootstrap.cs    Auto-spawn at AfterSceneLoad
    RIPAGauge.cs               Optional UI gauge
    RIPACSVColumn.cs           Registers LiveLoadIndex column
Assets/Scripts/Skeleton/     Researcher template (v1.4+)
  Managers/                  TrialManager + ExperimentManager
                             + AgentManager + EnvironmentManager
  Core/                      IExperimentPhaseHandler interface
  Data/                      TrialConfiguration ScriptableObject
  Editor/                    SkeletonSceneSetup wizard
  Environment/               Procedural research-room generator
  OpenXREyeTracker.cs        Low-level VIVE OpenXR wrapper
Assets/Scripts/Replay/       CSV playback (ReplayScene)
Assets/Scripts/Experiment/   Sample experiment controller + tasks
Assets/Tests/EditMode/       NUnit tests for profile + estimator
```

## Common operations

```csharp
// Per-frame data collection runs automatically once the EyeTracker +
// HMDDataCollector + SessionRecorder trio is in the scene (RC2 split).
// Callers update the trial context on phase change:
sessionRecorder.SetSessionContext(trialNumber: 1,
                                  phase: "Navigation",
                                  config: "ConditionA");

// Anchor the world-space origin used for the position columns.
// SetCoordinateOrigin() uses the camera's current position; the
// header writer captures whatever value is set at the moment the
// first data row lands.
hmdCollector.SetCoordinateOrigin();

// Pull the current quality rating at trial end:
var metrics = sessionRecorder.GetQualityMetrics();
if (metrics != null) {
    string rating = metrics.GetQualityRating();  // Excellent / Good /
                                                 // Acceptable / Poor /
                                                 // Unusable
}
```

The calibrator exposes the same data through the `CalibrationResults`
struct on the Session Complete screen, including settled-fixation
accuracy, saccade landing accuracy, and smooth-pursuit gain.

## Tests

EditMode tests for the calibration data path live in
`Assets/Tests/EditMode/` and run from `Window > General > Test Runner
> EditMode > EyeLean.Tests.EditMode`. They cover the
`EyeTrackingProfile` JSON round-trip (schema versioning,
forward-compat defaults, quaternion construction, clone independence)
and the `OffsetEstimator` median-residual fitter (synthetic samples
with known biases recover the bias to within float epsilon). Hardware
not required.

## Retrieving data from the headset

```bash
adb pull /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/ ./Logs/
adb logcat -s Unity            # live logs from the device
```

CSVs and profile JSONs land in the same directory.

## Citation

```bibtex
@software{eye_lean_toolkit,
  title     = {{EYE-LEAN} (Locomotion, Exploration, Action, and Navigation with Eye Tracking): a Behavioral Research Toolkit for Data Rich Virtual Reality Experiments},
  author    = {Suchojad, Jakub and Dalawella, Kavindya and DeStefani, Serena and Stromswold, Karin and Feldman, Jacob},
  year      = {2026},
  publisher = {Zenodo},
  version   = {v1.0.0},
  doi       = {10.5281/zenodo.20040453},
  url       = {https://doi.org/10.5281/zenodo.20040453}
}
```

See [`CITATION.cff`](CITATION.cff) for the machine-readable form.

## License

MIT License. See [`../../LICENSE`](../../LICENSE).
