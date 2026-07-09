namespace Hedron.Core.Systems
{
    /// <summary>
    /// Two-axis classification result of <see cref="IPowerBudgetSystem.Classify"/>: <paramref name="Tier"/>
    /// (0&#8211;<see cref="PowerBudgetConstants.MaxTier"/>, the mechanical Ascension scalar) crossed with
    /// <paramref name="Band"/> (1&#8211;3, a purely descriptive CR-style subdivision within the tier — grants
    /// no power, gates nothing). Mirrors the <c>AscendEligibility</c> result-record idiom.
    /// </summary>
    public readonly record struct PowerBand(int Tier, int Band);
}
