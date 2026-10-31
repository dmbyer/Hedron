using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Spawn.Systems;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Admin.Systems
{
    /// <summary>
    /// Implements runtime room authoring: creation, exit wiring, and property mutation.
    /// Commands are thin orchestrators; this system holds the domain logic so a future
    /// in-game editor can reuse the same operations without a live player session.
    /// </summary>
    public sealed class RoomBuilderSystem : IRoomBuilderSystem
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IAreaSystem _areaSystem;
        private readonly ISpawnSystem _spawnSystem;
        private readonly ILogger<RoomBuilderSystem> _logger;

        public RoomBuilderSystem(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IAreaSystem areaSystem,
            ISpawnSystem spawnSystem,
            ILogger<RoomBuilderSystem> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _areaSystem = areaSystem;
            _spawnSystem = spawnSystem;
            _logger = logger;
        }

        public RoomCreationResult CreateRoom(string name, string description = "", string areaId = "")
        {
            var blueprintId = GenerateUniqueBlueprintId();

            var entity = _entityService.CreateEntity();
            _entityService.AddComponent(entity.Id, new RoomComponent { Name = name, Description = description });
            _entityService.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = blueprintId });

            var template = new RoomTemplate(blueprintId) { Name = name, Description = description };
            if (!string.IsNullOrEmpty(areaId))
                template.AreaId = areaId;
            _templateRegistry.Register(blueprintId, template);

            if (!string.IsNullOrEmpty(areaId))
            {
                // Find the area entity by its blueprint ID.
                uint areaEntityId = 0;
                foreach (var (id, bp) in _entityService.GetAllComponents<BlueprintComponent>())
                {
                    if (string.Equals(bp.BlueprintId, areaId, StringComparison.OrdinalIgnoreCase))
                    { areaEntityId = id; break; }
                }
                if (areaEntityId != 0)
                    _areaSystem.AssignRoomToArea(entity.Id, areaEntityId, areaId);
            }

            _logger.LogDebug("RoomBuilderSystem: created room entity={EntityId} blueprint={BlueprintId}", entity.Id, blueprintId);
            return new RoomCreationResult(entity.Id, blueprintId);
        }

        public void LinkExits(uint sourceRoomId, Direction direction, uint targetRoomId, bool bidirectional)
        {
            if (_entityService.TryGet<RoomComponent>(sourceRoomId, out var sourceRoom))
                sourceRoom.Exits[direction] = targetRoomId;

            MirrorExitToTemplate(sourceRoomId, direction, targetRoomId);

            if (bidirectional)
            {
                var opposite = Opposite(direction);
                if (opposite is not null && _entityService.TryGet<RoomComponent>(targetRoomId, out var targetRoom))
                    targetRoom.Exits[opposite.Value] = sourceRoomId;

                if (opposite is not null)
                    MirrorExitToTemplate(targetRoomId, opposite.Value, sourceRoomId);
            }
        }

        public void SetRoomName(uint roomId, string name)
        {
            if (_entityService.TryGet<RoomComponent>(roomId, out var room))
                room.Name = name;
            var tpl = TryGetTemplate(roomId);
            if (tpl is not null) tpl.Name = name;
        }

        public void SetRoomDescription(uint roomId, string description)
        {
            if (_entityService.TryGet<RoomComponent>(roomId, out var room))
                room.Description = description;
            var tpl = TryGetTemplate(roomId);
            if (tpl is not null) tpl.Description = description;
        }

        public bool TryGetAuthorableRoom(uint roomId, out string roomBlueprintId)
        {
            var ok = TryResolveRoom(roomId, out var template, out _);
            roomBlueprintId = template?.BlueprintId ?? string.Empty;
            return ok;
        }

        public SpawnRuleMutationResult AddSpawnRule(
            uint roomId, string blueprintId, int count, int respawnDelaySeconds, string? ruleId = null)
        {
            if (!TryResolveRoom(roomId, out var template, out var refusal))
                return refusal;
            if (CheckSpawnable(blueprintId, template.BlueprintId) is { } blueprintRefusal)
                return blueprintRefusal;

            var id = string.IsNullOrWhiteSpace(ruleId)
                ? SpawnRuleIds.Mint(template.SpawnRules.Select(r => r.Id), blueprintId)
                : ruleId.Trim();
            var rule = new SpawnRule(id, blueprintId, count, respawnDelaySeconds);

            var candidate = new List<SpawnRule>(template.SpawnRules) { rule };
            return Commit(roomId, template, candidate, rule);
        }

        public SpawnRuleMutationResult UpdateSpawnRule(uint roomId, string ruleId, SpawnRuleUpdate update)
        {
            if (!TryResolveRoom(roomId, out var template, out var refusal))
                return refusal;

            var index = IndexOfRule(template, ruleId);
            if (index < 0)
                return SpawnRuleMutationResult.Refused(
                    $"Room '{template.BlueprintId}' has no spawn rule '{ruleId}'.", template.BlueprintId);

            var current = template.SpawnRules[index];
            if (update.BlueprintId is { } newBlueprint &&
                CheckSpawnable(newBlueprint, template.BlueprintId) is { } blueprintRefusal)
                return blueprintRefusal;

            var rule = current with
            {
                BlueprintId = update.BlueprintId ?? current.BlueprintId,
                Count = update.Count ?? current.Count,
                RespawnDelaySeconds = update.RespawnDelaySeconds ?? current.RespawnDelaySeconds,
            };

            var candidate = new List<SpawnRule>(template.SpawnRules) { [index] = rule };
            return Commit(roomId, template, candidate, rule);
        }

        public SpawnRuleMutationResult RemoveSpawnRule(uint roomId, string ruleId)
        {
            if (!TryResolveRoom(roomId, out var template, out var refusal))
                return refusal;

            var index = IndexOfRule(template, ruleId);
            if (index < 0)
                return SpawnRuleMutationResult.Refused(
                    $"Room '{template.BlueprintId}' has no spawn rule '{ruleId}'.", template.BlueprintId);

            var removed = template.SpawnRules[index];
            var candidate = new List<SpawnRule>(template.SpawnRules);
            candidate.RemoveAt(index);
            return Commit(roomId, template, candidate, removed);
        }

        /// <summary>
        /// Validates the candidate rule set; on success writes it to the template and the live
        /// component (the component is removed with the last rule) and syncs the tracker.
        /// </summary>
        private SpawnRuleMutationResult Commit(uint roomId, RoomTemplate template, List<SpawnRule> candidate, SpawnRule rule)
        {
            var errors = SpawnRuleValidation.Errors(template.BlueprintId, candidate);
            if (errors.Count > 0)
                return SpawnRuleMutationResult.Refused(string.Join(" ", errors), template.BlueprintId);

            template.SpawnRules.Clear();
            template.SpawnRules.AddRange(candidate);

            if (candidate.Count == 0)
                _entityService.RemoveComponent<SpawnConfigComponent>(roomId);
            else
            {
                if (!_entityService.TryGet<SpawnConfigComponent>(roomId, out var config))
                {
                    config = new SpawnConfigComponent();
                    _entityService.AddComponent(roomId, config);
                }
                config.Rules.Clear();
                config.Rules.AddRange(candidate);
            }

            var sync = _spawnSystem.SyncRoom(roomId);
            _logger.LogDebug(
                "RoomBuilderSystem: room={RoomBlueprintId} spawn rule '{RuleId}' committed; spawned={Spawned} detached={Detached}",
                template.BlueprintId, rule.Id, sync.SpawnedEntityIds.Count, sync.SlotsDetached);
            return new SpawnRuleMutationResult(true, null, rule, template.BlueprintId, sync);
        }

        private bool TryResolveRoom(uint roomId, [NotNullWhen(true)] out RoomTemplate? template, out SpawnRuleMutationResult refusal)
        {
            template = _entityService.HasComponent<RoomComponent>(roomId) ? TryGetTemplate(roomId) : null;
            refusal = template is null
                ? SpawnRuleMutationResult.Refused("You must be standing in a world room with a registered room template.")
                : null!;
            return template is not null;
        }

        /// <summary>
        /// Refuses an unknown blueprint, or one naming world structure (a room or area template)
        /// rather than a mob or item.
        /// </summary>
        private SpawnRuleMutationResult? CheckSpawnable(string blueprintId, string roomBlueprintId)
        {
            if (string.IsNullOrWhiteSpace(blueprintId) || !_templateRegistry.TryGet(blueprintId, out var target))
                return SpawnRuleMutationResult.Refused($"No template found with blueprint id '{blueprintId}'.", roomBlueprintId);
            if (target is RoomTemplate or AreaTemplate)
                return SpawnRuleMutationResult.Refused(
                    $"'{blueprintId}' is a room or area template; spawn rules name a mob or item.", roomBlueprintId);
            return null;
        }

        private static int IndexOfRule(RoomTemplate template, string ruleId) =>
            template.SpawnRules.FindIndex(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));

        private string GenerateUniqueBlueprintId()
        {
            const int maxAttempts = 10;
            for (var i = 0; i < maxAttempts; i++)
            {
                var id = "room.adhoc." + ToBase36(Guid.NewGuid())[..8];
                if (!_templateRegistry.TryGet(id, out _))
                    return id;
            }
            // Fallback: use full guid suffix — collision-free by construction
            return "room.adhoc." + Guid.NewGuid().ToString("N")[..16];
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

        private void MirrorExitToTemplate(uint roomId, Direction direction, uint targetRoomId)
        {
            if (!_entityService.TryGet<BlueprintComponent>(roomId, out var sourceBp)) return;
            if (!_entityService.TryGet<BlueprintComponent>(targetRoomId, out var targetBp)) return;
            if (!_templateRegistry.TryGet(sourceBp.BlueprintId, out var template)) return;
            if (template is RoomTemplate roomTemplate)
                roomTemplate.Exits[direction] = targetBp.BlueprintId;
        }

        private RoomTemplate? TryGetTemplate(uint roomId)
        {
            if (_entityService.TryGet<BlueprintComponent>(roomId, out var bp) &&
                _templateRegistry.TryGet(bp.BlueprintId, out var template) &&
                template is RoomTemplate roomTemplate)
                return roomTemplate;
            return null;
        }

        private static Direction? Opposite(Direction d) => d switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East  => Direction.West,
            Direction.West  => Direction.East,
            Direction.Up    => Direction.Down,
            Direction.Down  => Direction.Up,
            _               => null,
        };
    }
}
