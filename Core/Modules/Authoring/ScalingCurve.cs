namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// How a generated mob's combat stats scale with its level. Selected per
    /// <see cref="GenerationProfile.Scaling"/>; <see cref="ScalingCurveExtensions.HpForLevel"/>
    /// and siblings translate a curve + level into concrete stat numbers, so that a fixed-seed
    /// run produces a reproducible difficulty gradient across a generated world's level range.
    /// </summary>
    public enum ScalingCurve
    {
        /// <summary>Stats grow as a straight line with level (gentle, predictable test gradient).</summary>
        Linear,

        /// <summary>Stats grow faster at higher levels (stress-tests high-level balance).</summary>
        Quadratic,
    }
}
