using System;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Implements runtime area authoring: creation of area entities.
    /// Commands are thin orchestrators; this system holds the domain logic so a future
    /// in-game editor can reuse the same operations without a live player session.
    /// </summary>
    public sealed class AreaBuilderSystem : IAreaBuilderSystem
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly ILogger<AreaBuilderSystem> _logger;

        public AreaBuilderSystem(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            ILogger<AreaBuilderSystem> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _logger = logger;
        }

        public AreaCreationResult CreateArea(string name)
        {
            var blueprintId = GenerateUniqueBlueprintId();

            var entity = _entityService.CreateEntity();
            _entityService.AddComponent(entity.Id, new AreaComponent { Name = name, Description = "" });
            _entityService.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = blueprintId });

            var template = new AreaTemplate(blueprintId) { Name = name };
            _templateRegistry.Register(blueprintId, template);

            _logger.LogDebug("AreaBuilderSystem: created area entity={EntityId} blueprint={BlueprintId}", entity.Id, blueprintId);
            return new AreaCreationResult(entity.Id, blueprintId, template);
        }

        private string GenerateUniqueBlueprintId()
        {
            const int maxAttempts = 10;
            for (var i = 0; i < maxAttempts; i++)
            {
                var id = "area.adhoc." + ToBase36(Guid.NewGuid())[..8];
                if (!_templateRegistry.TryGet(id, out _))
                    return id;
            }
            // Fallback: use full guid suffix — collision-free by construction
            return "area.adhoc." + Guid.NewGuid().ToString("N")[..16];
        }

        private static string ToBase36(Guid guid)
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
            var bytes = guid.ToByteArray();
            var value = Math.Abs(BitConverter.ToInt64(bytes, 0));
            if (value == 0) return "0";
            var result = new System.Text.StringBuilder();
            while (value > 0)
            {
                result.Insert(0, chars[(int)(value % 36)]);
                value /= 36;
            }
            return result.ToString();
        }
    }
}
