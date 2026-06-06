using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Movement.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Movement.Handlers
{
    /// <summary>
    /// Translates a successful move (player-initiated or admin-teleport) into the visible
    /// effects: departure broadcast on the source room, arrival broadcast on the destination,
    /// and a <c>look</c> sent to the moved player.
    /// </summary>
    /// <remarks>
    /// Both <see cref="PlayerMovedEvent"/> and <see cref="PlayerTeleportedByAdminEvent"/>
    /// funnel through the same private helper to avoid drift between regular movement and
    /// admin teleport. Teleports use direction-agnostic flavour text since teleport has no
    /// natural direction.
    /// </remarks>
    public class PlayerMovedHandler :
        IEventHandler<PlayerMovedEvent>,
        IEventHandler<PlayerTeleportedByAdminEvent>
    {
        private readonly EntityService _entityService;
        private readonly IBroadcastSystem _broadcast;

        public int Priority => HandlerPriority.Domain;

        public PlayerMovedHandler(EntityService entityService, IBroadcastSystem broadcast)
        {
            _entityService = entityService;
            _broadcast = broadcast;
        }

        public Task HandleAsync(PlayerMovedEvent @event) =>
            BroadcastTransitionAsync(
                @event.PlayerEntityId,
                @event.FromRoomEntityId,
                @event.ToRoomEntityId,
                $"leaves {DirectionName(@event.Direction)}.",
                $"arrives from the {DirectionName(Opposite(@event.Direction))}.");

        public Task HandleAsync(PlayerTeleportedByAdminEvent @event) =>
            BroadcastTransitionAsync(
                @event.TargetEntityId,
                @event.FromRoomEntityId,
                @event.ToRoomEntityId,
                "vanishes in a puff of admin smoke.",
                "appears in a puff of admin smoke.");

        private async Task BroadcastTransitionAsync(
            uint movedEntityId, uint fromRoomId, uint toRoomId,
            string departureSuffix, string arrivalSuffix)
        {
            var name = GetDisplayName(movedEntityId);

            if (fromRoomId != 0)
                await _broadcast.SendToRoomAsync(
                    fromRoomId,
                    new PlainMessage($"{name} {departureSuffix}", OutputSeverity.System, OutputCategory.System),
                    entityId => entityId != movedEntityId).ConfigureAwait(false);

            if (toRoomId != 0)
                await _broadcast.SendToRoomAsync(
                    toRoomId,
                    new PlainMessage($"{name} {arrivalSuffix}", OutputSeverity.System, OutputCategory.System),
                    entityId => entityId != movedEntityId).ConfigureAwait(false);

            if (toRoomId != 0)
                await _broadcast.SendRoomDescriptionAsync(movedEntityId, toRoomId)
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
