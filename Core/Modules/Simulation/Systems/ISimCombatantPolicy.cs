namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// What a combatant does this round: an always-available melee attack, or an ability by id
    /// (which the executor activates via <see cref="Abilities.Systems.IAbilitySystem.Activate"/>).
    /// </summary>
    public abstract record SimAction
    {
        private SimAction() { }

        public sealed record MeleeAttack : SimAction;

        public sealed record UseAbility(string AbilityId) : SimAction;

        public static SimAction Melee { get; } = new MeleeAttack();

        public static SimAction Ability(string abilityId) => new UseAbility(abilityId);
    }

    /// <summary>
    /// "What does this actor do this round" — a pure, stateless decision seam (no mutable instance
    /// state: a policy instance is shared across every concurrently-running sandbox world, so any
    /// per-actor memory must be derived from <paramref name="roundIndex"/> or world state, never
    /// stored on the policy itself). Simple built-ins ship now; a future <c>IAISystem</c> adapter
    /// binds behind this same seam (backlogged, not built).
    /// </summary>
    public interface ISimCombatantPolicy
    {
        /// <summary>The stable id a <see cref="CombatantSpec.PolicyId"/> references.</summary>
        string PolicyId { get; }

        /// <summary>
        /// Chooses <paramref name="selfId"/>'s action against <paramref name="opponentId"/> this
        /// round. <paramref name="roundIndex"/> is the 0-based round counter within the current run
        /// — the deterministic substitute for per-actor memory.
        /// </summary>
        SimAction ChooseAction(SandboxWorld world, uint selfId, uint opponentId, int roundIndex);
    }
}
