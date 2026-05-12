# Algorithms

Mathematical documentation for all algorithms implemented in the VR Eye Tracking Research Toolkit.

---

## Table of Contents

1. [Vergence Point Calculation](#vergence-point-calculation)
   - [Simple Method (Closest-Point)](#1-simple-method-closest-point)
   - [Paper Algorithm (Vector-Vector Intersection)](#2-paper-algorithm-vector-vector-intersection)
   - [Depth Extension Method (Surface Clamping)](#3-depth-extension-method-with-surface-clamping)
2. [Smoothing Filters](#smoothing-filters)
   - [WeightedEMA (Default)](#weightedema-default-method)
   - [Butterworth Low-Pass Filter](#butterworth-low-pass-filter)
   - [Savitzky-Golay Polynomial Filter](#savitzky-golay-polynomial-filter)
3. [Gaze Entropy Analysis](#gaze-entropy-analysis)
4. [Validation Criteria](#validation-criteria)

---

## Vergence Point Calculation

Vergence is the simultaneous movement of both eyes in opposite directions to obtain or maintain binocular vision. The **vergence point** is the 3D location in space where the gaze vectors from both eyes intersect, representing the point of visual fixation.

**Note**: The `DistanceBased` and `Hybrid` methods were removed (May 2025) as they were fundamentally flawed. `DistanceBased` only searched along the head-forward axis, missing off-axis vergence points entirely. `Hybrid` inherited this flaw.

### Input Data

All vergence methods receive:
- **Left eye origin** (`P_L`): 3D position of the left eye in world space
- **Right eye origin** (`P_R`): 3D position of the right eye in world space
- **Left eye direction** (`D_L`): Normalized gaze direction vector for left eye
- **Right eye direction** (`D_R`): Normalized gaze direction vector for right eye

### 1. Simple Method (Closest-Point)

**Implementation**: `Assets/Scripts/EyeTracking/Components/EyeTracker.cs:579-596` (`CalculateUsingSimpleMethod`)

The simple method finds the closest point between two skew lines (rays that don't perfectly intersect in 3D space due to measurement noise).

#### Mathematical Formulation

Given two rays:
- Ray 1: `P_L + t * D_L` (left eye)
- Ray 2: `P_R + s * D_R` (right eye)

We find the parameters `t` and `s` that minimize the distance between points on each ray.

Let `w_0 = P_L - P_R`

Compute dot products:
```
a = D_L · D_L
b = D_L · D_R
c = D_R · D_R
d = D_L · w_0
e = D_R · w_0
```

Calculate the denominator:
```
denom = a * c - b²
```

If `|denom| < ε` (rays are parallel), return invalid.

Calculate parameters:
```
t = (b * e - c * d) / denom
s = (a * e - b * d) / denom
```

Find closest points:
```
Point_L = P_L + t * D_L
Point_R = P_R + s * D_R
```

**Vergence point** (midpoint):
```
V = (Point_L + Point_R) / 2
```

**Quality metric** (lower is better):
```
quality = ||Point_L - Point_R||
```

---

### 2. Paper Algorithm (Vector-Vector Intersection)

**Implementation**: `Assets/Scripts/EyeTracking/Components/EyeTracker.cs:598-610` (`CalculateUsingPaperAlgorithm`), `627-639` (`CalculateVectorVectorIntersection`)

**Reference**: Duchowski, A. T., Krejtz, K., Volonte, M., Hughes, C., Brescia-Zapata, M., & Orero, P. (2022). *3D Gaze in Virtual Reality: Vergence, Calibration, Event Detection*. Procedia Computer Science, 207, 1641-1648. [DOI: 10.1016/j.procs.2022.09.221](https://doi.org/10.1016/j.procs.2022.09.221)

This method uses a more rigorous parametric intersection calculation based on the algorithm described in Duchowski et al.

#### Mathematical Formulation

Given:
- `P_1` = Left eye origin
- `P_3` = Right eye origin
- `R_2` = Left eye direction (normalized)
- `R_4` = Right eye direction (normalized)

Compute difference vector:
```
p_13 = P_1 - P_3
```

Compute dot products:
```
r4·r4 = R_4 · R_4
r2·r2 = R_2 · R_2
r2·r4 = R_2 · R_4
```

Calculate denominator:
```
denom = (r2·r4)² - (r2·r2)(r4·r4)
```

If `|r2·r4| < ε` or `|denom| < ε`, return invalid (rays are parallel).

Calculate intersection parameters:
```
t_2 = ((p_13 · R_2)(r4·r4) - (p_13 · R_4)(r2·r4)) / denom
t_1 = (p_13 · R_2 + t_2 * r2·r2) / r2·r4
```

If `t_1 < 0` or `t_2 < 0`, return invalid (intersection is behind eyes).

**Note on Duchowski et al.'s indexing convention.** The paper uses
unconventional subscripts: `t_2` is the parameter on the **left** ray
`P_1 + t · R_2`, and `t_1` is the parameter on the **right** ray
`P_3 + t · R_4`. The subscript matches the equation index (the first
dot-product equation `(p_13 + ...)·R_2 = 0` solves for `t_2`; the
second `(p_13 + ...)·R_4 = 0` solves for `t_1`), not the ray index.

Find intersection points (per the paper's convention):
```
I_L = P_1 + t_2 * R_2     // left ray uses t_2
I_R = P_3 + t_1 * R_4     // right ray uses t_1
```

**Vergence point**:
```
V = (I_L + I_R) / 2
```

**Quality metric**:
```
quality = ||I_L - I_R||
```

---

### 3. Depth Extension Method (with Surface Clamping)

**Implementation**: `Assets/Scripts/EyeTracking/Components/EyeTracker.cs:449-544` (`CalculateVergenceWithDepthExtension`)

**Status**: PRIMARY METHOD for VIVE Focus Vision

The Depth Extension method combines ray-ray intersection for depth sensitivity with surface raycasting to "catch" the vergence point at colliders. This overcomes the VIVE Focus Vision hardware limitation of ~2° constant convergence angle.

#### Why This Method Exists

The VIVE Focus Vision eye tracker reports nearly constant ~2° convergence angle regardless of actual fixation distance. This limits mathematical vergence to ~1.5-2.3m depth range. The Depth Extension method extends usable depth by:
1. Using ray-ray intersection for basic depth sensitivity
2. Clamping to surface colliders when available

#### Algorithm

1. **Calculate eye ray origins** (world space):
```
leftOrigin = cameraPosition - cameraRight * IPD/2
rightOrigin = cameraPosition + cameraRight * IPD/2
centerOrigin = (leftOrigin + rightOrigin) / 2
```
Where IPD offset = 0.08m (8cm)

2. **Use gaze directions directly** from VIVE API (already in world/tracking space):
```
leftDir = leftDirection.normalized
rightDir = rightDirection.normalized
centerDir = normalize(leftDir + rightDir)
```

**Important**: VIVE API returns directions in world/tracking space, NOT head-local space. Do NOT apply `TransformDirection()`.

3. **Calculate ray-ray intersection** (closest point between skew lines):
```
intersectionPoint, convergenceDistance = CalculateRayIntersection(
    leftOrigin, leftDir, rightOrigin, rightDir)
```

4. **Surface raycast** to detect colliders:
```
hitSurface = Physics.Raycast(centerOrigin, centerDir, maxDistance, layerMask=-1)
```

5. **Select final vergence point** (use CLOSER of intersection or surface):
```
if hitSurface AND surfaceDistance < convergenceDistance:
    vergencePoint = surfaceHitPoint  // "Caught" by surface
else:
    vergencePoint = intersectionPoint  // Use ray intersection
```

#### Key Insight: Coordinate Space

The VIVE OpenXR API returns gaze directions in **world/tracking space**, not head-local space:
```csharp
// In OpenXREyeTracker.cs:
leftEyeDirection = (gazePose.orientation.ToUnityQuaternion() * Vector3.forward).normalized;
```

The `gazePose.orientation` is in the OpenXR reference space (world-aligned), so the resulting direction is already world-space.

**Critical**: Do NOT transform directions with `cameraTransform.TransformDirection()` - this would double-transform and break alignment.

#### Debug Output Examples

```
// Looking at front wall (6m away):
"Ray intersection at 4.57m (surface at 6.01m) [smoothed]"

// Looking at fixation target (surface is closer):
"Surface (FixationTarget_0) at 2.46m [smoothed]"

// Looking at side wall:
"Surface (RightWall) at 4.02m [smoothed]"
```

#### Fallback Behavior

If rays are parallel/diverging (no valid intersection):
- Use surface hit if available
- Otherwise, project along centerDir at fallback distance (6m default)

---

## Smoothing Filters

All smoothing algorithms are implemented in `Assets/Scripts/EyeTracking/Vergence/VergenceSmoothingProcessor.cs` (the `VergenceSmoothingProcessor` class).

### WeightedEMA (Default Method)

**Implementation**: `Assets/Scripts/EyeTracking/Vergence/VergenceSmoothingProcessor.cs:169-206` (`ApplyWeightedEMA`)

**Reference (partial)**:
- Roberts, S.W. (1959). "Control Chart Tests Based on Geometric Moving Averages". *Technometrics*, 1(3), 239-250. [DOI: 10.1080/00401706.1959.10489860](https://doi.org/10.1080/00401706.1959.10489860)

> Citation scope: Roberts (1959) — and equivalently Brown (1963) — introduce the textbook EMA recursion `y_t = α · x_t + (1−α) · y_{t−1}`, which is the *final blend step* of WeightedEMA only. The preceding **quality-and-time-weighted history average** and the **adaptive-α rescaling** (distance- and quality-conditioned, see `GetAdaptiveSmoothingFactor`) are Eye_lean implementation details with no paper basis — they are heuristics tuned for VIVE Focus Vision vergence noise characteristics. Treat WeightedEMA as a custom filter, not as Roberts EMA.

A quality-weighted exponential moving average that adapts to tracking conditions.

#### Algorithm

1. Maintain history queue of gaze points (configurable buffer size)
2. Calculate weighted average with:
   - Time weight: newer samples weighted more `(i+1)/N`
   - Quality weight: high quality points (quality near 0) weighted more
3. Apply exponential smoothing

```
weight[i] = time_weight * quality_weight
y[n] = Σ(weight[i] * x[i]) / Σ(weight[i])
```

#### Adaptive Smoothing (`VergenceSmoothingProcessor.cs:149-167`, `GetAdaptiveSmoothingFactor`)

When enabled, adjusts smoothing factor based on:
- **Distance**: Less smoothing for close (<2m) and distant (>4m) objects
- **Quality**: Adjusts to maintain responsiveness

#### Parameters
| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| smoothingFactor | 0-1 | 0.8 | Higher = smoother |
| adaptiveSmoothing | bool | true | Distance/quality adaptation |
| bufferSize | 2-10 | 5 | History size |

---

### Butterworth Low-Pass Filter

**Implementation**: `Assets/Scripts/EyeTracking/Vergence/VergenceSmoothingProcessor.cs:55-74` (`InitializeButterworthCoefficients`), `208-238` (`ApplyButterworthFilter`)

**References**:
- Butterworth, S. (1930). "On the Theory of Filter Amplifiers". *Wireless Engineer*, 7(6), 536-541.
- Smith, S.W. (1997). *The Scientist and Engineer's Guide to Digital Signal Processing*. Chapter 20: Recursive Filters. California Technical Publishing. [Available online](https://www.dspguide.com/ch20.htm)

A 2nd-order IIR (Infinite Impulse Response) low-pass filter with maximally flat passband.

#### Coefficient Computation

```
ω = tan(π * f_c)
denom = 1 + √2*ω + ω²

b[0] = b[2] = 1/denom
b[1] = 2/denom

a[0] = 1
a[1] = 2*(1 - ω²)/denom
a[2] = (1 - √2*ω + ω²)/denom
```

Where `f_c` = cutoff frequency (normalized, 0.01-0.5)

#### Filter Application

```
y[n] = b[0]*x[n] + b[1]*x[n-1] + b[2]*x[n-2] - a[1]*y[n-1] - a[2]*y[n-2]
```

#### Parameters
| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| cutoffFrequency | 0.01-0.5 | 0.1 | Lower = smoother, more lag |

---

### Savitzky-Golay Polynomial Filter

**Implementation**: `Assets/Scripts/EyeTracking/Vergence/VergenceSmoothingProcessor.cs:25-28` (precomputed coefficient tables), `240-284` (`GetSavitzkyGolayWindowSize`, `GetSavitzkyGolayCoefficients`, `ApplySavitzkyGolayFilter`)

**References**:
- Savitzky, A., & Golay, M.J.E. (1964). "Smoothing and Differentiation of Data by Simplified Least Squares Procedures". *Analytical Chemistry*, 36(8), 1627-1639. [DOI: 10.1021/ac60214a047](https://doi.org/10.1021/ac60214a047)
- Schafer, R.W. (2011). "What Is a Savitzky-Golay Filter?" *IEEE Signal Processing Magazine*, 28(4), 111-117. [DOI: 10.1109/MSP.2011.941097](https://doi.org/10.1109/MSP.2011.941097)

A polynomial smoothing filter that preserves signal peaks and shapes better than moving average.

#### Precomputed Coefficients (Quadratic Polynomial)

| Window | Coefficients |
|--------|-------------|
| 5-point | `[-0.086, 0.343, 0.486, 0.343, -0.086]` |
| 7-point | `[-0.095, 0.143, 0.286, 0.333, 0.286, 0.143, -0.095]` |
| 9-point | `[-0.091, 0.061, 0.168, 0.234, 0.255, 0.234, 0.168, 0.061, -0.091]` |
| 11-point | `[-0.084, 0.021, 0.103, 0.161, 0.196, 0.207, 0.196, 0.161, 0.103, 0.021, -0.084]` |

#### Algorithm

```
y[n] = Σ(coeff[i] * x[n-windowSize+i]) for i = 0 to windowSize-1
```

#### Parameters
| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| windowSize | 5, 7, 9, 11 | 5 | Larger = smoother, more lag |

---

### Method Selection Guide

| Method | Best For | Latency | CPU Cost |
|--------|----------|---------|----------|
| **WeightedEMA** | General use, variable conditions | Low | Low |
| **Butterworth** | Maximum smoothness needed | Medium | Medium |
| **SavitzkyGolay** | Preserving signal peaks | Medium-High | Low |

---

## Gaze Entropy Analysis

**Unity (live monitor)**: `GazeEntropyCalculator.cs:737-769` — raw-sample
spherical-grid Shannon entropy, used for the live dispersion HUD.

**Python (offline / reportable)**: `eyelean_analysis.metrics.entropy.fixation_entropy`
— SGE + GTE over a fixation list per Shiferaw 2019. Returns both raw
bits and normalised-by-`log2(N)` values so results compare across
discretisations and recordings.

**References**:
- Shannon (1948) — base entropy formula.
- Krejtz et al. (2015), *ACM TAP* 13(1):4 — gaze transition entropy
  (GTE) formulation and the `Hmax = log2(N)` normalisation used in
  Eye_lean.
- Shiferaw, Downey & Crewther (2019), *Neurosci. Biobehav. Rev.* 96 —
  review establishing that gaze entropy is a property of the
  **fixation sequence** (raw samples deflate GTE through self-loops)
  and that SGE + GTE should be reported jointly.

### Stationary Gaze Entropy (SGE)

```
SGE = -Σ p_i · log₂(p_i)              over fixation-location bins
SGE_normalised = SGE / log₂(N)        ∈ [0, 1]
```

Higher SGE = more dispersed fixation distribution.

### Gaze Transition Entropy (GTE)

```
GTE = -Σ_i π_i Σ_j P(j|i) · log₂ P(j|i)    over fixation-to-fixation
                                              transitions in the same grid
GTE_normalised = GTE / log₂(N)         ∈ [0, 1]
```

Higher GTE = less predictable scanning. The Krejtz 2015 convention
treats within-state transitions as observed, so `Hmax = log₂(N)`
(not `log₂(N−1)` as in Weiss 1989).

### Spherical Discretization (Unity live monitor)

Gaze directions are converted to spherical coordinates and binned:

```
yaw = atan2(x, z)     // Range: [-π, π]
pitch = asin(y)        // Range: [-π/2, π/2]
```

Bin mapping:
```
yaw_bin = floor((yaw + π) / (2π) * num_horizontal_bins)
pitch_bin = floor((pitch + π/2) / π * num_vertical_bins)
```

### Parameters
- Time window: 0.5 - 10 seconds (live monitor)
- Horizontal bins: 4 - 36 (live monitor); default 8 in
  `fixation_entropy` for HMD field of view
- Vertical bins: 4 - 36 (live monitor); default 8 in `fixation_entropy`

---

## Validation Criteria

**Implementation**: `Assets/Scripts/EyeTracking/Components/EyeTracker.cs:612-625` (`ValidateVergenceResult`)

### Quality Threshold
```
quality ≤ maxVergenceDistance (default: 2.0m)
```

### Distance Bounds
```
minDistance ≤ ||V - headCenter|| ≤ maxDistance
```
Default: 0.3m to 10m

### Convergence Angle
```
minAngle ≤ angle(D_L, D_R) ≤ maxAngle
```
Default: Based on vergence preset (typically 45°-75°)

### Forward Direction Check
```
dot(headForward, toGazePoint) ≥ 0.5
```
Ensures the vergence point is in front of the user.

---

*Last updated: 2025-12-21*
