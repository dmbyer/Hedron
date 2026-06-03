using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Attributes.Systems
{
    public sealed class AttributeSystem : IAttributeSystem
    {
        private readonly EntityService _entityService;
        private readonly IEffectSystem _effectSystem;
        private readonly int _hpFloor;

        // NOTE: AttributeSystem reads the HP-floor clamp from DeathOptions because the death floor
        // is the only value that changes the lower clamp on SetCurrentHp. This is a cross-module
        // dependency (Attributes → Death) within the same project. The backlog item
        // "IOptions<T> sweep — typed config options across Core" tracks decoupling this, either by
        // moving HpFloor to AttributeOptions or by eliminating the clamp from AttributeSystem
        // entirely (letting callers own the floor).
        //
        // IEffectSystem is injected so that SetCurrentX pool setters clamp to the *effective* max
        // (base pool max + active stat modifiers) rather than just the stored base max. This ensures
        // that passive modifiers like toughness (+HpMax) are respected when healing or spending pools.
        // AttributeSystem (domain) → IEffectSystem (core-tier) is a legal downward dependency.
        public AttributeSystem(EntityService entityService, IEffectSystem effectSystem, IOptions<DeathOptions> deathOptions)
        {
            _entityService = entityService;
            _effectSystem = effectSystem;
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
            var effectiveMax = GetMaxHp(entityId) + _effectSystem.GetModifiers(entityId, ScoreId.HpMax);
            p.CurrentHp = Math.Clamp(value, _hpFloor, effectiveMax);
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
            var effectiveMax = GetMaxMana(entityId) + _effectSystem.GetModifiers(entityId, ScoreId.ManaMax);
            p.CurrentMana = Math.Clamp(value, 0, effectiveMax);
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
            var effectiveMax = GetMaxStamina(entityId) + _effectSystem.GetModifiers(entityId, ScoreId.StaminaMax);
            p.CurrentStamina = Math.Clamp(value, 0, effectiveMax);
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
            var effectiveMax = GetMaxAstra(entityId) + _effectSystem.GetModifiers(entityId, ScoreId.AstraMax);
            p.CurrentAstra = Math.Clamp(value, 0, effectiveMax);
        }
    }
}
