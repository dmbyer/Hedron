using System.Collections.Generic;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Abilities
{
    public interface IAbilityRegistry
    {
        bool TryGet(string abilityId, out AbilityDefinition definition);
        IReadOnlyCollection<string> AllIds { get; }
    }

    public sealed class AbilityRegistry : IAbilityRegistry
    {
        private static readonly Dictionary<string, AbilityDefinition> _definitions = new()
        {
            ["toughness"] = new AbilityDefinition(
                "toughness", "Toughness",
                AbilityKind.Skill, Activation.Passive, Targeting.Self,
                Costs: new List<ResourceCost>(),
                Effects: new List<string> { "toughness_passive" },
                CooldownSeconds: 0f),

            ["kick"] = new AbilityDefinition(
                "kick", "Kick",
                AbilityKind.Skill, Activation.Active, Targeting.Target,
                Costs: new List<ResourceCost> { new ResourceCost(ResourceType.Stamina, 10) },
                Effects: new List<string> { "kick_damage" },
                CooldownSeconds: 6f),

            ["empower"] = new AbilityDefinition(
                "empower", "Empower",
                AbilityKind.Spell, Activation.Active, Targeting.Self,
                Costs: new List<ResourceCost> { new ResourceCost(ResourceType.Mana, 10) },
                Effects: new List<string> { "empower" },
                CooldownSeconds: 30f),

            ["mend"] = new AbilityDefinition(
                "mend", "Mend",
                AbilityKind.Spell, Activation.Active, Targeting.Self,
                Costs: new List<ResourceCost> { new ResourceCost(ResourceType.Mana, 15) },
                Effects: new List<string> { "mend_heal" },
                CooldownSeconds: 20f),

            ["blood_pact"] = new AbilityDefinition(
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

        public bool TryGet(string abilityId, out AbilityDefinition definition)
            => _definitions.TryGetValue(abilityId, out definition!);

        public IReadOnlyCollection<string> AllIds => _definitions.Keys;
    }
}
