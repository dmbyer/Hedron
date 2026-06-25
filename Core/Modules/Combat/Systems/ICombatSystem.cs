using Hedron.Core.Modules.Aspects;

namespace Hedron.Core.Modules.Combat.Systems
{
    /// <summary>
    /// Domain system for combat resolution. Pure: no events, no persistence (INV-5, INV-8).
    /// Computes attack resolution via <c>IStatSystem</c>; applies aspect resolution via
    /// <c>IAspectSystem</c>; mutates HP via <c>IAttributeSystem.SetCurrentHp</c>.
    /// Returns structured <see cref="CombatRoundResult"/> to callers.
    /// Result types live in the parent <c>Combat</c> namespace so events and handlers
    /// can reference them without importing <c>Systems</c>.
    /// </summary>
    public interface ICombatSystem
    {
        /// <summary>
        /// Returns <c>true</c> iff <paramref name="targetEntityId"/> can be attacked —
        /// i.e., it does NOT carry <see cref="Hedron.Core.ECS.Components.ProtectionComponent"/>
        /// with the <see cref="Hedron.Core.ECS.Components.ProtectionFlags.Untargetable"/> flag set.
        /// Returns <c>true</c> when no <c>ProtectionComponent</c> is present (unprotected entities are attackable).
        /// PURE: no events, no state mutation (INV-5, INV-8). Shared query used by ≥2 initiators (INV-19).
        /// </summary>
        bool CanBeAttacked(uint targetEntityId);

        bool TryFindTargetInRoom(uint roomEntityId, string token, out uint mobEntityId);
        void StartCombat(uint attackerEntityId, uint defenderEntityId);
        void EndCombat(uint attackerEntityId, uint defenderEntityId);

        /// <summary>
        /// Executes one round of melee combat. Composition source: the attacker's entity
        /// affinity (<c>IAspectSystem.Affinity</c>), empty if untyped.
        /// </summary>
        CombatRoundResult ExecuteRound(uint attackerEntityId, uint defenderEntityId);

        /// <summary>
        /// Resolves an ability-powered strike that always hits (no hit/miss roll).
        /// Damage is defense-mitigated from <paramref name="basePower"/> and aspect-resolved
        /// via <paramref name="composition"/> before being applied to the defender.
        /// Returns a <see cref="CombatRoundResult"/> with
        /// <see cref="CombatRoundResult.AttackerHit"/> always <c>true</c>.
        /// PURE: no events, no side effects (INV-5, INV-8). Callers publish events.
        /// </summary>
        CombatRoundResult ResolveAbilityStrike(
            uint attackerEntityId,
            uint defenderEntityId,
            int basePower,
            AspectComposition? composition = null);
    }
}
