namespace Hedron.Core.Systems
{
    /// <summary>
    /// Cross-cutting system responsible for saving and loading entity state to/from disk.
    /// </summary>
    public interface IPersistenceSystem
    {
        /// <summary>
        /// Marks an entity as dirty; it will be included in the next <see cref="FlushAsync"/> pass.
        /// </summary>
        void MarkDirty(uint entityId);

        /// <summary>Returns <c>true</c> if the entity is currently marked dirty.</summary>
        bool IsDirty(uint entityId);

        /// <summary>
        /// Writes all dirty entities to disk. Errors on individual entities are logged and
        /// skipped (best-effort); a failed entity remains dirty and retries next flush.
        /// </summary>
        Task FlushAsync(CancellationToken ct = default);

        /// <summary>
        /// Forces an immediate flush of a single entity, bypassing the dirty flag.
        /// Marks the entity clean on success.
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
    }
}
