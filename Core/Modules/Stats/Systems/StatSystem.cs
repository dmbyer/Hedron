using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Effects.Systems;

namespace Hedron.Core.Modules.Stats.Systems
{
    public sealed class StatSystem : IStatSystem
    {
        private readonly IAttributeSystem _attributes;
        private readonly IEffectSystem _effectSystem;

        public StatSystem(IAttributeSystem attributes, IEffectSystem effectSystem)
        {
            _attributes = attributes;
            _effectSystem = effectSystem;
        }

        public int GetEffectiveMind(uint entityId) => Get(entityId, ScoreId.Mind);
        public int GetEffectiveBody(uint entityId) => Get(entityId, ScoreId.Body);
        public int GetEffectiveSpirit(uint entityId) => Get(entityId, ScoreId.Spirit);
        public int GetEffectiveAttunement(uint entityId) => Get(entityId, ScoreId.Attunement);
        public int GetEffectiveAttackPower(uint entityId) => Get(entityId, ScoreId.AttackPower);
        public int GetEffectiveDefense(uint entityId) => Get(entityId, ScoreId.Defense);
        public int GetCurrentHp(uint entityId) => Get(entityId, ScoreId.HpCurrent);
        public int GetMaxHp(uint entityId) => Get(entityId, ScoreId.HpMax);

        public int Get(uint entityId, ScoreId score) => score switch
        {
            ScoreId.Mind            => _attributes.GetMind(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.Body            => _attributes.GetBody(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.Spirit          => _attributes.GetSpirit(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.Attunement      => _attributes.GetAttunement(entityId) + _effectSystem.GetModifiers(entityId, score),
            ScoreId.HpMax           => _attributes.GetEffectiveMax(entityId, ResourceType.Hp),
            ScoreId.HpCurrent       => _attributes.GetCurrent(entityId, ResourceType.Hp),
            ScoreId.ManaMax         => _attributes.GetEffectiveMax(entityId, ResourceType.Mana),
            ScoreId.ManaCurrent     => _attributes.GetCurrent(entityId, ResourceType.Mana),
            ScoreId.StaminaMax      => _attributes.GetEffectiveMax(entityId, ResourceType.Stamina),
            ScoreId.StaminaCurrent  => _attributes.GetCurrent(entityId, ResourceType.Stamina),
            ScoreId.AstraMax        => _attributes.GetEffectiveMax(entityId, ResourceType.Astra),
            ScoreId.AstraCurrent    => _attributes.GetCurrent(entityId, ResourceType.Astra),
            ScoreId.AttackPower     => DerivedFromBody(entityId, score, divisor: 2),
            ScoreId.Defense         => DerivedFromBody(entityId, score, divisor: 4),
            _                       => 0,
        };

        // The derived scores' base comes from *effective* Body, so progression, passives, buffs and
        // tier reach combat the same way worn gear already did. Floored at 0: a negative attack power
        // would break the damage roll's range. No cycle — no contributor's backing system reads IStatSystem.
        private int DerivedFromBody(uint entityId, ScoreId score, int divisor)
            => System.Math.Max(0, Get(entityId, ScoreId.Body) / divisor + _effectSystem.GetModifiers(entityId, score));
    }
}
