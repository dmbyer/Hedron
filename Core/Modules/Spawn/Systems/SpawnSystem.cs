using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Time.Events;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;

namespace Hedron.Core.Modules.Spawn.Systems
{
    /// <summary>
    /// Tracks which spawn slots are occupied and schedules respawns for mobs and world-spawn
    /// items after they are removed from their room. Initializes from the live entity graph on
    /// <c>WorldContentReadyEvent</c>; reacts to <c>MobDiedEvent</c> and <c>ItemPickedUpEvent</c>
    /// to mark slots vacant; respawns on <c>HeartbeatTickEvent</c>.
    /// </summary>
    public sealed class SpawnSystem :
        ISpawnSystem,
        IEventHandler<WorldContentReadyEvent>,
        IEventHandler<MobDiedEvent>,
        IEventHandler<ItemPickedUpEvent>,
        IEventHandler<HeartbeatTickEvent>
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly ILogger<SpawnSystem> _logger;

        // (roomEntityId, blueprintId) → slot state
        private readonly Dictionary<(uint, string), SlotState> _slots = new();
        // entityId → slot key (reverse lookup for O(1) vacancy marking)
        private readonly Dictionary<uint, (uint, string)> _entityToSlot = new();

        int IEventHandler<WorldContentReadyEvent>.Priority => HandlerPriority.Notification;
        int IEventHandler<MobDiedEvent>.Priority => HandlerPriority.Domain;
        int IEventHandler<ItemPickedUpEvent>.Priority => HandlerPriority.Domain;
        int IEventHandler<HeartbeatTickEvent>.Priority => HandlerPriority.Ai;

        public SpawnSystem(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            ILogger<SpawnSystem> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _logger = logger;
        }

        /// <summary>
        /// Scans all rooms with <see cref="SpawnConfigComponent"/> and registers their current
        /// live entities in the slot tracker.
        /// </summary>
        public Task HandleAsync(WorldContentReadyEvent @event)
        {
            foreach (var (roomEntityId, spawnConfig) in _entityService.GetAllComponents<SpawnConfigComponent>())
            {
                foreach (var rule in spawnConfig.Rules)
                {
                    var slotKey = (roomEntityId, rule.BlueprintId);
                    var slot = new SlotState(roomEntityId, rule.BlueprintId, rule.RespawnDelaySeconds);

                    if (_slots.ContainsKey(slotKey))
                    {
                        _logger.LogWarning(
                            "SpawnSystem: duplicate spawn rule for blueprint '{Blueprint}' in room {RoomId} — later rule ignored.",
                            rule.BlueprintId, roomEntityId);
                        continue;
                    }

                    _slots[slotKey] = slot;

                    // Find a live entity in this room with this blueprint.
                    uint? foundEntityId = null;
                    foreach (var (entityId, blueprint) in _entityService.GetAllComponents<BlueprintComponent>())
                    {
                        if (!string.Equals(blueprint.BlueprintId, rule.BlueprintId, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!_entityService.TryGet<LocationComponent>(entityId, out var loc))
                            continue;
                        if (loc.RoomEntityId != roomEntityId)
                            continue;
                        foundEntityId = entityId;
                        break;
                    }

                    if (foundEntityId.HasValue)
                    {
                        slot.LiveEntityId = foundEntityId.Value;
                        _entityToSlot[foundEntityId.Value] = slotKey;
                    }
                    else
                    {
                        // Slot was vacant at startup (e.g. mob killed before last restart).
                        slot.RespawnAt = DateTime.UtcNow + TimeSpan.FromSeconds(rule.RespawnDelaySeconds);
                    }
                }
            }

            _logger.LogInformation(
                "SpawnSystem: initialized {SlotCount} spawn slots.", _slots.Count);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Marks the mob's spawn slot vacant and schedules a respawn.
        /// Must run before <c>EntityService.DestroyEntity</c> is called.
        /// </summary>
        public Task HandleAsync(MobDiedEvent @event)
        {
            MarkVacant(@event.MobEntityId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Marks the item's spawn slot vacant. The item entity itself is promoted to persistent
        /// by <c>ItemContextHandler</c>; the slot will respawn a fresh world-spawn instance.
        /// </summary>
        public Task HandleAsync(ItemPickedUpEvent @event)
        {
            MarkVacant(@event.ItemEntityId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Checks all pending respawn timers; spawns fresh entities for slots whose delay has elapsed.
        /// </summary>
        public Task HandleAsync(HeartbeatTickEvent @event)
        {
            var now = DateTime.UtcNow;
            foreach (var slot in _slots.Values)
            {
                if (slot.LiveEntityId.HasValue || slot.RespawnAt is null || slot.RespawnAt > now)
                    continue;

                TryRespawn(slot);
            }
            return Task.CompletedTask;
        }

        private void MarkVacant(uint entityId)
        {
            if (!_entityToSlot.TryGetValue(entityId, out var slotKey))
                return;

            _entityToSlot.Remove(entityId);

            if (!_slots.TryGetValue(slotKey, out var slot))
                return;

            slot.LiveEntityId = null;
            slot.RespawnAt = DateTime.UtcNow + TimeSpan.FromSeconds(slot.RespawnDelaySeconds);
        }

        private void TryRespawn(SlotState slot)
        {
            if (!_templateRegistry.TryGet(slot.BlueprintId, out _))
            {
                _logger.LogWarning(
                    "SpawnSystem: cannot respawn '{Blueprint}' — template not found; slot disabled.",
                    slot.BlueprintId);
                slot.RespawnAt = null;
                return;
            }

            // Resolve the room's blueprint ID for LocationComponent.RoomBlueprintId.
            string? roomBlueprintId = null;
            if (_entityService.TryGet<BlueprintComponent>(slot.RoomEntityId, out var roomBp))
                roomBlueprintId = roomBp.BlueprintId;

            var entity = _templateRegistry.Spawn(slot.BlueprintId);
            _entityService.AddComponent(entity.Id, new LocationComponent
            {
                RoomEntityId = slot.RoomEntityId,
                RoomBlueprintId = roomBlueprintId,
            });

            slot.LiveEntityId = entity.Id;
            slot.RespawnAt = null;
            _entityToSlot[entity.Id] = (slot.RoomEntityId, slot.BlueprintId);

            _logger.LogDebug(
                "SpawnSystem: respawned '{Blueprint}' (entity {EntityId}) in room {RoomId}.",
                slot.BlueprintId, entity.Id, slot.RoomEntityId);
        }

        private sealed class SlotState
        {
            public uint RoomEntityId { get; }
            public string BlueprintId { get; }
            public int RespawnDelaySeconds { get; }
            public uint? LiveEntityId { get; set; }
            public DateTime? RespawnAt { get; set; }

            public SlotState(uint roomEntityId, string blueprintId, int respawnDelaySeconds)
            {
                RoomEntityId = roomEntityId;
                BlueprintId = blueprintId;
                RespawnDelaySeconds = respawnDelaySeconds;
            }
        }
    }
}
