using System;
using Hedron.Core.Events;

namespace Hedron.Core.Commands.Events
{
    /// <summary>
    /// Published by <see cref="CommandDispatcher"/> after every dispatch — success,
    /// parse-fail, unauthorized, or threw. Provides a low-fidelity command trace
    /// controllable via log level; <c>AdminAuditHandler</c> uses the richer slice-2
    /// admin events instead.
    /// </summary>
    public record CommandExecutedEvent(
        uint InvokerEntityId,
        string Verb,
        string ArgsSummary,
        CommandOutcome Outcome) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
