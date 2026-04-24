using System;

namespace Hedron.Core.Events
{
    /// <summary>
    /// Marker interface for all domain events published on the event bus.
    /// </summary>
    /// <remarks>
    /// Events are past-tense facts. Payloads are records — see
    /// <c>docs/architecture/03-events.md</c>.
    /// </remarks>
    public interface IEvent
    {
        /// <summary>When the event was raised.</summary>
        DateTime OccurredAt { get; }

        /// <summary>Unique identity for tracing / deduplication.</summary>
        Guid EventId { get; }
    }
}
