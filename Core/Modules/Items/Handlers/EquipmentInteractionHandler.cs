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
    /// Broadcasts wear and remove events to the room and confirms the action to the actor.
    /// Pure output fan-out — no domain logic, no persistence calls.
    /// </summary>
    public sealed class EquipmentInteractionHandler :
        IEventHandler<ItemEquippedEvent>,
        IEventHandler<ItemUnequippedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Notification;

        public EquipmentInteractionHandler(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task HandleAsync(ItemEquippedEvent @event)
        {
            var playerName = GetDisplayName(@event.PlayerEntityId);
            var itemName = GetItemName(@event.ItemEntityId);

            if (!_entityService.TryGet<LocationComponent>(@event.PlayerEntityId, out var location))
                return;

            await _broadcast.SendToRoomAsync(
                location.RoomEntityId,
                new PlainMessage($"{playerName} wears {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);

            await _broadcast.SendToRoomAsync(
                location.RoomEntityId,
                new PlainMessage($"You wear {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        public async Task HandleAsync(ItemUnequippedEvent @event)
        {
            var playerName = GetDisplayName(@event.PlayerEntityId);
            var itemName = GetItemName(@event.ItemEntityId);

            if (!_entityService.TryGet<LocationComponent>(@event.PlayerEntityId, out var location))
                return;

            await _broadcast.SendToRoomAsync(
                location.RoomEntityId,
                new PlainMessage($"{playerName} removes {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.PlayerEntityId)
                .ConfigureAwait(false);

            await _broadcast.SendToRoomAsync(
                location.RoomEntityId,
                new PlainMessage($"You remove {itemName}.", OutputSeverity.System, OutputCategory.System),
                entityId => entityId == @event.PlayerEntityId)
                .ConfigureAwait(false);
        }

        private string GetDisplayName(uint entityId) =>
            _entityService.TryGet<PlayerComponent>(entityId, out var p) ? p.DisplayName : "Someone";

        private string GetItemName(uint entityId) =>
            _entityService.TryGet<ItemDataComponent>(entityId, out var d) ? d.Name : "something";
    }
}
