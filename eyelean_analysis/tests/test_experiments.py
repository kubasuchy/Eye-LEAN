"""Tests for the SampleExperiment per-phase report builder."""

import json

import numpy as np
import pandas as pd
import pytest

from eyelean_analysis.experiments import (
    SAMPLE_EXPERIMENT_PHASES,
    SampleExperimentReport,
    analyze_sample_experiment,
)


@pytest.fixture
def synthetic_sample_experiment(tmp_path):
    """Synthesize a SampleExperiment-shaped CSV that walks through the six
    canonical phases at 120 Hz, with gaze + pupil columns populated. Also
    write a matching `experiment_results_*.json` with one VisualSearch
    trial and one ChangeDetection trial.

    Returns (csv_path, results_json_path)."""
    fs = 120
    phases = SAMPLE_EXPERIMENT_PHASES  # six canonical
    # 6 s per phase keeps LHIPA happy (default min_duration=5 s)
    seconds_per_phase = 6.0
    samples_per_phase = int(fs * seconds_per_phase)

    rows = []
    rng = np.random.default_rng(seed=42)
    t = 0.0
    dt = 1.0 / fs
    for phase in phases:
        # Slight per-phase gaze offset so each phase has distinguishable gaze
        # statistics. Direction vectors point mostly forward (+Z) with small
        # x/y offsets that shift the apparent yaw/pitch a few degrees.
        x_bias = (hash(phase) % 7) * 0.01
        y_bias = ((hash(phase) // 7) % 7) * 0.01
        for _ in range(samples_per_phase):
            rows.append({
                "UnityTimestamp": t,
                "CurrentPhase": phase,
                "SubTask": "default",
                "CombinedDir_X": x_bias + rng.normal(0, 0.005),
                "CombinedDir_Y": y_bias + rng.normal(0, 0.005),
                "CombinedDir_Z": 1.0,
                "LeftPupilDiameter": 4.0 + 0.3 * np.sin(2 * np.pi * 0.2 * t),
            })
            t += dt

    csv_path = tmp_path / "sample_session.csv"
    pd.DataFrame(rows).to_csv(csv_path, index=False)

    results = {
        "visualSearchResults": [
            {
                "trialNumber": 1,
                "targetFound": True,
                "searchTimeSeconds": 2.4,
                "targetPosition": {"x": 1.0, "y": 1.6, "z": 3.0},
            }
        ],
        "changeDetectionResults": [
            {
                "trialNumber": 1,
                "changeDetected": False,
                "detectionTimeSeconds": 0.0,
                "changeType": "color",
                "changedObjectName": "Cube_3",
            }
        ],
    }
    json_path = tmp_path / "experiment_results.json"
    json_path.write_text(json.dumps(results))

    return csv_path, json_path


def test_report_has_all_six_phases(synthetic_sample_experiment):
    csv, js = synthetic_sample_experiment
    report = analyze_sample_experiment(csv, results_json_path=js)

    assert isinstance(report, SampleExperimentReport)
    assert set(report.phases.keys()) == set(SAMPLE_EXPERIMENT_PHASES)


def test_each_phase_has_basic_metrics(synthetic_sample_experiment):
    csv, js = synthetic_sample_experiment
    report = analyze_sample_experiment(csv, results_json_path=js)

    for phase in SAMPLE_EXPERIMENT_PHASES:
        r = report.phases[phase]
        assert r.n_samples > 0, f"{phase} has no samples"
        assert r.duration_seconds > 0
        # 120 Hz synthetic — allow some slack for numerical estimation
        assert 100 < r.sample_rate_hz < 140, (
            f"{phase} sample_rate {r.sample_rate_hz:.2f} unexpected"
        )


def test_gaze_metrics_populated(synthetic_sample_experiment):
    csv, js = synthetic_sample_experiment
    report = analyze_sample_experiment(csv, results_json_path=js)

    for phase, r in report.phases.items():
        assert r.n_fixations is not None, f"{phase} missing fixation count"
        assert r.scanpath_length_deg is not None, f"{phase} missing scanpath"
        # SGE/GTE need >=2 fixations; allow None for phases with too few.
        if r.n_fixations and r.n_fixations >= 2:
            assert r.sge_bits is not None, f"{phase} missing SGE"
            assert r.gte_bits is not None, f"{phase} missing GTE"
            assert r.gaze_entropy_bits == r.sge_bits  # back-compat alias


def test_visual_search_trials_joined_from_json(synthetic_sample_experiment):
    csv, js = synthetic_sample_experiment
    report = analyze_sample_experiment(csv, results_json_path=js)

    vs = report.phases["VisualSearch"]
    assert len(vs.trials) == 1
    trial = vs.trials[0]
    assert trial["trial_number"] == 1
    assert trial["target_found"] is True
    assert trial["search_time_seconds"] == pytest.approx(2.4)
    assert trial["target_position"] == {"x": 1.0, "y": 1.6, "z": 3.0}


def test_change_detection_trials_joined_from_json(synthetic_sample_experiment):
    csv, js = synthetic_sample_experiment
    report = analyze_sample_experiment(csv, results_json_path=js)

    cd = report.phases["ChangeDetection"]
    assert len(cd.trials) == 1
    assert cd.trials[0]["change_type"] == "color"
    assert cd.trials[0]["changed_object_name"] == "Cube_3"


def test_works_without_results_json(synthetic_sample_experiment):
    """Per the E1 contract: no JSON => trials list empty, but every other
    metric still computes."""
    csv, _ = synthetic_sample_experiment
    report = analyze_sample_experiment(csv)  # no JSON

    assert report.results_json_path is None
    for phase in SAMPLE_EXPERIMENT_PHASES:
        r = report.phases[phase]
        assert r.trials == []
        assert r.n_fixations is not None  # gaze metrics still there


def test_to_dataframe_shape(synthetic_sample_experiment):
    csv, js = synthetic_sample_experiment
    report = analyze_sample_experiment(csv, results_json_path=js)
    df = report.to_dataframe()

    assert len(df) == len(SAMPLE_EXPERIMENT_PHASES)
    assert set(df["phase"]) == set(SAMPLE_EXPERIMENT_PHASES)
    # Columns we promise downstream code can read
    for col in ["n_samples", "duration_seconds", "n_fixations", "lhipa", "n_trials"]:
        assert col in df.columns


def test_missing_phase_column_raises(tmp_path):
    """A non-SampleExperiment CSV should fail with a clear message rather
    than producing an empty report or a deep AttributeError."""
    p = tmp_path / "no_phase.csv"
    pd.DataFrame({
        "UnityTimestamp": [0.0, 0.008],
        "HeadPos_X": [0.0, 0.0],
    }).to_csv(p, index=False)

    with pytest.raises(ValueError, match="CurrentPhase"):
        analyze_sample_experiment(p)


def test_extra_phases_in_csv_are_ignored(tmp_path):
    """Idle / Instructions / Complete bookkeeping rows must not appear in
    the report and must not crash phase iteration."""
    rows = []
    t = 0.0
    dt = 1 / 120
    for phase in ("Idle", "FreeExploration", "Complete"):
        for _ in range(720):  # 6 s
            rows.append({
                "UnityTimestamp": t,
                "CurrentPhase": phase,
                "CombinedDir_X": 0.01,
                "CombinedDir_Y": 0.01,
                "CombinedDir_Z": 1.0,
            })
            t += dt
    p = tmp_path / "with_idle.csv"
    pd.DataFrame(rows).to_csv(p, index=False)

    report = analyze_sample_experiment(p)
    assert "Idle" not in report.phases
    assert "Complete" not in report.phases
    assert "FreeExploration" in report.phases
