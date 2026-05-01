using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Movement.Handlers
{
    public class PlayerMovedHandler : IEventHandler<PlayerMovedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Domain;

        public PlayerMovedHandler(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task HandleAsync(PlayerMovedEvent @event)
        {
            var playerName = GetDisplayName(@event.PlayerEntityId);
            var opposite = Opposite(@event.Direction);

            await _broadcast.SendToRoomAsync(
                @event.FromRoomEntityId,
                $"{playerName} leaves {DirectionName(@event.Direction)}.",
                excludeEntityId: @event.PlayerEntityId).ConfigureAwait(false);

            await _broadcast.SendToRoomAsync(
                @event.ToRoomEntityId,
                $"{playerName} arrives from the {DirectionName(opposite)}.",
                excludeEntityId: @event.PlayerEntityId).ConfigureAwait(false);

            await _broadcast.SendRoomDescriptionAsync(@event.PlayerEntityId, @event.ToRoomEntityId)
                .ConfigureAwait(false);
        }

        private string GetDisplayName(uint entityId) =>
            _entityService.TryGet<PlayerComponent>(entityId, out var p) ? p.DisplayName : "Someone";

        private static string DirectionName(Direction d) => d.ToString().ToLower();

        private static Direction Opposite(Direction d) => d switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East  => Direction.West,
            Direction.West  => Direction.East,
            Direction.Up    => Direction.Down,
            Direction.Down  => Direction.Up,
            _               => d,
        };
    }
}
