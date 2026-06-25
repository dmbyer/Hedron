using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Abilities.Systems
{
    public sealed class AbilitySystem : IAbilitySystem
    {
        private readonly EntityService _entityService;
        private readonly IAbilityRegistry _abilityRegistry;
        private readonly IEffectSystem _effectSystem;
        private readonly IEffectRegistry _effectRegistry;
        private readonly IAttributeSystem _attributeSystem;
        private readonly IEntityStateService _entityStateService;

        public AbilitySystem(
            EntityService entityService,
            IAbilityRegistry abilityRegistry,
            IEffectSystem effectSystem,
            IEffectRegistry effectRegistry,
            IAttributeSystem attributeSystem,
            IEntityStateService entityStateService)
        {
            _entityService = entityService;
            _abilityRegistry = abilityRegistry;
            _effectSystem = effectSystem;
            _effectRegistry = effectRegistry;
            _attributeSystem = attributeSystem;
            _entityStateService = entityStateService;
        }

        public bool IsOffensive(string abilityId)
        {
            if (!_abilityRegistry.TryGet(abilityId, out var definition))
                return false;

            if (definition.Targeting != Targeting.Target)
                return false;

            foreach (var effectId in definition.Effects)
            {
                if (_effectRegistry.TryGet(effectId, out var effectDef) &&
                    IsOffensiveDamageEffect(effectDef))
                    return true;
            }

            return false;
        }

        public AbilityActivationResult Activate(uint actorEntityId, string abilityId, uint? targetEntityId = null, bool resolveOffensiveExternally = false)
        {
            // 1. Resolve definition
            if (!_abilityRegistry.TryGet(abilityId, out var definition))
                return Fail(AbilityActivationOutcome.UnknownAbility, abilityId);

            // 2. Confirm known
            if (!IsKnown(actorEntityId, abilityId))
                return Fail(AbilityActivationOutcome.NotKnown, abilityId);

            // 3. Must be Active
            if (definition.Activation != Activation.Active)
                return Fail(AbilityActivationOutcome.NotActivatable, abilityId);

            // 4. State check
            if (_entityStateService.IsInState(actorEntityId, EntityStateFlags.Incapacitated))
                return Fail(AbilityActivationOutcome.StateBlocked, abilityId, "incapacitated");

            // 5. Cooldown check
            var cooldownRemaining = GetCooldownRemaining(actorEntityId, abilityId);
            if (cooldownRemaining > 0f)
                return Fail(AbilityActivationOutcome.OnCooldown, abilityId, cooldownRemaining.ToString("F1") + "s");

            // 6. Resource check (atomic — spend nothing if any fails)
            foreach (var cost in definition.Costs)
            {
                var current = GetCurrentPool(actorEntityId, cost.Resource);
                if (current < cost.Amount)
                    return Fail(AbilityActivationOutcome.InsufficientResources, abilityId, cost.Resource.ToString());
            }

            // 7. All checks passed — commit
            var spent = new List<ResourceCost>();
            foreach (var cost in definition.Costs)
            {
                SpendPool(actorEntityId, cost.Resource, cost.Amount);
                spent.Add(cost);
            }

            if (definition.CooldownSeconds > 0f)
            {
                var comp = EnsureComponent(actorEntityId);
                comp.CooldownRemaining[abilityId] = definition.CooldownSeconds;
            }

            var resolvedTarget = targetEntityId ?? actorEntityId;

            var appliedEffects = new List<Effect>();
            int? offensivePower = null;

            foreach (var effectId in definition.Effects)
            {
                if (!_effectRegistry.TryGet(effectId, out var effectDef))
                    continue;

                // When resolveOffensiveExternally is set, skip offensive damage effects and
                // capture their raw magnitude so the caller (e.g. a combat command) can feed
                // it into ResolveAbilityStrike for defense-mitigated resolution.
                if (resolveOffensiveExternally && IsOffensiveDamageEffect(effectDef))
                {
                    offensivePower = Math.Abs(effectDef.Params.BaseMagnitude);
                    continue;
                }

                var applyResult = _effectSystem.Apply(resolvedTarget, effectDef, actorEntityId);
                if (applyResult is Hedron.Core.Modules.Effects.EffectApplyResult.Applied applied)
                {
                    if (applied.Effect.Kind == EffectKind.Instant)
                    {
                        ApplyInstantMagnitude(resolvedTarget, applied.Effect.Params.TargetScore, applied.Effect.Power);
                    }
                    appliedEffects.Add(applied.Effect);
                }
                // Immune or stacking-blocked: exclude from AppliedEffects.
                // The initiator (AbilityInvocationPipeline) emits no EffectAppliedEvent for excluded effects (INV-5).
            }

            return new AbilityActivationResult(
                AbilityActivationOutcome.Activated,
                abilityId,
                appliedEffects,
                spent,
                definition.CooldownSeconds,
                OffensivePower: offensivePower);
        }

        /// <summary>
        /// Returns true if <paramref name="effectDef"/> is an offensive damage effect:
        /// Instant or Periodic kind, targeting HpCurrent, with a negative BaseMagnitude.
        /// </summary>
        private static bool IsOffensiveDamageEffect(EffectDefinition effectDef) =>
            (effectDef.Kind == EffectKind.Instant || effectDef.Kind == EffectKind.Periodic) &&
            effectDef.Params.TargetScore == ScoreId.HpCurrent &&
            effectDef.Params.BaseMagnitude < 0;

        public bool Learn(uint entityId, string abilityId)
        {
            if (!_abilityRegistry.TryGet(abilityId, out _))
                return false;

            var comp = EnsureComponent(entityId);
            if (comp.Known.Contains(abilityId))
                return false;

            comp.Known.Add(abilityId);
            return true;
        }

        public bool Teach(uint teacherEntityId, uint studentEntityId, string abilityId)
        {
            return Learn(studentEntityId, abilityId);
        }

        public IReadOnlyList<string> GetKnown(uint entityId)
        {
            if (!_entityService.TryGet<AbilitiesComponent>(entityId, out var comp))
                return Array.Empty<string>();
            return comp.Known;
        }

        public bool IsKnown(uint entityId, string abilityId)
        {
            if (!_entityService.TryGet<AbilitiesComponent>(entityId, out var comp))
                return false;
            return comp.Known.Contains(abilityId);
        }

        public float GetCooldownRemaining(uint entityId, string abilityId)
        {
            if (!_entityService.TryGet<AbilitiesComponent>(entityId, out var comp))
                return 0f;
            return comp.CooldownRemaining.TryGetValue(abilityId, out var remaining) ? remaining : 0f;
        }

        public IReadOnlyList<(string AbilityId, float CooldownRemaining)> GetCooldowns(uint entityId)
        {
            if (!_entityService.TryGet<AbilitiesComponent>(entityId, out var comp))
                return Array.Empty<(string, float)>();
            return comp.CooldownRemaining
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        public void AdvanceCooldowns(TimeSpan elapsed)
        {
            var elapsedSeconds = (float)elapsed.TotalSeconds;
            foreach (var (_, comp) in _entityService.GetAllComponents<AbilitiesComponent>())
            {
                var toRemove = new List<string>();
                var keys = new List<string>(comp.CooldownRemaining.Keys);
                foreach (var key in keys)
                {
                    comp.CooldownRemaining[key] -= elapsedSeconds;
                    if (comp.CooldownRemaining[key] <= 0f)
                        toRemove.Add(key);
                }
                foreach (var k in toRemove)
                    comp.CooldownRemaining.Remove(k);
            }
        }

        private AbilitiesComponent EnsureComponent(uint entityId)
        {
            if (!_entityService.TryGet<AbilitiesComponent>(entityId, out var comp))
            {
                comp = new AbilitiesComponent();
                _entityService.AddComponent(entityId, comp);
            }
            return comp;
        }

        private int GetCurrentPool(uint entityId, ResourceType resource) => resource switch
        {
            ResourceType.Hp      => _attributeSystem.GetCurrentHp(entityId),
            ResourceType.Mana    => _attributeSystem.GetCurrentMana(entityId),
            ResourceType.Stamina => _attributeSystem.GetCurrentStamina(entityId),
            ResourceType.Astra   => _attributeSystem.GetCurrentAstra(entityId),
            _                    => 0,
        };

        private void SpendPool(uint entityId, ResourceType resource, int amount)
        {
            switch (resource)
            {
                case ResourceType.Hp:
                    _attributeSystem.SetCurrentHp(entityId, _attributeSystem.GetCurrentHp(entityId) - amount);
                    break;
                case ResourceType.Mana:
                    _attributeSystem.SetCurrentMana(entityId, _attributeSystem.GetCurrentMana(entityId) - amount);
                    break;
                case ResourceType.Stamina:
                    _attributeSystem.SetCurrentStamina(entityId, _attributeSystem.GetCurrentStamina(entityId) - amount);
                    break;
                case ResourceType.Astra:
                    _attributeSystem.SetCurrentAstra(entityId, _attributeSystem.GetCurrentAstra(entityId) - amount);
                    break;
            }
        }

        private void ApplyInstantMagnitude(uint entityId, ScoreId scoreId, int power)
        {
            switch (scoreId)
            {
                case ScoreId.HpCurrent:
                    _attributeSystem.SetCurrentHp(entityId, _attributeSystem.GetCurrentHp(entityId) + power);
                    break;
                case ScoreId.ManaCurrent:
                    _attributeSystem.SetCurrentMana(entityId, _attributeSystem.GetCurrentMana(entityId) + power);
                    break;
                case ScoreId.StaminaCurrent:
                    _attributeSystem.SetCurrentStamina(entityId, _attributeSystem.GetCurrentStamina(entityId) + power);
                    break;
                case ScoreId.AstraCurrent:
                    _attributeSystem.SetCurrentAstra(entityId, _attributeSystem.GetCurrentAstra(entityId) + power);
                    break;
                // Other scores: ignore for now
            }
        }

        private static AbilityActivationResult Fail(
            AbilityActivationOutcome outcome,
            string abilityId,
            string? reason = null)
        {
            return new AbilityActivationResult(
                outcome, abilityId,
                Array.Empty<Effect>(),
                Array.Empty<ResourceCost>(),
                0f,
                reason);
        }
    }
}
