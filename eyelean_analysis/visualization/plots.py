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


# ---------------------------------------------------------------------------
# Gaze heatmaps (2D angular, 3D world-space, per-object AOI)
# ---------------------------------------------------------------------------

def _smooth_histogram2d(
    x: np.ndarray, y: np.ndarray, bins: int,
    x_range: Optional[Tuple[float, float]] = None,
    y_range: Optional[Tuple[float, float]] = None,
    sigma_bins: float = 1.5,
) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """2D histogram with separable Gaussian smoothing.

    Returns ``(H, x_edges, y_edges)`` with H shaped (n_y, n_x) for
    ``imshow``-style display (note the axis swap relative to numpy's
    default ``histogram2d`` output).
    """
    mask = ~(np.isnan(x) | np.isnan(y))
    x, y = x[mask], y[mask]
    if x_range is None:
        x_range = (float(np.min(x)), float(np.max(x))) if len(x) else (0.0, 1.0)
    if y_range is None:
        y_range = (float(np.min(y)), float(np.max(y))) if len(y) else (0.0, 1.0)
    H, xe, ye = np.histogram2d(x, y, bins=bins, range=[x_range, y_range])
    H = H.T  # match imshow orientation

    # Separable Gaussian smoothing (avoid scipy dependency).
    if sigma_bins > 0 and H.size:
        radius = max(1, int(np.ceil(3 * sigma_bins)))
        k = np.arange(-radius, radius + 1, dtype=float)
        kernel = np.exp(-(k ** 2) / (2 * sigma_bins ** 2))
        kernel /= kernel.sum()
        # Convolve along each axis with reflect padding.
        for axis in (0, 1):
            H = np.apply_along_axis(
                lambda v: np.convolve(np.pad(v, radius, mode='reflect'), kernel, mode='valid'),
                axis, H,
            )
    return H, xe, ye


def gaze_heatmap_2d(
    yaw: np.ndarray,
    pitch: np.ndarray,
    bins: int = 60,
    sigma_bins: float = 1.5,
    yaw_range: Optional[Tuple[float, float]] = None,
    pitch_range: Optional[Tuple[float, float]] = None,
    cmap: str = 'hot',
    title: str = 'Gaze heatmap (angular)',
    figsize: Tuple[int, int] = (10, 6),
    ax: Optional['plt.Axes'] = None,
) -> Tuple['plt.Figure', 'plt.Axes']:
    """Gaussian-smoothed 2D heatmap of gaze in angular (yaw, pitch) space.

    Use the combined-gaze yaw/pitch derived from ``CombinedDir_*``
    columns:

        yaw   = degrees(arctan2(dx, dz))
        pitch = -degrees(arcsin(clip(dy, -1, 1)))

    Args:
        yaw, pitch: per-sample yaw/pitch in degrees.
        bins: grid resolution.
        sigma_bins: Gaussian smoothing kernel σ, in bins. 0 disables
            smoothing (recovers the raw 2D histogram).
        yaw_range, pitch_range: explicit angular extents. Pass fixed
            values (e.g. (-60, 60), (-45, 45) for a typical HMD FOV)
            when comparing heatmaps across recordings.
    """
    _check_matplotlib()
    if ax is None:
        fig, ax = plt.subplots(figsize=figsize)
    else:
        fig = ax.get_figure()
    H, xe, ye = _smooth_histogram2d(
        np.asarray(yaw, dtype=float), np.asarray(pitch, dtype=float),
        bins=bins, x_range=yaw_range, y_range=pitch_range, sigma_bins=sigma_bins,
    )
    extent = (xe[0], xe[-1], ye[0], ye[-1])
    im = ax.imshow(H, origin='upper', extent=extent, aspect='auto',
                   cmap=cmap, interpolation='bilinear')
    ax.invert_yaxis()  # pitch positive = up
    ax.set_xlabel('yaw (deg)')
    ax.set_ylabel('pitch (deg)')
    ax.set_title(title)
    cbar = plt.colorbar(im, ax=ax, fraction=0.046, pad=0.04)
    cbar.set_label('density (smoothed)')
    return fig, ax


def gaze_heatmap_3d_projections(
    x: np.ndarray, y: np.ndarray, z: np.ndarray,
    bins: int = 50,
    sigma_bins: float = 1.5,
    cmap: str = 'hot',
    title: str = '3D gaze (vergence point) — orthographic projections',
    figsize: Tuple[int, int] = (13, 4),
) -> Tuple['plt.Figure', np.ndarray]:
    """Three orthographic projections of a 3D gaze point cloud.

    Plots top-down (X-Z, looking down at the floor), front (X-Y,
    looking at the back wall), and side (Z-Y, looking from the right)
    Gaussian-smoothed heatmaps of a vergence-point cloud. Static and
    legible — preferable to a 3D scatter that rotates badly in a
    notebook.

    Args:
        x, y, z: world-space coordinates from
            ``VergencePoint_X/Y/Z``. Pass only rows where
            ``HasValidVergence`` is true.
    """
    _check_matplotlib()
    x = np.asarray(x, dtype=float); y = np.asarray(y, dtype=float); z = np.asarray(z, dtype=float)
    mask = ~(np.isnan(x) | np.isnan(y) | np.isnan(z))
    x, y, z = x[mask], y[mask], z[mask]

    fig, axes = plt.subplots(1, 3, figsize=figsize)
    if len(x) == 0:
        for a, lab in zip(axes, ('Top (X–Z)', 'Front (X–Y)', 'Side (Z–Y)')):
            a.set_title(f'{lab}\n(no valid samples)')
            a.set_xticks([]); a.set_yticks([])
        fig.suptitle(title)
        plt.tight_layout()
        return fig, axes

    panels = [
        (x, z, 'X (m, right→)', 'Z (m, forward→)', 'Top (X–Z)',   False),
        (x, y, 'X (m, right→)', 'Y (m, up→)',     'Front (X–Y)', False),
        (z, y, 'Z (m, forward→)', 'Y (m, up→)',   'Side (Z–Y)',  False),
    ]
    for ax, (u, v, xl, yl, lab, invert) in zip(axes, panels):
        H, ue, ve = _smooth_histogram2d(u, v, bins=bins, sigma_bins=sigma_bins)
        ax.imshow(H, origin='upper', extent=(ue[0], ue[-1], ve[0], ve[-1]),
                  aspect='equal', cmap=cmap, interpolation='bilinear')
        ax.invert_yaxis()
        ax.set_xlabel(xl); ax.set_ylabel(yl); ax.set_title(lab, fontsize=10)
    fig.suptitle(title)
    plt.tight_layout()
    return fig, axes


def list_gazed_objects(
    df: 'pd.DataFrame',
    min_samples: int = 5,
    exclude: Optional[Tuple[str, ...]] = None,
) -> 'pd.DataFrame':
    """Tally per-object dwell counts from the ``GazedObjectName`` column.

    Args:
        df: Eye_lean main CSV as a DataFrame (must have
            ``GazedObjectName`` and a timestamp/delta column).
        min_samples: drop objects with fewer than this many samples.
        exclude: object names to drop (e.g. wall identifiers).

    Returns:
        DataFrame with columns ``object_name``, ``n_samples``,
        ``dwell_seconds``, sorted by ``n_samples`` desc.
    """
    import pandas as pd
    col = 'GazedObjectName' if 'GazedObjectName' in df.columns else 'gazed_object_name'
    if col not in df.columns:
        return pd.DataFrame(columns=['object_name', 'n_samples', 'dwell_seconds'])
    sr = df[col].fillna('').astype(str).str.strip()
    sr = sr[sr != '']
    if exclude:
        sr = sr[~sr.isin(set(exclude))]
    counts = sr.value_counts()
    counts = counts[counts >= int(min_samples)]
    # Dwell seconds from frame deltas if available.
    dt_col = next((c for c in ('DeltaTime', 'delta_time') if c in df.columns), None)
    if dt_col is None:
        dwell = counts * float('nan')
    else:
        dwell = df.groupby(col)[dt_col].sum().reindex(counts.index)
    out = pd.DataFrame({
        'object_name': counts.index,
        'n_samples':   counts.values,
        'dwell_seconds': dwell.values,
    })
    return out.reset_index(drop=True)


def aoi_heatmap(
    df: 'pd.DataFrame',
    object_name: str,
    bins: int = 50,
    sigma_bins: float = 1.5,
    yaw_range: Optional[Tuple[float, float]] = None,
    pitch_range: Optional[Tuple[float, float]] = None,
    cmap: str = 'hot',
    figsize: Tuple[int, int] = (10, 6),
    ax: Optional['plt.Axes'] = None,
) -> Tuple['plt.Figure', 'plt.Axes', int]:
    """Angular gaze heatmap restricted to samples that hit ``object_name``.

    Filters the main-CSV DataFrame to rows where
    ``GazedObjectName == object_name``, derives yaw/pitch from
    ``CombinedDir_*``, and plots a Gaussian-smoothed 2D heatmap. Shows
    where the participant's eyes were pointing *when looking at* the
    object — useful both for catching tracking offsets (the heatmap
    should center on the object's apparent direction) and for spotting
    "looking-through" behaviours (gaze that briefly clips through one
    object on its way to another).

    Args:
        df: Eye_lean main CSV as a DataFrame.
        object_name: value to filter against (case-sensitive, matches
            the Unity GameObject.name written into ``GazedObjectName``).
        yaw_range, pitch_range: pass fixed extents to align heatmaps
            across objects in a multi-panel figure.

    Returns:
        ``(figure, axes, n_samples_used)``.
    """
    _check_matplotlib()
    col = 'GazedObjectName' if 'GazedObjectName' in df.columns else 'gazed_object_name'
    if col not in df.columns:
        raise ValueError(f"DataFrame has no {col!r} column")
    sub = df[df[col].astype(str) == str(object_name)]
    needed = ('CombinedDir_X', 'CombinedDir_Y', 'CombinedDir_Z')
    snake = ('combined_dir_x', 'combined_dir_y', 'combined_dir_z')
    cols = needed if all(c in df.columns for c in needed) else (snake if all(c in df.columns for c in snake) else None)
    if cols is None:
        raise ValueError("DataFrame missing CombinedDir_X/Y/Z columns")
    dx = sub[cols[0]].to_numpy(dtype=float)
    dy = sub[cols[1]].to_numpy(dtype=float)
    dz = sub[cols[2]].to_numpy(dtype=float)
    yaw   = np.degrees(np.arctan2(dx, dz))
    pitch = -np.degrees(np.arcsin(np.clip(dy, -1, 1)))
    fig, ax = gaze_heatmap_2d(
        yaw, pitch, bins=bins, sigma_bins=sigma_bins,
        yaw_range=yaw_range, pitch_range=pitch_range, cmap=cmap,
        title=f"AOI heatmap — {object_name}  ({len(yaw)} samples)",
        figsize=figsize, ax=ax,
    )
    return fig, ax, int(len(yaw))
