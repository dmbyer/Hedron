using System;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Core-tier (INV-2) implementation of <see cref="IPowerBudgetSystem"/>. Imports no
    /// <c>Core/Modules/&lt;Feature&gt;/</c> domain type — every input is either the
    /// constructor-supplied <see cref="PowerBudgetTunables"/> plain-data record or the
    /// caller-supplied <see cref="PowerSnapshot"/>. The tunables record is the one permitted
    /// constructor dependency under the snapshot-only extensibility principle (see
    /// <c>docs/design/power-model.md</c>) — never a registry, loader, or domain reference.
    /// </summary>
    public sealed class PowerBudgetSystem : IPowerBudgetSystem
    {
        private readonly PowerBudgetTunables _tunables;

        public PowerBudgetSystem(PowerBudgetTunables tunables)
        {
            _tunables = tunables;
        }

        public int Estimate(PowerSnapshot snapshot, int tier = 0)
        {
            var total = 0;

            foreach (var (score, value) in snapshot.Scores)
            {
                if (_tunables.Weights.TryGetValue(score, out var weight))
                    total += weight * value;
            }

            foreach (var tracked in _tunables.TrackedScores)
            {
                if (_tunables.Weights.TryGetValue(tracked, out var weight))
                    total += weight * (_tunables.TierBaselineStep * tier);
            }

            return total;
        }

        public PowerBand Classify(int power)
        {
            var tier = 0;
            for (var t = _tunables.MaxTier; t >= 0; t--)
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
            if (tier < 0 || tier > _tunables.MaxTier)
                throw new ArgumentOutOfRangeException(
                    nameof(tier), tier, $"Tier must be 0-{_tunables.MaxTier}.");
            if (band < 1 || band > _tunables.BandsPerTier)
                throw new ArgumentOutOfRangeException(
                    nameof(band), band, $"Band must be 1-{_tunables.BandsPerTier}.");

            var tierFloor = TierReferencePower(tier);
            var span = _tunables.TierSpan();
            var thirdStep = span / _tunables.BandsPerTier;

            var min = tierFloor + (band - 1) * thirdStep;
            var max = band == _tunables.BandsPerTier
                ? tierFloor + span
                : tierFloor + band * thirdStep;

            return new PowerRange(min, max);
        }

        public int BandAnchor(int tier)
            => TierReferencePower(tier) - _tunables.BandSpan;

        // The reference base build's power at a tier, with no band-boundary overlap subtracted —
        // the per-tier "floor" the within-tier band subdivision partitions upward from.
        private int TierReferencePower(int tier)
            => Estimate(new PowerSnapshot(_tunables.ReferenceBaseScores), tier);

        private int BandWithinTier(int tier, int power)
        {
            var position = power - TierReferencePower(tier);
            if (position < 0)
                return 1; // tier-boundary overlap zone — not yet at the tier's own reference power.

            var thirdStep = _tunables.TierSpan() / _tunables.BandsPerTier;
            if (thirdStep <= 0)
                return 1;

            return Math.Min(_tunables.BandsPerTier, position / thirdStep + 1);
        }
    }
}
