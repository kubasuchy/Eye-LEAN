"""Eye tracking metrics and analysis."""
from .lhipa import (
    LHIPACalculator,
    LHIPAResult,
    calculate_lhipa,
    lhipa_timeseries,
    combine_pupil_eyes,
    calculate_pupil_metrics,
)
from .ripa2 import (
    RIPA2Result,
    calculate_ripa2,
    ripa2_timeseries,
    schaefer_half_width,
    sg_first_derivative_coeffs,
)
from .entropy import (
    GazeEntropyCalculator,
    EntropyResult,
    calculate_gaze_entropy,
    entropy_timeseries,
    stationary_entropy,
    transition_entropy,
)
from .data_quality import (
    QualityMetrics,
    calculate_quality_metrics,
    detect_tracking_loss,
    calculate_precision,
)

__all__ = [
    # LHIPA (offline reference; v1.2 metric — kept for paper comparison)
    "LHIPACalculator",
    "LHIPAResult",
    "calculate_lhipa",
    "lhipa_timeseries",
    "combine_pupil_eyes",
    "calculate_pupil_metrics",
    # RIPA2 (real-time metric, v1.3+; matches Unity-side LiveLoadIndex)
    "RIPA2Result",
    "calculate_ripa2",
    "ripa2_timeseries",
    "schaefer_half_width",
    "sg_first_derivative_coeffs",
    # Entropy
    "GazeEntropyCalculator",
    "EntropyResult",
    "calculate_gaze_entropy",
    "entropy_timeseries",
    "stationary_entropy",
    "transition_entropy",
    # Data quality
    "QualityMetrics",
    "calculate_quality_metrics",
    "detect_tracking_loss",
    "calculate_precision",
]
