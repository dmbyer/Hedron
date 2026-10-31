using System.Collections.Generic;
using System.Linq;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.World.Systems
{
    /// <summary>
    /// <see cref="IAreaPlacementSource"/> over the <see cref="ITemplateRegistry"/> — the in-game
    /// source, since admin verbs mutate the registered templates (INV-21). Area membership is the
    /// room's authored <see cref="RoomTemplate.AreaId"/>, the same key the catalog source uses.
    /// </summary>
    public sealed class RegistryPlacementSource : IAreaPlacementSource
    {
        private readonly ITemplateRegistry _registry;

        public RegistryPlacementSource(ITemplateRegistry registry)
        {
            _registry = registry;
        }

        public IEnumerable<PlacementSite> Sites()
        {
            foreach (var room in _registry.AllBlueprintIds()
                .Select(id => _registry.TryGet(id, out var template) ? template : null)
                .OfType<RoomTemplate>())
            {
                var areaId = string.IsNullOrEmpty(room.AreaId) ? null : room.AreaId;
                foreach (var rule in room.SpawnRules)
                    yield return new PlacementSite(room.BlueprintId, areaId, rule);
            }
        }

        public AreaDesignation? DesignationOf(string areaBlueprintId)
            => _registry.TryGet(areaBlueprintId, out var template) && template is AreaTemplate area
                ? area.Designation
                : null;

        public PlacedTag? TagOf(string blueprintId)
        {
            if (!_registry.TryGet(blueprintId, out var template))
                return null;

            return template switch
            {
                MobTemplate mob => new PlacedTag(PlacedKind.Mob, mob.Tier, mob.Band),
                ItemTemplate item => new PlacedTag(PlacedKind.Item, item.Tier, item.Band),
                _ => null,
            };
        }
    }
}
