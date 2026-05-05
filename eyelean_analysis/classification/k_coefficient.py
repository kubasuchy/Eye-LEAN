"""
K-coefficient for ambient/focal attention classification.

The K-coefficient characterizes eye movement patterns as either:
- Focal: Detailed inspection with long fixations and small saccades (K > 0)
- Ambient: Scanning/overview with short fixations and large saccades (K < 0)
- Neutral: Balanced attention (K ≈ 0)

Reference:
    Krejtz, K., Duchowski, A. T., Niber, T., Krejtz, I., & Kopacz, A. (2016).
    Eye tracking cognitive load using pupil diameter and microsaccades with
    fixed gaze. PLoS ONE, 11(9), e0163087.
    https://doi.org/10.1371/journal.pone.0163087

    Krejtz, K., Duchowski, A., Krejtz, I., Kopacz, A., & Chrząstowski-Wachtel, P. (2016).
    Gaze transition entropy. Proceedings of the 2016 symposium on Eye tracking
    research & applications (ETRA '16), 191-194.
"""

import numpy as np
from typing import Optional, List, Tuple, Dict, Union
from dataclasses import dataclass
from enum import Enum

from .velocity_classifier import Fixation, Saccade, VelocityClassifier


class AttentionType(Enum):
    """Types of attention based on K-coefficient."""
    FOCAL = 0      # K > threshold (detailed inspection)
    AMBIENT = 1    # K < -threshold (scanning)
    NEUTRAL = 2    # |K| < threshold (balanced)
    UNKNOWN = 3


@dataclass
class KCoefficientResult:
    """Result of K-coefficient calculation."""
    k_coefficient: float
    attention_type: AttentionType
    mean_fixation_duration: float
    mean_saccade_amplitude: float
    n_fixations: int
    n_saccades: int
    window_start: Optional[float] = None
    window_end: Optional[float] = None


class KCoefficientCalculator:
    """
    Calculator for the K-coefficient attention metric.

    The K-coefficient is computed from the relationship between fixation
    durations and saccade amplitudes:

        K = ln(d_i / d̄) × ln(a_i / ā)

    Where:
        d_i = individual fixation duration
        d̄ = mean fixation duration
        a_i = individual saccade amplitude
        ā = mean saccade amplitude

    Positive K indicates focal attention (long fixations after small saccades),
    negative K indicates ambient attention (short fixations after large saccades).

    Example:
        >>> calculator = KCoefficientCalculator()
        >>> result = calculator.calculate(fixations, saccades)
        >>> print(f"K={result.k_coefficient:.2f}, Attention: {result.attention_type}")
    """

    def __init__(self,
                 focal_threshold: float = 0.5,
                 ambient_threshold: float = 0.5,
                 velocity_threshold: float = 50.0,
                 min_fixation_duration: float = 0.1,
                 window_size: int = 30):
        """
        Initialize the K-coefficient calculator.

        Args:
            focal_threshold: K value above which attention is classified as focal.
            ambient_threshold: K value below which attention is classified as ambient.
            velocity_threshold: Velocity threshold for fixation/saccade detection.
            min_fixation_duration: Minimum fixation duration in seconds.
            window_size: Number of events to use for calculation.
        """
        self.focal_threshold = focal_threshold
        self.ambient_threshold = ambient_threshold
        self.velocity_threshold = velocity_threshold
        self.min_fixation_duration = min_fixation_duration
        self.window_size = window_size

        # Internal classifier
        self._classifier = VelocityClassifier(
            velocity_threshold=velocity_threshold,
            min_fixation_duration=min_fixation_duration,
        )

    def calculate(self,
                  fixations: List[Fixation],
                  saccades: List[Saccade]) -> KCoefficientResult:
        """
        Calculate K-coefficient from fixation and saccade lists.

        Args:
            fixations: List of detected fixations.
            saccades: List of detected saccades.

        Returns:
            KCoefficientResult with K-coefficient and classification.
        """
        if len(fixations) < 2 or len(saccades) < 1:
            return KCoefficientResult(
                k_coefficient=0.0,
                attention_type=AttentionType.UNKNOWN,
                mean_fixation_duration=0.0,
                mean_saccade_amplitude=0.0,
                n_fixations=len(fixations),
                n_saccades=len(saccades),
            )

        # Get durations and amplitudes
        durations = np.array([f.duration for f in fixations])
        amplitudes = np.array([s.amplitude for s in saccades])

        # Calculate mean values
        mean_duration = np.mean(durations)
        mean_amplitude = np.mean(amplitudes)

        # Avoid division by zero
        if mean_duration <= 0 or mean_amplitude <= 0:
            return KCoefficientResult(
                k_coefficient=0.0,
                attention_type=AttentionType.NEUTRAL,
                mean_fixation_duration=mean_duration,
                mean_saccade_amplitude=mean_amplitude,
                n_fixations=len(fixations),
                n_saccades=len(saccades),
            )

        # Calculate K-coefficient (using most recent pair)
        # Standard approach: pair each fixation with following saccade
        n_pairs = min(len(durations), len(amplitudes))
        k_values = []

        for i in range(n_pairs):
            if durations[i] > 0 and amplitudes[i] > 0:
                ln_d = np.log(durations[i] / mean_duration)
                ln_a = np.log(amplitudes[i] / mean_amplitude)
                k_values.append(ln_d * ln_a)

        if len(k_values) == 0:
            k_coefficient = 0.0
        else:
            k_coefficient = np.mean(k_values)

        # Classify attention type
        attention_type = self._classify_attention(k_coefficient)

        return KCoefficientResult(
            k_coefficient=k_coefficient,
            attention_type=attention_type,
            mean_fixation_duration=mean_duration,
            mean_saccade_amplitude=mean_amplitude,
            n_fixations=len(fixations),
            n_saccades=len(saccades),
        )

    def calculate_from_gaze(self,
                            gaze_x: np.ndarray,
                            gaze_y: np.ndarray,
                            timestamps: np.ndarray) -> KCoefficientResult:
        """
        Calculate K-coefficient directly from gaze data.

        Args:
            gaze_x: X coordinates of gaze positions.
            gaze_y: Y coordinates of gaze positions.
            timestamps: Timestamps for each sample.

        Returns:
            KCoefficientResult with K-coefficient and classification.
        """
        # Detect fixations and saccades
        movements = self._classifier.detect_eye_movements(gaze_x, gaze_y, timestamps)
        fixations = movements['fixations']
        saccades = movements['saccades']

        result = self.calculate(fixations, saccades)

        # Add window timing
        if len(timestamps) > 0:
            result.window_start = timestamps[0]
            result.window_end = timestamps[-1]

        return result

    def calculate_windowed(self,
                           gaze_x: np.ndarray,
                           gaze_y: np.ndarray,
                           timestamps: np.ndarray,
                           window_duration: float = 5.0,
                           step_size: float = 1.0) -> List[KCoefficientResult]:
        """
        Calculate K-coefficient over sliding windows.

        Args:
            gaze_x: X coordinates of gaze positions.
            gaze_y: Y coordinates of gaze positions.
            timestamps: Timestamps for each sample.
            window_duration: Window size in seconds.
            step_size: Step between windows in seconds.

        Returns:
            List of KCoefficientResult for each window.
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
                result = self.calculate_from_gaze(
                    gaze_x[mask], gaze_y[mask], timestamps[mask]
                )
                result.window_start = window_start
                result.window_end = window_end
                results.append(result)

            window_start += step_size

        return results

    def calculate_velocity_based(self,
                                 velocity: np.ndarray,
                                 timestamps: Optional[np.ndarray] = None) -> KCoefficientResult:
        """
        Calculate K-coefficient directly from velocity using simplified approach.

        This method maps velocity to K-coefficient without explicit fixation/saccade
        detection, useful for real-time visualization.

        Velocity mapping:
            - < 15 deg/s: Strong to weak focal (K = 1.5 to 0.5)
            - 15-40 deg/s: Weak focal to weak ambient (K = 0.5 to -0.5)
            - > 40 deg/s: Weak to strong ambient (K = -0.5 to -1.5)

        Args:
            velocity: Array of velocity values (degrees/second).
            timestamps: Optional timestamps for samples.

        Returns:
            KCoefficientResult with estimated K-coefficient.
        """
        velocity = np.asarray(velocity)
        valid_velocity = velocity[~np.isnan(velocity)]

        if len(valid_velocity) == 0:
            return KCoefficientResult(
                k_coefficient=0.0,
                attention_type=AttentionType.UNKNOWN,
                mean_fixation_duration=0.0,
                mean_saccade_amplitude=0.0,
                n_fixations=0,
                n_saccades=0,
            )

        # Calculate mean velocity
        mean_velocity = np.mean(valid_velocity)

        # Map velocity to K-coefficient
        if mean_velocity < 15:
            # 5-15 deg/s → 1.5 to 0.5 (focal)
            t = np.clip((mean_velocity - 5) / 10, 0, 1)
            k = 1.5 - t * 1.0  # 1.5 to 0.5
        elif mean_velocity < 40:
            # 15-40 deg/s → 0.5 to -0.5 (transition)
            t = (mean_velocity - 15) / 25
            k = 0.5 - t * 1.0  # 0.5 to -0.5
        else:
            # 40-100 deg/s → -0.5 to -1.5 (ambient)
            t = np.clip((mean_velocity - 40) / 60, 0, 1)
            k = -0.5 - t * 1.0  # -0.5 to -1.5

        attention_type = self._classify_attention(k)

        return KCoefficientResult(
            k_coefficient=k,
            attention_type=attention_type,
            mean_fixation_duration=0.0,
            mean_saccade_amplitude=mean_velocity,  # Using mean velocity as proxy
            n_fixations=0,
            n_saccades=0,
            window_start=timestamps[0] if timestamps is not None and len(timestamps) > 0 else None,
            window_end=timestamps[-1] if timestamps is not None and len(timestamps) > 0 else None,
        )

    def _classify_attention(self, k: float) -> AttentionType:
        """Classify attention type based on K-coefficient value."""
        if np.isnan(k):
            return AttentionType.UNKNOWN
        elif k > self.focal_threshold:
            return AttentionType.FOCAL
        elif k < -self.ambient_threshold:
            return AttentionType.AMBIENT
        else:
            return AttentionType.NEUTRAL


def calculate_k_coefficient(fixations: List[Fixation],
                            saccades: List[Saccade],
                            focal_threshold: float = 0.5,
                            ambient_threshold: float = 0.5) -> KCoefficientResult:
    """
    Convenience function to calculate K-coefficient.

    Args:
        fixations: List of detected fixations.
        saccades: List of detected saccades.
        focal_threshold: Threshold for focal classification.
        ambient_threshold: Threshold for ambient classification.

    Returns:
        KCoefficientResult with K-coefficient and classification.
    """
    calculator = KCoefficientCalculator(
        focal_threshold=focal_threshold,
        ambient_threshold=ambient_threshold,
    )
    return calculator.calculate(fixations, saccades)


def classify_attention(k_coefficient: Union[float, KCoefficientResult],
                       focal_threshold: float = 0.5,
                       ambient_threshold: float = 0.5) -> AttentionType:
    """
    Classify attention type from a K-coefficient value or result object.

    Args:
        k_coefficient: Either a float K-coefficient value, or a KCoefficientResult
                      object (from calculate_k_coefficient()). If a KCoefficientResult
                      is passed, the k_coefficient field will be extracted automatically.
        focal_threshold: Threshold for focal classification.
        ambient_threshold: Threshold for ambient classification.

    Returns:
        AttentionType enum value.

    Example:
        >>> result = calculate_k_coefficient(fixations, saccades)
        >>> attention = classify_attention(result)  # Pass result object directly
        >>> # OR
        >>> attention = classify_attention(result.k_coefficient)  # Pass float
    """
    # Handle KCoefficientResult input
    if isinstance(k_coefficient, KCoefficientResult):
        k_value = k_coefficient.k_coefficient
    else:
        k_value = float(k_coefficient)

    if np.isnan(k_value):
        return AttentionType.UNKNOWN
    elif k_value > focal_threshold:
        return AttentionType.FOCAL
    elif k_value < -ambient_threshold:
        return AttentionType.AMBIENT
    else:
        return AttentionType.NEUTRAL


def k_coefficient_timeseries(gaze_x: np.ndarray,
                             gaze_y: np.ndarray,
                             timestamps: np.ndarray,
                             window_duration: float = 5.0,
                             step_size: float = 1.0) -> Tuple[np.ndarray, np.ndarray]:
    """
    Compute K-coefficient time series over sliding windows.

    Args:
        gaze_x: X coordinates.
        gaze_y: Y coordinates.
        timestamps: Timestamps.
        window_duration: Window size in seconds.
        step_size: Step between windows in seconds.

    Returns:
        Tuple of (window_centers, k_values).
    """
    calculator = KCoefficientCalculator()
    results = calculator.calculate_windowed(
        gaze_x, gaze_y, timestamps,
        window_duration=window_duration,
        step_size=step_size,
    )

    if not results:
        return np.array([]), np.array([])

    centers = np.array([
        (r.window_start + r.window_end) / 2 for r in results
    ])
    k_values = np.array([r.k_coefficient for r in results])

    return centers, k_values
