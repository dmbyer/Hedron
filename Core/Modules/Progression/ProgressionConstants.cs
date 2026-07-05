using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// Balance constants for the progression substrate (configuration Category 3 —
    /// System Math/Balance). Co-located with <see cref="Systems.ProgressionSystem"/> so tuning
    /// changes land in the same commit as the code that reads them. Promotion to a tunable data
    /// file is deferred to a demonstrated need (OD-2) — the slice-4 simulator is the likely trigger.
    /// </summary>
    public static class ProgressionConstants
    {
        /// <summary>Linear power step granted per improvement, folded by <see cref="Systems.ProgressionEffectContributor"/>.</summary>
        public const int PowerPerImprovement = 2;

        /// <summary>Cumulative XP required to cross the first threshold (improvement 0 → 1).</summary>
        public const int ThresholdBase = 100;

        /// <summary>
        /// Growth added to the cumulative threshold per improvement already earned — the growing
        /// gap that slows the rate of gain while the power step itself stays linear.
        /// </summary>
        public const int ThresholdIncrement = 50;

        /// <summary>Inclusive lower bound of the randomized per-track base combat award.</summary>
        public const int CombatAwardMin = 8;

        /// <summary>Inclusive upper bound of the randomized per-track base combat award.</summary>
        public const int CombatAwardMax = 12;

        /// <summary>
        /// Anti-grind ratio floor: a killer-vs-victim power ratio below this rounds the award to
        /// zero (trivial victims grant nothing).
        /// </summary>
        public const double AntiGrindFloorRatio = 0.25;

        /// <summary>Anti-grind ratio cap: the scale never exceeds this (no over-strong-victim windfall).</summary>
        public const double AntiGrindCap = 1.5;

        /// <summary>The tracks a combat kill awards in slice 1 — physical-only (owner-accepted default).</summary>
        public static readonly ScoreId[] CombatTracks = { ScoreId.Body, ScoreId.HpMax };
    }
}
