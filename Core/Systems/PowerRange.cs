namespace Hedron.Core.Systems
{
    /// <summary>
    /// Inclusive-lower/exclusive-upper power window a <see cref="PowerBand"/> cell targets, as
    /// returned by <see cref="IPowerBudgetSystem.TargetRange"/>. Band 3's <see cref="MaxPower"/>
    /// abuts the next tier's band-1 <see cref="MinPower"/> (partition, not overlap).
    /// </summary>
    public readonly record struct PowerRange(int MinPower, int MaxPower);
}
