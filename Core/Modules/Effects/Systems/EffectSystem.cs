using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Modules.Effects.Systems
{
    public sealed class EffectSystem : IEffectSystem
    {
        private readonly EntityService _entityService;

        public EffectSystem(EntityService entityService)
        {
            _entityService = entityService;
        }

        public Effect? Apply(uint targetEntityId, EffectDefinition definition, uint sourceEntityId)
        {
            var power = PowerScaling.Evaluate(definition.PowerScalingFormula, definition, _entityService, sourceEntityId);

            var effect = new Effect(
                EffectId: definition.EffectId,
                Kind: definition.Kind,
                Params: definition.Params,
                Category: definition.Category,
                Power: power,
                Source: new EffectSource(sourceEntityId),
                Group: null,
                Lifetime: definition.Kind == EffectKind.Instant ? EffectLifetime.Instant : MapLifetime(definition.Duration),
                Duration: definition.Duration,
                Elapsed: 0f,
                Stacking: definition.Stacking,
                Phase: definition.Phase
            );

            if (definition.Kind == EffectKind.Instant)
                return effect;

            EnsureComponent(targetEntityId, out var comp);

            switch (definition.Stacking)
            {
                case StackPolicy.HighestWins:
                {
                    var existing = comp.Effects.Find(e => e.EffectId == definition.EffectId);
                    if (existing != null)
                    {
                        if (existing.Power >= power)
                            return null;
                        comp.Effects.Remove(existing);
                    }
                    comp.Effects.Add(effect);
                    break;
                }
                case StackPolicy.Refresh:
                {
                    var idx = comp.Effects.FindIndex(e => e.EffectId == definition.EffectId);
                    if (idx >= 0)
                        comp.Effects[idx] = comp.Effects[idx] with { Elapsed = 0f };
                    else
                        comp.Effects.Add(effect);
                    break;
                }
                case StackPolicy.UniquePerSource:
                {
                    var idx = comp.Effects.FindIndex(e => e.EffectId == definition.EffectId && e.Source.EntityId == sourceEntityId);
                    if (idx >= 0)
                        comp.Effects[idx] = effect;
                    else
                        comp.Effects.Add(effect);
                    break;
                }
                case StackPolicy.Replace:
                {
                    comp.Effects.RemoveAll(e => e.EffectId == definition.EffectId);
                    comp.Effects.Add(effect);
                    break;
                }
                default: // Stack
                    comp.Effects.Add(effect);
                    break;
            }

            return effect;
        }

        public void Remove(uint entityId, string effectId)
        {
            if (_entityService.TryGet<EffectsComponent>(entityId, out var comp))
                comp.Effects.RemoveAll(e => e.EffectId == effectId);
        }

        public void RemoveByCategory(uint entityId, EffectCategory category)
        {
            if (_entityService.TryGet<EffectsComponent>(entityId, out var comp))
                comp.Effects.RemoveAll(e => e.Category == category);
        }

        public void RemoveImpermanent(uint entityId)
        {
            if (_entityService.TryGet<EffectsComponent>(entityId, out var comp))
                comp.Effects.RemoveAll(e => e.Lifetime != EffectLifetime.UntilRemoved);
        }

        public IReadOnlyList<Effect> GetActive(uint entityId)
        {
            if (_entityService.TryGet<EffectsComponent>(entityId, out var comp))
                return comp.Effects;
            return Array.Empty<Effect>();
        }

        public int GetModifiers(uint entityId, ScoreId scoreId)
        {
            if (!_entityService.TryGet<EffectsComponent>(entityId, out var comp))
                return 0;
            return comp.Effects
                .Where(e => e.Kind == EffectKind.StatModifier && e.Params.TargetScore == scoreId)
                .Sum(e => e.Power);
        }

        public EffectTickResult AdvanceTick(TimeSpan elapsed)
        {
            var elapsedSeconds = (float)elapsed.TotalSeconds;
            var due = new List<PeriodicApplication>();
            var expired = new List<(uint EntityId, Effect Effect)>();

            foreach (var (entityId, comp) in _entityService.GetAllComponents<EffectsComponent>())
            {
                for (var i = comp.Effects.Count - 1; i >= 0; i--)
                {
                    var effect = comp.Effects[i];

                    if (effect.Lifetime == EffectLifetime.Timed)
                    {
                        var newElapsed = effect.Elapsed + elapsedSeconds;
                        comp.Effects[i] = effect with { Elapsed = newElapsed };
                        effect = comp.Effects[i];

                        if (effect.Elapsed >= effect.Duration)
                        {
                            expired.Add((entityId, effect));
                            comp.Effects.RemoveAt(i);
                            continue;
                        }
                    }

                    if (effect.Kind == EffectKind.Periodic)
                        due.Add(new PeriodicApplication(entityId, effect, effect.Power));
                }
            }

            due.Sort((a, b) => a.Effect.Phase.CompareTo(b.Effect.Phase));
            expired.Sort((a, b) => a.Effect.Phase.CompareTo(b.Effect.Phase));

            return new EffectTickResult(due, expired);
        }

        private void EnsureComponent(uint entityId, out EffectsComponent comp)
        {
            if (!_entityService.TryGet<EffectsComponent>(entityId, out comp))
            {
                comp = new EffectsComponent();
                _entityService.AddComponent(entityId, comp);
            }
        }

        private static EffectLifetime MapLifetime(float duration)
        {
            if (duration < 0f)
                return EffectLifetime.UntilRemoved;
            return EffectLifetime.Timed;
        }
    }
}
