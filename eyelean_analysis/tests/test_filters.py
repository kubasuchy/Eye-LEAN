"""Savitzky-Golay filter edge cases."""

import numpy as np
import pytest

from eyelean_analysis.filters.savitzky_golay import savgol_smooth


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
