using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Attributes.Systems
{
    public sealed class AttributeSystem : IAttributeSystem
    {
        private readonly EntityService _entityService;

        public AttributeSystem(EntityService entityService)
        {
            _entityService = entityService;
        }

        public int GetLevel(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Level : 1;

        public int GetStrength(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Strength : 10;

        public int GetDexterity(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Dexterity : 10;

        public int GetConstitution(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Constitution : 10;

        public int GetMaxHp(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.MaxHp : 100;

        public int GetCurrentHp(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.CurrentHp : 100;

        public void SetLevel(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Level = value;
        }

        public void SetStrength(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Strength = value;
        }

        public void SetDexterity(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Dexterity = value;
        }

        public void SetConstitution(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Constitution = value;
        }

        public void SetMaxHp(uint entityId, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            p.MaxHp = value;
            if (p.CurrentHp > p.MaxHp)
                p.CurrentHp = p.MaxHp;
        }

        public void SetCurrentHp(uint entityId, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            var max = GetMaxHp(entityId);
            p.CurrentHp = Math.Clamp(value, 0, max);
        }
    }
}
