using Hedron.Core.Events;

namespace Hedron.Core.Modules.Persistence.Events
{
    /// <summary>
    /// Published once by <c>PersistenceBootstrap.StartAsync</c> after
    /// <c>IPersistenceSystem.LoadAllAsync</c> completes and every persisted entity is in-world.
    /// </summary>
    /// <remarks>
    /// This is the approved signal for cross-entity startup work: rebuilding occupancy indexes,
    /// re-establishing inter-entity references, seeding derived state from loaded components.
    /// All handlers here may safely query any entity.
    /// </remarks>
    public record WorldLoadedEvent : IEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}
