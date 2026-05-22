using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Session.Events
{
    /// <summary>Published after a player has authenticated and their character entity is bound to the session.</summary>
    public record PlayerConnectedEvent(uint PlayerEntityId, string Name, uint AccountEntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
