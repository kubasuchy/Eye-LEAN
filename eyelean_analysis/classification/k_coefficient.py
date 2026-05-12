"""
K-coefficient for ambient/focal attention classification.

Per Krejtz et al. (2016, PLoS ONE Eq. 1), the K-coefficient is a
**difference of z-scores** computed per fixation/saccade pair:

    K_i = z(a_i) − z(d_i)
        = (a_i − μ_a) / σ_a  −  (d_i − μ_d) / σ_d
    K   = mean_i(K_i)

where d_i is the duration of fixation i and a_i is the amplitude of the
saccade following fixation i. Sign convention follows the paper:
**K > 0 ⇒ focal** (long fixations following short saccades),
**K < 0 ⇒ ambient** (short fixations following long saccades). Magnitude
is in standard-deviation units; classification is sign-based, not
threshold-based.

Reference:
    Krejtz, K., Duchowski, A. T., Niber, T., Krejtz, I., & Kopacz, A. (2016).
    Eye tracking cognitive load using pupil diameter and microsaccades with
    fixed gaze. PLoS ONE, 11(9), e0163087.
    https://doi.org/10.1371/journal.pone.0163087

    Krejtz, K., Duchowski, A., Krejtz, I., Kopacz, A., & Chrząstowski-Wachtel, P. (2016).
    Discerning ambient/focal attention with coefficient K. ACM Trans.
    Applied Perception, 13(3), 11:1-11:20. https://doi.org/10.1145/2896452
"""

import warnings

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
    Calculator for the K-coefficient attention metric (Krejtz et al. 2016).

    Per Eq. 1 of Krejtz, Duchowski, Niber, Krejtz, & Kopacz (2016, PLoS ONE):

        K_i = (a_i − μ_a) / σ_a  −  (d_i − μ_d) / σ_d
        K   = mean_i(K_i)

    where the i-th pair couples fixation i (duration d_i) with the
    saccade that immediately follows it (amplitude a_i). K is in
    standard-deviation units; classification is **sign-based** —

      - K > 0 ⇒ focal   (long fixations after short saccades)
      - K < 0 ⇒ ambient (short fixations after long saccades)

    No fixed magnitude threshold (e.g. 0.5) is canonical; the optional
    `dead_zone` argument lets callers report values within ±dead_zone of
    zero as `Neutral` for visualisation purposes only — it is not part
    of Krejtz's classification.

    Example:
        >>> calculator = KCoefficientCalculator()
        >>> result = calculator.calculate(fixations, saccades)
        >>> print(f"K={result.k_coefficient:.2f}, Attention: {result.attention_type}")
    """

    def __init__(self,
                 dead_zone: float = 0.0,
                 velocity_threshold: float = 50.0,
                 min_fixation_duration: float = 0.1,
                 window_size: int = 30,
                 # Deprecated aliases retained for back-compat with v1.0.x;
                 # if either is set non-None it is mapped onto `dead_zone`.
                 focal_threshold: Optional[float] = None,
                 ambient_threshold: Optional[float] = None):
        """
        Initialize the K-coefficient calculator.

        Args:
            dead_zone: Visualization-only neutral band. Values with
                |K| < dead_zone are reported as Neutral. Krejtz's paper
                uses sign-only classification (dead_zone = 0).
            velocity_threshold: Velocity threshold for fixation/saccade detection.
            min_fixation_duration: Minimum fixation duration in seconds.
            window_size: Number of events to use for calculation.
            focal_threshold, ambient_threshold: deprecated aliases for
                `dead_zone`. Pass `dead_zone=` instead. Krejtz 2016
                classification is sign-based (`dead_zone=0`); any
                magnitude threshold is a visualization-only neutral
                band.
        """
        if focal_threshold is not None or ambient_threshold is not None:
            warnings.warn(
                "focal_threshold/ambient_threshold are deprecated in favour of "
                "`dead_zone` (sign-based classification per Krejtz 2016).",
                DeprecationWarning,
                stacklevel=2,
            )
            chosen = focal_threshold if focal_threshold is not None else ambient_threshold
            dead_zone = float(chosen) if chosen is not None else dead_zone
        self.dead_zone = float(dead_zone)
        # Back-compat aliases — kept on the instance so old code still
        # reading `.focal_threshold` doesn't AttributeError. They mirror
        # `dead_zone` and are equal by construction (sign-based fit).
        self.focal_threshold = self.dead_zone
        self.ambient_threshold = self.dead_zone
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
                  saccades: List[Saccade],
                  pooled_stats: Optional[Tuple[float, float, float, float]] = None
                  ) -> KCoefficientResult:
        """
        Calculate K-coefficient from fixation and saccade lists per
        Krejtz et al. (2016, PLoS ONE) Eq. 1.

        Pairing convention: the i-th fixation is paired with the
        saccade that immediately follows it. With n fixations and m
        saccades the number of valid pairs is min(n, m) — any trailing
        unpaired fixation or leading unpaired saccade is dropped.

        **IMPORTANT — pooled vs local statistics.** Krejtz's K-coefficient
        is a difference of z-scores. If μ, σ for the z-scores are
        computed over the SAME pairs that K is averaged over, the result
        is mathematically identically zero (mean of z-scores is zero by
        construction). This is why the canonical paper reports K
        per-stimulus / per-condition / per-window using
        recording-global μ, σ across ALL fixations and saccades.

        Two usage modes:

        - **Local stats (default):** call `calculate(fixations, saccades)`
          on a subset; μ, σ are taken from that subset, and K returns
          identically zero. This is only useful if you intend to compare
          something else about the result (e.g., the per-pair K_i array
          via `pooled_stats=(...)` below).
        - **Pooled stats (recommended):** pass session-global stats via
          ``pooled_stats=(mean_d, std_d, mean_a, std_a)``. K is then
          computed with z-scores against the pooled reference and the
          result is non-zero whenever the passed subset is unbalanced
          relative to the pooled distribution. Use
          :meth:`calculate_per_window` for the standard time-windowed
          K(t) reporting in Krejtz 2017.

        Args:
            fixations: List of detected fixations, in time order.
            saccades:  List of detected saccades, in time order. The
                       i-th saccade is assumed to follow fixation i.
            pooled_stats: Optional ``(mean_d, std_d, mean_a, std_a)``
                       tuple from a broader pooling reference. When
                       provided, z-scores use these instead of the
                       within-subset stats.

        Returns:
            KCoefficientResult with K-coefficient and classification.
        """
        # Need >=2 pairs so std (with ddof=1) is defined and the metric
        # has any statistical meaning.
        n_pairs = min(len(fixations), len(saccades))
        if n_pairs < 2:
            return KCoefficientResult(
                k_coefficient=0.0,
                attention_type=AttentionType.UNKNOWN,
                mean_fixation_duration=0.0,
                mean_saccade_amplitude=0.0,
                n_fixations=len(fixations),
                n_saccades=len(saccades),
            )

        durations  = np.asarray([f.duration  for f in fixations[:n_pairs]], dtype=np.float64)
        amplitudes = np.asarray([s.amplitude for s in saccades[:n_pairs]],  dtype=np.float64)

        if pooled_stats is not None:
            mean_duration, std_duration, mean_amplitude, std_amplitude = (
                float(x) for x in pooled_stats
            )
            local_stats = False
        else:
            mean_duration  = float(durations.mean())
            mean_amplitude = float(amplitudes.mean())
            std_duration   = float(durations.std(ddof=1))
            std_amplitude  = float(amplitudes.std(ddof=1))
            local_stats = True
            warnings.warn(
                "K-coefficient computed without pooled_stats: the mean of "
                "z-scores over the same set used to estimate (μ, σ) is "
                "identically zero (mod floating-point). The returned K is "
                "not interpretable. Pass `pooled_stats=(mean_d, std_d, "
                "mean_a, std_a)` from a session-global reference, or use "
                "`calculate_per_window()`. See Krejtz 2016 Eq. 1 and the "
                "module docstring.",
                UserWarning,
                stacklevel=2,
            )

        # If either spread is degenerate (constant duration or constant
        # amplitude across all pairs) the z-score is undefined. Krejtz
        # says nothing about this corner — return Unknown rather than
        # silently producing inf/nan or a misleading zero.
        if std_duration <= 0.0 or std_amplitude <= 0.0:
            return KCoefficientResult(
                k_coefficient=0.0,
                attention_type=AttentionType.UNKNOWN,
                mean_fixation_duration=mean_duration,
                mean_saccade_amplitude=mean_amplitude,
                n_fixations=len(fixations),
                n_saccades=len(saccades),
            )

        # Eq. 1: K_i = z(a_i) − z(d_i); K = mean_i(K_i).
        z_d = (durations  - mean_duration)  / std_duration
        z_a = (amplitudes - mean_amplitude) / std_amplitude
        k_values = z_a - z_d
        k_coefficient = float(np.nanmean(k_values))

        # K computed against local stats is zero by construction; the sign
        # is floating-point noise and must not be used to classify.
        if local_stats:
            attention_type = AttentionType.UNKNOWN
        else:
            attention_type = self._classify_attention(k_coefficient)

        return KCoefficientResult(
            k_coefficient=k_coefficient,
            attention_type=attention_type,
            mean_fixation_duration=mean_duration,
            mean_saccade_amplitude=mean_amplitude,
            n_fixations=len(fixations),
            n_saccades=len(saccades),
        )

    def calculate_per_window(self,
                             fixations: List[Fixation],
                             saccades: List[Saccade],
                             window_seconds: float = 5.0,
                             step_seconds: float = 1.0,
                             ) -> List[KCoefficientResult]:
        """Sliding-window K(t) using session-global pooled μ, σ.

        This is the canonical Krejtz usage pattern: pool fixation/saccade
        statistics over the entire passed session, then for each
        ``window_seconds``-long window step the time index by
        ``step_seconds`` and compute the mean of (z_a − z_d) over the
        pairs whose fixation start-time falls in that window. Sliding
        windows yield non-zero K values whose sign indicates focal
        (positive) or ambient (negative) attention relative to the
        session baseline.

        Args:
            fixations: All session fixations, time-ordered.
            saccades:  All session saccades, time-ordered. Pair i =
                       (fixations[i], saccades[i]).
            window_seconds: K averaging window in seconds (Krejtz 2017
                       uses ~5 s for the dynamic K(t) plot).
            step_seconds:  Step between adjacent windows.

        Returns:
            List of KCoefficientResult, one per window, with
            ``window_start`` / ``window_end`` populated.
        """
        n_pairs = min(len(fixations), len(saccades))
        if n_pairs < 2:
            return []

        durations  = np.asarray([f.duration  for f in fixations[:n_pairs]], dtype=np.float64)
        amplitudes = np.asarray([s.amplitude for s in saccades[:n_pairs]],  dtype=np.float64)
        starts     = np.asarray([f.start_time for f in fixations[:n_pairs]], dtype=np.float64)

        mean_d = float(durations.mean());  std_d = float(durations.std(ddof=1))
        mean_a = float(amplitudes.mean()); std_a = float(amplitudes.std(ddof=1))
        if std_d <= 0.0 or std_a <= 0.0:
            return []
        pooled = (mean_d, std_d, mean_a, std_a)

        t0, t1 = float(starts.min()), float(starts.max())
        results: List[KCoefficientResult] = []
        w_start = t0
        while w_start + window_seconds <= t1 + 1e-9:
            w_end = w_start + window_seconds
            mask = (starts >= w_start) & (starts < w_end)
            if int(mask.sum()) >= 2:
                fix_w = [fixations[i] for i in range(n_pairs) if mask[i]]
                sac_w = [saccades[i]  for i in range(n_pairs) if mask[i]]
                r = self.calculate(fix_w, sac_w, pooled_stats=pooled)
                r.window_start = w_start
                r.window_end   = w_end
                results.append(r)
            w_start += step_seconds
        return results

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

    def velocity_based_attention_proxy(self,
                                       velocity: np.ndarray,
                                       timestamps: Optional[np.ndarray] = None) -> KCoefficientResult:
        """
        Velocity-based attention PROXY — NOT the Krejtz K-coefficient.

        Maps mean angular velocity to a focal/ambient indicator using
        hard-coded velocity bands. Useful for real-time visualization
        when fixation/saccade events have not yet been segmented, but
        do not report this as "K-coefficient" — Krejtz K is defined
        only over paired fixation/saccade events.

        Velocity mapping (proxy, not from any paper):
            - < 15 deg/s: Strong to weak focal  (proxy 1.5 to 0.5)
            - 15-40 deg/s: Weak focal to weak ambient (proxy 0.5 to -0.5)
            - > 40 deg/s: Weak to strong ambient (proxy -0.5 to -1.5)

        Args:
            velocity: Array of velocity values (degrees/second).
            timestamps: Optional timestamps for samples.

        Returns:
            KCoefficientResult with the proxy value placed in
            `k_coefficient`. Treat as a categorical attention indicator,
            not a quantitative K.
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

    def calculate_velocity_based(self,
                                 velocity: np.ndarray,
                                 timestamps: Optional[np.ndarray] = None) -> KCoefficientResult:
        """Deprecated alias for `velocity_based_attention_proxy`. The
        original name implied this was the K-coefficient, which it is
        not — see Krejtz 2016 Eq. 1 vs the velocity-band heuristic in
        the renamed method's docstring."""
        warnings.warn(
            "calculate_velocity_based is renamed to velocity_based_attention_proxy "
            "to clarify that the velocity-band mapping is NOT the Krejtz "
            "K-coefficient. This alias will be removed in v1.2.",
            DeprecationWarning,
            stacklevel=2,
        )
        return self.velocity_based_attention_proxy(velocity, timestamps)

    def _classify_attention(self, k: float) -> AttentionType:
        """Classify attention type based on K-coefficient sign.

        Krejtz 2016 classifies by sign; the optional `dead_zone` widens
        the neutral band purely for visualization. Default dead_zone=0
        ⇒ pure sign classification.
        """
        if np.isnan(k):
            return AttentionType.UNKNOWN
        if k > self.dead_zone:
            return AttentionType.FOCAL
        if k < -self.dead_zone:
            return AttentionType.AMBIENT
        return AttentionType.NEUTRAL


def calculate_k_coefficient(fixations: List[Fixation],
                            saccades: List[Saccade],
                            dead_zone: float = 0.0) -> KCoefficientResult:
    """
    Convenience function to calculate K-coefficient per Krejtz 2016.

    Args:
        fixations: List of detected fixations, in time order.
        saccades:  List of detected saccades, in time order. Pairing
                   convention: saccade i follows fixation i.
        dead_zone: Visualization-only neutral band (|K| < dead_zone
                   ⇒ Neutral). Krejtz uses sign-based classification
                   (dead_zone = 0).

    Returns:
        KCoefficientResult with K-coefficient and classification.
    """
    calculator = KCoefficientCalculator(dead_zone=dead_zone)
    return calculator.calculate(fixations, saccades)


def classify_attention(k_coefficient: Union[float, KCoefficientResult],
                       dead_zone: float = 0.0) -> AttentionType:
    """
    Classify attention type from a K-coefficient value or result object.

    Per Krejtz 2016 the classification is **sign-based**: K > 0 ⇒ focal,
    K < 0 ⇒ ambient. The `dead_zone` argument widens the neutral band
    purely for visualization and is not part of Krejtz's definition.

    Args:
        k_coefficient: Either a float K-coefficient value, or a
                      KCoefficientResult object (from
                      calculate_k_coefficient()). If a KCoefficientResult
                      is passed, the `k_coefficient` field will be
                      extracted automatically.
        dead_zone: Half-width of the neutral band around 0; default 0
                   (pure sign classification).

    Returns:
        AttentionType enum value.

    Example:
        >>> result = calculate_k_coefficient(fixations, saccades)
        >>> attention = classify_attention(result)
        >>> attention = classify_attention(result.k_coefficient)
    """
    if isinstance(k_coefficient, KCoefficientResult):
        k_value = k_coefficient.k_coefficient
    else:
        k_value = float(k_coefficient)

    if np.isnan(k_value):
        return AttentionType.UNKNOWN
    if k_value > dead_zone:
        return AttentionType.FOCAL
    if k_value < -dead_zone:
        return AttentionType.AMBIENT
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
