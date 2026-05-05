"""Signal processing filters."""
from .butterworth import (
    ButterworthFilter,
    butterworth_filter,
    estimate_cutoff_from_data,
)
from .savitzky_golay import (
    SavitzkyGolayFilter,
    savgol_smooth,
    savgol_velocity,
    compute_gaze_velocity,
    compute_angular_velocity,
)

__all__ = [
    "ButterworthFilter",
    "butterworth_filter",
    "estimate_cutoff_from_data",
    "SavitzkyGolayFilter",
    "savgol_smooth",
    "savgol_velocity",
    "compute_gaze_velocity",
    "compute_angular_velocity",
]
