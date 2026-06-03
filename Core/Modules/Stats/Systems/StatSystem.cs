using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Effects.Systems;

namespace Hedron.Core.Modules.Stats.Systems
{
    public sealed class StatSystem : IStatSystem
    {
        private readonly IAttributeSystem _attributes;
        private readonly EntityService _entityService;
        private readonly IEffectSystem _effectSystem;

        public StatSystem(IAttributeSystem attributes, EntityService entityService, IEffectSystem effectSystem)
        {
            _attributes = attributes;
            _entityService = entityService;
            _effectSystem = effectSystem;
        }

        public int GetEffectiveMind(uint entityId) => _attributes.GetMind(entityId);
        public int GetEffectiveBody(uint entityId) => _attributes.GetBody(entityId);
        public int GetEffectiveSpirit(uint entityId) => _attributes.GetSpirit(entityId);
        public int GetEffectiveAttunement(uint entityId) => _attributes.GetAttunement(entityId);

        public int GetEffectiveAttackPower(uint entityId)
        {
            var body = _attributes.GetBody(entityId);
            var bonus = 0;

            if (_entityService.TryGet<EquipmentComponent>(entityId, out var equipment) &&
                equipment.Slots.TryGetValue(WornSlot.MainHand, out var mainHandItemId) &&
                _entityService.TryGet<ItemDataComponent>(mainHandItemId, out var itemData))
            {
                bonus = itemData.DamageBonus;
            }

            return body / 2 + bonus;
        }

        public int GetEffectiveDefense(uint entityId) => _attributes.GetBody(entityId) / 4;

        public int GetCurrentHp(uint entityId) => _attributes.GetCurrentHp(entityId);
        public int GetMaxHp(uint entityId) => _attributes.GetMaxHp(entityId);

        public int Get(uint entityId, ScoreId score) => score switch
        {
            ScoreId.Mind            => GetEffectiveMind(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.Body            => GetEffectiveBody(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.Spirit          => GetEffectiveSpirit(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.Attunement      => GetEffectiveAttunement(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.HpMax           => _attributes.GetMaxHp(entityId) + _effectSystem.GetModifiers(entityId, ScoreId.HpMax),
            ScoreId.HpCurrent       => _attributes.GetCurrentHp(entityId),
            ScoreId.ManaMax         => _attributes.GetMaxMana(entityId) + _effectSystem.GetModifiers(entityId, ScoreId.ManaMax),
            ScoreId.ManaCurrent     => _attributes.GetCurrentMana(entityId),
            ScoreId.StaminaMax      => _attributes.GetMaxStamina(entityId) + _effectSystem.GetModifiers(entityId, ScoreId.StaminaMax),
            ScoreId.StaminaCurrent  => _attributes.GetCurrentStamina(entityId),
            ScoreId.AstraMax        => _attributes.GetMaxAstra(entityId) + _effectSystem.GetModifiers(entityId, ScoreId.AstraMax),
            ScoreId.AstraCurrent    => _attributes.GetCurrentAstra(entityId),
            ScoreId.AttackPower     => GetEffectiveAttackPower(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.Defense         => GetEffectiveDefense(entityId) + _effectSystem.GetModifiers(entityId, score),
            _                       => 0,
        };
    }
}
