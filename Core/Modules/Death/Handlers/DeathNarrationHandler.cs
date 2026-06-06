using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Death.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Death.Handlers
{
    /// <summary>
    /// Broadcasts and writes narrative messages for all player death lifecycle events.
    /// Pure output: no state mutations, no system calls beyond <see cref="IBroadcastSystem"/>.
    /// Priority 80 (<see cref="HandlerPriority.Notification"/>).
    /// </summary>
    public sealed class DeathNarrationHandler :
        IEventHandler<PlayerIncapacitatedEvent>,
        IEventHandler<PlayerBleedingEvent>,
        IEventHandler<PlayerDiedEvent>,
        IEventHandler<PlayerRespawnedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Notification;

        public DeathNarrationHandler(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task HandleAsync(PlayerIncapacitatedEvent @event)
        {
            var name = GetPlayerName(@event.PlayerEntityId);

            // Write to the downed player personally.
            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage("You collapse, bleeding out. You cannot act — find healing fast.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);

            // Broadcast to everyone else in the room.
            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"{name} collapses, mortally wounded!", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        public async Task HandleAsync(PlayerBleedingEvent @event)
        {
            // Resolve the room once; both sends below use it.
            var roomEntityId = _entityService.TryGet<LocationComponent>(@event.PlayerEntityId, out var loc)
                ? loc.RoomEntityId
                : 0u;

            if (roomEntityId == 0)
                return;

            // Personal message to the downed player.
            await _broadcast.SendToRoomAsync(
                roomEntityId,
                new PlainMessage(
                    $"You are bleeding out ({@event.CurrentHp}/{@event.HpFloor}). Without healing you will die.",
                    OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);

            // Room broadcast (everyone except the player) so witnesses know someone is dying.
            var name = GetPlayerName(@event.PlayerEntityId);
            await _broadcast.SendToRoomAsync(
                roomEntityId,
                new PlainMessage($"{name} is bleeding out and near death.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        public async Task HandleAsync(PlayerDiedEvent @event)
        {
            var name = GetPlayerName(@event.PlayerEntityId);

            // Broadcast death to the room where the player fell.
            await _broadcast.SendToRoomAsync(
                @event.DeathRoomEntityId,
                new PlainMessage($"{name} has died.", OutputSeverity.System, OutputCategory.System))
                .ConfigureAwait(false);
        }

        public async Task HandleAsync(PlayerRespawnedEvent @event)
        {
            var roomName = GetRoomName(@event.RespawnRoomEntityId);

            // Tell the respawning player personally.
            await _broadcast.SendToRoomAsync(
                @event.RespawnRoomEntityId,
                new PlainMessage($"You awaken, weak but alive, at {roomName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);

            // Announce arrival to others in the respawn room.
            var name = GetPlayerName(@event.PlayerEntityId);
            await _broadcast.SendToRoomAsync(
                @event.RespawnRoomEntityId,
                new PlainMessage($"{name} awakens at {roomName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        private string GetPlayerName(uint entityId) =>
            _entityService.TryGet<PlayerComponent>(entityId, out var p) ? p.DisplayName : "Someone";

        private string GetRoomName(uint roomEntityId) =>
            _entityService.TryGet<RoomComponent>(roomEntityId, out var room) ? room.Name : "an unknown place";
    }
}
