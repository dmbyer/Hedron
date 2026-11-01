namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// Coarse danger label for a target relative to a subject, weakest to strongest. Produced by
    /// <see cref="Systems.IRelativePowerSystem.Classify"/> from the effective-power ratio; the
    /// cut-points live in <see cref="ProgressionConstants"/> (<c>Trivial</c>'s upper bound is
    /// <see cref="ProgressionConstants.AntiGrindFloorRatio"/>). Presentation (phrases, casing) is
    /// the consumer's concern, never encoded here.
    /// </summary>
    public enum RelativePowerLabel
    {
        Trivial,
        Easy,
        Even,
        Dangerous,
        Deadly,
    }
}
