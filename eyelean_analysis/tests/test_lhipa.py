"""LHIPA on synthetic pupil signals — Duchowski 2020 semantics.

Locks in the Listing 1 implementation: count of surviving modulus maxima
of the LF/HF DWT-coefficient ratio, normalised by duration in seconds.
LHIPA decreases with rising cognitive load.
"""

import numpy as np
import pytest

pywt = pytest.importorskip("pywt", reason="LHIPA requires PyWavelets")

from eyelean_analysis.metrics.lhipa import (  # noqa: E402
    LHIPACalculator,
    LHIPAResult,
    calculate_lhipa,
)


SAMPLE_RATE = 120.0
DURATION = 30.0


def _t():
    return np.arange(0, DURATION, 1 / SAMPLE_RATE)


def test_returns_canonical_dataclass():
    pupil = 4.0 + 0.05 * np.sin(2 * np.pi * 1.0 * _t())
    result = calculate_lhipa(pupil, sample_rate=SAMPLE_RATE)
    assert isinstance(result, LHIPAResult)
    assert result.is_valid
    assert np.isfinite(result.lhipa)
    # LHIPA is a count per second, so non-negative.
    assert result.lhipa >= 0.0


def test_constant_pupil_yields_zero_lhipa():
    """A perfectly steady pupil produces no surviving modulus maxima
    in any band — LHIPA = 0, low_ipa = 0, high_ipa = 0."""
    pupil = np.full(int(SAMPLE_RATE * DURATION), 4.0)
    result = calculate_lhipa(pupil, sample_rate=SAMPLE_RATE)
    assert result.is_valid
    assert result.lhipa == pytest.approx(0.0, abs=1e-9)
    assert result.low_ipa == pytest.approx(0.0, abs=1e-9)
    assert result.high_ipa == pytest.approx(0.0, abs=1e-9)


def test_per_band_ipa_localizes_signal_to_correct_dwt_level():
    """When we explicitly pin j_high/j_low to known DWT levels and feed
    a sinusoid centred in each level's band, the corresponding per-band
    IPA value should rise above the off-band one. Uses explicit
    j_high/j_low so the test is independent of how `n` maps to
    `dwt_max_level` (the canonical formula `j_low = floor(maxlevel/2)`
    only puts j_low in the realistic-pupil-frequency range for very
    long recordings)."""
    fs = 120.0
    t  = np.arange(0, 30.0, 1 / fs)

    # At fs=120 Hz, sym16 DWT level 1 captures roughly [30, 60] Hz, level
    # 3 roughly [7.5, 15] Hz. Pin to those levels and exercise each.
    calc = LHIPACalculator(j_high=1, j_low=3)

    hf_signal = 4.0 + 0.5 * np.sin(2 * np.pi * 40.0 * t)   # in HF band
    hf_result = calc.calculate(hf_signal, sample_rate=fs)
    assert hf_result.is_valid
    assert hf_result.high_ipa > hf_result.low_ipa

    lf_signal = 4.0 + 0.5 * np.sin(2 * np.pi * 10.0 * t)   # in LF band
    lf_result = calc.calculate(lf_signal, sample_rate=fs)
    assert lf_result.is_valid
    assert lf_result.low_ipa  > lf_result.high_ipa


def test_lhipa_distinguishes_signals_with_different_hf_lf_balance():
    """LHIPA is sensitive to the HF/LF balance of the signal: two
    signals with materially different LF/HF coefficient ratios should
    NOT produce the same LHIPA. (The directional 'lower LHIPA = higher
    cognitive load' claim from Duchowski 2020 §4 is an empirical
    finding on human n-back data and is validated against real
    recordings, not synthetic sinusoids — universal-threshold
    sensitivity to modmax variance makes the directional sign on clean
    synthetics non-monotonic. We test only the discriminability
    invariant here.)"""
    t = _t()
    lf_dominated = 4.0 + 0.5 * np.sin(2 * np.pi * 0.1 * t)
    hf_dominated = 4.0 + 0.5 * np.sin(2 * np.pi * 5.0  * t)
    a = calculate_lhipa(lf_dominated, sample_rate=SAMPLE_RATE)
    b = calculate_lhipa(hf_dominated, sample_rate=SAMPLE_RATE)
    assert a.is_valid and b.is_valid
    assert abs(a.lhipa - b.lhipa) > 0.1, (
        f"LHIPA failed to discriminate LF-dominated from HF-dominated signal: "
        f"both gave {a.lhipa} ≈ {b.lhipa}"
    )


def test_too_short_signal_returns_invalid():
    pupil = np.full(int(SAMPLE_RATE * 1.0), 4.0)
    result = LHIPACalculator(min_duration=5.0).calculate(pupil, sample_rate=SAMPLE_RATE)
    assert not result.is_valid
