using Hedron.Core.Events;

namespace Hedron.Core.Modules.Persistence.Events
{
    /// <summary>
    /// Published after a single entity has been successfully written to disk via
    /// <c>IPersistenceSystem.SaveEntityAsync</c>.
    /// </summary>
    /// <remarks>
    /// The publisher is the code that called <c>SaveEntityAsync</c> (e.g. an admin command
    /// handler), not <c>PersistenceSystem</c> itself. <c>PersistenceSystem</c> is a pure Core
    /// System with no event-bus dependency.
    /// Informational only. The expected consumer is logging. Do not use this event to trigger
    /// gameplay behaviour.
    /// </remarks>
    public record EntityPersistedEvent(uint EntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
