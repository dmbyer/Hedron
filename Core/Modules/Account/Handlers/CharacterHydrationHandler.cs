using System.Collections.Generic;
using System.Linq;
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
    /// After world content loads, resolves each persistent entity's <c>RoomBlueprintId</c> to
    /// the current live <c>RoomEntityId</c>. Characters whose blueprint cannot be resolved
    /// (deleted room, instanced room, first login with no blueprint set) are moved to the
    /// starting room; the correction is saved immediately. Non-character persistent entities
    /// in an unresolvable room are destroyed.
    /// Subscribes to <c>WorldContentReadyEvent</c> so that all rooms are live before resolution.
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
            var liveBlueprints = BuildLiveBlueprintMap();

            // Snapshot to avoid modifying the collection while iterating (DestroyEntity can remove components).
            var locationEntities = _entityService.GetAllComponents<LocationComponent>().ToList();

            foreach (var (entityId, location) in locationEntities)
            {
                if (!_entityService.HasComponent<PersistentEntity>(entityId))
                    continue;

                var isCharacter = _entityService.HasComponent<CharacterComponent>(entityId);

                if (!string.IsNullOrEmpty(location.RoomBlueprintId) &&
                    liveBlueprints.TryGetValue(location.RoomBlueprintId, out var resolvedId))
                {
                    location.RoomEntityId = resolvedId;
                }
                else
                {
                    if (isCharacter)
                    {
                        _logger.LogWarning(
                            "Character entity {EntityId} had unresolvable RoomBlueprintId '{BlueprintId}'; resetting to starting room.",
                            entityId, location.RoomBlueprintId);
                        ResetToStartingRoom(location);
                        await _persistence.SaveEntityAsync(entityId).ConfigureAwait(false);
                    }
                    else
                    {
                        // Non-character persistent entity in an instanced or deleted room — destroy it.
                        _logger.LogWarning(
                            "Persistent entity {EntityId} had unresolvable RoomBlueprintId '{BlueprintId}'; destroying.",
                            entityId, location.RoomBlueprintId);
                        _entityService.DestroyEntity(entityId);
                        continue;
                    }
                }

                if (!isCharacter) continue;

                // Migration guards: attach missing required components for characters persisted
                // before the slices that introduced them.
                if (!_entityService.HasComponent<InventoryComponent>(entityId))
                    _entityService.AddComponent(entityId, new InventoryComponent());

                if (!_entityService.HasComponent<EquipmentComponent>(entityId))
                    _entityService.AddComponent(entityId, new EquipmentComponent());

                if (!_entityService.HasComponent<AttributesComponent>(entityId))
                    _entityService.AddComponent(entityId, new AttributesComponent());

                if (!_entityService.HasComponent<PoolsComponent>(entityId))
                    _entityService.AddComponent(entityId, new PoolsComponent());
            }
        }

        private void ResetToStartingRoom(LocationComponent location)
        {
            location.RoomEntityId = _worldConfig.StartingRoomEntityId;
            location.RoomBlueprintId = _entityService.TryGet<BlueprintComponent>(_worldConfig.StartingRoomEntityId, out var bp)
                ? bp.BlueprintId
                : null;
        }

        private Dictionary<string, uint> BuildLiveBlueprintMap()
        {
            var map = new Dictionary<string, uint>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var (entityId, bp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (!string.IsNullOrEmpty(bp.BlueprintId))
                    map[bp.BlueprintId] = entityId;
            }
            return map;
        }
    }
}
