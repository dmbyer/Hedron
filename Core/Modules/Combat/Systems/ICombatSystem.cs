namespace Hedron.Core.Modules.Combat.Systems
{
    public readonly record struct CombatRoundResult(
        uint AttackerEntityId,
        uint DefenderEntityId,
        int DamageDealt,
        bool AttackerHit,
        CombatRoundOutcome Outcome);

    public enum CombatRoundOutcome { Hit, Miss, MobDied, PlayerIncapacitated }

    /// <summary>
    /// Domain system for combat resolution. Pure: no events, no persistence (INV-5, INV-8).
    /// Computes attack resolution via <c>IStatSystem</c> and mutates HP via
    /// <c>IAttributeSystem.SetCurrentHp</c>. Returns structured results to callers.
    /// </summary>
    public interface ICombatSystem
    {
        bool TryFindTargetInRoom(uint roomEntityId, string token, out uint mobEntityId);
        void StartCombat(uint attackerEntityId, uint defenderEntityId);
        void EndCombat(uint attackerEntityId, uint defenderEntityId);
        CombatRoundResult ExecuteRound(uint attackerEntityId, uint defenderEntityId);
    }
}
