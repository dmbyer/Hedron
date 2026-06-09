using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Systems
{
    public sealed class AreaSystem : IAreaSystem
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;

        public AreaSystem(EntityService entityService, ITemplateRegistry templateRegistry)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
        }

        public IReadOnlyList<uint> GetRoomsInArea(uint areaEntityId)
        {
            var result = new List<uint>();
            foreach (var (entityId, room) in _entityService.GetAllComponents<RoomComponent>())
            {
                if (room.AreaEntityId == areaEntityId)
                    result.Add(entityId);
            }
            return result;
        }

        public uint? GetAreaForRoom(uint roomEntityId)
        {
            if (!_entityService.TryGet<RoomComponent>(roomEntityId, out var room))
                return null;
            return room.AreaEntityId == 0 ? null : room.AreaEntityId;
        }

        public void AssignRoomToArea(uint roomEntityId, uint areaEntityId, string areaBlueprintId)
        {
            if (_entityService.TryGet<RoomComponent>(roomEntityId, out var room))
                room.AreaEntityId = areaEntityId;

            // Mirror areaBlueprintId to the in-memory RoomTemplate.AreaId (durable form).
            if (_entityService.TryGet<BlueprintComponent>(roomEntityId, out var bp) &&
                _templateRegistry.TryGet(bp.BlueprintId, out var template) &&
                template is RoomTemplate roomTemplate)
                roomTemplate.AreaId = areaBlueprintId;
        }
    }
}
