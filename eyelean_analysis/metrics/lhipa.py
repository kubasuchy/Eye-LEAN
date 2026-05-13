"""
Low/High Index of Pupillary Activity (LHIPA) for cognitive load measurement.

LHIPA decomposes the pupil-diameter signal into low and high frequency
detail-coefficient bands, forms the per-sample LF/HF ratio, and counts
the modulus maxima of that ratio that survive Donoho's universal
threshold — normalised by signal duration.

    cD_H = DWT detail at level j_HF = 1
    cD_L = DWT detail at level j_LF = floor(maxlevel / 2)
    cD_LH[i] = cD_L[i] / cD_H[(2^j_LF / 2^j_HF) · i]
    λ        = std(modmax(cD_LH)) · sqrt(2 · log2(N))         (Donoho)
    LHIPA    = count(|modmax(cD_LH)| > λ) / duration_seconds

LHIPA decreases with rising cognitive load (Duchowski 2020 §4).

Reference (cite this when reporting results):
    Duchowski, A. T., Krejtz, K., Wnuk, J., Sankarasubramanian, K.,
    Andersson, R., & Krejtz, I. (2020). The Low/High Index of Pupillary
    Activity. Proceedings of the 2020 CHI Conference on Human Factors
    in Computing Systems (CHI '20), Paper 282.
    https://doi.org/10.1145/3313831.3376394

Background (the original IPA paper, single-level):
    Duchowski, A. T., Krejtz, K., Krejtz, I., Biele, C., Niber, T.,
    Kiefer, P., ... & Giannopoulos, I. (2018). The Index of Pupillary
    Activity. CHI '18, Paper 282. https://doi.org/10.1145/3173574.3173856
"""

import numpy as np
from typing import Optional, Tuple, Dict, List
from dataclasses import dataclass
import warnings

try:
    import pywt
    HAS_PYWT = True
except ImportError:
    HAS_PYWT = False
    warnings.warn(
        "pywt (PyWavelets) not installed. LHIPA calculation will not be available. "
        "Install with: pip install pywt"
    )


def _modmax(x: np.ndarray) -> np.ndarray:
    """Local modulus maxima of `|x|`. Returns an array the same shape as
    `x`, zero except at samples where `|x[i]|` is greater than both of
    its immediate neighbours; at those indices the value is `x[i]`. The
    endpoints can never be local maxima under this definition (matches
    Duchowski 2018/2020 Listings).
    """
    if x.size < 3:
        return np.zeros_like(x)
    abs_x = np.abs(x)
    out = np.zeros_like(x)
    interior = (abs_x[1:-1] > abs_x[:-2]) & (abs_x[1:-1] > abs_x[2:])
    out[1:-1] = np.where(interior, x[1:-1], 0.0)
    return out


def _universal_threshold(modmax_values: np.ndarray) -> float:
    """Donoho universal threshold `σ √(2 log₂ N)` where σ is estimated
    from the modulus-maxima vector itself (paper choice; *not* MAD on
    the finest detail level). Uses the paper's `log2`, not `ln`.
    """
    survivors = modmax_values[modmax_values != 0]
    n = max(survivors.size, 1)
    sigma = float(np.std(survivors)) if survivors.size >= 2 else 0.0
    return sigma * np.sqrt(2.0 * np.log2(n)) if sigma > 0 else 0.0


def _modmax_count_per_second(coeffs: np.ndarray, duration_s: float) -> float:
    """count(|modmax(coeffs)| > universal_threshold) / duration_s. Paper §3.2."""
    mm = _modmax(coeffs)
    lam = _universal_threshold(mm)
    if lam <= 0:
        # Degenerate: no spread in the modmax vector ⇒ no survivors by
        # definition. Reporting 0 (rather than NaN) matches the paper's
        # "perfectly steady signal yields zero index" intuition.
        return 0.0
    survivors = np.sum(np.abs(mm) > lam)
    return float(survivors) / duration_s


@dataclass
class LHIPAResult:
    """Result of LHIPA calculation per Duchowski et al. 2020.

    Fields:
        lhipa:     Canonical LHIPA value (count of surviving modulus
                   maxima of the LF/HF ratio coefficient series,
                   normalised by duration in seconds). Decreases with
                   rising cognitive load.
        low_ipa:   IPA at the LF level (count of surviving modulus
                   maxima of cD_L / duration_seconds). Background field
                   preserved for diagnostic and back-compat use.
        high_ipa:  IPA at the HF level (count of surviving modulus
                   maxima of cD_H / duration_seconds). Same.
        mean_pupil, std_pupil: descriptive statistics of the input.
        n_samples, sample_rate: input shape.
        is_valid:  True if the algorithm produced a finite result.
        error_message: human-readable reason on failure.
    """
    lhipa: float
    low_ipa: float
    high_ipa: float
    mean_pupil: float
    std_pupil: float
    n_samples: int
    sample_rate: float
    is_valid: bool
    error_message: Optional[str] = None


class LHIPACalculator:
    """
    Calculator for the canonical Low/High Index of Pupillary Activity
    (LHIPA) of Duchowski et al. (2020).

    Implements the paper's Listing 1: discrete wavelet decomposition with
    Symlets-16, paired detail-coefficient bands at j_HF = 1 and
    j_LF = floor(maxlevel / 2), per-sample LF/HF coefficient
    ratio, modulus maxima, Donoho universal threshold, and count of
    survivors normalised by duration. **Lower LHIPA = higher cognitive
    load** (paper §4).

    Example:
        >>> calculator = LHIPACalculator()
        >>> result = calculator.calculate(pupil_diameter, sample_rate=120)
        >>> print(f"LHIPA = {result.lhipa:.3f} survivals/s")
    """

    def __init__(self,
                 wavelet: str = 'sym16',
                 j_high: int = 1,
                 j_low: Optional[int] = None,
                 min_duration: float = 5.0):
        """
        Initialize the LHIPA calculator.

        Args:
            wavelet: Wavelet basis. Paper uses 'sym16'. Override only
                     if you have a specific reason; results are not
                     directly comparable to the paper's numbers under
                     a different basis.
            j_high:  DWT detail level for the HF band. Paper sets j_HF=1.
            j_low:   DWT detail level for the LF band. Paper sets
                     j_LF = floor(maxlevel / 2). When None (default),
                     auto-derived per signal at calculation time using
                     `dwt_max_level`.
            min_duration: Minimum signal duration in seconds for valid
                     calculation (need enough cycles at the LF band).
        """
        self.wavelet = wavelet
        self.j_high = int(j_high)
        self.j_low = None if j_low is None else int(j_low)
        self.min_duration = float(min_duration)

    def calculate(self,
                  pupil_data: np.ndarray,
                  sample_rate: float,
                  timestamps: Optional[np.ndarray] = None) -> LHIPAResult:
        """
        Calculate LHIPA from pupil diameter data.

        Args:
            pupil_data: Array of pupil diameter values (mm).
            sample_rate: Sampling rate in Hz.
            timestamps: Optional timestamps (used to verify sample rate).

        Returns:
            LHIPAResult with LHIPA value and diagnostics.
        """
        if not HAS_PYWT:
            return LHIPAResult(
                lhipa=np.nan,
                low_ipa=np.nan,
                high_ipa=np.nan,
                mean_pupil=np.nan,
                std_pupil=np.nan,
                n_samples=0,
                sample_rate=sample_rate,
                is_valid=False,
                error_message="PyWavelets (pywt) not installed",
            )

        pupil_data = np.asarray(pupil_data, dtype=np.float64)

        # Handle NaN values
        valid_mask = ~np.isnan(pupil_data)
        if not np.any(valid_mask):
            return LHIPAResult(
                lhipa=np.nan,
                low_ipa=np.nan,
                high_ipa=np.nan,
                mean_pupil=np.nan,
                std_pupil=np.nan,
                n_samples=0,
                sample_rate=sample_rate,
                is_valid=False,
                error_message="No valid pupil samples",
            )

        # Interpolate NaN values for wavelet analysis
        pupil_clean = self._interpolate_nans(pupil_data)

        # Check minimum duration
        duration = len(pupil_clean) / sample_rate
        if duration < self.min_duration:
            return LHIPAResult(
                lhipa=np.nan,
                low_ipa=np.nan,
                high_ipa=np.nan,
                mean_pupil=np.nanmean(pupil_data),
                std_pupil=np.nanstd(pupil_data),
                n_samples=len(pupil_data),
                sample_rate=sample_rate,
                is_valid=False,
                error_message=f"Duration too short ({duration:.2f}s < {self.min_duration}s)",
            )

        # Canonical LHIPA per Duchowski et al. 2020 Listing 1.
        try:
            lhipa, low_ipa, high_ipa = self._compute_lhipa(pupil_clean, sample_rate)
        except Exception as e:
            return LHIPAResult(
                lhipa=np.nan,
                low_ipa=np.nan,
                high_ipa=np.nan,
                mean_pupil=np.nanmean(pupil_data),
                std_pupil=np.nanstd(pupil_data),
                n_samples=len(pupil_data),
                sample_rate=sample_rate,
                is_valid=False,
                error_message=f"Wavelet decomposition failed: {str(e)}",
            )

        return LHIPAResult(
            lhipa=lhipa,
            low_ipa=low_ipa,
            high_ipa=high_ipa,
            mean_pupil=np.nanmean(pupil_data),
            std_pupil=np.nanstd(pupil_data),
            n_samples=len(pupil_data),
            sample_rate=sample_rate,
            is_valid=True,
        )

    def _compute_lhipa(self, data: np.ndarray, sample_rate: float) -> Tuple[float, float, float]:
        """Canonical LHIPA per Duchowski et al. 2020, Listing 1.

        Returns:
            (lhipa, low_ipa, high_ipa):
              lhipa    — count of surviving modulus maxima of the LF/HF
                         coefficient ratio per second.
              low_ipa  — count of surviving modulus maxima of cD_L per
                         second (single-band IPA at the LF level).
              high_ipa — same for cD_H.

        Sign convention: lhipa **decreases** with rising cognitive load
        (paper §4). The two single-band IPAs typically rise with HF
        oscillation and fall with smoothing, but their absolute
        comparison across recordings depends on duration and basis.
        """
        wavelet = pywt.Wavelet(self.wavelet)
        max_level = pywt.dwt_max_level(len(data), wavelet.dec_len)
        # Need at least the HF level + one more decomposition step for
        # an LF level to exist. dwt_max_level == 1 means the signal is
        # too short for a meaningful LF band.
        if max_level < 2:
            return float('nan'), float('nan'), float('nan')

        j_high = int(self.j_high)
        # Paper Listing 1: `lof = int(maxlevel / 2)`. Capped at
        # max_level so we don't ask for a deeper decomposition than the
        # signal supports, and forced strictly above j_high so the LF
        # band is genuinely lower-frequency than the HF band.
        if self.j_low is None:
            j_low = max(j_high + 1, int(max_level // 2))
            j_low = min(j_low, max_level)
        else:
            j_low = int(self.j_low)
        if j_low <= j_high:
            j_low = j_high + 1

        # Single-level downcoef extraction at each band, then renormalize
        # by sqrt(2^level) per Listing 1.
        cD_h = pywt.downcoef('d', data, self.wavelet, mode='per', level=j_high)
        cD_l = pywt.downcoef('d', data, self.wavelet, mode='per', level=j_low)
        cD_h = cD_h / np.sqrt(2.0 ** j_high)
        cD_l = cD_l / np.sqrt(2.0 ** j_low)

        # Per-sample LF/HF ratio. cD_l is shorter than cD_h by a factor
        # of 2^(j_low - j_high); index cD_h with that stride so each
        # cD_l[i] is matched with the contemporaneous cD_h[stride · i].
        stride = int(round(2.0 ** (j_low - j_high)))
        if stride <= 0:
            return float('nan'), float('nan'), float('nan')
        n_lh = len(cD_l)
        max_h_index = stride * (n_lh - 1)
        if max_h_index >= len(cD_h):
            n_lh = len(cD_h) // stride
            cD_l = cD_l[:n_lh]
        # Vectorised cD_LH[i] = cD_L[i] / cD_H[stride · i] per Listing 1.
        # The paper does not guard against small denominators — large
        # ratio magnitudes when cD_H is small are the very signal LHIPA
        # is sensitive to. We only mask exact zeros (which produce inf)
        # so the modmax/threshold step has finite inputs.
        h_indexed = cD_h[np.arange(n_lh) * stride]
        with np.errstate(divide='ignore', invalid='ignore'):
            cD_lh = cD_l / np.where(h_indexed == 0.0, np.nan, h_indexed)
        cD_lh = cD_lh[np.isfinite(cD_lh)]
        if cD_lh.size < 4:
            return float('nan'), float('nan'), float('nan')

        # Modulus maxima + Donoho universal threshold + count per second.
        duration_s = len(data) / sample_rate
        if duration_s <= 0:
            return float('nan'), float('nan'), float('nan')

        lhipa    = _modmax_count_per_second(cD_lh, duration_s)
        low_ipa  = _modmax_count_per_second(cD_l,  duration_s)
        high_ipa = _modmax_count_per_second(cD_h,  duration_s)
        return lhipa, low_ipa, high_ipa

    def _interpolate_nans(self, data: np.ndarray) -> np.ndarray:
        """Interpolate NaN values using linear interpolation."""
        clean_data = data.copy()
        nan_mask = np.isnan(clean_data)

        if not np.any(nan_mask):
            return clean_data

        # Linear interpolation
        indices = np.arange(len(clean_data))
        valid_indices = indices[~nan_mask]
        valid_values = clean_data[~nan_mask]

        if len(valid_indices) < 2:
            # Not enough valid points, fill with mean
            clean_data[nan_mask] = np.nanmean(data) if np.any(~nan_mask) else 0.0
        else:
            clean_data[nan_mask] = np.interp(indices[nan_mask], valid_indices, valid_values)

        return clean_data

    def calculate_windowed(self,
                           pupil_data: np.ndarray,
                           sample_rate: float,
                           window_duration: float = 10.0,
                           step_size: float = 5.0) -> List[LHIPAResult]:
        """
        Calculate LHIPA over sliding windows.

        Args:
            pupil_data: Array of pupil diameter values.
            sample_rate: Sampling rate in Hz.
            window_duration: Window size in seconds.
            step_size: Step between windows in seconds.

        Returns:
            List of LHIPAResult for each window.
        """
        pupil_data = np.asarray(pupil_data)
        n_samples = len(pupil_data)
        window_samples = int(window_duration * sample_rate)
        step_samples = int(step_size * sample_rate)

        results = []
        start = 0

        while start + window_samples <= n_samples:
            window = pupil_data[start:start + window_samples]
            result = self.calculate(window, sample_rate)
            results.append(result)
            start += step_samples

        return results


def calculate_lhipa(pupil_data: np.ndarray,
                    sample_rate: float,
                    wavelet: str = 'sym16') -> LHIPAResult:
    """
    Convenience function to calculate LHIPA.

    Args:
        pupil_data: Array of pupil diameter values.
        sample_rate: Sampling rate in Hz.
        wavelet: Wavelet type for decomposition.

    Returns:
        LHIPAResult with LHIPA value and diagnostics.

    Example:
        >>> result = calculate_lhipa(pupil_diameter, sample_rate=120)
        >>> if result.is_valid:
        ...     print(f"Cognitive load index: {result.lhipa:.3f}")
    """
    calculator = LHIPACalculator(wavelet=wavelet)
    return calculator.calculate(pupil_data, sample_rate)


def lhipa_timeseries(pupil_data: np.ndarray,
                     sample_rate: float,
                     window_duration: float = 10.0,
                     step_size: float = 5.0) -> Tuple[np.ndarray, np.ndarray]:
    """
    Compute LHIPA time series over sliding windows.

    Args:
        pupil_data: Array of pupil diameter values.
        sample_rate: Sampling rate in Hz.
        window_duration: Window size in seconds.
        step_size: Step between windows in seconds.

    Returns:
        Tuple of (window_centers_seconds, lhipa_values).
    """
    calculator = LHIPACalculator()
    results = calculator.calculate_windowed(
        pupil_data, sample_rate,
        window_duration=window_duration,
        step_size=step_size,
    )

    if not results:
        return np.array([]), np.array([])

    # Calculate window centers
    n_windows = len(results)
    window_samples = int(window_duration * sample_rate)
    step_samples = int(step_size * sample_rate)

    centers = []
    values = []

    for i, result in enumerate(results):
        start_sample = i * step_samples
        center_sample = start_sample + window_samples // 2
        centers.append(center_sample / sample_rate)
        values.append(result.lhipa if result.is_valid else np.nan)

    return np.array(centers), np.array(values)


def combine_pupil_eyes(left_pupil: np.ndarray,
                       right_pupil: np.ndarray,
                       method: str = 'average') -> np.ndarray:
    """
    Combine left and right pupil data for LHIPA calculation.

    Args:
        left_pupil: Left eye pupil diameter.
        right_pupil: Right eye pupil diameter.
        method: Combination method ('average', 'left', 'right', 'valid').

    Returns:
        Combined pupil diameter array.
    """
    left_pupil = np.asarray(left_pupil)
    right_pupil = np.asarray(right_pupil)

    if method == 'left':
        return left_pupil

    elif method == 'right':
        return right_pupil

    elif method == 'average':
        return (left_pupil + right_pupil) / 2

    elif method == 'valid':
        # Use whichever eye has valid data
        combined = np.empty_like(left_pupil)
        left_valid = ~np.isnan(left_pupil)
        right_valid = ~np.isnan(right_pupil)
        both_valid = left_valid & right_valid

        combined[both_valid] = (left_pupil[both_valid] + right_pupil[both_valid]) / 2
        combined[left_valid & ~right_valid] = left_pupil[left_valid & ~right_valid]
        combined[right_valid & ~left_valid] = right_pupil[right_valid & ~left_valid]
        combined[~left_valid & ~right_valid] = np.nan

        return combined

    else:
        raise ValueError(f"Unknown method: {method}")


def calculate_pupil_metrics(pupil_data: np.ndarray,
                            sample_rate: float) -> Dict:
    """
    Calculate comprehensive pupil metrics including LHIPA.

    Args:
        pupil_data: Array of pupil diameter values.
        sample_rate: Sampling rate in Hz.

    Returns:
        Dictionary with pupil metrics.
    """
    pupil_data = np.asarray(pupil_data)
    valid_data = pupil_data[~np.isnan(pupil_data)]

    metrics = {
        'mean': np.mean(valid_data) if len(valid_data) > 0 else np.nan,
        'std': np.std(valid_data) if len(valid_data) > 0 else np.nan,
        'min': np.min(valid_data) if len(valid_data) > 0 else np.nan,
        'max': np.max(valid_data) if len(valid_data) > 0 else np.nan,
        'range': np.ptp(valid_data) if len(valid_data) > 0 else np.nan,
        'valid_percent': (len(valid_data) / len(pupil_data) * 100) if len(pupil_data) > 0 else 0,
    }

    # Add LHIPA if available
    if HAS_PYWT:
        lhipa_result = calculate_lhipa(pupil_data, sample_rate)
        metrics['lhipa'] = lhipa_result.lhipa
        metrics['low_ipa'] = lhipa_result.low_ipa
        metrics['high_ipa'] = lhipa_result.high_ipa
        metrics['lhipa_valid'] = lhipa_result.is_valid
    else:
        metrics['lhipa'] = np.nan
        metrics['lhipa_valid'] = False

    return metrics
