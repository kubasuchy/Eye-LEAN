"""Tests for the batch processor.

The processor is a thin wrapper around the loader + per-sample analyses.
These tests exercise the orchestration paths (sequential, custom analyzer,
empty input, missing-file failure) rather than re-validating the underlying
metric correctness, which is covered by the per-module tests."""

import numpy as np
import pandas as pd
import pytest

from eyelean_analysis.batch.processor import (
    BatchProcessor,
    ProcessingResult,
    process_batch,
    process_directory_batch,
)


def _write_minimal_csv(path):
    """Write a minimum viable Eye_lean CSV the loader will accept."""
    n = 240
    fs = 120.0
    df = pd.DataFrame({
        "UnityTimestamp": np.arange(n) / fs,
        "FrameNumber": np.arange(n),
        "CombinedDir_X": np.zeros(n),
        "CombinedDir_Y": np.zeros(n),
        "CombinedDir_Z": np.ones(n),
        "CombinedEyeOriginX": np.zeros(n),
        "CombinedEyeOriginY": np.full(n, 1.5),
        "CombinedEyeOriginZ": np.zeros(n),
        "LeftPupilDiameter": np.full(n, 3.4),
        "RightPupilDiameter": np.full(n, 3.5),
        "IsTrackingValid": ["True"] * n,
    })
    df.to_csv(path, index=False)


def test_batch_empty_input_returns_empty():
    proc = BatchProcessor(n_workers=1, show_progress=False)
    assert proc.process_files([]) == []


def test_batch_sequential_single_file(tmp_path):
    csv = tmp_path / "a.csv"
    _write_minimal_csv(csv)
    proc = BatchProcessor(
        n_workers=1, show_progress=False,
        compute_lhipa=False, compute_entropy=False, compute_fixations=False,
    )
    results = proc.process_files([csv])
    assert len(results) == 1
    r = results[0]
    assert r.success
    assert r.n_samples == 240
    assert r.duration_seconds > 0
    assert r.sample_rate_hz > 100  # 120-ish


def test_batch_missing_file_records_failure(tmp_path):
    proc = BatchProcessor(n_workers=1, show_progress=False)
    results = proc.process_files([tmp_path / "does_not_exist.csv"])
    assert len(results) == 1
    assert not results[0].success
    assert "FileNotFoundError" in (results[0].error_message or "")


def test_batch_custom_analyzer_runs(tmp_path):
    csv = tmp_path / "a.csv"
    _write_minimal_csv(csv)
    proc = BatchProcessor(
        n_workers=1, show_progress=False,
        compute_lhipa=False, compute_entropy=False, compute_fixations=False,
    )

    def my_analyzer(data):
        return {"my_metric": float(len(data.df))}

    proc.add_analyzer(my_analyzer)
    results = proc.process_files([csv])
    assert results[0].custom_metrics.get("my_metric") == 240.0


def test_batch_custom_analyzer_exception_caught(tmp_path):
    csv = tmp_path / "a.csv"
    _write_minimal_csv(csv)
    proc = BatchProcessor(
        n_workers=1, show_progress=False,
        compute_lhipa=False, compute_entropy=False, compute_fixations=False,
    )

    def boom(_data):
        raise RuntimeError("analyzer crashed")

    proc.add_analyzer(boom)
    results = proc.process_files([csv])
    # Exception in custom analyzer should not fail the file
    assert results[0].success
    assert results[0].custom_metrics == {}


def test_batch_results_to_dataframe(tmp_path):
    csv = tmp_path / "a.csv"
    _write_minimal_csv(csv)
    proc = BatchProcessor(
        n_workers=1, show_progress=False,
        compute_lhipa=False, compute_entropy=False, compute_fixations=False,
    )
    results = proc.process_files([csv])
    df = BatchProcessor.results_to_dataframe(results)
    assert len(df) == 1
    assert "n_samples" in df.columns
    assert df["success"].iloc[0]


def test_batch_summarize_results():
    results = [
        ProcessingResult(file_path="a", success=True, n_samples=100, duration_seconds=1.0),
        ProcessingResult(file_path="b", success=True, n_samples=200, duration_seconds=2.0),
        ProcessingResult(file_path="c", success=False, error_message="bad"),
    ]
    summary = BatchProcessor.summarize_results(results)
    assert summary["n_files"] == 3
    assert summary["n_successful"] == 2
    assert summary["n_failed"] == 1
    assert summary["total_duration_seconds"] == 3.0


def test_process_directory_batch_writes_csv(tmp_path):
    csv = tmp_path / "rec.csv"
    _write_minimal_csv(csv)
    out = tmp_path / "summary.csv"
    df = process_directory_batch(
        tmp_path, pattern="*.csv", output_path=out,
        n_workers=1, show_progress=False,
        compute_lhipa=False, compute_entropy=False, compute_fixations=False,
    )
    assert len(df) == 1
    assert out.exists()


def test_process_batch_convenience(tmp_path):
    csv = tmp_path / "rec.csv"
    _write_minimal_csv(csv)
    df = process_batch(
        [csv], n_workers=1, show_progress=False,
        compute_lhipa=False, compute_entropy=False, compute_fixations=False,
    )
    assert len(df) == 1
    assert bool(df["success"].iloc[0])


def test_process_directory_no_match_returns_empty(tmp_path):
    proc = BatchProcessor(n_workers=1, show_progress=False)
    with pytest.warns(UserWarning, match="No files matching"):
        results = proc.process_directory(tmp_path, pattern="*.nope")
    assert results == []
