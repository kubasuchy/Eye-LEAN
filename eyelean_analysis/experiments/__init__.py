"""Experiment-aware analysis helpers.

Currently ships a SampleExperiment-specific report builder that loads a CSV
exported by `SampleExperimentController` and produces per-phase metrics,
joining with the corresponding `experiment_results_*.json` when present.
"""

from .sample_experiment import (
    SAMPLE_EXPERIMENT_PHASES,
    PhaseReport,
    SampleExperimentReport,
    analyze_sample_experiment,
)

__all__ = [
    "SAMPLE_EXPERIMENT_PHASES",
    "SampleExperimentReport",
    "PhaseReport",
    "analyze_sample_experiment",
]
