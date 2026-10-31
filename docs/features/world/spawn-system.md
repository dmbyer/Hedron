# Spawn System

> Rule-keyed spawn slots for world-content mobs and found items: fills every slot at boot, vacates a slot on death or pickup, and re-spawns it from its template on the heartbeat. Room spawn rules are the **single spawn truth**. **Authoring checkpoints:** persistence-reform Stage C; spawn-authoring (Phase 5 slice 2, [record](../../roadmap/completed/spawn-authoring.md)). Living document.

## What it is / does

`SpawnSystem` is a **domain-tier system** (plus event subscriber) that owns every world mob and found item's lifecycle placement. Each room `SpawnRule` declares `Count` slots; the system spawns one entity per slot, tracks which entity occupies which slot, and re-spawns a slot `RespawnDelaySeconds` after its occupant dies or is picked up. It never publishes events and never calls persistence (INV-5, INV-8). Nothing else places world mobs or items: the loader spawns only areas and rooms, and mob/item templates carry no spawn location.

## How it works

### Rule shape

`SpawnRule(Id, BlueprintId, Count, RespawnDelaySeconds)` lives on the room's `SpawnConfigComponent` (copied from `RoomTemplate.SpawnRules` by `Apply`; not `[Persistent]` — the YAML is authoritative).

| Field | Meaning |
|---|---|
| `Id` | Rule identity, unique (case-insensitive) within the room. Defaults to `SpawnRuleIds.DefaultFor(BlueprintId)` — the blueprint id, deterministically truncated to 60 characters. Always written. |
| `BlueprintId` | A mob or item template. |
| `Count` | Population, `1..SpawnRuleLimits.MaxCountPerRule` (20); default 1. |
| `RespawnDelaySeconds` | Per-slot cooldown, `>= 0`; default 300. |

Pure helpers sit beside the record in `Core/ECS/Components/` so World, Admin and Authoring share them without a module cycle: `SpawnRuleLimits` (constants), `SpawnRuleIds` (`DefaultFor`, `Mint` → default then `-2`…`-999`, `IsWellFormed` = `^[A-Za-z0-9._-]{1,64}$`), and `SpawnRuleValidation.Errors(roomId, rules)` — the **one** rule-shape check, enforced by the room deserializer (throws `ContentShapeException`), `ContentValidator.Validate(RoomTemplate)` (refuses a save) and `RoomBuilderSystem` (refuses an admin mutation). Reference problems (a rule naming an unknown or non-mob/item template) stay warnings (`ContentValidator.ValidateRegistry`, `ContentReferenceIndex`) and the slot is disabled at runtime.

### Slot model

Slots key on **`(room entity, rule id, ordinal)`** — never on blueprint or file position. Two rules may share a blueprint (distinct ids); reordering rules changes nothing. Internally: room → ordered `RuleState`s (rule id, blueprint, delay, `Count` slots), and entity → slot for O(1) vacancy. Each slot holds its occupant and its own `RespawnAt`.

### Event subscriptions

| Event | Priority | Action |
|---|---|---|
| `WorldContentReadyEvent` | **State (10)** | Clears the tracker, builds one `RuleState` per `(room, rule id)` (a duplicate id in the component is skipped with a warning) and **immediately** spawns every slot. Runs ahead of `ShopkeeperSpawnHandler` (Domain 20) so rule-placed shopkeepers are seeded in the same fan-out. |
| `MobDiedEvent` | Domain (20) | Vacates only that entity's slot; `RespawnAt = now + rule delay`. Must run before `DestroyEntity`. |
| `ItemPickedUpEvent` | Domain (20) | Same, for found items; `ItemContextHandler` promotes the picked-up entity to persistent. |
| `HeartbeatTickEvent` | Ai (95) | Spawns one entity per due slot. |

**No claiming.** An existing same-blueprint entity (e.g. a player-owned copy) is never adopted: every slot occupant is one this system spawned. Reload destroys every world entity before re-publishing the ready event, so nothing legitimate is lost.

### Spawn execution

`ITemplateRegistry.Spawn(blueprintId)` (INV-12) then `LocationComponent { RoomEntityId, RoomBlueprintId }` (INV-23). An unknown template disables the slot with a warning. A slot whose room entity has lost its `RoomComponent` is skipped (see concurrency).

### Live authoring API

- `SpawnSyncResult SyncRoom(uint roomEntityId)` reconciles the tracker with the room's live `SpawnConfigComponent`: a new rule spawns `Count` now; a count increase spawns the delta now; a decrease or removed rule drops surplus slots — **vacant first** — and **detaches** any dropped occupant (alive, untracked; its death schedules nothing — despawning in front of players would need narration the engine lacks); an unchanged rule keeps its entities and pending timers; a changed delay applies from the next vacancy, a changed blueprint from the next spawn (a slot disabled by an unknown blueprint is re-armed). Returns `(SpawnedEntityIds, SlotsAdded, SlotsDetached)`.
- `IReadOnlyList<SpawnRuleStatus> DescribeRoom(uint roomEntityId)` — per rule: id, blueprint, count, live, delay, next due time.

`RoomBuilderSystem` is the only caller of `SyncRoom` (admin `spawnrule`, `mkmob`, `mkitem` — see [admin-authoring](../admin-authoring/admin-authoring.md)). After an added or updated rule commits, it also judges the placement against the room's area designation (`IAreaPlacementSystem.JudgeRule`) and returns any finding on `SpawnRuleMutationResult.Findings`. This is advisory: the rule is saved either way, and the commands print it as a `Warning:` line ([area designation](area-model.md#area-designation)).

### INV-21 — blueprint/instance separation

Picking up an item keeps its `BlueprintComponent` as an origin record (`ItemSystem.MoveToInventory` does not clear it); the slot is vacated by `ItemPickedUpEvent`, not by clearing the component. Mobs are destroyed on death after `MobDiedEvent` vacates the slot. See [INV-21](../../architecture/checklist.md).

## Interface

- [`ISpawnSystem.cs`](../../../Core/Modules/Spawn/Systems/ISpawnSystem.cs) — `SyncRoom`, `DescribeRoom`; result records `SpawnSyncResult`, `SpawnRuleStatus` beside it.

## Considerations

- **Concurrency (INV-31): guarded.** The tracker is reached from the heartbeat thread (tick, combat `MobDiedEvent`) and session threads (`ItemPickedUpEvent`, reload's ready event, `SyncRoom` via admin verbs). Every entry point takes one private lock; nothing inside re-enters the system and `ITemplateRegistry.Spawn` publishes nothing. ECS mutations stay on the existing session/heartbeat paths (the acknowledged `ComponentRepository` debt, not widened).
- **Reload-teardown race — mostly closed.** `ReloadAsync` (session thread) destroys the world before the ready event rebuilds the tracker, while a heartbeat tick may fire against the old tracker. Inside the lock, a due slot whose room has lost its `RoomComponent` is skipped, so a tick landing after the room is destroyed spawns no orphan. A tick interleaved *between* individual `DestroyEntity` calls (room still alive) remains — acknowledged under the backlog's "World-state threading model" entry.
- **Boot fill in its own handler.** Filling on `WorldContentReadyEvent` rather than inside the loader keeps the loader's phases closed to spawn concerns — a phase-separation design choice, not an invariant.
- **`IClock` for `UtcNow`** (INV-26). No randomness yet.
- **Shopkeeper till on non-boot spawn** — acknowledged debt (backlog "Seed shop tills on non-boot spawn"): a shopkeeper spawned by a heartbeat respawn or `SyncRoom` has an empty till until `reload` (base stock self-heals via restock).

## Extensibility

- **Stage-B weighted pools** — a pool rule is a `SpawnRule` whose blueprint *source* is a weighted pool drawn per spawn through `IRandom`. Slots, vacancy, timers, ids, `SyncRoom` and authoring all key on the rule id, so pools touch only the "which blueprint" step.
- **Area-level respawn rate** — future: an area's `RespawnRate` scales per-rule delays.

## Related

- [`world.md`](world.md) — holistic feature view · [`world-content.md`](world-content.md) — `spawnRules` file shape and converting a pre-slice-2 world.
- [`time-system.md`](time-system.md) — `HeartbeatTickEvent`.
- [Flow 1](../../architecture/flows/flow-01-server-startup.md) (boot fill) · [Flow 5](../../architecture/flows/flow-05-content-reload.md) (reload) · [Flow 9](../../architecture/flows/flow-09-item-pickup.md) · [Flow 20](../../architecture/flows/flow-20-mob-death-respawn.md).
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md).
