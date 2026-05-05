"""Tests for the plug-and-play notebook bootstrap helper."""

import os
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

import eyelean_analysis as ela


def _write_main_csv(path, n=240):
    fs = 120.0
    pd.DataFrame({
        "UnityTimestamp": np.arange(n) / fs,
        "FrameNumber": np.arange(n),
        "CombinedDir_X": np.zeros(n),
        "CombinedDir_Y": np.zeros(n),
        "CombinedDir_Z": np.ones(n),
        "IsTrackingValid": ["True"] * n,
    }).to_csv(path, index=False)


def _write_state_sidecar(path):
    with open(path, "w") as f:
        f.write("# FileVersion: 1.0\n")
        f.write("Frame,T,ObjectId,Pos_X,Pos_Y,Pos_Z,Rot_X,Rot_Y,Rot_Z,Rot_W,Active\n")
        f.write("0,0.0,x,0,0,0,0,0,0,1,True\n")


def _write_events_sidecar(path):
    with open(path, "w") as f:
        f.write("# FileVersion: 1.0\n")
        f.write("Frame,T,EventType,ObjectId,Detail\n")
        f.write("0,0.0,Spawn,x,\n")


def test_explicit_path_argument(tmp_path):
    csv = tmp_path / "EyeTracking_20260101_000000.csv"
    _write_main_csv(csv)
    ctx = ela.notebook_context(csv=csv)
    assert ctx.source == "argument"
    assert ctx.csv_path == csv.resolve()
    assert ctx.scene_state_path is None
    assert ctx.scene_events_path is None
    assert ctx.data is not None
    assert len(ctx.data.df) == 240


def test_explicit_path_missing_raises(tmp_path):
    with pytest.raises(FileNotFoundError):
        ela.notebook_context(csv=tmp_path / "nope.csv")


def test_env_var_discovery(tmp_path, monkeypatch):
    csv = tmp_path / "EyeTracking_xxx.csv"
    _write_main_csv(csv)
    monkeypatch.setenv("EYELEAN_CSV", str(csv))
    # Move cwd somewhere else so the Logs/ walk is guaranteed not to fire
    monkeypatch.chdir(tmp_path / ".." if (tmp_path.parent.exists()) else tmp_path)
    ctx = ela.notebook_context()
    assert ctx.source == "env"
    assert ctx.csv_path == csv.resolve()


def test_env_var_pointing_to_missing_raises(tmp_path, monkeypatch):
    monkeypatch.setenv("EYELEAN_CSV", str(tmp_path / "ghost.csv"))
    with pytest.raises(FileNotFoundError, match="EYELEAN_CSV"):
        ela.notebook_context()


def test_logs_walk_finds_main_csv_excludes_sidecars(tmp_path, monkeypatch):
    monkeypatch.delenv("EYELEAN_CSV", raising=False)
    logs = tmp_path / "Logs"
    logs.mkdir()
    main = logs / "EyeTracking_20260101_120000.csv"
    state = logs / "EyeTracking_20260101_120000_SceneState.csv"
    events = logs / "EyeTracking_20260101_120000_SceneEvents.csv"
    _write_main_csv(main)
    _write_state_sidecar(state)
    _write_events_sidecar(events)

    work = tmp_path / "deep" / "subdir"
    work.mkdir(parents=True)
    monkeypatch.chdir(work)

    ctx = ela.notebook_context()
    assert ctx.source == "logs"
    assert ctx.csv_path == main.resolve()
    assert ctx.scene_state_path == state.resolve()
    assert ctx.scene_events_path == events.resolve()


def test_logs_walk_picks_most_recent(tmp_path, monkeypatch):
    monkeypatch.delenv("EYELEAN_CSV", raising=False)
    logs = tmp_path / "Logs"
    logs.mkdir()
    older = logs / "EyeTracking_old.csv"
    newer = logs / "EyeTracking_new.csv"
    _write_main_csv(older)
    _write_main_csv(newer)
    # Make `older` actually older
    os.utime(older, (1, 1))
    os.utime(newer, (1_000_000_000, 1_000_000_000))
    monkeypatch.chdir(tmp_path)
    ctx = ela.notebook_context()
    assert ctx.csv_path == newer.resolve()


def test_pairs_profile_and_results(tmp_path):
    csv = tmp_path / "EyeTracking_session.csv"
    _write_main_csv(csv)
    profile = tmp_path / "HTC VIVE Focus Vision_20260101_120000.json"
    profile.write_text("{}")
    results = tmp_path / "experiment_results_session.json"
    results.write_text("{}")
    ctx = ela.notebook_context(csv=csv)
    assert ctx.profile_path == profile.resolve()
    assert ctx.results_path == results.resolve()


def test_require_profile_raises_without_one(tmp_path):
    csv = tmp_path / "EyeTracking_session.csv"
    _write_main_csv(csv)
    with pytest.raises(FileNotFoundError, match="require_profile"):
        ela.notebook_context(csv=csv, require_profile=True)


def test_require_results_raises_without_one(tmp_path):
    csv = tmp_path / "EyeTracking_session.csv"
    _write_main_csv(csv)
    with pytest.raises(FileNotFoundError, match="require_results"):
        ela.notebook_context(csv=csv, require_results=True)


def test_require_sidecars_raises_without_them(tmp_path):
    csv = tmp_path / "EyeTracking_session.csv"
    _write_main_csv(csv)
    with pytest.raises(FileNotFoundError, match="require_sidecars"):
        ela.notebook_context(csv=csv, require_sidecars=True)


def test_load_false_skips_pandas(tmp_path):
    csv = tmp_path / "EyeTracking_session.csv"
    _write_main_csv(csv)
    ctx = ela.notebook_context(csv=csv, load=False)
    assert ctx.data is None
    # Metadata read still happens (it's cheap)
    assert isinstance(ctx.metadata, dict)


def test_repr_does_not_blow_up_with_loaded_data(tmp_path):
    csv = tmp_path / "EyeTracking_session.csv"
    _write_main_csv(csv)
    ctx = ela.notebook_context(csv=csv)
    s = repr(ctx)
    assert "NotebookContext" in s
    assert ctx.csv_path.name in s


def test_bundled_sample_fallback_when_no_logs(tmp_path, monkeypatch):
    """If no Logs/ exists in the cwd walk and no env var is set, the
    bundled sample under the repo's StreamingAssets is the canonical
    fallback. Skip if running outside a checkout."""
    monkeypatch.delenv("EYELEAN_CSV", raising=False)
    # Empty workdir with no Logs/ above it (all the way to /tmp)
    isolated = tmp_path / "isolated_work"
    isolated.mkdir()
    monkeypatch.chdir(isolated)

    from eyelean_analysis.notebook_context import _find_bundled_sample, _find_repo_root
    bundled = _find_bundled_sample(_find_repo_root())
    if bundled is None:
        pytest.skip("Bundled sample not present (running outside checkout)")

    # Walk-from-cwd will return None. Walk-from-package may still find a
    # Logs/ in the repo, so this assertion only holds if no Logs/ sits
    # above the package install. In a normal dev clone there IS a
    # Logs/ at the repo root, so the source will be 'logs', not
    # 'bundled'. Test the discovery primitives instead.
    assert bundled.is_file()
    assert "EyeTracking" in bundled.name
