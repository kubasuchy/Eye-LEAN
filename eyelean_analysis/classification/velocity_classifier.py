"""
Velocity-threshold (I-VT) eye movement classification.

Implements fixation and saccade detection based on the I-VT algorithm of
Salvucci & Goldberg (2000) extended with the now-standard duration and
gap-based filtering of Olsson (2007) / Komogortsev et al. (2010). The
core threshold-and-group step is Salvucci–Goldberg; the
`min_fixation_duration` / `min_saccade_duration` / `merge_threshold`
parameters and their default values follow the Olsson/Tobii I-VT-Filter
convention (notably the 75 ms inter-fixation merge gap).

This module also uses Savitzky-Golay derivative smoothing to compute
velocity, which is a strict improvement over Salvucci-Goldberg's
two-point finite difference for high-sample-rate eye trackers; see
Nyström & Holmqvist (2010) for the rationale.

References:
    Salvucci, D. D., & Goldberg, J. H. (2000). Identifying fixations
    and saccades in eye-tracking protocols. *ETRA '00*, 71-78.
    https://doi.org/10.1145/355017.355028
    — the I-VT velocity-threshold algorithm.

    Olsson, P. (2007). *Real-time and offline filters for eye tracking.*
    MSc thesis, KTH/Tobii.
    — minimum-duration filtering and the 75 ms inter-fixation merge.

    Komogortsev, O. V., Gobert, D. V., Jayarathna, S., Koh, D. H., &
    Gowda, S. M. (2010). Standardization of automated analyses of
    oculomotor fixation and saccadic behaviors. *IEEE Transactions on
    Biomedical Engineering*, 57(11), 2635-2645.
    https://doi.org/10.1109/TBME.2010.2057429
    — formal evaluation framework for I-VT plus its post-processing
    additions.

    Nyström, M., & Holmqvist, K. (2010). An adaptive algorithm for
    fixation, saccade, and glissade detection in eye-tracking data.
    *Behavior Research Methods*, 42, 188-204.
    — rationale for derivative smoothing over two-point differences.
"""

import warnings

import numpy as np
import pandas as pd
from typing import Optional, Tuple, List, Dict, Union
from dataclasses import dataclass
from enum import Enum

from ..filters.savitzky_golay import SavitzkyGolayFilter, compute_gaze_velocity


class EyeMovementType(Enum):
    """Types of eye movements."""
    FIXATION = 0
    SACCADE = 1
    UNKNOWN = 2


@dataclass
class Fixation:
    """Represents a detected fixation."""
    start_idx: int
    end_idx: int
    start_time: float
    end_time: float
    duration: float
    centroid_x: float
    centroid_y: float
    centroid_z: Optional[float] = None
    dispersion: float = 0.0
    sample_count: int = 0


@dataclass
class Saccade:
    """Represents a detected saccade."""
    start_idx: int
    end_idx: int
    start_time: float
    end_time: float
    duration: float
    amplitude: float  # degrees
    peak_velocity: float  # degrees per second
    start_x: float
    start_y: float
    end_x: float
    end_y: float


class VelocityClassifier:
    """
    Velocity-threshold (I-VT) classifier for eye movement detection.

    Uses velocity thresholds to classify gaze samples as fixations or saccades,
    then groups consecutive samples into events.

    Example:
        >>> classifier = VelocityClassifier(velocity_threshold=50.0)
        >>> labels = classifier.classify(velocity_data)
        >>> fixations = classifier.detect_fixations(gaze_x, gaze_y, timestamps)
    """

    def __init__(self,
                 velocity_threshold: float = 50.0,
                 min_fixation_duration: float = 0.1,
                 min_saccade_duration: float = 0.02,
                 merge_threshold: float = 0.075):
        """
        Initialize the velocity classifier.

        Args:
            velocity_threshold: Velocity threshold in degrees/second.
                               Above = saccade, below = fixation.
            min_fixation_duration: Minimum fixation duration in seconds.
            min_saccade_duration: Minimum saccade duration in seconds.
            merge_threshold: Max gap between fixations to merge (seconds).
        """
        self.velocity_threshold = velocity_threshold
        self.min_fixation_duration = min_fixation_duration
        self.min_saccade_duration = min_saccade_duration
        self.merge_threshold = merge_threshold

    def classify(self, velocity: np.ndarray) -> np.ndarray:
        """
        Classify each sample as fixation or saccade based on velocity.

        Args:
            velocity: Array of velocity values (degrees/second).

        Returns:
            Array of EyeMovementType enum values.
        """
        velocity = np.asarray(velocity)
        labels = np.empty(len(velocity), dtype=object)

        for i, v in enumerate(velocity):
            if np.isnan(v):
                labels[i] = EyeMovementType.UNKNOWN
            elif v < self.velocity_threshold:
                labels[i] = EyeMovementType.FIXATION
            else:
                labels[i] = EyeMovementType.SACCADE

        return labels

    def classify_numeric(self, velocity: np.ndarray) -> np.ndarray:
        """
        Classify each sample and return numeric labels.

        Args:
            velocity: Array of velocity values.

        Returns:
            Array of integers (0=fixation, 1=saccade, 2=unknown).
        """
        velocity = np.asarray(velocity)
        labels = np.full(len(velocity), EyeMovementType.UNKNOWN.value, dtype=int)

        valid_mask = ~np.isnan(velocity)
        labels[valid_mask & (velocity < self.velocity_threshold)] = EyeMovementType.FIXATION.value
        labels[valid_mask & (velocity >= self.velocity_threshold)] = EyeMovementType.SACCADE.value

        return labels

    def detect_fixations(self,
                         gaze_x: np.ndarray,
                         gaze_y: np.ndarray,
                         timestamps: np.ndarray,
                         velocity: Optional[np.ndarray] = None,
                         gaze_z: Optional[np.ndarray] = None) -> List[Fixation]:
        """
        Detect fixation events from gaze data.

        Args:
            gaze_x: X coordinates of gaze positions.
            gaze_y: Y coordinates of gaze positions.
            timestamps: Timestamps for each sample.
            velocity: Pre-computed velocity (if None, computed internally).
            gaze_z: Optional Z coordinates for 3D gaze.

        Returns:
            List of Fixation objects.
        """
        gaze_x = np.asarray(gaze_x)
        gaze_y = np.asarray(gaze_y)
        timestamps = np.asarray(timestamps)

        n_samples = len(gaze_x)
        if n_samples == 0:
            return []

        # Compute velocity if not provided
        if velocity is None:
            sample_rate = self._estimate_sample_rate(timestamps)
            velocity = compute_gaze_velocity(gaze_x, gaze_y, sample_rate)

        # Get sample-level classification
        labels = self.classify_numeric(velocity)

        # Group consecutive fixation samples
        fixations = []
        in_fixation = False
        fix_start = 0

        for i in range(n_samples):
            if labels[i] == EyeMovementType.FIXATION.value:
                if not in_fixation:
                    in_fixation = True
                    fix_start = i
            else:
                if in_fixation:
                    # End of fixation
                    fixation = self._create_fixation(
                        fix_start, i - 1, gaze_x, gaze_y, timestamps, gaze_z
                    )
                    if fixation is not None:
                        fixations.append(fixation)
                    in_fixation = False

        # Handle fixation at end of data
        if in_fixation:
            fixation = self._create_fixation(
                fix_start, n_samples - 1, gaze_x, gaze_y, timestamps, gaze_z
            )
            if fixation is not None:
                fixations.append(fixation)

        # Merge nearby fixations
        fixations = self._merge_fixations(fixations, timestamps)

        return fixations

    def detect_saccades(self,
                        gaze_x: np.ndarray,
                        gaze_y: np.ndarray,
                        timestamps: np.ndarray,
                        velocity: Optional[np.ndarray] = None) -> List[Saccade]:
        """
        Detect saccade events from gaze data.

        Args:
            gaze_x: X coordinates of gaze positions.
            gaze_y: Y coordinates of gaze positions.
            timestamps: Timestamps for each sample.
            velocity: Pre-computed velocity (if None, computed internally).

        Returns:
            List of Saccade objects.
        """
        gaze_x = np.asarray(gaze_x)
        gaze_y = np.asarray(gaze_y)
        timestamps = np.asarray(timestamps)

        n_samples = len(gaze_x)
        if n_samples == 0:
            return []

        # Compute velocity if not provided
        if velocity is None:
            sample_rate = self._estimate_sample_rate(timestamps)
            velocity = compute_gaze_velocity(gaze_x, gaze_y, sample_rate)

        # Get sample-level classification
        labels = self.classify_numeric(velocity)

        # Group consecutive saccade samples
        saccades = []
        in_saccade = False
        sacc_start = 0

        for i in range(n_samples):
            if labels[i] == EyeMovementType.SACCADE.value:
                if not in_saccade:
                    in_saccade = True
                    sacc_start = i
            else:
                if in_saccade:
                    # End of saccade
                    saccade = self._create_saccade(
                        sacc_start, i - 1, gaze_x, gaze_y, timestamps, velocity
                    )
                    if saccade is not None:
                        saccades.append(saccade)
                    in_saccade = False

        # Handle saccade at end of data
        if in_saccade:
            saccade = self._create_saccade(
                sacc_start, n_samples - 1, gaze_x, gaze_y, timestamps, velocity
            )
            if saccade is not None:
                saccades.append(saccade)

        return saccades

    def detect_eye_movements(self,
                             gaze_x: np.ndarray,
                             gaze_y: np.ndarray,
                             timestamps: np.ndarray,
                             gaze_z: Optional[np.ndarray] = None) -> Dict:
        """
        Detect all eye movement events (fixations and saccades).

        Args:
            gaze_x: X coordinates of gaze positions.
            gaze_y: Y coordinates of gaze positions.
            timestamps: Timestamps for each sample.
            gaze_z: Optional Z coordinates for 3D gaze.

        Returns:
            Dictionary with 'fixations', 'saccades', 'labels', and 'velocity'.
        """
        gaze_x = np.asarray(gaze_x)
        gaze_y = np.asarray(gaze_y)
        timestamps = np.asarray(timestamps)

        # Compute velocity
        sample_rate = self._estimate_sample_rate(timestamps)
        velocity = compute_gaze_velocity(gaze_x, gaze_y, sample_rate)

        # Classify samples
        labels = self.classify_numeric(velocity)

        # Detect events
        fixations = self.detect_fixations(
            gaze_x, gaze_y, timestamps, velocity, gaze_z
        )
        saccades = self.detect_saccades(
            gaze_x, gaze_y, timestamps, velocity
        )

        return {
            'fixations': fixations,
            'saccades': saccades,
            'labels': labels,
            'velocity': velocity,
            'sample_rate': sample_rate,
        }

    def _create_fixation(self,
                         start_idx: int,
                         end_idx: int,
                         gaze_x: np.ndarray,
                         gaze_y: np.ndarray,
                         timestamps: np.ndarray,
                         gaze_z: Optional[np.ndarray] = None) -> Optional[Fixation]:
        """Create a Fixation object from sample indices."""
        duration = timestamps[end_idx] - timestamps[start_idx]

        if duration < self.min_fixation_duration:
            return None

        x_segment = gaze_x[start_idx:end_idx + 1]
        y_segment = gaze_y[start_idx:end_idx + 1]

        # Calculate centroid (ignoring NaN)
        valid_mask = ~(np.isnan(x_segment) | np.isnan(y_segment))
        if not np.any(valid_mask):
            return None

        centroid_x = np.nanmean(x_segment)
        centroid_y = np.nanmean(y_segment)

        # Calculate dispersion
        dispersion = np.sqrt(
            np.nanvar(x_segment) + np.nanvar(y_segment)
        )

        # Handle 3D
        centroid_z = None
        if gaze_z is not None:
            z_segment = gaze_z[start_idx:end_idx + 1]
            centroid_z = np.nanmean(z_segment)

        return Fixation(
            start_idx=start_idx,
            end_idx=end_idx,
            start_time=timestamps[start_idx],
            end_time=timestamps[end_idx],
            duration=duration,
            centroid_x=centroid_x,
            centroid_y=centroid_y,
            centroid_z=centroid_z,
            dispersion=dispersion,
            sample_count=end_idx - start_idx + 1,
        )

    def _create_saccade(self,
                        start_idx: int,
                        end_idx: int,
                        gaze_x: np.ndarray,
                        gaze_y: np.ndarray,
                        timestamps: np.ndarray,
                        velocity: np.ndarray) -> Optional[Saccade]:
        """Create a Saccade object from sample indices."""
        duration = timestamps[end_idx] - timestamps[start_idx]

        if duration < self.min_saccade_duration:
            return None

        # Calculate amplitude (Euclidean distance)
        start_x = gaze_x[start_idx]
        start_y = gaze_y[start_idx]
        end_x = gaze_x[end_idx]
        end_y = gaze_y[end_idx]

        amplitude = np.sqrt((end_x - start_x)**2 + (end_y - start_y)**2)

        # Peak velocity
        vel_segment = velocity[start_idx:end_idx + 1]
        peak_velocity = np.nanmax(vel_segment)

        return Saccade(
            start_idx=start_idx,
            end_idx=end_idx,
            start_time=timestamps[start_idx],
            end_time=timestamps[end_idx],
            duration=duration,
            amplitude=amplitude,
            peak_velocity=peak_velocity,
            start_x=start_x,
            start_y=start_y,
            end_x=end_x,
            end_y=end_y,
        )

    def _merge_fixations(self,
                         fixations: List[Fixation],
                         timestamps: np.ndarray) -> List[Fixation]:
        """Merge nearby fixations whose inter-event gap is ≤ `merge_threshold`.

        Centroid is sample-count-weighted (Olsson 2007 / Tobii I-VT-Filter)
        rather than averaged with equal weights — when one fixation is
        much longer than the other, equal-weight averaging biases the
        merged centroid toward the shorter event.
        """
        if len(fixations) <= 1:
            return fixations

        merged = [fixations[0]]

        for fix in fixations[1:]:
            prev = merged[-1]
            gap = fix.start_time - prev.end_time

            if gap <= self.merge_threshold:
                w_prev = prev.sample_count
                w_curr = fix.sample_count
                w_total = w_prev + w_curr if (w_prev + w_curr) > 0 else 1
                cz_prev, cz_curr = prev.centroid_z, fix.centroid_z
                merged_cz = (
                    (cz_prev * w_prev + cz_curr * w_curr) / w_total
                    if (cz_prev is not None and cz_curr is not None)
                    else None
                )
                new_fix = Fixation(
                    start_idx=prev.start_idx,
                    end_idx=fix.end_idx,
                    start_time=prev.start_time,
                    end_time=fix.end_time,
                    duration=fix.end_time - prev.start_time,
                    centroid_x=(prev.centroid_x * w_prev + fix.centroid_x * w_curr) / w_total,
                    centroid_y=(prev.centroid_y * w_prev + fix.centroid_y * w_curr) / w_total,
                    centroid_z=merged_cz,
                    dispersion=(prev.dispersion * w_prev + fix.dispersion * w_curr) / w_total,
                    sample_count=w_total,
                )
                merged[-1] = new_fix
            else:
                merged.append(fix)

        return merged

    def _estimate_sample_rate(self, timestamps: np.ndarray) -> float:
        """Estimate sample rate from timestamps. Falls back to 120 Hz with a warning
        when the timestamp series is too short or non-monotonic — silently using the
        default mis-classifies fixations on data recorded at any other rate."""
        if len(timestamps) < 2:
            warnings.warn(
                "VelocityClassifier: cannot estimate sample rate from <2 timestamps; "
                "falling back to 120 Hz. Pass sample_rate explicitly if your data is at a different rate."
            )
            return 120.0

        dt = np.median(np.diff(timestamps))
        if dt <= 0:
            warnings.warn(
                "VelocityClassifier: median timestamp delta is non-positive "
                f"(got {dt}); falling back to 120 Hz. Check that timestamps are monotonic and in seconds."
            )
            return 120.0

        return 1.0 / dt


def classify_fixations(gaze_x: np.ndarray,
                       gaze_y: np.ndarray,
                       timestamps: np.ndarray,
                       velocity_threshold: float = 50.0,
                       min_duration: float = 0.1) -> List[Fixation]:
    """
    Convenience function to detect fixations.

    Args:
        gaze_x: X coordinates.
        gaze_y: Y coordinates.
        timestamps: Timestamps.
        velocity_threshold: Velocity threshold (deg/s).
        min_duration: Minimum fixation duration (s).

    Returns:
        List of Fixation objects.
    """
    classifier = VelocityClassifier(
        velocity_threshold=velocity_threshold,
        min_fixation_duration=min_duration,
    )
    return classifier.detect_fixations(gaze_x, gaze_y, timestamps)


def classify_saccades(gaze_x: np.ndarray,
                      gaze_y: np.ndarray,
                      timestamps: np.ndarray,
                      velocity_threshold: float = 50.0,
                      min_duration: float = 0.02) -> List[Saccade]:
    """
    Convenience function to detect saccades.

    Args:
        gaze_x: X coordinates.
        gaze_y: Y coordinates.
        timestamps: Timestamps.
        velocity_threshold: Velocity threshold (deg/s).
        min_duration: Minimum saccade duration (s).

    Returns:
        List of Saccade objects.
    """
    classifier = VelocityClassifier(
        velocity_threshold=velocity_threshold,
        min_saccade_duration=min_duration,
    )
    return classifier.detect_saccades(gaze_x, gaze_y, timestamps)


def detect_eye_movements(gaze_x: np.ndarray,
                         gaze_y: np.ndarray,
                         timestamps: np.ndarray,
                         velocity_threshold: float = 50.0) -> Dict:
    """
    Detect fixations and saccades using the velocity-threshold (I-VT) algorithm.

    IMPORTANT: Input coordinates must be in ANGULAR DEGREES, not 3D direction vectors!

    If you have 3D gaze direction vectors from Eye_lean (e.g., combined_dir_x/y/z),
    convert them to angular coordinates first:

        gaze_x = np.degrees(np.arctan2(dir_x, dir_z))  # Horizontal angle (yaw)
        gaze_y = np.degrees(np.arctan2(dir_y, dir_z))  # Vertical angle (pitch)

    Args:
        gaze_x: Horizontal gaze angle in DEGREES. Not radians, not world coordinates.
        gaze_y: Vertical gaze angle in DEGREES. Not radians, not world coordinates.
        timestamps: Sample timestamps in seconds.
        velocity_threshold: Threshold for fixation/saccade classification in
                           DEGREES PER SECOND. Typical values: 30-100 deg/s.
                           Lower values detect more fixations, higher values more saccades.

    Returns:
        Dictionary containing:
            - 'fixations': List[Fixation] - detected fixation events
            - 'saccades': List[Saccade] - detected saccade events
            - 'labels': np.ndarray - per-sample classification (0=fixation, 1=saccade)
            - 'velocity': np.ndarray - computed velocity at each sample (deg/s)
            - 'sample_rate': float - estimated sample rate in Hz

    Example:
        >>> # Convert Eye_lean direction vectors to angles
        >>> gaze_x = np.degrees(np.arctan2(data.df['combined_dir_x'], data.df['combined_dir_z']))
        >>> gaze_y = np.degrees(np.arctan2(data.df['combined_dir_y'], data.df['combined_dir_z']))
        >>> timestamps = data.get_timestamps()
        >>>
        >>> # Detect eye movements
        >>> movements = detect_eye_movements(gaze_x, gaze_y, timestamps, velocity_threshold=30.0)
        >>> print(f"Found {len(movements['fixations'])} fixations")
    """
    classifier = VelocityClassifier(velocity_threshold=velocity_threshold)
    return classifier.detect_eye_movements(gaze_x, gaze_y, timestamps)


def fixations_to_dataframe(fixations: List[Fixation]) -> pd.DataFrame:
    """
    Convert list of fixations to a pandas DataFrame.

    Args:
        fixations: List of Fixation objects.

    Returns:
        DataFrame with fixation data.
    """
    if not fixations:
        return pd.DataFrame()

    data = []
    for fix in fixations:
        data.append({
            'start_idx': fix.start_idx,
            'end_idx': fix.end_idx,
            'start_time': fix.start_time,
            'end_time': fix.end_time,
            'duration': fix.duration,
            'centroid_x': fix.centroid_x,
            'centroid_y': fix.centroid_y,
            'centroid_z': fix.centroid_z,
            'dispersion': fix.dispersion,
            'sample_count': fix.sample_count,
        })

    return pd.DataFrame(data)


def saccades_to_dataframe(saccades: List[Saccade]) -> pd.DataFrame:
    """
    Convert list of saccades to a pandas DataFrame.

    Args:
        saccades: List of Saccade objects.

    Returns:
        DataFrame with saccade data.
    """
    if not saccades:
        return pd.DataFrame()

    data = []
    for sacc in saccades:
        data.append({
            'start_idx': sacc.start_idx,
            'end_idx': sacc.end_idx,
            'start_time': sacc.start_time,
            'end_time': sacc.end_time,
            'duration': sacc.duration,
            'amplitude': sacc.amplitude,
            'peak_velocity': sacc.peak_velocity,
            'start_x': sacc.start_x,
            'start_y': sacc.start_y,
            'end_x': sacc.end_x,
            'end_y': sacc.end_y,
        })

    return pd.DataFrame(data)
