"""Tests for the Python RIPA2 implementation.

Parity with the Unity ``RIPA2Analyzer`` is exercised by deriving the SG
filter coefficients via the same Vandermonde-fit math and asserting the
analytic closed forms for low orders.
"""
from __future__ import annotations

import numpy as np
import pytest

from eyelean_analysis.metrics.ripa2 import (
    CLIP_MAX,
    CLIP_MIN,
    calculate_ripa2,
    schaefer_half_width,
    sg_first_derivative_coeffs,
)


# ---- SG coefficients ----


def test_sg_M2_N2_matches_analytic():
    # h[i] = (i - 2) / sum_{k=-2..2} k^2  =  (i - 2) / 10
    h = sg_first_derivative_coeffs(2, 2)
    np.testing.assert_allclose(h, [-0.2, -0.1, 0.0, 0.1, 0.2], atol=1e-12)


def test_sg_M5_N2_matches_analytic():
    h = sg_first_derivative_coeffs(5, 2)
    expected = np.array([(i - 5) / 110.0 for i in range(11)])
    np.testing.assert_allclose(h, expected, atol=1e-12)


def test_sg_recovers_linear_slope():
    h = sg_first_derivative_coeffs(5, 2)
    y = np.array([3.0 * (i - 5) + 7.0 for i in range(11)])
    np.testing.assert_allclose(float(h @ y), 3.0, atol=1e-10)


def test_sg_order3_recovers_cubic_slope_at_center():
    # y = 2 t^3 + 4 t + 11 → dy/dt at t=0 is 4
    h = sg_first_derivative_coeffs(5, 3)
    y = np.array([2 * (i - 5) ** 3 + 4 * (i - 5) + 11 for i in range(11)], dtype=np.float64)
    np.testing.assert_allclose(float(h @ y), 4.0, atol=1e-9)


# ---- Schäfer cutoff sizing ----


def test_schaefer_60hz_vlf():
    # fs=60, target=0.29 → fc=0.00966; N=2 → M ≈ 98
    M = schaefer_half_width(60.0, 0.29, 2)
    assert 96 <= M <= 100


def test_schaefer_60hz_lf():
    M = schaefer_half_width(60.0, 4.0, 4)
    assert 12 <= M <= 15


def test_schaefer_rejects_invalid_inputs():
    with pytest.raises(ValueError):
        schaefer_half_width(60.0, 30.0, 2)  # > Nyquist
    with pytest.raises(ValueError):
        schaefer_half_width(60.0, -1.0, 2)
    with pytest.raises(ValueError):
        schaefer_half_width(60.0, 4.0, 0)


# ---- RIPA2 end-to-end ----


def test_ripa2_constant_signal_is_zero():
    fs = 60.0
    n = 600
    pupil = np.full(n, 3.5)
    res = calculate_ripa2(pupil, fs, vlf_half_width=30, lf_half_width=5)
    finite = res.ripa2_raw[np.isfinite(res.ripa2_raw)]
    assert finite.size > 0
    np.testing.assert_allclose(finite, 0.0, atol=1e-12)


def test_ripa2_clipped_to_paper_range():
    rng = np.random.default_rng(42)
    fs = 60.0
    n = 800
    pupil = 3.5 + np.cumsum(rng.standard_normal(n) * 0.05)
    pupil = np.clip(pupil, 1.5, 6.0)
    res = calculate_ripa2(pupil, fs, vlf_half_width=30, lf_half_width=5)
    finite = res.ripa2_raw[np.isfinite(res.ripa2_raw)]
    assert finite.size > 0
    assert finite.min() >= CLIP_MIN
    assert finite.max() <= CLIP_MAX


def test_ripa2_warmup_is_nan():
    fs = 60.0
    pupil = np.full(300, 3.5)
    res = calculate_ripa2(pupil, fs, vlf_half_width=30, lf_half_width=5)
    # First M_VLF samples have no centered VLF window → NaN.
    assert np.all(~np.isfinite(res.ripa2_raw[:30]))
    # Last M_VLF samples likewise.
    assert np.all(~np.isfinite(res.ripa2_raw[-30:]))
    # Middle: at least one finite value.
    assert np.any(np.isfinite(res.ripa2_raw[30:-30]))


def test_ripa2_short_input_returns_all_nan():
    # Input shorter than the VLF window → no valid output.
    fs = 60.0
    res = calculate_ripa2(np.zeros(10), fs, vlf_half_width=30, lf_half_width=5)
    assert np.all(~np.isfinite(res.ripa2_raw))
    assert np.all(~np.isfinite(res.ripa2_smoothed))


def test_ripa2_lf_must_fit_in_vlf():
    fs = 60.0
    with pytest.raises(ValueError):
        calculate_ripa2(np.zeros(200), fs, vlf_half_width=5, lf_half_width=30)


def test_ripa2_smoothed_tracks_raw_within_clip_range():
    rng = np.random.default_rng(7)
    fs = 60.0
    n = 1200
    pupil = 3.5 + 0.1 * np.sin(np.linspace(0, 8 * np.pi, n)) + rng.normal(scale=0.02, size=n)
    res = calculate_ripa2(pupil, fs, vlf_half_width=30, lf_half_width=5, smoothing_seconds=1.0)
    finite_smooth = res.ripa2_smoothed[np.isfinite(res.ripa2_smoothed)]
    assert finite_smooth.size > 0
    assert finite_smooth.min() >= CLIP_MIN
    assert finite_smooth.max() <= CLIP_MAX
