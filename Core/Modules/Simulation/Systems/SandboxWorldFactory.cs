using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Abilities.Systems;
using Hedron.Core.Modules.Ascension;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Regeneration.Systems;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Hand-news-up per world (resolved decision — seed OQ3): no per-run scoped DI container, the
    /// same composition <c>CombatFlowTests.TestWorld</c> proves is per-instance composable. Shares
    /// only immutable, <c>EntityService</c>-free singletons across every world it creates
    /// (<see cref="IAbilityRegistry"/>, <see cref="IEffectRegistry"/>, <see cref="IPowerBudgetSystem"/>,
    /// <see cref="IOptions{DeathOptions}"/>) — safe because none of them hold per-world state.
    /// </summary>
    public sealed class SandboxWorldFactory : ISandboxWorldFactory
    {
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEffectRegistry _effectRegistry;
        private readonly IPowerBudgetSystem _powerBudget;
        private readonly IOptions<DeathOptions> _deathOptions;

        public SandboxWorldFactory(
            IAbilityRegistry abilityRegistry,
            IEffectRegistry effectRegistry,
            IPowerBudgetSystem powerBudget,
            IOptions<DeathOptions> deathOptions)
        {
            _abilityRegistry = abilityRegistry;
            _effectRegistry = effectRegistry;
            _powerBudget = powerBudget;
            _deathOptions = deathOptions;
        }

        public SandboxWorld Create(IRandom random)
        {
            var ecs = new EntityService();

            // No dependency cycle: the contributors below depend only on EntityService and each
            // other's backing system — never on IEffectSystem/IStatSystem — so EffectSystem can be
            // built before AttributeSystem/StatSystem need it (same guard ProgressionSystem's
            // anti-grind proxy observes against IStatSystem).
            var progression = new ProgressionSystem(ecs, random, _powerBudget);
            var ascension = new AscensionSystem(ecs);

            var contributors = new List<IEffectContributor>
            {
                new EquipmentEffectContributor(ecs),
                new AbilityEffectContributor(ecs, _abilityRegistry, _effectRegistry),
                new ProgressionEffectContributor(progression),
                new AscensionEffectContributor(ascension),
            };

            var effects = new EffectSystem(ecs, contributors);
            var attributes = new AttributeSystem(ecs, effects, _deathOptions);
            var stats = new StatSystem(attributes, effects);
            var aspects = new AspectSystem(ecs);
            var combat = new CombatSystem(ecs, stats, attributes, aspects, random);
            var entityState = new EntityStateService(ecs);
            var abilities = new AbilitySystem(ecs, _abilityRegistry, effects, _effectRegistry, attributes, entityState);
            var regeneration = new RegenerationSystem(ecs, entityState, attributes);

            var arenaRoom = ecs.CreateEntity();

            return new SandboxWorld(
                ecs, random, effects, attributes, stats, aspects, combat,
                abilities, entityState, regeneration, progression, ascension,
                arenaRoom.Id);
        }
    }
}
