# Spawn System

> Tracks spawn-slot occupancy for world-content entities (mobs, world-spawn items) and re-spawns them from templates on the heartbeat. **Authoring checkpoint:** persistence-reform Stage C. Living document.

## What it is / does

`SpawnSystem` is a **domain-tier system** that maintains a slot registry of which mobs and items should be alive in which rooms, detects when a slot becomes vacant (mob death, item pickup), and schedules + executes re-spawns from `ITemplateRegistry` on each heartbeat tick. It never publishes events and never calls persistence (INV-5, INV-8). It self-initializes from the live entity graph on `WorldContentReadyEvent` so it needs no startup hook in `WorldContentLoader`.

## How it works

### Slot model

Each `SpawnConfigComponent.Rules` entry on a room entity defines one independent respawn slot:

| `SpawnRule` field | Meaning |
|---|---|
| `BlueprintId` | Which template to re-spawn from |
| `MinCount` / `MaxCount` | Desired population (currently MinCount == MaxCount == 1 per rule) |
| `RespawnDelaySeconds` | Cooldown before the slot re-fills |

`SpawnSystem` maintains two internal maps:
- `_slots: Dictionary<(roomEntityId, blueprintId), SlotState>` — slot registry keyed by owning room and blueprint.
- `_entityToSlot: Dictionary<entityId, (roomEntityId, blueprintId)>` — reverse map for O(1) vacancy marking.

### Event subscriptions

| Event | Priority | Action |
|---|---|---|
| `WorldContentReadyEvent` | 80 | Sweeps all entities with `SpawnConfigComponent`; registers live entities in the tracker; sets `RespawnAt = now + delay` for slots with no live entity |
| `MobDiedEvent` | 20 | Marks slot vacant; sets `RespawnAt = now + delay` |
| `ItemPickedUpEvent` | 20 | Marks slot vacant; sets `RespawnAt = now + delay` |
| `HeartbeatTickEvent` | 95 | For each slot with `RespawnAt <= UtcNow`: calls `ITemplateRegistry.Spawn(blueprintId)`, attaches `LocationComponent`, updates the tracker |

### Spawn execution

On re-spawn, `SpawnSystem` calls `ITemplateRegistry.Spawn(blueprintId)`, which allocates a new entity and runs `IEntityTemplate.Apply` to attach the archetype-specific components (including `MobDataComponent` or `ItemDataComponent`). `SpawnSystem` then attaches `LocationComponent { RoomEntityId }` to place the entity. No events are published — the new entity simply appears in the world.

### INV-21 — blueprint/instance separation

When a player picks up an item, `ItemSystem.MoveToInventory` clears `BlueprintComponent` so the spawn slot is free to re-fill. Mobs are destroyed on death; `SpawnSystem`'s `MobDiedEvent` handler marks the slot vacant and schedules re-spawn. See [INV-21](../../architecture/checklist.md).

## Interface

- [`ISpawnSystem.cs`](../../../Core/Modules/Spawn/Systems/ISpawnSystem.cs) — currently empty (no external API in Stage C; all coordination is via event subscriptions). The interface is the DI registration hook and the architecture-guard anchor.

## Considerations

- **Self-initializing.** `SpawnSystem` subscribes to `WorldContentReadyEvent` rather than being called by `WorldContentLoader`, keeping the loader's phases closed to spawn concerns (INV-10).
- **`IClock` for `UtcNow`.** All timestamp comparisons use `IClock.UtcNow` (INV-26) — no `DateTime.UtcNow` direct calls.
- **`HeartbeatTickEvent` priority 95.** Runs after combat (20) and effect ticks so dead mobs are fully processed before re-spawn is evaluated on the same tick.

## Extensibility

- **Multi-count slots** — `MinCount`/`MaxCount` support the data shape for "2-3 goblins per room"; the slot logic currently treats each rule as a single slot. Multi-count iteration is additive.
- **Area-level respawn enforcement** — future: an area's `RespawnRate` governs per-area spawn cadence. `SpawnConfigComponent` entries already carry `RespawnDelaySeconds`; a future pass reads the area's template value.
- **Shop restocking** — shops follow the same `SpawnConfigComponent` pattern with an `ItemDataComponent` blueprint; no new slot machinery needed.

## Related

- [`world.md`](world.md) — holistic feature view.
- [`time-system.md`](time-system.md) — `HeartbeatTickEvent` that drives the respawn check.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5 (systems pure), INV-21 (blueprint/instance separation), INV-26 (injectable time seam).
- [`../../reference/systems.md`](../../reference/systems.md) — `SpawnSystem` catalog row.
- [`../../reference/components.md`](../../reference/components.md) — `SpawnConfigComponent` row.
