# Changelog

All notable changes to `eyelean-analysis` are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/); this project
uses semantic versioning.

## [1.1.0]

### Added
- Gaze heatmaps — `gaze_heatmap_2d`, `gaze_heatmap_3d_projections`,
  `aoi_heatmap`, and `list_gazed_objects` (notebook 10 walks through all three
  views with a per-object dropdown).
- `fixation_entropy` returns paired stationary (SGE) and transition (GTE) gaze
  entropy over fixation centroids (Shiferaw 2019 / Krejtz 2015);
  `analyze_sample_experiment` reports both per phase.
- `LHIPAResult.duration_s` — the signal duration in seconds actually used for
  the gate and the normalisation (true elapsed span when timestamps were
  supplied, else `n / sample_rate`).

### Changed
- **LHIPA duration is derived from the true elapsed timestamp span.**
  `LHIPACalculator.calculate` and `calculate_lhipa` accept per-sample
  `timestamps`; when supplied, the signal duration is
  `timestamps[-1] - timestamps[0]` for both the minimum-duration gate and the
  per-second normalisation, instead of `n / sample_rate` — robust to frame
  jitter (a `1 / median(Δt)` rate over-estimates the true sampling rate and
  previously rejected genuine ≥ 5 s recordings as "Duration too short" while
  biasing LHIPA upward). The report paths (`analyze_sample_experiment` and the
  batch processor) pass timestamps by default. LHIPA values for jitter-affected
  recordings differ from 1.0.0; call `calculate_lhipa` without `timestamps` to
  retain the previous `n / sample_rate` behaviour.
- Loader reads CSVs with `low_memory=False`; batch pupil averaging masks before
  dividing.

### Fixed
- K-coefficient warns and returns `UNKNOWN` when `pooled_stats` is absent,
  instead of returning a misleading value.
