using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Abilities.Events
{
    public sealed record AbilityLearnedEvent(uint EntityId, string AbilityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
