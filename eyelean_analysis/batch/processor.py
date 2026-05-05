"""
Batch processing utilities for multi-file eye tracking analysis.

Provides parallel processing of multiple eye tracking files with
configurable analysis pipelines.
"""

import numpy as np
import pandas as pd
from pathlib import Path
from typing import Dict, List, Optional, Callable, Union, Any
from dataclasses import dataclass, field
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor, as_completed
import warnings

# Optional progress bar dependency
try:
    from tqdm import tqdm
    HAS_TQDM = True
except ImportError:
    HAS_TQDM = False
    tqdm = None  # Will be handled in code
import traceback

from ..data.loader import EyeLeanLoader, EyeLeanData, load_eyetracking
from ..data.validator import DataValidator, ValidationResult
from ..metrics.data_quality import calculate_quality_metrics, QualityMetrics
from ..metrics.lhipa import calculate_lhipa, LHIPAResult
from ..metrics.entropy import calculate_gaze_entropy, EntropyResult
from ..classification.velocity_classifier import detect_eye_movements


@dataclass
class ProcessingResult:
    """Result of processing a single file."""
    file_path: str
    success: bool
    error_message: Optional[str] = None

    # Basic info
    n_samples: int = 0
    duration_seconds: float = 0.0
    sample_rate_hz: float = 0.0

    # Quality metrics
    quality_metrics: Optional[QualityMetrics] = None

    # Analysis results
    lhipa: Optional[float] = None
    entropy: Optional[float] = None
    n_fixations: int = 0
    n_saccades: int = 0
    mean_fixation_duration: float = 0.0
    mean_saccade_amplitude: float = 0.0

    # Custom metrics
    custom_metrics: Dict = field(default_factory=dict)

    def to_dict(self) -> Dict:
        """Convert to dictionary for DataFrame creation."""
        result = {
            'file_path': self.file_path,
            'success': self.success,
            'error_message': self.error_message,
            'n_samples': self.n_samples,
            'duration_seconds': self.duration_seconds,
            'sample_rate_hz': self.sample_rate_hz,
            'lhipa': self.lhipa,
            'entropy': self.entropy,
            'n_fixations': self.n_fixations,
            'n_saccades': self.n_saccades,
            'mean_fixation_duration': self.mean_fixation_duration,
            'mean_saccade_amplitude': self.mean_saccade_amplitude,
        }

        # Add quality metrics
        if self.quality_metrics:
            result['quality_score'] = self.quality_metrics.quality_score
            result['validity_percent'] = self.quality_metrics.validity_percent
            result['n_gaps'] = self.quality_metrics.n_gaps

        # Add custom metrics
        result.update(self.custom_metrics)

        return result


class BatchProcessor:
    """
    Batch processor for multi-file eye tracking analysis.

    Supports parallel processing and configurable analysis pipelines.

    Example:
        >>> processor = BatchProcessor()
        >>> results = processor.process_directory('data/', pattern='*.csv')
        >>> summary_df = processor.results_to_dataframe(results)
        >>> summary_df.to_csv('summary.csv')
    """

    def __init__(self,
                 loader: Optional[EyeLeanLoader] = None,
                 validator: Optional[DataValidator] = None,
                 n_workers: int = 4,
                 use_multiprocessing: bool = True,
                 show_progress: bool = True,
                 velocity_threshold: float = 50.0,
                 compute_lhipa: bool = True,
                 compute_entropy: bool = True,
                 compute_fixations: bool = True):
        """
        Initialize the batch processor.

        Args:
            loader: EyeLeanLoader instance (created if None).
            validator: DataValidator instance (created if None).
            n_workers: Number of parallel workers.
            use_multiprocessing: If True, use processes; else use threads.
            show_progress: If True, show progress bar.
            velocity_threshold: Velocity threshold for fixation/saccade
                classification (deg/s). Default 50 deg/s matches the I-VT
                literature; tighten for high-precision tasks or loosen for
                noisy data.
            compute_lhipa: If True, compute LHIPA per file.
            compute_entropy: If True, compute gaze entropy per file.
            compute_fixations: If True, run fixation/saccade detection.
        """
        self.loader = loader or EyeLeanLoader()
        self.validator = validator or DataValidator()
        self.n_workers = n_workers
        self.use_multiprocessing = use_multiprocessing
        self.show_progress = show_progress

        self.compute_lhipa = compute_lhipa
        self.compute_entropy = compute_entropy
        self.compute_fixations = compute_fixations
        self.velocity_threshold = velocity_threshold

        # Custom analysis functions
        self._custom_analyzers: List[Callable] = []

    def add_analyzer(self, func: Callable[[EyeLeanData], Dict]):
        """
        Add a custom analyzer function.

        Args:
            func: Function that takes EyeLeanData and returns a dict of metrics.

        Example:
            >>> def my_analyzer(data):
            ...     return {'custom_metric': data.df['value'].mean()}
            >>> processor.add_analyzer(my_analyzer)
        """
        self._custom_analyzers.append(func)

    def process_files(self,
                      file_paths: List[Union[str, Path]],
                      validate: bool = True) -> List[ProcessingResult]:
        """
        Process multiple files.

        Args:
            file_paths: List of file paths to process.
            validate: If True, validate data before processing.

        Returns:
            List of ProcessingResult objects.
        """
        file_paths = [str(p) for p in file_paths]

        if len(file_paths) == 0:
            return []

        if len(file_paths) == 1 or self.n_workers <= 1:
            # Sequential processing
            results = []
            iterator = file_paths
            if self.show_progress and HAS_TQDM:
                iterator = tqdm(file_paths, desc="Processing")
            elif self.show_progress and not HAS_TQDM:
                warnings.warn("tqdm not installed. Install with: pip install tqdm")
            for fp in iterator:
                result = self._process_single_file(fp, validate)
                results.append(result)
            return results

        # Parallel processing
        executor_class = ProcessPoolExecutor if self.use_multiprocessing else ThreadPoolExecutor

        results = []
        with executor_class(max_workers=self.n_workers) as executor:
            futures = {
                executor.submit(self._process_single_file, fp, validate): fp
                for fp in file_paths
            }

            iterator = as_completed(futures)
            if self.show_progress and HAS_TQDM:
                iterator = tqdm(iterator, total=len(futures), desc="Processing")
            elif self.show_progress and not HAS_TQDM:
                warnings.warn("tqdm not installed. Install with: pip install tqdm")

            for future in iterator:
                try:
                    result = future.result()
                    results.append(result)
                except Exception as e:
                    fp = futures[future]
                    results.append(ProcessingResult(
                        file_path=fp,
                        success=False,
                        error_message=str(e),
                    ))

        return results

    def process_directory(self,
                          directory: Union[str, Path],
                          pattern: str = "*.csv",
                          recursive: bool = False,
                          validate: bool = True) -> List[ProcessingResult]:
        """
        Process all matching files in a directory.

        Args:
            directory: Directory path.
            pattern: Glob pattern for file matching.
            recursive: If True, search subdirectories.
            validate: If True, validate data before processing.

        Returns:
            List of ProcessingResult objects.
        """
        directory = Path(directory)

        if recursive:
            file_paths = list(directory.rglob(pattern))
        else:
            file_paths = list(directory.glob(pattern))

        if len(file_paths) == 0:
            warnings.warn(f"No files matching '{pattern}' found in {directory}")
            return []

        return self.process_files(file_paths, validate)

    def _process_single_file(self, file_path: str, validate: bool) -> ProcessingResult:
        """Process a single file."""
        try:
            # Load data
            data = self.loader.load(file_path)

            # Basic info
            n_samples = len(data)
            sample_rate = data.get_sample_rate()
            timestamps = data.get_timestamps()
            duration = timestamps[-1] - timestamps[0] if len(timestamps) > 1 else 0.0

            # Validation
            if validate:
                validation = self.validator.validate(data.df)
                if not validation.is_valid:
                    return ProcessingResult(
                        file_path=file_path,
                        success=False,
                        error_message=f"Validation failed: {'; '.join(validation.errors)}",
                        n_samples=n_samples,
                        duration_seconds=duration,
                        sample_rate_hz=sample_rate,
                    )

            # Quality metrics
            quality_metrics = self._compute_quality_metrics(data)

            # Initialize result
            result = ProcessingResult(
                file_path=file_path,
                success=True,
                n_samples=n_samples,
                duration_seconds=duration,
                sample_rate_hz=sample_rate,
                quality_metrics=quality_metrics,
            )

            # LHIPA
            if self.compute_lhipa:
                result.lhipa = self._compute_lhipa(data, sample_rate)

            # Entropy
            if self.compute_entropy:
                result.entropy = self._compute_entropy(data)

            # Fixations and saccades
            if self.compute_fixations:
                fix_stats = self._compute_fixation_stats(data, sample_rate)
                result.n_fixations = fix_stats.get('n_fixations', 0)
                result.n_saccades = fix_stats.get('n_saccades', 0)
                result.mean_fixation_duration = fix_stats.get('mean_fixation_duration', 0.0)
                result.mean_saccade_amplitude = fix_stats.get('mean_saccade_amplitude', 0.0)

            # Custom analyzers
            for analyzer in self._custom_analyzers:
                try:
                    custom_result = analyzer(data)
                    if isinstance(custom_result, dict):
                        result.custom_metrics.update(custom_result)
                except Exception as e:
                    warnings.warn(f"Custom analyzer failed for {file_path}: {e}")

            return result

        except Exception as e:
            return ProcessingResult(
                file_path=file_path,
                success=False,
                error_message=f"{type(e).__name__}: {str(e)}\n{traceback.format_exc()}",
            )

    def _compute_quality_metrics(self, data: EyeLeanData) -> Optional[QualityMetrics]:
        """Compute quality metrics for the data."""
        try:
            timestamps = data.get_timestamps()

            validity = None
            if data.has_column('is_tracking_valid'):
                validity = data.df['is_tracking_valid'].values

            left_validity = None
            right_validity = None
            if data.has_column('has_left_direction'):
                left_validity = data.df['has_left_direction'].values
            if data.has_column('has_right_direction'):
                right_validity = data.df['has_right_direction'].values

            left_pupil = None
            right_pupil = None
            if data.has_column('left_pupil_diameter'):
                left_pupil = data.df['left_pupil_diameter'].values
            if data.has_column('right_pupil_diameter'):
                right_pupil = data.df['right_pupil_diameter'].values

            left_openness = None
            right_openness = None
            if data.has_column('left_openness'):
                left_openness = data.df['left_openness'].values
            if data.has_column('right_openness'):
                right_openness = data.df['right_openness'].values

            return calculate_quality_metrics(
                timestamps=timestamps,
                validity=validity,
                left_validity=left_validity,
                right_validity=right_validity,
                left_pupil=left_pupil,
                right_pupil=right_pupil,
                left_openness=left_openness,
                right_openness=right_openness,
            )
        except Exception:
            return None

    def _compute_lhipa(self, data: EyeLeanData, sample_rate: float) -> Optional[float]:
        """Compute LHIPA from pupil data."""
        try:
            pupil_data = data.get_pupil_data(eye='both')

            if 'left_pupil' in pupil_data.columns:
                left = pupil_data['left_pupil'].values
            else:
                left = None

            if 'right_pupil' in pupil_data.columns:
                right = pupil_data['right_pupil'].values
            else:
                right = None

            if left is None and right is None:
                return None

            # Average both eyes
            if left is not None and right is not None:
                pupil = np.nanmean([left, right], axis=0)
            elif left is not None:
                pupil = left
            else:
                pupil = right

            result = calculate_lhipa(pupil, sample_rate)
            return result.lhipa if result.is_valid else None

        except Exception:
            return None

    def _compute_entropy(self, data: EyeLeanData) -> Optional[float]:
        """Compute gaze entropy."""
        try:
            gaze = data.compute_gaze_points(distance=1.0)

            if 'gaze_x' not in gaze.columns or 'gaze_z' not in gaze.columns:
                return None

            result = calculate_gaze_entropy(
                gaze['gaze_x'].values,
                gaze['gaze_z'].values,
            )
            return result.entropy if result.is_valid else None

        except Exception:
            return None

    def _compute_fixation_stats(self, data: EyeLeanData, sample_rate: float) -> Dict:
        """Compute fixation and saccade statistics."""
        try:
            gaze = data.compute_gaze_points(distance=1.0)
            timestamps = data.get_timestamps()

            if 'gaze_x' not in gaze.columns or 'gaze_z' not in gaze.columns:
                return {}

            movements = detect_eye_movements(
                gaze['gaze_x'].values,
                gaze['gaze_z'].values,
                timestamps,
                velocity_threshold=self.velocity_threshold,
            )

            fixations = movements['fixations']
            saccades = movements['saccades']

            stats = {
                'n_fixations': len(fixations),
                'n_saccades': len(saccades),
            }

            if len(fixations) > 0:
                stats['mean_fixation_duration'] = np.mean([f.duration for f in fixations])

            if len(saccades) > 0:
                stats['mean_saccade_amplitude'] = np.mean([s.amplitude for s in saccades])

            return stats

        except Exception:
            return {}

    @staticmethod
    def results_to_dataframe(results: List[ProcessingResult]) -> pd.DataFrame:
        """
        Convert processing results to a pandas DataFrame.

        Args:
            results: List of ProcessingResult objects.

        Returns:
            DataFrame with one row per file.
        """
        if not results:
            return pd.DataFrame()

        rows = [r.to_dict() for r in results]
        return pd.DataFrame(rows)

    @staticmethod
    def summarize_results(results: List[ProcessingResult]) -> Dict:
        """
        Generate summary statistics from processing results.

        Args:
            results: List of ProcessingResult objects.

        Returns:
            Dictionary with summary statistics.
        """
        if not results:
            return {}

        successful = [r for r in results if r.success]
        failed = [r for r in results if not r.success]

        summary = {
            'n_files': len(results),
            'n_successful': len(successful),
            'n_failed': len(failed),
            'success_rate': len(successful) / len(results) * 100 if results else 0,
        }

        if successful:
            durations = [r.duration_seconds for r in successful]
            summary['total_duration_seconds'] = sum(durations)
            summary['mean_duration_seconds'] = np.mean(durations)

            samples = [r.n_samples for r in successful]
            summary['total_samples'] = sum(samples)
            summary['mean_samples'] = np.mean(samples)

            lhipa_vals = [r.lhipa for r in successful if r.lhipa is not None]
            if lhipa_vals:
                summary['mean_lhipa'] = np.mean(lhipa_vals)
                summary['std_lhipa'] = np.std(lhipa_vals)

            entropy_vals = [r.entropy for r in successful if r.entropy is not None]
            if entropy_vals:
                summary['mean_entropy'] = np.mean(entropy_vals)
                summary['std_entropy'] = np.std(entropy_vals)

        return summary


def process_batch(file_paths: List[Union[str, Path]],
                  output_path: Optional[Union[str, Path]] = None,
                  n_workers: int = 4,
                  **kwargs) -> pd.DataFrame:
    """
    Convenience function for batch processing.

    Args:
        file_paths: List of file paths to process.
        output_path: Optional path to save results CSV.
        n_workers: Number of parallel workers.
        **kwargs: Additional arguments passed to BatchProcessor.

    Returns:
        DataFrame with processing results.
    """
    processor = BatchProcessor(n_workers=n_workers, **kwargs)
    results = processor.process_files(file_paths)
    df = BatchProcessor.results_to_dataframe(results)

    if output_path:
        df.to_csv(output_path, index=False)

    return df


def process_directory_batch(directory: Union[str, Path],
                            pattern: str = "*.csv",
                            output_path: Optional[Union[str, Path]] = None,
                            n_workers: int = 4,
                            recursive: bool = False,
                            **kwargs) -> pd.DataFrame:
    """
    Convenience function for batch processing a directory.

    Args:
        directory: Directory path.
        pattern: Glob pattern for file matching.
        output_path: Optional path to save results CSV.
        n_workers: Number of parallel workers.
        recursive: If True, search subdirectories.
        **kwargs: Additional arguments passed to BatchProcessor.

    Returns:
        DataFrame with processing results.
    """
    processor = BatchProcessor(n_workers=n_workers, **kwargs)
    results = processor.process_directory(directory, pattern, recursive)
    df = BatchProcessor.results_to_dataframe(results)

    if output_path:
        df.to_csv(output_path, index=False)

    return df
