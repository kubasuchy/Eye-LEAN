// SPDX-License-Identifier: MIT
namespace EyeTracking.Metrics
{
    /// <summary>
    /// Immutable snapshot of a scene's cognitive-load collection preferences,
    /// read by <see cref="RIPAMonitorBootstrap"/> before it decides whether/how
    /// to spawn the monitor. Supplied by any scene component implementing
    /// <see cref="ICognitiveLoadConfigProvider"/> (Eye_lean's EyeTracker does).
    /// When no provider is present the bootstrap uses <see cref="Default"/>
    /// (everything on), so provider-less scenes behave exactly as before this
    /// feature. Governs computation + CSV columns only — the on-screen gauge is
    /// owned separately by EyeTracker.spawnCognitiveLoadOverlay.
    /// </summary>
    public struct CognitiveLoadConfig
    {
        public bool Collect;
        public bool Ripa2;
        public bool Butterworth;
        public bool Fft;
        public bool Dwt;
        public CognitiveLoadMethod DisplayedMethod;

        /// <summary>Pre-feature behavior: master on, all four detectors on, RIPA2 displayed.</summary>
        public static CognitiveLoadConfig Default => new CognitiveLoadConfig
        {
            Collect = true,
            Ripa2 = true,
            Butterworth = true,
            Fft = true,
            Dwt = true,
            DisplayedMethod = CognitiveLoadMethod.RIPA2,
        };

        /// <summary>True if at least one detector method is enabled.</summary>
        public bool AnyMethodEnabled => Ripa2 || Butterworth || Fft || Dwt;

        /// <summary>
        /// True only when collection is requested AND at least one method is
        /// enabled. "Master on but every method off" normalizes to "off".
        /// </summary>
        public bool CollectsAnything => Collect && AnyMethodEnabled;

        /// <summary>Whether a specific method should run, honoring the master switch.</summary>
        public bool IsEnabled(CognitiveLoadMethod method)
        {
            if (!Collect) return false;
            switch (method)
            {
                case CognitiveLoadMethod.RIPA2: return Ripa2;
                case CognitiveLoadMethod.Butterworth: return Butterworth;
                case CognitiveLoadMethod.FFT: return Fft;
                case CognitiveLoadMethod.DWT: return Dwt;
                default: return false;
            }
        }
    }

    /// <summary>
    /// Implemented by a scene component that governs cognitive-load collection
    /// (Eye_lean's EyeTracker). <see cref="RIPAMonitorBootstrap"/> scans the
    /// active scene for the first provider and applies its config. Kept as an
    /// interface (rather than referencing EyeTracker directly) so external rigs
    /// can supply their own without coupling the monitor to Eye_lean.
    /// </summary>
    public interface ICognitiveLoadConfigProvider
    {
        CognitiveLoadConfig GetCognitiveLoadConfig();
    }
}
