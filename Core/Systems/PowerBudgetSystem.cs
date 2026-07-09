using System;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Core-tier (INV-2) implementation of <see cref="IPowerBudgetSystem"/>. Takes no injected
    /// dependencies and imports no <c>Core/Modules/&lt;Feature&gt;/</c> domain type — every input
    /// is a static balance constant on <see cref="PowerBudgetConstants"/> (which mirrors both
    /// <c>CharacterDefaultsOptions</c> and <c>AscensionConstants</c> as co-located constants,
    /// rather than referencing the domain <c>Account</c>/<c>Ascension</c> modules) or the
    /// caller-supplied <see cref="PowerSnapshot"/>.
    /// </summary>
    public sealed class PowerBudgetSystem : IPowerBudgetSystem
    {
        public int Estimate(PowerSnapshot snapshot, int tier = 0)
        {
            var total = 0;

            foreach (var (score, value) in snapshot.Scores)
            {
                if (PowerBudgetConstants.Weights.TryGetValue(score, out var weight))
                    total += weight * value;
            }

            foreach (var tracked in PowerBudgetConstants.TrackedScores)
            {
                if (PowerBudgetConstants.Weights.TryGetValue(tracked, out var weight))
                    total += weight * (PowerBudgetConstants.TierBaselineStep * tier);
            }

            return total;
        }

        public PowerBand Classify(int power)
        {
            var tier = 0;
            for (var t = PowerBudgetConstants.MaxTier; t >= 0; t--)
            {
                if (BandAnchor(t) <= power)
                {
                    tier = t;
                    break;
                }
            }

            return new PowerBand(tier, BandWithinTier(tier, power));
        }

        public PowerRange TargetRange(int tier, int band)
        {
            if (tier < 0 || tier > PowerBudgetConstants.MaxTier)
                throw new ArgumentOutOfRangeException(
                    nameof(tier), tier, $"Tier must be 0-{PowerBudgetConstants.MaxTier}.");
            if (band < 1 || band > PowerBudgetConstants.BandsPerTier)
                throw new ArgumentOutOfRangeException(
                    nameof(band), band, $"Band must be 1-{PowerBudgetConstants.BandsPerTier}.");

            var tierFloor = TierReferencePower(tier);
            var span = TierSpan();
            var thirdStep = span / PowerBudgetConstants.BandsPerTier;

            var min = tierFloor + (band - 1) * thirdStep;
            var max = band == PowerBudgetConstants.BandsPerTier
                ? tierFloor + span
                : tierFloor + band * thirdStep;

            return new PowerRange(min, max);
        }

        public int BandAnchor(int tier)
            => TierReferencePower(tier) - PowerBudgetConstants.BandSpan;

        // The reference base build's power at a tier, with no band-boundary overlap subtracted —
        // the per-tier "floor" the within-tier band subdivision partitions upward from.
        private int TierReferencePower(int tier)
            => Estimate(new PowerSnapshot(PowerBudgetConstants.ReferenceBaseScores), tier);

        // The per-tier power step (Σ weight[TrackedScores] × TierBaselineStep) — constant across
        // every tier since Estimate's tier contribution is affine in tier. This is the whole
        // tier's power span, subdivided into PowerBudgetConstants.BandsPerTier equal thirds.
        private static int TierSpan()
        {
            var total = 0;
            foreach (var tracked in PowerBudgetConstants.TrackedScores)
            {
                if (PowerBudgetConstants.Weights.TryGetValue(tracked, out var weight))
                    total += weight * PowerBudgetConstants.TierBaselineStep;
            }
            return total;
        }

        private int BandWithinTier(int tier, int power)
        {
            var position = power - TierReferencePower(tier);
            if (position < 0)
                return 1; // tier-boundary overlap zone — not yet at the tier's own reference power.

            var thirdStep = TierSpan() / PowerBudgetConstants.BandsPerTier;
            if (thirdStep <= 0)
                return 1;

            return Math.Min(PowerBudgetConstants.BandsPerTier, position / thirdStep + 1);
        }
    }
}
