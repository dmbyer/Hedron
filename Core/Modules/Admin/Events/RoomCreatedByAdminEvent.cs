using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>Published after an admin creates a new room via <c>dig</c>.</summary>
    public record RoomCreatedByAdminEvent(
        uint AdminEntityId,
        uint NewRoomEntityId,
        string BlueprintId,
        uint SourceRoomEntityId,
        Direction Direction,
        bool BidirectionalLinkCreated) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
