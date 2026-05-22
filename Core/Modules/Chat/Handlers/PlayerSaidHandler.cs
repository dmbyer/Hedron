using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Chat.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Chat.Handlers
{
    public class PlayerSaidHandler : IEventHandler<PlayerSaidEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Domain;

        public PlayerSaidHandler(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task HandleAsync(PlayerSaidEvent @event)
        {
            if (!_entityService.TryGet<LocationComponent>(@event.PlayerEntityId, out var location))
                return;

            var playerName = _entityService.TryGet<PlayerComponent>(@event.PlayerEntityId, out var p)
                ? p.DisplayName
                : "Someone";

            await _broadcast.SendToRoomAsync(
                location.RoomEntityId,
                new PlainMessage($"{playerName} says: {@event.Message}", OutputSeverity.Chat))
                .ConfigureAwait(false);
        }
    }
}
