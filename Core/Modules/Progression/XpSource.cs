namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// The stable key for an experience award's origin, and the key of the advancement table
    /// (<see cref="ProgressionConstants.Rules"/>). A source with no rule row is inert — the
    /// vocabulary declares sources ahead of their wiring so the award signature stays stable.
    /// </summary>
    public enum XpSource
    {
        /// <summary>A kill the earner is attributable for. Chance-free and anti-grind scaled.</summary>
        CombatKill,

        /// <summary>The earner successfully activated a known ability (<c>AbilityActivatedEvent</c>).</summary>
        AbilityUse,

        /// <summary>The earner absorbed damage as the defender of a melee round or ability strike.</summary>
        DamageTaken,

        Book,
        Trainer,
        Objective,
    }
}
