using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Session.Events
{
    /// <summary>Published when a player's connection drops, before their entity is cleaned up.</summary>
    public record PlayerDisconnectedEvent(uint PlayerEntityId, string Name) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
