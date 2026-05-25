using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Components;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Account.Handlers
{
    /// <summary>
    /// After world content loads, validates each hydrated character entity's room.
    /// Resets to the starting room if the room entity no longer exists (e.g. it was deleted
    /// between sessions). Saves the entity immediately when a correction is made so the
    /// fix is durable without waiting for the next flush cycle.
    /// Subscribes to <c>WorldContentReadyEvent</c> so that authored room entities are available
    /// and <c>WorldConfiguration.StartingRoomEntityId</c> is set.
    /// </summary>
    public sealed class CharacterHydrationHandler : IEventHandler<WorldContentReadyEvent>
    {
        private readonly EntityService _entityService;
        private readonly WorldConfiguration _worldConfig;
        private readonly IPersistenceSystem _persistence;
        private readonly ILogger<CharacterHydrationHandler> _logger;

        public int Priority => HandlerPriority.Domain;

        public CharacterHydrationHandler(
            EntityService entityService,
            WorldConfiguration worldConfig,
            IPersistenceSystem persistence,
            ILogger<CharacterHydrationHandler> logger)
        {
            _entityService = entityService;
            _worldConfig = worldConfig;
            _persistence = persistence;
            _logger = logger;
        }

        public async Task HandleAsync(WorldContentReadyEvent @event)
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
                    await _persistence.SaveEntityAsync(entityId).ConfigureAwait(false);
                }

                // Migration guard: characters persisted before Phase B lack InventoryComponent.
                // Attach an empty one now; it will be persisted on the character's next save-on-change.
                if (!_entityService.HasComponent<InventoryComponent>(entityId))
                    _entityService.AddComponent(entityId, new InventoryComponent());
            }
        }
    }
}
