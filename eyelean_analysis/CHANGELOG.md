# Changelog

All notable changes to `eyelean-analysis` are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/); this project
uses semantic versioning.

## [1.0.1]

### Changed
- **LHIPA duration is derived from the true elapsed timestamp span.**
  `LHIPACalculator.calculate` and `calculate_lhipa` accept per-sample
  `timestamps`; when supplied, the signal duration is
  `timestamps[-1] - timestamps[0]` for both the minimum-duration gate and the
  per-second normalisation, instead of `n / sample_rate`. This is robust to
  frame jitter: a `1 / median(Δt)` rate over-estimates the true sampling rate
  when frames occasionally hitch, which under-computes `n / sample_rate` and
  could reject genuine ≥ 5 s recordings as "Duration too short" while biasing
  LHIPA values upward. The canonical report paths (`analyze_sample_experiment`
  and the batch processor) now pass timestamps by default.
  - LHIPA values for jitter-affected recordings differ from 1.0.0 — they are
    now normalised by true elapsed time. Call `calculate_lhipa` without
    `timestamps` to retain the previous `n / sample_rate` behaviour.

### Added
- `LHIPAResult.duration_s` — the signal duration in seconds actually used for
  the gate and the normalisation (true elapsed span when timestamps were
  supplied, else `n / sample_rate`).
