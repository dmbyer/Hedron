using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.World.Events;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Account.Handlers
{
    /// <summary>
    /// After world content loads, validates each hydrated character entity's room.
    /// Resets to the starting room if the room entity no longer exists (e.g. it was deleted
    /// between sessions). Subscribes to <c>WorldContentReadyEvent</c> so that authored room
    /// entities are available and <c>WorldConfiguration.StartingRoomEntityId</c> is set.
    /// </summary>
    public sealed class CharacterHydrationHandler : IEventHandler<WorldContentReadyEvent>
    {
        private readonly EntityService _entityService;
        private readonly WorldConfiguration _worldConfig;
        private readonly ILogger<CharacterHydrationHandler> _logger;

        public int Priority => HandlerPriority.Domain;

        public CharacterHydrationHandler(
            EntityService entityService,
            WorldConfiguration worldConfig,
            ILogger<CharacterHydrationHandler> logger)
        {
            _entityService = entityService;
            _worldConfig = worldConfig;
            _logger = logger;
        }

        public Task HandleAsync(WorldContentReadyEvent @event)
        {
            foreach (var (entityId, location) in _entityService.GetAllComponents<LocationComponent>())
            {
                if (!_entityService.HasComponent<CharacterComponent>(entityId))
                    continue;

                if (!_entityService.HasComponent<RoomComponent>(location.RoomEntityId))
                {
                    _logger.LogWarning(
                        "Character entity {EntityId} had invalid room {RoomId}; resetting to starting room.",
                        entityId, location.RoomEntityId);
                    location.RoomEntityId = _worldConfig.StartingRoomEntityId;
                }
            }

            return Task.CompletedTask;
        }
    }
}
