using System;
using System.Collections.Generic;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Items.Events
{
    public sealed record ItemUnequippedEvent(
        uint PlayerEntityId,
        uint ItemEntityId,
        IReadOnlyList<WornSlot> Slots) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
