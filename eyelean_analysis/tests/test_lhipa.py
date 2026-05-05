"""LHIPA on synthetic pupil signals with known frequency content."""

import numpy as np
import pytest

pywt = pytest.importorskip("pywt", reason="LHIPA requires PyWavelets")

from eyelean_analysis.metrics.lhipa import LHIPACalculator  # noqa: E402


def test_constant_pupil_yields_zero_activity():
    """A perfectly steady pupil has no oscillation in any band, so both
    low_ipa and high_ipa must be (near) zero."""
    sample_rate = 120.0
    duration = 30.0
    pupil = np.full(int(sample_rate * duration), 4.0)  # 4 mm constant

    result = LHIPACalculator().calculate(pupil, sample_rate=sample_rate)
    assert result.is_valid
    assert result.low_ipa < 1e-9
    assert result.high_ipa < 1e-9


def test_high_frequency_signal_dominates_high_band():
    """A 5 Hz pupil oscillation (well above the 0.5 Hz LF/HF cutoff) should
    place far more activity in the high-frequency band than in the low one."""
    sample_rate = 120.0
    t = np.arange(0, 30.0, 1 / sample_rate)
    pupil = 4.0 + 0.5 * np.sin(2 * np.pi * 5.0 * t)  # 5 Hz oscillation

    result = LHIPACalculator().calculate(pupil, sample_rate=sample_rate)
    assert result.is_valid
    assert result.high_ipa > result.low_ipa * 5, (
        f"expected HF >> LF for a 5 Hz signal; got LF={result.low_ipa}, HF={result.high_ipa}"
    )


def test_low_frequency_signal_dominates_low_band():
    """A 0.05 Hz pupil oscillation (well below the 0.5 Hz cutoff) should
    place its activity in the low-frequency band."""
    sample_rate = 120.0
    t = np.arange(0, 60.0, 1 / sample_rate)  # need enough cycles at 0.05 Hz
    pupil = 4.0 + 0.5 * np.sin(2 * np.pi * 0.05 * t)

    result = LHIPACalculator().calculate(pupil, sample_rate=sample_rate)
    assert result.is_valid
    assert result.low_ipa > result.high_ipa, (
        f"expected LF > HF for a 0.05 Hz signal; got LF={result.low_ipa}, HF={result.high_ipa}"
    )


def test_too_short_signal_returns_invalid():
    """Signals shorter than `min_duration` (default 5 s) must be flagged
    invalid rather than producing a misleading number."""
    sample_rate = 120.0
    pupil = np.full(int(sample_rate * 1.0), 4.0)  # 1 second only

    result = LHIPACalculator(min_duration=5.0).calculate(pupil, sample_rate=sample_rate)
    assert not result.is_valid
