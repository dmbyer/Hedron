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

        public int GetEffectiveMind(uint entityId) => _attributes.GetMind(entityId);
        public int GetEffectiveBody(uint entityId) => _attributes.GetBody(entityId);
        public int GetEffectiveSpirit(uint entityId) => _attributes.GetSpirit(entityId);
        public int GetEffectiveAttunement(uint entityId) => _attributes.GetAttunement(entityId);

        // Base attack power only. Weapon (and all worn-gear) bonuses now ride the effect
        // contributor folded by Get(ScoreId.AttackPower) — see EquipmentEffectContributor.
        // Callers that need the gear-inclusive value MUST read Get(AttackPower), not this getter.
        public int GetEffectiveAttackPower(uint entityId) => _attributes.GetBody(entityId) / 2;

        public int GetEffectiveDefense(uint entityId) => _attributes.GetBody(entityId) / 4;

        public int GetCurrentHp(uint entityId) => _attributes.GetCurrentHp(entityId);
        public int GetMaxHp(uint entityId) => Get(entityId, ScoreId.HpMax);

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
