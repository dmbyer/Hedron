using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Account.Systems;
using Hedron.Core.Modules.Session.Events;
using Hedron.Core.Output;
using Hedron.Core.Sessions;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Session.Handlers
{
    /// <summary>
    /// Attaches and detaches the transient session shim for connecting and disconnecting players.
    /// On connect: the character entity already has <c>CharacterComponent</c> and
    /// <c>LocationComponent</c> set by the login flow — this handler attaches the transient
    /// <c>PlayerComponent</c> and broadcasts the arrival.
    /// On disconnect: calls <c>IAccountSystem.RecordLogout</c>, detaches <c>PlayerComponent</c>,
    /// and broadcasts the departure. The character entity is <b>not</b> destroyed.
    /// </summary>
    public class PlayerSessionHandler :
        IEventHandler<PlayerConnectedEvent>,
        IEventHandler<PlayerDisconnectedEvent>
    {
        private readonly EntityService _entityService;
        private readonly ISessionManager _sessionManager;
        private readonly IBroadcastSystem _broadcast;
        private readonly IAccountSystem _accountSystem;

        public int Priority => HandlerPriority.Domain;

        public PlayerSessionHandler(
            EntityService entityService,
            ISessionManager sessionManager,
            IBroadcastSystem broadcast,
            IAccountSystem accountSystem)
        {
            _entityService = entityService;
            _sessionManager = sessionManager;
            _broadcast = broadcast;
            _accountSystem = accountSystem;
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

            if (!_entityService.TryGet<LocationComponent>(@event.PlayerEntityId, out var location))
                return;

            await _broadcast.SendToRoomAsync(
                location.RoomEntityId,
                new PlainMessage($"Welcome, {@event.Name}!", OutputSeverity.System),
                entityId => entityId == @event.PlayerEntityId).ConfigureAwait(false);

            await _broadcast.SendToRoomAsync(
                location.RoomEntityId,
                new PlainMessage($"{@event.Name} has entered the world.", OutputSeverity.System),
                entityId => entityId != @event.PlayerEntityId).ConfigureAwait(false);

            await _broadcast.SendRoomDescriptionAsync(
                @event.PlayerEntityId,
                location.RoomEntityId).ConfigureAwait(false);
        }

        private async Task HandleDisconnectedAsync(PlayerDisconnectedEvent @event)
        {
            _accountSystem.RecordLogout(@event.PlayerEntityId);

            var name = @event.Name;
            uint? roomId = null;

            if (_entityService.TryGet<LocationComponent>(@event.PlayerEntityId, out var location))
                roomId = location.RoomEntityId;

            _entityService.RemoveComponent<PlayerComponent>(@event.PlayerEntityId);

            if (roomId.HasValue)
            {
                await _broadcast.SendToRoomAsync(
                    roomId.Value,
                    new PlainMessage($"{name} has left the world.", OutputSeverity.System))
                    .ConfigureAwait(false);
            }
        }
    }
}
