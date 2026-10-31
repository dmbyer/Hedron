using System;
using System.Collections.Generic;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Components;
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

        public AreaPropertyResult SetAreaName(string areaBlueprintId, string name)
            => Mutate(areaBlueprintId, (template, liveId) =>
            {
                template.Name = name;
                if (liveId != 0 && _entityService.TryGet<AreaComponent>(liveId, out var area))
                    area.Name = name;
            });

        public AreaPropertyResult SetAreaDescription(string areaBlueprintId, string description)
            => Mutate(areaBlueprintId, (template, liveId) =>
            {
                template.Description = description;
                if (liveId != 0 && _entityService.TryGet<AreaComponent>(liveId, out var area))
                    area.Description = description;
            });

        public AreaPropertyResult SetAreaDesignation(string areaBlueprintId, AreaDesignation? designation)
        {
            if (designation is not null)
            {
                var errors = AreaDesignationValidation.Errors(areaBlueprintId, designation);
                if (errors.Count > 0)
                    return AreaPropertyResult.Refused(areaBlueprintId, string.Join(" ", errors));
            }

            return Mutate(areaBlueprintId, (template, liveId) =>
            {
                template.Designation = designation;
                if (liveId == 0)
                    return;

                if (designation is null)
                    _entityService.RemoveComponent<AreaDesignationComponent>(liveId);
                else if (_entityService.TryGet<AreaDesignationComponent>(liveId, out var component))
                    component.Designation = designation;
                else
                    _entityService.AddComponent(liveId, new AreaDesignationComponent { Designation = designation });
            });
        }

        /// <summary>
        /// Resolves the registered <see cref="AreaTemplate"/> (refusing an unknown one) and the live
        /// area entity, if spawned, then applies <paramref name="apply"/> to both (INV-21).
        /// </summary>
        private AreaPropertyResult Mutate(string areaBlueprintId, Action<AreaTemplate, uint> apply)
        {
            if (!_templateRegistry.TryGet(areaBlueprintId, out var raw) || raw is not AreaTemplate template)
                return AreaPropertyResult.Refused(areaBlueprintId, $"No area template found with blueprint id '{areaBlueprintId}'.");

            var liveId = FindLiveArea(template.BlueprintId);
            apply(template, liveId);

            _logger.LogDebug("AreaBuilderSystem: mutated area blueprint={BlueprintId} entity={EntityId}", template.BlueprintId, liveId);
            return new AreaPropertyResult(true, null, template.BlueprintId, liveId, template);
        }

        // BlueprintComponent scan — the acknowledged "Blueprint-scan index" backlog debt.
        private uint FindLiveArea(string blueprintId)
        {
            foreach (var (entityId, bp) in _entityService.GetAllComponents<BlueprintComponent>())
            {
                if (string.Equals(bp.BlueprintId, blueprintId, StringComparison.Ordinal) &&
                    _entityService.HasComponent<AreaComponent>(entityId))
                    return entityId;
            }
            return 0;
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
