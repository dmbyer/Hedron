namespace Hedron.Core.Modules.Spawn.Systems
{
    /// <summary>
    /// Outcome of <see cref="ISpawnSystem.SyncRoom"/>: the entities spawned now, the slots added,
    /// and the occupied slots dropped (their entities detached — alive but untracked).
    /// </summary>
    public sealed record SpawnSyncResult(
        IReadOnlyList<uint> SpawnedEntityIds,
        int SlotsAdded,
        int SlotsDetached)
    {
        public static SpawnSyncResult None { get; } = new(Array.Empty<uint>(), 0, 0);
    }
}
