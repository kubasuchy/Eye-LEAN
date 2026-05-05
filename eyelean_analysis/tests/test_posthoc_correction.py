"""Tests for the post-hoc EyeTrackingProfile correction module.

Verifies the math matches Unity's `ActiveProfile.ApplyCombinedCorrection`
and that the CSV-rewriting helper preserves all non-direction columns.
"""

import json
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

from eyelean_analysis.calibration import (
    GazeCorrection,
    EyeTrackingProfile,
    apply_combined_correction,
    apply_profile_to_csv,
    load_profile,
)
from eyelean_analysis.calibration.posthoc_correction import (
    _decompose_yaw_pitch_deg,
    _recompose_from_yaw_pitch_deg,
)


def _identity_quat_xyzw(n: int) -> np.ndarray:
    """`(n, 4)` array of identity quaternions in (x, y, z, w) order."""
    q = np.zeros((n, 4))
    q[:, 3] = 1.0
    return q


def test_identity_correction_is_no_op():
    """Default-constructed correction must return the input array
    unchanged, not even a copy."""
    dirs = np.array([[0.0, 0.0, 1.0], [0.5, 0.0, 0.866]])
    quats = _identity_quat_xyzw(2)
    out = apply_combined_correction(dirs, quats, GazeCorrection())
    assert out is dirs  # fast-path returns input by identity


def test_yaw_offset_rotates_around_y_under_identity_head():
    """A pure yaw offset under identity head rotation should rotate the
    direction by exactly that yaw."""
    forward = np.array([[0.0, 0.0, 1.0]])
    quats = _identity_quat_xyzw(1)
    correction = GazeCorrection(gaze_yaw_offset_deg=10.0)
    corrected = apply_combined_correction(forward, quats, correction)

    # Expect (sin(10°), 0, cos(10°))
    expected = np.array([[np.sin(np.radians(10.0)), 0.0, np.cos(np.radians(10.0))]])
    np.testing.assert_allclose(corrected, expected, atol=1e-7)


def test_pitch_offset_rotates_around_x_under_identity_head():
    """A pure pitch offset under identity head rotation. Unity's pitch
    convention is `pitch = -asin(y)`, so a positive `gazePitchOffsetDeg`
    means looking *down* (negative y). Recompose path: y = sin(-pitchRad)."""
    forward = np.array([[0.0, 0.0, 1.0]])
    quats = _identity_quat_xyzw(1)
    correction = GazeCorrection(gaze_pitch_offset_deg=10.0)
    corrected = apply_combined_correction(forward, quats, correction)

    # pitch = 10° applied; y = sin(-10°) = -0.1736
    np.testing.assert_allclose(corrected[0, 0], 0.0, atol=1e-7)
    np.testing.assert_allclose(corrected[0, 1], -np.sin(np.radians(10.0)), atol=1e-7)
    np.testing.assert_allclose(corrected[0, 2], np.cos(np.radians(10.0)), atol=1e-7)


def test_decompose_recompose_roundtrip():
    """yaw/pitch decompose then recompose must round-trip a unit
    direction (modulo float)."""
    rng = np.random.default_rng(42)
    raw = rng.normal(size=(50, 3))
    raw = raw / np.linalg.norm(raw, axis=1, keepdims=True)
    # Avoid the gimbal-lock pole (y == ±1) where yaw is degenerate.
    mask = np.abs(raw[:, 1]) < 0.99
    raw = raw[mask]

    yaw, pitch = _decompose_yaw_pitch_deg(raw)
    back = _recompose_from_yaw_pitch_deg(yaw, pitch)

    np.testing.assert_allclose(back, raw, atol=1e-7)


def test_yaw_gain_scales_local_yaw_under_identity_head():
    """Gain multiplies the *local* yaw before the offset is added."""
    # Direction at 30° local yaw under identity head.
    yaw0 = 30.0
    dirs = np.array([[np.sin(np.radians(yaw0)), 0.0, np.cos(np.radians(yaw0))]])
    quats = _identity_quat_xyzw(1)
    correction = GazeCorrection(gaze_yaw_gain=2.0)
    corrected = apply_combined_correction(dirs, quats, correction)

    # Expected new yaw = 60°
    expected = np.array([[np.sin(np.radians(60.0)), 0.0, np.cos(np.radians(60.0))]])
    np.testing.assert_allclose(corrected, expected, atol=1e-7)


def test_correction_under_yawed_head_rotates_correctly():
    """Under a head rotated 45° around Y, a forward gaze in world space
    becomes (sin45°, 0, cos45°) in head-local. Adding a +10° local yaw
    offset should produce a world direction at world yaw `45° + 10°`."""
    head_yaw = 45.0
    # Unity's quaternion (y-axis rotation, w = cos(half-angle))
    half = np.radians(head_yaw / 2.0)
    quat_xyzw = np.array([[0.0, np.sin(half), 0.0, np.cos(half)]])

    # World forward direction.
    world_forward = np.array([[0.0, 0.0, 1.0]])
    correction = GazeCorrection(gaze_yaw_offset_deg=10.0)

    corrected = apply_combined_correction(world_forward, quat_xyzw, correction)

    # head-local direction was -sin(45°), 0, cos(45°). Adding 10° yaw
    # gives -sin(35°), 0, cos(35°). Rotating back by +45° head yaw
    # produces world direction at world yaw -35° + 45° = +10°.
    expected_yaw_world = 10.0
    expected = np.array([[
        np.sin(np.radians(expected_yaw_world)),
        0.0,
        np.cos(np.radians(expected_yaw_world)),
    ]])
    np.testing.assert_allclose(corrected, expected, atol=1e-6)


def test_load_profile_rejects_unsupported_major(tmp_path):
    """A profile with `schemaVersion: 2.x` should be rejected as
    incompatible until v2 lands."""
    p = tmp_path / "p.json"
    p.write_text(json.dumps({"schemaVersion": "2.0", "combinedGaze": {}}))
    with pytest.raises(ValueError, match="schemaVersion"):
        load_profile(p)


def test_load_profile_parses_real_artifact(tmp_path):
    """Round-trip a representative profile JSON (matches the Unity
    output captured from round-10 hardware testing)."""
    blob = {
        "schemaVersion": "1.0",
        "metadata": {
            "profileName": "TestUser_2026",
            "participantID": "P_TEST",
            "createdAt": "2026-04-30T16:15:59Z",
            "deviceModel": "HTC VIVE Focus Vision",
            "viveDeviceCalibrationStatus": 0,
            "eyeleanAppVersion": "1.0.0",
        },
        "combinedGaze": {
            "gazeYawOffsetDeg": -0.262,
            "gazePitchOffsetDeg": -0.398,
            "gazeYawGain": 1.0,
            "gazePitchGain": 1.0,
        },
        "leftEye": {
            "gazeYawOffsetDeg": 0.0,
            "gazePitchOffsetDeg": 0.0,
            "gazeYawGain": 1.0,
            "gazePitchGain": 1.0,
        },
        "rightEye": {
            "gazeYawOffsetDeg": 0.0,
            "gazePitchOffsetDeg": 0.0,
            "gazeYawGain": 1.0,
            "gazePitchGain": 1.0,
        },
    }
    p = tmp_path / "real.json"
    p.write_text(json.dumps(blob))

    profile = load_profile(p)
    assert profile.profile_name == "TestUser_2026"
    assert profile.combined_gaze.gaze_yaw_offset_deg == pytest.approx(-0.262)
    assert profile.combined_gaze.gaze_pitch_offset_deg == pytest.approx(-0.398)
    assert profile.left_eye.is_identity()
    assert profile.right_eye.is_identity()


def _make_minimal_csv(path: Path, n: int = 10) -> None:
    """Write a tiny CSV with the columns `apply_profile_to_csv` needs."""
    forward = np.tile([0.0, 0.0, 1.0], (n, 1))
    head_quat = _identity_quat_xyzw(n)
    df = pd.DataFrame({
        "UnityTimestamp": np.linspace(0.0, n / 90.0, n),
        "FrameNumber": np.arange(n),
        "CurrentPhase": ["Test"] * n,
        "HeadRot_X": head_quat[:, 0],
        "HeadRot_Y": head_quat[:, 1],
        "HeadRot_Z": head_quat[:, 2],
        "HeadRot_W": head_quat[:, 3],
        "CombinedDir_X": forward[:, 0],
        "CombinedDir_Y": forward[:, 1],
        "CombinedDir_Z": forward[:, 2],
        "LeftDir_X": forward[:, 0],
        "LeftDir_Y": forward[:, 1],
        "LeftDir_Z": forward[:, 2],
        "RightDir_X": forward[:, 0],
        "RightDir_Y": forward[:, 1],
        "RightDir_Z": forward[:, 2],
        "HasCombinedDirection": [True] * n,
    })
    with path.open("w", newline="") as f:
        f.write("# CoordinateOrigin: 0.0,0.0,0.0\n")
        f.write("# CoordinateOriginSet: False\n")
        df.to_csv(f, index=False, lineterminator="\n")


def test_apply_profile_to_csv_rewrites_combined_dir(tmp_path):
    csv_in = tmp_path / "in.csv"
    csv_out = tmp_path / "out.csv"
    profile_path = tmp_path / "p.json"
    _make_minimal_csv(csv_in, n=5)
    profile_path.write_text(json.dumps({
        "schemaVersion": "1.0",
        "metadata": {"profileName": "Rotator"},
        "combinedGaze": {"gazeYawOffsetDeg": 10.0, "gazePitchOffsetDeg": 0.0,
                         "gazeYawGain": 1.0, "gazePitchGain": 1.0},
        "leftEye": {"gazeYawOffsetDeg": 0.0, "gazePitchOffsetDeg": 0.0,
                    "gazeYawGain": 1.0, "gazePitchGain": 1.0},
        "rightEye": {"gazeYawOffsetDeg": 0.0, "gazePitchOffsetDeg": 0.0,
                     "gazeYawGain": 1.0, "gazePitchGain": 1.0},
    }))

    stats = apply_profile_to_csv(profile_path, csv_in, csv_out)
    assert stats["samples_corrected_combined"] == 5
    assert stats["samples_corrected_left"] == 0
    assert stats["samples_corrected_right"] == 0

    out_text = csv_out.read_text()
    # Metadata block + new AppliedProfile line preserved.
    assert "# CoordinateOrigin: 0.0,0.0,0.0" in out_text
    assert "# AppliedProfile: Rotator" in out_text

    out_df = pd.read_csv(csv_out, comment="#")
    expected_x = np.sin(np.radians(10.0))
    np.testing.assert_allclose(out_df["CombinedDir_X"].values,
                               np.full(5, expected_x), atol=1e-6)
    # Per-eye unchanged because correction is identity.
    np.testing.assert_allclose(out_df["LeftDir_X"].values, np.zeros(5), atol=1e-9)


def test_apply_profile_to_csv_refuses_to_overwrite(tmp_path):
    csv_in = tmp_path / "in.csv"
    csv_out = tmp_path / "out.csv"
    profile_path = tmp_path / "p.json"
    _make_minimal_csv(csv_in, n=2)
    profile_path.write_text(json.dumps({
        "schemaVersion": "1.0",
        "combinedGaze": {"gazeYawOffsetDeg": 1.0, "gazePitchOffsetDeg": 0.0,
                         "gazeYawGain": 1.0, "gazePitchGain": 1.0},
    }))
    csv_out.write_text("preexisting\n")

    with pytest.raises(FileExistsError):
        apply_profile_to_csv(profile_path, csv_in, csv_out)
    # Overwrite=True should succeed.
    stats = apply_profile_to_csv(profile_path, csv_in, csv_out, overwrite=True)
    assert stats["samples_total"] == 2


def test_apply_profile_to_csv_skips_invalid_quaternions(tmp_path):
    """Rows with NaN head-rotation should be skipped without raising,
    and reported via `samples_skipped_invalid`."""
    csv_in = tmp_path / "in.csv"
    csv_out = tmp_path / "out.csv"
    profile_path = tmp_path / "p.json"
    _make_minimal_csv(csv_in, n=5)

    df = pd.read_csv(csv_in, comment="#")
    df.loc[2, "HeadRot_W"] = np.nan
    with csv_in.open("w", newline="") as f:
        f.write("# Test header\n")
        df.to_csv(f, index=False, lineterminator="\n")

    profile_path.write_text(json.dumps({
        "schemaVersion": "1.0",
        "combinedGaze": {"gazeYawOffsetDeg": 10.0, "gazePitchOffsetDeg": 0.0,
                         "gazeYawGain": 1.0, "gazePitchGain": 1.0},
    }))

    stats = apply_profile_to_csv(profile_path, csv_in, csv_out)
    assert stats["samples_total"] == 5
    assert stats["samples_skipped_invalid"] == 1
    assert stats["samples_corrected_combined"] == 4
