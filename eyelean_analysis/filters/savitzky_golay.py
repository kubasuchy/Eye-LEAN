"""
Savitzky-Golay filter for eye tracking signal smoothing and derivatives.

Excellent for preserving peaks and shape characteristics while removing noise.
Also used for computing velocity from gaze position data.

Reference:
    Savitzky, A., & Golay, M. J. E. (1964). Smoothing and Differentiation
    of Data by Simplified Least Squares Procedures. Analytical Chemistry,
    36(8), 1627-1639. https://doi.org/10.1021/ac60214a047
"""

import numpy as np
from scipy import signal
from typing import Optional, Tuple, Union
import warnings


class SavitzkyGolayFilter:
    """
    Savitzky-Golay filter for smoothing and differentiation of eye tracking data.

    The Savitzky-Golay filter performs a local polynomial regression and is
    particularly good at preserving signal features like peaks while still
    providing effective smoothing.

    Example:
        >>> filter = SavitzkyGolayFilter(window_size=9, poly_order=2)
        >>> smoothed = filter.smooth(noisy_signal)
        >>> velocity = filter.derivative(position_signal, sample_rate=120)
    """

    # Pre-computed smoothing coefficients for common configurations
    SMOOTHING_COEFFICIENTS = {
        (5, 2): np.array([-3, 12, 17, 12, -3]) / 35,
        (5, 3): np.array([-3, 12, 17, 12, -3]) / 35,  # Same as quadratic for smoothing
        (5, 4): np.array([-3, 12, 17, 12, -3]) / 35,
        (7, 2): np.array([-2, 3, 6, 7, 6, 3, -2]) / 21,
        (7, 3): np.array([-2, 3, 6, 7, 6, 3, -2]) / 21,
        (7, 4): np.array([5, -30, 75, 131, 75, -30, 5]) / 231,
        (9, 2): np.array([-21, 14, 39, 54, 59, 54, 39, 14, -21]) / 231,
        (9, 3): np.array([-21, 14, 39, 54, 59, 54, 39, 14, -21]) / 231,
        (9, 4): np.array([15, -55, 30, 135, 179, 135, 30, -55, 15]) / 429,
        (11, 2): np.array([-36, 9, 44, 69, 84, 89, 84, 69, 44, 9, -36]) / 429,
        (11, 4): np.array([18, -45, -10, 60, 120, 143, 120, 60, -10, -45, 18]) / 429,
    }

    # Pre-computed first derivative coefficients
    DERIVATIVE_COEFFICIENTS = {
        (5, 2): np.array([-2, -1, 0, 1, 2]) / 10,
        (5, 3): np.array([1, -8, 0, 8, -1]) / 12,
        (7, 2): np.array([-3, -2, -1, 0, 1, 2, 3]) / 28,
        (7, 3): np.array([22, -67, -58, 0, 58, 67, -22]) / 252,
        (9, 2): np.array([-4, -3, -2, -1, 0, 1, 2, 3, 4]) / 60,
        (9, 4): np.array([86, -142, -193, -126, 0, 126, 193, 142, -86]) / 1188,
        (11, 2): np.array([-5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5]) / 110,
    }

    def __init__(self,
                 window_size: int = 9,
                 poly_order: int = 2,
                 mode: str = 'interp'):
        """
        Initialize the Savitzky-Golay filter.

        Args:
            window_size: Size of the filter window (must be odd, >= 3).
            poly_order: Order of the polynomial fit (must be < window_size).
            mode: Edge handling mode ('interp', 'nearest', 'constant', 'wrap', 'mirror').
        """
        self.window_size = window_size
        self.poly_order = poly_order
        self.mode = mode

        # Validate and adjust parameters
        self._validate_parameters()

    def _validate_parameters(self):
        """Validate and adjust filter parameters."""
        # Ensure window size is odd
        if self.window_size % 2 == 0:
            self.window_size += 1
            warnings.warn(f"Window size must be odd. Adjusted to {self.window_size}")

        # Ensure window size is at least 3
        if self.window_size < 3:
            self.window_size = 3
            warnings.warn(f"Window size must be >= 3. Adjusted to {self.window_size}")

        # Ensure polynomial order is valid
        if self.poly_order >= self.window_size:
            self.poly_order = self.window_size - 1
            warnings.warn(
                f"Polynomial order must be < window size. Adjusted to {self.poly_order}"
            )

        if self.poly_order < 0:
            self.poly_order = 0
            warnings.warn("Polynomial order must be >= 0. Adjusted to 0")

    def smooth(self, data: np.ndarray, axis: int = -1) -> np.ndarray:
        """
        Apply smoothing filter to data.

        Args:
            data: Input signal (1D or 2D array).
            axis: Axis along which to filter (for 2D data).

        Returns:
            Smoothed signal with same shape as input.
        """
        data = np.asarray(data, dtype=np.float64)

        if data.size == 0:
            return data

        # Handle NaN values
        if np.any(np.isnan(data)):
            return self._smooth_with_nan_handling(data, axis)

        return signal.savgol_filter(
            data,
            window_length=self.window_size,
            polyorder=self.poly_order,
            deriv=0,
            mode=self.mode,
            axis=axis
        )

    def _smooth_with_nan_handling(self, data: np.ndarray, axis: int) -> np.ndarray:
        """Apply smoothing while handling NaN values."""
        result = data.copy()

        if data.ndim == 1:
            valid_mask = ~np.isnan(data)
            if valid_mask.sum() < self.window_size:
                return result  # Not enough valid data

            # Interpolate NaN values
            indices = np.arange(len(data))
            interpolated = np.interp(indices, indices[valid_mask], data[valid_mask])

            # Apply filter
            smoothed = signal.savgol_filter(
                interpolated,
                window_length=self.window_size,
                polyorder=self.poly_order,
                deriv=0,
                mode=self.mode
            )

            # Restore NaN positions
            result = smoothed.copy()
            result[~valid_mask] = np.nan

        else:
            result = np.apply_along_axis(
                lambda x: self._smooth_with_nan_handling(x, -1),
                axis,
                data
            )

        return result

    def derivative(self,
                   data: np.ndarray,
                   sample_rate: float = 1.0,
                   deriv_order: int = 1,
                   axis: int = -1) -> np.ndarray:
        """
        Compute derivative of signal using Savitzky-Golay filter.

        Args:
            data: Input signal.
            sample_rate: Sampling rate in Hz (for proper scaling).
            deriv_order: Order of derivative (1 = velocity, 2 = acceleration).
            axis: Axis along which to compute derivative.

        Returns:
            Derivative of signal (same shape as input).

        Example:
            >>> velocity = filter.derivative(position, sample_rate=120, deriv_order=1)
            >>> acceleration = filter.derivative(position, sample_rate=120, deriv_order=2)
        """
        data = np.asarray(data, dtype=np.float64)

        if data.size == 0:
            return data

        if deriv_order > self.poly_order:
            warnings.warn(
                f"Derivative order ({deriv_order}) exceeds polynomial order ({self.poly_order}). "
                f"Result may be inaccurate."
            )

        # Handle NaN values
        if np.any(np.isnan(data)):
            return self._derivative_with_nan_handling(data, sample_rate, deriv_order, axis)

        # Compute derivative
        # The delta parameter accounts for sample spacing
        delta = 1.0 / sample_rate

        derivative = signal.savgol_filter(
            data,
            window_length=self.window_size,
            polyorder=self.poly_order,
            deriv=deriv_order,
            delta=delta,
            mode=self.mode,
            axis=axis
        )

        return derivative

    def _derivative_with_nan_handling(self,
                                       data: np.ndarray,
                                       sample_rate: float,
                                       deriv_order: int,
                                       axis: int) -> np.ndarray:
        """Compute derivative while handling NaN values."""
        result = np.full_like(data, np.nan)

        if data.ndim == 1:
            valid_mask = ~np.isnan(data)
            if valid_mask.sum() < self.window_size:
                return result

            # Interpolate NaN values
            indices = np.arange(len(data))
            interpolated = np.interp(indices, indices[valid_mask], data[valid_mask])

            # Compute derivative
            delta = 1.0 / sample_rate
            derivative = signal.savgol_filter(
                interpolated,
                window_length=self.window_size,
                polyorder=self.poly_order,
                deriv=deriv_order,
                delta=delta,
                mode=self.mode
            )

            # Only keep derivatives where original data was valid
            result = derivative.copy()
            result[~valid_mask] = np.nan

        else:
            result = np.apply_along_axis(
                lambda x: self._derivative_with_nan_handling(x, sample_rate, deriv_order, -1),
                axis,
                data
            )

        return result

    def velocity(self,
                 position: np.ndarray,
                 sample_rate: float,
                 axis: int = -1) -> np.ndarray:
        """
        Compute velocity from position data.

        Convenience wrapper for first derivative.

        Args:
            position: Position signal (1D or 2D).
            sample_rate: Sampling rate in Hz.
            axis: Axis along which to compute velocity.

        Returns:
            Velocity signal (units per second).
        """
        return self.derivative(position, sample_rate, deriv_order=1, axis=axis)

    def acceleration(self,
                     position: np.ndarray,
                     sample_rate: float,
                     axis: int = -1) -> np.ndarray:
        """
        Compute acceleration from position data.

        Convenience wrapper for second derivative.

        Args:
            position: Position signal (1D or 2D).
            sample_rate: Sampling rate in Hz.
            axis: Axis along which to compute acceleration.

        Returns:
            Acceleration signal (units per second squared).
        """
        return self.derivative(position, sample_rate, deriv_order=2, axis=axis)

    def get_coefficients(self, deriv_order: int = 0) -> np.ndarray:
        """
        Get filter coefficients for the current configuration.

        Args:
            deriv_order: Order of derivative (0 = smoothing).

        Returns:
            Array of filter coefficients.
        """
        key = (self.window_size, self.poly_order)

        if deriv_order == 0:
            if key in self.SMOOTHING_COEFFICIENTS:
                return self.SMOOTHING_COEFFICIENTS[key].copy()
        elif deriv_order == 1:
            if key in self.DERIVATIVE_COEFFICIENTS:
                return self.DERIVATIVE_COEFFICIENTS[key].copy()

        # Compute coefficients using scipy
        # This creates the convolution coefficients
        return signal.savgol_coeffs(
            self.window_size,
            self.poly_order,
            deriv=deriv_order
        )

    def __repr__(self) -> str:
        return (
            f"SavitzkyGolayFilter(window_size={self.window_size}, "
            f"poly_order={self.poly_order}, mode='{self.mode}')"
        )


def savgol_smooth(data,
                  window_size: int = 9,
                  poly_order: int = 2) -> np.ndarray:
    """
    Apply Savitzky-Golay polynomial smoothing filter.

    This filter fits a polynomial to a sliding window of data points, providing
    smooth output while preserving features like peaks better than moving average.

    Args:
        data: Input signal. Can be a numpy array, list, or pandas Series.
              NaN values are handled automatically via interpolation.
        window_size: Size of filter window (must be odd integer, default 9).
                    Larger windows = more smoothing but may lose detail.
        poly_order: Polynomial order for fit (default 2). Must be less than window_size.
                   Higher order = better peak preservation but less noise reduction.

    Returns:
        Smoothed signal as numpy array (same length as input).

    Example:
        >>> # From numpy array
        >>> smoothed = savgol_smooth(noisy_data, window_size=11, poly_order=3)
        >>>
        >>> # From pandas Series (e.g., from EyeLeanData)
        >>> pupil = data.df['left_pupil_diameter']
        >>> smoothed = savgol_smooth(pupil, window_size=11)
    """
    filt = SavitzkyGolayFilter(window_size, poly_order)
    return filt.smooth(data)


def savgol_velocity(position: np.ndarray,
                    sample_rate: float,
                    window_size: int = 9,
                    poly_order: int = 2) -> np.ndarray:
    """
    Convenience function to compute velocity using Savitzky-Golay filter.

    Args:
        position: Position signal.
        sample_rate: Sampling rate in Hz.
        window_size: Size of filter window (odd integer).
        poly_order: Polynomial order for fit.

    Returns:
        Velocity signal.

    Example:
        >>> velocity = savgol_velocity(gaze_x, sample_rate=120)
    """
    filt = SavitzkyGolayFilter(window_size, poly_order)
    return filt.velocity(position, sample_rate)


def compute_gaze_velocity(gaze_x: np.ndarray,
                          gaze_y: np.ndarray,
                          sample_rate: float,
                          window_size: int = 9,
                          poly_order: int = 2) -> np.ndarray:
    """
    Compute gaze velocity magnitude from x and y components.

    Args:
        gaze_x: X component of gaze position.
        gaze_y: Y component of gaze position.
        sample_rate: Sampling rate in Hz.
        window_size: Size of filter window.
        poly_order: Polynomial order for fit.

    Returns:
        Velocity magnitude (sqrt(vx^2 + vy^2)).

    Example:
        >>> velocity = compute_gaze_velocity(gaze_x, gaze_y, sample_rate=120)
    """
    filt = SavitzkyGolayFilter(window_size, poly_order)

    vx = filt.velocity(gaze_x, sample_rate)
    vy = filt.velocity(gaze_y, sample_rate)

    return np.sqrt(vx**2 + vy**2)


def compute_angular_velocity(dir_x: np.ndarray,
                             dir_y: np.ndarray,
                             dir_z: np.ndarray,
                             sample_rate: float,
                             window_size: int = 9,
                             poly_order: int = 2) -> np.ndarray:
    """
    Compute angular velocity from gaze direction vectors.

    Uses the cross product of consecutive direction vectors to estimate
    angular velocity in degrees per second.

    Args:
        dir_x: X component of gaze direction.
        dir_y: Y component of gaze direction.
        dir_z: Z component of gaze direction.
        sample_rate: Sampling rate in Hz.
        window_size: Size of filter window for smoothing.
        poly_order: Polynomial order for fit.

    Returns:
        Angular velocity in degrees per second.
    """
    filt = SavitzkyGolayFilter(window_size, poly_order)

    # Smooth the direction vectors first
    dir_x_smooth = filt.smooth(dir_x)
    dir_y_smooth = filt.smooth(dir_y)
    dir_z_smooth = filt.smooth(dir_z)

    # Compute angular displacement between consecutive samples
    # Using dot product to find angle between vectors
    n = len(dir_x)
    angular_velocity = np.zeros(n)

    for i in range(1, n):
        v1 = np.array([dir_x_smooth[i-1], dir_y_smooth[i-1], dir_z_smooth[i-1]])
        v2 = np.array([dir_x_smooth[i], dir_y_smooth[i], dir_z_smooth[i]])

        # Normalize vectors
        v1_norm = np.linalg.norm(v1)
        v2_norm = np.linalg.norm(v2)

        if v1_norm > 0 and v2_norm > 0:
            v1 = v1 / v1_norm
            v2 = v2 / v2_norm

            # Compute angle using dot product
            dot = np.clip(np.dot(v1, v2), -1.0, 1.0)
            angle_rad = np.arccos(dot)
            angle_deg = np.degrees(angle_rad)

            # Convert to velocity (degrees per second)
            angular_velocity[i] = angle_deg * sample_rate

    # Copy first value
    angular_velocity[0] = angular_velocity[1] if n > 1 else 0

    return angular_velocity
