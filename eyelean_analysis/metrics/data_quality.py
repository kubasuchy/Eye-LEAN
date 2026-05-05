"""
Data quality metrics for eye tracking data.

Provides various quality indicators for assessing eye tracking data validity
and reliability — validity rate, gap counts, blink-rate estimation,
spatial precision (RMS sample-to-sample distance).

The metric definitions follow the eye-tracking methodology literature:

Reference:
    Holmqvist, K., Nyström, M., Andersson, R., Dewhurst, R., Jarodzka,
    H., & van de Weijer, J. (2011). Eye Tracking: A Comprehensive
    Guide to Methods and Measures. Oxford University Press. — chapter
    on data quality (validity rate, accuracy, precision, robustness).
"""

import numpy as np
from typing import Dict, Optional, List, Tuple
from dataclasses import dataclass


@dataclass
class QualityMetrics:
    """Container for data quality metrics."""
    # Sample counts
    total_samples: int
    valid_samples: int
    invalid_samples: int
    validity_percent: float

    # Timing
    duration_seconds: float
    sample_rate_hz: float
    sample_rate_stability: float  # CV of sample intervals

    # Data gaps
    n_gaps: int
    total_gap_duration: float
    max_gap_duration: float
    gap_percent: float

    # Signal quality
    left_eye_validity: float
    right_eye_validity: float
    combined_eye_validity: float

    # Pupil quality (if available)
    pupil_validity_percent: Optional[float] = None
    pupil_range_mm: Optional[Tuple[float, float]] = None

    # Blink statistics (if available)
    n_blinks: Optional[int] = None
    blink_rate_per_min: Optional[float] = None

    # Overall score
    quality_score: float = 0.0

    def to_dict(self) -> Dict:
        """Convert to dictionary."""
        return {
            'total_samples': self.total_samples,
            'valid_samples': self.valid_samples,
            'invalid_samples': self.invalid_samples,
            'validity_percent': self.validity_percent,
            'duration_seconds': self.duration_seconds,
            'sample_rate_hz': self.sample_rate_hz,
            'sample_rate_stability': self.sample_rate_stability,
            'n_gaps': self.n_gaps,
            'total_gap_duration': self.total_gap_duration,
            'max_gap_duration': self.max_gap_duration,
            'gap_percent': self.gap_percent,
            'left_eye_validity': self.left_eye_validity,
            'right_eye_validity': self.right_eye_validity,
            'combined_eye_validity': self.combined_eye_validity,
            'pupil_validity_percent': self.pupil_validity_percent,
            'pupil_range_mm': self.pupil_range_mm,
            'n_blinks': self.n_blinks,
            'blink_rate_per_min': self.blink_rate_per_min,
            'quality_score': self.quality_score,
        }


def calculate_quality_metrics(timestamps: np.ndarray,
                              validity: Optional[np.ndarray] = None,
                              left_validity: Optional[np.ndarray] = None,
                              right_validity: Optional[np.ndarray] = None,
                              left_pupil: Optional[np.ndarray] = None,
                              right_pupil: Optional[np.ndarray] = None,
                              left_openness: Optional[np.ndarray] = None,
                              right_openness: Optional[np.ndarray] = None,
                              gap_threshold: float = 0.1,
                              blink_openness_threshold: float = 0.2) -> QualityMetrics:
    """
    Calculate comprehensive quality metrics for eye tracking data.

    Args:
        timestamps: Array of timestamps in seconds.
        validity: Overall tracking validity flags (boolean).
        left_validity: Left eye validity flags.
        right_validity: Right eye validity flags.
        left_pupil: Left eye pupil diameter values.
        right_pupil: Right eye pupil diameter values.
        left_openness: Left eye openness values (for blink detection).
        right_openness: Right eye openness values (for blink detection).
        gap_threshold: Gap threshold in seconds for gap detection.
        blink_openness_threshold: Openness value below which the eye is treated as
            closed for blink-counting (default 0.2). VIVE openness is in [0, 1].
            Tighten (e.g. 0.1) on devices that report fluttery openness signals
            to suppress micro-blinks; loosen (e.g. 0.4) on noisier devices.

    Returns:
        QualityMetrics with computed quality indicators.
    """
    timestamps = np.asarray(timestamps)
    n_samples = len(timestamps)

    if n_samples == 0:
        return QualityMetrics(
            total_samples=0,
            valid_samples=0,
            invalid_samples=0,
            validity_percent=0.0,
            duration_seconds=0.0,
            sample_rate_hz=0.0,
            sample_rate_stability=0.0,
            n_gaps=0,
            total_gap_duration=0.0,
            max_gap_duration=0.0,
            gap_percent=0.0,
            left_eye_validity=0.0,
            right_eye_validity=0.0,
            combined_eye_validity=0.0,
            quality_score=0.0,
        )

    # Duration and sample rate
    duration = timestamps[-1] - timestamps[0] if n_samples > 1 else 0.0
    intervals = np.diff(timestamps) if n_samples > 1 else np.array([])

    if len(intervals) > 0:
        median_interval = np.median(intervals)
        sample_rate = 1.0 / median_interval if median_interval > 0 else 0.0
        # Coefficient of variation for stability
        sample_rate_cv = np.std(intervals) / np.mean(intervals) if np.mean(intervals) > 0 else 0.0
    else:
        sample_rate = 0.0
        sample_rate_cv = 0.0

    # Gap detection
    gaps = intervals[intervals > gap_threshold] if len(intervals) > 0 else np.array([])
    n_gaps = len(gaps)
    total_gap = np.sum(gaps)
    max_gap = np.max(gaps) if n_gaps > 0 else 0.0
    gap_percent = (total_gap / duration * 100) if duration > 0 else 0.0

    # Validity statistics
    if validity is not None:
        validity = np.asarray(validity).astype(bool)
        valid_count = np.sum(validity)
        validity_pct = (valid_count / n_samples * 100)
    else:
        valid_count = n_samples
        validity_pct = 100.0

    invalid_count = n_samples - valid_count

    # Eye-specific validity
    left_valid_pct = 100.0
    right_valid_pct = 100.0

    if left_validity is not None:
        left_validity = np.asarray(left_validity).astype(bool)
        left_valid_pct = np.sum(left_validity) / n_samples * 100

    if right_validity is not None:
        right_validity = np.asarray(right_validity).astype(bool)
        right_valid_pct = np.sum(right_validity) / n_samples * 100

    # Combined validity (either eye valid)
    if left_validity is not None and right_validity is not None:
        combined_valid = left_validity | right_validity
        combined_valid_pct = np.sum(combined_valid) / n_samples * 100
    elif left_validity is not None:
        combined_valid_pct = left_valid_pct
    elif right_validity is not None:
        combined_valid_pct = right_valid_pct
    else:
        combined_valid_pct = validity_pct

    # Pupil quality
    pupil_valid_pct = None
    pupil_range = None

    if left_pupil is not None or right_pupil is not None:
        pupil_values = []
        pupil_valid_count = 0
        pupil_total = 0

        for pupil in [left_pupil, right_pupil]:
            if pupil is not None:
                pupil = np.asarray(pupil)
                valid_pupil = ~np.isnan(pupil)
                pupil_valid_count += np.sum(valid_pupil)
                pupil_total += len(pupil)
                pupil_values.extend(pupil[valid_pupil].tolist())

        if pupil_total > 0:
            pupil_valid_pct = pupil_valid_count / pupil_total * 100

        if len(pupil_values) > 0:
            pupil_range = (np.min(pupil_values), np.max(pupil_values))

    # Blink detection
    n_blinks = None
    blink_rate = None

    if left_openness is not None or right_openness is not None:
        n_blinks = 0

        for openness in [left_openness, right_openness]:
            if openness is not None:
                openness = np.asarray(openness)
                is_closed = openness < blink_openness_threshold
                # Count transitions from open to closed
                transitions = np.diff(is_closed.astype(int))
                n_blinks += np.sum(transitions == 1)

        # Average blinks between eyes
        if left_openness is not None and right_openness is not None:
            n_blinks = n_blinks // 2

        if duration > 0:
            blink_rate = n_blinks / duration * 60  # per minute

    # Calculate overall quality score (0-100)
    quality_score = _calculate_quality_score(
        validity_pct=validity_pct,
        sample_rate_stability=1.0 - min(sample_rate_cv, 1.0),
        gap_percent=gap_percent,
        combined_validity=combined_valid_pct,
    )

    return QualityMetrics(
        total_samples=n_samples,
        valid_samples=int(valid_count),
        invalid_samples=int(invalid_count),
        validity_percent=validity_pct,
        duration_seconds=duration,
        sample_rate_hz=sample_rate,
        sample_rate_stability=1.0 - min(sample_rate_cv, 1.0),
        n_gaps=n_gaps,
        total_gap_duration=total_gap,
        max_gap_duration=max_gap,
        gap_percent=gap_percent,
        left_eye_validity=left_valid_pct,
        right_eye_validity=right_valid_pct,
        combined_eye_validity=combined_valid_pct,
        pupil_validity_percent=pupil_valid_pct,
        pupil_range_mm=pupil_range,
        n_blinks=n_blinks,
        blink_rate_per_min=blink_rate,
        quality_score=quality_score,
    )


def _calculate_quality_score(validity_pct: float,
                              sample_rate_stability: float,
                              gap_percent: float,
                              combined_validity: float) -> float:
    """
    Calculate overall quality score from individual metrics.

    Score ranges from 0 (poor) to 100 (excellent).
    """
    # Weight factors
    weights = {
        'validity': 0.4,
        'stability': 0.2,
        'gaps': 0.2,
        'combined': 0.2,
    }

    # Score components (all normalized to 0-100)
    validity_score = validity_pct
    stability_score = sample_rate_stability * 100
    gap_score = max(0, 100 - gap_percent * 10)  # Penalize gaps
    combined_score = combined_validity

    # Weighted average
    score = (
        weights['validity'] * validity_score +
        weights['stability'] * stability_score +
        weights['gaps'] * gap_score +
        weights['combined'] * combined_score
    )

    return min(100, max(0, score))


def detect_tracking_loss(validity: np.ndarray,
                         timestamps: np.ndarray,
                         min_duration: float = 0.1) -> List[Tuple[float, float]]:
    """
    Detect periods of tracking loss.

    Args:
        validity: Boolean array of tracking validity.
        timestamps: Timestamps for each sample.
        min_duration: Minimum duration to report.

    Returns:
        List of (start_time, end_time) tuples for tracking loss periods.
    """
    validity = np.asarray(validity).astype(bool)
    timestamps = np.asarray(timestamps)

    periods = []
    in_loss = False
    loss_start = 0.0

    for i, valid in enumerate(validity):
        if not valid and not in_loss:
            # Start of tracking loss
            in_loss = True
            loss_start = timestamps[i]
        elif valid and in_loss:
            # End of tracking loss
            in_loss = False
            duration = timestamps[i] - loss_start
            if duration >= min_duration:
                periods.append((loss_start, timestamps[i]))

    # Handle tracking loss at end
    if in_loss:
        duration = timestamps[-1] - loss_start
        if duration >= min_duration:
            periods.append((loss_start, timestamps[-1]))

    return periods


def calculate_precision(gaze_x: np.ndarray,
                        gaze_y: np.ndarray,
                        window_size: int = 30) -> float:
    """
    Estimate gaze precision (spatial noise) using RMS of deviations.

    Args:
        gaze_x: X coordinates during fixation.
        gaze_y: Y coordinates during fixation.
        window_size: Window size for local mean calculation.

    Returns:
        Precision estimate in same units as input.
    """
    gaze_x = np.asarray(gaze_x)
    gaze_y = np.asarray(gaze_y)

    # Remove NaN
    valid = ~(np.isnan(gaze_x) | np.isnan(gaze_y))
    gaze_x = gaze_x[valid]
    gaze_y = gaze_y[valid]

    if len(gaze_x) < window_size:
        return np.nan

    # Calculate RMS of sample-to-sample differences
    dx = np.diff(gaze_x)
    dy = np.diff(gaze_y)
    distances = np.sqrt(dx**2 + dy**2)

    return np.sqrt(np.mean(distances**2))
