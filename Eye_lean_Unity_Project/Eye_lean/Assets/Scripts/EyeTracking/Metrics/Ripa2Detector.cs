// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// <see cref="ICognitiveLoadDetector"/> adapter around the existing
    /// <see cref="RIPA2Analyzer"/>. Preserves the v1.3+ RIPA2 behavior exactly;
    /// added in v1.0.1 only so RIPA2 can sit alongside the new Duchowski 2026
    /// detectors under a uniform interface.
    /// </summary>
    public sealed class Ripa2Detector : ICognitiveLoadDetector
    {
        public CognitiveLoadMethod Method => CognitiveLoadMethod.RIPA2;
        public string ColumnSuffix => "RIPA2";

        private readonly RIPA2Analyzer analyzer;
        public RIPA2Analyzer Analyzer => analyzer;

        public Ripa2Detector(RIPA2Analyzer analyzer)
        {
            this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        }

        public bool IsValid => analyzer.IsValid;
        public float CurrentRaw => (float)analyzer.CurrentRaw;
        public float CurrentSmoothed => (float)analyzer.CurrentSmoothed;
        public void PushSample(double pupilMm) => analyzer.PushSample(pupilMm);
        public void Reset() => analyzer.Reset();
    }
}
