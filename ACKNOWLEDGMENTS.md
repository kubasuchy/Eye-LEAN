# Acknowledgments

## In memory of Professor Eileen Kowler

Dedicated to **Professor Eileen Kowler** (Rutgers University–New
Brunswick), whose work on eye movements, attention, and active
vision laid the conceptual ground this toolkit stands on.

## Lab and institutional support

- **Visual Cognition Lab**, Rutgers University–New Brunswick.
- **Rutgers University–New Brunswick** — hardware and lab space.

## Foundational eye-movement research

- **Kowler, E.** (2011). Eye movements: The past 25 years. *Vision
  Research*, 51(13), 1457–1483.
  <https://doi.org/10.1016/j.visres.2010.12.014>
- **Holmqvist, K., Nyström, M., Andersson, R., Dewhurst, R.,
  Jarodzka, H., & van de Weijer, J.** (2011). *Eye Tracking: A
  Comprehensive Guide to Methods and Measures.* Oxford University
  Press. — methodology basis for the calibrator's fixation /
  saccade / smooth-pursuit tests and the settled-vs-transition
  sample handling in `CalibrationTestRunner`.

## Real-time cognitive load — RIPA2

`RIPAMonitor` is a direct on-device implementation of **RIPA2**.
Cite this paper if you publish data using `LiveLoadIndex` or any
output of `RIPAMonitor`:

> Jayawardena, G., Jayawardana, Y., & Gwizdka, J. (2025).
> Measuring Mental Effort in Real Time Using Pupillometry.
> *Journal of Eye Movement Research*, 18(6), 70.
> <https://doi.org/10.3390/jemr18060070>

VLF ≈ 0.29 Hz / LF ≈ 4 Hz Savitzky–Golay derivative bands, [0, 1.5]
clip range, and 1–2 s smoothing window are taken from the paper.
`docs/RIPA_MONITOR.md` and the source files
`Assets/Scripts/EyeTracking/Metrics/{RIPAMonitor,RIPA2Analyzer,SavitzkyGolayDerivative}.cs`
carry the same citation.

## Pupillometry — IPA / LHIPA / RIPA / RIPA2 lineage

- **Marshall, S. P.** (2002). The Index of Cognitive Activity:
  measuring cognitive workload. *Proc. IEEE 7th Conf. Human
  Factors and Power Plants*, 7-5–7-9. — first wavelet-based
  pupil-oscillation cognitive-load metric.
- **Duchowski, A. T., Krejtz, K., Krejtz, I., Biele, C., Niber, T.,
  Kiefer, P., & Giannopoulos, I.** (2018). The Index of Pupillary
  Activity. *CHI '18*, Paper 282.
  <https://doi.org/10.1145/3173574.3173856> — IPA + LHIPA, the
  basis for `eyelean_analysis.metrics.lhipa`.
- **Jayawardena, G., et al.** (2022). Original RIPA. Near-real-time
  predecessor cited by Jayawardena 2025.
- **Peysakhovich, V., Causse, M., Yarrow, K., et al.** (2017).
  Frequency analysis of a task-evoked pupillary response. *Int. J.
  Psychophysiology*, 112, 40–45.
- **Medeiros, J., Couceiro, R., et al.** (2021). Software code
  complexity assessment using EEG features. *Sensors*, 21, 5128. —
  with Peysakhovich 2017, the basis for the VLF/LF band split.

## Eye-movement classification

- **Salvucci, D. D., & Goldberg, J. H.** (2000). Identifying
  fixations and saccades in eye-tracking protocols. *ETRA '00*,
  71–78. <https://doi.org/10.1145/355017.355028> — the I-VT
  reference algorithm
  `eyelean_analysis.classification.velocity_classifier` implements.

## Attention and gaze-distribution metrics

- **Krejtz, K., Duchowski, A. T., Niber, T., Krejtz, I., & Kopacz, A.**
  (2016). Eye tracking cognitive load using pupil diameter and
  microsaccades with fixed gaze. *PLoS ONE*, 11(9), e0163087.
  <https://doi.org/10.1371/journal.pone.0163087> — K-coefficient,
  implemented in
  `eyelean_analysis.classification.k_coefficient`.
- **Krejtz, K., Duchowski, A., Krejtz, I., Kopacz, A., &
  Chrząstowski-Wachtel, P.** (2016). Gaze transition entropy.
  *ETRA '16*, 191–194. — basis for
  `eyelean_analysis.metrics.entropy.transition_entropy`.
- **Shannon, C. E.** (1948). A Mathematical Theory of Communication.
  *Bell System Tech. J.*, 27(3), 379–423.
  <https://doi.org/10.1002/j.1538-7305.1948.tb01338.x> — basis for
  `GazeEntropyCalculator` and the stationary-entropy helper.

## Signal processing

- **Savitzky, A., & Golay, M. J. E.** (1964). Smoothing and
  Differentiation of Data by Simplified Least Squares Procedures.
  *Analytical Chemistry*, 36(8), 1627–1639.
  <https://doi.org/10.1021/ac60214a047>. Used for gaze-velocity
  smoothing (`eyelean_analysis.filters.savitzky_golay`) and the
  SG first-derivative filters in `RIPA2Analyzer`.
- **Schäfer, R. W.** (2011). What Is a Savitzky–Golay Filter? *IEEE
  Signal Processing Magazine*, 28(4), 111–117. — the cutoff
  approximation `fc ≈ (N+1) / (3.2M − 4.6)` `RIPA2Analyzer` uses to
  size SG filter half-widths from the live sample rate.
- **Butterworth, S.** (1930). On the theory of filter amplifiers.
  *Wireless Engineer*, 7(6), 536–541. — used by
  `eyelean_analysis.filters.butterworth` and the Unity-side
  vergence smoother.

## Avatars

- **Gonzalez-Franco, M., et al.** (2020). The Rocketbox Library
  and the Utility of Freely Available Rigged Avatars. *Frontiers
  in Virtual Reality*, 1, 20.
  <https://doi.org/10.3389/frvir.2020.561558>. Library
  `AgentManager` is designed against; CC-BY 4.0. Install
  instructions in [`docs/SKELETON_AGENTS.md`](docs/SKELETON_AGENTS.md).

## Robust statistics

- **Huber, P. J.** (1964). Robust estimation of a location
  parameter. *Annals of Math. Stat.*, 35(1), 73–101.
- **Rousseeuw, P. J., & Leroy, A. M.** (1987). *Robust Regression
  and Outlier Detection.* Wiley. — rationale for `OffsetEstimator`
  using a median residual rather than mean: blinks and mid-window
  saccades produce outliers that destroy a mean-based fit.

## Cryptographic hash

- **Rivest, R. L.** (1992). The MD5 Message-Digest Algorithm.
  RFC 1321. <https://www.rfc-editor.org/rfc/rfc1321> — used by
  `Recordable.SetUniqueId(seed)` to derive stable cross-session
  GUIDs from researcher-supplied seeds. Identity, not security.

## Software dependencies

### Python
- **NumPy** — Harris, C. R., et al. (2020). *Nature*, 585,
  357–362. <https://doi.org/10.1038/s41586-020-2649-2>
- **SciPy** — Virtanen, P., et al. (2020). *Nature Methods*, 17,
  261–272. <https://doi.org/10.1038/s41592-019-0686-2>. Butterworth
  + Savitzky–Golay coefficients, Welch PSD.
- **pandas** — McKinney, W. (2010). *Proc. SciPy*, 56–61.
  <https://doi.org/10.25080/Majora-92bf1922-00a>
- **scikit-learn** — Pedregosa, F., et al. (2011). *JMLR*, 12,
  2825–2830.
- **PyWavelets** — Lee, G. R., et al. (2019). *JOSS*, 4(36), 1237.
  <https://doi.org/10.21105/joss.01237> — offline LHIPA module.
- **matplotlib** — Hunter, J. D. (2007). *CSE*, 9(3), 90–95.
  <https://doi.org/10.1109/MCSE.2007.55>
- **plotly** — <https://plot.ly>
- **NumExpr** — <https://github.com/pydata/numexpr>
- **joblib** — <https://joblib.readthedocs.io>

### Unity / VR runtime
- **Unity Engine** — Unity Technologies. <https://unity.com>
- **OpenXR** — Khronos Group.
  <https://www.khronos.org/openxr/>
- **VIVE OpenXR Plugin** — HTC Corporation.
  <https://github.com/ViveSoftware/VIVE-OpenXR>
- **Universal Render Pipeline**, **Input System**, **TextMeshPro**,
  **AI Navigation**, **XR Interaction Toolkit** — Unity
  Technologies.
- **NUnit** + **Unity Test Framework** — EditMode and PlayMode
  tests.

## Adapted code

- The Skeleton researcher template (`Assets/Scripts/Skeleton/`) was
  adapted from a `VR_Experiments_Skeleton_Starter` project,
  rewritten into the `EyeLean.Skeleton` namespace and rewired
  against Eye_lean's post-RC recording layer.

## Citing Eye_lean

Cite the toolkit using [`CITATION.cff`](CITATION.cff). Cite the
algorithm paper too when the corresponding feature contributed to
your analysis:

| Feature | Required citation |
|---|---|
| `LiveLoadIndex` / `RIPAMonitor` | Jayawardena, Jayawardana, & Gwizdka 2025 |
| `eyelean_analysis.metrics.lhipa` | Duchowski et al. 2018 |
| `eyelean_analysis.metrics.entropy.stationary_entropy` | Shannon 1948 |
| `eyelean_analysis.metrics.entropy.transition_entropy` | Krejtz et al. 2016 (ETRA) + Shannon 1948 |
| `eyelean_analysis.classification.k_coefficient` | Krejtz et al. 2016 (PLoS ONE) |
| `eyelean_analysis.classification.velocity_classifier` | Salvucci & Goldberg 2000 |
| `eyelean_analysis.filters.savitzky_golay` | Savitzky & Golay 1964 |
| `eyelean_analysis.filters.butterworth` | Butterworth 1930 |
| Skeleton + Rocketbox avatars | Gonzalez-Franco et al. 2020 |
| Calibrator (fixation / saccade / smooth pursuit) | Holmqvist et al. 2011 |

Python module top-of-file docstrings carry the same citations;
`docs/RIPA_MONITOR.md` carries the RIPA2 reminder inline.

---

If your work belongs here and isn't, open an issue or a PR.
