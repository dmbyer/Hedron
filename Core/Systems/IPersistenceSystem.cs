using System.Collections.Generic;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Cross-cutting system responsible for saving and loading entity state to/from SQLite.
    /// Implements the two-level persistence model: <c>PersistentEntity</c> opts an entity in;
    /// <c>[Persistent]</c> on a component type controls which components are included in the snapshot.
    /// </summary>
    public interface IPersistenceSystem
    {
        /// <summary>
        /// Immediately saves one entity to SQLite. Called at entity construction time only
        /// (admin content creation, account/character creation). Not used for runtime mutations —
        /// those are covered by the periodic flush.
        /// No-ops silently if the entity does not carry <c>PersistentEntity</c>.
        /// </summary>
        Task SaveEntityAsync(uint entityId, CancellationToken ct = default);

        /// <summary>
        /// Writes every entity in the world that carries <c>PersistentEntity</c>.
        /// Called by <c>PersistenceBootstrap.StopAsync</c> for a complete shutdown sweep.
        /// </summary>
        Task FlushAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Writes every entity in the world that carries <c>PersistentEntity</c>.
        /// Called by <c>PersistenceFlushTimer</c> on each tick. "Dirty" is implicit —
        /// all persistent entities are always flushed; no property-level tracking is performed.
        /// </summary>
        Task FlushDirtyAsync(CancellationToken ct = default);

        /// <summary>
        /// Loads all persisted entities from SQLite, restoring them into <c>EntityService</c>.
        /// No event-bus events are published — component attachment is silent.
        /// Returns the IDs of every entity that was successfully restored so that the calling
        /// orchestrator (<c>PersistenceBootstrap</c>) can publish <c>EntityHydratedEvent</c>
        /// and <c>WorldLoadedEvent</c> at the appropriate time.
        /// </summary>
        Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default);
    }
}
