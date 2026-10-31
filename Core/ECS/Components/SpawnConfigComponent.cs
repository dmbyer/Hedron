using System.Collections.Generic;

namespace Hedron.Core.ECS.Components
{
    /// <summary>
    /// One entry in a room's spawn configuration. <see cref="Id"/> is the rule's identity,
    /// unique (case-insensitive) within the room; it keys the rule's <see cref="Count"/> slots,
    /// each of which respawns on its own timer. Shape checks live in
    /// <see cref="SpawnRuleValidation"/>; id helpers in <see cref="SpawnRuleIds"/>.
    /// </summary>
    public sealed record SpawnRule(
        string Id,
        string BlueprintId,
        int Count,
        int RespawnDelaySeconds);

    /// <summary>
    /// Attached to room entities to declare their spawn rules. One component holds all
    /// rules for the entity. Not persisted — the YAML template is the authoritative source.
    /// </summary>
    public sealed class SpawnConfigComponent : IComponent
    {
        public List<SpawnRule> Rules { get; } = new();
    }
}
