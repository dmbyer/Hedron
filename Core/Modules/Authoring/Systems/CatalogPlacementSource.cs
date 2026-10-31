using System;
using System.Collections.Generic;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>
    /// <see cref="IAreaPlacementSource"/> over the content catalog — the web-host source, reading
    /// what the editors write on disk (resolved decision 6). Built per sweep: its area and target
    /// memo is local to one sweep, so it is confined to the calling thread (INV-31).
    /// </summary>
    /// <remarks>
    /// <see cref="IContentDefinitionCatalog.Load"/> deserializes without a catch, so a hand-edited
    /// malformed area, mob or item file would otherwise throw out of a sweep — blanking the whole
    /// Integrity page or failing a rule save after its write. An unreadable file is logged and
    /// treated as unknown (<c>null</c>), which <see cref="AreaPlacementCheck.Judge"/> already reads
    /// as "no finding".
    /// </remarks>
    public sealed class CatalogPlacementSource : IAreaPlacementSource
    {
        private readonly IContentDefinitionCatalog _catalog;
        private readonly ISpawnRuleAuthoring _spawnRules;
        private readonly ILogger _logger;
        private readonly Dictionary<string, AreaDesignation?> _designations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PlacedTag?> _tags = new(StringComparer.Ordinal);

        public CatalogPlacementSource(IContentDefinitionCatalog catalog, ISpawnRuleAuthoring spawnRules, ILogger logger)
        {
            _catalog = catalog;
            _spawnRules = spawnRules;
            _logger = logger;
        }

        public IEnumerable<PlacementSite> Sites()
        {
            foreach (var site in _spawnRules.AllSpawnSites())
                yield return new PlacementSite(
                    site.RoomBlueprintId,
                    string.IsNullOrEmpty(site.AreaBlueprintId) ? null : site.AreaBlueprintId,
                    site.Rule);
        }

        public AreaDesignation? DesignationOf(string areaBlueprintId)
        {
            if (_designations.TryGetValue(areaBlueprintId, out var cached))
                return cached;

            var designation = (TryLoad(ContentKind.Area, areaBlueprintId) as AreaTemplate)?.Designation;
            _designations[areaBlueprintId] = designation;
            return designation;
        }

        public PlacedTag? TagOf(string blueprintId)
        {
            if (_tags.TryGetValue(blueprintId, out var cached))
                return cached;

            PlacedTag? tag = TryLoad(ContentKind.Mob, blueprintId) is MobTemplate mob
                ? new PlacedTag(PlacedKind.Mob, mob.Tier, mob.Band)
                : TryLoad(ContentKind.Item, blueprintId) is ItemTemplate item
                    ? new PlacedTag(PlacedKind.Item, item.Tier, item.Band)
                    : null;
            _tags[blueprintId] = tag;
            return tag;
        }

        private Hedron.Core.Systems.IEntityTemplate? TryLoad(ContentKind kind, string blueprintId)
        {
            try
            {
                return _catalog.Load(kind, blueprintId)?.Template;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CatalogPlacementSource: could not read {Kind} '{BlueprintId}'; treating it as unknown for the designation check.",
                    kind, blueprintId);
                return null;
            }
        }
    }
}
