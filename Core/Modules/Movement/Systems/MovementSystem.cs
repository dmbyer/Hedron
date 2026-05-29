using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Movement.Systems
{
    public class MovementSystem : IMovementSystem
    {
        private readonly EntityService _entityService;

        public MovementSystem(EntityService entityService) =>
            _entityService = entityService;

        public MoveResult TryMove(uint playerEntityId, Direction direction)
        {
            if (!_entityService.TryGet<LocationComponent>(playerEntityId, out var location))
                return MoveResult.Blocked("You are not in a room.");

            if (!_entityService.TryGet<RoomComponent>(location.RoomEntityId, out var room))
                return MoveResult.Blocked("You are floating in the void.");

            if (!room.Exits.TryGetValue(direction, out var destinationRoomId))
                return MoveResult.Blocked("There is no exit in that direction.");

            var fromRoomId = location.RoomEntityId;
            location.RoomEntityId = destinationRoomId;
            location.RoomBlueprintId = _entityService.TryGet<BlueprintComponent>(destinationRoomId, out var destBp)
                ? destBp.BlueprintId
                : null;

            return MoveResult.Moved(fromRoomId, destinationRoomId);
        }
    }
}
