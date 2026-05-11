# Eye-LEAN

[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.20040453.svg)](https://doi.org/10.5281/zenodo.20040453)
[![PyPI](https://img.shields.io/pypi/v/eyelean-analysis.svg)](https://pypi.org/project/eyelean-analysis/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Eye-LEAN** (**L**ocomotion, **E**xploration, **A**ction, and **N**avigation with **Eye** Tracking): a Behavioral Research Toolkit for Data Rich Virtual Reality Experiments.

## What it is

Eye-LEAN is a Unity + Python toolkit for visualizing, collecting and analyzing
eye-tracking data from virtual-reality head-mounted displays. The Unity
project was tested on the VIVE Focus Vision headsets (but is built on the OpenXR standard)
and writes per-frame binocular gaze, pupil, and head-tracking data to CSV. The
Python package loads those CSVs, detects fixations and saccades,
computes attention and cognitive-load metrics (K-coefficient, RIPA2,
LHIPA, gaze entropy), and visualizes results. The two halves are
designed to work together but can also be used independently: the CSV
format is documented and stable, and the analysis package accepts data
from any source that matches the schema.

In memory of Professor Eileen Kowler.

## Audience

Behavioral researchers who want to conduct VR experiments with eye tracking.

## Prerequisites

- Unity 6000.3.9f1 (Unity-side work)
- VIVE OpenXR 2.5.1
- HTC VIVE Focus Vision (or another OpenXR eye-tracking headset - NOT TESTED)
- Python 3.10+ (analysis side)

---

## Components

| Component | Purpose | Location |
|-----------|---------|----------|
| Unity toolkit | Data collection on VIVE Focus Vision | `Eye_lean_Unity_Project/Eye_lean/` |
| Python analysis package | Loading, classification, metrics, visualization | `eyelean_analysis/` |
| Sample experiment | Four-phase cognitive task battery (FreeExploration, VisualSearch, CountingTask, ChangeDetection) | Unity `SampleExperiment` scene |
| Replay system | Offline playback and inspection of recorded sessions | Unity `ReplayScene` (editor only) |

## Scene flow
Make sure to run the device-level eye calibration first.
To avoid issues with the environment building in the calibration and sample experiment,
we recommend resetting the device's view right before starting the app.
The UI is fully gaze-based, no controllers needed.

Three scenes ship as build targets in the APK; a fourth runs only in
the Unity editor.

1. `MainMenu.unity` (build index 0) — launcher. Reuses the calibrator's
   gaze-dwell button system to route the participant to either the
   calibrator or the experiment scene. Auto-loads the user's
   `_default.json` calibration profile if one is present.
2. `CalibrationScene.unity` (build index 1) — calibrator. Runs three
   ground-truth tests (fixation, saccade, smooth pursuit) plus a
   tuning phase that fits a per-user yaw/pitch correction and a
   verification phase that re-tests with the new profile applied.
   Saves an `EyeTrackingProfile` JSON next to the CSV for reuse.
3. `SampleExperiment.unity` (build index 2) — four-phase battery
   (FreeExploration, VisualSearch, CountingTask, ChangeDetection).
   Records CSV with a `CurrentPhase` column so per-phase analysis is
   automatic on the Python side. The
   [`RIPAMonitor`](docs/RIPA_MONITOR.md) drop-in component writes a
   real-time RIPA2 cognitive-load index to the `LiveLoadIndex` column
   on every row.
4. `ReplayScene.unity` (editor only) — loads a recorded CSV and steps
   through it with synchronized gaze rays, vergence point, and head
   pose. Used during analysis to spot-check an interesting trial.

---

## Quick start

### Unity

- **First time using Unity?** Work through
  [`docs/QUICKSTART.md`](docs/QUICKSTART.md) — install Unity Hub,
  open the project, hit Play. ~30 minutes end to end.
- **Already comfortable with Unity?**

  ```bash
  git clone https://github.com/kubasuchy/Eye-LEAN.git
  ```

  Open `Eye_lean_Unity_Project/Eye_lean/` in Unity 6000.3.9f1, then
  follow [`docs/BUILD_GUIDE.md`](docs/BUILD_GUIDE.md) for the APK
  build steps and
  [`docs/SETUP.md`](Eye_lean_Unity_Project/Eye_lean/docs/SETUP.md) for
  hardware setup.

#### Verify

Unity opens the project without compile errors and the four scenes
(`MainMenu`, `CalibrationScene`, `SampleExperiment`, `ReplayScene`)
appear under `Assets/Scenes/`.

### Python

1. Install the analysis package:

   ```bash
   pip install -e ./eyelean_analysis            # core
   pip install -e "./eyelean_analysis[all]"     # core + visualization + tests
   ```

2. Open the example notebook suite under
   [`eyelean_analysis/notebooks/examples/`](eyelean_analysis/notebooks/examples/).
   Every notebook auto-discovers data via one line and falls back to a
   bundled v1.2 sample on a fresh checkout, so `Run All` works
   end-to-end before any recording exists:

   ```python
   import eyelean_analysis as ela
   ctx = ela.notebook_context()      # arg -> EYELEAN_CSV -> Logs/ -> bundled
   print(ctx)
   ```

3. Start with `01_quickstart.ipynb`. The
   [notebook index](eyelean_analysis/notebooks/README.md) covers what
   each notebook does and the recommended run order.

#### Verify

`ctx` prints a path to a real CSV (either one in `Logs/`, the path in
`EYELEAN_CSV`, or the bundled sample) and `Run All` completes without
errors.

#### Programmatic example

```python
import eyelean_analysis as ela

data = ela.load_eyetracking("Logs/EyeTracking_<session>.csv")
movements = ela.detect_eye_movements(
    yaw_deg, pitch_deg, data.get_timestamps()
)
report = ela.analyze_sample_experiment(
    "Logs/EyeTracking_<session>.csv",
    results_json_path="Logs/experiment_results_<session>.json",
)
print(report.to_dataframe())
```

The reusable conversion from world-space gaze direction columns
(`CombinedDir_X/Y/Z`) to angular yaw/pitch in degrees is documented
in `04_eye_movements.ipynb`.

#### v1.2 scene-state and event sidecars

When scene recording is enabled, each session writes two sidecars
alongside the main CSV. The Python loader knows how to find and join
them:

```python
state, events = ela.load_scene_sidecars(
    "Logs/EyeTracking_<session>.csv", decode_config=True,
)
joined = ela.merge_gaze_with_scene_state(data.df, state)
```

Notebook `07_scene_sidecars.ipynb` walks through the full pattern
including event-aligned analyses and Spawn/Despawn lifetime tracking.

### Post-hoc calibration correction

A saved `EyeTrackingProfile` can be re-applied to any recording made
without it active:

```python
ela.apply_profile_to_csv(
    "Logs/HTC VIVE Focus Vision_<timestamp>.json",
    "Logs/EyeTracking_<session>.csv",
    "Logs/EyeTracking_<session>_corrected.csv",
)
```

See [`docs/POST_HOC_CORRECTION.md`](docs/POST_HOC_CORRECTION.md) for
the math, the schema, and when re-correcting an already-corrected
recording will compound the offset.

---

## CSV format

Each recording starts with a metadata block of `# Key: value` lines
followed by the column header. The metadata captures everything
needed to reproduce the analysis later:

```
# CoordinateOrigin: 0.0000,0.0000,0.0000
# CoordinateOriginSet: False
# Profile: HTC VIVE Focus Vision_20260430_111559
# ProfileCombinedYawDeg: -0.2618
# ProfileCombinedPitchDeg: -0.3985
UnityTimestamp,RealTimeSinceStartup,...,CurrentPhase,...,CombinedDir_X,...
```

`CoordinateOrigin` and `CoordinateOriginSet` indicate whether
position columns are world-space or normalized to a trial-start
anchor. `Profile` records which calibration correction was already
applied live. The Python loader treats `#` lines as comments by
default; `eyelean_analysis.read_csv_metadata(path)` returns the
block as a dict for inspection.

Column definitions are in
[`docs/DATA_SCHEMA.md`](Eye_lean_Unity_Project/Eye_lean/docs/DATA_SCHEMA.md).

---

## Project layout

```
Eye_lean/
  Eye_lean_Unity_Project/Eye_lean/    Unity project (collection)
    Assets/Scripts/EyeTracking/         Core eye-tracking pipeline
    Assets/Scripts/Replay/              CSV playback
    Assets/Scenes/                      MainMenu, CalibrationScene,
                                        SampleExperiment, ReplayScene
    Assets/Tests/EditMode/              NUnit tests for the
                                        calibration math
    docs/                               Architecture, algorithms,
                                        data schema
  eyelean_analysis/                   Python package (analysis)
    data/                               Loader + metadata
    classification/                     Velocity-based classifier
    metrics/                            LHIPA, entropy, K-coefficient
    experiments/                        Per-phase report builder
    calibration/                        Post-hoc profile correction
    tests/                              pytest suite
    notebooks/examples/                 9-notebook example suite
    notebook_context.py                 Auto-discovery for notebooks
  docs/                                Cross-cutting guides
    BUILD_GUIDE.md
    POST_HOC_CORRECTION.md
    TROUBLESHOOTING.md
  LICENSE
  CONTRIBUTING.md
```

---

## Documentation

| Document | Topic |
|----------|-------|
| [Build guide](docs/BUILD_GUIDE.md) | APK build process |
| [Setup guide](Eye_lean_Unity_Project/Eye_lean/docs/SETUP.md) | Hardware and software setup |
| [Architecture](Eye_lean_Unity_Project/Eye_lean/docs/ARCHITECTURE.md) | Unity-side system design |
| [Algorithms](Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md) | Vergence and smoothing math |
| [Data schema](Eye_lean_Unity_Project/Eye_lean/docs/DATA_SCHEMA.md) | CSV column definitions |
| [Sample experiment](Eye_lean_Unity_Project/Eye_lean/docs/SAMPLE_EXPERIMENT_SETUP.md) | Four-phase battery setup |
| [Custom metadata](Eye_lean_Unity_Project/Eye_lean/docs/CUSTOM_METADATA_TUTORIAL.md) | Adding experiment variables |
| [Post-hoc correction](docs/POST_HOC_CORRECTION.md) | Re-applying a profile to a recorded CSV |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Common issues |
| [Bibliography](Eye_lean_Unity_Project/Eye_lean/docs/BIBLIOGRAPHY.md) | Citations |

The Python package has its own
[`README`](eyelean_analysis/README.md) covering installation flavors
and the full API surface.

---

## Hardware

| Device | Eye-tracking sample rate | Status |
|--------|--------------------------|--------|
| HTC VIVE Focus Vision | up to 120 Hz; 90 Hz observed in current builds | Supported |
| Varjo XR-3 / VR-3 | 200 Hz | Planned |
| HoloLens 2 | 30 Hz | Planned |

The Unity-side abstraction (`IEyeTracker` and `EyeTrackerFactory`) is
device-agnostic; adding a new device requires implementing one
interface and registering it with the factory.

---

## Retrieving data from the headset

```bash
adb pull /sdcard/Android/data/com.RutgersVCL.Eye_lean/files/ ./Logs/
adb logcat -s Unity                          # live logs
```
Or use Android Studio / Android File Transfer to download the files from the headset.

CSVs and profile JSONs land in the same directory; the explore
notebook expects them in `Logs/` at the repo root.

---

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
Citing the underlying algorithms (RIPA2 — Jayawardena 2025; LHIPA —
Duchowski 2018; Rocketbox avatars — Gonzalez-Franco 2020) is also
expected when the corresponding feature contributed to your
analysis. See [`ACKNOWLEDGMENTS.md`](ACKNOWLEDGMENTS.md) for the
full citation list.

## Authors

**Jakub Suchojad**¹, **Kavindya Dalawella**¹, **Serena DeStefani**², **Karin Stromswold**¹, **Jacob Feldman**¹.

¹ Rutgers University
² Ohio State University

Primary author and maintainer: Jakub Suchojad ([jhs212@scarletmail.rutgers.edu](mailto:jhs212@scarletmail.rutgers.edu)), Visual Cognition Lab, Rutgers University–New Brunswick. See [`AUTHORS.md`](AUTHORS.md) for full author details.

## Acknowledgments

For prior work, software dependencies, and the people / labs whose
contributions made Eye_lean possible, see
[`ACKNOWLEDGMENTS.md`](ACKNOWLEDGMENTS.md).

## License

MIT License. See [`LICENSE`](LICENSE).

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md).
