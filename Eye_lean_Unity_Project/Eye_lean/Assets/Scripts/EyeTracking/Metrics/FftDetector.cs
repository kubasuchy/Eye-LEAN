// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// <see cref="ICognitiveLoadDetector"/> wrapper around
    /// <see cref="FftLfHfAnalyzer"/> (Duchowski 2026 Listing 1).
    /// </summary>
    public sealed class FftDetector : ICognitiveLoadDetector
    {
        public CognitiveLoadMethod Method => CognitiveLoadMethod.FFT;
        public string ColumnSuffix => "FFT";

        private readonly FftLfHfAnalyzer analyzer;
        public FftLfHfAnalyzer Analyzer => analyzer;

        public FftDetector(FftLfHfAnalyzer analyzer)
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
