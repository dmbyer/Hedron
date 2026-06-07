using Hedron.Core.Modules.Aspects;

namespace Hedron.Core.Modules.Combat
{
    /// <summary>
    /// Outcome of a single combat round returned by <see cref="Systems.ICombatSystem.ExecuteRound"/>.
    /// Defined at the <c>Combat</c> namespace level so event and handler types can reference it
    /// without importing the <c>Systems</c> namespace.
    /// <para>
    /// <see cref="AspectComposition"/> is a point-in-time capture of the damage typing that
    /// produced <see cref="DamageDealt"/> — empty when the strike was untyped (INV-6).
    /// </para>
    /// </summary>
    public readonly record struct CombatRoundResult(
        uint AttackerEntityId,
        uint DefenderEntityId,
        int DamageDealt,
        bool AttackerHit,
        CombatRoundOutcome Outcome,
        AspectComposition? AspectComposition = null);

    public enum CombatRoundOutcome { Hit, Miss, MobDied, PlayerIncapacitated }
}
