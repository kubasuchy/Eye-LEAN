"""
RIPA2 — Real-time Index of Pupillary Activity (v2).

Python parity layer for the Unity-side ``RIPA2Analyzer``. The same SG
derivative filter coefficients, evaluation point, clip range, and moving-
average smoother are produced here, so a researcher can re-derive the
``LiveLoadIndex`` column from the recorded pupil stream byte-for-byte
(within IEEE-754 single-precision epsilon).

Reference:
    Jayawardena, G., Jayawardana, Y., & Gwizdka, J. (2025).
    Measuring Mental Effort in Real Time Using Pupillometry.
    Journal of Eye Movement Research, 18(6), 70.
    https://doi.org/10.3390/jemr18060070

This module deliberately uses a numpy-only implementation of the SG
derivative coefficients (matching the Unity Vandermonde-fit derivation)
rather than ``scipy.signal.savgol_coeffs`` so the byte-for-byte parity
test does not depend on a specific scipy version.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

import numpy as np


__all__ = [
    "RIPA2Result",
    "schaefer_half_width",
    "sg_first_derivative_coeffs",
    "calculate_ripa2",
    "ripa2_timeseries",
]


CLIP_MIN = 0.0
CLIP_MAX = 1.5


@dataclass
class RIPA2Result:
    """Output of :func:`calculate_ripa2`."""

    ripa2_raw: np.ndarray
    """Per-sample raw RIPA2 values, clipped to [0, 1.5]. NaN where the
    VLF window cannot be evaluated (warmup tail / end of stream)."""

    ripa2_smoothed: np.ndarray
    """Trailing moving-average over ``smoothing_size`` raw samples."""

    sample_rate: float
    vlf_half_width: int
    vlf_poly_order: int
    lf_half_width: int
    lf_poly_order: int
    smoothing_size: int


def schaefer_half_width(sample_rate: float, target_cutoff_hz: float, poly_order: int) -> int:
    """Schäfer 2011 normalized-cutoff approximation (his eq. 18)::

        fc ≈ (N + 1) / (3.2 M − 4.6)

    Returns ``M`` (half-width) given ``poly_order = N`` and a target cutoff
    expressed in Hz. Clamped to ``M ≥ N + 1`` so the SG fit is well-posed.
    """
    if sample_rate <= 0:
        raise ValueError("sample_rate must be > 0")
    if poly_order < 1:
        raise ValueError("poly_order must be >= 1 for a derivative filter")
    nyquist = sample_rate / 2.0
    if not (0 < target_cutoff_hz < nyquist):
        raise ValueError("target_cutoff_hz must lie in (0, sample_rate/2)")
    fc = target_cutoff_hz / nyquist
    M = ((poly_order + 1) / fc + 4.6) / 3.2
    return max(int(round(M)), poly_order + 1)


def sg_first_derivative_coeffs(half_width: int, poly_order: int) -> np.ndarray:
    """First-derivative SG coefficients via the same normal-equations path
    used by the Unity ``SavitzkyGolayDerivative``.

    For window size ``W = 2 M + 1`` and polynomial order ``N``:

    1. Build moments ``A[j, k] = sum_{i=-M..M} i^(j+k)`` for ``j, k = 0..N``
    2. Solve ``A u = e_1`` (the unit vector picking row 1, the t¹ coef)
    3. ``h[i] = sum_j (i - M)^j u[j]``

    The result satisfies ``sum_i h[i] * y[i+offset] = polynomial-fit
    derivative at the window center``.
    """
    if half_width < 1:
        raise ValueError("half_width must be >= 1")
    if poly_order < 1:
        raise ValueError("poly_order must be >= 1 for a derivative filter")
    if poly_order > 2 * half_width:
        raise ValueError("poly_order must be <= 2 * half_width")

    M = int(half_width)
    N = int(poly_order)
    K = N + 1

    # Even/odd moments under the symmetric sum [-M, M]. Odd powers vanish.
    moments = np.zeros(2 * N + 1, dtype=np.float64)
    indices = np.arange(-M, M + 1, dtype=np.float64)
    for p in range(2 * N + 1):
        if p % 2 == 1:
            continue
        moments[p] = float(np.sum(indices ** p))
    A = np.empty((K, K), dtype=np.float64)
    for j in range(K):
        for k in range(K):
            A[j, k] = moments[j + k]
    rhs = np.zeros(K, dtype=np.float64)
    rhs[1] = 1.0
    u = np.linalg.solve(A, rhs)

    h = np.zeros(2 * M + 1, dtype=np.float64)
    for i in range(2 * M + 1):
        t = float(i - M)
        s = 0.0
        tk = 1.0
        for j in range(K):
            s += tk * u[j]
            tk *= t
        h[i] = s
    return h


def calculate_ripa2(
    pupil: np.ndarray,
    sample_rate: float,
    vlf_cutoff_hz: float = 0.29,
    vlf_poly_order: int = 2,
    lf_cutoff_hz: float = 4.0,
    lf_poly_order: int = 4,
    vlf_half_width: Optional[int] = None,
    lf_half_width: Optional[int] = None,
    smoothing_seconds: float = 1.5,
) -> RIPA2Result:
    """Compute the RIPA2 metric over a pupil-diameter time series.

    The output is per-sample. Indices for which the VLF window cannot be
    evaluated (the first ``2 * M_VLF`` samples) are filled with NaN. The
    smoothed series uses a trailing moving-average over
    ``ceil(smoothing_seconds * sample_rate)`` samples.

    Parameters
    ----------
    pupil
        1-D pupil diameter (mm). Non-finite samples are tolerated and
        propagate as NaN through the per-sample SG convolution.
    sample_rate
        Sample rate in Hz.
    vlf_cutoff_hz, lf_cutoff_hz
        Target cutoff frequencies for the VLF and LF derivative bands.
        Defaults follow the paper (0.29 Hz / 4 Hz).
    vlf_poly_order, lf_poly_order
        SG polynomial orders. Paper defaults 2 / 4.
    vlf_half_width, lf_half_width
        Optional explicit half-widths. When None, they are derived from
        the cutoff via Schäfer's formula.
    smoothing_seconds
        Trailing moving-average window. Paper recommends 1–2 s.
    """
    pupil = np.asarray(pupil, dtype=np.float64)
    if pupil.ndim != 1:
        raise ValueError("pupil must be 1-D")
    if vlf_half_width is None:
        vlf_half_width = schaefer_half_width(sample_rate, vlf_cutoff_hz, vlf_poly_order)
    if lf_half_width is None:
        lf_half_width = schaefer_half_width(sample_rate, lf_cutoff_hz, lf_poly_order)
    if lf_half_width > vlf_half_width:
        raise ValueError("LF half-width must be <= VLF half-width (LF window must fit inside VLF)")

    h_vlf = sg_first_derivative_coeffs(vlf_half_width, vlf_poly_order)
    h_lf = sg_first_derivative_coeffs(lf_half_width, lf_poly_order)
    n_vlf = h_vlf.size  # 2*M_VLF + 1
    n_lf = h_lf.size

    n = pupil.size
    raw = np.full(n, np.nan, dtype=np.float64)
    if n < n_vlf:
        smoothing_size = max(1, int(np.ceil(smoothing_seconds * sample_rate)))
        return RIPA2Result(
            ripa2_raw=raw,
            ripa2_smoothed=np.full(n, np.nan, dtype=np.float64),
            sample_rate=float(sample_rate),
            vlf_half_width=vlf_half_width,
            vlf_poly_order=vlf_poly_order,
            lf_half_width=lf_half_width,
            lf_poly_order=lf_poly_order,
            smoothing_size=smoothing_size,
        )

    # Slide both filters across the signal, evaluating at the same time
    # index. The valid output index `t` runs from `M_VLF` to `n - 1 -
    # M_VLF` inclusive (the paper's "buffer holds enough data" gate).
    last = n - 1 - vlf_half_width
    for t in range(vlf_half_width, last + 1):
        vlf_start = t - vlf_half_width
        vlf_window = pupil[vlf_start : vlf_start + n_vlf]
        lf_start = t - lf_half_width
        lf_window = pupil[lf_start : lf_start + n_lf]
        v = float(h_vlf @ vlf_window)
        l = float(h_lf @ lf_window)
        val = (l * l) - (v * v)
        if not np.isfinite(val):
            raw[t] = np.nan
            continue
        if val < CLIP_MIN:
            val = CLIP_MIN
        elif val > CLIP_MAX:
            val = CLIP_MAX
        raw[t] = val

    # Trailing moving average over the raw series. NaN-tolerant: the
    # window's denominator excludes NaN samples so the smoother trails in
    # cleanly without dragging NaN forever.
    smoothing_size = max(1, int(np.ceil(smoothing_seconds * sample_rate)))
    smoothed = _trailing_moving_average(raw, smoothing_size)

    return RIPA2Result(
        ripa2_raw=raw,
        ripa2_smoothed=smoothed,
        sample_rate=float(sample_rate),
        vlf_half_width=vlf_half_width,
        vlf_poly_order=vlf_poly_order,
        lf_half_width=lf_half_width,
        lf_poly_order=lf_poly_order,
        smoothing_size=smoothing_size,
    )


def _trailing_moving_average(x: np.ndarray, window: int) -> np.ndarray:
    """Trailing moving-average matching the Unity analyzer's ring-buffer
    smoother: the ``i``-th output is the mean of the last ``min(i+1, window)``
    non-NaN samples of ``x``. NaN inputs do not contribute to the sum or
    the count, so ``smoothed[t]`` is the mean of finite raw values within
    the trailing window.
    """
    n = x.size
    out = np.empty(n, dtype=np.float64)
    out[:] = np.nan
    # Cumulative finite sums + counts for O(n) sliding window.
    finite = np.isfinite(x)
    finite_x = np.where(finite, x, 0.0)
    csum = np.concatenate(([0.0], np.cumsum(finite_x)))
    ccnt = np.concatenate(([0], np.cumsum(finite.astype(np.int64))))
    for i in range(n):
        start = max(0, i + 1 - window)
        s = csum[i + 1] - csum[start]
        c = ccnt[i + 1] - ccnt[start]
        if c > 0:
            out[i] = s / c
    return out


def ripa2_timeseries(
    pupil: np.ndarray,
    sample_rate: float,
    **kwargs,
) -> np.ndarray:
    """Convenience wrapper returning just the smoothed RIPA2 array.

    Useful when plotting alongside the Unity-recorded ``LiveLoadIndex``
    column for visual parity verification.
    """
    return calculate_ripa2(pupil, sample_rate, **kwargs).ripa2_smoothed
