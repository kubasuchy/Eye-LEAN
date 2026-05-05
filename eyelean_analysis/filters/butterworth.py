"""
Butterworth low-pass filter for eye tracking signal smoothing.

Based on Butterworth (1930) filter design, implemented using scipy.signal.

Reference:
    Butterworth, S. (1930). On the theory of filter amplifiers.
    Wireless Engineer, 7(6), 536-541.
"""

import numpy as np
from scipy import signal
from typing import Optional, Tuple, Union
import warnings


class ButterworthFilter:
    """
    Butterworth low-pass filter for smoothing eye tracking data.

    The Butterworth filter provides maximally flat frequency response in the
    passband, making it excellent for smoothing noisy signals while preserving
    low-frequency content.

    Example:
        >>> filter = ButterworthFilter(cutoff=10.0, sample_rate=120.0, order=2)
        >>> smoothed = filter.apply(noisy_signal)
    """

    def __init__(self,
                 cutoff: float,
                 sample_rate: float,
                 order: int = 2,
                 filter_type: str = 'lowpass'):
        """
        Initialize the Butterworth filter.

        Args:
            cutoff: Cutoff frequency in Hz.
            sample_rate: Sampling rate in Hz.
            order: Filter order (1-4 recommended). Higher = sharper cutoff.
            filter_type: Type of filter ('lowpass', 'highpass', 'bandpass', 'bandstop').
        """
        self.cutoff = cutoff
        self.sample_rate = sample_rate
        self.order = order
        self.filter_type = filter_type

        # Validate parameters
        self._validate_parameters()

        # Compute filter coefficients
        self._compute_coefficients()

    def _validate_parameters(self):
        """Validate filter parameters."""
        if self.sample_rate <= 0:
            raise ValueError("Sample rate must be positive")

        nyquist = self.sample_rate / 2

        if self.cutoff <= 0:
            raise ValueError("Cutoff frequency must be positive")

        if self.cutoff >= nyquist:
            warnings.warn(
                f"Cutoff ({self.cutoff} Hz) exceeds Nyquist frequency ({nyquist} Hz). "
                f"Clamping to 0.99 * Nyquist."
            )
            self.cutoff = 0.99 * nyquist

        if self.order < 1:
            raise ValueError("Filter order must be at least 1")

        if self.order > 10:
            warnings.warn(
                f"High filter order ({self.order}) may cause numerical instability"
            )

    def _compute_coefficients(self):
        """Compute filter coefficients using scipy."""
        nyquist = self.sample_rate / 2
        normalized_cutoff = self.cutoff / nyquist

        # Design the filter
        self.b, self.a = signal.butter(
            self.order,
            normalized_cutoff,
            btype=self.filter_type,
            analog=False,
            output='ba'
        )

        # Also compute second-order sections for numerical stability
        self.sos = signal.butter(
            self.order,
            normalized_cutoff,
            btype=self.filter_type,
            analog=False,
            output='sos'
        )

    def apply(self,
              data: np.ndarray,
              axis: int = -1,
              zero_phase: bool = True) -> np.ndarray:
        """
        Apply the filter to data.

        Args:
            data: Input signal (1D or 2D array).
            axis: Axis along which to filter (for 2D data).
            zero_phase: If True, use forward-backward filtering for zero phase delay.
                       If False, use causal filtering (introduces phase delay).

        Returns:
            Filtered signal with same shape as input.
        """
        data = np.asarray(data, dtype=np.float64)

        if data.size == 0:
            return data

        # Handle NaN values
        if np.any(np.isnan(data)):
            return self._apply_with_nan_handling(data, axis, zero_phase)

        # Apply filter
        if zero_phase:
            # Forward-backward filtering (zero phase delay)
            return signal.sosfiltfilt(self.sos, data, axis=axis)
        else:
            # Causal filtering (has phase delay but works for real-time)
            return signal.sosfilt(self.sos, data, axis=axis)

    def _apply_with_nan_handling(self,
                                  data: np.ndarray,
                                  axis: int,
                                  zero_phase: bool) -> np.ndarray:
        """
        Apply filter while handling NaN values.

        Interpolates over NaN gaps, filters, then restores NaN positions.
        """
        result = data.copy()

        if data.ndim == 1:
            # 1D case
            valid_mask = ~np.isnan(data)
            if valid_mask.sum() < 4:  # Need minimum points for filtering
                return result

            # Interpolate NaN values
            indices = np.arange(len(data))
            result = np.interp(indices, indices[valid_mask], data[valid_mask])

            # Apply filter
            if zero_phase:
                result = signal.sosfiltfilt(self.sos, result)
            else:
                result = signal.sosfilt(self.sos, result)

            # Restore NaN positions
            result[~valid_mask] = np.nan

        else:
            # Multi-dimensional case - process along specified axis
            result = np.apply_along_axis(
                lambda x: self._apply_with_nan_handling(x, -1, zero_phase),
                axis,
                data
            )

        return result

    def apply_realtime(self,
                       sample: Union[float, np.ndarray],
                       state: Optional[np.ndarray] = None) -> Tuple[np.ndarray, np.ndarray]:
        """
        Apply filter to a single sample (for real-time processing).

        Args:
            sample: Single sample or vector of values.
            state: Filter state from previous call (None for first sample).

        Returns:
            Tuple of (filtered_sample, new_state).

        Example:
            >>> filter = ButterworthFilter(cutoff=10, sample_rate=120)
            >>> state = None
            >>> for sample in data_stream:
            ...     filtered, state = filter.apply_realtime(sample, state)
        """
        sample = np.atleast_1d(np.asarray(sample, dtype=np.float64))

        if state is None:
            # Initialize state (using second-order sections)
            n_sections = self.sos.shape[0]
            state = np.zeros((n_sections, 2, sample.shape[0] if sample.ndim > 0 else 1))

        # Apply filter using sosfilt with state
        filtered, state = signal.sosfilt(
            self.sos,
            sample.reshape(1, -1),
            axis=0,
            zi=state
        )

        return filtered.flatten(), state

    def frequency_response(self,
                           n_points: int = 512) -> Tuple[np.ndarray, np.ndarray]:
        """
        Compute the frequency response of the filter.

        Args:
            n_points: Number of frequency points to compute.

        Returns:
            Tuple of (frequencies in Hz, magnitude in dB).
        """
        w, h = signal.freqz(self.b, self.a, worN=n_points)
        frequencies = w * self.sample_rate / (2 * np.pi)
        magnitude_db = 20 * np.log10(np.abs(h) + 1e-10)

        return frequencies, magnitude_db

    def reset(self):
        """Reset filter state (recompute coefficients)."""
        self._compute_coefficients()

    def __repr__(self) -> str:
        return (
            f"ButterworthFilter(cutoff={self.cutoff}, "
            f"sample_rate={self.sample_rate}, order={self.order})"
        )


def butterworth_filter(data: Union[np.ndarray, list, 'pd.Series'],
                       cutoff: float,
                       sample_rate: float,
                       order: int = 2,
                       zero_phase: bool = True) -> np.ndarray:
    """
    Apply Butterworth low-pass filter to smooth noisy signals.

    Args:
        data: Input signal. Can be a numpy array, list, or pandas Series.
              NaN values are handled automatically via interpolation.
        cutoff: Cutoff frequency in Hz (signals above this frequency are attenuated).
        sample_rate: Sampling rate of the data in Hz.
        order: Filter order (default 2). Higher = sharper cutoff but may introduce ringing.
        zero_phase: If True (default), use forward-backward filtering for zero phase delay.
                   Set to False for real-time/causal filtering.

    Returns:
        Filtered signal as numpy array (same length as input).

    Example:
        >>> # From numpy array
        >>> smoothed = butterworth_filter(noisy_data, cutoff=10, sample_rate=120)
        >>>
        >>> # From pandas Series (e.g., from EyeLeanData)
        >>> pupil = data.df['left_pupil_diameter']
        >>> smoothed = butterworth_filter(pupil, cutoff=4.0, sample_rate=90)
    """
    filt = ButterworthFilter(cutoff, sample_rate, order)
    return filt.apply(data, zero_phase=zero_phase)


def estimate_cutoff_from_data(data: np.ndarray,
                              sample_rate: float,
                              noise_percentile: float = 95.0) -> float:
    """
    Estimate appropriate cutoff frequency based on signal characteristics.

    Uses spectral analysis to find a cutoff that preserves signal while
    removing high-frequency noise.

    Args:
        data: Input signal.
        sample_rate: Sampling rate in Hz.
        noise_percentile: Percentile of frequency content to preserve.

    Returns:
        Recommended cutoff frequency in Hz.
    """
    data = np.asarray(data)
    data = data[~np.isnan(data)]

    if len(data) < 10:
        return sample_rate / 4  # Default to Nyquist/2

    # Compute power spectrum
    freqs = np.fft.rfftfreq(len(data), 1/sample_rate)
    spectrum = np.abs(np.fft.rfft(data))

    # Find frequency containing noise_percentile of total power
    cumsum = np.cumsum(spectrum**2)
    total_power = cumsum[-1]
    threshold = (noise_percentile / 100) * total_power

    cutoff_idx = np.searchsorted(cumsum, threshold)
    cutoff = freqs[min(cutoff_idx, len(freqs)-1)]

    # Clamp to reasonable range
    nyquist = sample_rate / 2
    cutoff = max(1.0, min(cutoff, nyquist * 0.8))

    return cutoff
