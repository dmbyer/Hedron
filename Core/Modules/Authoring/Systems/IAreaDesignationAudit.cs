using System.Collections.Generic;
using Hedron.Core.Modules.World.Systems;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// Catalog-backed out-of-designation findings for the web host (the area editor's list and the
    /// Integrity page's category, R7/R12): <see cref="AreaPlacementCheck"/> over a
    /// <see cref="CatalogPlacementSource"/>, so it reflects disk without a reload. Stateless; returns
    /// results, publishes nothing (INV-5). The in-game counterpart is <see cref="IAreaPlacementSystem"/>.
    /// </summary>
    public interface IAreaDesignationAudit
    {
        /// <summary>Every finding on disk, or only those for rooms in <paramref name="areaBlueprintId"/>.</summary>
        IReadOnlyList<PlacementFinding> Findings(string? areaBlueprintId = null);
    }
}
