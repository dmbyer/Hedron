namespace Hedron.Core.Modules.Combat
{
    /// <summary>
    /// Outcome of a single combat round returned by <see cref="Systems.ICombatSystem.ExecuteRound"/>.
    /// Defined at the <c>Combat</c> namespace level so event and handler types can reference it
    /// without importing the <c>Systems</c> namespace.
    /// </summary>
    public readonly record struct CombatRoundResult(
        uint AttackerEntityId,
        uint DefenderEntityId,
        int DamageDealt,
        bool AttackerHit,
        CombatRoundOutcome Outcome);

    public enum CombatRoundOutcome { Hit, Miss, MobDied, PlayerIncapacitated }
}
