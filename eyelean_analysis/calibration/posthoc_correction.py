"""Re-apply an EyeTrackingProfile to a recorded CSV in post-processing.

The Unity-side correction lives in
`ActiveProfile.ApplyCombinedCorrection` (and `ApplyPerEyeCorrection`).
The math here is a direct port:

  1. Convert the world-space gaze direction to head-local via the
     inverse of the recorded head rotation quaternion.
  2. Decompose the local direction to (yaw, pitch) using Unity's
     conventions: yaw = atan2(x, z), pitch = -asin(y).
  3. Apply `gain` then `offset` separately to yaw and pitch.
  4. Recompose into a head-local unit vector, then rotate back into
     world space with the recorded quaternion.

Quaternion convention:
  - Unity serializes quaternions as (x, y, z, w) and uses left-handed
    Y-up coordinates. Quaternion-vector multiplication is handedness-
    independent, so this code treats the four components as opaque
    rotation components.
  - `scipy.spatial.transform.Rotation.from_quat` accepts (x, y, z, w)
    in that exact order.

The fast path (offset=0, gain=1) is preserved so a "default profile"
correction is a true no-op modulo float casting.
"""

from __future__ import annotations

import json
import math
import warnings
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Optional, Tuple, Union

import numpy as np
import pandas as pd
from scipy.spatial.transform import Rotation

# Unity ActiveProfile uses Mathf.Approximately for the no-op fast path,
# which has a tolerance of ~1e-6 relative. Match that here.
_FLOAT_EPS = 1e-6


@dataclass
class GazeCorrection:
    """Mirrors Unity's `GazeCorrection`. Defaults are a no-op."""

    gaze_yaw_offset_deg: float = 0.0
    gaze_pitch_offset_deg: float = 0.0
    gaze_yaw_gain: float = 1.0
    gaze_pitch_gain: float = 1.0

    @classmethod
    def from_dict(cls, d: dict) -> "GazeCorrection":
        return cls(
            gaze_yaw_offset_deg=float(d.get("gazeYawOffsetDeg", 0.0)),
            gaze_pitch_offset_deg=float(d.get("gazePitchOffsetDeg", 0.0)),
            gaze_yaw_gain=float(d.get("gazeYawGain", 1.0)),
            gaze_pitch_gain=float(d.get("gazePitchGain", 1.0)),
        )

    def is_identity(self) -> bool:
        return (
            abs(self.gaze_yaw_offset_deg) < _FLOAT_EPS
            and abs(self.gaze_pitch_offset_deg) < _FLOAT_EPS
            and abs(self.gaze_yaw_gain - 1.0) < _FLOAT_EPS
            and abs(self.gaze_pitch_gain - 1.0) < _FLOAT_EPS
        )


@dataclass
class EyeTrackingProfile:
    """Subset of the Unity `EyeTrackingProfile` schema relevant for gaze
    correction. Vergence/constraint settings are loaded but unused — they
    affect the live pipeline only."""

    schema_version: str = "1.0"
    profile_name: str = ""
    participant_id: str = ""
    combined_gaze: GazeCorrection = None  # type: ignore[assignment]
    left_eye: GazeCorrection = None  # type: ignore[assignment]
    right_eye: GazeCorrection = None  # type: ignore[assignment]

    def __post_init__(self):
        if self.combined_gaze is None:
            self.combined_gaze = GazeCorrection()
        if self.left_eye is None:
            self.left_eye = GazeCorrection()
        if self.right_eye is None:
            self.right_eye = GazeCorrection()


def load_profile(path: Union[str, Path]) -> EyeTrackingProfile:
    """Load an EyeTrackingProfile JSON from disk.

    Raises:
        FileNotFoundError: if `path` doesn't exist.
        ValueError: if the schemaVersion's major component doesn't match
            this loader's expected major (currently `1`).
    """
    p = Path(path)
    with p.open("r") as f:
        blob = json.load(f)

    schema_version = str(blob.get("schemaVersion", ""))
    major = schema_version.split(".")[0] if schema_version else ""
    if major != "1":
        raise ValueError(
            f"{p}: profile schemaVersion '{schema_version}' is not "
            f"supported by this loader (expected major=1)."
        )

    metadata = blob.get("metadata", {}) or {}
    return EyeTrackingProfile(
        schema_version=schema_version,
        profile_name=str(metadata.get("profileName", "")),
        participant_id=str(metadata.get("participantID", "")),
        combined_gaze=GazeCorrection.from_dict(blob.get("combinedGaze", {}) or {}),
        left_eye=GazeCorrection.from_dict(blob.get("leftEye", {}) or {}),
        right_eye=GazeCorrection.from_dict(blob.get("rightEye", {}) or {}),
    )


def _decompose_yaw_pitch_deg(directions: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    """Convert head-local unit vectors `(N, 3)` to Unity-convention
    (yaw_deg, pitch_deg). Yaw is `atan2(x, z)`; pitch is `-asin(y)`."""
    x = directions[:, 0]
    y = directions[:, 1]
    z = directions[:, 2]
    yaw_deg = np.degrees(np.arctan2(x, z))
    pitch_deg = -np.degrees(np.arcsin(np.clip(y, -1.0, 1.0)))
    return yaw_deg, pitch_deg


def _recompose_from_yaw_pitch_deg(yaw_deg: np.ndarray, pitch_deg: np.ndarray) -> np.ndarray:
    """Inverse of `_decompose_yaw_pitch_deg`. Returns `(N, 3)` unit vectors."""
    yaw_rad = np.radians(yaw_deg)
    pitch_rad = -np.radians(pitch_deg)
    cos_p = np.cos(pitch_rad)
    out = np.column_stack([
        np.sin(yaw_rad) * cos_p,
        np.sin(pitch_rad),
        np.cos(yaw_rad) * cos_p,
    ])
    norms = np.linalg.norm(out, axis=1, keepdims=True)
    norms[norms < 1e-9] = 1.0
    return out / norms


def apply_combined_correction(
    world_directions: np.ndarray,
    head_quaternions_xyzw: np.ndarray,
    correction: GazeCorrection,
) -> np.ndarray:
    """Apply a `GazeCorrection` to an array of world-space gaze
    directions, given the recorded head rotation per sample.

    Args:
        world_directions: shape `(N, 3)`, world-space gaze unit vectors.
        head_quaternions_xyzw: shape `(N, 4)` in `(x, y, z, w)` order.
        correction: the correction to apply.

    Returns:
        Corrected world-space directions, shape `(N, 3)`. If the
        correction is identity (default), the input is returned
        unchanged (no copy).
    """
    if correction.is_identity():
        return world_directions

    if world_directions.ndim != 2 or world_directions.shape[1] != 3:
        raise ValueError(f"world_directions must be (N, 3), got {world_directions.shape}")
    if head_quaternions_xyzw.shape != (world_directions.shape[0], 4):
        raise ValueError(
            f"head_quaternions_xyzw must be ({world_directions.shape[0]}, 4) "
            f"in (x,y,z,w) order, got {head_quaternions_xyzw.shape}"
        )

    rot = Rotation.from_quat(head_quaternions_xyzw)
    head_local = rot.inv().apply(world_directions)

    yaw_deg, pitch_deg = _decompose_yaw_pitch_deg(head_local)
    yaw_deg = yaw_deg * correction.gaze_yaw_gain + correction.gaze_yaw_offset_deg
    pitch_deg = pitch_deg * correction.gaze_pitch_gain + correction.gaze_pitch_offset_deg

    corrected_local = _recompose_from_yaw_pitch_deg(yaw_deg, pitch_deg)
    return rot.apply(corrected_local)


def apply_profile_to_csv(
    profile_path: Union[str, Path],
    csv_in: Union[str, Path],
    csv_out: Union[str, Path],
    apply_per_eye: bool = True,
    overwrite: bool = False,
) -> dict:
    """Re-apply a profile's correction to every gaze direction column in
    a recorded CSV.

    The output CSV is byte-equivalent to the input except for:
      - `CombinedDir_X/Y/Z` (always rewritten when combined correction
        is non-identity)
      - `LeftDir_X/Y/Z`, `RightDir_X/Y/Z` (rewritten when
        `apply_per_eye=True` and the per-eye correction is non-identity)
      - A new `# AppliedProfile: <name>` metadata header line.

    Direction validity flags (`HasCombinedDirection`, etc.) are not
    modified — invalid samples have their directions multiplied by
    correction matrices but the validity flag remains False so downstream
    consumers still ignore them. We do skip rows with NaN/Inf directions
    to avoid scipy warnings, leaving them unchanged.

    Args:
        profile_path: path to the `EyeTrackingProfile` JSON.
        csv_in: input CSV (raw recording).
        csv_out: output path. Refuses to overwrite unless `overwrite=True`.
        apply_per_eye: whether to also rewrite per-eye directions.
        overwrite: if False (default), raises FileExistsError when csv_out exists.

    Returns:
        A dict with `samples_total`, `samples_corrected_combined`,
        `samples_corrected_left`, `samples_corrected_right`,
        `samples_skipped_invalid`, and `profile_name`.
    """
    profile_path = Path(profile_path)
    csv_in = Path(csv_in)
    csv_out = Path(csv_out)

    if csv_out.exists() and not overwrite:
        raise FileExistsError(
            f"{csv_out} already exists; pass overwrite=True to replace it."
        )

    profile = load_profile(profile_path)

    metadata_lines, header_line, data_text = _split_csv_sections(csv_in)
    df = pd.read_csv(csv_in, comment="#")

    stats = {
        "samples_total": len(df),
        "samples_corrected_combined": 0,
        "samples_corrected_left": 0,
        "samples_corrected_right": 0,
        "samples_skipped_invalid": 0,
        "profile_name": profile.profile_name,
    }

    quat_cols = ("HeadRot_X", "HeadRot_Y", "HeadRot_Z", "HeadRot_W")
    if not all(c in df.columns for c in quat_cols):
        raise ValueError(
            f"{csv_in} is missing one or more head-rotation columns "
            f"{quat_cols}; cannot project gaze into head-local space."
        )
    quat = df[list(quat_cols)].to_numpy(dtype=float)

    quat_finite = np.all(np.isfinite(quat), axis=1) & (np.linalg.norm(quat, axis=1) > 1e-9)
    stats["samples_skipped_invalid"] = int((~quat_finite).sum())

    _correct_inplace(df, ("CombinedDir_X", "CombinedDir_Y", "CombinedDir_Z"),
                     quat, quat_finite, profile.combined_gaze, stats, "samples_corrected_combined")
    if apply_per_eye:
        _correct_inplace(df, ("LeftDir_X", "LeftDir_Y", "LeftDir_Z"),
                         quat, quat_finite, profile.left_eye, stats, "samples_corrected_left")
        _correct_inplace(df, ("RightDir_X", "RightDir_Y", "RightDir_Z"),
                         quat, quat_finite, profile.right_eye, stats, "samples_corrected_right")

    csv_out.parent.mkdir(parents=True, exist_ok=True)
    with csv_out.open("w", newline="") as f:
        for line in metadata_lines:
            f.write(line)
            if not line.endswith("\n"):
                f.write("\n")
        f.write(f"# AppliedProfile: {profile.profile_name or profile_path.stem}\n")
        df.to_csv(f, index=False, lineterminator="\n")

    return stats


def _correct_inplace(
    df: pd.DataFrame,
    cols: Tuple[str, str, str],
    quat: np.ndarray,
    quat_finite: np.ndarray,
    correction: GazeCorrection,
    stats: dict,
    stat_key: str,
) -> None:
    """Apply `correction` to the three `cols` of `df` in place. Rows
    with non-finite head quaternions or non-finite directions are
    untouched."""
    if correction.is_identity():
        return
    if not all(c in df.columns for c in cols):
        return  # column set absent — silently skip (e.g. left-eye-only recording)

    dirs = df[list(cols)].to_numpy(dtype=float)
    dir_finite = np.all(np.isfinite(dirs), axis=1)
    valid_mask = quat_finite & dir_finite
    if not valid_mask.any():
        return

    corrected = apply_combined_correction(dirs[valid_mask], quat[valid_mask], correction)
    dirs[valid_mask] = corrected
    df.loc[:, list(cols)] = dirs
    stats[stat_key] = int(valid_mask.sum())


def _split_csv_sections(path: Path) -> Tuple[list, str, str]:
    """Read a CSV and split it into (metadata `# ...` lines, header line,
    rest-of-file as text). Used so we can preserve the metadata block
    when writing the corrected output."""
    metadata: list = []
    header: Optional[str] = None
    rest_lines = []
    with path.open("r") as f:
        for line in f:
            if header is None:
                if line.startswith("#"):
                    metadata.append(line)
                    continue
                header = line
            else:
                rest_lines.append(line)
    return metadata, header or "", "".join(rest_lines)
