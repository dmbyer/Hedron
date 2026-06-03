using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Abilities
{
    public sealed class AbilityEffectContributor : IEffectContributor
    {
        private readonly EntityService _entityService;
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEffectRegistry _effectRegistry;

        public AbilityEffectContributor(EntityService entityService, IAbilityRegistry abilityRegistry, IEffectRegistry effectRegistry)
        {
            _entityService = entityService;
            _abilityRegistry = abilityRegistry;
            _effectRegistry = effectRegistry;
        }

        public int GetModifiers(uint entityId, ScoreId scoreId)
        {
            if (!_entityService.TryGet<AbilitiesComponent>(entityId, out var comp))
                return 0;
            var total = 0;
            foreach (var abilityId in comp.Known)
            {
                if (!_abilityRegistry.TryGet(abilityId, out var ability) || ability.Activation != Activation.Passive)
                    continue;
                foreach (var effectId in ability.Effects)
                {
                    if (!_effectRegistry.TryGet(effectId, out var def))
                        continue;
                    if (def.Kind == EffectKind.StatModifier && def.Params.TargetScore == scoreId)
                        total += PowerScaling.Evaluate(def.PowerScalingFormula, def, _entityService, entityId);
                }
            }
            return total;
        }

        public IEnumerable<Effect> GetActive(uint entityId)
        {
            if (!_entityService.TryGet<AbilitiesComponent>(entityId, out var comp))
                yield break;
            foreach (var abilityId in comp.Known)
            {
                if (!_abilityRegistry.TryGet(abilityId, out var ability) || ability.Activation != Activation.Passive)
                    continue;
                foreach (var effectId in ability.Effects)
                {
                    if (!_effectRegistry.TryGet(effectId, out var def))
                        continue;
                    var power = PowerScaling.Evaluate(def.PowerScalingFormula, def, _entityService, entityId);
                    yield return new Effect(
                        effectId, def.Kind, def.Params, def.Category, power,
                        new EffectSource(entityId),
                        null, EffectLifetime.WhileKnown,
                        0f, 0f,
                        def.Stacking, def.Phase);
                }
            }
        }
    }
}
