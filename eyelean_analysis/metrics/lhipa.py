"""
Low/High Index of Pupillary Activity (LHIPA) for cognitive load measurement.

The LHIPA uses wavelet decomposition to analyze pupil oscillations as an
indicator of cognitive load, based on the relationship between pupil
dilation and mental workload.

Reference:
    Duchowski, A. T., Krejtz, K., Krejtz, I., Biele, C., Niber, T.,
    Kiefer, P., ... & Giannopoulos, I. (2018). The Index of Pupillary
    Activity: Measuring Cognitive Load vis-à-vis Task Difficulty with
    Pupil Oscillation. Proceedings of the 2018 CHI Conference on Human
    Factors in Computing Systems (CHI '18), Paper 282.
    https://doi.org/10.1145/3173574.3173856
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


@dataclass
class LHIPAResult:
    """Result of LHIPA calculation."""
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
    Calculator for the Low/High Index of Pupillary Activity (LHIPA).

    The LHIPA analyzes pupil diameter oscillations using wavelet decomposition
    to estimate cognitive load. It separates pupil fluctuations into low and
    high frequency components.

    - Low-frequency components (< 0.5 Hz): Related to slow arousal changes
    - High-frequency components (> 0.5 Hz): Related to task-evoked responses

    The ratio of high to low frequency activity indicates cognitive load.

    Example:
        >>> calculator = LHIPACalculator()
        >>> result = calculator.calculate(pupil_diameter, sample_rate=120)
        >>> print(f"LHIPA = {result.lhipa:.3f}")
    """

    def __init__(self,
                 wavelet: str = 'sym16',
                 decomposition_level: Optional[int] = None,
                 low_freq_cutoff: float = 0.5,
                 min_duration: float = 5.0):
        """
        Initialize the LHIPA calculator.

        Args:
            wavelet: Wavelet type for decomposition (default 'sym16' as per paper).
            decomposition_level: Level of wavelet decomposition (auto-calculated if None).
            low_freq_cutoff: Frequency cutoff for low/high separation (Hz).
            min_duration: Minimum signal duration in seconds for valid calculation.
        """
        self.wavelet = wavelet
        self.decomposition_level = decomposition_level
        self.low_freq_cutoff = low_freq_cutoff
        self.min_duration = min_duration

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

        # Calculate LHIPA using wavelet decomposition
        try:
            low_ipa, high_ipa = self._wavelet_ipa(pupil_clean, sample_rate)
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

        # LHIPA = High IPA / Low IPA (or a normalized combination)
        # Higher LHIPA indicates higher cognitive load
        if low_ipa > 0:
            lhipa = high_ipa / low_ipa
        else:
            lhipa = high_ipa if high_ipa > 0 else 0.0

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

    def _wavelet_ipa(self, data: np.ndarray, sample_rate: float) -> Tuple[float, float]:
        """
        Calculate IPA using wavelet decomposition.

        The method decomposes the signal into approximation (low-frequency) and
        detail (high-frequency) coefficients, then calculates the "activity"
        in each band.
        """
        # Determine decomposition level
        if self.decomposition_level is not None:
            level = self.decomposition_level
        else:
            # Pyramid algorithm: at decomposition level L the approximation
            # coefficients capture frequencies in [0, fs/2^(L+1)). To place
            # the LF/HF band boundary near `low_freq_cutoff` we want
            #     fs / 2^(L+1) ~= low_freq_cutoff
            #     L = log2(fs / low_freq_cutoff) - 1
            # Duchowski et al. (2018, CHI '18) use L = floor(log2(fs/cutoff)),
            # which puts the boundary at cutoff/2 — one level deeper than the
            # naive derivation, slightly conservative (HF activity gets a
            # wider band than LF). We keep the paper's formula for parity with
            # the published results; adjust only via the explicit
            # `decomposition_level` constructor arg if you need a different
            # split. `dwt_max_level` clamps L to what the signal length allows.
            max_level = pywt.dwt_max_level(len(data), pywt.Wavelet(self.wavelet).dec_len)
            level = min(max_level, int(np.log2(sample_rate / self.low_freq_cutoff)))
            level = max(1, level)

        # Perform wavelet decomposition
        coeffs = pywt.wavedec(data, self.wavelet, level=level)

        # coeffs[0] = approximation coefficients (lowest frequencies)
        # coeffs[1:] = detail coefficients (high to low frequency)

        # Calculate Low IPA from approximation coefficients
        # IPA is proportional to the sum of absolute differences
        approx = coeffs[0]
        low_activity = np.sum(np.abs(np.diff(approx))) / len(approx)

        # Calculate High IPA from detail coefficients
        # Sum activity across all detail levels
        high_activity = 0.0
        for detail in coeffs[1:]:
            if len(detail) > 1:
                high_activity += np.sum(np.abs(np.diff(detail))) / len(detail)

        return low_activity, high_activity

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
