using System.Threading.Tasks;
using Hedron.Core.Events;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Time.Events;

namespace Hedron.Core.Modules.Effects.Handlers
{
    public sealed class EffectTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly IEffectSystem _effectSystem;
        private readonly IAttributeSystem _attributeSystem;
        private readonly IEventBus _eventBus;

        public int Priority => HandlerPriority.Domain;

        public EffectTickHandler(IEffectSystem effectSystem, IAttributeSystem attributeSystem, IEventBus eventBus)
        {
            _effectSystem = effectSystem;
            _attributeSystem = attributeSystem;
            _eventBus = eventBus;
        }

        public async Task HandleAsync(HeartbeatTickEvent @event)
        {
            var result = _effectSystem.AdvanceTick(@event.Elapsed);

            foreach (var app in result.DueApplications)
            {
                ApplyMagnitude(app.EntityId, app.Effect.Params.TargetScore, app.Magnitude);
            }

            foreach (var (entityId, effect) in result.Expired)
            {
                await _eventBus.PublishAsync(new EffectExpiredEvent(entityId, effect.EffectId))
                    .ConfigureAwait(false);
            }
        }

        private void ApplyMagnitude(uint entityId, ScoreId targetScore, int magnitude)
        {
            switch (targetScore)
            {
                case ScoreId.HpCurrent:
                    _attributeSystem.SetCurrentHp(entityId,
                        _attributeSystem.GetCurrentHp(entityId) + magnitude);
                    break;
                case ScoreId.ManaCurrent:
                    _attributeSystem.SetCurrentMana(entityId,
                        _attributeSystem.GetCurrentMana(entityId) + magnitude);
                    break;
                case ScoreId.StaminaCurrent:
                    _attributeSystem.SetCurrentStamina(entityId,
                        _attributeSystem.GetCurrentStamina(entityId) + magnitude);
                    break;
                case ScoreId.AstraCurrent:
                    _attributeSystem.SetCurrentAstra(entityId,
                        _attributeSystem.GetCurrentAstra(entityId) + magnitude);
                    break;
                // Non-pool scores (stat modifiers) are felt via IEffectSystem.GetModifiers on read.
            }
        }
    }
}
