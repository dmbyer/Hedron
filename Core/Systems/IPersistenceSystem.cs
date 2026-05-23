using System.Collections.Generic;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Cross-cutting system responsible for saving and loading entity state to/from disk.
    /// Implements the two-level persistence model: <c>PersistentEntity</c> opts an entity in;
    /// <c>[Persistent]</c> on a component type controls which components are included in the snapshot.
    /// </summary>
    public interface IPersistenceSystem
    {
        /// <summary>
        /// Forces an immediate flush of a single entity to disk.
        /// No-ops silently if the entity does not carry <c>PersistentEntity</c>.
        /// </summary>
        Task SaveEntityAsync(uint entityId, CancellationToken ct = default);

        /// <summary>
        /// Loads all persisted entities from disk, restoring them into the <c>EntityService</c>.
        /// No event-bus events are published — component attachment is silent.
        /// Returns the IDs of every entity that was successfully restored so that the calling
        /// orchestrator (<c>PersistenceBootstrap</c>) can publish <c>EntityHydratedEvent</c>
        /// and <c>WorldLoadedEvent</c> at the appropriate time.
        /// </summary>
        Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Writes all <c>PersistentEntity</c>-carrying entities whose <c>LocationComponent</c>
        /// places them in one of the <paramref name="occupiedRoomIds"/> rooms, plus the player
        /// entities themselves (which also carry <c>LocationComponent</c>).
        /// Called by <c>PersistenceFlushTimer</c> on each tick.
        /// </summary>
        Task FlushActivePlayerFootprintAsync(IEnumerable<uint> occupiedRoomIds, CancellationToken ct = default);

        /// <summary>
        /// Writes every entity in the world that carries <c>PersistentEntity</c>.
        /// Used by <c>PersistenceBootstrap.StopAsync</c> for a complete shutdown sweep.
        /// </summary>
        Task FlushAllPersistentAsync(CancellationToken ct = default);
    }
}
