using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Ascension.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Ascension.Handlers
{
    /// <summary>
    /// Broadcasts a narrative message for <see cref="AscendedEvent"/>. Pure output: no state
    /// mutations, no system calls beyond <see cref="IBroadcastSystem"/>. Priority 80
    /// (<see cref="HandlerPriority.Notification"/>).
    /// </summary>
    public sealed class AscensionNarrationHandler : IEventHandler<AscendedEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Notification;

        public AscensionNarrationHandler(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public async Task HandleAsync(AscendedEvent @event)
        {
            var roomEntityId = _entityService.TryGet<LocationComponent>(@event.EntityId, out var loc)
                ? loc.RoomEntityId
                : 0u;

            if (roomEntityId == 0)
                return;

            await _broadcast.SendToRoomAsync(
                roomEntityId,
                new PlainMessage($"You ascend to Tier {@event.NewTier}.", OutputSeverity.Confirmation, OutputCategory.System),
                entityId => entityId == @event.EntityId)
                .ConfigureAwait(false);

            var name = GetPlayerName(@event.EntityId);
            await _broadcast.SendToRoomAsync(
                roomEntityId,
                new PlainMessage($"{name} ascends to Tier {@event.NewTier}!", OutputSeverity.System, OutputCategory.System),
                entityId => entityId != @event.EntityId)
                .ConfigureAwait(false);
        }

        private string GetPlayerName(uint entityId) =>
            _entityService.TryGet<PlayerComponent>(entityId, out var p) ? p.DisplayName : "Someone";
    }
}
