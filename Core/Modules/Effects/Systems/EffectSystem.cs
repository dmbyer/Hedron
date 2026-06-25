using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Stats;
using ProtComp = Hedron.Core.ECS.Components.ProtectionComponent;

namespace Hedron.Core.Modules.Effects.Systems
{
    public sealed class EffectSystem : IEffectSystem
    {
        private readonly EntityService _entityService;
        private readonly IEnumerable<IEffectContributor> _contributors;

        public EffectSystem(EntityService entityService, IEnumerable<IEffectContributor> contributors)
        {
            _entityService = entityService;
            _contributors = contributors;
        }

        public EffectApplyResult Apply(uint targetEntityId, EffectDefinition definition, uint sourceEntityId)
        {
            // Gate B — effect immunity: reject ALL effects (beneficial and harmful) if the target
            // carries EffectImmune. This check must be first, before any Power computation or
            // EffectsComponent mutation (INV-4, INV-8).
            if (_entityService.TryGet<ProtComp>(targetEntityId, out var protection) &&
                protection.Flags.HasFlag(ProtectionFlags.EffectImmune))
            {
                return EffectApplyResult.Immune;
            }

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
                return EffectApplyResult.ForApplied(effect);

            EnsureComponent(targetEntityId, out var comp);

            switch (definition.Stacking)
            {
                case StackPolicy.HighestWins:
                {
                    var existing = comp.Effects.Find(e => e.EffectId == definition.EffectId);
                    if (existing != null)
                    {
                        if (existing.Power >= power)
                            return EffectApplyResult.StackingBlocked;
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

            return EffectApplyResult.ForApplied(effect);
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
            IEnumerable<Effect> stored = _entityService.TryGet<EffectsComponent>(entityId, out var comp)
                ? comp.Effects
                : Array.Empty<Effect>();
            var derived = _contributors.SelectMany(c => c.GetActive(entityId));
            return stored.Concat(derived).ToList();
        }

        public int GetModifiers(uint entityId, ScoreId scoreId)
        {
            int stored = 0;
            if (_entityService.TryGet<EffectsComponent>(entityId, out var comp))
                stored = comp.Effects
                    .Where(e => e.Kind == EffectKind.StatModifier && e.Params.TargetScore == scoreId)
                    .Sum(e => e.Power);
            return stored + _contributors.Sum(c => c.GetModifiers(entityId, scoreId));
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
