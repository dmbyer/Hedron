using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Rule-keyed spawn slot tracker. Each room <see cref="SpawnRule"/> owns <c>Count</c> slots
    /// keyed by <c>(room entity, rule id, ordinal)</c>; each slot respawns on its own timer.
    /// Room spawn rules are the single spawn truth for world mobs and found items: the loader
    /// places none, and this system fills every slot on <c>WorldContentReadyEvent</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Boot fill at State.</b> <c>WorldContentReadyEvent</c> is handled at
    /// <see cref="HandlerPriority.State"/> (10) so rule-placed shopkeepers exist before
    /// <c>ShopkeeperSpawnHandler</c> (Domain, 20) seeds tills and base stock in the same fan-out.
    /// Existing same-blueprint entities (e.g. player-owned copies) are never claimed: every slot
    /// occupant is one this system spawned.
    /// </para>
    /// <para>
    /// <b>Concurrency (INV-31): guarded.</b> The tracker is reached from the heartbeat thread
    /// (tick; <c>MobDiedEvent</c> from combat) and session threads (<c>ItemPickedUpEvent</c>,
    /// reload's <c>WorldContentReadyEvent</c>, <see cref="SyncRoom"/> via admin verbs). Every entry
    /// point takes <see cref="_gate"/>; nothing inside re-enters this system, and
    /// <c>ITemplateRegistry.Spawn</c> publishes nothing. A due slot whose room entity has lost its
    /// <see cref="RoomComponent"/> (torn down mid-reload) is skipped, so a tick landing after the
    /// room is destroyed spawns no orphan.
    /// </para>
    /// </remarks>
    public sealed class SpawnSystem :
        ISpawnSystem,
        IEventHandler<WorldContentReadyEvent>,
        IEventHandler<MobDiedEvent>,
        IEventHandler<ItemPickedUpEvent>,
        IEventHandler<HeartbeatTickEvent>
    {
        private readonly EntityService _entityService;
        private readonly ITemplateRegistry _templateRegistry;
        private readonly IClock _clock;
        private readonly ILogger<SpawnSystem> _logger;

        private readonly object _gate = new();
        // roomEntityId → that room's rules, in component order.
        private readonly Dictionary<uint, List<RuleState>> _rooms = new();
        // entityId → occupied slot (reverse lookup for O(1) vacancy marking).
        private readonly Dictionary<uint, (RuleState Rule, Slot Slot)> _entityToSlot = new();

        int IEventHandler<WorldContentReadyEvent>.Priority => HandlerPriority.State;
        int IEventHandler<MobDiedEvent>.Priority => HandlerPriority.Domain;
        int IEventHandler<ItemPickedUpEvent>.Priority => HandlerPriority.Domain;
        int IEventHandler<HeartbeatTickEvent>.Priority => HandlerPriority.Ai;

        public SpawnSystem(
            EntityService entityService,
            ITemplateRegistry templateRegistry,
            IClock clock,
            ILogger<SpawnSystem> logger)
        {
            _entityService = entityService;
            _templateRegistry = templateRegistry;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>
        /// Rebuilds the tracker from every room's <see cref="SpawnConfigComponent"/> and
        /// immediately fills every slot.
        /// </summary>
        /// <remarks>
        /// Reload-safe: <c>WorldContentReadyEvent</c> is re-published after a successful
        /// <c>reload</c>, which has already destroyed every world entity. The prior tracker is
        /// cleared first so it never references destroyed entities.
        /// </remarks>
        public Task HandleAsync(WorldContentReadyEvent @event)
        {
            lock (_gate)
            {
                _rooms.Clear();
                _entityToSlot.Clear();

                int slots = 0;
                foreach (var (roomEntityId, spawnConfig) in _entityService.GetAllComponents<SpawnConfigComponent>().ToList())
                {
                    var states = new List<RuleState>();
                    foreach (var rule in DistinctRules(roomEntityId, spawnConfig))
                    {
                        var state = new RuleState(roomEntityId, rule);
                        for (int i = 0; i < rule.Count; i++)
                        {
                            var slot = new Slot();
                            state.Slots.Add(slot);
                            TrySpawn(state, slot);
                        }
                        states.Add(state);
                        slots += rule.Count;
                    }
                    _rooms[roomEntityId] = states;
                }

                _logger.LogInformation("SpawnSystem: initialized and filled {SlotCount} spawn slots.", slots);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Vacates the mob's slot and schedules its respawn.
        /// Must run before <c>EntityService.DestroyEntity</c> is called.
        /// </summary>
        public Task HandleAsync(MobDiedEvent @event)
        {
            lock (_gate)
                MarkVacant(@event.MobEntityId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Vacates the item's slot. The item entity itself is promoted to persistent by
        /// <c>ItemContextHandler</c>; the slot will respawn a fresh world-spawn instance.
        /// </summary>
        public Task HandleAsync(ItemPickedUpEvent @event)
        {
            lock (_gate)
                MarkVacant(@event.ItemEntityId);
            return Task.CompletedTask;
        }

        /// <summary>Spawns one fresh entity for every slot whose respawn time has elapsed.</summary>
        public Task HandleAsync(HeartbeatTickEvent @event)
        {
            lock (_gate)
            {
                var now = _clock.UtcNow;
                foreach (var states in _rooms.Values)
                foreach (var state in states)
                foreach (var slot in state.Slots)
                {
                    if (slot.LiveEntityId.HasValue || slot.RespawnAt is null || slot.RespawnAt > now)
                        continue;
                    TrySpawn(state, slot);
                }
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public SpawnSyncResult SyncRoom(uint roomEntityId)
        {
            lock (_gate)
            {
                var spawned = new List<uint>();
                int added = 0, detached = 0;

                _rooms.TryGetValue(roomEntityId, out var existing);
                existing ??= new List<RuleState>();

                var desired = _entityService.TryGet<SpawnConfigComponent>(roomEntityId, out var config)
                    ? DistinctRules(roomEntityId, config).ToList()
                    : new List<SpawnRule>();

                // Removed rules: drop every slot.
                foreach (var state in existing)
                {
                    if (desired.Any(r => SameId(r.Id, state.RuleId)))
                        continue;
                    detached += DropSlots(state, state.Slots.Count);
                }

                var next = new List<RuleState>(desired.Count);
                foreach (var rule in desired)
                {
                    var state = existing.FirstOrDefault(s => SameId(s.RuleId, rule.Id));
                    if (state is null)
                        state = new RuleState(roomEntityId, rule);
                    else
                    {
                        var blueprintChanged = !string.Equals(state.BlueprintId, rule.BlueprintId, StringComparison.OrdinalIgnoreCase);
                        state.BlueprintId = rule.BlueprintId;
                        state.RespawnDelaySeconds = rule.RespawnDelaySeconds;

                        // A slot disabled by an unknown blueprint is re-armed when the blueprint changes.
                        if (blueprintChanged)
                            foreach (var slot in state.Slots)
                                if (slot.LiveEntityId is null && slot.RespawnAt is null)
                                    slot.RespawnAt = _clock.UtcNow;
                    }

                    if (rule.Count < state.Slots.Count)
                        detached += DropSlots(state, state.Slots.Count - rule.Count);

                    while (state.Slots.Count < rule.Count)
                    {
                        var slot = new Slot();
                        state.Slots.Add(slot);
                        added++;
                        if (TrySpawn(state, slot) is { } id)
                            spawned.Add(id);
                    }

                    next.Add(state);
                }

                if (next.Count > 0)
                    _rooms[roomEntityId] = next;
                else
                    _rooms.Remove(roomEntityId);

                return new SpawnSyncResult(spawned, added, detached);
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<SpawnRuleStatus> DescribeRoom(uint roomEntityId)
        {
            lock (_gate)
            {
                if (!_rooms.TryGetValue(roomEntityId, out var states))
                    return Array.Empty<SpawnRuleStatus>();

                return states.Select(s => new SpawnRuleStatus(
                        s.RuleId,
                        s.BlueprintId,
                        s.Slots.Count,
                        s.Slots.Count(slot => slot.LiveEntityId.HasValue),
                        s.RespawnDelaySeconds,
                        s.Slots.Where(slot => slot.LiveEntityId is null && slot.RespawnAt.HasValue)
                               .Select(slot => slot.RespawnAt)
                               .Min()))
                    .ToList();
            }
        }

        // ── Private helpers (callers hold _gate) ─────────────────────────────────

        private IEnumerable<SpawnRule> DistinctRules(uint roomEntityId, SpawnConfigComponent config)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in config.Rules)
            {
                if (seen.Add(rule.Id))
                {
                    yield return rule;
                    continue;
                }
                _logger.LogWarning(
                    "SpawnSystem: duplicate spawn rule id '{RuleId}' in room {RoomId} — later rule ignored.",
                    rule.Id, roomEntityId);
            }
        }

        /// <summary>Drops <paramref name="n"/> slots, vacant first; returns how many occupants were detached.</summary>
        private int DropSlots(RuleState state, int n)
        {
            int detached = 0;
            for (int i = 0; i < n; i++)
            {
                var slot = state.Slots.LastOrDefault(s => s.LiveEntityId is null) ?? state.Slots[^1];
                state.Slots.Remove(slot);
                if (slot.LiveEntityId is { } entityId)
                {
                    _entityToSlot.Remove(entityId);
                    detached++;
                }
            }
            return detached;
        }

        private void MarkVacant(uint entityId)
        {
            if (!_entityToSlot.Remove(entityId, out var tracked))
                return;

            tracked.Slot.LiveEntityId = null;
            tracked.Slot.RespawnAt = _clock.UtcNow + TimeSpan.FromSeconds(tracked.Rule.RespawnDelaySeconds);
        }

        private uint? TrySpawn(RuleState state, Slot slot)
        {
            // Reload-teardown guard: never spawn into a room that has been destroyed.
            if (!_entityService.HasComponent<RoomComponent>(state.RoomEntityId))
                return null;

            if (!_templateRegistry.TryGet(state.BlueprintId, out _))
            {
                _logger.LogWarning(
                    "SpawnSystem: cannot spawn '{Blueprint}' for rule '{RuleId}' in room {RoomId} — template not found; slot disabled.",
                    state.BlueprintId, state.RuleId, state.RoomEntityId);
                slot.RespawnAt = null;
                return null;
            }

            string? roomBlueprintId = null;
            if (_entityService.TryGet<BlueprintComponent>(state.RoomEntityId, out var roomBp))
                roomBlueprintId = roomBp.BlueprintId;

            var entity = _templateRegistry.Spawn(state.BlueprintId);
            _entityService.AddComponent(entity.Id, new LocationComponent
            {
                RoomEntityId = state.RoomEntityId,
                RoomBlueprintId = roomBlueprintId,
            });

            slot.LiveEntityId = entity.Id;
            slot.RespawnAt = null;
            _entityToSlot[entity.Id] = (state, slot);

            _logger.LogDebug(
                "SpawnSystem: spawned '{Blueprint}' (entity {EntityId}) for rule '{RuleId}' in room {RoomId}.",
                state.BlueprintId, entity.Id, state.RuleId, state.RoomEntityId);
            return entity.Id;
        }

        private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private sealed class RuleState
        {
            public uint RoomEntityId { get; }
            public string RuleId { get; }
            public string BlueprintId { get; set; }
            public int RespawnDelaySeconds { get; set; }
            public List<Slot> Slots { get; } = new();

            public RuleState(uint roomEntityId, SpawnRule rule)
            {
                RoomEntityId = roomEntityId;
                RuleId = rule.Id;
                BlueprintId = rule.BlueprintId;
                RespawnDelaySeconds = rule.RespawnDelaySeconds;
            }
        }

        private sealed class Slot
        {
            public uint? LiveEntityId { get; set; }
            public DateTime? RespawnAt { get; set; }
        }
    }
}
