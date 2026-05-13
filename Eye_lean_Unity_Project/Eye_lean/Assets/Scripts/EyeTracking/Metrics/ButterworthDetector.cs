// SPDX-License-Identifier: MIT
using System;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// <see cref="ICognitiveLoadDetector"/> wrapper around
    /// <see cref="ButterworthLfHfAnalyzer"/>. The detector's raw output is
    /// the (capped) LF/HF power ratio; the smoothed output is log(1+ratio)
    /// scaled into the shared [0, 1.5] HUD range.
    /// </summary>
    public sealed class ButterworthDetector : ICognitiveLoadDetector
    {
        public CognitiveLoadMethod Method => CognitiveLoadMethod.Butterworth;
        public string ColumnSuffix => "BW";

        private readonly ButterworthLfHfAnalyzer analyzer;
        public ButterworthLfHfAnalyzer Analyzer => analyzer;

        public ButterworthDetector(ButterworthLfHfAnalyzer analyzer)
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
