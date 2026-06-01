using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Death;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Attributes.Systems
{
    public sealed class AttributeSystem : IAttributeSystem
    {
        private readonly EntityService _entityService;
        private readonly int _hpFloor;

        // NOTE: AttributeSystem reads the HP-floor clamp from DeathOptions because the death floor
        // is the only value that changes the lower clamp on SetCurrentHp. This is a cross-module
        // dependency (Attributes → Death) within the same project. The backlog item
        // "IOptions<T> sweep — typed config options across Core" tracks decoupling this, either by
        // moving HpFloor to AttributeOptions or by eliminating the clamp from AttributeSystem
        // entirely (letting callers own the floor).
        public AttributeSystem(EntityService entityService, IOptions<DeathOptions> deathOptions)
        {
            _entityService = entityService;
            _hpFloor = deathOptions.Value.HpFloor;
        }

        public int GetLevel(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Level : 1;

        public int GetMind(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Mind : 10;

        public int GetBody(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Body : 10;

        public int GetSpirit(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Spirit : 10;

        public int GetAttunement(uint entityId)
            => _entityService.TryGet<AttributesComponent>(entityId, out var a) ? a.Attunement : 10;

        public int GetMaxHp(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.MaxHp : 100;

        public int GetCurrentHp(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.CurrentHp : 100;

        public int GetMaxMana(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.MaxMana : 50;

        public int GetCurrentMana(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.CurrentMana : 50;

        public int GetMaxStamina(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.MaxStamina : 50;

        public int GetCurrentStamina(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.CurrentStamina : 50;

        public int GetMaxAstra(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.MaxAstra : 10;

        public int GetCurrentAstra(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) ? p.CurrentAstra : 10;

        public void SetLevel(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Level = value;
        }

        public void SetMind(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Mind = value;
        }

        public void SetBody(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Body = value;
        }

        public void SetSpirit(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Spirit = value;
        }

        public void SetAttunement(uint entityId, int value)
        {
            if (_entityService.TryGet<AttributesComponent>(entityId, out var a))
                a.Attunement = value;
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
            p.CurrentHp = Math.Clamp(value, _hpFloor, GetMaxHp(entityId));
        }

        public void SetMaxMana(uint entityId, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            p.MaxMana = value;
            if (p.CurrentMana > p.MaxMana)
                p.CurrentMana = p.MaxMana;
        }

        public void SetCurrentMana(uint entityId, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            p.CurrentMana = Math.Clamp(value, 0, GetMaxMana(entityId));
        }

        public void SetMaxStamina(uint entityId, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            p.MaxStamina = value;
            if (p.CurrentStamina > p.MaxStamina)
                p.CurrentStamina = p.MaxStamina;
        }

        public void SetCurrentStamina(uint entityId, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            p.CurrentStamina = Math.Clamp(value, 0, GetMaxStamina(entityId));
        }

        public void SetMaxAstra(uint entityId, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            p.MaxAstra = value;
            if (p.CurrentAstra > p.MaxAstra)
                p.CurrentAstra = p.MaxAstra;
        }

        public void SetCurrentAstra(uint entityId, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            p.CurrentAstra = Math.Clamp(value, 0, GetMaxAstra(entityId));
        }
    }
}
