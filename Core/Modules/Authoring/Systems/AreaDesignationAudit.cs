using System.Collections.Generic;
using Hedron.Core.Modules.World.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Authoring.Systems
{
    /// <summary>Default <see cref="IAreaDesignationAudit"/>, over the content catalog.</summary>
    public sealed class AreaDesignationAudit : IAreaDesignationAudit
    {
        private readonly IContentDefinitionCatalog _catalog;
        private readonly ISpawnRuleAuthoring _spawnRules;
        private readonly ILogger<AreaDesignationAudit> _logger;

        public AreaDesignationAudit(
            IContentDefinitionCatalog catalog, ISpawnRuleAuthoring spawnRules, ILogger<AreaDesignationAudit> logger)
        {
            _catalog = catalog;
            _spawnRules = spawnRules;
            _logger = logger;
        }

        public IReadOnlyList<PlacementFinding> Findings(string? areaBlueprintId = null)
            => AreaPlacementCheck.Sweep(new CatalogPlacementSource(_catalog, _spawnRules, _logger), areaBlueprintId);
    }
}
