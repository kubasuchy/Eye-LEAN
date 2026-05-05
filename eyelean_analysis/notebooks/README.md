# Notebook suite

Plug-and-play analysis notebooks for Eye_lean recordings. Each opens
with the same one-line bootstrap (`ctx = ela.notebook_context()`),
auto-discovers your data, and runs end-to-end against the bundled v1.2
sample on a fresh checkout — so you can `Run All` before you've made
your own recording.

## Getting your data into the notebooks

`notebook_context()` resolves the canonical CSV by trying, in order:

1. **Explicit path** — `notebook_context(csv="path/to/EyeTracking_*.csv")`.
2. **`EYELEAN_CSV` environment variable** — handy for batch shells.
3. **`Logs/` walk** — most-recent main `EyeTracking_*.csv` (sidecars
   excluded) under any `Logs/` directory found by walking up from the
   notebook's working directory or the package install.
4. **Bundled sample** — falls back to a v1.2 SampleExperiment recording
   shipped at `Eye_lean_Unity_Project/Eye_lean/Assets/StreamingAssets/`.

The same context also exposes:

- `ctx.scene_state_path` / `ctx.scene_events_path` — v1.2 sidecars (None if missing).
- `ctx.profile_path` — most recent `EyeTrackingProfile` JSON next to the CSV.
- `ctx.results_path` — most recent `experiment_results_*.json`.
- `ctx.data` — pre-loaded `EyeLeanData`.
- `ctx.metadata` — dict from `read_csv_metadata` (CoordinateOrigin, Profile, FileVersion, …).
- `ctx.source` — `'argument'`, `'env'`, `'logs'`, or `'bundled'`.

## The notebooks

| # | File | Purpose | Needs |
|---|---|---|---|
| 01 | [01_quickstart.ipynb](examples/01_quickstart.ipynb) | "Does this work?" — package import + one-line summary + scanpath + pupil. Start here. | core |
| 02 | [02_explore_session.ipynb](examples/02_explore_session.ipynb) | End-to-end walkthrough of one recording — quality, classifier, pupil/LHIPA, per-phase report, post-hoc preview. | core, matplotlib, PyWavelets |
| 03 | [03_data_quality.ipynb](examples/03_data_quality.ipynb) | Decide whether a recording is worth analyzing. Validity, gaps, sample-rate stability, blink/pupil sanity, pass/fail thresholds. | core, matplotlib |
| 04 | [04_eye_movements.ipynb](examples/04_eye_movements.ipynb) | I-VT classifier deep dive — velocity-threshold sweep, fixation/saccade stats, scanpath, K-coefficient attention typing. | core, matplotlib |
| 05 | [05_pupil_lhipa.ipynb](examples/05_pupil_lhipa.ipynb) | Cognitive load on the pupil signal — whole-recording LHIPA, sliding window, baseline comparison. | core, matplotlib, **PyWavelets** |
| 06 | [06_sample_experiment.ipynb](examples/06_sample_experiment.ipynb) | Per-phase report against the bundled SampleExperiment, joined with `experiment_results_*.json`. Phase-segmented scanpath + per-phase metric bars. | core, matplotlib |
| 07 | [07_scene_sidecars.ipynb](examples/07_scene_sidecars.ipynb) | v1.2 scene-state + events sidecars. Decoded Config payloads, event-type histogram, Spawn/Despawn lifetimes, gaze × scene-state join. | core, matplotlib, v1.2 recording |
| 08 | [08_posthoc_correction.ipynb](examples/08_posthoc_correction.ipynb) | Re-apply a saved `EyeTrackingProfile` to a recording. Angular shift histogram, before/after metric diff, compounding-offsets foot-gun warning. | core, matplotlib, profile JSON |
| 09 | [09_batch_processing.ipynb](examples/09_batch_processing.ipynb) | N CSVs → one summary DataFrame. Custom analyzer pattern, per-participant aggregation, summary export. | core |

## Running

Smoke-test from the command line — every notebook should execute
cleanly against the bundled sample:

```bash
for nb in eyelean_analysis/notebooks/examples/0*.ipynb; do
  python3 -m nbconvert --to notebook --execute "$nb" \
    --output /tmp/$(basename "$nb") \
    --ExecutePreprocessor.timeout=240
done
```

Interactive use:

```bash
pip install -e ".[jupyter]"
jupyter lab eyelean_analysis/notebooks/examples/
```

## Pointing at a different recording

The cleanest pattern is one line at the top of any notebook:

```python
ctx = ela.notebook_context(csv="/path/to/your/EyeTracking_session.csv")
```

For a directory full of recordings, set the env var and re-run:

```bash
export EYELEAN_CSV=~/data/p07/EyeTracking_20260601_143022.csv
jupyter lab ...
```

To override only the profile or results paths after auto-discovery:

```python
ctx = ela.notebook_context()
ctx.profile_path = Path("/path/to/profile.json")
# subsequent cells use the override
```

## Run order recommendation

- **First time on a fresh checkout:** 01 → 02. That confirms install
  and shows you the data model.
- **After a hardware session:** 03 (decide if data is good) → 02
  (overall view) → 06 if it's a SampleExperiment recording.
- **Methods deep dive:** 04 (eye movements) and 05 (pupil/LHIPA) are
  the canonical references for the two algorithms most often customized.
- **Multi-participant studies:** 09 once your recordings live in a
  shared directory.
- **Custom experiments built on Eye_lean:** 07 (scene sidecars) is the
  bridge between gaze and your custom Recordables/Events.

## Adding your own notebook

Use the same skeleton:

```python
import os, sys
from pathlib import Path

# Allow opening this notebook from a checkout without `pip install -e`.
_here = Path(os.getcwd()).resolve()
for _candidate in [_here, *_here.parents]:
    if (_candidate / "eyelean_analysis" / "__init__.py").is_file():
        if str(_candidate) not in sys.path:
            sys.path.insert(0, str(_candidate))
        break

import numpy as np, pandas as pd, matplotlib.pyplot as plt
import eyelean_analysis as ela

ctx = ela.notebook_context()
print(ctx)
```

Then build on `ctx.data.df`, `ctx.scene_state_path`, etc. If your
notebook genuinely needs a profile JSON or sidecars, declare it
explicitly:

```python
ctx = ela.notebook_context(require_profile=True, require_sidecars=True)
```

The require-flags fail loudly with a clear error message instead of
having a downstream cell crash.
