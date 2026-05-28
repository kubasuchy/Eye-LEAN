// SPDX-License-Identifier: MIT
namespace EyeLean.NBack
{
    /// <summary>
    /// Phases of the N-back working-memory experiment. Maps onto the
    /// memory-load levels validated in Jayawardena 2025 (RIPA2 paper):
    /// passive fixation (no task; Eye_lean addition), 0-back (target
    /// detection against a single fixed letter; serves as a motor /
    /// attention baseline), 1/2/3-back (working-memory load levels). Block
    /// order within a session is randomized; the enum order here is the
    /// canonical ascending-load reference.
    /// </summary>
    public enum NBackPhase
    {
        Idle,
        Instructions,
        PassiveBaseline,
        ZeroBack,
        OneBack,
        TwoBack,
        ThreeBack,
        Complete
    }

    public static class NBackPhaseExtensions
    {
        /// <summary>
        /// Numeric load level for the CSV `NBackLevel` column. Passive baseline
        /// uses -1 (no memory task); 0/1/2/3-back use their N value; non-block
        /// phases use -2 so the column is never empty / NaN.
        /// </summary>
        public static int LoadLevel(this NBackPhase phase)
        {
            switch (phase)
            {
                case NBackPhase.PassiveBaseline: return -1;
                case NBackPhase.ZeroBack: return 0;
                case NBackPhase.OneBack: return 1;
                case NBackPhase.TwoBack: return 2;
                case NBackPhase.ThreeBack: return 3;
                default: return -2;
            }
        }

        public static bool IsBlock(this NBackPhase phase)
        {
            return phase == NBackPhase.PassiveBaseline
                || phase == NBackPhase.ZeroBack
                || phase == NBackPhase.OneBack
                || phase == NBackPhase.TwoBack
                || phase == NBackPhase.ThreeBack;
        }
    }
}
