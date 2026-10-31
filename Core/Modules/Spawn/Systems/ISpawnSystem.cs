namespace Hedron.Core.Modules.Spawn.Systems
{
    /// <summary>
    /// Tracks rule-keyed spawn slots and schedules respawns for world-content mobs and found items.
    /// Slots key on <c>(room entity, rule id, ordinal)</c>; each <c>SpawnRule</c> owns
    /// <c>Count</c> slots, each with its own respawn timer. Subscribes to
    /// <c>WorldContentReadyEvent</c> (State: rebuild + immediate fill), <c>MobDiedEvent</c> /
    /// <c>ItemPickedUpEvent</c> (vacate), and <c>HeartbeatTickEvent</c> (respawn due slots).
    /// Pure domain system — no event publishing, no persistence (INV-5, INV-8).
    /// </summary>
    public interface ISpawnSystem
    {
        /// <summary>
        /// Reconciles the tracker with the room's live <c>SpawnConfigComponent</c>: a new rule
        /// spawns <c>Count</c> now; a count increase spawns the delta now; a count decrease or a
        /// removed rule drops surplus slots (vacant first), detaching any occupant (alive but
        /// untracked); an unchanged rule keeps its entities and timers; a changed delay applies
        /// from the next vacancy and a changed blueprint from the next spawn.
        /// </summary>
        SpawnSyncResult SyncRoom(uint roomEntityId);

        /// <summary>Per-rule status for the room, in the component's rule order.</summary>
        IReadOnlyList<SpawnRuleStatus> DescribeRoom(uint roomEntityId);
    }
}
