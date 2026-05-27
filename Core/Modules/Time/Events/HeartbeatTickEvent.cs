using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Time.Events
{
    public sealed record HeartbeatTickEvent(
        long TickId,
        DateTimeOffset Timestamp,
        TimeSpan Elapsed) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
