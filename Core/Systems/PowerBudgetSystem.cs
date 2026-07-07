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

        public int Classify(int power)
        {
            for (var band = PowerBudgetConstants.MaxTier; band >= 0; band--)
            {
                if (BandAnchor(band) <= power)
                    return band;
            }

            return 0;
        }

        public int BandAnchor(int tier)
            => Estimate(new PowerSnapshot(PowerBudgetConstants.ReferenceBaseScores), tier) - PowerBudgetConstants.BandSpan;
    }
}
