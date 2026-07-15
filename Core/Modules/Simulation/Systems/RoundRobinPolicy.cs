namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Cycles through the actor's known abilities in <c>Known</c> order, one per round
    /// (<c>roundIndex % known.Count</c>); degrades to melee with an empty kit. Does not check
    /// cooldown/affordability itself — a chosen-but-unusable ability is a no-op activation the
    /// executor treats as a passed action, matching the live activation-failure UX.
    /// </summary>
    public sealed class RoundRobinPolicy : ISimCombatantPolicy
    {
        public string PolicyId => "round-robin";

        public SimAction ChooseAction(SandboxWorld world, uint selfId, uint opponentId, int roundIndex)
        {
            var known = world.Abilities.GetKnown(selfId);
            if (known.Count == 0)
                return SimAction.Melee;

            var abilityId = known[roundIndex % known.Count];
            return SimAction.Ability(abilityId);
        }
    }
}
