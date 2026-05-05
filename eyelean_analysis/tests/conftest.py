"""Shared fixtures for the eyelean_analysis test suite."""

import numpy as np
import pandas as pd
import pytest


@pytest.fixture
def synthetic_eyelean_csv(tmp_path):
    """Write a minimal Eye_lean-style CSV (mixed canonical and aliased
    column names) to a temp path and return that path. Exercises the
    loader's column-mapping logic on a file the loader has never seen."""
    df = pd.DataFrame({
        # Canonical-cased; matches the alias dict's first entry
        "UnityTimestamp": np.arange(0.0, 1.0, 1 / 120),
        "HeadPos_X": np.zeros(120),
        "HeadPos_Y": np.full(120, 1.5),
        "HeadPos_Z": np.zeros(120),
        # Mixed-case alias for combined origin
        "CombinedEyeOriginX": np.zeros(120),
        "CombinedEyeOriginY": np.full(120, 1.5),
        "CombinedEyeOriginZ": np.zeros(120),
        # Direction columns (world space, unit vectors pointing forward)
        "CombinedDir_X": np.zeros(120),
        "CombinedDir_Y": np.zeros(120),
        "CombinedDir_Z": np.ones(120),
        # Boolean-like text the loader should parse
        "HasCombinedOrigin": ["True"] * 120,
        "IsTrackingValid": ["True"] * 120,
    })
    csv_path = tmp_path / "synthetic.csv"
    df.to_csv(csv_path, index=False)
    return csv_path


@pytest.fixture
def fixation_saccade_signal():
    """Build a synthetic gaze trace at 120 Hz with a known structure:

      - 1.0 s fixation at (0, 0)
      - 100 ms saccade ramp to (10, 0)  → ~100 deg/s peak velocity
      - 1.0 s fixation at (10, 0)
      - 100 ms saccade ramp to (10, 5)
      - 1.0 s fixation at (10, 5)

    Saccade duration must exceed VelocityClassifier's default 75 ms
    `merge_threshold` so the post-detection merger doesn't fuse the
    flanking fixations across the saccade gap.

    Two saccades, three fixations. Returns (gaze_x, gaze_y, timestamps).
    Signal is in angular degrees as required by detect_eye_movements."""
    fs = 120.0
    fix_dur = 1.0
    sacc_dur = 0.1
    rng = np.random.default_rng(seed=0)

    def fix(x, y, dur):
        n = int(round(dur * fs))
        # Tiny jitter so velocity isn't identically zero (more realistic)
        return (
            np.full(n, x) + rng.normal(0, 0.01, n),
            np.full(n, y) + rng.normal(0, 0.01, n),
        )

    def ramp(x0, y0, x1, y1, dur):
        n = int(round(dur * fs))
        return (
            np.linspace(x0, x1, n, endpoint=False),
            np.linspace(y0, y1, n, endpoint=False),
        )

    segs = [
        fix(0, 0, fix_dur),
        ramp(0, 0, 10, 0, sacc_dur),
        fix(10, 0, fix_dur),
        ramp(10, 0, 10, 5, sacc_dur),
        fix(10, 5, fix_dur),
    ]
    gaze_x = np.concatenate([s[0] for s in segs])
    gaze_y = np.concatenate([s[1] for s in segs])
    timestamps = np.arange(len(gaze_x)) / fs
    return gaze_x, gaze_y, timestamps
