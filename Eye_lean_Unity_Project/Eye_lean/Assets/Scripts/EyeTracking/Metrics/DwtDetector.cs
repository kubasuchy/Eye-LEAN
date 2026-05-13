// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// <see cref="ICognitiveLoadDetector"/> wrapper around
    /// <see cref="DwtLfHfAnalyzer"/> (Duchowski 2026 Listing 2).
    /// </summary>
    public sealed class DwtDetector : ICognitiveLoadDetector
    {
        public CognitiveLoadMethod Method => CognitiveLoadMethod.DWT;
        public string ColumnSuffix => "DWT";

        private readonly DwtLfHfAnalyzer analyzer;
        public DwtLfHfAnalyzer Analyzer => analyzer;

        public DwtDetector(DwtLfHfAnalyzer analyzer)
        {
            this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        }

        public bool IsValid => analyzer.IsValid;
        public float CurrentRaw => (float)analyzer.CurrentRawRatio;
        public float CurrentSmoothed => (float)analyzer.CurrentSmoothed;
        public void PushSample(double pupilMm) => analyzer.PushSample(pupilMm);
        public void Reset() => analyzer.Reset();
    }
}
