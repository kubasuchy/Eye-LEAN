// SPDX-License-Identifier: MIT
using UnityEngine;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Identifier for a cognitive-load detection method. Each value maps to
    /// one <see cref="ICognitiveLoadDetector"/> implementation, one CSV
    /// column suffix (e.g. <c>LiveLoadIndex_BW</c>), and one HUD selection
    /// option on <see cref="RIPAMonitor"/>.
    ///
    /// RIPA2 is the v1.3+ default and remains the legacy "RIPA" path; the
    /// other three were added in v1.0.1 from Duchowski 2026, "Real-Time
    /// Cognitive Load Measurement of Pupillary Oscillation", Proc. ACM CGIT
    /// 9(2) Article 23. All four are implemented; FFT and DWT are disabled
    /// by default on <see cref="RIPAMonitor"/> because they need a 30+ s
    /// warm-up window before they emit a first reading.
    /// </summary>
    public enum CognitiveLoadMethod
    {
        /// <summary>Jayawardena et al. 2025 RIPA2 (Savitzky–Golay derivative² difference).</summary>
        RIPA2 = 0,
        /// <summary>Duchowski 2026 Butterworth IIR LF/HF power ratio (paper's primary contribution; 1 s warm-up).</summary>
        Butterworth = 1,
        /// <summary>Duchowski 2026 FFT periodogram LF/HF (34 s warm-up at 60 Hz).</summary>
        FFT = 2,
        /// <summary>Duchowski 2026 DWT (db4) LF/HF energy ratio (34 s warm-up at 60 Hz).</summary>
        DWT = 3,
    }

    /// <summary>
    /// Common interface for streaming pupil-based cognitive-load detectors.
    /// Implementations consume one pupil-diameter sample per call and expose
    /// a smoothed current value suitable for HUD display.
    ///
    /// Output convention: <see cref="CurrentSmoothed"/> lives in [0, 1.5],
    /// matching the historical RIPA2 clip range so the existing
    /// <see cref="RIPAGauge"/> color thresholds (green/yellow/red) work
    /// unchanged when the user switches detectors. <see cref="CurrentRaw"/>
    /// is the pre-smoothing/pre-scaling value with detector-specific units
    /// (RIPA2: derivative² difference; LF/HF detectors: power ratio).
    ///
    /// Pure data-plane interface — no Unity types — so detectors can be
    /// unit-tested in EditMode and run identically under deterministic
    /// replay against recorded pupil streams.
    /// </summary>
    public interface ICognitiveLoadDetector
    {
        CognitiveLoadMethod Method { get; }

        /// <summary>Short CSV column suffix, e.g. "RIPA2", "BW", "FFT", "DWT".</summary>
        string ColumnSuffix { get; }

        /// <summary>True once the detector has buffered enough samples to produce a valid reading.</summary>
        bool IsValid { get; }

        /// <summary>Detector-native raw output (units differ by detector — see <see cref="Method"/>).</summary>
        float CurrentRaw { get; }

        /// <summary>HUD-ready value in [0, 1.5], comparable across detectors at a coarse level.</summary>
        float CurrentSmoothed { get; }

        /// <summary>Push one pupil-diameter sample (millimeters). NaN/Inf are ignored.</summary>
        void PushSample(double pupilMm);

        /// <summary>Reset all internal buffers and filter state.</summary>
        void Reset();
    }
}
