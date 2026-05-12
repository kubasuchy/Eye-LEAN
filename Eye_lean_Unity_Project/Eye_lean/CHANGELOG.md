# Changelog

All notable changes to the VR Eye Tracking Research Toolkit will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

- World-fixed scene placement: room, UI panels, and answer rows now sit parallel to the back wall regardless of head heading at scene-load.
- Visual search: per-trial schedule interleaves color pop-out, shape pop-out, and conjunction trials over an 8 → 28 set-size sweep.
- Calibration: post-fit verification trusts the same-sample fit residual when the small-sample re-test regresses by sampling noise.
- Calibration: rewrite of the offset/gain fit. `OffsetEstimator` now runs a Theil-Sen joint offset+gain regression per axis and composes the result cumulatively with the prior profile. Verification gate switched to median-only (the 5pp accuracy-pct gate flipped on binomial noise). Gain estimation is gated on per-axis sample count, total eccentricity span, and the interquartile range of measured-axis values — the IQR gate prevents noise-driven gain blowup when the bulk of fixation samples sits in a tight central cluster with one or two outlier targets at each extreme. `[ActiveProfile] Applied` log line now surfaces gain alongside offset so non-unit gains are visible at runtime.
- Analysis: `fixation_entropy` returns paired SGE + GTE per Shiferaw 2019 / Krejtz 2015; `analyze_sample_experiment` reports both per phase.
- Analysis: K-coefficient warns and returns `UNKNOWN` without `pooled_stats`; notebooks 04/05 rewritten for per-phase analysis.
- Analysis: loader `low_memory=False`; batch pupil averaging masks before division.

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
