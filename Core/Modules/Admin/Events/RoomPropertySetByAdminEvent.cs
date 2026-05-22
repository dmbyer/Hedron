using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>Published after an admin sets a property on a room via <c>set</c>.</summary>
    public record RoomPropertySetByAdminEvent(
        uint AdminEntityId,
        uint RoomEntityId,
        string PropertyName,
        string NewValue) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
