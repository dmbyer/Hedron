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
        /// Weighted sum over <paramref name="snapshot"/> per <see cref="PowerBudgetTunables.Weights"/>,
        /// plus (when <paramref name="tier"/> is positive) the tier baseline contribution for each
        /// of <see cref="PowerBudgetTunables.TrackedScores"/> (mirrors the Ascension tier baseline).
        /// <paramref name="tier"/> of 0 (the default) adds nothing.
        /// </summary>
        int Estimate(PowerSnapshot snapshot, int tier = 0);

        /// <summary>
        /// Classifies a power scalar into a <see cref="PowerBand"/> cell: <c>Tier</c> is the highest
        /// tier (0&#8211;<see cref="PowerBudgetTunables.MaxTier"/>) whose <see cref="BandAnchor"/> is
        /// at or below <paramref name="power"/> (falling back to tier 0 for a power below every
        /// anchor — the shipped tier-boundary hysteresis, retained). <c>Band</c> (1&#8211;3) then
        /// buckets the position within that tier's power span into thirds (low/mid/high); a power
        /// still in the tier-boundary overlap (below the tier's own reference power) floors to band 1.
        /// Computed <c>Band</c> is never 0 &#8212; that value is exclusively the authored "unbanded" tag.
        /// </summary>
        PowerBand Classify(int power);

        /// <summary>
        /// Inverts a <see cref="PowerBand"/> cell to its target power window — the near-free
        /// reflection of the anchor table used for forward design (author toward this range) and
        /// the drift audit. Within a tier the three bands partition the tier's span (no overlap);
        /// band 3's <see cref="PowerRange.MaxPower"/> abuts the next tier's band-1
        /// <see cref="PowerRange.MinPower"/>. Throws <see cref="System.ArgumentOutOfRangeException"/>
        /// for a <paramref name="tier"/> outside <c>[0, MaxTier]</c> or a <paramref name="band"/>
        /// outside <c>[1, BandsPerTier]</c> (fail-fast — never a reverse-engineered stat block).
        /// </summary>
        PowerRange TargetRange(int tier, int band);

        /// <summary>
        /// The lower power anchor for <paramref name="tier"/>: the reference base build's power at
        /// that tier, minus <see cref="PowerBudgetTunables.BandSpan"/> (the deliberate overlap so a
        /// maxed lower-tier build can reach into the next band before formally ascending).
        /// </summary>
        int BandAnchor(int tier);
    }
}
