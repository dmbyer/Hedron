using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Items.Handlers
{
    /// <summary>
    /// Broadcasts pickup and drop events to the room and confirms the action to the actor.
    /// Pure output fan-out — no domain logic, no persistence calls.
    /// </summary>
    public sealed class ItemInteractionHandler :
        IEventHandler<ItemPickedUpEvent>,
        IEventHandler<ItemDroppedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Notification;

        public ItemInteractionHandler(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task HandleAsync(ItemPickedUpEvent @event)
        {
            var playerName = GetDisplayName(@event.PlayerEntityId);
            var itemName = GetItemName(@event.ItemEntityId);

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"{playerName} picks up {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"You pick up {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        public async Task HandleAsync(ItemDroppedEvent @event)
        {
            var playerName = GetDisplayName(@event.PlayerEntityId);
            var itemName = GetItemName(@event.ItemEntityId);

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"{playerName} drops {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);

            await _broadcast.SendToRoomAsync(
                @event.RoomEntityId,
                new PlainMessage($"You drop {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        private string GetDisplayName(uint entityId) =>
            _entityService.TryGet<PlayerComponent>(entityId, out var p) ? p.DisplayName : "Someone";

        private string GetItemName(uint entityId) =>
            _entityService.TryGet<ItemDataComponent>(entityId, out var d) ? d.Name : "something";
    }
}
