"""
Data quality validation for Eye_lean eye tracking files.

Provides checks for data completeness, validity, and quality metrics.
"""

import pandas as pd
import numpy as np
from typing import Dict, List, Optional, Tuple, Union
from dataclasses import dataclass, field
from pathlib import Path

from .loader import EyeLeanData, COLUMN_ALIASES


@dataclass
class ValidationResult:
    """Container for validation results."""
    is_valid: bool
    errors: List[str] = field(default_factory=list)
    warnings: List[str] = field(default_factory=list)
    metrics: Dict = field(default_factory=dict)

    def add_error(self, message: str):
        """Add an error and mark as invalid."""
        self.errors.append(message)
        self.is_valid = False

    def add_warning(self, message: str):
        """Add a warning (doesn't affect validity)."""
        self.warnings.append(message)

    def __str__(self) -> str:
        status = "VALID" if self.is_valid else "INVALID"
        lines = [f"Validation Result: {status}"]

        if self.errors:
            lines.append("\nErrors:")
            for err in self.errors:
                lines.append(f"  - {err}")

        if self.warnings:
            lines.append("\nWarnings:")
            for warn in self.warnings:
                lines.append(f"  - {warn}")

        if self.metrics:
            lines.append("\nMetrics:")
            for key, val in self.metrics.items():
                if isinstance(val, float):
                    lines.append(f"  {key}: {val:.2f}")
                else:
                    lines.append(f"  {key}: {val}")

        return "\n".join(lines)


class DataValidator:
    """
    Validator for Eye_lean eye tracking data.

    Performs various quality checks and returns a ValidationResult.
    """

    # Default required columns for basic eye tracking
    REQUIRED_COLUMNS = [
        'timestamp',
        'combined_dir_x',
        'combined_dir_y',
        'combined_dir_z',
    ]

    # Columns required for pupillometry
    PUPIL_COLUMNS = [
        'left_pupil_diameter',
        'right_pupil_diameter',
    ]

    # Reasonable ranges for validation
    VALID_RANGES = {
        'pupil_diameter': (1.0, 10.0),  # mm
        'eye_openness': (0.0, 1.0),
        'direction_magnitude': (0.9, 1.1),  # should be ~1.0 (normalized)
        'vergence_quality': (0.0, 1.0),
    }

    def __init__(self,
                 min_validity_percent: float = 70.0,
                 min_duration_seconds: float = 1.0,
                 max_gap_seconds: float = 0.5):
        """
        Initialize the validator.

        Args:
            min_validity_percent: Minimum percentage of valid samples required.
            min_duration_seconds: Minimum recording duration in seconds.
            max_gap_seconds: Maximum allowed gap between samples in seconds.
        """
        self.min_validity_percent = min_validity_percent
        self.min_duration_seconds = min_duration_seconds
        self.max_gap_seconds = max_gap_seconds

    def validate(self,
                 data: Union[EyeLeanData, pd.DataFrame],
                 check_pupil: bool = False,
                 check_vergence: bool = False) -> ValidationResult:
        """
        Perform comprehensive validation on eye tracking data.

        Args:
            data: EyeLeanData object or DataFrame to validate.
            check_pupil: If True, also validate pupil data requirements.
            check_vergence: If True, also validate vergence data requirements.

        Returns:
            ValidationResult with errors, warnings, and metrics.
        """
        result = ValidationResult(is_valid=True)

        # Convert DataFrame to EyeLeanData if needed
        if isinstance(data, pd.DataFrame):
            df = data
        else:
            df = data.df

        # Basic checks
        self._check_not_empty(df, result)
        if not result.is_valid:
            return result

        self._check_required_columns(df, result)
        self._check_duration(df, result)
        self._check_sampling_consistency(df, result)
        self._check_validity_percentage(df, result)
        self._check_direction_vectors(df, result)

        if check_pupil:
            self._check_pupil_data(df, result)

        if check_vergence:
            self._check_vergence_data(df, result)

        return result

    def _check_not_empty(self, df: pd.DataFrame, result: ValidationResult):
        """Check that data is not empty."""
        if len(df) == 0:
            result.add_error("Data is empty (0 rows)")
            return

        result.metrics['n_samples'] = len(df)

        if len(df) < 10:
            result.add_warning(f"Very few samples ({len(df)} rows)")

    def _check_required_columns(self, df: pd.DataFrame, result: ValidationResult):
        """Check that required columns are present."""
        missing = []

        for col in self.REQUIRED_COLUMNS:
            # Check both canonical and original names
            col_found = col in df.columns
            if not col_found:
                # Check aliases
                aliases = COLUMN_ALIASES.get(col, [])
                col_found = any(alias in df.columns for alias in aliases)

            if not col_found:
                missing.append(col)

        if missing:
            result.add_error(f"Missing required columns: {', '.join(missing)}")

    def _check_duration(self, df: pd.DataFrame, result: ValidationResult):
        """Check recording duration."""
        timestamp_col = self._find_column(df, 'timestamp')
        if timestamp_col is None:
            return

        timestamps = df[timestamp_col].values
        if len(timestamps) < 2:
            return

        duration = timestamps[-1] - timestamps[0]
        result.metrics['duration_seconds'] = float(duration)

        if duration < self.min_duration_seconds:
            result.add_error(
                f"Recording too short: {duration:.2f}s (minimum: {self.min_duration_seconds}s)"
            )

    def _check_sampling_consistency(self, df: pd.DataFrame, result: ValidationResult):
        """Check for gaps and irregular sampling."""
        timestamp_col = self._find_column(df, 'timestamp')
        if timestamp_col is None:
            return

        timestamps = df[timestamp_col].values
        if len(timestamps) < 2:
            return

        # Calculate sample intervals
        intervals = np.diff(timestamps)

        # Estimate sample rate
        median_interval = np.median(intervals)
        if median_interval > 0:
            sample_rate = 1.0 / median_interval
            result.metrics['sample_rate_hz'] = float(sample_rate)
        else:
            result.add_warning("Cannot determine sample rate (zero interval)")
            return

        # Check for large gaps
        max_gap = np.max(intervals)
        result.metrics['max_gap_seconds'] = float(max_gap)

        if max_gap > self.max_gap_seconds:
            gap_count = np.sum(intervals > self.max_gap_seconds)
            result.add_warning(
                f"Found {gap_count} gap(s) > {self.max_gap_seconds}s (max: {max_gap:.3f}s)"
            )

        # Check interval consistency (coefficient of variation)
        interval_cv = np.std(intervals) / np.mean(intervals) if np.mean(intervals) > 0 else 0
        result.metrics['interval_cv'] = float(interval_cv)

        if interval_cv > 0.5:
            result.add_warning(
                f"Irregular sampling (CV={interval_cv:.2f}). Consider resampling."
            )

    def _check_validity_percentage(self, df: pd.DataFrame, result: ValidationResult):
        """Check percentage of valid samples."""
        validity_col = self._find_column(df, 'is_tracking_valid')
        if validity_col is None:
            result.add_warning("No validity flag column found. Cannot check data quality.")
            return

        valid_count = df[validity_col].sum()
        valid_percent = (valid_count / len(df)) * 100

        result.metrics['valid_samples'] = int(valid_count)
        result.metrics['valid_percent'] = float(valid_percent)

        if valid_percent < self.min_validity_percent:
            result.add_error(
                f"Low validity: {valid_percent:.1f}% valid samples "
                f"(minimum: {self.min_validity_percent}%)"
            )
        elif valid_percent < 90:
            result.add_warning(f"Validity below 90%: {valid_percent:.1f}%")

    def _check_direction_vectors(self, df: pd.DataFrame, result: ValidationResult):
        """Check that direction vectors are normalized."""
        dir_x = self._find_column(df, 'combined_dir_x')
        dir_y = self._find_column(df, 'combined_dir_y')
        dir_z = self._find_column(df, 'combined_dir_z')

        if not all([dir_x, dir_y, dir_z]):
            return

        # Calculate magnitude
        magnitudes = np.sqrt(
            df[dir_x]**2 + df[dir_y]**2 + df[dir_z]**2
        )

        # Filter out invalid samples
        validity_col = self._find_column(df, 'has_combined_direction')
        if validity_col:
            valid_magnitudes = magnitudes[df[validity_col] == True]
        else:
            valid_magnitudes = magnitudes.dropna()

        if len(valid_magnitudes) == 0:
            result.add_warning("No valid direction vectors to check")
            return

        mean_mag = valid_magnitudes.mean()
        result.metrics['direction_magnitude_mean'] = float(mean_mag)

        min_mag, max_mag = self.VALID_RANGES['direction_magnitude']
        if mean_mag < min_mag or mean_mag > max_mag:
            result.add_warning(
                f"Direction vectors may not be normalized (mean magnitude: {mean_mag:.3f})"
            )

    def _check_pupil_data(self, df: pd.DataFrame, result: ValidationResult):
        """Check pupil data quality."""
        left_col = self._find_column(df, 'left_pupil_diameter')
        right_col = self._find_column(df, 'right_pupil_diameter')

        if not left_col and not right_col:
            result.add_error("No pupil diameter columns found")
            return

        min_pupil, max_pupil = self.VALID_RANGES['pupil_diameter']

        for col, name in [(left_col, 'left'), (right_col, 'right')]:
            if col is None:
                result.add_warning(f"No {name} pupil data")
                continue

            values = df[col].dropna()
            if len(values) == 0:
                result.add_warning(f"No valid {name} pupil measurements")
                continue

            mean_val = values.mean()
            std_val = values.std()
            result.metrics[f'{name}_pupil_mean'] = float(mean_val)
            result.metrics[f'{name}_pupil_std'] = float(std_val)

            # Range check
            out_of_range = ((values < min_pupil) | (values > max_pupil)).sum()
            if out_of_range > 0:
                pct = (out_of_range / len(values)) * 100
                result.add_warning(
                    f"{pct:.1f}% of {name} pupil values outside valid range "
                    f"({min_pupil}-{max_pupil}mm)"
                )

    def _check_vergence_data(self, df: pd.DataFrame, result: ValidationResult):
        """Check vergence data quality."""
        vergence_cols = [
            self._find_column(df, 'vergence_point_x'),
            self._find_column(df, 'vergence_point_y'),
            self._find_column(df, 'vergence_point_z'),
        ]

        if not all(vergence_cols):
            result.add_error("Missing vergence point columns")
            return

        validity_col = self._find_column(df, 'has_valid_vergence')
        if validity_col:
            valid_count = df[validity_col].sum()
            valid_pct = (valid_count / len(df)) * 100
            result.metrics['vergence_valid_percent'] = float(valid_pct)

            if valid_pct < 50:
                result.add_warning(f"Low vergence validity: {valid_pct:.1f}%")

        quality_col = self._find_column(df, 'vergence_quality')
        if quality_col:
            mean_quality = df[quality_col].mean()
            result.metrics['vergence_quality_mean'] = float(mean_quality)

    def _find_column(self, df: pd.DataFrame, canonical_name: str) -> Optional[str]:
        """Find a column by canonical name or its aliases."""
        if canonical_name in df.columns:
            return canonical_name

        aliases = COLUMN_ALIASES.get(canonical_name, [])
        for alias in aliases:
            if alias in df.columns:
                return alias

        return None


def validate_file(file_path: Union[str, Path], **kwargs) -> ValidationResult:
    """
    Convenience function to validate an eye tracking file.

    Args:
        file_path: Path to the CSV file.
        **kwargs: Additional arguments passed to DataValidator.validate().

    Returns:
        ValidationResult object.
    """
    from .loader import load_eyetracking

    data = load_eyetracking(file_path)
    validator = DataValidator()
    return validator.validate(data, **kwargs)


def calculate_quality_score(result: ValidationResult) -> float:
    """
    Calculate an overall quality score from validation results.

    Score ranges from 0.0 (poor) to 1.0 (excellent).

    Args:
        result: ValidationResult from validation.

    Returns:
        Quality score between 0.0 and 1.0.
    """
    if not result.is_valid:
        return 0.0

    score = 1.0

    # Deduct for warnings
    score -= len(result.warnings) * 0.05

    # Weight by validity percentage
    valid_pct = result.metrics.get('valid_percent', 100)
    score *= (valid_pct / 100)

    # Weight by sampling consistency
    interval_cv = result.metrics.get('interval_cv', 0)
    if interval_cv > 0.1:
        score *= max(0.5, 1.0 - interval_cv)

    return max(0.0, min(1.0, score))
