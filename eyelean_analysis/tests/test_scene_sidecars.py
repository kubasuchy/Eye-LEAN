"""Tests for the v1.2 scene-state and scene-events sidecar loaders."""

import warnings

import pandas as pd
import pytest

import eyelean_analysis as ela


def _write_state_sidecar(path, rows):
    header = (
        "# Eye_lean Scene State Sidecar\n"
        "# FileVersion: 1.0\n"
        "# SessionID: test\n"
        "# CoordinateOrigin: 0,1.5,0\n"
        "# Profile: TestProfile\n"
        "Frame,T,ObjectId,Pos_X,Pos_Y,Pos_Z,Rot_X,Rot_Y,Rot_Z,Rot_W,Active\n"
    )
    with open(path, "w") as f:
        f.write(header)
        for r in rows:
            f.write(",".join(str(v) for v in r) + "\n")


def _write_events_sidecar(path, rows):
    header = (
        "# Eye_lean Scene Events Sidecar\n"
        "# FileVersion: 1.0\n"
        "# SessionID: test\n"
        "Frame,T,EventType,ObjectId,Detail\n"
    )
    with open(path, "w") as f:
        f.write(header)
        for r in rows:
            f.write(",".join("" if v is None else str(v) for v in r) + "\n")


def test_load_scene_state_skips_metadata_block(tmp_path):
    p = tmp_path / "rec_SceneState.csv"
    _write_state_sidecar(p, [
        (0, 0.0, "obj-a", 1.0, 2.0, 3.0, 0, 0, 0, 1, "True"),
        (1, 0.011, "obj-a", 1.1, 2.0, 3.0, 0, 0, 0, 1, "True"),
        (1, 0.011, "obj-b", -1, -2, -3, 0, 0, 0, 1, "False"),
    ])
    df = ela.load_scene_state(p)
    assert len(df) == 3
    assert list(df.columns) == [
        "Frame", "T", "ObjectId", "Pos_X", "Pos_Y", "Pos_Z",
        "Rot_X", "Rot_Y", "Rot_Z", "Rot_W", "Active",
    ]
    # Metadata still readable separately
    meta = ela.read_csv_metadata(p)
    assert meta.get("Profile") == "TestProfile"
    assert meta.get("FileVersion") == "1.0"


def test_load_scene_state_missing_file_returns_empty(tmp_path):
    df = ela.load_scene_state(tmp_path / "does_not_exist_SceneState.csv")
    assert df.empty


def test_load_scene_events_basic(tmp_path):
    p = tmp_path / "rec_SceneEvents.csv"
    _write_events_sidecar(p, [
        (0, 0.0, "Spawn", "obj-a", ""),
        (5, 0.5, "ShowInstruction", None, "hello"),
        (10, 1.0, "Despawn", "obj-a", ""),
    ])
    df = ela.load_scene_events(p)
    assert len(df) == 3
    assert set(df["EventType"]) == {"Spawn", "ShowInstruction", "Despawn"}


def test_load_scene_events_decode_config(tmp_path):
    import base64
    import json

    cfg = {"trialCount": 3, "duration": 10.0}
    encoded = base64.b64encode(json.dumps(cfg).encode("utf-8")).decode("ascii")

    p = tmp_path / "rec_SceneEvents.csv"
    _write_events_sidecar(p, [
        (0, 0.0, "ConfigCounting", None, encoded),
        (1, 0.1, "ShowInstruction", None, "hello"),
    ])
    df = ela.load_scene_events(p, decode_config=True)
    assert "Config" in df.columns
    cfg_row = df[df["EventType"] == "ConfigCounting"].iloc[0]
    assert cfg_row["Config"] == cfg
    # Non-Config rows leave Config = None
    instr_row = df[df["EventType"] == "ShowInstruction"].iloc[0]
    assert instr_row["Config"] is None


def test_load_scene_events_decode_config_warns_on_garbage(tmp_path):
    p = tmp_path / "rec_SceneEvents.csv"
    _write_events_sidecar(p, [
        (0, 0.0, "ConfigCounting", None, "not-base64"),
    ])
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        df = ela.load_scene_events(p, decode_config=True)
    assert any("Failed to decode Config" in str(w.message) for w in caught)
    assert df.iloc[0]["Config"] is None


def test_load_scene_sidecars_finds_both(tmp_path):
    main = tmp_path / "rec.csv"
    main.write_text("UnityTimestamp,FrameNumber\n0.0,0\n")
    _write_state_sidecar(tmp_path / "rec_SceneState.csv", [
        (0, 0.0, "x", 0, 0, 0, 0, 0, 0, 1, "True"),
    ])
    _write_events_sidecar(tmp_path / "rec_SceneEvents.csv", [
        (0, 0.0, "Spawn", "x", ""),
    ])
    state, events = ela.load_scene_sidecars(main)
    assert len(state) == 1
    assert len(events) == 1


def test_load_scene_sidecars_empty_when_missing(tmp_path):
    main = tmp_path / "rec.csv"
    main.write_text("UnityTimestamp,FrameNumber\n0.0,0\n")
    state, events = ela.load_scene_sidecars(main)
    assert state.empty
    assert events.empty


def test_merge_gaze_with_scene_state(tmp_path):
    # Three gaze samples, two of which match a scene-state row.
    gaze = pd.DataFrame({
        "frame_number": [0, 1, 2],
        "combined_dir_x": [0.0, 0.1, 0.2],
    })
    state = pd.DataFrame({
        "Frame": [0, 1],
        "ObjectId": ["x", "x"],
        "Pos_X": [10.0, 11.0],
    })
    merged = ela.merge_gaze_with_scene_state(gaze, state)
    # Frame 2 has no state row → NaN
    assert len(merged) == 3
    assert merged.loc[merged["frame_number"] == 0, "Pos_X"].iloc[0] == 10.0
    assert pd.isna(merged.loc[merged["frame_number"] == 2, "Pos_X"].iloc[0])


def test_merge_gaze_with_scene_state_filter_by_object(tmp_path):
    gaze = pd.DataFrame({"frame_number": [0, 0, 1, 1]})
    state = pd.DataFrame({
        "Frame": [0, 0, 1, 1],
        "ObjectId": ["x", "y", "x", "y"],
        "Pos_X": [1.0, 2.0, 3.0, 4.0],
    })
    merged = ela.merge_gaze_with_scene_state(gaze, state, object_id="x")
    # Only x-rows survive — one per gaze frame
    assert len(merged) == 4
    assert set(merged["Pos_X"].dropna()) == {1.0, 3.0}


def test_merge_gaze_missing_frame_column_raises():
    gaze = pd.DataFrame({"timestamp": [0, 1]})
    state = pd.DataFrame({"Frame": [0], "ObjectId": ["x"], "Pos_X": [0.0]})
    with pytest.raises(KeyError, match="frame_number"):
        ela.merge_gaze_with_scene_state(gaze, state)


def test_merge_with_empty_state_returns_copy_of_gaze():
    gaze = pd.DataFrame({"frame_number": [0, 1]})
    out = ela.merge_gaze_with_scene_state(gaze, pd.DataFrame())
    assert len(out) == 2
    # Returned copy, not the same object
    assert out is not gaze
