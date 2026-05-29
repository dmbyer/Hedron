namespace Hedron.Core.Modules.Spawn.Systems
{
    /// <summary>
    /// Tracks spawn slot occupancy and schedules respawns for world-content entities (mobs,
    /// world-spawn items). Subscribes to <c>WorldContentReadyEvent</c>, <c>MobDiedEvent</c>,
    /// <c>ItemPickedUpEvent</c>, and <c>HeartbeatTickEvent</c>; no external API for Stage C.
    /// Pure domain system — no event publishing, no persistence (INV-5, INV-8).
    /// </summary>
    public interface ISpawnSystem
    {
    }
}
