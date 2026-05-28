// SPDX-License-Identifier: MIT
using System;
using UnityEngine;

namespace EyeLean.NBack
{
    /// <summary>
    /// Per-block performance aggregates. d-prime is the signal-detection
    /// sensitivity statistic (Macmillan & Creelman 2005); useful as a paper-
    /// comparable behavioral measure paired with the per-detector cognitive-
    /// load CSV columns. Serialized as JSON into the
    /// `NBackBlockResultJSON` metadata column on the block's last row.
    /// </summary>
    [Serializable]
    public struct NBackBlockResult
    {
        public int blockIndex;
        public int loadLevel;
        public int totalTrials;
        public int totalTargets;
        public int hits;
        public int misses;
        public int falseAlarms;
        public int correctRejections;
        public float hitRate;
        public float falseAlarmRate;
        public float dPrime;
        public float meanRTms;

        /// <summary>
        /// Standard d-prime with log-linear correction (Hautus 1995) so a
        /// perfect / chance block doesn't produce ±infinity. Applies the
        /// +0.5/(N+1) adjustment to each rate before z-transform.
        /// </summary>
        public static NBackBlockResult Compute(
            int blockIndex, int loadLevel,
            int hits, int misses, int falseAlarms, int correctRejections,
            float meanRTms)
        {
            int targets = hits + misses;
            int lures = falseAlarms + correctRejections;
            int total = targets + lures;

            float hitRate = targets > 0 ? (hits + 0.5f) / (targets + 1f) : 0f;
            float faRate = lures > 0 ? (falseAlarms + 0.5f) / (lures + 1f) : 0f;
            float dPrime = InverseNormalCdf(hitRate) - InverseNormalCdf(faRate);

            return new NBackBlockResult
            {
                blockIndex = blockIndex,
                loadLevel = loadLevel,
                totalTrials = total,
                totalTargets = targets,
                hits = hits,
                misses = misses,
                falseAlarms = falseAlarms,
                correctRejections = correctRejections,
                hitRate = hitRate,
                falseAlarmRate = faRate,
                dPrime = dPrime,
                meanRTms = meanRTms,
            };
        }

        // Beasley-Springer-Moro rational approximation of Φ⁻¹.
        // Precision ≈ 1e-9 over [1e-15, 1 - 1e-15]; adequate for d-prime
        // reporting at any realistic block length.
        private static float InverseNormalCdf(float p)
        {
            if (p <= 0f) return -10f;
            if (p >= 1f) return 10f;
            double x = p;
            double y = x - 0.5;
            if (Math.Abs(y) < 0.42)
            {
                double r = y * y;
                double num = ((-25.44106049637 * r + 41.39119773534) * r - 18.61500062529) * r + 2.50662823884;
                num *= y;
                double den = (((3.13082909833 * r - 21.06224101826) * r + 23.08336743743) * r - 8.47351093090) * r + 1.0;
                return (float)(num / den);
            }
            double s = x < 0.5 ? x : 1.0 - x;
            double t = Math.Log(-Math.Log(s));
            double[] c = { 0.3374754822726147, 0.9761690190917186, 0.1607979714918209,
                           0.0276438810333863, 0.0038405729373609, 0.0003951896511919,
                           0.0000321767881768, 0.0000002888167364, 0.0000003960315187 };
            double r2 = c[0] + t * (c[1] + t * (c[2] + t * (c[3] + t * (c[4] + t * (c[5] + t * (c[6] + t * (c[7] + t * c[8])))))));
            return (float)(x < 0.5 ? -r2 : r2);
        }
    }
}
