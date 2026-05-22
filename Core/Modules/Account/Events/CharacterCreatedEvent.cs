using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Account.Events
{
    public record CharacterCreatedEvent(
        uint CharacterEntityId,
        uint AccountEntityId,
        string CharacterName) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
