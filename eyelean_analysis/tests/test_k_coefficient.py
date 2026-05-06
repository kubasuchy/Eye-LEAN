"""Tests for the canonical Krejtz K-coefficient.

Locks in K_i = z(a_i) - z(d_i), K = mean_i(K_i) per Krejtz et al. 2016
(PLoS ONE Eq. 1). Covers both the local-stats and pooled-stats usage
modes, plus the canonical sliding-window K(t) reporter.
"""
from __future__ import annotations

import numpy as np
import pytest

from eyelean_analysis.classification.k_coefficient import (
    AttentionType,
    KCoefficientCalculator,
    KCoefficientResult,
    calculate_k_coefficient,
    classify_attention,
)
from eyelean_analysis.classification.velocity_classifier import Fixation, Saccade


def _mk_fix(duration: float, idx: int = 0) -> Fixation:
    return Fixation(
        start_idx=idx, end_idx=idx + 1,
        start_time=float(idx), end_time=float(idx) + duration,
        duration=duration, centroid_x=0.0, centroid_y=0.0,
        sample_count=2,
    )


def _mk_sac(amplitude: float, idx: int = 0) -> Saccade:
    return Saccade(
        start_idx=idx, end_idx=idx + 1,
        start_time=float(idx), end_time=float(idx) + 0.05,
        duration=0.05, amplitude=amplitude, peak_velocity=200.0,
        start_x=0.0, start_y=0.0, end_x=amplitude, end_y=0.0,
    )


class TestKrejtzFormula:
    def test_matches_eq1_formula_directly(self):
        # Hand-pick fixations and saccades; compute K_i = z(a_i) - z(d_i)
        # by hand and check the calculator agrees.
        durations  = np.array([0.10, 0.30, 0.20, 0.50, 0.15])
        amplitudes = np.array([8.0,  3.0,  6.0,  2.0,  10.0])
        fixations = [_mk_fix(d, i) for i, d in enumerate(durations)]
        saccades  = [_mk_sac(a, i) for i, a in enumerate(amplitudes)]

        z_d = (durations  - durations.mean())  / durations.std(ddof=1)
        z_a = (amplitudes - amplitudes.mean()) / amplitudes.std(ddof=1)
        expected_K = float((z_a - z_d).mean())

        result = calculate_k_coefficient(fixations, saccades)
        assert result.k_coefficient == pytest.approx(expected_K, rel=1e-9, abs=1e-12)

    def test_hand_calculation_round_trip(self):
        # Exercise a second hand-picked dataset and confirm the calculator
        # reproduces a direct (z(a) - z(d)).mean() computation.
        ds  = np.array([0.45, 0.50, 0.55, 0.10])
        as_ = np.array([1.0,  1.5,  2.0,  15.0])
        fixations = [_mk_fix(d, i) for i, d in enumerate(ds)]
        saccades  = [_mk_sac(a, i) for i, a in enumerate(as_)]

        z_d = (ds  - ds.mean())  / ds.std(ddof=1)
        z_a = (as_ - as_.mean()) / as_.std(ddof=1)
        expected = float((z_a - z_d).mean())

        result = calculate_k_coefficient(fixations, saccades)
        assert result.k_coefficient == pytest.approx(expected, rel=1e-9, abs=1e-12)
        assert result.attention_type in (AttentionType.FOCAL, AttentionType.AMBIENT, AttentionType.NEUTRAL)

    def test_pairing_truncates_to_min_count(self):
        # 5 fixations, 3 saccades ⇒ only 3 pairs. The trailing 2 fixations
        # are dropped (no following saccade exists).
        fixations = [_mk_fix(0.1 * (i + 1), i) for i in range(5)]
        saccades  = [_mk_sac(2.0 * (i + 1), i) for i in range(3)]
        result = calculate_k_coefficient(fixations, saccades)

        ds = np.array([0.1, 0.2, 0.3])
        as_ = np.array([2.0, 4.0, 6.0])
        z_d = (ds  - ds.mean())  / ds.std(ddof=1)
        z_a = (as_ - as_.mean()) / as_.std(ddof=1)
        expected = float((z_a - z_d).mean())
        assert result.k_coefficient == pytest.approx(expected, rel=1e-9, abs=1e-12)


class TestPooledStatsAndWindowedK:
    def test_whole_recording_returns_zero_with_local_stats(self):
        # Mathematical truism: mean of (z(a) - z(d)) over the same set
        # used to compute z-scores is identically zero.
        ds  = np.array([0.10, 0.30, 0.20, 0.50, 0.15, 0.40, 0.25])
        as_ = np.array([8.0,  3.0,  6.0,  2.0,  10.0, 4.0,  7.0])
        fixations = [_mk_fix(d, i) for i, d in enumerate(ds)]
        saccades  = [_mk_sac(a, i) for i, a in enumerate(as_)]
        result = calculate_k_coefficient(fixations, saccades)
        assert abs(result.k_coefficient) < 1e-9

    def test_pooled_stats_arg_yields_nonzero_K_on_subset(self):
        # With pooled stats from a broader pool, evaluating K on a
        # subset that's biased relative to the pool yields non-zero K.
        ds_pool  = np.array([0.10, 0.30, 0.20, 0.50, 0.15, 0.40, 0.25, 0.45, 0.05, 0.35])
        as_pool  = np.array([8.0,  3.0,  6.0,  2.0,  10.0, 4.0,  7.0,  3.5,  12.0, 5.0])
        pooled = (
            float(ds_pool.mean()), float(ds_pool.std(ddof=1)),
            float(as_pool.mean()), float(as_pool.std(ddof=1)),
        )
        # Subset: long fixations + short saccades (focal pattern).
        focal_idx = [3, 5, 7, 9]  # ds: 0.50, 0.40, 0.45, 0.35; as: 2, 4, 3.5, 5
        fix_subset = [_mk_fix(ds_pool[i], i) for i in focal_idx]
        sac_subset = [_mk_sac(as_pool[i], i) for i in focal_idx]
        calc = KCoefficientCalculator()
        result = calc.calculate(fix_subset, sac_subset, pooled_stats=pooled)
        assert result.k_coefficient < 0, (
            f"focal-pattern subset (long fix, short sac) should give K<0 "
            f"under z(a)-z(d) sign convention; got {result.k_coefficient}"
        )

    def test_calculate_per_window_yields_time_varying_K(self):
        # Synthetic session: first half has long fixations + short
        # saccades (focal), second half has short fixations + long
        # saccades (ambient). Per-window K(t) should produce different-
        # sign medians for the two halves under any consistent sign
        # convention.
        n_pairs = 60
        fixations, saccades = [], []
        t = 0.0
        boundary_idx = n_pairs // 2
        for i in range(n_pairs):
            d = 0.5 if i < boundary_idx else 0.1
            a = 2.0 if i < boundary_idx else 15.0
            f = Fixation(start_idx=i, end_idx=i+1, start_time=t, end_time=t+d,
                         duration=d, centroid_x=0, centroid_y=0, sample_count=2)
            s = Saccade(start_idx=i, end_idx=i+1, start_time=t+d, end_time=t+d+0.05,
                        duration=0.05, amplitude=a, peak_velocity=200,
                        start_x=0, start_y=0, end_x=a, end_y=0)
            fixations.append(f); saccades.append(s)
            t += d + 0.05
        boundary_t = fixations[boundary_idx].start_time

        calc = KCoefficientCalculator()
        windows = calc.calculate_per_window(fixations, saccades, window_seconds=2.0, step_seconds=1.0)
        assert len(windows) > 4

        first_half  = [w.k_coefficient for w in windows if w.window_end <= boundary_t]
        second_half = [w.k_coefficient for w in windows if w.window_start >= boundary_t]
        assert len(first_half) > 0 and len(second_half) > 0
        first_med = float(np.median(first_half))
        second_med = float(np.median(second_half))
        assert np.sign(first_med) != np.sign(second_med), (
            f"K(t) failed to distinguish synthetic focal/ambient halves: "
            f"first half median {first_med:.3f}, second half median {second_med:.3f}"
        )

    def test_per_window_too_few_pairs_returns_empty(self):
        calc = KCoefficientCalculator()
        result = calc.calculate_per_window([_mk_fix(0.1)], [_mk_sac(5.0)], window_seconds=5.0)
        assert result == []


class TestEdgeCases:
    def test_too_few_pairs_returns_unknown(self):
        result = calculate_k_coefficient([_mk_fix(0.1)], [_mk_sac(5.0)])
        assert result.attention_type == AttentionType.UNKNOWN
        assert result.k_coefficient == 0.0

    def test_constant_durations_returns_unknown_not_inf(self):
        # Zero variance in durations ⇒ z-score undefined.
        fixations = [_mk_fix(0.2, i) for i in range(5)]
        saccades  = [_mk_sac(2.0 * (i + 1), i) for i in range(5)]
        result = calculate_k_coefficient(fixations, saccades)
        assert result.attention_type == AttentionType.UNKNOWN
        assert np.isfinite(result.k_coefficient)

    def test_constant_amplitudes_returns_unknown(self):
        fixations = [_mk_fix(0.1 * (i + 1), i) for i in range(5)]
        saccades  = [_mk_sac(5.0, i) for i in range(5)]
        result = calculate_k_coefficient(fixations, saccades)
        assert result.attention_type == AttentionType.UNKNOWN


class TestClassification:
    def test_sign_based_classification_default(self):
        assert classify_attention(0.5) == AttentionType.FOCAL
        assert classify_attention(-0.5) == AttentionType.AMBIENT
        assert classify_attention(0.0) == AttentionType.NEUTRAL  # exactly zero
        assert classify_attention(1e-10) == AttentionType.FOCAL

    def test_dead_zone_widens_neutral_band_for_visualization(self):
        assert classify_attention(0.3, dead_zone=0.5) == AttentionType.NEUTRAL
        assert classify_attention(0.6, dead_zone=0.5) == AttentionType.FOCAL
        assert classify_attention(-0.6, dead_zone=0.5) == AttentionType.AMBIENT

    def test_nan_classifies_as_unknown(self):
        assert classify_attention(float("nan")) == AttentionType.UNKNOWN

    def test_result_object_input(self):
        r = KCoefficientResult(
            k_coefficient=0.7, attention_type=AttentionType.UNKNOWN,
            mean_fixation_duration=0.2, mean_saccade_amplitude=5.0,
            n_fixations=10, n_saccades=10,
        )
        assert classify_attention(r) == AttentionType.FOCAL
