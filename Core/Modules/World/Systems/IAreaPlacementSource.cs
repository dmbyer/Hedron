using System.Collections.Generic;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>What a spawn rule places, for the out-of-designation check.</summary>
    public enum PlacedKind { Mob, Item }

    /// <summary>One room spawn rule, with the room's area (<c>null</c> when the room has none).</summary>
    public readonly record struct PlacementSite(string RoomBlueprintId, string? AreaBlueprintId, SpawnRule Rule);

    /// <summary>A placed blueprint's kind and authored (Tier, Band). <c>Band == 0</c> means unbanded.</summary>
    public readonly record struct PlacedTag(PlacedKind Kind, int Tier, int Band);

    /// <summary>
    /// The content the out-of-designation check reads. One seam, two sources (resolved decision 6):
    /// <see cref="RegistryPlacementSource"/> for in-game surfaces (the template registry admin verbs
    /// mutate) and the Authoring catalog source for the web host (what the editors write on disk).
    /// The judgement itself (<see cref="AreaPlacementCheck"/>) is identical over either.
    /// </summary>
    public interface IAreaPlacementSource
    {
        /// <summary>Every (room, area, rule) placement.</summary>
        IEnumerable<PlacementSite> Sites();

        /// <summary>The area's designation; <c>null</c> when undesignated or the area is unknown.</summary>
        AreaDesignation? DesignationOf(string areaBlueprintId);

        /// <summary>The placed blueprint's tag; <c>null</c> when unknown or not a mob/item template.</summary>
        PlacedTag? TagOf(string blueprintId);
    }
}
