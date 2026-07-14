using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Plain-data tunables for <see cref="PowerBudgetSystem"/> — the single permitted constructor
    /// input under the snapshot-only extensibility principle (see
    /// <c>docs/design/power-model.md</c>). Composed by the host (<c>BalanceInspectionModule</c>)
    /// from either <see cref="Default"/> (no standards file present) or the loaded balance-standards
    /// document; the oracle itself never gains a registry/loader/domain reference (INV-2).
    /// Replaces the former <c>PowerBudgetConstants</c> static class.
    /// </summary>
    public sealed record PowerBudgetTunables(
        IReadOnlyDictionary<ScoreId, int> Weights,
        int BandSpan,
        int BandsPerTier,
        IReadOnlyDictionary<ScoreId, int> ReferenceBaseScores,
        int MaxTier,
        int TierBaselineStep,
        IReadOnlyList<ScoreId> TrackedScores)
    {
        /// <summary>
        /// Compiled fallback — the pre-slice constant values, used when no standards file is
        /// present (Postcondition 3) and as the golden-number baseline for oracle tests.
        /// </summary>
        public static readonly PowerBudgetTunables Default = new(
            Weights: new Dictionary<ScoreId, int>
            {
                [ScoreId.Mind] = 1,
                [ScoreId.Body] = 10,
                [ScoreId.Spirit] = 1,
                [ScoreId.Attunement] = 1,
                [ScoreId.HpMax] = 2,
                [ScoreId.ManaMax] = 0,
                [ScoreId.StaminaMax] = 0,
                [ScoreId.AstraMax] = 0,
                [ScoreId.HpCurrent] = 0,
                [ScoreId.ManaCurrent] = 0,
                [ScoreId.StaminaCurrent] = 0,
                [ScoreId.AstraCurrent] = 0,
                [ScoreId.AttackPower] = 8,
                [ScoreId.Defense] = 8,
            },
            BandSpan: 20,
            BandsPerTier: 3,
            ReferenceBaseScores: new Dictionary<ScoreId, int>
            {
                [ScoreId.Mind] = 10,
                [ScoreId.Body] = 10,
                [ScoreId.Spirit] = 10,
                [ScoreId.Attunement] = 10,
                [ScoreId.HpMax] = 100,
                [ScoreId.ManaMax] = 50,
                [ScoreId.StaminaMax] = 50,
                [ScoreId.AstraMax] = 10,
                [ScoreId.AttackPower] = 5,
                [ScoreId.Defense] = 2,
            },
            MaxTier: 6,
            TierBaselineStep: 10,
            TrackedScores: new[] { ScoreId.Body, ScoreId.HpMax });

        /// <summary>
        /// Flattens a (Tier, Band) cell into a single strictly-increasing index across the whole
        /// table (tier-major, band-minor) so drift can be expressed as one integer distance
        /// regardless of whether it crosses a tier boundary. Its only input is
        /// <see cref="BandsPerTier"/> — the one home for this index math (INV-27), replacing the
        /// former <c>BalanceAuditConstants.GlobalBandIndex</c>.
        /// </summary>
        public int GlobalBandIndex(int tier, int band) => tier * BandsPerTier + (band - 1);

        /// <summary>
        /// The per-tier power step (Σ weight[TrackedScores] × TierBaselineStep) — constant across
        /// every tier since <see cref="PowerBudgetSystem.Estimate"/>'s tier contribution is affine
        /// in tier. This is the whole tier's power span, subdivided into <see cref="BandsPerTier"/>
        /// equal thirds. One home for the formula — both the oracle and the standards store's
        /// calibration check (<c>BandSpan &lt; TierSpan / BandsPerTier</c>) call this.
        /// </summary>
        public int TierSpan()
        {
            var total = 0;
            foreach (var tracked in TrackedScores)
                if (Weights.TryGetValue(tracked, out var weight))
                    total += weight * TierBaselineStep;
            return total;
        }
    }
}
