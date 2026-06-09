using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>Published after an admin assigns a room to an area via <c>setarea</c>.</summary>
    public record RoomAreaAssignedByAdminEvent(
        uint AdminEntityId,
        uint RoomEntityId,
        string RoomBlueprintId,
        uint AreaEntityId,
        string AreaBlueprintId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
