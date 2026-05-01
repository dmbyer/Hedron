using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Session.Handlers
{
    /// <summary>
    /// Sets up and tears down the in-world state for a connecting or disconnecting player.
    /// </summary>
    public class PlayerSessionHandler :
        IEventHandler<PlayerConnectedEvent>,
        IEventHandler<PlayerDisconnectedEvent>
    {
        private readonly EntityService _entityService;
        private readonly ISessionManager _sessionManager;
        private readonly IBroadcastSystem _broadcast;
        private readonly WorldConfiguration _worldConfig;

        public int Priority => HandlerPriority.Domain;

        public PlayerSessionHandler(
            EntityService entityService,
            ISessionManager sessionManager,
            IBroadcastSystem broadcast,
            WorldConfiguration worldConfig)
        {
            _entityService = entityService;
            _sessionManager = sessionManager;
            _broadcast = broadcast;
            _worldConfig = worldConfig;
        }

        Task IEventHandler<PlayerConnectedEvent>.HandleAsync(PlayerConnectedEvent @event) =>
            HandleConnectedAsync(@event);

        Task IEventHandler<PlayerDisconnectedEvent>.HandleAsync(PlayerDisconnectedEvent @event) =>
            HandleDisconnectedAsync(@event);

        private async Task HandleConnectedAsync(PlayerConnectedEvent @event)
        {
            var session = _sessionManager.GetSession(@event.PlayerEntityId);

            _entityService.AddComponent(@event.PlayerEntityId, new PlayerComponent
            {
                DisplayName = @event.Name,
                Session = session,
            });

            _entityService.AddComponent(@event.PlayerEntityId, new LocationComponent
            {
                RoomEntityId = _worldConfig.StartingRoomEntityId,
            });

            await _broadcast.SendToRoomAsync(
                _worldConfig.StartingRoomEntityId,
                $"{@event.Name} has entered the world.",
                excludeEntityId: @event.PlayerEntityId).ConfigureAwait(false);

            await _broadcast.SendRoomDescriptionAsync(
                @event.PlayerEntityId,
                _worldConfig.StartingRoomEntityId).ConfigureAwait(false);
        }

        private async Task HandleDisconnectedAsync(PlayerDisconnectedEvent @event)
        {
            if (!_entityService.TryGet<LocationComponent>(@event.PlayerEntityId, out var location))
            {
                _entityService.DestroyEntity(@event.PlayerEntityId);
                return;
            }

            var name = _entityService.TryGet<PlayerComponent>(@event.PlayerEntityId, out var player)
                ? player.DisplayName
                : @event.Name;

            await _broadcast.SendToRoomAsync(
                location.RoomEntityId,
                $"{name} has left the world.").ConfigureAwait(false);

            _entityService.DestroyEntity(@event.PlayerEntityId);
        }
    }
}
