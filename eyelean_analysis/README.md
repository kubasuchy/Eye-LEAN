# eyelean_analysis — Python Researcher Guide

The Python half of [Eye-LEAN](../README.md). Loads the CSVs the Unity toolkit
writes, then runs signal processing, eye-movement classification, attention
and cognitive-load metrics, per-phase reports for the sample experiment,
post-hoc calibration correction, and batch processing.

This is the "I have a recording, what do I do with it?" walkthrough. The
Unity-side [RESEARCHER_GUIDE.md](../Eye_lean_Unity_Project/Eye_lean/docs/RESEARCHER_GUIDE.md)
covers recording and replay; this guide picks up where that one ends.

## Contents

- [Concepts at a glance](#concepts-at-a-glance)
- [Installation](#installation)
- [Quick start](#quick-start)
- [The data model](#the-data-model)
- [Loading and inspection](#loading-and-inspection)
- [Scene-state and event sidecars (v1.2+)](#scene-state-and-event-sidecars-v12)
- [Eye-movement classification](#eye-movement-classification)
- [Pupillary cognitive load (LHIPA)](#pupillary-cognitive-load-lhipa)
- [Data quality metrics](#data-quality-metrics)
- [Per-task analysis recipes (sample experiment)](#per-task-analysis-recipes-sample-experiment)
- [Custom experiments](#custom-experiments)
- [Post-hoc calibration correction](#post-hoc-calibration-correction)
- [Batch processing](#batch-processing)
- [Performance notes](#performance-notes)
- [Troubleshooting](#troubleshooting)
- [API reference](#api-reference)
- [References](#references)

## Concepts at a glance

A live Eye_lean session writes one to three CSV files into `Logs/` (or
`Assets/StreamingAssets/` when running from the editor against the bundled
sample):

| File | Always present | Contents |
|---|---|---|
| `EyeTracking_<session>.csv` | yes | Per-frame gaze, pupil, head pose, vergence, validity flags. |
| `EyeTracking_<session>_SceneState.csv` | when scene recording is enabled | Per-frame transforms (pose + active flag) for every Recordable in the scene. |
| `EyeTracking_<session>_SceneEvents.csv` | when scene recording is enabled | Discrete events (Spawn/Despawn, Config\*, ShowInstruction, CountingAnswerSelected, ChangeDetectionFeedback, RandomStateSnapshot, …). |

Plus optional sidecar JSON:

- `experiment_results_<session>.json` — per-trial outcome records from
  the sample experiment (target acquisition times, counting answers,
  change-detection accuracy).
- `<HMD>_<timestamp>.json` — `EyeTrackingProfile` snapshot the
  calibrator saved. Used for post-hoc calibration correction.

The Python package treats the main CSV as authoritative and the
sidecars as optional joins.

## Installation

Python 3.9+ is supported.

```bash
# Stable release from PyPI (recommended for users):
pip install eyelean-analysis
pip install "eyelean-analysis[all]"          # + visualization, jupyter, batch, …

# Development install from a local clone (recommended for contributors):
pip install -e ./eyelean_analysis            # core
pip install -e "./eyelean_analysis[test]"    # core + pytest + PyWavelets
pip install -e "./eyelean_analysis[all]"     # everything
```

Optional extras declared in `pyproject.toml`:

| Extra | Adds | Needed for |
|---|---|---|
| `wavelet` | PyWavelets | LHIPA |
| `visualization` | matplotlib | The plotting helpers |
| `batch` | joblib, tqdm | `BatchProcessor` parallel + progress |
| `jupyter` | jupyter, jupyterlab, ipywidgets | The example notebooks |
| `export` | pyreadstat | SPSS export |
| `test` | pytest, PyWavelets | Running the test suite |

`scipy`, `numpy`, `pandas` are all in the core install.

## Quick start

The fastest path is the **9-notebook plug-and-play suite** under
[`notebooks/examples/`](notebooks/examples/) — see the
[notebook index](notebooks/README.md) for what each one covers. Every
notebook auto-discovers your data via one line:

```python
ctx = ela.notebook_context()      # arg → env → Logs/ → bundled sample
print(ctx)                        # see what got resolved
```

Run on a fresh checkout:

```bash
jupyter lab eyelean_analysis/notebooks/examples/01_quickstart.ipynb
```

The bundled v1.2 sample at
`Eye_lean_Unity_Project/Eye_lean/Assets/StreamingAssets/` is the
fallback, so every notebook works end-to-end before you've recorded
anything yourself.

Programmatically without a notebook, five lines get you from a
recording to a scanpath plot:

```python
import eyelean_analysis as ela
import matplotlib.pyplot as plt

data = ela.load_eyetracking("Logs/EyeTracking_<session>.csv")
gaze = data.compute_gaze_points(distance=2.0)
ela.create_trajectory_plot(gaze["gaze_x"], gaze["gaze_z"])
plt.show()
```

## The data model

`load_eyetracking(path)` returns an `EyeLeanData` wrapper around a
pandas DataFrame. Two key conventions matter when interpreting columns:

### Column aliasing

The Unity toolkit has emitted at least two CSV schemas across its
lifetime (Flask-app era and the current Unity-direct era). The loader
maps both to a single canonical schema before handing the DataFrame
back. Canonical names are snake_case
(`combined_dir_x`, `head_pos_y`, `left_pupil_diameter`, …); the full
map is `eyelean_analysis.COLUMN_ALIASES`.

If `standardize_columns=True` (the default), the DataFrame columns are
the canonical names. Pass `standardize_columns=False` to keep the
original column casing.

### Coordinate normalization

`HeadPos_*` and the per-eye `*Origin_*` columns are written
**normalized to the trial-start world position**, not in absolute
world space. The normalization origin is recorded in the metadata
header as `# CoordinateOrigin: x,y,z`. To recover absolute world-space
positions, add that origin back to the normalized columns:

```python
meta = ela.read_csv_metadata("Logs/EyeTracking_<session>.csv")
ox, oy, oz = (float(c) for c in meta["CoordinateOrigin"].split(","))
abs_head_x = data.df["head_pos_x"] + ox
```

`CombinedDir_*` is a unit vector in **world space**, not normalized.

### Timestamps

`UnityTimestamp` is `Time.realtimeSinceStartup` at sample acquisition,
in seconds since process start. It's monotonic and dense at the eye
tracker's native rate (90 Hz on VIVE Focus Vision). Use
`data.get_timestamps()` to access it as a NumPy array;
`data.get_sample_rate()` returns the median-derived rate.

For the full schema, see the top-level
[DATA_SCHEMA.md](../docs/DATA_SCHEMA.md).

## Loading and inspection

```python
import eyelean_analysis as ela

data = ela.load_eyetracking("Logs/EyeTracking_<session>.csv")

print(data.summary())          # dict: n_samples, sample_rate, phases, …
print(data.duration, "s")
print(data.get_phases())       # ['Recording'] or ['FreeExploration', …]

# Direct DataFrame access for arbitrary slicing
df = data.df
free_explore = df[df["phase"] == "FreeExploration"]
```

The metadata header above the column row carries information that
can't be re-derived from the data alone:

```python
meta = ela.read_csv_metadata("Logs/EyeTracking_<session>.csv")
print(meta.get("Profile", "none"))           # which calibration profile
                                              # was active at recording
print(meta["CoordinateOrigin"])               # de-normalization origin
print(meta.get("FileVersion"))                # schema version
```

Use this — not the `# Profile:` header in the file content — to decide
whether a recording needs post-hoc correction.

## Scene-state and event sidecars (v1.2+)

When scene recording is enabled in Unity (default for the sample
experiment), each session also writes two sidecars next to the main
CSV. The package loads them with the same metadata-header tolerance:

```python
state = ela.load_scene_state("Logs/EyeTracking_<session>_SceneState.csv")
events = ela.load_scene_events(
    "Logs/EyeTracking_<session>_SceneEvents.csv",
    decode_config=True,    # base64-decode the Detail of Config* events
)
```

Or load both at once given the main CSV path:

```python
state, events = ela.load_scene_sidecars(
    "Logs/EyeTracking_<session>.csv",
    decode_config=True,
)
```

`state` is one row per (frame, recordable) pair. `events` is one row
per discrete event; the `Detail` column is free-text for most types
and base64-encoded JSON for `Config*` events (set `decode_config=True`
to materialize a `Config` dict column).

To answer "which Recordable was the participant gazing at on a given
frame?", join the gaze DataFrame with scene-state on `Frame`:

```python
joined = ela.merge_gaze_with_scene_state(
    data.df, state,
    object_id=None,           # all recordables; or pass a stable id
)
# `joined` has gaze columns + Pos_X/Pos_Y/Pos_Z + ObjectId for every
# (sample, recordable-present-that-frame) pair.
```

`ObjectId` is a stable hash from Unity's seeded-id system: it's
identical across replay runs of the same recording, so analyses that
key on an object id are deterministic.

## Eye-movement classification

`detect_eye_movements` runs an I-VT (velocity-threshold) classifier on
angular gaze data. Inputs are angles in **degrees**, not direction
vectors:

```python
import numpy as np

dx = data.df["combined_dir_x"].to_numpy()
dy = data.df["combined_dir_y"].to_numpy()
dz = data.df["combined_dir_z"].to_numpy()
yaw_deg   = np.degrees(np.arctan2(dx, dz))
pitch_deg = -np.degrees(np.arcsin(np.clip(dy, -1.0, 1.0)))

mv = ela.detect_eye_movements(yaw_deg, pitch_deg, data.get_timestamps())
print(len(mv["fixations"]), "fixations,",
      len(mv["saccades"]),  "saccades")
```

Each fixation has `start_time`, `end_time`, `duration`, `centroid_x`,
`centroid_y`. Each saccade has `amplitude`, `peak_velocity`,
`mean_velocity`. Defaults follow the I-VT literature
(50 deg/s threshold, 75 ms merge window, 100 ms minimum fixation);
override via `velocity_threshold=`, `min_fixation_duration=`,
`merge_threshold=` keyword args.

For attention typing, feed the classified movements into the
K-coefficient calculator (Krejtz et al. 2016):

```python
k = ela.calculate_k_coefficient(mv["fixations"], mv["saccades"])
print(k.k_coefficient, k.attention_type)  # K and FOCAL/AMBIENT/NEUTRAL
```

## Pupillary cognitive load (LHIPA)

LHIPA (Duchowski et al. 2018) is a wavelet-based cognitive-load index
computed on a continuous pupil-diameter signal. As of v1.3 the on-device
live monitor uses RIPA2 (Jayawardena et al. 2025) instead of LHIPA;
LHIPA remains available for offline analysis through this Python
package. For per-frame on-device cognitive load, read the
`LiveLoadIndex` column produced by `RIPAMonitor`
(see `docs/RIPA_MONITOR.md`).

```python
left  = data.df["left_pupil_diameter"].to_numpy()
right = data.df["right_pupil_diameter"].to_numpy()
pupil = np.nanmean([left, right], axis=0)

result = ela.calculate_lhipa(pupil, sample_rate=data.get_sample_rate())
print(result.lhipa, result.is_valid)
```

Higher LHIPA ≈ higher cognitive load. The metric needs at least
~5 seconds of continuous pupil data; shorter windows return
`is_valid=False`.

## Data quality metrics

`calculate_quality_metrics` produces a single struct summarizing the
recording's signal quality — useful for batch reports and for
filtering out bad sessions before downstream analysis:

```python
q = ela.calculate_quality_metrics(
    timestamps   = data.get_timestamps(),
    validity     = data.df["is_tracking_valid"].to_numpy()
                   if "is_tracking_valid" in data.df.columns else None,
    left_pupil   = data.df["left_pupil_diameter"].to_numpy(),
    right_pupil  = data.df["right_pupil_diameter"].to_numpy(),
    left_openness  = data.df.get("left_openness"),
    right_openness = data.df.get("right_openness"),
)
print(q.validity_percent, q.n_gaps, q.n_blinks, q.quality_score)
```

`quality_score` is a 0-100 composite; `validity_percent`,
`sample_rate_stability`, `n_gaps`, `n_blinks`, and pupil-range
diagnostics are exposed individually.

## Per-task analysis recipes (sample experiment)

The bundled SampleExperiment cycles four phases:

1. **FreeExploration** — open look-around in a research room.
2. **VisualSearch** — find target spheres among distractors.
3. **CountingTask** — enumerate target shapes.
4. **ChangeDetection** — flicker paradigm; spot which object moved.

The fastest way to a per-phase report:

```python
report = ela.analyze_sample_experiment(
    "Logs/EyeTracking_<session>.csv",
    results_json_path="Logs/experiment_results_<session>.json",
)
df = report.to_dataframe()      # one row per phase
```

Missing columns and short phases populate `missing_metrics` rather
than raising. To go deeper into one phase, slice the DataFrame on
`phase` and run the per-phase tools.

### FreeExploration → fixation map

```python
free = data.df[data.df["phase"] == "FreeExploration"]
yaw   =  np.degrees(np.arctan2(free["combined_dir_x"], free["combined_dir_z"]))
pitch = -np.degrees(np.arcsin(np.clip(free["combined_dir_y"], -1, 1)))

mv = ela.detect_eye_movements(yaw.values, pitch.values,
                              free["timestamp"].values)
ela.create_fixation_plot(mv["fixations"])  # bubble plot, area ∝ duration
```

### VisualSearch → target acquisition time

The results JSON records per-trial acquisition times directly. To
verify them against the gaze stream, use the events sidecar:

```python
events = ela.load_scene_events(
    "Logs/EyeTracking_<session>_SceneEvents.csv",
)
# Each VisualSearch trial emits a Spawn for the target and a Despawn
# when the participant fixates on it long enough.
trial_starts = events[(events["EventType"] == "Spawn")
                      & events["ObjectId"].str.startswith("vs_target_")]
trial_ends   = events[(events["EventType"] == "Despawn")
                      & events["ObjectId"].str.startswith("vs_target_")]
durations = (trial_ends["T"].values - trial_starts["T"].values)
```

### CountingTask → accuracy

```python
answers = events[events["EventType"] == "CountingAnswerSelected"]
# Detail is the chosen number; correct answer is in the Config payload.
config_row = events[events["EventType"] == "ConfigCounting"].iloc[0]
# When loaded with decode_config=True, config_row["Config"] is a dict.
```

### ChangeDetection → detection latency

```python
feedback = events[events["EventType"] == "ChangeDetectionFeedback"]
# Detail = "Correct" or "Incorrect"; T = when the answer was finalized.
hide   = events[events["EventType"] == "ChangeDetectionHideScene"]
shown  = events[events["EventType"] == "ChangeDetectionShowChangedScene"]
# Latency: feedback["T"] - shown["T"], paired by trial order.
```

For more elaborate per-trial joins, scenes are spawned with stable
ObjectIds; pair Spawn → Despawn pairs by id within each phase.

## Custom experiments

Researchers building experiments on top of Eye_lean don't need to fork
the analysis package. The patterns:

**Filter to your phase names.** `phase` is a free-text column written
by your experiment controller; once you set it, this works:

```python
mine = data.df[data.df["phase"].isin(["MyPhase1", "MyPhase2"])]
```

**Consume custom CSV columns.** `SessionRecorder` lets you append
extra columns at session start; they reach the DataFrame untouched
(no aliasing). Either reference them by their literal name or extend
the alias map at load time:

```python
loader = ela.EyeLeanLoader(custom_aliases={
    "my_metric": ["MyMetric", "myMetric_v2"],
})
data = loader.load("...csv")
```

**Read your own event types.** The events sidecar accepts arbitrary
`EventType` strings; everything except `Config*` (base64+JSON) is
stored as plain text in `Detail`. Filter on the type you care about:

```python
mine = events[events["EventType"] == "MyExperimentLandmark"]
```

**Align scene-state to gaze.** The seeded-id system makes ObjectIds
stable across runs of the same scene, so analyses keyed on a specific
recordable are reproducible:

```python
joined = ela.merge_gaze_with_scene_state(
    data.df, state, object_id="my_stable_id"
)
```

## Post-hoc calibration correction

If a recording was made before a better calibration profile became
available — or you want to compare the same recording under several
profiles — re-apply a saved `EyeTrackingProfile` to the CSV:

```python
stats = ela.apply_profile_to_csv(
    "Logs/HTC VIVE Focus Vision_<timestamp>.json",
    "Logs/EyeTracking_<session>.csv",
    "Logs/EyeTracking_<session>_corrected.csv",
)
print(stats["samples_corrected_combined"],
      stats["samples_skipped_invalid"])
```

The math, schema, and the **compounding-offsets foot-gun** (never
apply correction to an already-corrected CSV — the metadata's
`# Profile:` line is the canonical "was a profile active at
recording" flag) are documented in
[`docs/POST_HOC_CORRECTION.md`](../docs/POST_HOC_CORRECTION.md).

## Batch processing

`BatchProcessor` runs the same analysis pipeline across many CSVs,
returning a flat per-file summary suitable for cross-participant
analyses:

```python
summary = ela.process_directory_batch(
    "data/recordings/",
    pattern="*.csv",
    output_path="data/results/summary.csv",
    n_workers=4,
)
```

For more control (custom analyzers, partial pipelines), use the class
directly:

```python
proc = ela.BatchProcessor(
    n_workers=4,
    compute_lhipa=True,
    compute_entropy=True,
    compute_fixations=True,
)
proc.add_analyzer(lambda d: {"my_metric": d.df["my_col"].mean()})
results = proc.process_directory("data/recordings/", pattern="*.csv")
df = ela.BatchProcessor.results_to_dataframe(results)
```

The full pipeline runs at ~1× wall-clock (a 5-minute recording takes
~5-10 seconds end-to-end, single-threaded).

## Performance notes

- A typical 5-minute recording at 90 Hz is ~27,000 rows. At ~100 KB
  per row of CSV, that's ~3 MB on disk; pandas loads it in <1 s.
- Scene-state sidecars are larger (one row per recordable per frame).
  A 4-minute SampleExperiment recording with ~10 active recordables
  produces ~120,000 state rows; that loads in ~0.3 s.
- LHIPA dominates batch runtime: ~2-3 s per recording for the wavelet
  decomposition. Disable it (`compute_lhipa=False`) when you don't
  need it.
- Memory ceiling: a single recording's full DataFrame is well under
  100 MB; you can hold dozens in memory before pressure becomes a
  concern. For larger sweeps, use `process_directory_batch` with
  `n_workers > 1` and let it stream the per-file summary to disk.
- The fixation/saccade classifier and quality metrics are O(n) in
  sample count; nothing in the package is intentionally super-linear.

## Troubleshooting

**Loader warns about mixed-type columns.** Some optional columns
(per-frame phase strings, partial validity flags) only fill in once
the experiment has actually started. The first few hundred rows are
NaN, the rest are strings. Pass `low_memory=False` to silence:

```python
data = ela.load_eyetracking("...csv", low_memory=False)
```

**`SceneState.csv` exists but is empty.** Check the metadata header:
`SampleEveryNthFrame` controls write rate, and the recorder needs at
least one Recordable in the scene. The bundled
SampleExperimentController auto-registers the right Recordables; for
custom scenes, attach a `Recordable` component to anything you want
recorded.

**Post-hoc correction produces near-zero offsets but the recording
looks miscalibrated.** The CSV may already be corrected. Read
`read_csv_metadata(path)["Profile"]` — if it's anything other than
`none`, that profile was applied live and a second correction will
compound the offsets.

**`merge_gaze_with_scene_state` produces more rows than the gaze
DataFrame.** That's expected when multiple recordables are present in
the scene each frame: the merge is many-to-many on `Frame`. Pass
`object_id=` to restrict to a single recordable.

**Tests pass on dev machine but fail in CI.** Confirm the CI runner
has `PyWavelets` available — the LHIPA tests skip cleanly without it,
but a few other paths log warnings.

## API reference

Top-level `eyelean_analysis` module exports (all also available via
the subpackages they live in):

### Data loading

- `load_eyetracking(path, **kw)` → `EyeLeanData`
- `EyeLeanLoader(custom_aliases=None)` — class-based loader
- `EyeLeanData` — DataFrame wrapper with accessors
- `read_csv_metadata(path)` → `dict` of header `# Key: value` lines
- `load_scene_state(path)` → `pd.DataFrame`
- `load_scene_events(path, decode_config=False)` → `pd.DataFrame`
- `load_scene_sidecars(main_csv_path, decode_config=False)` → `(state_df, events_df)`
- `merge_gaze_with_scene_state(gaze_df, state_df, object_id=None, frame_column="frame_number")`
- `COLUMN_ALIASES` — the canonical→variants map

### Notebook bootstrap

- `notebook_context(csv=None, *, require_sidecars=False, require_profile=False, require_results=False, load=True)` → `NotebookContext`
- `NotebookContext` — dataclass with `csv_path`, `scene_state_path`, `scene_events_path`, `profile_path`, `results_path`, `repo_root`, `data`, `metadata`, `source`

### Validation

- `DataValidator(...)`, `ValidationResult`, `validate_file(path)`

### Filters

- `butterworth_filter(data, cutoff_hz, sample_rate_hz, order=4)`
- `savgol_smooth(data, window, order=3)`
- `compute_gaze_velocity(x, y, timestamps)`

### Classification

- `detect_eye_movements(x, y, t, **thresholds)` → `{"fixations": [...], "saccades": [...]}`
- `classify_fixations(...)`, `classify_saccades(...)`
- `Fixation`, `Saccade` — dataclasses
- `calculate_k_coefficient(fixations, saccades)` → `KCoefficientResult`
- `classify_attention(k)` → `AttentionType`
- `k_coefficient_timeseries(...)`

### Metrics

- `calculate_lhipa(pupil, sample_rate)` → `LHIPAResult`
- `calculate_ripa2(pupil, sample_rate, ...)` → `RIPA2Result` (per-sample
  cognitive-load index; byte-for-byte parity with the on-device
  `LiveLoadIndex` column written by `RIPAMonitor`)
- `fixation_entropy(fixations, horizontal_bins=8, vertical_bins=8, ...)`
  → `FixationEntropyResult` (Shiferaw 2019 stationary gaze entropy
  SGE + gaze transition entropy GTE over a fixation list, with
  normalised-by-`log2(N)` variants for cross-recording comparison)
- `calculate_gaze_entropy(x, y, ...)` → `EntropyResult` (legacy
  raw-sample Shannon entropy; prefer `fixation_entropy` for SGE
  per Shiferaw 2019)
- `transition_entropy(...)` (Krejtz 2015 GTE; canonical AOI mode or
  spatial-proxy mode for raw samples)
- `calculate_quality_metrics(...)` → `QualityMetrics`

### Experiments

- `SAMPLE_EXPERIMENT_PHASES`
- `analyze_sample_experiment(csv, results_json_path=None)` → `SampleExperimentReport`
- `PhaseReport`, `SampleExperimentReport`

### Calibration

- `load_profile(json_path)` → `EyeTrackingProfile`
- `apply_combined_correction(world_dirs, head_quats, correction)`
- `apply_profile_to_csv(profile_path, csv_in, csv_out)` → `dict`
- `EyeTrackingProfile`, `GazeCorrection`

### Batch

- `BatchProcessor(...)`, `ProcessingResult`
- `process_batch(file_paths, output_path=None, **kw)` → `pd.DataFrame`
- `process_directory_batch(dir, pattern="*.csv", output_path=None, **kw)` → `pd.DataFrame`

### Visualization

- `create_heatmap(x, y, ...)`
- `create_trajectory_plot(x, y, ...)`
- `create_timeseries_plot(timestamps, values, ...)`
- `create_fixation_plot(fixations, ...)`
- `create_pupil_plot(timestamps, left, right, ...)`

## References

- Duchowski, A. T., Krejtz, K., et al. (2018). The Index of Pupillary
  Activity: Measuring Cognitive Load vis-à-vis Task Difficulty with
  Pupil Oscillation. *CHI*.
- Duchowski, A. T., et al. (2020). The Low/High Index of Pupillary
  Activity. *CHI '20*. — LHIPA.
- Duchowski, A. T., et al. (2022). Vergence calculation algorithms.
- Jayawardena, G., Jayawardana, Y., & Gwizdka, J. (2025). RIPA2:
  Real-time Pupil-derived Index of Cognitive Activity. *JEMR*,
  18(6), 70.
- Krejtz, K., Szmidt, T., Duchowski, A. T., & Krejtz, I. (2015).
  Entropy-based statistical analysis of eye movement transitions.
  *ACM Trans. Applied Perception*, 13(1), 4. — GTE formula and
  normalisation convention.
- Krejtz, K., Duchowski, A. T., et al. (2016). Eye tracking cognitive
  load using pupil diameter and microsaccades with fixed gaze.
  *PLOS ONE*. — K-coefficient.
- Salvucci, D. D., & Goldberg, J. H. (2000). Identifying fixations
  and saccades in eye-tracking protocols. *ETRA*. — I-VT classifier
  (with min-duration / merge-gap extensions from Olsson 2007 and
  Komogortsev 2010).
- Shiferaw, B., Downey, L., & Crewther, D. (2019). A review of
  gaze entropy as a measure of visual scanning efficiency.
  *Neurosci. Biobehav. Rev.*, 96, 353–366. — SGE/GTE conventions
  (fixation-sequence input, log2(N) normalisation).

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

See [`../CITATION.cff`](../CITATION.cff) and
[`../ACKNOWLEDGMENTS.md`](../ACKNOWLEDGMENTS.md) for the full citation
table covering RIPA2 (Jayawardena 2025), LHIPA (Duchowski 2018),
K-coefficient, and others.

## License

MIT License. See [`../LICENSE`](../LICENSE).
