namespace Hedron.Core.Systems
{
    /// <summary>
    /// Core-tier (INV-2), generic power-budget oracle. Takes a <see cref="PowerSnapshot"/> —
    /// never an entity id — so the same one function serves every consumer (INV-19): the
    /// in-game <c>power</c>/<c>powerband</c> inspectors, the Blazor editor readout, and the
    /// <c>ProgressionSystem</c> anti-grind proxy. Pure, deterministic math (INV-26: no
    /// <see cref="IRandom"/>/<see cref="IClock"/> seam needed — no chance, no wall-clock).
    /// </summary>
    public interface IPowerBudgetSystem
    {
        /// <summary>
        /// Weighted sum over <paramref name="snapshot"/> per <see cref="PowerBudgetConstants.Weights"/>,
        /// plus (when <paramref name="tier"/> is positive) the tier baseline contribution for each
        /// of <see cref="PowerBudgetConstants.TrackedScores"/> (mirrors the Ascension tier baseline).
        /// <paramref name="tier"/> of 0 (the default) adds nothing.
        /// </summary>
        int Estimate(PowerSnapshot snapshot, int tier = 0);

        /// <summary>
        /// Classifies a power scalar into the highest tier band (0–<see cref="PowerBudgetConstants.MaxTier"/>)
        /// whose <see cref="BandAnchor"/> is at or below <paramref name="power"/>. Falls back to
        /// band 0 for a power below every anchor.
        /// </summary>
        int Classify(int power);

        /// <summary>
        /// The lower power anchor for <paramref name="tier"/>: the reference base build's power at
        /// that tier, minus <see cref="PowerBudgetConstants.BandSpan"/> (the deliberate overlap so a
        /// maxed lower-tier build can reach into the next band before formally ascending).
        /// </summary>
        int BandAnchor(int tier);
    }
}
