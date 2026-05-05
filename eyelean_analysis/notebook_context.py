"""Plug-and-play data discovery for the example notebooks.

Every notebook in `notebooks/examples/` opens with a single call:

    from eyelean_analysis import notebook_context
    ctx = notebook_context()

`ctx` carries paths to the main CSV, the v1.2 sidecars, the most recent
calibration profile JSON, the SampleExperiment results JSON, and a
pre-loaded `EyeLeanData`. Researchers running the notebooks against
their own recordings change nothing — the helper auto-discovers their
data.

Discovery order:

  1. Explicit `csv=` argument.
  2. `EYELEAN_CSV` environment variable.
  3. Most-recent main `EyeTracking_*.csv` (sidecars excluded) under
     a `Logs/` directory found by walking up from `os.getcwd()` and
     also from this file's location.
  4. Bundled sample at
     `Eye_lean_Unity_Project/Eye_lean/Assets/StreamingAssets/
      EyeTracking_20260503_160712.csv`.

Any of those steps can be the canonical source — a researcher who just
`git clone`d the repo gets the bundled sample; one who's recorded into
`Logs/` gets their latest session.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Union

from .data.loader import (
    EyeLeanData,
    load_eyetracking,
    read_csv_metadata,
    _scene_sidecar_paths,
)


_BUNDLED_SAMPLE_RELATIVE = (
    "Eye_lean_Unity_Project/Eye_lean/Assets/StreamingAssets/"
    "EyeTracking_20260503_160712.csv"
)


@dataclass
class NotebookContext:
    """Container for everything a notebook typically needs.

    Attributes:
        csv_path: Main gaze CSV. Always present.
        scene_state_path: v1.2 `_SceneState.csv` if it exists alongside
            the main CSV; else None.
        scene_events_path: v1.2 `_SceneEvents.csv` if it exists; else None.
        profile_path: Most recent `EyeTrackingProfile` JSON found in the
            same directory as the CSV; None if none.
        results_path: Most recent `experiment_results_*.json` in the
            same directory; None if none.
        repo_root: Best-guess repository root (directory containing
            `eyelean_analysis/`). May be None if the package was
            installed and is being run from outside a checkout.
        data: Pre-loaded `EyeLeanData`.
        metadata: Dict from `read_csv_metadata(csv_path)`.
        source: One of `"argument"`, `"env"`, `"logs"`, `"bundled"` —
            tells the user where the discovery landed.
    """

    csv_path: Path
    scene_state_path: Optional[Path]
    scene_events_path: Optional[Path]
    profile_path: Optional[Path]
    results_path: Optional[Path]
    repo_root: Optional[Path]
    data: EyeLeanData
    metadata: dict
    source: str

    def __repr__(self) -> str:
        def _fmt(p: Optional[Path]) -> str:
            return p.name if p else "(none)"
        return (
            f"NotebookContext(\n"
            f"  source={self.source!r}\n"
            f"  csv={self.csv_path.name}\n"
            f"  scene_state={_fmt(self.scene_state_path)}\n"
            f"  scene_events={_fmt(self.scene_events_path)}\n"
            f"  profile={_fmt(self.profile_path)}\n"
            f"  results={_fmt(self.results_path)}\n"
            f"  n_samples={len(self.data.df)}\n"
            f"  duration={self.data.duration:.1f}s\n"
            f")"
        )


def _walk_for_logs(start: Path) -> Optional[Path]:
    """Walk up from `start` looking for a directory named `Logs` that
    contains at least one main `EyeTracking_*.csv` (not a sidecar)."""
    p = start.resolve()
    seen = set()
    while p != p.parent and p not in seen:
        seen.add(p)
        candidate = p / "Logs"
        if candidate.is_dir() and _newest_main_csv(candidate) is not None:
            return candidate
        p = p.parent
    return None


def _newest_main_csv(directory: Path) -> Optional[Path]:
    """Return the most-recent (mtime) `EyeTracking_*.csv` in `directory`,
    excluding the v1.2 `_SceneState.csv` / `_SceneEvents.csv` sidecars."""
    candidates = [
        c for c in directory.glob("EyeTracking_*.csv")
        if not (c.stem.endswith("_SceneState") or c.stem.endswith("_SceneEvents"))
    ]
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def _find_repo_root() -> Optional[Path]:
    """Walk up from this file looking for a directory containing both
    `eyelean_analysis/` and a sibling that suggests a repo checkout."""
    here = Path(__file__).resolve().parent
    for candidate in [here, *here.parents]:
        if (candidate / "eyelean_analysis" / "__init__.py").is_file():
            return candidate
    return None


def _find_bundled_sample(repo_root: Optional[Path]) -> Optional[Path]:
    if repo_root is None:
        return None
    p = repo_root / _BUNDLED_SAMPLE_RELATIVE
    return p if p.is_file() else None


def _pair_profile(csv_dir: Path) -> Optional[Path]:
    """Find the newest non-`experiment_results_*.json` next to the CSV."""
    candidates = [
        c for c in csv_dir.glob("*.json")
        if not c.name.startswith("experiment_results_")
    ]
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def _pair_results(csv_dir: Path) -> Optional[Path]:
    candidates = list(csv_dir.glob("experiment_results_*.json"))
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def notebook_context(
    csv: Union[str, Path, None] = None,
    *,
    require_sidecars: bool = False,
    require_profile: bool = False,
    require_results: bool = False,
    load: bool = True,
    **load_kwargs,
) -> NotebookContext:
    """Discover the canonical recording for the running notebook and
    load it.

    Args:
        csv: Explicit CSV path; bypasses discovery if given.
        require_sidecars: Raise if `_SceneState.csv` / `_SceneEvents.csv`
            are absent. Default False (sidecars are optional).
        require_profile: Raise if no calibration profile JSON sits next
            to the CSV.
        require_results: Raise if no `experiment_results_*.json` sits
            next to the CSV.
        load: If False, build the context but skip the CSV load. The
            `data` and `metadata` fields will still be populated; this
            flag is for tests that want to assert path discovery
            without paying for pandas read.
        **load_kwargs: Forwarded to `load_eyetracking`.

    Returns:
        A populated `NotebookContext`.

    Raises:
        FileNotFoundError: If discovery fails and no `csv=` was given,
            or if a `require_*` flag isn't satisfied.
    """
    repo_root = _find_repo_root()
    source: str

    if csv is not None:
        csv_path = Path(csv).resolve()
        if not csv_path.is_file():
            raise FileNotFoundError(f"CSV not found: {csv_path}")
        source = "argument"
    elif os.environ.get("EYELEAN_CSV"):
        csv_path = Path(os.environ["EYELEAN_CSV"]).resolve()
        if not csv_path.is_file():
            raise FileNotFoundError(
                f"EYELEAN_CSV={csv_path} does not exist"
            )
        source = "env"
    else:
        # Walk up from cwd, then from the package location.
        logs_dir = _walk_for_logs(Path(os.getcwd()))
        if logs_dir is None and repo_root is not None:
            logs_dir = _walk_for_logs(repo_root)
        if logs_dir is not None:
            csv_path = _newest_main_csv(logs_dir)
            assert csv_path is not None  # _walk_for_logs guarantees
            source = "logs"
        else:
            bundled = _find_bundled_sample(repo_root)
            if bundled is None:
                raise FileNotFoundError(
                    "No EyeTracking CSV found. Pass csv=<path>, set "
                    "EYELEAN_CSV, drop a recording into a Logs/ "
                    "directory under your project, or run from a repo "
                    "checkout that contains the bundled sample."
                )
            csv_path = bundled
            source = "bundled"

    state_path, events_path = _scene_sidecar_paths(csv_path)
    state_path = state_path if state_path.is_file() else None
    events_path = events_path if events_path.is_file() else None

    if require_sidecars and (state_path is None or events_path is None):
        raise FileNotFoundError(
            f"require_sidecars=True but sidecars missing for {csv_path.name} "
            f"(state={state_path}, events={events_path})"
        )

    profile_path = _pair_profile(csv_path.parent)
    results_path = _pair_results(csv_path.parent)

    if require_profile and profile_path is None:
        raise FileNotFoundError(
            f"require_profile=True but no profile JSON next to {csv_path.name}"
        )
    if require_results and results_path is None:
        raise FileNotFoundError(
            f"require_results=True but no experiment_results_*.json next to "
            f"{csv_path.name}"
        )

    metadata = read_csv_metadata(csv_path)
    if load:
        data = load_eyetracking(csv_path, **load_kwargs)
    else:
        # Build a minimal stub so tests can introspect paths without
        # paying for pandas. We still populate metadata.
        data = None  # type: ignore[assignment]

    return NotebookContext(
        csv_path=csv_path,
        scene_state_path=state_path,
        scene_events_path=events_path,
        profile_path=profile_path,
        results_path=results_path,
        repo_root=repo_root,
        data=data,
        metadata=metadata,
        source=source,
    )
