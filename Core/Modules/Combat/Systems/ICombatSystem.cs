namespace Hedron.Core.Modules.Combat.Systems
{
    /// <summary>
    /// Domain system for combat resolution. Pure: no events, no persistence (INV-5, INV-8).
    /// Computes attack resolution via <c>IStatSystem</c> and mutates HP via
    /// <c>IAttributeSystem.SetCurrentHp</c>. Returns structured <see cref="CombatRoundResult"/>
    /// to callers. Result types live in the parent <c>Combat</c> namespace so events and handlers
    /// can reference them without importing <c>Systems</c>.
    /// </summary>
    public interface ICombatSystem
    {
        bool TryFindTargetInRoom(uint roomEntityId, string token, out uint mobEntityId);
        void StartCombat(uint attackerEntityId, uint defenderEntityId);
        void EndCombat(uint attackerEntityId, uint defenderEntityId);
        CombatRoundResult ExecuteRound(uint attackerEntityId, uint defenderEntityId);

        /// <summary>
        /// Resolves an ability-powered strike that always hits (no hit/miss roll).
        /// Damage is defense-mitigated from <paramref name="basePower"/> and applied to the
        /// defender. Returns a <see cref="CombatRoundResult"/> with
        /// <see cref="CombatRoundResult.AttackerHit"/> always <c>true</c>.
        /// PURE: no events, no side effects (INV-5, INV-8). Callers publish events.
        /// </summary>
        CombatRoundResult ResolveAbilityStrike(uint attackerEntityId, uint defenderEntityId, int basePower);
    }
}
