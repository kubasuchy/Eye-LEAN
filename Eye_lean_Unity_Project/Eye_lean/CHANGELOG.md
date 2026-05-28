# Changelog

All notable changes to the VR Eye Tracking Research Toolkit will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [1.1.0] - 2026-05-28

This release adds two new shippable experiment scenes (N-back working memory + Navigation Maze), redesigns the launcher, and lands a universal deterministic-replay contract that lets any new experiment built on Eye-LEAN replay out of the box. It also introduces two canonical UI widgets (`RIPAGauge`, `WorldInstructionPanel`) that consolidate previously-duplicated implementations across scenes.

### New experiments

- **N-back working memory** (`NBackScene`). Jayawardena 2025 paper-exact protocol with 0/1/2/3-back load levels plus a passive-viewing baseline that frequency-matches stimulus rhythm. Per-trial signal-detection metrics (d-prime, log-linear corrected hit / false-alarm rates), per-block result JSON for analysis joins, and live cognitive-load gauge for online monitoring. Includes deterministic stimulus stream generation with target-spacing constraints that prevent chain-copy artifacts.
- **Navigation Maze** (`MazeScene`). Procedural 5×5 DFS-generated maze with block-based trial sequencing: Exploration, Wayfinding, Sequential-goal, Probe, and Competitive (NPC race) modes. Per-trial metadata includes optimal-path length, actual-path length, path efficiency, decision-point traversals, wrong turns, dead-end entries, backtrack count, landmark-fixation ratio (from real eye gaze, not head pose), and goal-reach time. Distal and proximal landmark conditions configurable per block. Editor WASD debug locomotion with capsule-cast wall collision for in-editor testing.

### Universal deterministic replay contract

- New 7-rule contract documented in `docs/REPLAY.md` for any experiment to opt into deterministic replay. Existing rules: stay enabled during replay (controller coroutines are the replay), gate live input only, use `EyeTrackerFactory` for gaze queries, use `UnityEngine.Random` for stimulus generation, defer world-space UI placement until `ReplayController.IsPlaying`, auto-start during replay.
- `ReplayController` auto-spawns `ReplayUI` and a cognitive-load overlay; the displayed detector method is selectable via Inspector dropdown. `Stop` reloads the scene and suppresses next autoplay (universal "reset and wait for Play" semantics). `Pause` uses `Time.timeScale = 0f` so all experiment coroutines freeze universally, not just the replay frame-advance.
- `ReplayUI` works standalone (no `ReplayManager` required): task filter dropdown populates from session frames; auto-loads when the controller has a pre-set CSV path; mouse-wheel scrolling in dropdowns; ensures `InputSystemUIInputModule` on the EventSystem so clicks land.

### Canonical UI widgets

- **`RIPAGauge`** (`Scripts/EyeTracking/Metrics/`). One canonical cognitive-load gauge widget used by `RIPAOverlay` (screen-space corner), `ExperimentUI` (inside the world-space panel), and the new `ReplayController` overlay. Anchor-based vertical fill (no Image sprite required), color tints green → amber → red, polls the monitor each frame so slow value changes still propagate to the visual. Factory method `CreateVerticalStrip(parent, size)` returns the assembled widget plus the gauge component for placement-only callers.
- **`WorldInstructionPanel`** (`Scripts/EyeTracking/UI/`). One canonical world-space title + body instruction panel used by both N-back and Maze. Uses `VRMaterialProvider` with the Android shader-stripping fallback chain. `PlaceInFrontOf(camT)` for one-time placement at the participant's recorded HMD pose; auto-spawned by controllers if the scene doesn't contain one.
- `RIPAMonitor` skips duplicate pupil samples during replay so the Savitzky-Golay filter windows operate at the recording's native rate rather than the editor's much higher frame rate.

### MainMenu redesign

- World-space panel with one button per available scene (Calibrator, Sample Experiment, N-back, Maze). Per-button progress fill on gaze dwell. Eye-tracker driven gaze selection with head-direction fallback while the tracker warms up. 3-second dwell time and 6° gaze cone with 6 cm button spacing for unambiguous selection. Drops the legacy two-button cycling pattern.

### Universality & toolbox conventions

- New `docs/TOOLBOX_TUNING.md` documents the `SerializedField` vs `ScriptableObject` vs `const` decision tree for toolbox tunables. Defines how researchers customize defaults without producing repo-dirty diffs (duplicate-and-rename the canonical `.asset`). Audit identifying the 62 fields across 9 components ready for migration to ScriptableObjects in a future release.
- `IPupilSampleSource` interface + `EyeLeanPupilSampleSource` adapter make `RIPAMonitor` portable: external projects (Pupil Labs, Tobii) implement the interface and assign their adapter on the monitor. Skips disabled `EyeTracker` so replay falls through to `EyeTrackerFactory` correctly.

### Editor wizard hardening

- Scene-setup wizards (`NBackSceneSetup`, `MazeSceneSetup`) re-resolve `ScriptableObject` references after `AssetDatabase.Refresh()`. Without this, `NewScene` and intermediate asset operations could invalidate in-memory SO instances, leading to `fileID: 0` references in the saved scene (silent broken-config bug).
- `EnvironmentManager` integration left in `Skeleton/` template as the canonical pattern for per-trial environment generation; reference it when building new experiments.

---

## [1.0.0] - 2026-05-05 — Initial public release

First public release. The Unity + Python toolkit covers calibration,
recording, deterministic replay, and analysis end to end.

### Highlights

- **Calibrator with per-user `EyeTrackingProfile`.** Five-test battery
  (fixation / saccade / smooth pursuit / tuning / verification) fits a
  combined yaw/pitch correction from settled fixation samples (median
  residual; robust to blinks and mid-window saccades). Profile JSON
  saves alongside each session and auto-applies on next launch.
  Hardware-verified at 96.0% within-2° fixation accuracy, 0.40°
  median.

- **`SampleExperiment` four-phase battery.** FreeExploration,
  VisualSearch, CountingTask, ChangeDetection. Per-frame CSV +
  `SceneState` / `SceneEvents` sidecars. Production-stable; every
  phase has been hardware-verified end to end.

- **`MainMenu` launcher.** Single APK routes participants between
  the calibrator and the experiment. Auto-loads the user's
  `_default.json` profile. Gaze-dwell button UI shared with the
  calibrator.

- **`RIPAMonitor` real-time cognitive-load index.** Plug-and-play
  RIPA2 (Jayawardena, Jayawardana & Gwizdka 2025) on the live pupil
  stream. Auto-spawns a monitor + CSV column in every scene.
  `RIPAOverlay` and `RIPAGauge` give zero-setup or drop-on-Image
  on-screen indicators. `ExperimentUI.showRipaHud` toggles the
  bundled HUD strip without disabling recording.

- **Deterministic replay.** Re-runs the live experiment against
  recorded HMD pose, eye gaze, and `UnityEngine.Random.state`. Editor
  only — replay correctness is verified against recorded CSVs.

- **`Skeleton` researcher template.** Editor-side scaffold materialized
  via **VR Experiment > New Skeleton Scene**. Trial state machine
  (ITI → Platform → Fixation → ExperimentalPhase),
  `IExperimentPhaseHandler` contract, agent / environment / fixation-
  cross subsystems. Auto-wires into Eye_lean's recorder rig.

- **Python `eyelean_analysis` package.** Loads the Eye_lean CSV +
  sidecar trio. Velocity-threshold fixation/saccade detection
  (Salvucci & Goldberg 2000), K-coefficient (Krejtz et al. 2016),
  gaze entropy (Shannon; Krejtz et al. 2016 ETRA), offline LHIPA
  (Duchowski 2018), real-time RIPA2 parity (`metrics.ripa2`), batch
  processor, post-hoc profile correction. 9 example notebooks under
  `notebooks/examples/`, all plug-and-play against a bundled sample.

### Documentation

- New `docs/QUICKSTART.md` — first-time-Unity researcher walkthrough.
- `docs/README.md` indexes every component manual.
- 14 per-component manuals in `docs/<COMPONENT>.md`, all following
  the same 7-section template.
- `eyelean_analysis/README.md` covers the Python API surface and
  per-task analysis recipes.
- `RESEARCHER_GUIDE.md` covers the install → calibrate → record →
  replay → analyze flow end to end.

### Citation

`CITATION.cff` and `ACKNOWLEDGMENTS.md` are the canonical credit
surfaces. Cite the underlying algorithm paper alongside the toolkit
when the corresponding feature contributed to your analysis (RIPA2,
LHIPA, K-coefficient, Salvucci & Goldberg, Holmqvist et al., etc.).

### License

MIT.

---

## [0.6.0] - 2026-04-09 - Pre-release scaffolding

Earlier work that was previously labelled "1.0.0" before the calibrator
overhaul + MainMenu + E3 data contract landed.

### Added
- `Eye_lean > Create Replay Scene` menu item; ReplayManager + ReplayUI + EnvironmentGenerator scene wiring; `Eye_lean > Validate Replay Scene` validator
- `AnalysisConstants.cs` — unified threshold values synchronized with Python (K-coefficient 0.5, saccade velocity 50°/s, pupil bounds 1.5–9.0 mm)
- `docs/BUILD_GUIDE.md`, `docs/TROUBLESHOOTING.md`, `README_CALIBRATION.md`

### Changed
- K-coefficient thresholds aligned to 0.5 across C# and Python (Krejtz et al., 2016)
- `pyproject.toml` for modern Python packaging; package README; expanded column aliases (`head_right_*`, `head_up_*`, `session_config`, `is_debug_mode`)
- Bundle version bumped to 1.0.0 (Unity)
- `EyeMovementClassifier` switched to centralized `AnalysisConstants`

### Fixed
- CSV column alignment between C# export and Python import verified complete (84+ columns)

---

## [0.5.0] - 2025-12-21

### Added
- Git repository initialization with comprehensive .gitignore
- Root README.md for repository overview

### Changed
- Updated all documentation timestamps
- Updated GitHub repository references

---

## [0.4.0] - 2025-12-20

### Fixed
- **Critical**: Material system shader stripping on Android VR
  - All primitives now use `VRMaterialProvider.GetMaterial()`
  - Added `Unlit/Color` and `Mobile/Diffuse` to Always Included Shaders
  - Documented material system requirements

### Changed
- Updated VRMaterialProvider fallback chain for Android compatibility

---

## [0.3.0] - 2025-12-16

### Added

#### SubTask Tracking System
- Added `SubTask` column for fine-grained task tracking within phases
- `SetSubTask(string)` API for updating sub-task without changing phase
- Two-level tracking: `CurrentPhase` (main phase) + `SubTask` (specific task)

#### Phase 6E: Sample Experiment Scripts
- `SampleExperimentController.cs` - Main experiment flow with ParticipantID
- `VisualSearchManager.cs` - Find-the-target task
- `CountingTaskManager.cs` - Count colored objects task
- `ChangeDetectionManager.cs` - Spot-the-change task
- `ExperimentUI.cs` - World-space VR instructions
- Added `ParticipantID` support to CSV output

### Changed
- Updated Python loader to recognize `sub_task` column
- Fixed K-coefficient API: `classify_attention()` accepts both float and KCoefficientResult

---

## [0.2.5] - 2025-12-15

### Added

#### Phase 6A: Python Analysis Package (eyelean_analysis)
- Data loading with flexible column mapping
- Signal filters: Butterworth, Savitzky-Golay
- Eye movement classification: VelocityClassifier
- Attention metrics: K-coefficient, LHIPA, gaze entropy
- Batch processing with progress bars
- Visualization: heatmaps, trajectories, timeseries

#### Phase 6D: Jupyter Notebooks
- `quick_start.ipynb` - Sample analysis workflow
- Trial-level and multi-participant analysis examples

### Documentation
- Added `CUSTOM_METADATA_TUTORIAL.md`
- Added `SAMPLE_EXPERIMENT_SETUP.md`

---

## [0.2.4] - 2025-12-14

### Added

#### Phase 4: Consolidated Smoothing Filters
- `VergenceSmoothingMethod` enum (WeightedEMA, Butterworth, SavitzkyGolay)
- Butterworth 2nd-order IIR filter implementation
- Savitzky-Golay polynomial smoothing (5, 7, 9, 11-point windows)
- Method-specific settings structs

### Documentation
- Added academic citations for all smoothing methods
- Updated `ALGORITHMS.md` with filter documentation

---

## [0.2.3] - 2025-05-26

### Added

#### Phase 3: Unified Data Format & Settings
- `VergenceSettingsFile` wrapper for JSON serialization
- Editor-based Export/Import via ContextMenu
- `DataExportSettings` ScriptableObject integration
- CSV metadata headers (`#`-prefixed comments)
- Configurable flush interval

---

## [0.2.2] - 2025-05-26

### Added

#### Phase 2: Calibrator Migration
- `CalibrationSessionManager` - Session flow orchestration
- `CalibrationTestRunner` base class
- `FixationTestRunner` - 7 fixation targets, 2s each
- `SmoothPursuitTestRunner` - Figure-8 moving target
- `SaccadeTestRunner` - Rapid eye movement test
- `GroundTruthValidator` - Accuracy validation
- `CalibrationWorldUI` - VR world-space UI

### Fixed
- Vergence point Y-axis offset (world-space origins)
- Target positioning at fixed room coordinates
- Validation system with vergence collision checking

### Changed
- Quality rating thresholds adjusted for VR hardware limitations
- Report format: per-test accuracy percentages
- Visual improvements: Orange gazed color, Cyan vergence point

---

## [0.2.0] - 2025-12-13

### Added

#### Phase 1.5: Data Quality Metrics System
- `DataQualityMetrics` component
  - Blink detection via eye openness thresholds
  - Tracking loss sample counting
  - Stuck ray detection (60+ frames unchanged)
  - Quality rating (Excellent/Good/Acceptable/Poor/Unusable)
- Auto-integration with `SimpleEyeTracker`
- New public API: `GetQualityMetrics()`, `LogQualitySummary()`, `ResetQualityMetrics()`

---

## [0.1.0] - 2024-12-12

### Added

#### Phase 1: Foundation & Eye Tracker Abstraction
- `IEyeTracker` interface for multi-device support
- `IEyeTrackerExtended` interface with feature flags
- `EyeTrackerFactory` for automatic device detection
- `OpenXREyeTrackerProvider` wrapping VIVE OpenXR
- `NullEyeTracker` for graceful fallback

#### Documentation
- `README.md` - Project overview
- `docs/SETUP.md` - Hardware and software setup
- `docs/ARCHITECTURE.md` - System design
- `docs/ALGORITHMS.md` - Mathematical documentation
- `docs/DATA_SCHEMA.md` - CSV field definitions
- `docs/BIBLIOGRAPHY.md` - Citations
- `CITATION.cff` - Citation metadata
- `LICENSE` - MIT License

### Changed
- Updated `SimpleEyeTracker` to use `IEyeTracker` interface
- Removed `#if USE_WAVE_SDK / USE_OPENXR` conditionals

---

## [0.0.1] - 2024-12-10

### Added

#### Phase 0: Unity 6.3 Migration
- Unity 6.3 project setup with URP
- VIVE OpenXR 2.5.1 package integration
- `OpenXREyeTracker.cs` - Low-level VIVE API wrapper
- `SimpleEyeTracker.cs` - Main data collection (~2300 lines)
- `ResearchDataStructure.cs` - Data structures and CSV export
- `DebugFileLogger.cs` - File-based debug logging
- `VRMaterialProvider.cs` - Reliable material creation
- Environment generation scripts

### Fixed
- URP material shader stripping issue
- Android external storage for CSV export
- Package identifier (com.RutgersVCL.Eye_lean)

---

## Version History Summary

| Version | Date | Phase | Description |
|---------|------|-------|-------------|
| 1.4.0 | 2026-05-04 | - | Skeleton researcher template + per-component documentation hub (`docs/<COMPONENT>.md` for every major surface) |
| 1.3.0 | 2026-05-04 | - | Plug-and-play RIPA2 cognitive-load monitor; on-device metric swapped sym4 LHIPA → RIPA2 (Jayawardena 2025); CSV `# FileVersion 1.0 → 1.1` |
| 1.2.0 | 2026-05-03 | - | Deterministic replay + Python plug-and-play notebook suite |
| 1.0.0 | TBD (target 2026-05-13) | - | **Public Release** — Calibrator overhaul, MainMenu launcher, E3 data contract |
| 0.6.0 | 2026-04-09 | - | Pre-release scaffolding |
| 0.5.0 | 2025-12-21 | - | Git repository setup |
| 0.4.0 | 2025-12-20 | - | Material system fix for Android |
| 0.3.0 | 2025-12-16 | 6E | Sample experiment scripts |
| 0.2.5 | 2025-12-15 | 6A, 6D | Python analysis package |
| 0.2.4 | 2025-12-14 | 4 | Smoothing filter consolidation |
| 0.2.3 | 2025-05-26 | 3 | Unified data format |
| 0.2.2 | 2025-05-26 | 2 | Calibrator migration |
| 0.2.0 | 2025-12-13 | 1.5 | Data quality metrics |
| 0.1.0 | 2024-12-12 | 1 | Eye tracker abstraction |
| 0.0.1 | 2024-12-10 | 0 | Unity 6.3 migration |

---

*For detailed technical documentation, see [docs/](docs/)*
