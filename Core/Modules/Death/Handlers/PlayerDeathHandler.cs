using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Modules.Death.Systems;

namespace Hedron.Core.Modules.Death.Handlers
{
    /// <summary>
    /// Responds to <see cref="PlayerDiedEvent"/> by calling
    /// <see cref="IDeathSystem.Respawn"/> (which handles state reset, location,
    /// effects, and pool restoration), then publishes
    /// <see cref="PlayerRespawnedEvent"/>. Priority 20 (<see cref="HandlerPriority.Domain"/>).
    /// </summary>
    public sealed class PlayerDeathHandler : IEventHandler<PlayerDiedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IDeathSystem _deathSystem;
        private readonly IEventBus _eventBus;

        public int Priority => HandlerPriority.Domain;

        public PlayerDeathHandler(
            EntityService entityService,
            IDeathSystem deathSystem,
            IEventBus eventBus)
        {
            _entityService = entityService;
            _deathSystem = deathSystem;
            _eventBus = eventBus;
        }

        public async Task HandleAsync(PlayerDiedEvent @event)
        {
            _deathSystem.Respawn(@event.PlayerEntityId);

            // Read the new location AFTER Respawn has already set it.
            var respawnRoomEntityId = _entityService.TryGet<LocationComponent>(@event.PlayerEntityId, out var loc)
                ? loc.RoomEntityId
                : 0u;

            await _eventBus.PublishAsync(
                new PlayerRespawnedEvent(@event.PlayerEntityId, respawnRoomEntityId))
                .ConfigureAwait(false);
        }
    }
}
