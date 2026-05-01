using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Session.Events
{
    /// <summary>Published after a new player has entered their name and been bound to a world entity.</summary>
    public record PlayerConnectedEvent(uint PlayerEntityId, string Name) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
