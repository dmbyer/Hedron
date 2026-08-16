using System;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// The power-budget formulas themselves, as pure statics over a caller-supplied
    /// <see cref="PowerBudgetTunables"/>. <see cref="PowerBudgetSystem"/> is a thin instance facade
    /// that supplies its constructor-injected tunables snapshot to these; this class is the one
    /// home for each formula (INV-27).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why a pure split exists at all.</strong> The balance-standards editor previews the
    /// target range of every (Tier, Band) cell against *candidate, unsaved* tunables — not the
    /// composed DI instance's snapshot. Before this split it did that by constructing a throwaway
    /// <see cref="PowerBudgetSystem"/> per cell per render inside a Razor component: instantiating a
    /// DI-registered type in a component, and an allocation per cell. The preview needs a function of
    /// (tunables, tier, band), so that is what it now calls.
    /// </para>
    /// <para>
    /// This is deliberately <em>not</em> an <see cref="IPowerBudgetSystem"/> overload. Such a member
    /// would ignore instance state on every implementation and force a caller that has no use for the
    /// composed snapshot to inject the singleton anyway. It also leaves the composed instance's
    /// ctor-injected snapshot semantics untouched — a recorded decision (see
    /// <c>docs/roadmap/backlog.md</c> §Live balance-standards reload).
    /// </para>
    /// <para>
    /// Core-tier (INV-2) and pure: no domain import beyond the <c>ScoreId</c> vocabulary reached
    /// through <see cref="PowerSnapshot"/>/<see cref="PowerBudgetTunables"/>, no RNG, no clock
    /// (INV-26 moot).
    /// </para>
    /// </remarks>
    public static class PowerBudgetMath
    {
        /// <summary>
        /// Weighted sum over <paramref name="snapshot"/> per <see cref="PowerBudgetTunables.Weights"/>,
        /// plus the tier baseline contribution for each tracked score. See
        /// <see cref="IPowerBudgetSystem.Estimate"/> for the contract.
        /// </summary>
        public static int Estimate(PowerBudgetTunables tunables, PowerSnapshot snapshot, int tier = 0)
        {
            var total = 0;

            foreach (var (score, value) in snapshot.Scores)
            {
                if (tunables.Weights.TryGetValue(score, out var weight))
                    total += weight * value;
            }

            foreach (var tracked in tunables.TrackedScores)
            {
                if (tunables.Weights.TryGetValue(tracked, out var weight))
                    total += weight * (tunables.TierBaselineStep * tier);
            }

            return total;
        }

        /// <summary>
        /// Classifies a power scalar into a (Tier, Band) cell. See
        /// <see cref="IPowerBudgetSystem.Classify"/> for the contract.
        /// </summary>
        public static PowerBand Classify(PowerBudgetTunables tunables, int power)
        {
            var tier = 0;
            for (var t = tunables.MaxTier; t >= 0; t--)
            {
                if (BandAnchor(tunables, t) <= power)
                {
                    tier = t;
                    break;
                }
            }

            return new PowerBand(tier, BandWithinTier(tunables, tier, power));
        }

        /// <summary>
        /// Inverts a (Tier, Band) cell to its target power window. See
        /// <see cref="IPowerBudgetSystem.TargetRange"/> for the contract, including the fail-fast
        /// <see cref="ArgumentOutOfRangeException"/> on an out-of-table cell.
        /// </summary>
        public static PowerRange TargetRange(PowerBudgetTunables tunables, int tier, int band)
        {
            if (tier < 0 || tier > tunables.MaxTier)
                throw new ArgumentOutOfRangeException(
                    nameof(tier), tier, $"Tier must be 0-{tunables.MaxTier}.");
            if (band < 1 || band > tunables.BandsPerTier)
                throw new ArgumentOutOfRangeException(
                    nameof(band), band, $"Band must be 1-{tunables.BandsPerTier}.");

            var tierFloor = TierReferencePower(tunables, tier);
            var span = tunables.TierSpan();
            var thirdStep = span / tunables.BandsPerTier;

            var min = tierFloor + (band - 1) * thirdStep;
            var max = band == tunables.BandsPerTier
                ? tierFloor + span
                : tierFloor + band * thirdStep;

            return new PowerRange(min, max);
        }

        /// <summary>
        /// The lower power anchor for <paramref name="tier"/>. See
        /// <see cref="IPowerBudgetSystem.BandAnchor"/> for the contract.
        /// </summary>
        public static int BandAnchor(PowerBudgetTunables tunables, int tier)
            => TierReferencePower(tunables, tier) - tunables.BandSpan;

        // The reference base build's power at a tier, with no band-boundary overlap subtracted —
        // the per-tier "floor" the within-tier band subdivision partitions upward from.
        private static int TierReferencePower(PowerBudgetTunables tunables, int tier)
            => Estimate(tunables, new PowerSnapshot(tunables.ReferenceBaseScores), tier);

        private static int BandWithinTier(PowerBudgetTunables tunables, int tier, int power)
        {
            var position = power - TierReferencePower(tunables, tier);
            if (position < 0)
                return 1; // tier-boundary overlap zone — not yet at the tier's own reference power.

            var thirdStep = tunables.TierSpan() / tunables.BandsPerTier;
            if (thirdStep <= 0)
                return 1;

            return Math.Min(tunables.BandsPerTier, position / thirdStep + 1);
        }
    }
}
