using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Modules.Effects.Events;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Time.Events;

namespace Hedron.Core.Modules.Effects.Handlers
{
    public sealed class EffectTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly EntityService _entityService;
        private readonly IEffectSystem _effectSystem;
        private readonly IAttributeSystem _attributeSystem;
        private readonly IDeathSystem _deathSystem;
        private readonly IEventBus _eventBus;

        public int Priority => HandlerPriority.Domain;

        public EffectTickHandler(
            EntityService entityService,
            IEffectSystem effectSystem,
            IAttributeSystem attributeSystem,
            IDeathSystem deathSystem,
            IEventBus eventBus)
        {
            _entityService = entityService;
            _effectSystem = effectSystem;
            _attributeSystem = attributeSystem;
            _deathSystem = deathSystem;
            _eventBus = eventBus;
        }

        public async Task HandleAsync(HeartbeatTickEvent @event)
        {
            var result = _effectSystem.AdvanceTick(@event.Elapsed);

            foreach (var app in result.DueApplications)
            {
                await ApplyMagnitudeAsync(app.EntityId, app.Effect.Params.TargetScore, app.Magnitude)
                    .ConfigureAwait(false);
            }

            foreach (var (entityId, effect) in result.Expired)
            {
                await _eventBus.PublishAsync(new EffectExpiredEvent(entityId, effect.EffectId))
                    .ConfigureAwait(false);
            }
        }

        private async Task ApplyMagnitudeAsync(uint entityId, ScoreId targetScore, int magnitude)
        {
            switch (targetScore)
            {
                case ScoreId.HpCurrent:
                    var hpBefore = _attributeSystem.GetCurrentHp(entityId);
                    _attributeSystem.SetCurrentHp(entityId, hpBefore + magnitude);
                    var hpAfter = _attributeSystem.GetCurrentHp(entityId);

                    var transition = _deathSystem.OnHpChanged(entityId, hpBefore, hpAfter);
                    if (transition == DeathTransition.BecameIncapacitated)
                    {
                        var roomEntityId = _entityService.TryGet<LocationComponent>(entityId, out var loc)
                            ? loc.RoomEntityId
                            : 0u;
                        await _eventBus.PublishAsync(new PlayerIncapacitatedEvent(entityId, roomEntityId))
                            .ConfigureAwait(false);
                    }
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
