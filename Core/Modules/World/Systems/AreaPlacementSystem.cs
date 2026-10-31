using System.Collections.Generic;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>Default <see cref="IAreaPlacementSystem"/>, over the live template registry.</summary>
    public sealed class AreaPlacementSystem : IAreaPlacementSystem
    {
        private readonly ITemplateRegistry _registry;

        public AreaPlacementSystem(ITemplateRegistry registry)
        {
            _registry = registry;
        }

        public IReadOnlyList<PlacementFinding> Findings(string? areaBlueprintId = null)
            => AreaPlacementCheck.Sweep(new RegistryPlacementSource(_registry), areaBlueprintId);

        public PlacementFinding? JudgeRule(string roomBlueprintId, SpawnRule rule)
        {
            if (!_registry.TryGet(roomBlueprintId, out var template) || template is not RoomTemplate room)
                return null;

            var areaId = string.IsNullOrEmpty(room.AreaId) ? null : room.AreaId;
            return AreaPlacementCheck.Judge(
                new PlacementSite(roomBlueprintId, areaId, rule), new RegistryPlacementSource(_registry));
        }
    }
}
