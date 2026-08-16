namespace Hedron.Core.Modules.Progression
{
    /// <summary>
    /// What an <see cref="AdvancementRule"/> requires of an award attempt, expressed as
    /// <b>data on the rule</b> rather than a branch in the handler. Evaluated inside
    /// <c>ProgressionSystem.AwardUseExperience</c> against the incoming
    /// <see cref="Systems.UseAwardContext"/> — the handler's only job is the mechanical mapping
    /// <i>event fields → context</i> (INV-8: no game rule lives in the orchestration tier).
    /// </summary>
    /// <param name="RequiresAttributableActor">
    /// The earner must be a real entity (id <c>!= 0</c>). A mob dying with no attributable killer,
    /// or an event with no actor, awards nothing.
    /// </param>
    /// <param name="RequiresPlayerEarner">
    /// The earner must carry <c>CharacterComponent</c> — only player characters progress. Left
    /// <see langword="false"/> on the combat-kill row so the balance sandbox (whose combatants are
    /// mob-shaped, see <c>SimCombatantFactory</c>) keeps exercising the same seam it always has.
    /// </param>
    /// <param name="RequiresPositiveMagnitude">
    /// <see cref="Systems.UseAwardContext.Magnitude"/> must be <c>&gt; 0</c> — e.g. a combat round
    /// that dealt no damage grants nothing to the defender.
    /// </param>
    /// <param name="AppliesAntiGrindPowerRatio">
    /// The award is scaled by the victim-vs-earner power ratio. A ratio below
    /// <see cref="ProgressionConstants.AntiGrindFloorRatio"/> is an <b>eligibility failure</b>, not
    /// a zero multiplier: the candidate consumes <b>zero</b> <c>IRandom</c> draws, preserving the
    /// trivial-victim draw contract (INV-26).
    /// </param>
    public readonly record struct AdvancementEligibility(
        bool RequiresAttributableActor = false,
        bool RequiresPlayerEarner = false,
        bool RequiresPositiveMagnitude = false,
        bool AppliesAntiGrindPowerRatio = false);
}
