using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Admin.Events
{
    /// <summary>Published after an admin creates a new area via <c>mkarea</c>.</summary>
    public record AreaCreatedByAdminEvent(
        uint AdminEntityId,
        uint AreaEntityId,
        string BlueprintId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
