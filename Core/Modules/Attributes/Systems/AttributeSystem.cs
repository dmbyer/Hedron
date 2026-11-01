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
        // IEffectSystem is injected because this system owns each pool's *effective* cap (base pool
        // max + active <Pool>Max modifiers, effects and contributors alike). Reads of current are
        // clamped to it and writes clamp to it, so passive/progression/tier/gear max bonuses are
        // respected everywhere. AttributeSystem (domain) -> IEffectSystem (core-tier) is a legal
        // downward dependency; it must never depend on IStatSystem, which delegates its pool reads here.
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

        public int GetBaseMaxHp(uint entityId) => GetBaseMax(entityId, ResourceType.Hp);
        public int GetCurrentHp(uint entityId) => GetCurrent(entityId, ResourceType.Hp);
        public int GetBaseMaxMana(uint entityId) => GetBaseMax(entityId, ResourceType.Mana);
        public int GetCurrentMana(uint entityId) => GetCurrent(entityId, ResourceType.Mana);
        public int GetBaseMaxStamina(uint entityId) => GetBaseMax(entityId, ResourceType.Stamina);
        public int GetCurrentStamina(uint entityId) => GetCurrent(entityId, ResourceType.Stamina);
        public int GetBaseMaxAstra(uint entityId) => GetBaseMax(entityId, ResourceType.Astra);
        public int GetCurrentAstra(uint entityId) => GetCurrent(entityId, ResourceType.Astra);

        public int GetEffectiveMax(uint entityId, ResourceType pool)
        {
            var baseMax = GetBaseMax(entityId, pool);
            var effective = baseMax + _effectSystem.GetModifiers(entityId, MaxScore(pool));
            return Math.Max(baseMax > 0 ? 1 : 0, effective);
        }

        public int GetCurrent(uint entityId, ResourceType pool)
            => Math.Min(GetStoredCurrent(entityId, pool), GetEffectiveMax(entityId, pool));

        public void SetCurrent(uint entityId, ResourceType pool, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            var floor = pool == ResourceType.Hp ? _hpFloor : 0;
            var cap = GetEffectiveMax(entityId, pool);
            WriteCurrent(p, pool, Math.Clamp(value, Math.Min(floor, cap), cap));
        }

        public void SetMax(uint entityId, ResourceType pool, int value)
        {
            if (!_entityService.TryGet<PoolsComponent>(entityId, out var p))
                return;
            WriteMax(p, pool, value);
            var cap = GetEffectiveMax(entityId, pool);
            if (ReadCurrent(p, pool) > cap)
                WriteCurrent(p, pool, cap);
        }

        public void RestoreToFraction(uint entityId, ResourceType pool, double fraction)
            => SetCurrent(entityId, pool, (int)Math.Floor(GetEffectiveMax(entityId, pool) * fraction));

        public bool ReconcilePools(uint entityId)
            => _entityService.TryGet<PoolsComponent>(entityId, out var p) && Reconcile(entityId, p) > 0;

        public int ReconcileAllPools()
        {
            var corrected = 0;
            foreach (var (entityId, p) in _entityService.GetAllComponents<PoolsComponent>())
                corrected += Reconcile(entityId, p);
            return corrected;
        }

        private int Reconcile(uint entityId, PoolsComponent p)
        {
            var corrected = 0;
            foreach (var pool in AllPools)
            {
                var cap = GetEffectiveMax(entityId, pool);
                if (ReadCurrent(p, pool) <= cap)
                    continue;
                WriteCurrent(p, pool, cap);
                corrected++;
            }
            return corrected;
        }

        private static readonly ResourceType[] AllPools =
            { ResourceType.Hp, ResourceType.Mana, ResourceType.Stamina, ResourceType.Astra };

        private static ScoreId MaxScore(ResourceType pool) => pool switch
        {
            ResourceType.Hp      => ScoreId.HpMax,
            ResourceType.Mana    => ScoreId.ManaMax,
            ResourceType.Stamina => ScoreId.StaminaMax,
            ResourceType.Astra   => ScoreId.AstraMax,
            _ => throw new ArgumentOutOfRangeException(nameof(pool), pool, null),
        };

        // Defaults mirror PoolsComponent's initializers for entities with no component.
        private static readonly PoolsComponent Defaults = new();

        private int GetBaseMax(uint entityId, ResourceType pool)
            => ReadMax(_entityService.TryGet<PoolsComponent>(entityId, out var p) ? p : Defaults, pool);

        private int GetStoredCurrent(uint entityId, ResourceType pool)
            => ReadCurrent(_entityService.TryGet<PoolsComponent>(entityId, out var p) ? p : Defaults, pool);

        private static int ReadMax(PoolsComponent p, ResourceType pool) => pool switch
        {
            ResourceType.Hp      => p.MaxHp,
            ResourceType.Mana    => p.MaxMana,
            ResourceType.Stamina => p.MaxStamina,
            ResourceType.Astra   => p.MaxAstra,
            _ => throw new ArgumentOutOfRangeException(nameof(pool), pool, null),
        };

        private static int ReadCurrent(PoolsComponent p, ResourceType pool) => pool switch
        {
            ResourceType.Hp      => p.CurrentHp,
            ResourceType.Mana    => p.CurrentMana,
            ResourceType.Stamina => p.CurrentStamina,
            ResourceType.Astra   => p.CurrentAstra,
            _ => throw new ArgumentOutOfRangeException(nameof(pool), pool, null),
        };

        private static void WriteMax(PoolsComponent p, ResourceType pool, int value)
        {
            switch (pool)
            {
                case ResourceType.Hp:      p.MaxHp = value; break;
                case ResourceType.Mana:    p.MaxMana = value; break;
                case ResourceType.Stamina: p.MaxStamina = value; break;
                case ResourceType.Astra:   p.MaxAstra = value; break;
            }
        }

        private static void WriteCurrent(PoolsComponent p, ResourceType pool, int value)
        {
            switch (pool)
            {
                case ResourceType.Hp:      p.CurrentHp = value; break;
                case ResourceType.Mana:    p.CurrentMana = value; break;
                case ResourceType.Stamina: p.CurrentStamina = value; break;
                case ResourceType.Astra:   p.CurrentAstra = value; break;
            }
        }

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

        public void SetMaxHp(uint entityId, int value) => SetMax(entityId, ResourceType.Hp, value);
        public void SetCurrentHp(uint entityId, int value) => SetCurrent(entityId, ResourceType.Hp, value);
        public void SetMaxMana(uint entityId, int value) => SetMax(entityId, ResourceType.Mana, value);
        public void SetCurrentMana(uint entityId, int value) => SetCurrent(entityId, ResourceType.Mana, value);
        public void SetMaxStamina(uint entityId, int value) => SetMax(entityId, ResourceType.Stamina, value);
        public void SetCurrentStamina(uint entityId, int value) => SetCurrent(entityId, ResourceType.Stamina, value);
        public void SetMaxAstra(uint entityId, int value) => SetMax(entityId, ResourceType.Astra, value);
        public void SetCurrentAstra(uint entityId, int value) => SetCurrent(entityId, ResourceType.Astra, value);
    }
}
