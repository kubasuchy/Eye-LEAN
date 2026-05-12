"""Per-phase report for SampleExperiment CSVs.

`SampleExperimentController` runs the four-phase battery
(FreeExploration, VisualSearch, CountingTask, ChangeDetection) and
tags each row with `CurrentPhase`. The controller can additionally
emit `CalibrationCheck` and `SmoothPursuit` phases if a custom
launcher wires them up; this module recognises all six names. It
slices the CSV by phase, computes the metrics that make sense for
each, and — when the matching `experiment_results_*.json` is
available — joins per-trial summaries into the report.

Designed to degrade gracefully: missing columns and short-duration
phases produce a `missing_metrics` list rather than an exception.
"""

from __future__ import annotations

import json
import warnings
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Union

import numpy as np
import pandas as pd

from ..classification.velocity_classifier import detect_eye_movements
from ..data.loader import EyeLeanData, EyeLeanLoader
from ..metrics.entropy import fixation_entropy
from ..metrics.lhipa import calculate_lhipa


# Canonical phase names emitted by SampleExperimentController.SetPhase.
SAMPLE_EXPERIMENT_PHASES = (
    "CalibrationCheck",
    "FreeExploration",
    "VisualSearch",
    "SmoothPursuit",
    "CountingTask",
    "ChangeDetection",
)


@dataclass
class PhaseReport:
    """Metrics for a single phase of a SampleExperiment session."""

    phase: str
    n_samples: int = 0
    duration_seconds: float = 0.0
    sample_rate_hz: float = 0.0

    # Gaze metrics — populated if the CSV has gaze direction columns.
    n_fixations: Optional[int] = None
    mean_fixation_duration_s: Optional[float] = None
    scanpath_length_deg: Optional[float] = None
    # Stationary gaze entropy (SGE) and gaze transition entropy (GTE)
    # over fixation centroids in an 8×8 grid per Shiferaw 2019.
    # `*_normalized` divides by log2(N=64) so values are in [0, 1].
    sge_bits: Optional[float] = None
    sge_normalized: Optional[float] = None
    gte_bits: Optional[float] = None
    gte_normalized: Optional[float] = None
    # Back-compat alias: equal to `sge_bits` so existing notebooks /
    # downstream code that read `gaze_entropy_bits` keep working. The
    # legacy field used to be computed over raw samples; the value
    # under it now is the Shiferaw-correct fixation-based SGE.
    gaze_entropy_bits: Optional[float] = None

    # Pupil metrics — populated if the CSV has pupil-diameter columns and the
    # phase is at least `LHIPACalculator.min_duration` seconds long.
    lhipa: Optional[float] = None

    # Per-trial summary joined from the results JSON (if available).
    # Each entry is a dict — shape varies by phase.
    trials: List[Dict[str, Any]] = field(default_factory=list)

    # Diagnostic: which intended metrics were skipped and why.
    missing_metrics: List[str] = field(default_factory=list)


@dataclass
class SampleExperimentReport:
    """Whole-session report aggregating one PhaseReport per phase."""

    csv_path: str
    results_json_path: Optional[str]
    total_duration_seconds: float
    total_samples: int
    sample_rate_hz: float
    phases: Dict[str, PhaseReport]

    def to_dataframe(self) -> pd.DataFrame:
        """Per-phase summary as a tidy DataFrame (one row per phase)."""
        rows = []
        for name in SAMPLE_EXPERIMENT_PHASES:
            r = self.phases.get(name)
            if r is None:
                continue
            rows.append({
                "phase": r.phase,
                "n_samples": r.n_samples,
                "duration_seconds": r.duration_seconds,
                "sample_rate_hz": r.sample_rate_hz,
                "n_fixations": r.n_fixations,
                "mean_fixation_duration_s": r.mean_fixation_duration_s,
                "scanpath_length_deg": r.scanpath_length_deg,
                "sge_bits": r.sge_bits,
                "sge_normalized": r.sge_normalized,
                "gte_bits": r.gte_bits,
                "gte_normalized": r.gte_normalized,
                "lhipa": r.lhipa,
                "n_trials": len(r.trials),
                "missing_metrics": ", ".join(r.missing_metrics),
            })
        return pd.DataFrame(rows)


def analyze_sample_experiment(
    csv_path: Union[str, Path],
    results_json_path: Optional[Union[str, Path]] = None,
    loader: Optional[EyeLeanLoader] = None,
) -> SampleExperimentReport:
    """Build a SampleExperimentReport from a CSV (and optional results JSON).

    Args:
        csv_path: Path to the CSV exported by SampleExperimentController.
        results_json_path: Path to the `experiment_results_*.json` written
            alongside it. If omitted, per-trial metadata (target positions,
            change-detection outcomes, etc.) won't appear in the report.
        loader: Custom EyeLeanLoader (created if None).

    Returns:
        A SampleExperimentReport with one PhaseReport per phase observed.
    """
    loader = loader or EyeLeanLoader()
    data = loader.load(csv_path, standardize_columns=True, parse_booleans=True)

    if not data.has_column("phase"):
        raise ValueError(
            f"{csv_path}: CSV has no `CurrentPhase` (or alias) column; "
            "this loader expects a SampleExperiment-style export."
        )

    results_blob = _load_results_json(results_json_path) if results_json_path else None

    phase_reports: Dict[str, PhaseReport] = {}
    for phase_name in data.get_phases():
        if phase_name not in SAMPLE_EXPERIMENT_PHASES:
            # Idle/Instructions/Complete are bookkeeping; skip silently.
            continue
        phase_data = data.filter_by_phase(phase_name)
        phase_reports[phase_name] = _build_phase_report(
            phase_name, phase_data, results_blob
        )

    return SampleExperimentReport(
        csv_path=str(csv_path),
        results_json_path=str(results_json_path) if results_json_path else None,
        total_duration_seconds=float(data.duration),
        total_samples=len(data),
        sample_rate_hz=float(data.get_sample_rate()),
        phases=phase_reports,
    )


def _load_results_json(path: Union[str, Path]) -> Optional[dict]:
    p = Path(path)
    if not p.exists():
        warnings.warn(f"results JSON not found at {p}; per-trial fields will be empty.")
        return None
    with p.open("r") as f:
        return json.load(f)


def _build_phase_report(
    phase_name: str,
    data: EyeLeanData,
    results_blob: Optional[dict],
) -> PhaseReport:
    report = PhaseReport(
        phase=phase_name,
        n_samples=len(data),
        duration_seconds=float(data.duration),
        sample_rate_hz=float(data.get_sample_rate()),
    )

    if report.n_samples < 2:
        report.missing_metrics.append("phase had <2 samples; all metrics skipped")
        return report

    _fill_gaze_metrics(report, data)
    _fill_pupil_metrics(report, data)
    _fill_trials_from_results(report, phase_name, results_blob)
    return report


def _fill_gaze_metrics(report: PhaseReport, data: EyeLeanData) -> None:
    """Fixation count / scanpath length / entropy from gaze direction columns.

    Eye_lean stores combined gaze as a 3D unit vector (`combined_dir_x/y/z`).
    Convert to angular yaw/pitch in degrees so the velocity classifier and
    entropy bins operate on the right units."""
    df = data.df
    needed = ("combined_dir_x", "combined_dir_y", "combined_dir_z")
    if not all(col in df.columns for col in needed):
        report.missing_metrics.append("gaze direction columns not present")
        return

    dx = df["combined_dir_x"].to_numpy(dtype=float)
    dy = df["combined_dir_y"].to_numpy(dtype=float)
    dz = df["combined_dir_z"].to_numpy(dtype=float)
    # Forward-axis (Z) division can underflow for sideways gaze; mask before atan2.
    safe_z = np.where(np.abs(dz) < 1e-6, 1e-6 * np.sign(dz + 1e-12), dz)
    yaw_deg = np.degrees(np.arctan2(dx, safe_z))
    pitch_deg = np.degrees(np.arctan2(dy, safe_z))
    timestamps = data.get_timestamps()

    movements = detect_eye_movements(yaw_deg, pitch_deg, timestamps)
    fixations = movements.get("fixations", [])
    report.n_fixations = len(fixations)
    if fixations:
        report.mean_fixation_duration_s = float(
            np.mean([f.duration for f in fixations])
        )

    # Scanpath length = sum of angular distances between consecutive samples.
    if len(yaw_deg) > 1:
        d_yaw = np.diff(yaw_deg)
        d_pitch = np.diff(pitch_deg)
        report.scanpath_length_deg = float(np.sum(np.hypot(d_yaw, d_pitch)))

    # Shiferaw 2019 SGE/GTE — over fixation centroids, not raw samples.
    # Falls back to is_valid=False for phases with < 2 fixations.
    ent = fixation_entropy(fixations, horizontal_bins=8, vertical_bins=8)
    if ent.is_valid:
        report.sge_bits        = float(ent.sge)
        report.sge_normalized  = float(ent.sge_normalized)
        report.gte_bits        = float(ent.gte)
        report.gte_normalized  = float(ent.gte_normalized)
        report.gaze_entropy_bits = float(ent.sge)  # back-compat alias


def _fill_pupil_metrics(report: PhaseReport, data: EyeLeanData) -> None:
    df = data.df
    pupil_col = next(
        (c for c in ("left_pupil_diameter", "right_pupil_diameter") if c in df.columns),
        None,
    )
    if pupil_col is None:
        report.missing_metrics.append("pupil diameter columns not present")
        return

    pupil = df[pupil_col].to_numpy(dtype=float)
    sample_rate = report.sample_rate_hz or 120.0
    lhipa_result = calculate_lhipa(pupil, sample_rate=sample_rate)
    if lhipa_result.is_valid:
        report.lhipa = float(lhipa_result.lhipa)
    else:
        report.missing_metrics.append(
            f"LHIPA not computed: {lhipa_result.error_message or 'invalid'}"
        )


def _fill_trials_from_results(
    report: PhaseReport,
    phase_name: str,
    results_blob: Optional[dict],
) -> None:
    """Pull per-trial outcomes from the results JSON when available.

    The blob's top-level keys mirror the C# `ExperimentResults` struct:
    `visualSearchResults` (array) and `changeDetectionResults` (array).
    Other phases don't currently emit JSON trial entries — they only
    contribute via the CSV's per-row metadata."""
    if results_blob is None:
        return

    if phase_name == "VisualSearch":
        for trial in results_blob.get("visualSearchResults", []):
            report.trials.append({
                "trial_number": trial.get("trialNumber"),
                "target_found": trial.get("targetFound"),
                "search_time_seconds": trial.get("searchTimeSeconds"),
                "target_position": trial.get("targetPosition"),
            })
    elif phase_name == "ChangeDetection":
        for trial in results_blob.get("changeDetectionResults", []):
            report.trials.append({
                "trial_number": trial.get("trialNumber"),
                "change_detected": trial.get("changeDetected"),
                "detection_time_seconds": trial.get("detectionTimeSeconds"),
                "change_type": trial.get("changeType"),
                "changed_object_name": trial.get("changedObjectName"),
            })
