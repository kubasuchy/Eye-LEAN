"""Coverage for previously-untested metrics modules: data_quality and entropy."""

import numpy as np
import pytest

from eyelean_analysis.metrics.data_quality import calculate_quality_metrics
from eyelean_analysis.metrics.entropy import (
    calculate_gaze_entropy,
    stationary_entropy,
    transition_entropy,
)


def test_quality_all_valid_clean_signal():
    fs = 120.0
    n = int(2 * fs)
    ts = np.arange(n) / fs
    validity = np.ones(n, dtype=bool)
    pupil = np.full(n, 3.5)
    q = calculate_quality_metrics(
        timestamps=ts, validity=validity,
        left_pupil=pupil, right_pupil=pupil,
    )
    assert q.total_samples == n
    assert q.validity_percent == 100.0
    assert q.invalid_samples == 0
    assert abs(q.sample_rate_hz - fs) < 1.0
    # Clean uniform pupil → high quality
    assert q.quality_score > 50.0


def test_quality_with_gap():
    # 1 s of samples at 120 Hz, then 0.5 s gap, then more samples
    pre = np.arange(120) / 120.0
    post = pre[-1] + 0.5 + np.arange(120) / 120.0
    ts = np.concatenate([pre, post])
    validity = np.ones(len(ts), dtype=bool)
    q = calculate_quality_metrics(
        timestamps=ts, validity=validity, gap_threshold=0.1,
    )
    assert q.n_gaps >= 1
    assert q.max_gap_duration >= 0.4


def test_quality_partial_validity():
    fs = 120.0
    n = 240
    ts = np.arange(n) / fs
    validity = np.ones(n, dtype=bool)
    validity[60:120] = False  # 60 invalid samples = 25%
    q = calculate_quality_metrics(timestamps=ts, validity=validity)
    assert q.invalid_samples == 60
    assert abs(q.validity_percent - 75.0) < 0.1


def test_quality_handles_no_validity_array():
    ts = np.arange(120) / 120.0
    # No validity, no pupil — still produces a result
    q = calculate_quality_metrics(timestamps=ts)
    assert q.total_samples == 120
    assert q.duration_seconds > 0


def test_entropy_uniform_distribution_high():
    """Uniformly-distributed gaze should produce near-maximal entropy."""
    rng = np.random.default_rng(0)
    n = 5000
    gaze_x = rng.uniform(-1, 1, n)
    gaze_y = rng.uniform(-1, 1, n)
    res = calculate_gaze_entropy(gaze_x, gaze_y, horizontal_bins=8, vertical_bins=8)
    assert res.is_valid
    # log2(64) = 6.0; uniform should reach >90% of max
    assert res.normalized_entropy > 0.9


def test_entropy_single_point_zero():
    """All gaze in one bin should produce ~zero entropy."""
    n = 1000
    gaze_x = np.full(n, 0.0)
    gaze_y = np.full(n, 0.0)
    res = calculate_gaze_entropy(gaze_x, gaze_y, horizontal_bins=8, vertical_bins=8)
    assert res.is_valid
    assert res.entropy < 0.01
    assert res.n_bins_used == 1


def test_entropy_empty_input_invalid():
    res = calculate_gaze_entropy(np.array([]), np.array([]))
    assert not res.is_valid


def test_stationary_vs_transition_entropy():
    """`stationary_entropy` returns a float; `transition_entropy` returns
    a float too. A random-walk gaze should have non-zero entropy by
    both measures."""
    rng = np.random.default_rng(1)
    n = 2000
    x = rng.uniform(-1, 1, n)
    y = rng.uniform(-1, 1, n)
    s = stationary_entropy(x, y)
    t = transition_entropy(x, y, horizontal_bins=4, vertical_bins=4)
    assert np.isfinite(s) and s > 0
    assert np.isfinite(t) and t > 0
    # Stationary entropy ≤ log2(12*12) ≈ 7.17
    assert s <= np.log2(12 * 12) + 1e-6


class TestTransitionEntropyAOIMode:
    """Canonical Krejtz 2015 GTE input mode: explicit AOI labels."""

    def test_two_state_uniform_chain_matches_hand_calculation(self):
        # Sequence [0, 1, 0, 1, 0, 1, ...]: every state goes to the
        # other state with P=1; conditional entropy is 0 (deterministic
        # transitions).
        seq = np.tile([0, 1], 50)
        h = transition_entropy(aoi_sequence=seq)
        assert h == pytest.approx(0.0, abs=1e-12)

    def test_random_two_state_chain_approaches_one_bit(self):
        # Independent uniform 50/50 transitions: P(j|i) = 0.5 for all
        # (i,j); H_t = log2(2) = 1 bit, asymptotically. With finite
        # sample size we check approach to 1 within tolerance.
        rng = np.random.default_rng(3)
        seq = rng.integers(0, 2, size=20000)
        h = transition_entropy(aoi_sequence=seq, n_aois=2)
        assert h == pytest.approx(1.0, abs=0.02)

    def test_three_state_dirichlet_chain_under_log2_n(self):
        # H_t for any n-state Markov chain is upper-bounded by log2(n).
        rng = np.random.default_rng(4)
        seq = rng.integers(0, 3, size=10000)
        h = transition_entropy(aoi_sequence=seq, n_aois=3)
        assert 0 < h <= np.log2(3) + 1e-9

    def test_too_short_sequence_returns_zero(self):
        assert transition_entropy(aoi_sequence=np.array([0])) == 0.0
        assert transition_entropy(aoi_sequence=np.array([])) == 0.0

    def test_n_aois_inferred_when_not_passed(self):
        # Sequence uses labels {0,1,2}; n_aois is inferred from max + 1.
        seq = np.array([0, 1, 2, 1, 0, 2, 1])
        h_inferred = transition_entropy(aoi_sequence=seq)
        h_explicit = transition_entropy(aoi_sequence=seq, n_aois=3)
        assert h_inferred == pytest.approx(h_explicit, abs=1e-12)

    def test_aoi_mode_and_spatial_mode_produce_different_values(self):
        # Same fixation centroids fed in both modes should disagree (the
        # spatial-proxy mode buckets into 12×12 cells that don't match
        # the user-defined 3 AOIs).
        rng = np.random.default_rng(7)
        n = 500
        x = rng.uniform(-1, 1, n)
        y = rng.uniform(-1, 1, n)
        seq = ((x > 0).astype(int) + (y > 0).astype(int))  # 3 AOIs: 0, 1, 2
        h_aoi = transition_entropy(aoi_sequence=seq, n_aois=3)
        h_spatial = transition_entropy(x, y, horizontal_bins=12, vertical_bins=12)
        assert abs(h_aoi - h_spatial) > 0.1, (
            "AOI-mode GTE happens to match spatial-proxy GTE — pick "
            "different test data."
        )

    def test_calling_with_no_arguments_raises(self):
        with pytest.raises(ValueError):
            transition_entropy()
