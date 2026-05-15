using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>Published after an admin teleports themselves or another player via <c>@teleport</c>.</summary>
    public record PlayerTeleportedByAdminEvent(
        uint AdminEntityId,
        uint TargetEntityId,
        uint FromRoomEntityId,
        uint ToRoomEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
