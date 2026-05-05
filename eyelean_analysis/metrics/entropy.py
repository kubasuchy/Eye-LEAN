"""
Gaze entropy metrics for analyzing eye tracking data.

Shannon entropy measures the randomness/predictability of gaze distribution.
Higher entropy indicates more dispersed gaze patterns (scanning), while
lower entropy indicates more focused attention.

Reference:
    Shannon, C. E. (1948). A Mathematical Theory of Communication.
    Bell System Technical Journal, 27(3), 379-423.
    https://doi.org/10.1002/j.1538-7305.1948.tb01338.x
"""

import numpy as np
from typing import Optional, Tuple, Dict, List, Union
from dataclasses import dataclass


@dataclass
class EntropyResult:
    """Result of entropy calculation."""
    entropy: float  # Shannon entropy in bits
    normalized_entropy: float  # Normalized to [0, 1]
    max_entropy: float  # Maximum possible entropy for the bin configuration
    n_bins_used: int  # Number of bins with samples
    n_bins_total: int  # Total number of bins
    n_samples: int  # Number of samples used
    is_valid: bool
    error_message: Optional[str] = None


class GazeEntropyCalculator:
    """
    Calculator for Shannon entropy of gaze distributions.

    Discretizes gaze positions into a 2D grid and calculates Shannon entropy
    of the resulting distribution.

    Example:
        >>> calculator = GazeEntropyCalculator(horizontal_bins=12, vertical_bins=12)
        >>> result = calculator.calculate(gaze_x, gaze_y)
        >>> print(f"Entropy = {result.entropy:.2f} bits")
    """

    def __init__(self,
                 horizontal_bins: int = 12,
                 vertical_bins: int = 12,
                 x_range: Optional[Tuple[float, float]] = None,
                 y_range: Optional[Tuple[float, float]] = None):
        """
        Initialize the entropy calculator.

        Args:
            horizontal_bins: Number of bins in X direction.
            vertical_bins: Number of bins in Y direction.
            x_range: (min, max) range for X values. If None, uses data range.
            y_range: (min, max) range for Y values. If None, uses data range.
        """
        self.horizontal_bins = horizontal_bins
        self.vertical_bins = vertical_bins
        self.x_range = x_range
        self.y_range = y_range

        # Maximum possible entropy (uniform distribution across all bins)
        self.max_entropy = np.log2(horizontal_bins * vertical_bins)

    def calculate(self,
                  gaze_x: np.ndarray,
                  gaze_y: np.ndarray,
                  weights: Optional[np.ndarray] = None) -> EntropyResult:
        """
        Calculate Shannon entropy of gaze distribution.

        Args:
            gaze_x: X coordinates of gaze positions.
            gaze_y: Y coordinates of gaze positions.
            weights: Optional weights for each sample.

        Returns:
            EntropyResult with entropy value and diagnostics.
        """
        gaze_x = np.asarray(gaze_x, dtype=np.float64)
        gaze_y = np.asarray(gaze_y, dtype=np.float64)

        # Remove invalid samples
        valid_mask = ~(np.isnan(gaze_x) | np.isnan(gaze_y))
        gaze_x = gaze_x[valid_mask]
        gaze_y = gaze_y[valid_mask]

        if len(gaze_x) == 0:
            return EntropyResult(
                entropy=0.0,
                normalized_entropy=0.0,
                max_entropy=self.max_entropy,
                n_bins_used=0,
                n_bins_total=self.horizontal_bins * self.vertical_bins,
                n_samples=0,
                is_valid=False,
                error_message="No valid samples",
            )

        if weights is not None:
            weights = np.asarray(weights)[valid_mask]

        # Determine ranges
        if self.x_range is None:
            x_min, x_max = np.min(gaze_x), np.max(gaze_x)
            # Add small margin to avoid edge issues
            margin = (x_max - x_min) * 0.01
            x_min -= margin
            x_max += margin
        else:
            x_min, x_max = self.x_range

        if self.y_range is None:
            y_min, y_max = np.min(gaze_y), np.max(gaze_y)
            margin = (y_max - y_min) * 0.01
            y_min -= margin
            y_max += margin
        else:
            y_min, y_max = self.y_range

        # Create 2D histogram
        bins_2d, _, _ = self._create_histogram(
            gaze_x, gaze_y,
            x_min, x_max, y_min, y_max,
            weights
        )

        # Calculate entropy
        entropy, n_bins_used = self._calculate_shannon_entropy(bins_2d)

        # Normalize entropy
        normalized = entropy / self.max_entropy if self.max_entropy > 0 else 0.0

        return EntropyResult(
            entropy=entropy,
            normalized_entropy=normalized,
            max_entropy=self.max_entropy,
            n_bins_used=n_bins_used,
            n_bins_total=self.horizontal_bins * self.vertical_bins,
            n_samples=len(gaze_x),
            is_valid=True,
        )

    def calculate_from_directions(self,
                                   dir_x: np.ndarray,
                                   dir_y: np.ndarray,
                                   dir_z: np.ndarray) -> EntropyResult:
        """
        Calculate entropy from 3D gaze direction vectors.

        Converts directions to spherical coordinates (yaw, pitch) and calculates
        entropy on the angular distribution.

        Args:
            dir_x: X component of gaze direction.
            dir_y: Y component of gaze direction.
            dir_z: Z component of gaze direction.

        Returns:
            EntropyResult with entropy value.
        """
        dir_x = np.asarray(dir_x, dtype=np.float64)
        dir_y = np.asarray(dir_y, dtype=np.float64)
        dir_z = np.asarray(dir_z, dtype=np.float64)

        # Remove invalid samples
        valid_mask = ~(np.isnan(dir_x) | np.isnan(dir_y) | np.isnan(dir_z))
        dir_x = dir_x[valid_mask]
        dir_y = dir_y[valid_mask]
        dir_z = dir_z[valid_mask]

        if len(dir_x) == 0:
            return EntropyResult(
                entropy=0.0,
                normalized_entropy=0.0,
                max_entropy=self.max_entropy,
                n_bins_used=0,
                n_bins_total=self.horizontal_bins * self.vertical_bins,
                n_samples=0,
                is_valid=False,
                error_message="No valid samples",
            )

        # Convert to spherical coordinates
        yaw = np.arctan2(dir_x, dir_z)  # Horizontal angle
        pitch = np.arcsin(np.clip(dir_y, -1, 1))  # Vertical angle

        # Convert to degrees
        yaw_deg = np.degrees(yaw)
        pitch_deg = np.degrees(pitch)

        # Use fixed angular ranges
        return self.calculate(
            yaw_deg, pitch_deg
        )

    def _create_histogram(self,
                          x: np.ndarray,
                          y: np.ndarray,
                          x_min: float,
                          x_max: float,
                          y_min: float,
                          y_max: float,
                          weights: Optional[np.ndarray] = None) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
        """Create 2D histogram of gaze positions."""
        x_edges = np.linspace(x_min, x_max, self.horizontal_bins + 1)
        y_edges = np.linspace(y_min, y_max, self.vertical_bins + 1)

        histogram, x_edges, y_edges = np.histogram2d(
            x, y,
            bins=[x_edges, y_edges],
            weights=weights
        )

        return histogram, x_edges, y_edges

    def _calculate_shannon_entropy(self, bins: np.ndarray) -> Tuple[float, int]:
        """
        Calculate Shannon entropy from bin counts.

        Args:
            bins: 2D array of bin counts.

        Returns:
            Tuple of (entropy in bits, number of non-empty bins).
        """
        # Flatten bins
        counts = bins.flatten()

        # Get total
        total = np.sum(counts)
        if total <= 0:
            return 0.0, 0

        # Calculate probabilities for non-empty bins
        non_zero = counts > 0
        n_bins_used = np.sum(non_zero)

        if n_bins_used == 0:
            return 0.0, 0

        probabilities = counts[non_zero] / total

        # Shannon entropy: H = -sum(p * log2(p))
        entropy = -np.sum(probabilities * np.log2(probabilities))

        return entropy, int(n_bins_used)

    def calculate_windowed(self,
                           gaze_x: np.ndarray,
                           gaze_y: np.ndarray,
                           timestamps: np.ndarray,
                           window_duration: float = 3.0,
                           step_size: float = 1.0) -> List[EntropyResult]:
        """
        Calculate entropy over sliding time windows.

        Args:
            gaze_x: X coordinates.
            gaze_y: Y coordinates.
            timestamps: Timestamps for each sample.
            window_duration: Window size in seconds.
            step_size: Step between windows in seconds.

        Returns:
            List of EntropyResult for each window.
        """
        gaze_x = np.asarray(gaze_x)
        gaze_y = np.asarray(gaze_y)
        timestamps = np.asarray(timestamps)

        results = []
        start_time = timestamps[0]
        end_time = timestamps[-1]

        window_start = start_time
        while window_start + window_duration <= end_time:
            window_end = window_start + window_duration

            # Get samples in window
            mask = (timestamps >= window_start) & (timestamps < window_end)

            if np.sum(mask) > 10:  # Need minimum samples
                result = self.calculate(gaze_x[mask], gaze_y[mask])
                results.append(result)
            else:
                results.append(EntropyResult(
                    entropy=np.nan,
                    normalized_entropy=np.nan,
                    max_entropy=self.max_entropy,
                    n_bins_used=0,
                    n_bins_total=self.horizontal_bins * self.vertical_bins,
                    n_samples=np.sum(mask),
                    is_valid=False,
                    error_message="Insufficient samples",
                ))

            window_start += step_size

        return results

    def get_heatmap(self,
                    gaze_x: np.ndarray,
                    gaze_y: np.ndarray) -> np.ndarray:
        """
        Get the gaze distribution heatmap.

        Args:
            gaze_x: X coordinates.
            gaze_y: Y coordinates.

        Returns:
            2D numpy array of normalized bin counts.
        """
        gaze_x = np.asarray(gaze_x)
        gaze_y = np.asarray(gaze_y)

        # Remove invalid samples
        valid_mask = ~(np.isnan(gaze_x) | np.isnan(gaze_y))
        gaze_x = gaze_x[valid_mask]
        gaze_y = gaze_y[valid_mask]

        if len(gaze_x) == 0:
            return np.zeros((self.vertical_bins, self.horizontal_bins))

        # Determine ranges
        if self.x_range is None:
            x_min, x_max = np.min(gaze_x), np.max(gaze_x)
        else:
            x_min, x_max = self.x_range

        if self.y_range is None:
            y_min, y_max = np.min(gaze_y), np.max(gaze_y)
        else:
            y_min, y_max = self.y_range

        # Create histogram
        bins, _, _ = self._create_histogram(
            gaze_x, gaze_y,
            x_min, x_max, y_min, y_max
        )

        # Normalize and transpose for image display (Y axis inverted)
        max_val = np.max(bins)
        if max_val > 0:
            return (bins.T / max_val)[::-1, :]
        return bins.T[::-1, :]


def calculate_gaze_entropy(gaze_x: np.ndarray,
                           gaze_y: np.ndarray,
                           horizontal_bins: int = 12,
                           vertical_bins: int = 12) -> EntropyResult:
    """
    Convenience function to calculate gaze entropy.

    Args:
        gaze_x: X coordinates.
        gaze_y: Y coordinates.
        horizontal_bins: Number of X bins.
        vertical_bins: Number of Y bins.

    Returns:
        EntropyResult with entropy value.

    Example:
        >>> result = calculate_gaze_entropy(gaze_x, gaze_y)
        >>> print(f"Entropy: {result.entropy:.2f} bits")
    """
    calculator = GazeEntropyCalculator(horizontal_bins, vertical_bins)
    return calculator.calculate(gaze_x, gaze_y)


def entropy_timeseries(gaze_x: np.ndarray,
                       gaze_y: np.ndarray,
                       timestamps: np.ndarray,
                       window_duration: float = 3.0,
                       step_size: float = 1.0) -> Tuple[np.ndarray, np.ndarray]:
    """
    Compute entropy time series over sliding windows.

    Args:
        gaze_x: X coordinates.
        gaze_y: Y coordinates.
        timestamps: Timestamps.
        window_duration: Window size in seconds.
        step_size: Step between windows.

    Returns:
        Tuple of (window_centers, entropy_values).
    """
    calculator = GazeEntropyCalculator()
    results = calculator.calculate_windowed(
        gaze_x, gaze_y, timestamps,
        window_duration=window_duration,
        step_size=step_size,
    )

    if not results:
        return np.array([]), np.array([])

    # Calculate window centers
    start_time = timestamps[0]
    centers = []
    values = []

    for i, result in enumerate(results):
        center = start_time + i * step_size + window_duration / 2
        centers.append(center)
        values.append(result.entropy if result.is_valid else np.nan)

    return np.array(centers), np.array(values)


def stationary_entropy(gaze_x: np.ndarray,
                       gaze_y: np.ndarray) -> float:
    """
    Calculate stationary gaze entropy (single value for entire recording).

    Args:
        gaze_x: X coordinates.
        gaze_y: Y coordinates.

    Returns:
        Entropy value in bits.
    """
    result = calculate_gaze_entropy(gaze_x, gaze_y)
    return result.entropy if result.is_valid else np.nan


def transition_entropy(gaze_x: np.ndarray,
                       gaze_y: np.ndarray,
                       horizontal_bins: int = 12,
                       vertical_bins: int = 12) -> float:
    """
    Calculate gaze transition entropy (entropy of transitions between AOIs).

    Measures the unpredictability of gaze transitions between regions.

    Reference:
        Krejtz, K., et al. (2016). Gaze transition entropy. ETRA '16.

    Args:
        gaze_x: X coordinates.
        gaze_y: Y coordinates.
        horizontal_bins: Number of X bins.
        vertical_bins: Number of Y bins.

    Returns:
        Transition entropy in bits.
    """
    gaze_x = np.asarray(gaze_x)
    gaze_y = np.asarray(gaze_y)

    # Remove invalid samples
    valid_mask = ~(np.isnan(gaze_x) | np.isnan(gaze_y))
    gaze_x = gaze_x[valid_mask]
    gaze_y = gaze_y[valid_mask]

    if len(gaze_x) < 2:
        return 0.0

    # Determine ranges
    x_min, x_max = np.min(gaze_x), np.max(gaze_x)
    y_min, y_max = np.min(gaze_y), np.max(gaze_y)

    # Bin the data
    x_bins = np.clip(
        ((gaze_x - x_min) / (x_max - x_min + 1e-10) * horizontal_bins).astype(int),
        0, horizontal_bins - 1
    )
    y_bins = np.clip(
        ((gaze_y - y_min) / (y_max - y_min + 1e-10) * vertical_bins).astype(int),
        0, vertical_bins - 1
    )

    # Convert to single bin index
    n_bins = horizontal_bins * vertical_bins
    bin_indices = x_bins * vertical_bins + y_bins

    # Create transition matrix
    transition_matrix = np.zeros((n_bins, n_bins))
    for i in range(len(bin_indices) - 1):
        from_bin = bin_indices[i]
        to_bin = bin_indices[i + 1]
        transition_matrix[from_bin, to_bin] += 1

    # Calculate transition entropy
    # H(T) = -sum_i sum_j P(i,j) * log2(P(j|i))
    row_sums = transition_matrix.sum(axis=1, keepdims=True)
    row_sums[row_sums == 0] = 1  # Avoid division by zero

    # Conditional probabilities P(j|i)
    conditional_probs = transition_matrix / row_sums

    # Joint probabilities P(i,j)
    total_transitions = transition_matrix.sum()
    if total_transitions == 0:
        return 0.0
    joint_probs = transition_matrix / total_transitions

    # Calculate entropy
    entropy = 0.0
    for i in range(n_bins):
        for j in range(n_bins):
            if joint_probs[i, j] > 0 and conditional_probs[i, j] > 0:
                entropy -= joint_probs[i, j] * np.log2(conditional_probs[i, j])

    return entropy
