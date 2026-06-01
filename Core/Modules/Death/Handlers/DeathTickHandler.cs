using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Modules.Time.Events;
using Microsoft.Extensions.Options;

namespace Hedron.Core.Modules.Death.Handlers
{
    /// <summary>
    /// Applies the bleed-out tick to every incapacitated player on each heartbeat.
    /// Reads HP before the mutation, calls <see cref="IAttributeSystem.SetCurrentHp"/>,
    /// then calls <see cref="IDeathSystem.OnHpChanged"/> to evaluate whether the threshold
    /// was reached. Publishes either <see cref="PlayerBleedingEvent"/> (alive but bleeding)
    /// or <see cref="PlayerDiedEvent"/> (floor reached). Priority 20
    /// (<see cref="HandlerPriority.Domain"/>).
    /// </summary>
    public sealed class DeathTickHandler : IEventHandler<HeartbeatTickEvent>
    {
        private readonly EntityService _entityService;
        private readonly IAttributeSystem _attributeSystem;
        private readonly IDeathSystem _deathSystem;
        private readonly IEventBus _eventBus;
        private readonly DeathOptions _options;

        public int Priority => HandlerPriority.Domain;

        public DeathTickHandler(
            EntityService entityService,
            IAttributeSystem attributeSystem,
            IDeathSystem deathSystem,
            IEventBus eventBus,
            IOptions<DeathOptions> options)
        {
            _entityService = entityService;
            _attributeSystem = attributeSystem;
            _deathSystem = deathSystem;
            _eventBus = eventBus;
            _options = options.Value;
        }

        public async Task HandleAsync(HeartbeatTickEvent @event)
        {
            // Snapshot before iterating to avoid mutation during enumeration.
            var incapacitated = _entityService.GetAllComponents<EntityStateComponent>()
                .Where(pair => (pair.Component.ActiveStates & EntityStateFlags.Incapacitated) != 0)
                .Select(pair => pair.EntityId)
                .ToList();

            foreach (var entityId in incapacitated)
            {
                var previousHp = _attributeSystem.GetCurrentHp(entityId);
                var newHp = previousHp - _options.BleedPerTick;
                _attributeSystem.SetCurrentHp(entityId, newHp);

                // Re-read after the clamp applied by IAttributeSystem.
                newHp = _attributeSystem.GetCurrentHp(entityId);

                var transition = _deathSystem.OnHpChanged(entityId, previousHp, newHp);

                if (transition == DeathTransition.Died)
                {
                    var roomEntityId = _entityService.TryGet<LocationComponent>(entityId, out var loc)
                        ? loc.RoomEntityId
                        : 0u;

                    await _eventBus.PublishAsync(
                        new PlayerDiedEvent(entityId, roomEntityId, KillerEntityId: 0))
                        .ConfigureAwait(false);
                }
                else
                {
                    // transition == None (already incapacitated — not a new threshold crossing)
                    await _eventBus.PublishAsync(
                        new PlayerBleedingEvent(entityId, newHp, _options.HpFloor))
                        .ConfigureAwait(false);
                }
            }
        }
    }
}
