using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;

namespace Hedron.Core.Modules.Stats.Systems
{
    public sealed class StatSystem : IStatSystem
    {
        private readonly IAttributeSystem _attributes;
        private readonly EntityService _entityService;

        public StatSystem(IAttributeSystem attributes, EntityService entityService)
        {
            _attributes = attributes;
            _entityService = entityService;
        }

        public int GetEffectiveStrength(uint entityId) => _attributes.GetStrength(entityId);

        public int GetEffectiveDexterity(uint entityId) => _attributes.GetDexterity(entityId);

        public int GetEffectiveConstitution(uint entityId) => _attributes.GetConstitution(entityId);

        public int GetEffectiveAttackPower(uint entityId)
        {
            var strength = _attributes.GetStrength(entityId);
            var bonus = 0;

            if (_entityService.TryGet<EquipmentComponent>(entityId, out var equipment) &&
                equipment.Slots.TryGetValue(WornSlot.MainHand, out var mainHandItemId) &&
                _entityService.TryGet<ItemDataComponent>(mainHandItemId, out var itemData))
            {
                bonus = itemData.DamageBonus;
            }

            return strength / 2 + bonus;
        }

        public int GetEffectiveDefense(uint entityId) => _attributes.GetDexterity(entityId) / 4;

        public int GetCurrentHp(uint entityId) => _attributes.GetCurrentHp(entityId);

        public int GetMaxHp(uint entityId) => _attributes.GetMaxHp(entityId);
    }
}
