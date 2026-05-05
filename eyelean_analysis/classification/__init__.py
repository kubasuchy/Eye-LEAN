"""Eye movement classification algorithms."""
from .velocity_classifier import (
    VelocityClassifier,
    EyeMovementType,
    Fixation,
    Saccade,
    classify_fixations,
    classify_saccades,
    detect_eye_movements,
    fixations_to_dataframe,
    saccades_to_dataframe,
)
from .k_coefficient import (
    KCoefficientCalculator,
    AttentionType,
    KCoefficientResult,
    calculate_k_coefficient,
    classify_attention,
    k_coefficient_timeseries,
)

__all__ = [
    # Velocity classifier
    "VelocityClassifier",
    "EyeMovementType",
    "Fixation",
    "Saccade",
    "classify_fixations",
    "classify_saccades",
    "detect_eye_movements",
    "fixations_to_dataframe",
    "saccades_to_dataframe",
    # K-coefficient
    "KCoefficientCalculator",
    "AttentionType",
    "KCoefficientResult",
    "calculate_k_coefficient",
    "classify_attention",
    "k_coefficient_timeseries",
]
