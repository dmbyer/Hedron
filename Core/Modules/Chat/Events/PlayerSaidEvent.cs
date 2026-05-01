using System;
using Hedron.Core.Events;

namespace Hedron.Core.Modules.Chat.Events
{
    /// <summary>Published when a player uses the say command.</summary>
    public record PlayerSaidEvent(uint PlayerEntityId, string Message) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
