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

## TODO: Citations to Add

- [x] ~~Confirm full citation for "3D Gaze in Virtual Reality" paper~~ (Duchowski et al., 2022)
- [x] ~~Add citations for gaze entropy analysis methods if based on prior work~~ (Krejtz 2015 + Shiferaw, Downey & Crewther 2019; see entries above)
- [ ] Add Unity 6 documentation references
- [ ] Add any additional eye tracking validation methodology papers

---

*Last updated: 2024-12-12*
