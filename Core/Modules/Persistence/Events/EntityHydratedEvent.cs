using Hedron.Core.Events;

namespace Hedron.Core.Modules.Persistence.Events
{
    /// <summary>
    /// Published by <c>PersistenceSystem.LoadAllAsync</c> after a single entity's components
    /// have all been attached from disk.
    /// </summary>
    /// <remarks>
    /// <b>Constraint:</b> handlers of this event <b>must not query other entities</b> — the world
    /// is partially loaded at time of fire. Cross-entity startup work belongs in
    /// <see cref="WorldLoadedEvent"/> handlers, which fire after all entities are loaded.
    /// </remarks>
    public record EntityHydratedEvent(uint EntityId) : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
