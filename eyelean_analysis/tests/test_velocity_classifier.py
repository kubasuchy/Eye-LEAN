"""I-VT classifier tests: synthetic signal with known structure."""

import warnings

import numpy as np

from eyelean_analysis.classification.velocity_classifier import (
    VelocityClassifier,
    detect_eye_movements,
)


def _classify(gaze_x, gaze_y, timestamps):
    """Run the I-VT classifier with merge_threshold disabled so a clean
    synthetic gap of ~100 ms isn't fused by the post-detection merger
    (which defaults to 75 ms — close enough to the saccade duration that
    SG-velocity edge effects can occasionally bring the measured gap
    under threshold and merge real fixations)."""
    vc = VelocityClassifier(velocity_threshold=50.0, merge_threshold=0.0)
    return vc.detect_eye_movements(gaze_x, gaze_y, timestamps)


def test_detects_three_fixations_and_two_saccades(fixation_saccade_signal):
    """Synthetic 1s/100ms/1s/100ms/1s fixation-saccade-fixation-saccade-fixation
    pattern at 120 Hz should yield 3 fixations and 2 saccades."""
    movements = _classify(*fixation_saccade_signal)

    assert len(movements["fixations"]) == 3, (
        f"expected 3 fixations, got {len(movements['fixations'])}"
    )
    assert len(movements["saccades"]) == 2, (
        f"expected 2 saccades, got {len(movements['saccades'])}"
    )


def test_fixation_centroids_match_known_positions(fixation_saccade_signal):
    """Fixation centroids should land within 0.5° of the planted
    positions (0,0), (10,0), (10,5)."""
    movements = _classify(*fixation_saccade_signal)

    expected = [(0.0, 0.0), (10.0, 0.0), (10.0, 5.0)]
    for fix, (ex, ey) in zip(movements["fixations"], expected):
        assert abs(fix.centroid_x - ex) < 0.5
        assert abs(fix.centroid_y - ey) < 0.5


def test_estimate_sample_rate_warns_on_short_input():
    """M12 regression: <2 timestamps must produce a 120 Hz fallback warning,
    not silently mis-classify downstream."""
    vc = VelocityClassifier()
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        rate = vc._estimate_sample_rate(np.array([1.0]))
    assert rate == 120.0
    assert any("120 Hz" in str(w.message) for w in caught)


def test_estimate_sample_rate_warns_on_non_monotonic_timestamps():
    """M12 regression: a non-positive median delta also warns + falls back."""
    vc = VelocityClassifier()
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        rate = vc._estimate_sample_rate(np.array([1.0, 1.0, 1.0, 1.0]))
    assert rate == 120.0
    assert any("120 Hz" in str(w.message) for w in caught)
