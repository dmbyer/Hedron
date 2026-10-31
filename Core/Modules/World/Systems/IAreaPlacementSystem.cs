using System.Collections.Generic;
using Hedron.Core.ECS.Components;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// Registry-backed out-of-designation queries for in-game surfaces (the <c>area</c> verb,
    /// spawn-rule authoring). Domain-tier and stateless: it wraps <see cref="AreaPlacementCheck"/>
    /// over a <see cref="RegistryPlacementSource"/>, returns results, publishes nothing (INV-5).
    /// </summary>
    public interface IAreaPlacementSystem
    {
        /// <summary>Every finding, or only those for rooms in <paramref name="areaBlueprintId"/>.</summary>
        IReadOnlyList<PlacementFinding> Findings(string? areaBlueprintId = null);

        /// <summary>
        /// Judges one rule as placed in <paramref name="roomBlueprintId"/> (the room's registered
        /// area). <c>null</c> when in designation, unchecked, or the room is unknown.
        /// </summary>
        PlacementFinding? JudgeRule(string roomBlueprintId, SpawnRule rule);
    }
}
