// SPDX-License-Identifier: MIT
using System.Collections.Generic;

namespace EyeTracking.Metrics
{
    /// <summary>How a planned CSV column maps to a RIPACSVColumn value getter.</summary>
    public enum CognitiveLoadColumnKind
    {
        /// <summary>Legacy "LiveLoadIndex" alias — reflects the displayed detector.</summary>
        LegacyDisplayed,
        /// <summary>Per-method smoothed value, e.g. "LiveLoadIndex_RIPA2".</summary>
        DetectorSmoothed,
        /// <summary>Per-method raw ratio, e.g. "LiveLoadIndex_BW_Raw".</summary>
        DetectorRaw,
    }

    /// <summary>One planned CSV column: its name, how to source its value, and (for per-method kinds) which method.</summary>
    public struct CognitiveLoadColumn
    {
        public string Name;
        public CognitiveLoadColumnKind Kind;
        public CognitiveLoadMethod Method; // ignored for LegacyDisplayed

        public CognitiveLoadColumn(string name, CognitiveLoadColumnKind kind, CognitiveLoadMethod method)
        {
            Name = name;
            Kind = kind;
            Method = method;
        }
    }

    /// <summary>
    /// Pure decision of which CSV columns <see cref="RIPACSVColumn"/> registers
    /// for a given config. Extracted so the "omit disabled methods" rule is
    /// unit-testable without a live scene. Column ORDER is contract-significant
    /// (registration order == CSV column order) and matches the pre-feature
    /// layout: legacy alias, RIPA2, BW, BW_Raw, FFT, DWT.
    /// </summary>
    public static class CognitiveLoadColumns
    {
        public const string LegacyDefaultName = "LiveLoadIndex";

        public static List<CognitiveLoadColumn> Plan(
            CognitiveLoadConfig cfg, bool butterworthRecordRawRatio, string legacyColumnName)
        {
            var cols = new List<CognitiveLoadColumn>();

            // "Master off" or "master on but every method off" == no columns.
            if (!cfg.CollectsAnything) return cols;

            if (!string.IsNullOrEmpty(legacyColumnName))
                cols.Add(new CognitiveLoadColumn(
                    legacyColumnName, CognitiveLoadColumnKind.LegacyDisplayed, CognitiveLoadMethod.RIPA2));

            if (cfg.IsEnabled(CognitiveLoadMethod.RIPA2))
                cols.Add(new CognitiveLoadColumn(
                    "LiveLoadIndex_RIPA2", CognitiveLoadColumnKind.DetectorSmoothed, CognitiveLoadMethod.RIPA2));

            if (cfg.IsEnabled(CognitiveLoadMethod.Butterworth))
            {
                cols.Add(new CognitiveLoadColumn(
                    "LiveLoadIndex_BW", CognitiveLoadColumnKind.DetectorSmoothed, CognitiveLoadMethod.Butterworth));
                if (butterworthRecordRawRatio)
                    cols.Add(new CognitiveLoadColumn(
                        "LiveLoadIndex_BW_Raw", CognitiveLoadColumnKind.DetectorRaw, CognitiveLoadMethod.Butterworth));
            }

            if (cfg.IsEnabled(CognitiveLoadMethod.FFT))
                cols.Add(new CognitiveLoadColumn(
                    "LiveLoadIndex_FFT", CognitiveLoadColumnKind.DetectorSmoothed, CognitiveLoadMethod.FFT));

            if (cfg.IsEnabled(CognitiveLoadMethod.DWT))
                cols.Add(new CognitiveLoadColumn(
                    "LiveLoadIndex_DWT", CognitiveLoadColumnKind.DetectorSmoothed, CognitiveLoadMethod.DWT));

            return cols;
        }
    }
}
