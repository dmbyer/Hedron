using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.Authoring
{
    /// <summary>
    /// One place a mob or item spawns: a room spawn rule that names it. Produced by
    /// <see cref="Systems.ISpawnRuleAuthoring.SpawnSitesFor"/>.
    /// </summary>
    /// <param name="RoomBlueprintId">The room whose <c>spawnRules</c> carry the rule.</param>
    /// <param name="AreaBlueprintId">That room's area, or <c>null</c> when it has none.</param>
    /// <param name="Rule">The rule itself (id, blueprint, count, delay).</param>
    public sealed record SpawnSite(string RoomBlueprintId, string? AreaBlueprintId, SpawnRule Rule);
}
