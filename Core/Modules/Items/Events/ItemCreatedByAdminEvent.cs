using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Items.Events
{
    /// <summary>Published after an admin creates an ad-hoc item via <c>mkitem</c>.</summary>
    public sealed record ItemCreatedByAdminEvent(
        uint AdminEntityId,
        uint ItemEntityId,
        string BlueprintId,
        uint RoomEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
