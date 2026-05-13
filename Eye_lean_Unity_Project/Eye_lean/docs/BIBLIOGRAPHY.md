# Bibliography

References and citations for algorithms and methods used in the VR Eye Tracking Research Toolkit.

---

## Eye Tracking & Vergence

### Primary References

```bibtex
@article{duchowski2022vergence,
  title = {3D Gaze in Virtual Reality: Vergence, Calibration, Event Detection},
  author = {Duchowski, Andrew T. and Krejtz, Krzysztof and Volonte, Matias and Hughes, Chris and Brescia-Zapata, Marta and Orero, Pilar},
  journal = {Procedia Computer Science},
  volume = {207},
  pages = {1641--1648},
  year = {2022},
  publisher = {Elsevier},
  doi = {10.1016/j.procs.2022.09.221},
  url = {https://www.sciencedirect.com/science/article/pii/S187705092201105X},
  note = {Primary reference for vergence point calculation algorithm (Paper Algorithm)}
}
```

This paper provides the mathematical foundation for the "Paper Algorithm" vergence calculation method, including vector-vector intersection for gaze depth estimation.

### Eye-Tracking Methodology

```bibtex
@book{holmqvist2011eyetracking,
  title     = {Eye Tracking: A Comprehensive Guide to Methods and Measures},
  author    = {Holmqvist, Kenneth and Nystr{\"o}m, Marcus and Andersson, Richard and Dewhurst, Richard and Jarodzka, Halszka and van de Weijer, Joost},
  year      = {2011},
  publisher = {Oxford University Press},
  isbn      = {9780199697083}
}
```

Methodology basis for the calibrator's fixation / saccade / smooth-pursuit
tests and the settled-vs-transition sample handling in `CalibrationTestRunner`.

### Fixation / Saccade Classification (I-VT)

```bibtex
@inproceedings{salvucci2000identifying,
  title     = {Identifying fixations and saccades in eye-tracking protocols},
  author    = {Salvucci, Dario D. and Goldberg, Joseph H.},
  booktitle = {Proceedings of the 2000 Symposium on Eye Tracking Research \& Applications (ETRA '00)},
  pages     = {71--78},
  year      = {2000},
  publisher = {ACM},
  doi       = {10.1145/355017.355028}
}
```

Reference algorithm for `eyelean_analysis.classification.velocity_classifier`.

### OpenXR Eye Tracking

```bibtex
@misc{openxr_eye_tracking,
  title = {OpenXR Eye Gaze Interaction Extension},
  author = {{The Khronos Group}},
  year = {2023},
  url = {https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_EXT_eye_gaze_interaction}
}

@misc{vive_openxr_eye,
  title = {VIVE OpenXR Eye Tracker Extension (XR\_HTC\_eye\_tracker)},
  author = {{HTC Corporation}},
  year = {2024},
  url = {https://github.com/ViveSoftware/VIVE-OpenXR}
}
```

---

## Signal Processing & Filtering

### Savitzky-Golay Filter

```bibtex
@article{savitzky1964smoothing,
  title = {Smoothing and Differentiation of Data by Simplified Least Squares Procedures},
  author = {Savitzky, Abraham and Golay, Marcel JE},
  journal = {Analytical Chemistry},
  volume = {36},
  number = {8},
  pages = {1627--1639},
  year = {1964},
  publisher = {ACS Publications},
  doi = {10.1021/ac60214a047}
}
```

### Butterworth Filter

```bibtex
@article{butterworth1930theory,
  title = {On the Theory of Filter Amplifiers},
  author = {Butterworth, Stephen},
  journal = {Wireless Engineer},
  volume = {7},
  number = {6},
  pages = {536--541},
  year = {1930}
}
```

### Exponential Moving Average

```bibtex
@book{brown1963smoothing,
  title = {Smoothing, Forecasting and Prediction of Discrete Time Series},
  author = {Brown, Robert Goodell},
  year = {1963},
  publisher = {Prentice-Hall}
}
```

Brown (1963) covers the textbook EMA recursion only — i.e. the final blend `y_t = α x_t + (1−α) y_{t−1}` in `VergenceSmoothingProcessor.ApplyWeightedEMA`. The preceding quality-and-time-weighted history average and the adaptive-α rescaling are Eye_lean implementation details with no paper basis.

---

## Pupillometry — Cognitive Load

### LHIPA / IPA (offline, wavelet-based)

```bibtex
@inproceedings{duchowski2020lhipa,
  title     = {The Low/High Index of Pupillary Activity},
  author    = {Duchowski, Andrew T. and Krejtz, Krzysztof and Wnuk, Justyna and Sankarasubramanian, Krishnamoorthy and Andersson, Richard and Krejtz, Izabela},
  booktitle = {Proceedings of the 2020 CHI Conference on Human Factors in Computing Systems (CHI '20)},
  pages     = {1--12},
  year      = {2020},
  publisher = {ACM},
  doi       = {10.1145/3313831.3376394}
}

@inproceedings{duchowski2018ipa,
  title     = {The Index of Pupillary Activity: Measuring Cognitive Load Vis-{\`a}-Vis Task Difficulty with Pupil Oscillation of Pupil Diameter},
  author    = {Duchowski, Andrew T. and Krejtz, Krzysztof and Krejtz, Izabela and Biele, Cezary and Niedzielska, Anna and Kiefer, Peter and Raubal, Martin and Giannopoulos, Ioannis},
  booktitle = {Proceedings of the 2018 CHI Conference on Human Factors in Computing Systems (CHI '18)},
  pages     = {1--13},
  year      = {2018},
  publisher = {ACM},
  doi       = {10.1145/3173574.3173856}
}
```

Implemented in `eyelean_analysis.metrics.lhipa`. The LHIPA module
follows Duchowski 2020 Listing 1 (Symlets-16, paired
`j_HF = 1` / `j_LF = floor(maxlevel / 2)` detail bands, modulus
maxima count against Donoho's universal threshold).

### RIPA2 (on-device, real-time)

```bibtex
@article{jayawardena2025ripa2,
  title   = {Measuring Mental Effort in Real Time Using Pupillometry},
  author  = {Jayawardena, Gavindya and Jayawardana, Yasith and Gwizdka, Jacek},
  journal = {Journal of Eye Movement Research},
  volume  = {18},
  number  = {6},
  pages   = {70},
  year    = {2025},
  doi     = {10.3390/jemr18060070}
}
```

Implemented as `RIPAMonitor` / `RIPA2Analyzer` on the Unity side. VLF
≈ 0.29 Hz / LF ≈ 4 Hz Savitzky–Golay derivative bands, `[0, 1.5]`
clip range and 1–2 s smoothing window are taken from the paper. The
`LiveLoadIndex` CSV column is the per-sample RIPA2 output.

---

## Information Theory

### Shannon Entropy

```bibtex
@article{shannon1948mathematical,
  title = {A Mathematical Theory of Communication},
  author = {Shannon, Claude E},
  journal = {The Bell System Technical Journal},
  volume = {27},
  number = {3},
  pages = {379--423},
  year = {1948},
  publisher = {Nokia Bell Labs}
}
```

### Gaze Transition Entropy (GTE)

The transition-entropy formula and the `Hmax = log2(N)` normalisation
convention used by `eyelean_analysis.metrics.entropy.fixation_entropy`
follow Krejtz 2015.

```bibtex
@article{krejtz2015gaze,
  title  = {Entropy-based statistical analysis of eye movement transitions},
  author = {Krejtz, Krzysztof and Szmidt, Tomasz and Duchowski, Andrew T and Krejtz, Izabela},
  journal = {ACM Transactions on Applied Perception},
  volume = {13},
  number = {1},
  pages  = {4:1--4:20},
  year   = {2015},
  doi    = {10.1145/2834121}
}
```

### Gaze Entropy as a Measure of Visual Scanning Efficiency (review)

The Shiferaw 2019 review consolidates the SGE/GTE conventions
Eye_lean follows: entropy is a property of the **fixation sequence**
(not raw samples), and both raw bits and normalised-by-`log2(N)`
values are reported so results compare across discretisations.

```bibtex
@article{shiferaw2019review,
  title  = {A review of gaze entropy as a measure of visual scanning efficiency},
  author = {Shiferaw, Brook and Downey, Luke and Crewther, David},
  journal = {Neuroscience \& Biobehavioral Reviews},
  volume = {96},
  pages  = {353--366},
  year   = {2019},
  doi    = {10.1016/j.neubiorev.2018.12.007}
}
```

---

## Attention Metrics

### K-Coefficient (ambient ↔ focal attention)

```bibtex
@article{krejtz2016kcoefficient,
  title   = {Eye tracking cognitive load using pupil diameter and microsaccades with fixed gaze},
  author  = {Krejtz, Krzysztof and Duchowski, Andrew T. and Niedzielska, Anna and Biele, Cezary and Krejtz, Izabela},
  journal = {PLoS ONE},
  volume  = {11},
  number  = {9},
  pages   = {e0163087},
  year    = {2016},
  doi     = {10.1371/journal.pone.0163087}
}
```

Implemented in `eyelean_analysis.classification.k_coefficient`. K > 0 ⇒
focal attention; K < 0 ⇒ ambient. Pooled-stats convention follows
Krejtz 2016 Eq. 1 verbatim.

---

## Avatars

```bibtex
@article{gonzalezfranco2020rocketbox,
  title   = {The Rocketbox Library and the Utility of Freely Available Rigged Avatars},
  author  = {Gonzalez-Franco, Mar and Ofek, Eyal and Pan, Ye and Antley, Angus and Steed, Anthony and Spanlang, Bernhard and Maselli, Antonella and Banakou, Domna and Pelechano, Nuria and Orts-Escolano, Sergio and others},
  journal = {Frontiers in Virtual Reality},
  volume  = {1},
  pages   = {561558},
  year    = {2020},
  doi     = {10.3389/frvir.2020.561558}
}
```

Avatar library `AgentManager` is designed against (CC-BY 4.0). Install
instructions in [`docs/SKELETON_AGENTS.md`](../../docs/SKELETON_AGENTS.md).

---

## Hardware & SDK Documentation

### HTC VIVE Focus Vision

```bibtex
@misc{vive_focus_vision,
  title = {VIVE Focus Vision Developer Guide},
  author = {{HTC Corporation}},
  year = {2024},
  url = {https://developer.vive.com/resources/vive-focus-vision/}
}
```

### Unity XR

```bibtex
@misc{unity_xr,
  title = {Unity XR Interaction Toolkit Documentation},
  author = {{Unity Technologies}},
  year = {2024},
  url = {https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@latest}
}
```

---

## Software Dependencies

### Unity Packages

| Package | Version | License | URL |
|---------|---------|---------|-----|
| com.htc.upm.vive.openxr | 2.5.1 | Apache 2.0 | [GitHub](https://github.com/ViveSoftware/VIVE-OpenXR) |
| com.unity.xr.openxr | 1.16.1 | Unity License | [Unity](https://docs.unity3d.com/Packages/com.unity.xr.openxr@latest) |
| com.unity.render-pipelines.universal | 17.3.0 | Unity License | Unity Package Manager |
| com.unity.inputsystem | 1.16.0 | Unity License | Unity Package Manager |

### Python Libraries

| Library | License | URL |
|---------|---------|-----|
| Flask | BSD-3-Clause | [PyPI](https://pypi.org/project/Flask/) |
| pandas | BSD-3-Clause | [PyPI](https://pypi.org/project/pandas/) |
| matplotlib | PSF | [PyPI](https://pypi.org/project/matplotlib/) |
| seaborn | BSD-3-Clause | [PyPI](https://pypi.org/project/seaborn/) |
| plotly | MIT | [PyPI](https://pypi.org/project/plotly/) |
| scikit-learn | BSD-3-Clause | [PyPI](https://pypi.org/project/scikit-learn/) |

---

For software dependencies, dataset attributions, and the full credits
list with adapted-code provenance, see
[`ACKNOWLEDGMENTS.md`](../../ACKNOWLEDGMENTS.md). For the per-algorithm
feature-to-citation table (which paper to cite when reporting which
output), see the *Citing Eye_lean* section of `ACKNOWLEDGMENTS.md`.

---

*Last updated: 2026-05-13*
