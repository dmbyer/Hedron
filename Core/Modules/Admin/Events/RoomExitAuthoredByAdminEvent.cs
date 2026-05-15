using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>Published after an admin authors an exit between two rooms via <c>@dig</c>.</summary>
    public record RoomExitAuthoredByAdminEvent(
        uint AdminEntityId,
        uint RoomEntityId,
        Direction Direction,
        uint TargetRoomEntityId,
        bool BidirectionalLinkCreated) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
