// SPDX-License-Identifier: MIT
using UnityEngine;
using EyeTracking.Components;
using EyeTracking.Core;
using EyeTracking.Data;

namespace EyeTracking.Metrics
{
    /// <summary>
    /// Default <see cref="IPupilSampleSource"/> for Eye_lean. Prefers the
    /// per-scene <see cref="EyeTracker"/> MonoBehaviour (richer
    /// EyeFrameSample with vergence + raycast data) and falls back to the
    /// raw <see cref="EyeTrackerFactory"/> for scenes that only have the
    /// hardware-tracker stack.
    ///
    /// This adapter is the ONLY Eye_lean coupling point in the cognitive-
    /// load monitor; <see cref="RIPAMonitor"/> consumes it via the
    /// <see cref="IPupilSampleSource"/> interface, so external projects can
    /// substitute their own adapter (e.g., wrapping Pupil Labs, Tobii,
    /// HTC Eye SDK) by implementing the same interface.
    ///
    /// The cached <see cref="EyeTracker"/> reference is re-resolved when
    /// it becomes invalid (scene transitions); the <see cref="IEyeTracker"/>
    /// fallback is resolved per call (the factory is cheap).
    /// </summary>
    public sealed class EyeLeanPupilSampleSource : IPupilSampleSource
    {
        private EyeTracker cachedTracker;

        public double GetLatestPupilDiameterMm()
        {
            EyeTracker t = ResolveTracker();
            if (t != null)
            {
                EyeFrameSample s = t.SampleSnapshot();
                bool hasL = s.HasLeftValid && s.LeftPupilDiameter > 0f && !float.IsNaN(s.LeftPupilDiameter);
                bool hasR = s.HasRightValid && s.RightPupilDiameter > 0f && !float.IsNaN(s.RightPupilDiameter);
                if (hasL && hasR) return (s.LeftPupilDiameter + s.RightPupilDiameter) * 0.5;
                if (hasL) return s.LeftPupilDiameter;
                if (hasR) return s.RightPupilDiameter;
                return double.NaN;
            }

            try
            {
                IEyeTracker raw = EyeTrackerFactory.GetEyeTracker();
                if (raw == null || !raw.IsAvailable) return double.NaN;
                bool hasL = raw.GetLeftPupilDiameter(out float lmm) && lmm > 0f;
                bool hasR = raw.GetRightPupilDiameter(out float rmm) && rmm > 0f;
                if (hasL && hasR) return (lmm + rmm) * 0.5;
                if (hasL) return lmm;
                if (hasR) return rmm;
            }
            catch (System.Exception)
            {
                // Hardware adapters may throw during shutdown / device loss;
                // surfacing as NaN keeps the detector chain safe.
            }
            return double.NaN;
        }

        public float SamplingRateHz
        {
            get
            {
                try
                {
                    IEyeTracker raw = EyeTrackerFactory.GetEyeTracker();
                    return raw != null ? raw.SamplingRateHz : 0f;
                }
                catch (System.Exception)
                {
                    return 0f;
                }
            }
        }

        private EyeTracker ResolveTracker()
        {
            if (cachedTracker == null || !cachedTracker.enabled)
            {
                cachedTracker = null;
                foreach (var t in Object.FindObjectsByType<EyeTracker>(FindObjectsSortMode.None))
                {
                    if (t.enabled) { cachedTracker = t; break; }
                }
            }
            return cachedTracker;
        }
    }
}
