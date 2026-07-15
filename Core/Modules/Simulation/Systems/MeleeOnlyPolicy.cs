namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>Always melee — the simplest built-in, and every policy's fallback when a kit is empty.</summary>
    public sealed class MeleeOnlyPolicy : ISimCombatantPolicy
    {
        public string PolicyId => "melee-only";

        public SimAction ChooseAction(SandboxWorld world, uint selfId, uint opponentId, int roundIndex) => SimAction.Melee;
    }
}
