using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Account.Events
{
    public record AccountCreatedEvent(uint AccountEntityId, string Username) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
