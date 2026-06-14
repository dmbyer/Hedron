using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Items
{
    /// <summary>
    /// Folds worn-equipment stat bonuses into the effect modifier pipeline (the INV-24 contributor
    /// seam). Each authored <see cref="EquipmentStatBonus"/> on a worn item is a <c>WhileEquipped</c>
    /// <c>StatModifier</c> derived on read from <see cref="EquipmentComponent"/> +
    /// <see cref="ItemDataComponent"/> — never stored. Registered as an <see cref="IEffectContributor"/>
    /// alongside <c>AbilityEffectContributor</c>; <c>EffectSystem.GetModifiers</c> sums it, and
    /// <c>IStatSystem.Get</c> folds that on top of base + equipment for every consumer.
    /// </summary>
    public sealed class EquipmentEffectContributor : IEffectContributor
    {
        private readonly EntityService _entityService;

        public EquipmentEffectContributor(EntityService entityService)
        {
            _entityService = entityService;
        }

        public int GetModifiers(uint entityId, ScoreId scoreId)
        {
            if (!_entityService.TryGet<EquipmentComponent>(entityId, out var equipment))
                return 0;

            var total = 0;
            foreach (var itemEntityId in DistinctWornItems(equipment))
            {
                if (!_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                    continue;
                foreach (var bonus in item.StatBonuses)
                    if (bonus.TargetScore == scoreId)
                        total += bonus.Magnitude;
            }
            return total;
        }

        public IEnumerable<Effect> GetActive(uint entityId)
        {
            if (!_entityService.TryGet<EquipmentComponent>(entityId, out var equipment))
                yield break;

            foreach (var itemEntityId in DistinctWornItems(equipment))
            {
                if (!_entityService.TryGet<ItemDataComponent>(itemEntityId, out var item))
                    continue;
                foreach (var bonus in item.StatBonuses)
                {
                    yield return new Effect(
                        $"equip.{bonus.TargetScore}", EffectKind.StatModifier,
                        new EffectParams(bonus.TargetScore, bonus.Magnitude),
                        EffectCategory.Buff, bonus.Magnitude,
                        new EffectSource(itemEntityId, item.Name),
                        null, EffectLifetime.WhileEquipped,
                        0f, 0f,
                        StackPolicy.Stack, EffectPhase.Normal);
                }
            }
        }

        // One item can occupy several slots (a two-hand weapon fills MainHand + OffHand), so it
        // appears under multiple keys pointing at the same entity id. Dedupe so its bonuses are
        // counted once, not once per slot.
        private static IEnumerable<uint> DistinctWornItems(EquipmentComponent equipment)
        {
            var seen = new HashSet<uint>();
            foreach (var itemEntityId in equipment.Slots.Values)
                if (seen.Add(itemEntityId))
                    yield return itemEntityId;
        }
    }
}
