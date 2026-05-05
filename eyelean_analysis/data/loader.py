"""
Flexible data loader for Eye_lean eye tracking CSV files.

Supports multiple column naming conventions and automatic column detection.
"""

import pandas as pd
import numpy as np
from pathlib import Path
from typing import Dict, List, Optional, Union, Tuple
import warnings


# Column aliases mapping canonical names to possible variations
COLUMN_ALIASES = {
    # Timestamps
    'timestamp': ['UnityTimestamp', 'RealTimeSinceStartup', 'timestamp', 'time', 'Time', 'Timestamp', 'SessionTime'],
    'system_timestamp': ['SystemTimestamp', 'system_timestamp', 'SystemTime'],
    'frame_number': ['FrameNumber', 'frame_number', 'Frame', 'frame'],
    'delta_time': ['DeltaTime', 'delta_time', 'dt'],

    # Experiment metadata
    'trial_number': ['TrialNumber', 'trial_number', 'Trial', 'trial_id', 'TrialID'],
    'phase': ['CurrentPhase', 'phase', 'Phase', 'condition', 'Condition'],
    'sub_task': ['SubTask', 'sub_task', 'subtask', 'SubTaskID', 'Task'],
    'participant_id': ['ParticipantID', 'participant_id', 'SubjectID', 'P_ID', 'subject'],
    'session_config': ['SessionConfig', 'session_config', 'LaneConfiguration'],
    'is_debug_mode': ['IsDebugMode', 'is_debug_mode', 'DebugMode'],

    # Head position
    'head_pos_x': ['HeadPos_X', 'HeadPosX', 'head_pos_x', 'HeadPosition_X'],
    'head_pos_y': ['HeadPos_Y', 'HeadPosY', 'head_pos_y', 'HeadPosition_Y'],
    'head_pos_z': ['HeadPos_Z', 'HeadPosZ', 'head_pos_z', 'HeadPosition_Z'],

    # Head rotation (quaternion)
    'head_rot_x': ['HeadRot_X', 'HeadRotX', 'head_rot_x'],
    'head_rot_y': ['HeadRot_Y', 'HeadRotY', 'head_rot_y'],
    'head_rot_z': ['HeadRot_Z', 'HeadRotZ', 'head_rot_z'],
    'head_rot_w': ['HeadRot_W', 'HeadRotW', 'head_rot_w'],

    # Head forward direction
    'head_forward_x': ['HeadForward_X', 'HeadForwardX'],
    'head_forward_y': ['HeadForward_Y', 'HeadForwardY'],
    'head_forward_z': ['HeadForward_Z', 'HeadForwardZ'],

    # Head right direction
    'head_right_x': ['HeadRight_X', 'HeadRightX'],
    'head_right_y': ['HeadRight_Y', 'HeadRightY'],
    'head_right_z': ['HeadRight_Z', 'HeadRightZ'],

    # Head up direction
    'head_up_x': ['HeadUp_X', 'HeadUpX'],
    'head_up_y': ['HeadUp_Y', 'HeadUpY'],
    'head_up_z': ['HeadUp_Z', 'HeadUpZ'],

    # Combined eye origin (world space)
    'combined_origin_x': ['CombinedOrigin_X', 'CombinedEyeOriginX', 'combined_origin_x'],
    'combined_origin_y': ['CombinedOrigin_Y', 'CombinedEyeOriginY', 'combined_origin_y'],
    'combined_origin_z': ['CombinedOrigin_Z', 'CombinedEyeOriginZ', 'combined_origin_z'],

    # Combined eye direction (gaze vector)
    'combined_dir_x': ['CombinedDir_X', 'CombinedEyeDirectionX', 'combined_dir_x', 'GazeDirectionX'],
    'combined_dir_y': ['CombinedDir_Y', 'CombinedEyeDirectionY', 'combined_dir_y', 'GazeDirectionY'],
    'combined_dir_z': ['CombinedDir_Z', 'CombinedEyeDirectionZ', 'combined_dir_z', 'GazeDirectionZ'],

    # Combined eye validity flags
    'has_combined_origin': ['HasCombinedOrigin', 'has_combined_origin'],
    'has_combined_direction': ['HasCombinedDirection', 'has_combined_direction'],

    # Left eye origin
    'left_origin_x': ['LeftOrigin_X', 'LeftEyeOriginX', 'left_origin_x'],
    'left_origin_y': ['LeftOrigin_Y', 'LeftEyeOriginY', 'left_origin_y'],
    'left_origin_z': ['LeftOrigin_Z', 'LeftEyeOriginZ', 'left_origin_z'],

    # Left eye direction
    'left_dir_x': ['LeftDir_X', 'LeftEyeDirectionX', 'left_dir_x'],
    'left_dir_y': ['LeftDir_Y', 'LeftEyeDirectionY', 'left_dir_y'],
    'left_dir_z': ['LeftDir_Z', 'LeftEyeDirectionZ', 'left_dir_z'],

    # Left eye measurements
    'left_openness': ['LeftOpenness', 'LeftEyeOpenness', 'left_openness'],
    'left_pupil_diameter': ['LeftPupilDiameter', 'LeftPupil', 'left_pupil_diameter', 'left_pupil'],
    'left_pupil_pos_x': ['LeftPupilPos_X', 'LeftPupilPositionX', 'left_pupil_pos_x'],
    'left_pupil_pos_y': ['LeftPupilPos_Y', 'LeftPupilPositionY', 'left_pupil_pos_y'],

    # Left eye validity flags
    'has_left_origin': ['HasLeftOrigin', 'has_left_origin'],
    'has_left_direction': ['HasLeftDirection', 'has_left_direction'],
    'has_left_openness': ['HasLeftOpenness', 'has_left_openness'],
    'has_left_pupil_diameter': ['HasLeftPupilDiameter', 'has_left_pupil_diameter'],
    'has_left_pupil_position': ['HasLeftPupilPosition', 'has_left_pupil_position'],

    # Right eye origin
    'right_origin_x': ['RightOrigin_X', 'RightEyeOriginX', 'right_origin_x'],
    'right_origin_y': ['RightOrigin_Y', 'RightEyeOriginY', 'right_origin_y'],
    'right_origin_z': ['RightOrigin_Z', 'RightEyeOriginZ', 'right_origin_z'],

    # Right eye direction
    'right_dir_x': ['RightDir_X', 'RightEyeDirectionX', 'right_dir_x'],
    'right_dir_y': ['RightDir_Y', 'RightEyeDirectionY', 'right_dir_y'],
    'right_dir_z': ['RightDir_Z', 'RightEyeDirectionZ', 'right_dir_z'],

    # Right eye measurements
    'right_openness': ['RightOpenness', 'RightEyeOpenness', 'right_openness'],
    'right_pupil_diameter': ['RightPupilDiameter', 'RightPupil', 'right_pupil_diameter', 'right_pupil'],
    'right_pupil_pos_x': ['RightPupilPos_X', 'RightPupilPositionX', 'right_pupil_pos_x'],
    'right_pupil_pos_y': ['RightPupilPos_Y', 'RightPupilPositionY', 'right_pupil_pos_y'],

    # Right eye validity flags
    'has_right_origin': ['HasRightOrigin', 'has_right_origin'],
    'has_right_direction': ['HasRightDirection', 'has_right_direction'],
    'has_right_openness': ['HasRightOpenness', 'has_right_openness'],
    'has_right_pupil_diameter': ['HasRightPupilDiameter', 'has_right_pupil_diameter'],
    'has_right_pupil_position': ['HasRightPupilPosition', 'has_right_pupil_position'],

    # Eye tracking status
    'is_eye_tracking_available': ['IsEyeTrackingAvailable', 'is_eye_tracking_available'],
    'is_tracking_valid': ['IsTrackingValid', 'is_tracking_valid', 'TrackingValid'],

    # Vergence data
    'vergence_point_x': ['VergencePoint_X', 'VergencePointX', 'vergence_point_x'],
    'vergence_point_y': ['VergencePoint_Y', 'VergencePointY', 'vergence_point_y'],
    'vergence_point_z': ['VergencePoint_Z', 'VergencePointZ', 'vergence_point_z'],
    'vergence_quality': ['VergenceQuality', 'vergence_quality'],
    'has_valid_vergence': ['HasValidVergence', 'has_valid_vergence'],

    # Performance metrics
    'fps': ['CurrentFPS', 'FPS', 'fps', 'FrameRate'],
    'frame_time_ms': ['FrameTimeMs', 'frame_time_ms', 'FrameTime'],
    'sample_count': ['DataSampleCount', 'sample_count', 'SampleCount'],

    # Computed gaze points (from Flask app)
    'gaze_point_x': ['GazePointX', 'gaze_point_x', 'GazeX'],
    'gaze_point_y': ['GazePointY', 'gaze_point_y', 'GazeY'],
    'gaze_point_z': ['GazePointZ', 'gaze_point_z', 'GazeZ'],
}


def read_csv_metadata(file_path: Union[str, Path]) -> Dict[str, str]:
    """Extract the `# Key: value` metadata block from an Eye_lean CSV.

    SimpleEyeTracker emits unconditional metadata before the column header:
    `CoordinateOrigin`, `CoordinateOriginSet`, `Profile`, plus optional
    descriptive lines (`FileVersion`, `SessionID`, `Device`, …) gated on
    the `IncludeHeaderComments` toggle. This helper returns those lines as
    a dict so consumers can decide whether (and which) post-hoc correction
    is appropriate without needing to re-parse the CSV header.

    Lines without a `:` are ignored; duplicate keys keep the last value.

    Returns an empty dict when the file has no metadata block.
    """
    path = Path(file_path)
    out: Dict[str, str] = {}
    with path.open("r") as f:
        for line in f:
            if not line.startswith("#"):
                break
            colon = line.find(":")
            if colon < 0:
                continue
            key = line[1:colon].strip()
            value = line[colon + 1 :].strip()
            if key:
                out[key] = value
    return out


class EyeLeanLoader:
    """
    Flexible loader for Eye_lean eye tracking CSV files.

    Handles multiple column naming conventions and provides standardized access
    to eye tracking data with automatic column detection.

    Example:
        >>> loader = EyeLeanLoader()
        >>> data = loader.load('path/to/eyetracking.csv')
        >>> print(data.available_columns)
        >>> pupil_data = data.get_pupil_data()
    """

    def __init__(self, custom_aliases: Optional[Dict[str, List[str]]] = None):
        """
        Initialize the loader.

        Args:
            custom_aliases: Additional column aliases to merge with defaults.
                           Keys are canonical names, values are lists of possible column names.
        """
        self.aliases = COLUMN_ALIASES.copy()
        if custom_aliases:
            for key, values in custom_aliases.items():
                if key in self.aliases:
                    self.aliases[key].extend(values)
                else:
                    self.aliases[key] = values

    def load(self,
             file_path: Union[str, Path],
             standardize_columns: bool = True,
             parse_booleans: bool = True,
             **pandas_kwargs) -> 'EyeLeanData':
        """
        Load an eye tracking CSV file.

        Args:
            file_path: Path to the CSV file.
            standardize_columns: If True, rename columns to canonical names.
            parse_booleans: If True, convert True/False strings to boolean.
            **pandas_kwargs: Additional arguments passed to pd.read_csv().

        Returns:
            EyeLeanData object containing the loaded data.
        """
        file_path = Path(file_path)

        if not file_path.exists():
            raise FileNotFoundError(f"File not found: {file_path}")

        if not file_path.suffix.lower() == '.csv':
            warnings.warn(f"Expected .csv file, got {file_path.suffix}")

        # Load the CSV with robust error handling.
        # Eye_lean CSVs prepend optional `# Key: value` metadata lines (e.g.
        # `# CoordinateOrigin: x,y,z`, `# FileVersion: 1.0`) before the column
        # header. pandas does not treat `#` as a comment by default, so without
        # `comment='#'` the first metadata line is mis-read as the column header.
        # Caller-supplied `comment=` wins.
        pandas_kwargs.setdefault('comment', '#')
        # Eye_lean CSVs may also have inconsistent column counts if custom
        # metadata was added mid-session. Try normal parsing; on failure, retry
        # with on_bad_lines='warn' and count skipped rows for the user.
        try:
            df = pd.read_csv(file_path, **pandas_kwargs)
        except pd.errors.ParserError as e:
            with warnings.catch_warnings(record=True) as caught:
                warnings.simplefilter('always', pd.errors.ParserWarning)
                df = pd.read_csv(file_path, on_bad_lines='warn', **pandas_kwargs)
            n_dropped = sum(1 for w in caught if issubclass(w.category, pd.errors.ParserWarning))
            warnings.warn(
                f"CSV {file_path.name}: skipped {n_dropped} malformed row(s) "
                f"(likely custom metadata added mid-session). Original error: {e}"
            )

        # Build column mapping
        column_mapping = self._build_column_mapping(df.columns.tolist())

        # Standardize column names if requested
        if standardize_columns:
            df = df.rename(columns=column_mapping)

        # Parse boolean columns
        if parse_booleans:
            df = self._parse_booleans(df)

        return EyeLeanData(df, column_mapping, file_path)

    def _build_column_mapping(self, columns: List[str]) -> Dict[str, str]:
        """
        Build mapping from original column names to canonical names.

        Args:
            columns: List of column names from the CSV.

        Returns:
            Dictionary mapping original names to canonical names.
        """
        mapping = {}

        for canonical_name, aliases in self.aliases.items():
            for col in columns:
                if col in aliases:
                    mapping[col] = canonical_name
                    break

        return mapping

    def _parse_booleans(self, df: pd.DataFrame) -> pd.DataFrame:
        """Convert string True/False values to boolean dtype."""
        for col in df.columns:
            if df[col].dtype == object:
                # Check if column contains True/False strings
                unique_vals = df[col].dropna().unique()
                if set(unique_vals).issubset({'True', 'False', True, False}):
                    df[col] = df[col].map({'True': True, 'False': False, True: True, False: False})

        return df


class EyeLeanData:
    """
    Container for loaded eye tracking data with convenient accessors.

    Provides methods for extracting specific data types (pupil, gaze, etc.)
    and computing derived values (gaze points, velocity, etc.).
    """

    def __init__(self,
                 dataframe: pd.DataFrame,
                 column_mapping: Dict[str, str],
                 source_path: Optional[Path] = None):
        """
        Initialize the data container.

        Args:
            dataframe: The loaded pandas DataFrame.
            column_mapping: Mapping from original to canonical column names.
            source_path: Path to the source file.
        """
        self._df = dataframe
        self._column_mapping = column_mapping
        self._reverse_mapping = {v: k for k, v in column_mapping.items()}
        self.source_path = source_path

    @property
    def df(self) -> pd.DataFrame:
        """Access the underlying DataFrame."""
        return self._df

    @property
    def available_columns(self) -> List[str]:
        """List of canonical column names that are available."""
        return list(self._column_mapping.values())

    @property
    def original_columns(self) -> List[str]:
        """List of original column names from the CSV."""
        return self._df.columns.tolist()

    def has_column(self, canonical_name: str) -> bool:
        """Check if a canonical column name is available."""
        return canonical_name in self._df.columns

    def __len__(self) -> int:
        """Return number of samples."""
        return len(self._df)

    def __getitem__(self, key):
        """Allow direct DataFrame-style access."""
        return self._df[key]

    @property
    def duration(self) -> float:
        """
        Get the total duration of the recording in seconds.

        Returns:
            Duration in seconds, or 0.0 if timestamps unavailable.
        """
        try:
            timestamps = self.get_timestamps()
            if len(timestamps) < 2:
                return 0.0
            return float(timestamps[-1] - timestamps[0])
        except (KeyError, IndexError):
            return 0.0

    @property
    def column_map(self) -> Dict[str, str]:
        """Access the column mapping dictionary."""
        return self._column_mapping

    # ==================== Timestamp Methods ====================

    def get_timestamps(self, column: str = 'timestamp') -> np.ndarray:
        """
        Get timestamp array.

        Args:
            column: Which timestamp column to use ('timestamp', 'system_timestamp').

        Returns:
            Array of timestamps in seconds.
        """
        # Try the requested column name first
        if column in self._df.columns:
            return self._df[column].values

        # Try common timestamp column names (Eye_lean uses UnityTimestamp)
        timestamp_aliases = ['UnityTimestamp', 'RealTimeSinceStartup', 'timestamp', 'time', 'Time', 'SystemTimestamp']
        for alias in timestamp_aliases:
            if alias in self._df.columns:
                return self._df[alias].values

        raise KeyError(f"Timestamp column '{column}' not found. Tried: {timestamp_aliases}. Available: {list(self._df.columns)[:10]}...")

    def get_sample_rate(self) -> float:
        """
        Estimate the sampling rate from timestamps.

        Returns:
            Estimated sampling rate in Hz.
        """
        timestamps = self.get_timestamps()
        if len(timestamps) < 2:
            return 0.0

        # Use median of time differences for robustness
        dt = np.median(np.diff(timestamps))
        if dt <= 0:
            return 0.0

        return 1.0 / dt

    # ==================== Pupil Data Methods ====================

    def get_pupil_data(self,
                       eye: str = 'both',
                       valid_only: bool = True) -> pd.DataFrame:
        """
        Extract pupil diameter data.

        Args:
            eye: Which eye(s) to include ('left', 'right', 'both', 'average').
            valid_only: If True, mask invalid samples with NaN.

        Returns:
            DataFrame with timestamp and pupil diameter columns.
        """
        result = pd.DataFrame()

        if self.has_column('timestamp'):
            result['timestamp'] = self._df['timestamp']

        if eye in ('left', 'both'):
            if self.has_column('left_pupil_diameter'):
                col_data = self._df['left_pupil_diameter'].copy()
                if valid_only and self.has_column('has_left_pupil_diameter'):
                    col_data = col_data.where(self._df['has_left_pupil_diameter'])
                result['left_pupil'] = col_data

        if eye in ('right', 'both'):
            if self.has_column('right_pupil_diameter'):
                col_data = self._df['right_pupil_diameter'].copy()
                if valid_only and self.has_column('has_right_pupil_diameter'):
                    col_data = col_data.where(self._df['has_right_pupil_diameter'])
                result['right_pupil'] = col_data

        if eye == 'average':
            left = result.get('left_pupil', pd.Series(dtype=float))
            right = result.get('right_pupil', pd.Series(dtype=float))

            if not left.empty and not right.empty:
                result['average_pupil'] = (left + right) / 2
            elif not left.empty:
                result['average_pupil'] = left
            elif not right.empty:
                result['average_pupil'] = right

        return result

    # ==================== Gaze Data Methods ====================

    def get_gaze_direction(self,
                           eye: str = 'combined',
                           valid_only: bool = True) -> pd.DataFrame:
        """
        Extract gaze direction vectors.

        Args:
            eye: Which eye to use ('combined', 'left', 'right').
            valid_only: If True, mask invalid samples with NaN.

        Returns:
            DataFrame with timestamp and direction (x, y, z) columns.
        """
        result = pd.DataFrame()

        if self.has_column('timestamp'):
            result['timestamp'] = self._df['timestamp']

        prefix_map = {
            'combined': 'combined_dir',
            'left': 'left_dir',
            'right': 'right_dir'
        }
        prefix = prefix_map.get(eye, 'combined_dir')

        validity_col = {
            'combined': 'has_combined_direction',
            'left': 'has_left_direction',
            'right': 'has_right_direction'
        }.get(eye)

        for axis in ['x', 'y', 'z']:
            col_name = f'{prefix}_{axis}'
            if self.has_column(col_name):
                col_data = self._df[col_name].copy()
                if valid_only and validity_col and self.has_column(validity_col):
                    col_data = col_data.where(self._df[validity_col])
                result[f'dir_{axis}'] = col_data

        return result

    def get_gaze_origin(self,
                        eye: str = 'combined',
                        valid_only: bool = True) -> pd.DataFrame:
        """
        Extract gaze origin positions.

        Args:
            eye: Which eye to use ('combined', 'left', 'right').
            valid_only: If True, mask invalid samples with NaN.

        Returns:
            DataFrame with timestamp and origin (x, y, z) columns.
        """
        result = pd.DataFrame()

        if self.has_column('timestamp'):
            result['timestamp'] = self._df['timestamp']

        prefix_map = {
            'combined': 'combined_origin',
            'left': 'left_origin',
            'right': 'right_origin'
        }
        prefix = prefix_map.get(eye, 'combined_origin')

        validity_col = {
            'combined': 'has_combined_origin',
            'left': 'has_left_origin',
            'right': 'has_right_origin'
        }.get(eye)

        for axis in ['x', 'y', 'z']:
            col_name = f'{prefix}_{axis}'
            if self.has_column(col_name):
                col_data = self._df[col_name].copy()
                if valid_only and validity_col and self.has_column(validity_col):
                    col_data = col_data.where(self._df[validity_col])
                result[f'origin_{axis}'] = col_data

        return result

    def compute_gaze_points(self,
                            distance: float = 1.0,
                            eye: str = 'combined') -> pd.DataFrame:
        """
        Compute gaze points at a given distance from the eye origin.

        gaze_point = origin + direction * distance

        Args:
            distance: Distance from eye origin in meters.
            eye: Which eye to use ('combined', 'left', 'right').

        Returns:
            DataFrame with timestamp and gaze point (x, y, z) columns.
        """
        origin = self.get_gaze_origin(eye=eye)
        direction = self.get_gaze_direction(eye=eye)

        result = pd.DataFrame()

        if self.has_column('timestamp'):
            result['timestamp'] = self._df['timestamp']

        for axis in ['x', 'y', 'z']:
            origin_col = f'origin_{axis}'
            dir_col = f'dir_{axis}'

            if origin_col in origin.columns and dir_col in direction.columns:
                result[f'gaze_{axis}'] = origin[origin_col] + direction[dir_col] * distance

        return result

    # ==================== Eye Openness Methods ====================

    def get_eye_openness(self,
                         eye: str = 'both',
                         valid_only: bool = True) -> pd.DataFrame:
        """
        Extract eye openness data (for blink detection).

        Args:
            eye: Which eye(s) to include ('left', 'right', 'both').
            valid_only: If True, mask invalid samples with NaN.

        Returns:
            DataFrame with timestamp and openness columns.
        """
        result = pd.DataFrame()

        if self.has_column('timestamp'):
            result['timestamp'] = self._df['timestamp']

        if eye in ('left', 'both'):
            if self.has_column('left_openness'):
                col_data = self._df['left_openness'].copy()
                if valid_only and self.has_column('has_left_openness'):
                    col_data = col_data.where(self._df['has_left_openness'])
                result['left_openness'] = col_data

        if eye in ('right', 'both'):
            if self.has_column('right_openness'):
                col_data = self._df['right_openness'].copy()
                if valid_only and self.has_column('has_right_openness'):
                    col_data = col_data.where(self._df['has_right_openness'])
                result['right_openness'] = col_data

        return result

    # ==================== Vergence Methods ====================

    def get_vergence_data(self, valid_only: bool = True) -> pd.DataFrame:
        """
        Extract vergence point data.

        Args:
            valid_only: If True, mask invalid samples with NaN.

        Returns:
            DataFrame with timestamp, vergence point, and quality columns.
        """
        result = pd.DataFrame()

        if self.has_column('timestamp'):
            result['timestamp'] = self._df['timestamp']

        for axis in ['x', 'y', 'z']:
            col_name = f'vergence_point_{axis}'
            if self.has_column(col_name):
                col_data = self._df[col_name].copy()
                if valid_only and self.has_column('has_valid_vergence'):
                    col_data = col_data.where(self._df['has_valid_vergence'])
                result[f'vergence_{axis}'] = col_data

        if self.has_column('vergence_quality'):
            result['quality'] = self._df['vergence_quality']

        return result

    # ==================== Head Tracking Methods ====================

    def get_head_position(self) -> pd.DataFrame:
        """
        Extract head position data.

        Returns:
            DataFrame with timestamp and position (x, y, z) columns.
        """
        result = pd.DataFrame()

        if self.has_column('timestamp'):
            result['timestamp'] = self._df['timestamp']

        for axis in ['x', 'y', 'z']:
            col_name = f'head_pos_{axis}'
            if self.has_column(col_name):
                result[f'pos_{axis}'] = self._df[col_name]

        return result

    def get_head_rotation(self) -> pd.DataFrame:
        """
        Extract head rotation (quaternion) data.

        Returns:
            DataFrame with timestamp and quaternion (x, y, z, w) columns.
        """
        result = pd.DataFrame()

        if self.has_column('timestamp'):
            result['timestamp'] = self._df['timestamp']

        for axis in ['x', 'y', 'z', 'w']:
            col_name = f'head_rot_{axis}'
            if self.has_column(col_name):
                result[f'rot_{axis}'] = self._df[col_name]

        return result

    # ==================== Trial/Segment Methods ====================

    def get_trials(self) -> List[int]:
        """
        Get list of unique trial numbers.

        Returns:
            List of trial numbers.
        """
        if not self.has_column('trial_number'):
            return []

        return sorted(self._df['trial_number'].unique().tolist())

    def get_phases(self) -> List[str]:
        """
        Get list of unique phase/condition names.

        Returns:
            List of phase names.
        """
        if not self.has_column('phase'):
            return []

        return self._df['phase'].unique().tolist()

    def filter_by_trial(self, trial_number: int) -> 'EyeLeanData':
        """
        Get data for a specific trial.

        Args:
            trial_number: The trial number to filter by.

        Returns:
            New EyeLeanData containing only the specified trial.
        """
        if not self.has_column('trial_number'):
            raise KeyError("No trial_number column available")

        filtered_df = self._df[self._df['trial_number'] == trial_number].copy()
        return EyeLeanData(filtered_df, self._column_mapping, self.source_path)

    def filter_by_phase(self, phase: str) -> 'EyeLeanData':
        """
        Get data for a specific phase/condition.

        Args:
            phase: The phase name to filter by.

        Returns:
            New EyeLeanData containing only the specified phase.
        """
        if not self.has_column('phase'):
            raise KeyError("No phase column available")

        filtered_df = self._df[self._df['phase'] == phase].copy()
        return EyeLeanData(filtered_df, self._column_mapping, self.source_path)

    def filter_by_validity(self, require_tracking: bool = True) -> 'EyeLeanData':
        """
        Filter to only valid eye tracking samples.

        Args:
            require_tracking: If True, require IsTrackingValid == True.

        Returns:
            New EyeLeanData containing only valid samples.
        """
        mask = pd.Series(True, index=self._df.index)

        if require_tracking and self.has_column('is_tracking_valid'):
            mask &= self._df['is_tracking_valid'] == True

        if self.has_column('is_eye_tracking_available'):
            mask &= self._df['is_eye_tracking_available'] == True

        filtered_df = self._df[mask].copy()
        return EyeLeanData(filtered_df, self._column_mapping, self.source_path)

    # ==================== Export Methods ====================

    def to_dataframe(self) -> pd.DataFrame:
        """
        Export data as a pandas DataFrame.

        Returns:
            Copy of the underlying DataFrame.
        """
        return self._df.copy()

    def to_csv(self, path: Union[str, Path], **kwargs):
        """
        Export data to CSV.

        Args:
            path: Output file path.
            **kwargs: Additional arguments passed to DataFrame.to_csv().
        """
        self._df.to_csv(path, index=False, **kwargs)

    # ==================== Summary Methods ====================

    def summary(self) -> Dict:
        """
        Generate a summary of the data.

        Returns:
            Dictionary with summary statistics.
        """
        summary = {
            'n_samples': len(self),
            'duration_seconds': None,
            'sample_rate_hz': self.get_sample_rate(),
            'columns': self.available_columns,
            'trials': self.get_trials(),
            'phases': self.get_phases(),
        }

        if self.has_column('timestamp'):
            timestamps = self.get_timestamps()
            if len(timestamps) > 1:
                summary['duration_seconds'] = float(timestamps[-1] - timestamps[0])

        # Validity stats
        if self.has_column('is_tracking_valid'):
            valid_count = self._df['is_tracking_valid'].sum()
            summary['valid_samples'] = int(valid_count)
            summary['valid_percentage'] = float(valid_count / len(self) * 100)

        # Pupil stats
        pupil_data = self.get_pupil_data()
        if 'left_pupil' in pupil_data.columns:
            summary['left_pupil_mean'] = float(pupil_data['left_pupil'].mean())
            summary['left_pupil_std'] = float(pupil_data['left_pupil'].std())
        if 'right_pupil' in pupil_data.columns:
            summary['right_pupil_mean'] = float(pupil_data['right_pupil'].mean())
            summary['right_pupil_std'] = float(pupil_data['right_pupil'].std())

        return summary


def load_eyetracking(file_path: Union[str, Path], **kwargs) -> EyeLeanData:
    """
    Convenience function to load an eye tracking file.

    Args:
        file_path: Path to the CSV file.
        **kwargs: Additional arguments passed to EyeLeanLoader.load().

    Returns:
        EyeLeanData object.

    Example:
        >>> data = load_eyetracking('recording.csv')
        >>> print(data.summary())
    """
    loader = EyeLeanLoader()
    return loader.load(file_path, **kwargs)


# -----------------------------------------------------------------------------
# v1.2 sidecar loaders
# -----------------------------------------------------------------------------
#
# Starting in v1.2, the Unity recorder emits two optional sidecars next to the
# main gaze CSV:
#
#   - `<base>_SceneState.csv`  — per-frame transforms for every Recordable in
#     the scene. Columns: Frame, T, ObjectId, Pos_X/Y/Z, Rot_X/Y/Z/W, Active.
#   - `<base>_SceneEvents.csv` — discrete events emitted by the experiment
#     controller. Columns: Frame, T, EventType, ObjectId, Detail.
#
# Both share the metadata-block convention as the main CSV (lines beginning
# with `#` followed by `Key: value`).

def _scene_sidecar_paths(main_csv_path: Union[str, Path]) -> Tuple[Path, Path]:
    """Compute the expected sidecar paths from the main CSV path.

    Returns `(state_path, events_path)`. The files may not exist.
    """
    p = Path(main_csv_path)
    base = p.with_suffix("")  # strip .csv
    return (
        base.parent / f"{base.name}_SceneState.csv",
        base.parent / f"{base.name}_SceneEvents.csv",
    )


def load_scene_state(file_path: Union[str, Path]) -> pd.DataFrame:
    """Load a `_SceneState.csv` sidecar into a DataFrame.

    The file's `# Key: value` metadata block is skipped. Use
    `read_csv_metadata` against the same path to retrieve those header values.

    Returns an empty DataFrame if the file does not exist.
    """
    p = Path(file_path)
    if not p.exists():
        return pd.DataFrame()
    return pd.read_csv(p, comment='#')


def load_scene_events(
    file_path: Union[str, Path],
    decode_config: bool = False,
) -> pd.DataFrame:
    """Load a `_SceneEvents.csv` sidecar into a DataFrame.

    Args:
        file_path: Path to the events sidecar. Returns an empty DataFrame
            if the file does not exist.
        decode_config: If True, attempt to base64-decode-then-JSON-parse the
            `Detail` column for `Config*` event types and add a `Config` dict
            column. Other rows get `Config = None`. Decoding failures are
            warned and the offending row's `Config` stays None.

    The metadata block (lines beginning with `#`) is skipped; use
    `read_csv_metadata` for those values.
    """
    p = Path(file_path)
    if not p.exists():
        return pd.DataFrame()

    df = pd.read_csv(p, comment='#')

    if decode_config and 'EventType' in df.columns and 'Detail' in df.columns:
        import base64
        import json

        def _decode(row):
            etype = row['EventType']
            if not isinstance(etype, str) or not etype.startswith('Config'):
                return None
            detail = row['Detail']
            if not isinstance(detail, str) or not detail:
                return None
            try:
                return json.loads(base64.b64decode(detail).decode('utf-8'))
            except Exception as e:  # noqa: BLE001
                warnings.warn(
                    f"Failed to decode Config detail at frame "
                    f"{row.get('Frame', '?')}: {e}"
                )
                return None

        df['Config'] = df.apply(_decode, axis=1)

    return df


def load_scene_sidecars(
    main_csv_path: Union[str, Path],
    decode_config: bool = False,
) -> Tuple[pd.DataFrame, pd.DataFrame]:
    """Load both v1.2 sidecars given the main CSV path.

    Returns `(scene_state_df, scene_events_df)`. Either DataFrame is empty
    if the corresponding sidecar is missing.
    """
    state_path, events_path = _scene_sidecar_paths(main_csv_path)
    return (
        load_scene_state(state_path),
        load_scene_events(events_path, decode_config=decode_config),
    )


def merge_gaze_with_scene_state(
    gaze_df: pd.DataFrame,
    scene_state_df: pd.DataFrame,
    object_id: Optional[str] = None,
    frame_column: str = 'frame_number',
) -> pd.DataFrame:
    """Join gaze samples with scene-state rows on `Frame`.

    Args:
        gaze_df: A DataFrame from `load_eyetracking(...).df`. Must contain
            a frame column (default canonical name `frame_number`).
        scene_state_df: A DataFrame from `load_scene_state(...)`.
        object_id: If provided, restrict scene-state to that ObjectId before
            merging — useful when you want a single recordable's pose joined
            against every gaze sample. If None, the merge is many-to-many on
            `Frame` and the result has one row per (gaze sample, recordable
            present that frame) pair.
        frame_column: Column in `gaze_df` that maps to the sidecar's `Frame`.
            Defaults to the canonical name written by `EyeLeanLoader`.

    Returns a new DataFrame; the inputs are not modified.
    """
    if gaze_df.empty or scene_state_df.empty:
        return gaze_df.copy()
    if frame_column not in gaze_df.columns:
        raise KeyError(
            f"gaze_df has no column '{frame_column}'. Pass frame_column= "
            f"explicitly or load with standardize_columns=True."
        )

    state = scene_state_df
    if object_id is not None:
        state = state[state['ObjectId'] == object_id]

    return gaze_df.merge(
        state, how='left',
        left_on=frame_column, right_on='Frame',
        suffixes=('', '_state'),
    )
