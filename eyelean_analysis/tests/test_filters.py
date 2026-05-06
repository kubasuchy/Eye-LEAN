"""Savitzky-Golay filter edge cases."""

import numpy as np
import pytest
from scipy import signal as scipy_signal

from eyelean_analysis.filters.savitzky_golay import (
    SavitzkyGolayFilter,
    savgol_smooth,
)


def test_smoothing_lookup_matches_scipy():
    """Every entry in SMOOTHING_COEFFICIENTS must match what scipy
    computes for the same (window, poly_order). Prevents drift
    between the hand-typed coefficient table and the canonical
    Savitzky-Golay kernel.
    """
    for (window, poly), coeffs in SavitzkyGolayFilter.SMOOTHING_COEFFICIENTS.items():
        expected = scipy_signal.savgol_coeffs(window, poly, deriv=0)
        np.testing.assert_allclose(
            coeffs, expected, atol=1e-12,
            err_msg=f"Lookup table mismatch for (window={window}, poly={poly})",
        )


def test_derivative_lookup_matches_scipy():
    """Same guard for the first-derivative coefficients."""
    for (window, poly), coeffs in SavitzkyGolayFilter.DERIVATIVE_COEFFICIENTS.items():
        # scipy returns the same coefficients but per-sample (use=conv).
        expected = scipy_signal.savgol_coeffs(window, poly, deriv=1, use='conv')
        # SG derivative kernels differ by a sign flip depending on the
        # convolution convention (forward vs. reverse). We accept either
        # form so long as the magnitude pattern matches.
        if not np.allclose(coeffs, expected, atol=1e-12):
            np.testing.assert_allclose(
                coeffs, -expected, atol=1e-12,
                err_msg=f"Derivative table mismatch for (window={window}, poly={poly})",
            )


def test_smooth_clean_signal_is_near_identity():
    """A noiseless polynomial signal should round-trip through the filter
    with sub-1e-6 residual (Savitzky-Golay reproduces low-order polynomials
    exactly)."""
    t = np.linspace(0, 1, 200)
    signal = 1.0 + 2.0 * t + 3.0 * t ** 2  # quadratic — within sg polyorder
    smoothed = savgol_smooth(signal, window_size=11, poly_order=3)
    assert np.allclose(signal, smoothed, atol=1e-6)


def test_smooth_reduces_high_frequency_noise():
    """Smoothing white noise added to a constant should reduce its std."""
    rng = np.random.default_rng(seed=0)
    n = 500
    noisy = np.ones(n) + rng.normal(0, 1.0, n)
    smoothed = savgol_smooth(noisy, window_size=21, poly_order=2)
    assert smoothed.std() < noisy.std() / 2


def test_smooth_input_shorter_than_window_does_not_crash():
    """A signal shorter than the requested window must either return
    the input unchanged or raise a clear error — never silently corrupt
    or hang."""
    short = np.array([1.0, 2.0, 3.0])
    try:
        out = savgol_smooth(short, window_size=11, poly_order=2)
    except (ValueError, TypeError):
        # Acceptable: the underlying scipy.signal.savgol_filter raises
        # ValueError when window > signal length.
        return
    assert len(out) == len(short)


def test_smooth_all_nan_returns_all_nan():
    """All-NaN input should produce all-NaN output, not a hang or crash.
    Researchers with bad sensor windows rely on this."""
    nan_signal = np.full(200, np.nan)
    out = savgol_smooth(nan_signal, window_size=11, poly_order=2)
    assert np.all(np.isnan(out))


def test_smooth_preserves_shape():
    n = 333  # non-multiple of window
    sig = np.sin(np.linspace(0, 4 * np.pi, n))
    out = savgol_smooth(sig, window_size=11, poly_order=2)
    assert out.shape == sig.shape


@pytest.mark.parametrize("window_size", [5, 11, 21, 51])
def test_smooth_various_window_sizes(window_size):
    """Multiple window sizes should all run cleanly on a 200-sample signal."""
    sig = np.sin(np.linspace(0, 2 * np.pi, 200))
    out = savgol_smooth(sig, window_size=window_size, poly_order=2)
    assert out.shape == sig.shape
    assert not np.any(np.isnan(out))


class TestAngularVelocity3D:
    """Tests for `compute_angular_velocity`: stable atan2-based angular
    speed from gaze direction vectors, with SG-smoothed output that
    parallels the 2D angular-velocity path."""

    def test_constant_direction_gives_zero_angular_velocity(self):
        from eyelean_analysis.filters.savitzky_golay import compute_angular_velocity
        n = 200
        dx = np.full(n, 0.0); dy = np.full(n, 0.0); dz = np.full(n, 1.0)
        v = compute_angular_velocity(dx, dy, dz, sample_rate=120.0)
        assert v.shape == (n,)
        assert np.allclose(v, 0.0, atol=1e-9)

    def test_constant_rotation_recovers_known_angular_speed(self):
        """A direction vector rotating at a constant rate should produce
        a near-constant angular velocity matching the analytic value."""
        from eyelean_analysis.filters.savitzky_golay import compute_angular_velocity
        fs = 120.0
        n = 300
        omega_dps = 30.0
        t = np.arange(n) / fs
        theta = np.radians(omega_dps * t)
        dx = np.sin(theta)
        dy = np.zeros(n)
        dz = np.cos(theta)
        v = compute_angular_velocity(dx, dy, dz, sample_rate=fs, window_size=11, poly_order=2)
        # Drop edge samples where the SG smoothing has boundary effects.
        interior = v[20:-20]
        assert np.allclose(interior, omega_dps, atol=0.5), (
            f"expected ≈ {omega_dps} deg/s, got mean={interior.mean():.3f}, "
            f"std={interior.std():.3f}"
        )

    def test_recovers_small_angle_rotations(self):
        """At HMD sample rates, inter-sample angles are small and
        require a numerically-stable angle-between-vectors formulation
        to recover a non-zero velocity."""
        from eyelean_analysis.filters.savitzky_golay import compute_angular_velocity
        fs = 120.0
        n = 200
        omega_dps = 0.1
        t = np.arange(n) / fs
        theta = np.radians(omega_dps * t)
        dx = np.sin(theta)
        dy = np.zeros(n)
        dz = np.cos(theta)
        v = compute_angular_velocity(dx, dy, dz, sample_rate=fs, window_size=11, poly_order=2)
        interior = v[20:-20]
        assert interior.mean() == pytest.approx(omega_dps, abs=0.05), (
            f"failed to recover small-angle velocity: got {interior.mean()}"
        )
