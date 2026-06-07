using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Abilities
{
    public interface IAbilityRegistry : IRegistry<string, AbilityDefinition> { }

    public sealed class AbilityRegistry : DefinitionRegistry<string, AbilityDefinition>, IAbilityRegistry
    {
        public AbilityRegistry() : base(CreateRows(), d => d.Id) { }

        private static IEnumerable<AbilityDefinition> CreateRows() => new AbilityDefinition[]
        {
            new AbilityDefinition(
                "toughness", "Toughness",
                AbilityKind.Skill, Activation.Passive, Targeting.Self,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "toughness_passive" },
                CooldownSeconds: 0f),

            new AbilityDefinition(
                "kick", "Kick",
                AbilityKind.Skill, Activation.Active, Targeting.Target,
                Costs: new List<ResourceCost> { new ResourceCost(ResourceType.Stamina, 10) },
                Effects: new List<string> { "kick_damage" },
                CooldownSeconds: 6f),

            new AbilityDefinition(
                "empower", "Empower",
                AbilityKind.Spell, Activation.Active, Targeting.Self,
                Costs: new List<ResourceCost> { new ResourceCost(ResourceType.Mana, 10) },
                Effects: new List<string> { "empower" },
                CooldownSeconds: 30f),

            new AbilityDefinition(
                "mend", "Mend",
                AbilityKind.Spell, Activation.Active, Targeting.Self,
                Costs: new List<ResourceCost> { new ResourceCost(ResourceType.Mana, 15) },
                Effects: new List<string> { "mend_heal" },
                CooldownSeconds: 20f),

            new AbilityDefinition(
                "blood_pact", "Blood Pact",
                AbilityKind.Spell, Activation.Active, Targeting.Self,
                Costs: new List<ResourceCost>
                {
                    new ResourceCost(ResourceType.Hp, 10),
                    new ResourceCost(ResourceType.Mana, 15),
                },
                Effects: new List<string> { "empower" },
                CooldownSeconds: 30f),
        };
    }
}
