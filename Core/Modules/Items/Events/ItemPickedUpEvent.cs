using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Items.Events
{
    public sealed record ItemPickedUpEvent(
        uint PlayerEntityId,
        uint ItemEntityId,
        uint RoomEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
