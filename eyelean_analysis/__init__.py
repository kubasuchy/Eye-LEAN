"""
Eye_lean Analysis Toolkit
=========================

A comprehensive Python package for analyzing VR eye tracking data
collected with the Eye_lean Unity toolkit.

Features:
- Flexible CSV loading with column alias mapping
- Signal processing filters (Butterworth, Savitzky-Golay)
- Eye movement classification (fixations, saccades)
- Attention metrics (K-coefficient, LHIPA, entropy)
- Batch processing for multi-file experiments

Usage:
    from eyelean_analysis import load_eyetracking, calculate_lhipa, classify_fixations

    # Load data
    data = load_eyetracking("experiment_data.csv")

    # Get pupil data and analyze
    pupil = data.get_pupil_data()
    lhipa = calculate_lhipa(pupil['left_pupil'].values, data.get_sample_rate())

    # Detect fixations and saccades
    gaze = data.compute_gaze_points(distance=1.0)
    movements = detect_eye_movements(gaze['gaze_x'], gaze['gaze_z'], data.get_timestamps())

References:
    - Duchowski et al. (2018). "The Index of Pupillary Activity". CHI '18.
    - Krejtz et al. (2016). "Eye tracking cognitive load". ETRA '16.
    - Salvucci & Goldberg (2000). "Identifying fixations and saccades". ETRA '00.
"""

__version__ = "1.0.0"
__author__ = "Eye_lean Project"

# Data loading
from .data.loader import (
    EyeLeanLoader,
    EyeLeanData,
    COLUMN_ALIASES,
    load_eyetracking,
    read_csv_metadata,
    load_scene_state,
    load_scene_events,
    load_scene_sidecars,
    merge_gaze_with_scene_state,
)
from .data.validator import DataValidator, ValidationResult, validate_file

# Filters
from .filters.butterworth import ButterworthFilter, butterworth_filter
from .filters.savitzky_golay import SavitzkyGolayFilter, savgol_smooth, compute_gaze_velocity

# Classification
from .classification.velocity_classifier import (
    VelocityClassifier,
    Fixation,
    Saccade,
    classify_fixations,
    classify_saccades,
    detect_eye_movements,
)
from .classification.k_coefficient import (
    KCoefficientCalculator,
    KCoefficientResult,
    AttentionType,
    calculate_k_coefficient,
    classify_attention,
    k_coefficient_timeseries,
)

# Metrics
from .metrics.lhipa import calculate_lhipa, LHIPAResult
from .metrics.entropy import calculate_gaze_entropy, EntropyResult
from .metrics.data_quality import calculate_quality_metrics, QualityMetrics

# Batch processing
from .batch.processor import BatchProcessor, process_batch, process_directory_batch

# Experiments
from .experiments import (
    SAMPLE_EXPERIMENT_PHASES,
    PhaseReport,
    SampleExperimentReport,
    analyze_sample_experiment,
)

# Calibration (post-hoc profile correction)
from .calibration import (
    EyeTrackingProfile,
    apply_profile_to_csv,
    apply_combined_correction,
    load_profile,
)

# Notebook bootstrap
from .notebook_context import NotebookContext, notebook_context

# Visualization
from .visualization.plots import (
    create_heatmap,
    create_trajectory_plot,
    create_timeseries_plot,
    create_fixation_plot,
    create_pupil_plot,
    gaze_heatmap_2d,
    gaze_heatmap_3d_projections,
    aoi_heatmap,
    list_gazed_objects,
)

__all__ = [
    # Version
    "__version__",

    # Data loading
    "EyeLeanLoader",
    "EyeLeanData",
    "COLUMN_ALIASES",
    "load_eyetracking",
    "read_csv_metadata",
    "load_scene_state",
    "load_scene_events",
    "load_scene_sidecars",
    "merge_gaze_with_scene_state",
    "DataValidator",
    "ValidationResult",
    "validate_file",

    # Filters
    "ButterworthFilter",
    "butterworth_filter",
    "SavitzkyGolayFilter",
    "savgol_smooth",
    "compute_gaze_velocity",

    # Classification
    "VelocityClassifier",
    "Fixation",
    "Saccade",
    "classify_fixations",
    "classify_saccades",
    "detect_eye_movements",
    "KCoefficientCalculator",
    "KCoefficientResult",
    "AttentionType",
    "calculate_k_coefficient",
    "classify_attention",
    "k_coefficient_timeseries",

    # Experiments
    "SAMPLE_EXPERIMENT_PHASES",
    "PhaseReport",
    "SampleExperimentReport",
    "analyze_sample_experiment",

    # Calibration (post-hoc profile correction)
    "EyeTrackingProfile",
    "apply_profile_to_csv",
    "apply_combined_correction",
    "load_profile",

    # Metrics
    "calculate_lhipa",
    "LHIPAResult",
    "calculate_gaze_entropy",
    "EntropyResult",
    "calculate_quality_metrics",
    "QualityMetrics",

    # Batch
    "BatchProcessor",
    "process_batch",
    "process_directory_batch",

    # Notebook bootstrap
    "NotebookContext",
    "notebook_context",

    # Visualization
    "create_heatmap",
    "create_trajectory_plot",
    "create_timeseries_plot",
    "create_fixation_plot",
    "create_pupil_plot",
    "gaze_heatmap_2d",
    "gaze_heatmap_3d_projections",
    "aoi_heatmap",
    "list_gazed_objects",
]
