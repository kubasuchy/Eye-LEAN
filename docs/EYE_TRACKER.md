# Eye Tracker

Per-scene MonoBehaviour. Consumes raw gaze / pupil / openness from an
`IEyeTracker` provider, computes the vergence point (3D location
where the two eye rays converge), runs smoothing, dispatches
`GazeTarget` hover events, and renders the optional debug ray +
vergence-point visualization. Inspector-field guide below; for the
math see
[`../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md`](../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md).

## Audience

Researchers tuning the vergence pipeline for a specific HMD, room
geometry, or stimulus depth, and developers adding new providers or
metrics.

## Prerequisites

- Unity 6000.3.9f1 + VIVE OpenXR 2.5.1 (or any compliant OpenXR
  runtime).
- An `IEyeTracker` provider in the scene at start. `EyeTrackerFactory`
  auto-resolves; `NullEyeTracker` is the no-hardware fallback.
- A `MainCamera`-tagged Camera so `cameraTransform` auto-resolves.
  The CalibrationScene and SampleExperiment scenes already do.

---

## How the vergence point is computed

Per frame:

```
provider --> per-eye origin/direction --> vergence method --> raw point
                                                                 |
                                       smoothing processor <-----+
                                                |
                                                v
                                       final vergence point --> CSV + viz
```

1. `CollectEyeTrackingData` pulls left/right origin + direction from
   the active `IEyeTracker` (`EyeTracker.cs:255`).
2. `UpdateVergenceCalculation` dispatches to one of two top-level
   paths based on `vergenceDepthMode` (`EyeTracker.cs:433`):
   - `DepthExtension` -> `CalculateVergenceWithDepthExtension`
     (`EyeTracker.cs:449`)
   - `TrueConvergence` -> `CalculateVergencePoint`, which then
     branches on `vergenceSettings.method` to either `Simple` or
     `PaperAlgorithm` (`EyeTracker.cs:546`).
3. The raw point feeds `VergenceSmoothingProcessor.ProcessPoint`
   (`VergenceSmoothingProcessor.cs:76`) with one of three filters
   (WeightedEMA, Butterworth, Savitzky-Golay).
4. `SampleSnapshot` freezes the result into an `EyeFrameSample` for
   `SessionRecorder` (`EyeTracker.cs:280`).

Coordinate frame note: `OpenXREyeTrackerProvider` already transforms
gaze from VIVE tracking-space to world-space, so all eye origins and
directions inside `EyeTracker` are world-space. Do not re-transform
them.

---

## Vergence calculation methods

### TrueConvergence vs DepthExtension (top-level mode)

Field: `vergenceDepthMode` (`EyeTracker.cs:36`), enum `VergenceDepthMode`
(`VergenceTypes.cs:43`).

- **What it is.** The top-level switch between pure eye-convergence
  math and a hybrid mode that falls back to scene-collider raycasts
  for far targets.
- **Math.**
  - `TrueConvergence`: vergence point = output of
    `CalculateUsingSimpleMethod` or `CalculateUsingPaperAlgorithm`,
    no surface raycast.
  - `DepthExtension`: vergence point = `CalculateRayIntersection`
    output, then optionally replaced with `Physics.Raycast` hit point
    when (a) the surface is closer than the math intersection, or
    (b) the math has saturated near `hardwareDepthLimit` and a
    surface lies farther out (`EyeTracker.cs:507`).
- **Effect of changing it.** `TrueConvergence` is hardware-capped at
  ~1.5-2.3 m on the VIVE Focus Vision because the inter-eye angle
  barely changes for fixations beyond ~2 m. Switch to
  `DepthExtension` to recover usable depth for room-scale stimuli.
  Switch to `TrueConvergence` only when you want the pure angular
  signal (e.g. depth-of-fixation analysis) and accept the cap.
- **Starting value.** `DepthExtension` (matches the source default at
  `EyeTracker.cs:36`). It is the only mode that works for any target
  beyond ~2 m on VIVE.

### Method 1: Simple (closest-point-of-approach)

Implementation: `CalculateUsingSimpleMethod` (`EyeTracker.cs:579`),
also used by `DepthExtension` via `CalculateRayIntersection`
(`EyeTracker.cs:641`).

- **What it is.** The closest-point-of-approach between the two eye
  rays treated as skew lines in 3D.
- **Math.**

  Let `P_L`, `P_R` be the left/right eye origins and `D_L`, `D_R` the
  unit gaze directions. With `w0 = P_L − P_R`:

  ```
  a = D_L · D_L      b = D_L · D_R      c = D_R · D_R
  d = D_L · w0       e = D_R · w0
  denom = a·c − b²
  ```

  If `|denom| < 1e-4` the rays are parallel — return invalid. Else:

  ```
  s_c = (b·e − c·d) / denom
  t_c = (a·e − b·d) / denom
  V   = ((P_L + s_c·D_L) + (P_R + t_c·D_R)) / 2
  q   = || (P_L + s_c·D_L) − (P_R + t_c·D_R) ||
  ```

  `V` is the vergence point; `q` is the closest-approach distance
  (lower is better; reported as `quality` in
  `VergenceCalculationResult`).

- **Effect of changing related fields.** This method is gated by
  `vergenceSettings.distanceRange` and `vergenceSettings.validation`
  (see "Other Inspector fields" below). Tightening
  `maxVergenceDistance` rejects more samples as low-quality;
  tightening `minDistance` / `maxDistance` rejects more as
  out-of-range.
- **When to pick it.** Default for live recording. Cheaper than the
  Paper algorithm and degrades smoothly when one eye drops out.
- **Starting value.** `vergenceSettings.method = Simple` (matches
  source default at `VergenceSettings.cs:10`).

### Method 2: Paper algorithm (Duchowski et al. 2022)

Implementation: `CalculateUsingPaperAlgorithm` +
`CalculateVectorVectorIntersection` (`EyeTracker.cs:598`,
`EyeTracker.cs:627`).

- **What it is.** The parametric vector-vector intersection from
  Duchowski et al. 2022 (DOI: 10.1016/j.procs.2022.09.221). Equivalent
  geometry to Simple, different algebraic form.
- **Math.**

  With `p13 = P_L − P_R`:

  ```
  r2·r2 = D_L · D_L
  r4·r4 = D_R · D_R
  r2·r4 = D_L · D_R
  denom = (r2·r4)² − (r2·r2)·(r4·r4)
  ```

  If `|r2·r4| < ε` or `|denom| < ε`, return invalid. Else:

  ```
  t2 = ((p13 · D_L)·(r4·r4) − (p13 · D_R)·(r2·r4)) / denom
  t1 = (p13 · D_L + t2·(r2·r2)) / (r2·r4)
  ```

  Reject when `t1 < 0` or `t2 < 0` (intersection behind the eyes).
  Then:

  ```
  I_L = P_L + t1·D_L
  I_R = P_R + t2·D_R
  V   = (I_L + I_R) / 2
  q   = || I_L − I_R ||
  ```

- **Effect of changing it.** Requires both eyes valid
  (`requireBothEyes` is enforced regardless of preset for this
  method, see `EyeTracker.cs:601`). Slightly more strict about
  rejecting back-projecting intersections than Simple.
- **When to pick it.** When you want method-parity with the published
  reference implementation, or when binocular validity is enforced
  upstream (e.g. quality-controlled offline analysis). Will fall back
  to Simple inside `CalculateVergencePoint` if it returns invalid
  (`EyeTracker.cs:564`).
- **Starting value.** Use Simple unless replicating Duchowski et al.
  exactly. The `Precise` preset (`VergenceSettings.cs:25`) selects
  this method.

### Method 3: Depth extension (surface clamping)

Implementation: `CalculateVergenceWithDepthExtension`
(`EyeTracker.cs:449`).

- **What it is.** A wrapper that runs `CalculateRayIntersection`
  (Simple-method math) and then, given a `Physics.Raycast` hit along
  the cyclops gaze direction, decides whether the surface or the math
  intersection is the better answer.
- **Math.** Compute math intersection `V_math` at distance
  `d_math` (from `CalculateRayIntersection`). Cast a ray from
  `centerOrigin = (P_L + P_R)/2` along `centerDir` =
  normalize(`D_L` + `D_R`); call the hit `V_surf` at distance
  `d_surf` along the camera-forward ray, capped at
  `maxRaycastDistance`. Define `mathSaturated` = `d_math ≥
  hardwareDepthLimit − 0.3` (a fixed 0.3 m hardware-saturation margin
  at `EyeTracker.cs:507`). Then:

  ```
  if hitSurface and d_surf < d_math:        V = V_surf  # closer surface blocks the gaze
  elif hitSurface and mathSaturated:        V = V_surf  # math saturated, extend to surface
  else:                                     V = V_math  # trust the angular math
  ```

  When no math intersection is available (parallel/diverging rays):

  ```
  if hitSurface:  V = V_surf, quality = 0.7
  else:           V = centerOrigin + centerDir · fallbackFarDistance, quality = 0.5
  ```

- **Effect of changing related fields.** Three Inspector fields tune
  this method; see "Depth extension fields" below for each.
- **When to pick it.** Default. Required for any room-scale stimulus
  beyond ~2 m on VIVE Focus Vision.
- **Starting value.** Set `vergenceDepthMode = DepthExtension` and
  populate the scene with colliders for every visible surface (walls,
  stimuli, props).

---

## Vergence smoothing

Top-level field: `vergenceSettings.smoothing.enableSmoothing`
(`VergenceTypes.cs:172`). When false, `finalVergencePoint =
rawVergencePoint`. When true, the raw point flows through
`VergenceSmoothingProcessor.ProcessPoint`
(`VergenceSmoothingProcessor.cs:76`).

The processor also rejects non-finite samples at the boundary so a
single NaN cannot poison IIR filter state
(`VergenceSmoothingProcessor.cs:82`).

### Method 1: WeightedEMA

Implementation: `ApplyWeightedEMA` (`VergenceSmoothingProcessor.cs:169`).

- **What it is.** A history-buffer weighted average plus an
  exponential blend with the previous output. Time-weight favours
  newer samples; quality-weight favours samples with smaller
  closest-approach distance.
- **Math.** Let the buffer hold `N` samples `x_i` (i = 0..N-1, oldest
  first) with quality scores `q_i`. Compute weights:

  ```
  w_i        = ((i + 1) / N) · clamp01(1 − q_i)
  averaged   = Σ(w_i · x_i) / Σ(w_i)               # if Σw_i > 1e-6
  ```

  Then blend with the previous output `V_{t-1}`:

  ```
  V_t = (1 − α) · averaged + α · V_{t-1}
  ```

  where `α = smoothingFactor` (NOT the textbook EMA α — here higher
  `α` means MORE weight on the previous output, i.e. smoother). When
  `adaptiveSmoothing` is on, `α` is rescaled per frame
  (`VergenceSmoothingProcessor.cs:149`):

  ```
  if distance < 2 m:   α *= 0.7      # close → more responsive
  if distance > 4 m:   α *= 0.6      # far   → more responsive
  if quality < 0.1:    α *= 0.9
  if quality > 0.5:    α *= 0.8      # low quality → less smoothing
  α = clamp(α, 0.1, 0.8)
  ```

- **Effect of changing the smoothing factor.** Lower `smoothingFactor`
  (toward 0): more responsive, more visible jitter. Higher (toward
  1): smoother but laggier; above ~0.85 the vergence point visibly
  trails saccades and feels "stuck". The clamp at `0.8` for adaptive
  smoothing means values above 0.8 stop changing behavior when
  `adaptiveSmoothing = true`.
- **Effect of changing the buffer size.** Larger buffer averages over
  more history, smoothing more but lagging more. Sizes above ~7
  rarely buy noticeable smoothness past what the EMA blend already
  delivers.
- **Starting values.**
  - `smoothingFactor = 0.5` (matches the `Balanced` preset at
    `VergenceSettings.cs:40`; the source default of `0.8` from
    `WeightedEMASettings.Default` is over-smoothed for live use —
    flag).
  - `adaptiveSmoothing = true` (matches default; the
    distance/quality rescaling matters for room-scale stimuli).
  - `bufferSize = 5` (matches default; auto-corrected from 0 in
    `Start` at `EyeTracker.cs:181`).

### Method 2: Butterworth (2nd-order IIR low-pass)

Implementation: `InitializeButterworthCoefficients` +
`ApplyButterworthFilter` (`VergenceSmoothingProcessor.cs:55`,
`VergenceSmoothingProcessor.cs:208`).

- **What it is.** A second-order IIR low-pass with maximally flat
  passband, applied component-wise to the (x, y, z) of the vergence
  point.
- **Math.** Cutoff `f_c` is normalized to the sample rate (frame
  rate), clamped to `[0.01, 0.499]`. With `ω = tan(π · f_c)`:

  ```
  denom = 1 + √2·ω + ω²
  b[0] = b[2] = 1 / denom
  b[1] = 2 / denom
  a[0] = 1
  a[1] = 2·(1 − ω²) / denom
  a[2] = (1 − √2·ω + ω²) / denom
  ```

  Difference equation:

  ```
  y[n] = b[0]·x[n] + b[1]·x[n−1] + b[2]·x[n−2]
       − a[1]·y[n−1] − a[2]·y[n−2]
  ```

  On first sample the filter state is initialized to the input
  (`VergenceSmoothingProcessor.cs:212`) so it does not ring up from
  zero.

- **Effect of changing the cutoff.** Lower `cutoffFrequency` → more
  smoothing, more lag (group delay grows roughly as `1 / f_c`).
  Higher → less smoothing, sharper response. Because the cutoff is
  normalized to the sample rate, a target wall-clock cutoff in Hz
  must be converted: `f_c = f_Hz / f_sample`. At 90 fps,
  `cutoffFrequency = 0.1` corresponds to ~9 Hz wall-clock cutoff.
  Sample-rate dependency is real: change frame rate and the filter
  changes character.
- **Starting value.** `cutoffFrequency = 0.1` (matches default at
  `VergenceTypes.cs:144`; gives ~9 Hz cutoff at 90 fps, a reasonable
  match to vergence dynamics).

### Method 3: Savitzky-Golay polynomial

Implementation: `ApplySavitzkyGolayFilter`
(`VergenceSmoothingProcessor.cs:261`); coefficients precomputed for
window sizes 5/7/9/11 (`VergenceSmoothingProcessor.cs:25-28`).

- **What it is.** Per-axis convolution of the most recent
  `windowSize` samples with precomputed quadratic-fit coefficients.
  Preserves sharp peaks better than a moving average or EMA.
- **Math.** Let the buffer hold the most recent `windowSize` samples
  `x_{n-windowSize+1}, ..., x_n`, and let `c` be the coefficient array
  for that window size. Then:

  ```
  y[n] = Σ_{i=0..windowSize−1}  c[i] · x_{n − windowSize + 1 + i}
  ```

  Coefficient arrays:

  | windowSize | coefficients |
  |---|---|
  | 5  | `[-0.086, 0.343, 0.486, 0.343, -0.086]` |
  | 7  | `[-0.095, 0.143, 0.286, 0.333, 0.286, 0.143, -0.095]` |
  | 9  | `[-0.091, 0.061, 0.168, 0.234, 0.255, 0.234, 0.168, 0.061, -0.091]` |
  | 11 | `[-0.084, 0.021, 0.103, 0.161, 0.196, 0.207, 0.196, 0.161, 0.103, 0.021, -0.084]` |

  Until the buffer fills, the filter returns the most recent raw
  sample (`VergenceSmoothingProcessor.cs:271`).

- **Effect of changing the window length.** Larger window → smoother
  output, larger lag (group delay grows linearly with window size,
  ~ `(windowSize − 1) / 2` samples). Polynomial order is fixed at
  quadratic in the precomputed tables — to change order you would
  recompute coefficients. Window must be odd; even values are
  bumped up (`VergenceSmoothingProcessor.cs:240`); values outside
  `[5, 11]` are clamped.
- **Starting value.** `windowSize = 5` (matches default at
  `VergenceTypes.cs:161`; minimum lag while still preserving
  fixation peaks).

### Smoothing method selection guide

| Method | Best for | Latency | CPU cost |
|---|---|---|---|
| WeightedEMA | General live recording, variable conditions | Low | Low |
| Butterworth | Maximum smoothness with predictable phase | Medium | Medium |
| SavitzkyGolay | Preserving fixation peaks for offline analysis | Medium-High | Low |

---

## Other Inspector fields on `EyeTracker`

### Camera

| Field | What it does | Effect | Default |
|---|---|---|---|
| `cameraTransform` (`EyeTracker.cs:39`) | The HMD camera root used as the parent for visualization origins. | If null, `Start` auto-resolves to `Camera.main`. Wrong assignment moves the LineRenderer origins off the user's eyes (vergence math is unaffected). | `null` (auto-resolve) |

### Vergence configuration

| Field | What it does | Effect | Default |
|---|---|---|---|
| `vergenceSettings.preset` (`VergenceSettings.cs:9`) | One of `Precise / Balanced / Stable / Custom`. Ignored at runtime unless `SetVergencePreset()` is called — the preset only seeds the other fields when fetched via `GetPreset()`. | Selecting a preset via the runtime API replaces method, validation, and smoothing in one call (`EyeTracker.cs:769`). | `Balanced` |
| `vergenceSettings.method` | Simple vs PaperAlgorithm. See "Vergence calculation methods" above. | — | `Simple` |
| `vergenceSettings.distanceRange.minDistance` (`VergenceTypes.cs:76`) | Reject vergence points closer than this from the head center. | Raising it discards near-field fixations. Must be > 0 to avoid degenerate intersections. | `0.3 m` |
| `vergenceSettings.distanceRange.maxDistance` | Reject vergence points beyond this. | Lowering it excludes far-wall fixations from validation; raising past ~50 m has no effect because the underlying `CalculateRayIntersection` already rejects `distance > 1000 m` at `EyeTracker.cs:658`. | `100 m` |
| `vergenceSettings.distanceRange.wallMargin` | Reserved for room-bounds quality degradation. | Currently unused by the validator. | `0.5 m` |
| `vergenceSettings.validation.maxVergenceDistance` (`VergenceTypes.cs:94`) | Max allowed closest-approach distance between the two rays for a sample to be valid. | Lower → more samples rejected as low-quality. Above ~3 m the gate stops mattering because most degenerate samples already fail other checks. | `2.0 m` |
| `vergenceSettings.validation.maxConvergenceAngle` | Max angle between the two eye rays. | Lower → reject extreme cross-eyed samples. Above ~75° rays approach anti-parallel (looking at own nose) — beyond that the value stops mattering. | `60°` |
| `vergenceSettings.validation.minConvergenceAngle` | Min angle between the rays (rejects parallel-gaze). | Raise to require demonstrable convergence; effective floor is ~0.001° because parallel rays are already rejected by the determinant check. | `0.001°` |
| `vergenceSettings.validation.requireBothEyes` | Only run vergence when both eyes report valid data. | When false, mono-eye fixations fall through to the head-gaze fallback. PaperAlgorithm always requires both eyes regardless of this flag (`EyeTracker.cs:601`). | `true` |
| `constraintSettings` (`EyeTracker.cs:43`) | Reserved for the room-bounds fallback path. | Currently consumed only by external constraint logic — not wired to the live vergence path. Leave at default. | `Default` |

### Smoothing fine-tuning

| Field | What it does | Effect | Default |
|---|---|---|---|
| `surfaceRaycastExtraSmoothing` (`EyeTracker.cs:48`) | Reserved knob for additional surface-raycast smoothing in the depth-extension fallback. | Currently unused in the live path; sliders the value in `[0, 1]` for forward compatibility. | `0.0` |

### Debug visualization

| Field | What it does | Effect | Default |
|---|---|---|---|
| `enableDebugOnStart` (`EyeTracker.cs:52`) | Force-enable visualization on `Start` even when both `showDebugRays` and `showVergencePoint` are off. | Almost never needed — toggling either show flag also enables debug mode (`EyeTracker.cs:198`). | `false` |
| `showDebugRays` (`EyeTracker.cs:54`) | Render per-eye LineRenderers. | Setting true on `Start` also creates the LineRenderer GameObjects. Toggling at runtime re-enables/disables existing ones. | `true` |
| `showVergencePoint` (`EyeTracker.cs:56`) | Render the cyan sphere at the vergence point. | Setting true on `Start` also creates the sphere primitive (no collider, no rigidbody). | `true` |
| `rayLength` (`EyeTracker.cs:57`) | Length of the visualized ray, meters. | Visualization-only — does not affect vergence math, raycast distance, or `maxRaycastDistance`. | `10 m` |
| `leftRayColor / rightRayColor / vergenceColor` (`EyeTracker.cs:58-60`) | Visualization colors. | Visualization-only. | red / blue / cyan |
| `vergencePointSize` (`EyeTracker.cs:61`) | Scale of the vergence sphere, meters. | Visualization-only. Above ~0.2 m the sphere starts occluding the stimulus. | `0.05 m` |

### Depth extension fields

These tune `CalculateVergenceWithDepthExtension` and have no effect
when `vergenceDepthMode = TrueConvergence`.

| Field | What it does | Effect | Default |
|---|---|---|---|
| `hardwareDepthLimit` (`EyeTracker.cs:65`) | Distance at which the math vergence is assumed to have saturated against the HMD's hardware angular resolution. The decision rule fires when `convergenceDistance ≥ hardwareDepthLimit − 0.3 m`. | Set to your HMD's measured hardware cap. Lower values trigger surface extension earlier (more far-field samples snap to surfaces). Higher values disable extension entirely (the math always wins). The runtime setter clamps to `≥ 0.5 m` (`EyeTracker.cs:142`). | `2.0 m` |
| `maxRaycastDistance` (`EyeTracker.cs:67`) | Max distance for the surface raycast in depth extension. Also used to clamp the math intersection's reported distance. | Set to the largest dimension of your stimulus volume (e.g. room diagonal). Raising past ~50 m wastes raycast budget; lowering below the room size truncates far-wall fixations. | `20 m` |
| `fallbackFarDistance` (`EyeTracker.cs:69`) | Where to plant the vergence point when the rays are parallel/diverging AND no surface is hit. | Choose a value inside your stimulus volume so the visualization doesn't clip to infinity. Quality of these samples is reported as `0.5`. | `5 m` |

---

## API

File: `Assets/Scripts/EyeTracking/Components/EyeTracker.cs`

| Method / property | Line | Purpose |
|---|---|---|
| `HasValidGazeData()` | 115 | True iff at least one eye returned valid data this frame. |
| `GetCombinedGazeOrigin()` | 116 | Combined cyclops gaze origin in world space. |
| `GetCombinedGazeDirection()` | 117 | Combined gaze direction. |
| `GetCurrentVergenceResult()` | 118 | Smoothed vergence point + quality. |
| `GetCurrentGazedObject()` | 119 | The GameObject the user is fixated on (raycast or `GazeTarget` collider). |
| `GetCurrentGazeTarget()` | 120 | The `GazeTarget` component on the gazed object, if any. |
| `IsDebugMode()` | 121 | True if visualization (rays + vergence point) is on. |
| `CurrentVergenceDepthMode` | 124 | Get/set the depth mode at runtime. |
| `HardwareDepthLimit` | 137 | Get/set the hardware depth limit at runtime (clamped to ≥ 0.5 m). |
| `SampleSnapshot()` | 280 | Frozen `EyeFrameSample` for `SessionRecorder`. |
| `ResetGazedObject()` | 709 | Clear the gazed-object state (use on phase boundaries). |
| `SetDebugMode(bool)` | 744 | Toggle visualization. Locked once set. |
| `UpdateVergenceSettings(...)` | 762 | Replace the live vergence settings (also re-tunes the smoothing processor). |
| `SetVergencePreset(...)` | 769 | Apply a `VergencePreset` in one call. |
| `ResetVergenceSmoothing()` | 771 | Clear the smoothing buffers (use after a hard scene change or recalibration). |
| `GetQualityMetrics()` | 776 | The sibling `DataQualityMetrics` — gap counts, blink rate, etc. |
| `LogPerformanceStats()` | 778 | Emit average fps and total frame count for the session. |

File: `Assets/Scripts/EyeTracking/Core/IEyeTracker.cs`

`IEyeTracker` exposes per-eye + combined gaze origin / direction,
openness, pupil diameter, and pupil position. All getters return
`bool` indicating whether the device produced valid data this frame.

File: `Assets/Scripts/EyeTracking/Core/EyeTrackerFactory.cs`

| Method | Purpose |
|---|---|
| `GetEyeTracker()` | Singleton accessor. Detects VIVE / Varjo / HoloLens / Null in priority order. |
| `Reinitialize()` | Force re-detection. |
| `Invalidate()` | Drop the cached tracker (called by `EyeTrackerFactoryBootstrapper` on scene change). |
| `SetReplayOverride(IEyeTracker)` | Install a `ReplayingEyeTracker` so the factory routes to recorded samples instead of hardware. |

---

## Common patterns + gotchas

- **VIVE coord-frame.** VIVE OpenXR returns gaze in tracking space.
  `OpenXREyeTrackerProvider` applies the
  `cameraTransform.parent` transform before exposing world-space
  vectors. Inside `EyeTracker` everything is already world-space —
  do not re-transform. Symptom of forgetting: gaze rays orbit `(0,
  0, 0)` while the user stands at `(2, 1.6, 1)`.
- **Visualization-only origin offset.** The LineRenderer origin is
  laterally offset ±8 cm from `cameraTransform.position` (NOT the
  actual eye origin from the tracker). This is a workaround for URP
  single-pass instanced billboard rendering verified against five
  prior projects (`EyeTracker.cs:71`). The offset affects the
  LineRenderer only; CSV / vergence math uses the true tracker
  origins.
- **Per-scene scope.** `EyeTracker` is NOT `DontDestroyOnLoad`. The
  `IEyeTracker` provider survives scenes; the MonoBehaviour wrapper
  is per-scene. (Round-9 lesson — a `DontDestroyOnLoad` wrapper held
  stale `cameraTransform` refs and produced 2493 NREs after re-entry.)
- **`HasValidGazeData()` is cheap.** Use it as the gate before any
  consumer of `GetCombined*` to avoid acting on stale defaults.
- **Smoothing reset on phase change.** Call
  `ResetVergenceSmoothing()` at trial boundaries to flush the
  history buffers; otherwise a recall-phase fixation can be biased
  by the previous trial's data for ~5 frames.
- **JSON import/export.** In the Editor only, the component context
  menu offers "Export Vergence Settings" and "Import Vergence
  Settings", which round-trip `vergenceDepthMode`, `vergenceSettings`,
  and `constraintSettings` to JSON (`EyeTracker.cs:787`). File
  version is `1.1`.

---

## References

- Source:
  - `Assets/Scripts/EyeTracking/Components/EyeTracker.cs`
  - `Assets/Scripts/EyeTracking/Vergence/VergenceSettings.cs`
  - `Assets/Scripts/EyeTracking/Vergence/VergenceTypes.cs`
  - `Assets/Scripts/EyeTracking/Vergence/VergenceSmoothingProcessor.cs`
  - `Assets/Scripts/EyeTracking/Core/IEyeTracker.cs`
  - `Assets/Scripts/EyeTracking/Core/EyeTrackerFactory.cs`
  - `Assets/Scripts/EyeTracking/Providers/OpenXREyeTrackerProvider.cs`
- Math derivations:
  [`../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md`](../Eye_lean_Unity_Project/Eye_lean/docs/ALGORITHMS.md)
- Tests: `Assets/Editor/Tests/EyeTrackerTests.cs`
- Duchowski et al. (2022). *3D Gaze in Virtual Reality: Vergence,
  Calibration, Event Detection*. DOI: 10.1016/j.procs.2022.09.221
- Roberts, S.W. (1959). Control Chart Tests Based on Geometric Moving
  Averages. *Technometrics* 1(3).
- Butterworth, S. (1930). On the Theory of Filter Amplifiers.
  *Wireless Engineer* 7(6).
- Savitzky, A. & Golay, M.J.E. (1964). Smoothing and Differentiation
  of Data by Simplified Least Squares Procedures. *Anal. Chem.* 36(8).
