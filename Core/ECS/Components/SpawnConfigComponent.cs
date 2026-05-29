using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// One entry in a room/area's spawn configuration — describes a single respawnable slot.
    /// </summary>
    public sealed record SpawnRule(
        string BlueprintId,
        int MinCount,
        int MaxCount,
        int RespawnDelaySeconds);

    /// <summary>
    /// Attached to room/area entities to declare their spawn rules. One component holds all
    /// rules for the entity; each rule is an independent respawn slot. Not persisted — the
    /// YAML template is the authoritative source.
    /// </summary>
    public sealed class SpawnConfigComponent : IComponent
    {
        public List<SpawnRule> Rules { get; } = new();
    }
}
