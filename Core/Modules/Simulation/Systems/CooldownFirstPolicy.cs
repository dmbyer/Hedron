using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Picks the actor's first known ability (in <c>Known</c> order) that is off cooldown,
    /// <see cref="Abilities.Activation.Active"/>, and affordable given current pools; degrades to
    /// melee when no known ability qualifies (including an empty kit).
    /// </summary>
    public sealed class CooldownFirstPolicy : ISimCombatantPolicy
    {
        private readonly IAbilityRegistry _abilityRegistry;

        public CooldownFirstPolicy(IAbilityRegistry abilityRegistry)
        {
            _abilityRegistry = abilityRegistry;
        }

        public string PolicyId => "cooldown-first";

        public SimAction ChooseAction(SandboxWorld world, uint selfId, uint opponentId, int roundIndex)
        {
            foreach (var abilityId in world.Abilities.GetKnown(selfId))
            {
                if (world.Abilities.GetCooldownRemaining(selfId, abilityId) > 0f)
                    continue;

                if (!_abilityRegistry.TryGet(abilityId, out var definition))
                    continue;

                if (definition.Activation != Activation.Active)
                    continue;

                if (!CanAfford(world, selfId, definition))
                    continue;

                return SimAction.Ability(abilityId);
            }

            return SimAction.Melee;
        }

        private static bool CanAfford(SandboxWorld world, uint entityId, AbilityDefinition definition)
        {
            foreach (var cost in definition.Costs)
            {
                var current = cost.Resource switch
                {
                    ResourceType.Hp => world.Attributes.GetCurrentHp(entityId),
                    ResourceType.Mana => world.Attributes.GetCurrentMana(entityId),
                    ResourceType.Stamina => world.Attributes.GetCurrentStamina(entityId),
                    ResourceType.Astra => world.Attributes.GetCurrentAstra(entityId),
                    _ => 0,
                };
                if (current < cost.Amount)
                    return false;
            }
            return true;
        }
    }
}
