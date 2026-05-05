# RIPA Monitor — real-time pupillary cognitive-load index

Per-frame cognitive-load index from the live pupil-diameter stream.
Published to listeners and to the `LiveLoadIndex` CSV column. Metric
is **RIPA2** (Jayawardena, Jayawardana & Gwizdka 2025). The bootstrap
auto-spawns a monitor in every scene at `AfterSceneLoad` and attaches
a `RIPACSVColumn` so the CSV gains the column without manual wiring.

## Audience

Researchers using or interpreting the `LiveLoadIndex` column.

## Prerequisites

- An eye tracker in the scene (any scene calling
  `EyeTrackerFactory.GetEyeTracker()` directly or via `EyeTracker`).
- A `SessionRecorder` if you want the CSV column.
- Cite RIPA2 when publishing data using `LiveLoadIndex` or any
  `RIPAMonitor` output. See `ACKNOWLEDGMENTS.md`.

---

## What RIPA2 measures

Two Savitzky–Golay first-derivative filters at carefully chosen
cutoffs separate the very-low-frequency (luminance-driven) and
low-frequency (cognitively-driven) components of the pupil signal:

```
RIPA2[t] = (SG_LF · P[t ± M_LF])^2 − (SG_VLF · P[t ± M_VLF])^2
```

Output is clipped to `[0, 1.5]` and smoothed with a trailing moving
average (1.5 s by default).

| | VLF filter | LF filter |
|---|---|---|
| Target cutoff (paper) | 0.29 Hz | 4 Hz |
| Polynomial order N | 2 | 4 |
| Half-width M (60 Hz HMD) | ≈ 98 | ≈ 13 |
| Window in seconds | ≈ 3.3 s | ≈ 0.45 s |

Filter half-widths are auto-derived at startup from the live sample
rate (`IEyeTracker.SamplingRateHz`) via Schäfer's approximation
`fc ≈ (N+1) / (3.2 M − 4.6)`. Override via Inspector for a different
band.

For the algorithm derivation and pseudocode see
[ALGORITHMS.md](../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md).

### Why RIPA2, not LHIPA

v1.0–v1.2 used a Symlets-4 DWT approximation of LHIPA (Duchowski
2018). LHIPA is an offline algorithm — it operates on a long fixed
window and was never validated for streaming use. RIPA2 was designed
for real-time use against the same physiological bands and
outperforms RIPA in naturalistic tasks. The Python
`eyelean_analysis.metrics.lhipa` module is retained for offline LHIPA
comparisons that the paper validates RIPA2 against.

---

## How to read the values

- Range is the paper-defined `[0, 1.5]` clip window. Treat values as
  ordinal across phases within a session, not as absolute units.
- Higher means more cognitive load. The HUD bar in `ExperimentUI`
  fills `0..1` of `load / 1.5` and tints green → amber → red.
- Expect zeros during the buffer warm-up (see next section).
- Compare values *within a participant, within a session*. Pupil
  oscillation magnitude varies between people and across sessions
  due to luminance, alertness, and pupil-size baseline.

---

## When to expect zeros: buffer warm-up

The VLF Savitzky–Golay window needs `2 · M_VLF + 1` samples (~197
at 60 Hz, ≈ 3.3 s) before the analyzer reports `IsValid = true`.
During warm-up, `CurrentLoad` returns 0 and the CSV column writes 0.
Look for the first-valid log line to confirm:

```
[RIPAMonitor] First valid output: raw=<v> smoothed=<v> (after <n> pushed samples).
```

`RIPAMonitor.cs:221`. After this point, the load values are real.

---

## Adding RIPA to a custom scene

1. Open the scene. Confirm an eye tracker is present.
2. Press Play. The auto-bootstrap (`RIPAMonitorBootstrap`) creates a
   `RIPAMonitor` GameObject and attaches a `RIPACSVColumn`.
3. To opt out for a particular scene, set
   `RIPAMonitorBootstrap.DisableAutoSpawn = true` before the scene
   loads.

### Verify

In `adb logcat -s Unity` look for:

```
[RIPAMonitor] Active. fs=60.0 Hz, M_VLF=98 (window 197), M_LF=13 (window 27), buffer=240 samples, smoothing=90 samples.
```

`RIPAMonitor.cs:161`. The `LiveLoadIndex` column should appear in
the recorded CSV header between `DataSampleCount` and
`GazedObjectName`.

---

## Reading the load value in code

```csharp
using EyeTracking.Metrics;

void Update() {
    var monitor = RIPAMonitor.Instance;
    if (monitor != null && monitor.IsValid) {
        float load = monitor.CurrentLoad;     // smoothed, clipped to [0, 1.5]
        // float raw = monitor.CurrentRawLoad; // unsmoothed, clipped
    }
}

void OnEnable() {
    RIPAMonitor.Instance?.OnLoadChanged.AddListener(HandleLoadChanged);
}
void OnDisable() {
    RIPAMonitor.Instance?.OnLoadChanged.RemoveListener(HandleLoadChanged);
}
void HandleLoadChanged(float newLoad) { /* ... */ }
```

`RIPAMonitor.Instance` is a scene-scoped singleton — it does not
survive scene reloads. Re-resolve `Instance` in `OnEnable` if your
component lives across scene transitions.

API surface (file: `Assets/Scripts/EyeTracking/Metrics/RIPAMonitor.cs`):

| Member | Line | Notes |
|---|---|---|
| `static RIPAMonitor Instance` | `:85` | Scene-scoped singleton. |
| `bool IsValid` | `:106` | True once the VLF window is filled. |
| `float CurrentLoad` | `:110` | Smoothed, clipped output. |
| `float CurrentRawLoad` | `:114` | Unsmoothed, clipped output. |
| `FloatEvent OnLoadChanged` | `:72` | Fires after every recompute, throttled by `publishIntervalSeconds`. |

---

## On-screen indicator

Three options, in increasing order of integration effort:

1. **`RIPAOverlay` (zero-setup)** — drop on any GameObject in any
   scene. Builds its own ScreenSpaceOverlay canvas + filled bar +
   label internally and binds to `RIPAMonitor.Instance`. Inspector
   exposes corner placement, size, display max, and label format.
   This is the path researchers should use when adding RIPA to an
   experiment that already has its own UI flow.
2. **`RIPAGauge` (drop-on-Image)** — author your own UI Image inside
   an existing canvas, attach `RIPAGauge`, and the gauge will drive
   `fillAmount` between 0 and 1 scaled by `DisplayMax` (default 1.5
   to match the paper clip range). Fill tint moves green → amber →
   red. Use this when you want the gauge inside an existing
   researcher-authored panel.
3. **Hand-rolled subscriber** — listen to
   `RIPAMonitor.Instance.OnLoadChanged` and update your own UI. See
   [Reading the load value in code](#reading-the-load-value-in-code).

```csharp
// Option 1 — zero-setup: one line in any scene.
gameObject.AddComponent<RIPAOverlay>();

// Option 2 — programmatic gauge inside an existing canvas:
var fill = canvasObj.AddComponent<Image>();
fill.type = Image.Type.Filled;
fill.fillMethod = Image.FillMethod.Horizontal;
var gauge = canvasObj.AddComponent<RIPAGauge>();
gauge.Bind(fill);
```

### Toggle in the bundled SampleExperiment

`ExperimentUI` builds an in-panel HUD strip on the left edge of the
experiment panel. Set `showRipaHud = false` on the inspector to hide
the strip without affecting recording — the `LiveLoadIndex` CSV
column still populates because `RIPAMonitor` keeps running.

---

## CSV column registration

`SessionRecorder.RegisterMetric(name, getter, format = "F4")` lets
any component contribute a CSV column. Order of registration is
preserved in the header; columns are inserted between
`DataSampleCount` and `GazedObjectName`.

```csharp
// Register a custom researcher-defined metric column:
var rec = FindFirstObjectByType<SessionRecorder>();
rec.RegisterMetric("MyCustomScore", () => CustomLogic.CurrentScore(), "F3");
```

`RIPACSVColumn` uses this API to register `LiveLoadIndex`
(`RIPACSVColumn.cs:47`). The bootstrap attaches the component
automatically; manual attachment is rarely needed.

Constraint: registration must happen *before* the recorder writes
its header (within the 2-second coord-origin grace window after
`Start`). Late registrations are rejected with an error log.

---

## Diagnostic: 5-second `[RIPAMonitor] Diag:` block

Every 5 seconds the monitor logs a diagnostic so any session that
wrote zero `LiveLoadIndex` can be inspected post-hoc
(`RIPAMonitor.cs:211`):

```
[RIPAMonitor] Diag: pushed=<n> skippedNaN=<n> buffered=<m>/<window> valid=<bool> raw=<v> smoothed=<v>
```

Read it as:

- `pushed` — pupil samples handed to the analyzer.
- `skippedNaN` — samples rejected as NaN (blink artifact, dropped
  frame).
- `buffered=m/window` — VLF ring fill. The analyzer is `valid` only
  once `m >= window`.
- `raw` / `smoothed` — current outputs after the warm-up.

If `pushed` stays at zero, the eye tracker is not feeding pupil
samples. If `skippedNaN` dominates, the participant is blinking
heavily or the tracker is losing the eye.

---

## Python parity

`eyelean_analysis.metrics.ripa2.calculate_ripa2` is a numpy-only
implementation that matches the Unity computation within IEEE-754
float epsilon:

```python
from eyelean_analysis.metrics.ripa2 import calculate_ripa2

# Pupil column from your Eye_lean CSV (in mm, single eye or averaged).
result = calculate_ripa2(
    pupil_mm,
    sample_rate=60.0,
    vlf_cutoff_hz=0.29, vlf_poly_order=2,
    lf_cutoff_hz=4.0,   lf_poly_order=4,
    smoothing_seconds=1.5,
)
plt.plot(timestamps, result.ripa2_smoothed)
```

Use this to validate the recorded `LiveLoadIndex` column post-hoc,
or to recompute the metric with different parameters (longer
smoothing, narrower bands) without re-recording.

---

## Inspector reference

| Field | Default | Notes |
|---|---|---|
| `enableMonitor` | true | Master switch. Toggle `RIPAMonitor.Enabled` at runtime. |
| `sampleRateOverrideHz` | 0 (auto) | Pin a fixed sample rate; 0 uses `IEyeTracker.SamplingRateHz`. |
| `vlfCutoffHz`, `vlfPolyOrder` | 0.29 Hz, N=2 | Paper VLF band. |
| `lfCutoffHz`, `lfPolyOrder` | 4 Hz, N=4 | Paper LF band. |
| `bufferSeconds` | 4 s | Must cover the VLF SG window (≥ 2·M_VLF + 1 samples). |
| `smoothingSeconds` | 1.5 s | Trailing moving average. Paper recommends 1–2 s. |
| `publishIntervalSeconds` | 0 (every recompute) | Throttle `OnLoadChanged` for HUDs. |

---

## References

- Jayawardena, G., Jayawardana, Y., & Gwizdka, J. (2025).
  Measuring Mental Effort in Real Time Using Pupillometry.
  *J. Eye Movement Research* 18(6), 70.
  <https://doi.org/10.3390/jemr18060070>
- Schäfer, R. W. (2011). What Is a Savitzky–Golay Filter?
  *IEEE Signal Processing Magazine* 28(4), 111–117.
- Duchowski, A. T. et al. (2018). The Index of Pupillary Activity:
  Measuring Cognitive Load vis-à-vis Task Difficulty with Pupil
  Oscillation. *CHI '18*. Offline LHIPA reference; the paper
  validates RIPA2 against this metric.
- Peysakhovich, V., Causse, M., et al. (2017). Frequency analysis of
  a task-evoked pupillary response. *Int. J. Psychophysiology* 112,
  40–45. Source for the 0–1.6 / 1.6–4 Hz VLF/LF split.
- Medeiros, J., Couceiro, R., et al. (2021). Software code complexity
  assessment using EEG features. *Sensors* 21, 5128. Source for the
  optimal 0.06–0.29 Hz / 0.29–0.49 Hz pupil band.
- Algorithm derivation:
  [ALGORITHMS.md](../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md).
