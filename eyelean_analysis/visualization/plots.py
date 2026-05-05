"""
Visualization utilities for eye tracking data.

Provides standardized plotting functions for common eye tracking visualizations.
"""

import numpy as np
from typing import Optional, Tuple, List, Union

# Optional imports for visualization
try:
    import matplotlib.pyplot as plt
    import matplotlib.colors as mcolors
    HAS_MATPLOTLIB = True
except ImportError:
    HAS_MATPLOTLIB = False

def _check_matplotlib():
    """Check if matplotlib is available."""
    if not HAS_MATPLOTLIB:
        raise ImportError(
            "matplotlib is required for visualization. "
            "Install with: pip install matplotlib"
        )


def create_heatmap(
    x: np.ndarray,
    y: np.ndarray,
    bins: int = 50,
    cmap: str = 'hot',
    title: str = 'Gaze Heatmap',
    xlabel: str = 'X Position',
    ylabel: str = 'Y Position',
    figsize: Tuple[int, int] = (10, 8),
    colorbar_label: str = 'Density',
    ax: Optional['plt.Axes'] = None,
) -> Tuple['plt.Figure', 'plt.Axes']:
    """
    Create a 2D heatmap of gaze positions.

    Args:
        x: X coordinates of gaze points.
        y: Y coordinates of gaze points.
        bins: Number of bins for the histogram.
        cmap: Colormap name (e.g., 'hot', 'viridis', 'plasma').
        title: Plot title.
        xlabel: X-axis label.
        ylabel: Y-axis label.
        figsize: Figure size as (width, height).
        colorbar_label: Label for the colorbar.
        ax: Optional existing axes to plot on.

    Returns:
        Tuple of (figure, axes).

    Example:
        >>> fig, ax = create_heatmap(gaze_x, gaze_y, bins=30, cmap='plasma')
        >>> plt.show()
    """
    _check_matplotlib()

    # Remove NaN values
    mask = ~(np.isnan(x) | np.isnan(y))
    x_clean = x[mask]
    y_clean = y[mask]

    if len(x_clean) == 0:
        raise ValueError("No valid data points after removing NaN values")

    # Create figure if needed
    if ax is None:
        fig, ax = plt.subplots(figsize=figsize)
    else:
        fig = ax.get_figure()

    # Create 2D histogram heatmap
    h = ax.hist2d(x_clean, y_clean, bins=bins, cmap=cmap)

    # Add colorbar
    cbar = plt.colorbar(h[3], ax=ax)
    cbar.set_label(colorbar_label)

    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.set_title(title)
    ax.set_aspect('equal')

    return fig, ax


def create_trajectory_plot(
    x: np.ndarray,
    y: np.ndarray,
    timestamps: Optional[np.ndarray] = None,
    title: str = 'Gaze Trajectory',
    xlabel: str = 'X Position',
    ylabel: str = 'Y Position',
    figsize: Tuple[int, int] = (10, 8),
    show_start_end: bool = True,
    line_alpha: float = 0.6,
    line_width: float = 0.5,
    color_by_time: bool = True,
    cmap: str = 'viridis',
    ax: Optional['plt.Axes'] = None,
) -> Tuple['plt.Figure', 'plt.Axes']:
    """
    Create a trajectory plot showing the gaze path over time.

    Args:
        x: X coordinates of gaze points.
        y: Y coordinates of gaze points.
        timestamps: Optional timestamps for color coding by time.
        title: Plot title.
        xlabel: X-axis label.
        ylabel: Y-axis label.
        figsize: Figure size as (width, height).
        show_start_end: If True, mark start (green) and end (red) points.
        line_alpha: Transparency of trajectory line.
        line_width: Width of trajectory line.
        color_by_time: If True and timestamps provided, color by time.
        cmap: Colormap for time-based coloring.
        ax: Optional existing axes to plot on.

    Returns:
        Tuple of (figure, axes).

    Example:
        >>> fig, ax = create_trajectory_plot(gaze_x, gaze_y, timestamps)
        >>> plt.show()
    """
    _check_matplotlib()

    # Remove NaN values
    mask = ~(np.isnan(x) | np.isnan(y))
    x_clean = x[mask]
    y_clean = y[mask]

    if len(x_clean) == 0:
        raise ValueError("No valid data points after removing NaN values")

    # Create figure if needed
    if ax is None:
        fig, ax = plt.subplots(figsize=figsize)
    else:
        fig = ax.get_figure()

    if color_by_time and timestamps is not None:
        # Filter timestamps to match cleaned data
        t_clean = timestamps[mask]
        t_normalized = (t_clean - t_clean.min()) / (t_clean.max() - t_clean.min() + 1e-10)

        # Create line segments colored by time
        from matplotlib.collections import LineCollection
        points = np.array([x_clean, y_clean]).T.reshape(-1, 1, 2)
        segments = np.concatenate([points[:-1], points[1:]], axis=1)

        lc = LineCollection(segments, cmap=cmap, alpha=line_alpha)
        lc.set_array(t_normalized[:-1])
        lc.set_linewidth(line_width)
        ax.add_collection(lc)

        # Add colorbar
        cbar = plt.colorbar(lc, ax=ax)
        cbar.set_label('Time (normalized)')

        # Set axis limits
        ax.set_xlim(x_clean.min() - 0.1, x_clean.max() + 0.1)
        ax.set_ylim(y_clean.min() - 0.1, y_clean.max() + 0.1)
    else:
        # Simple line plot
        ax.plot(x_clean, y_clean, 'b-', alpha=line_alpha, linewidth=line_width)

    # Mark start and end points
    if show_start_end and len(x_clean) > 0:
        ax.scatter([x_clean[0]], [y_clean[0]], c='green', s=100, marker='o',
                   label='Start', zorder=5)
        ax.scatter([x_clean[-1]], [y_clean[-1]], c='red', s=100, marker='s',
                   label='End', zorder=5)
        ax.legend()

    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.set_title(title)
    ax.set_aspect('equal')

    return fig, ax


def create_timeseries_plot(
    timestamps: np.ndarray,
    signals: List[Tuple[np.ndarray, str]],
    title: str = 'Eye Tracking Time Series',
    xlabel: str = 'Time (seconds)',
    ylabel: str = 'Value',
    figsize: Tuple[int, int] = (14, 6),
    normalize_time: bool = True,
    ax: Optional['plt.Axes'] = None,
) -> Tuple['plt.Figure', 'plt.Axes']:
    """
    Create a time series plot of one or more signals.

    Args:
        timestamps: Time values for x-axis.
        signals: List of (data_array, label) tuples to plot.
        title: Plot title.
        xlabel: X-axis label.
        ylabel: Y-axis label.
        figsize: Figure size as (width, height).
        normalize_time: If True, start time from 0.
        ax: Optional existing axes to plot on.

    Returns:
        Tuple of (figure, axes).

    Example:
        >>> signals = [
        ...     (left_pupil, 'Left Pupil'),
        ...     (right_pupil, 'Right Pupil'),
        ... ]
        >>> fig, ax = create_timeseries_plot(timestamps, signals)
        >>> plt.show()
    """
    _check_matplotlib()

    # Create figure if needed
    if ax is None:
        fig, ax = plt.subplots(figsize=figsize)
    else:
        fig = ax.get_figure()

    # Normalize time if requested
    t = timestamps.copy()
    if normalize_time and len(t) > 0:
        t = t - t[0]

    # Plot each signal
    for data, label in signals:
        ax.plot(t, data, label=label, alpha=0.8)

    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.set_title(title)
    ax.legend()
    ax.grid(True, alpha=0.3)

    return fig, ax


def create_fixation_plot(
    x: np.ndarray,
    y: np.ndarray,
    fixations: List,
    title: str = 'Fixations',
    xlabel: str = 'X Position',
    ylabel: str = 'Y Position',
    figsize: Tuple[int, int] = (10, 8),
    duration_scale: float = 100.0,
    ax: Optional['plt.Axes'] = None,
) -> Tuple['plt.Figure', 'plt.Axes']:
    """
    Create a plot showing fixation locations with duration encoded as size.

    Args:
        x: X coordinates of all gaze points.
        y: Y coordinates of all gaze points.
        fixations: List of Fixation objects (with centroid_x, centroid_y, duration).
        title: Plot title.
        xlabel: X-axis label.
        ylabel: Y-axis label.
        figsize: Figure size as (width, height).
        duration_scale: Scale factor for fixation circle size.
        ax: Optional existing axes to plot on.

    Returns:
        Tuple of (figure, axes).

    Example:
        >>> from eyelean_analysis import detect_eye_movements
        >>> movements = detect_eye_movements(x, y, timestamps)
        >>> fig, ax = create_fixation_plot(x, y, movements['fixations'])
        >>> plt.show()
    """
    _check_matplotlib()

    # Create figure if needed
    if ax is None:
        fig, ax = plt.subplots(figsize=figsize)
    else:
        fig = ax.get_figure()

    # Plot scanpath in background
    mask = ~(np.isnan(x) | np.isnan(y))
    ax.plot(x[mask], y[mask], 'gray', alpha=0.2, linewidth=0.5)

    # Plot fixations
    if fixations:
        fix_x = [f.centroid_x for f in fixations]
        fix_y = [f.centroid_y for f in fixations]
        fix_dur = [f.duration * duration_scale for f in fixations]

        scatter = ax.scatter(fix_x, fix_y, s=fix_dur, c=range(len(fixations)),
                            cmap='plasma', alpha=0.7, edgecolors='black')

        cbar = plt.colorbar(scatter, ax=ax)
        cbar.set_label('Fixation Order')

    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.set_title(f'{title} (n={len(fixations)})')
    ax.set_aspect('equal')

    return fig, ax


def create_pupil_plot(
    timestamps: np.ndarray,
    left_pupil: Optional[np.ndarray] = None,
    right_pupil: Optional[np.ndarray] = None,
    title: str = 'Pupil Diameter Over Time',
    figsize: Tuple[int, int] = (14, 5),
    show_mean: bool = True,
) -> Tuple['plt.Figure', 'plt.Axes']:
    """
    Create a time series plot of pupil diameter.

    Args:
        timestamps: Time values.
        left_pupil: Left eye pupil diameter values.
        right_pupil: Right eye pupil diameter values.
        title: Plot title.
        figsize: Figure size.
        show_mean: If True and both eyes present, show mean line.

    Returns:
        Tuple of (figure, axes).
    """
    _check_matplotlib()

    signals = []
    if left_pupil is not None:
        signals.append((left_pupil, 'Left Pupil'))
    if right_pupil is not None:
        signals.append((right_pupil, 'Right Pupil'))

    if show_mean and left_pupil is not None and right_pupil is not None:
        mean_pupil = np.nanmean([left_pupil, right_pupil], axis=0)
        signals.append((mean_pupil, 'Mean'))

    return create_timeseries_plot(
        timestamps,
        signals,
        title=title,
        ylabel='Pupil Diameter (mm)',
        figsize=figsize,
    )
