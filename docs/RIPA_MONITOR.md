# RIPA Monitor — real-time pupillary cognitive-load index

Per-frame cognitive-load index from the live pupil-diameter stream, published
to listeners and to the `LiveLoadIndex` CSV column. The bootstrap auto-spawns
a monitor in every scene at `AfterSceneLoad` and attaches a `RIPACSVColumn`
so the CSV gains the column(s) without manual wiring.

> **v1.0.1**: The monitor now runs *multiple* cognitive-load detectors in
> parallel and writes a per-method CSV column for each one. **RIPA2 remains
> the default HUD method** (Jayawardena, Jayawardana & Gwizdka 2025), and the
> legacy `LiveLoadIndex` column is preserved as an alias of whichever method
> drives the HUD. Three additional detectors from Duchowski 2026 ship as
> alternatives — see *[Alternative methods](#alternative-methods-duchowski-2026)*.
> On the VIVE Focus Vision, RIPA2 is the recommended HUD method; the LF/HF
> methods saturate because the device's pupil stream is pre-smoothed by the
> driver. They're recorded anyway for cross-device validation.

## Prerequisites

- An eye tracker in the scene (any scene calling
  `EyeTrackerFactory.GetEyeTracker()` directly or via `EyeTracker`).
- A `SessionRecorder` if you want the CSV column.
- Cite RIPA2 when publishing data using `LiveLoadIndex` — see
  `ACKNOWLEDGMENTS.md`.

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
[RIPAMonitor] First valid output (<method>): raw=<v> smoothed=<v> (after <n> pushed samples).
```

After this point, the load values for that detector are real.

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
[RIPAMonitor] RIPA2 active. fs=60.0 Hz, M_VLF=98 (window 197), M_LF=13 (window 27), buffer=240 samples, smoothing=90 samples.
[RIPAMonitor] Butterworth active. fs=60.0 Hz, LF=lowpass(<=1.6 Hz), HF=bandpass(1.6-4 Hz), order=4, power window=5.0s (300 samples), cap=200, scale=0.280.
[RIPAMonitor] FFT active. fs=60.0 Hz, buffer=2048 samples (34.1s), Δf=0.0293 Hz, cap=200, scale=0.280.
[RIPAMonitor] DWT active. fs=60.0 Hz, buffer=2048 samples (34.1s), max_level=8, cap=200, scale=0.280.
```

One `Active` line per enabled detector. The per-detector
`LiveLoadIndex_*` columns appear in the recorded CSV header between
`DataSampleCount` and `GazedObjectName`.

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

| Member | Notes |
|---|---|
| `static RIPAMonitor Instance` | Scene-scoped singleton. |
| `bool IsValid` | True once the displayed detector has produced a valid reading. |
| `float CurrentLoad` | Smoothed value of the displayed detector, in `[0, 1.5]`. |
| `float CurrentRawLoad` | Raw value of the displayed detector. |
| `CognitiveLoadMethod DisplayedMethod` | Which detector drives `CurrentLoad`/`OnLoadChanged`. |
| `ICognitiveLoadDetector GetDetector(method)` | Look up any enabled detector by method. |
| `IReadOnlyList<ICognitiveLoadDetector> EnabledDetectors` | Stable list, RIPA2-first order. |
| `FloatEvent OnLoadChanged` | Fires after every recompute, throttled by `publishIntervalSeconds`. |

---

## Alternative methods (Duchowski 2026)

v1.0.1 adds three frequency-domain cognitive-load detectors from
Duchowski 2026, "Real-Time Cognitive Load Measurement of Pupillary
Oscillation" (Proc. ACM CGIT 9(2) Article 23). All three compute an
LF/HF power ratio of the pupil signal (LF = 0–1.6 Hz, HF = 1.6–4 Hz):

| Method | Listing | Warm-up | Buffer | When to use |
|---|---|---|---|---|
| **RIPA2** (default) | — | ~3.3 s | 240 samples @ 60 Hz | Always-on HUD; robust on pre-smoothed HMD pupil streams. |
| **Butterworth IIR** | §4 / Listing 3 | 5 s | streaming + 5 s variance window | Paper's primary contribution — best real-time LF/HF detector for raw, high-bandwidth pupil. |
| **FFT periodogram** | §2 / Listing 1 | ~34 s | 2048 samples (power-of-two) | Offline-class spectral baseline; useful for post-hoc analysis. |
| **db4 DWT** | §3 / Listing 2 | ~34 s | 2048 samples | Wavelet alternative to FFT; time-frequency localized. |

### What gets recorded

When `RIPAMonitor` is in the scene with all detectors enabled (the
v1.0.1 default), `RIPACSVColumn` registers one CSV column per method
plus the legacy alias:

| Column | Source |
|---|---|
| `LiveLoadIndex` | Smoothed value of `DisplayedMethod` (back-compat alias). |
| `LiveLoadIndex_RIPA2` | RIPA2 smoothed value. |
| `LiveLoadIndex_BW` | Butterworth smoothed value. |
| `LiveLoadIndex_BW_Raw` | Butterworth raw LF/HF ratio (uncapped, optional). |
| `LiveLoadIndex_FFT` | FFT smoothed value. |
| `LiveLoadIndex_DWT` | db4 DWT smoothed value. |

FFT and DWT columns are zero for the first ~34 s of a session
(warm-up); RIPA2 and Butterworth values appear within ~5 s.

### Selecting the HUD method

`RIPAMonitor` has a `displayedMethod` inspector field, and
`ExperimentUI` exposes `hudMethod` next to its `showRipaHud` toggle.
Setting `ExperimentUI.hudMethod` propagates to the monitor on bind.
The monitor still runs *every enabled detector* each frame regardless
of which one is on-screen, so the CSV captures all methods.

### Device-bandwidth caveat

The LF/HF methods (Butterworth/FFT/DWT) need a high-bandwidth raw
pupil stream to work as the paper intends. Duchowski 2026's validation
used an SR Research Eyelink 1000 at 1000 Hz; HMD eye trackers
typically pre-smooth pupil before exposing it via OpenXR.

On the **HTC VIVE Focus Vision**, Welch PSD on a real recording shows
~40% of pupil signal power below 0.1 Hz, ~50% in the 0.1–0.5 Hz
hippus band, and **only ~1% in the 1.6–4 Hz HF band**. The "true"
LF/HF ratio runs at 50–200, far above the paper's "practical [0, 20]"
range. The monitor's `lfHfMaxRatio` (default 200) and
`lfHfSmoothedScale` (default 0.28) inspector fields rescale the HUD
mapping so the gauge isn't pinned at the clip ceiling; the raw values
are still recorded in `LiveLoadIndex_BW_Raw`.

RIPA2 is not affected by this because it uses Savitzky–Golay
derivatives that respond to the slow oscillations the VIVE *does*
capture.

**Recommendation**: keep RIPA2 as the HUD method on the VIVE. Use the
recorded `LiveLoadIndex_BW` / `_FFT` / `_DWT` columns for cross-device
validation if you later run the same protocol on an Eyelink-class
tracker.

### Sign convention for LF/HF methods

Per Duchowski 2026 §7.2:

- **RIPA2**: higher value = more cognitive load (always).
- **Butterworth/FFT/DWT**: direction depends on task type.
  - *Arithmetic-type tasks*: LF/HF *increases* under load.
  - *N-back / working-memory tasks*: LF/HF *decreases* under load.

This is physiology, not a bug. The bundled SampleExperiment's
CountingTask falls between these two task families; expect the
direction to depend on participant strategy.

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

`RIPACSVColumn` uses this API to register `LiveLoadIndex` plus the
per-method `LiveLoadIndex_RIPA2` / `_BW` / `_FFT` / `_DWT` columns
(v1.0.1+). The bootstrap attaches the component automatically;
manual attachment is rarely needed.

Constraint: registration must happen *before* the recorder writes
its header (within the 2-second coord-origin grace window after
`Start`). Late registrations are rejected with an error log.

---

## Diagnostic: 5-second `[RIPAMonitor] Diag:` block

Every 5 seconds the monitor logs a diagnostic so any session that
wrote zero `LiveLoadIndex` can be inspected post-hoc:

```
[RIPAMonitor] Diag: pushed=<n> skippedNaN=<n> displayed=<method> valid=<bool> raw=<v> smoothed=<v>
```

Read it as:

- `pushed` — pupil samples fanned out to every enabled detector.
- `skippedNaN` — samples rejected as NaN (blink artifact, dropped
  frame).
- `displayed` — which detector currently drives `CurrentLoad` /
  `OnLoadChanged`.
- `raw` / `smoothed` — current outputs of the displayed detector.

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

Use this to validate the recorded `LiveLoadIndex_RIPA2` column
post-hoc, or to recompute the metric with different parameters
(longer smoothing, narrower bands) without re-recording.

A Python parity layer for the Duchowski 2026 Butterworth/FFT/DWT
detectors does not ship with v1.0.1 — the Unity analyzers are the
canonical reference. For offline analysis of the `LiveLoadIndex_BW` /
`_FFT` / `_DWT` columns, the C# sources in
`Assets/Scripts/EyeTracking/Metrics/` are short pure-C# ports that
match `scipy.signal.butter` / `sosfilt` to machine epsilon and follow
the paper's listings; a Python re-implementation is straightforward
when needed.

---

## Inspector reference

### Master + method selection

| Field | Default | Notes |
|---|---|---|
| `enableMonitor` | true | Master switch. Toggle `RIPAMonitor.Enabled` at runtime. |
| `displayedMethod` | RIPA2 | Which detector drives `CurrentLoad` / `OnLoadChanged` / HUD. |
| `enableRipa2` | true | Run the RIPA2 detector. |
| `enableButterworth` | true | Run the Duchowski 2026 IIR LF/HF detector. |
| `enableFft` | true | Run the FFT periodogram detector. |
| `enableDwt` | true | Run the db4 DWT detector. |
| `sampleRateOverrideHz` | 0 (auto) | Pin a fixed sample rate; 0 uses `IEyeTracker.SamplingRateHz`. |
| `publishIntervalSeconds` | 0 (every recompute) | Throttle `OnLoadChanged` for HUDs. |

### RIPA2 parameters (Jayawardena 2025)

| Field | Default | Notes |
|---|---|---|
| `vlfCutoffHz`, `vlfPolyOrder` | 0.29 Hz, N=2 | Paper VLF band. |
| `lfCutoffHz`, `lfPolyOrder` | 4 Hz, N=4 | Paper LF band. |
| `bufferSeconds` | 4 s | Must cover the VLF SG window (≥ 2·M_VLF + 1 samples). |
| `smoothingSeconds` | 1.5 s | Trailing moving average. Paper recommends 1–2 s. |

### Butterworth IIR parameters (Duchowski 2026 Listing 3)

| Field | Default | Notes |
|---|---|---|
| `bwLowBandHz`, `bwHighBandHz` | 1.6, 4 Hz | LF/HF band boundary. |
| `bwFilterOrder` | 4 | Both LP and BP filters. Paper recommends 4–6. |
| `bwPowerWindowSeconds` | 5 s | Sliding variance window. |

### FFT / DWT parameters (Duchowski 2026 Listings 1, 2)

| Field | Default | Notes |
|---|---|---|
| `fftDwtBufferSamples` | 2048 | Power of two. ≈34 s at 60 Hz. |
| `dwtMaxLevel` | 8 | DWT cascade depth, capped by `floor(log2(bufferSamples))`. |

### LF/HF HUD scaling (shared by Butterworth/FFT/DWT)

| Field | Default | Notes |
|---|---|---|
| `lfHfMaxRatio` | 200 | Cap on raw LF/HF ratio. Paper uses 20 for raw 1 kHz pupil; HMD-grade pre-smoothed signals run higher — tune per device. |
| `lfHfSmoothedScale` | 0.28 | `smoothed = log(1 + raw) · scale`, clipped at 1.5. With `lfHfMaxRatio = 200` this maps the cap to the HUD ceiling. |

---

## References

- Jayawardena, G., Jayawardana, Y., & Gwizdka, J. (2025).
  Measuring Mental Effort in Real Time Using Pupillometry.
  *J. Eye Movement Research* 18(6), 70.
  <https://doi.org/10.3390/jemr18060070>
- Duchowski, A. T. (2026). Real-Time Cognitive Load Measurement of
  Pupillary Oscillation. *Proc. ACM Comput. Graph. Interact. Tech.*
  9(2), Article 23. Source for the Butterworth/FFT/DWT LF/HF
  alternative detectors added in v1.0.1.
  <https://doi.org/10.1145/3803537>
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
