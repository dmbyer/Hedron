# Use Case: Persistence Reform

**Status:** planned
**Actors:** System
**Module:** `Core/Systems/` (cross-cutting); `Core/ECS/`; `Core/Modules/World/`; `Core/Modules/[all feature modules]`; `Server/`

---

## Description

Replaces the file-per-entity JSON snapshot model with SQLite-backed entity persistence, integrates persistence lifecycle into `EntityService` so handlers and commands no longer carry scattered save/delete responsibility, and corrects the world-content startup model so YAML is always the authoritative source for room and area data. The reform eliminates an entire class of state-drift bugs caused by two independent sources of truth (YAML templates vs JSON snapshots) for the same world content, and closes the permanent-growth problem in the entity snapshot directory caused by missing delete paths.

This use case is staged into three independently mergeable slices (A, B, C). Each stage leaves the build green and the game playable. The spec-review and code-review gates run once per stage.

---

## Decision Rationale

> This section exists because these decisions were reached through design discussion and are non-obvious. A cold-start implementation session that skips this section is likely to reproduce the old patterns. Read it before implementing any stage.

### Why SQLite instead of JSON files

File-per-entity JSON requires explicit delete operations. Every mechanic that removes an entity — mob death, item pickup, environmental destruction, room deletion — needs a corresponding delete call for that entity's JSON file or the file persists forever. This failure mode scales with the number of mechanics, not with the number of entities. The JSON directory grows without bound on any non-graceful shutdown. SQLite eliminates this: `DestroyEntity` issues `DELETE FROM entity_components WHERE entity_id = ?` and the row is gone. No file management, no dangling state, no missing-delete failure mode.

### Why periodic full flush instead of property-level dirty tracking

In a mutable-class ECS, component property mutations are invisible to `EntityService`:

```csharp
var pools = _entityService.GetComponent<PoolsComponent>(entityId);
pools.CurrentHp -= damage;   // EntityService has no visibility here
```

Transparent tracking requires either immutable record components with a `SetComponent` replace-the-whole-instance pattern (changes every mutation callsite) or observable property wrappers (heavy overhead). Requiring callers to call `MarkDirty` after every property mutation was the pre-slice-5b model and was removed because it was being missed. Making it automatic at the property level is prohibitively expensive. The correct answer is: **don't track property changes; flush periodically.** A MUD's entity count makes a full-sweep flush cheap. The flush-interval loss window (typically 60 s) is acceptable for all mutable runtime state (HP, location, crop growth, inventory).

### Why save-on-change is tightly restricted

Admin-authored content creation (`dig`, `mkitem`, `mkmob`) and account/character creation are rare, deliberate boundary crossings where a crash-between-write-and-flush would lose work the user just did. Immediate durability is required there; for **admin boundary saves** — an admin-gated command that mutates a persistent entity (e.g. `setplayer`, `setrespawn`) saves once after the mutation, paired with an audit event; and for **session-end boundary saves** — a player logout/disconnect/`quit` force-saves the player so their final state is durable before they leave. All other mutations — every combat round, movement, stat change, effect application — are covered by the periodic flush. Outside those cases, handlers and player commands never call `SaveEntityAsync` for runtime state changes.

### Why `DestroyEntity` must auto-delete from SQLite

Every mechanic that removes an entity (mob death, harvest, room deletion, item consumed) would otherwise need to know to call a delete method. Each new mechanic that forgets is a permanent row in the database. Centralizing the delete inside `EntityService.DestroyEntity` means no caller ever gets this wrong — they just call `DestroyEntity` and persistence cleanup is automatic.

### Why room entities are not persistent at all — `LocationComponent.RoomBlueprintId` is the stable reference

The naive solution to player location stability is to keep room entity IDs stable across restarts via SQLite. This is wrong for two reasons: it puts the entire room graph into the flush pool (wasteful), and it fails for instanced rooms — dungeons or player-private spaces that have no YAML backing. On restart, SQLite would load the instanced room entity, `WorldContentLoader` would find no YAML template to refresh it from, and the player would have no valid parent room.

The correct solution is to change what `LocationComponent` stores as its cross-restart reference. `LocationComponent` carries two fields:

- `RoomBlueprintId` (`string?`, `[Persistent]`): the stable cross-restart reference. This is the blueprint ID of the YAML-backed room, or `null` for instanced content.
- `RoomEntityId` (`uint`, NOT `[Persistent]`): runtime-only entity ID, resolved from `RoomBlueprintId` on startup by `CharacterHydrationHandler`.

On startup, after world content loads, `CharacterHydrationHandler` (on `WorldContentReadyEvent`) resolves each player's `RoomBlueprintId`:
- Blueprint found → set `RoomEntityId` to the current entity for that blueprint.
- Blueprint missing or null (instanced room, deleted room, first login) → move player to the configured default room; update both fields.

This means rooms need no `PersistentEntity` at all — YAML is the complete source of truth. The flush pool shrinks to players, accounts, crops, and player-owned items. Instanced rooms are handled gracefully: entities in them at restart (items, mobs) are non-persistent and simply cease to exist; players get default-room fallback. The `dig` command no longer calls `SaveEntityAsync` — the YAML file written by `dig` is the room's sole durable state.

### Why mobs and world-spawn items are never persistent

No other entity stores a persisted `uint` reference to a specific mob entity or to a world-spawn item entity. There is no ID stability requirement. Mobs and world-spawn items are therefore always fresh-spawned from templates on startup. Dead mobs are simply gone; a respawn system (slice C) schedules new spawns. This eliminates the mob-duplication problem that occurred when mob entities were in JSON but their YAML template also caused a fresh spawn.

### Why dropped items are non-persistent

When a player drops an item in a room, the item becomes non-persistent: `PersistentEntity` is removed and the item vanishes on restart. The template that originally spawned it will respawn a fresh instance independently. This avoids the "two swords in room" problem that arises if a player drops their personal instance of an item after the spawn slot has already respawned a new one. Persistent item storage between sessions requires placing items in a persistent container (chest, vault), not dropping them on the floor. This is standard MUD convention.

### Why spawn slots are room/area-owned, not entity-owned

If the spawn slot concept were encoded on item or mob entities, every mechanic that touches those entities (pickup, death, rust storm, merchant pickup, environmental effect) would need to explicitly "clear the slot." That failure mode scales with the number of mechanics. By making slots a room/area concern — tracked in `SpawnConfigComponent` + `SpawnTracker` with the spawn system subscribing to removal domain events — no individual mechanic knows about spawn slots. They fire their events; the spawn system handles everything from one place.

---

## Two Persistence Domains

| Domain | Representative entities | `PersistentEntity`? | Persisted components | On startup |
|---|---|---|---|---|
| **World content** | Rooms, areas, mobs, world-spawn items | No | None | Always fresh-spawned or refreshed from YAML/templates; spawn tracking is in-memory only |
| **Persistent entities** | Players, accounts, player-owned items, player-placed containers, crops, items in persistent containers | Yes | All `[Persistent]`-tagged components | Loaded fully from SQLite; `CharacterHydrationHandler` resolves `RoomBlueprintId` → `RoomEntityId` after world content loads |

**Cross-domain stable reference:** `LocationComponent` carries `RoomBlueprintId` (`string?`, `[Persistent]`) as the cross-restart room reference and `RoomEntityId` (`uint`, NOT `[Persistent]`) as the runtime entity ID resolved on startup. Every code path that places an entity in a room must set both fields. Instanced rooms (no blueprint, no YAML) use `RoomBlueprintId = null`; entities in them at restart fall back to the default room (players) or are destroyed (items, mobs).

---

## Preconditions

- Slices 1–9 complete; codebase is on the post-combat baseline.
- `PersistenceSystem` exists, backed by JSON files, with save-on-change calls scattered across admin commands, `LoginFlow`, `PlayerSessionHandler`, and `CharacterHydrationHandler`.
- `WorldContentLoader.SpawnMissingEntities` uses a blueprint-existence check to avoid re-spawning already-live entities; both rooms and mobs carry `PersistentEntity`.
- `RoomComponent` is `[Persistent]`.
- `EntityService.DestroyEntity` does not touch persistence.
- No `Microsoft.Data.Sqlite` dependency exists.

---

## Postconditions

- `PersistenceSystem` is backed by SQLite. No JSON entity files remain in use.
- `EntityService.AddComponent<PersistentEntity>(id, ...)` registers the entity in the SQLite-backed persistence pool. `EntityService.DestroyEntity(id)` deletes the entity from SQLite automatically if it carried `PersistentEntity`. No handler or command calls any delete method.
- Handlers and commands call `SaveEntityAsync` only at one of three boundaries — entity construction (account/character creation), an admin boundary save (an admin-gated mutation command paired with an audit event, e.g. `setplayer`), or a session-end force-save (player logout/disconnect/`quit`). All other persistence is covered by the periodic flush.
- `PersistenceFlushTimer` performs a full sweep of all `PersistentEntity`-carrying entities on each cycle. No footprint calculation.
- Room and area entities carry `PersistentEntity` (for ID stability) but `RoomComponent` is no longer `[Persistent]`. Room data is always refreshed from YAML on startup.
- Mob entities and world-spawn item entities never carry `PersistentEntity`.
- Player-owned items gain `PersistentEntity` when entering a persistent context (player inventory, persistent container) and lose it when dropped to the floor.
- A `SpawnConfigComponent` on room/area entities declares spawn rules (template ID, count, respawn delay). `SpawnSystem` subscribes to `MobDiedEvent` and `ItemPickedUpEvent` to track slot vacancies and schedule respawns.

---

## Stage A — SQLite Infrastructure + EntityService Lifecycle

### Goal
Replace the JSON file-per-entity snapshot store with SQLite and integrate persistence lifecycle into `EntityService`. Zero gameplay change; the game runs identically after this stage.

### Stage A Preconditions
All of the overall preconditions above.

### Stage A Postconditions

- `Microsoft.Data.Sqlite` added to the appropriate project (`Core` or `Server`).
- SQLite schema bootstrapped on first run:
  ```sql
  CREATE TABLE IF NOT EXISTS entity_components (
      entity_id  INTEGER NOT NULL,
      type_name  TEXT    NOT NULL,
      data       TEXT    NOT NULL,
      PRIMARY KEY (entity_id, type_name)
  );
  ```
- `IPersistenceSystem` interface simplified to:
  - `Task SaveEntityAsync(uint entityId, CancellationToken ct = default)` — boundary saves only (construction, admin boundary, session end)
  - `Task FlushAllAsync(CancellationToken ct = default)` — writes all `PersistentEntity` entities (shutdown)
  - `Task FlushDirtyAsync(CancellationToken ct = default)` — writes all `PersistentEntity` entities (timer cycle; "dirty" is implicit — all persistent entities are always flushed)
  - `Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default)` — startup hydration
  - No `MarkDirty`, `IsDirty`, `FlushAsync`, `FlushActivePlayerFootprintAsync`
- `EntityService.AddComponent` detects when `PersistentEntity` is being attached and registers the entity ID in an internal set. `EntityService.DestroyEntity` checks this set and issues a DELETE to SQLite if the entity was registered.
- `PersistenceFlushTimer` calls `FlushDirtyAsync` on each cycle; no `ISessionManager` dependency.
- `PersistenceBootstrap.StartAsync` calls `LoadAllAsync` (reads from SQLite); `StopAsync` calls `FlushAllAsync`.
- `appsettings.json` gains `Persistence:DatabasePath` (default `data/hedron.db`). `Persistence:DataDirectory` is removed.
- Docker-relevant: database path and content directory paths must be overridable via environment variable so they can be mounted as volumes. Use `HEDRON_PERSISTENCE__DATABASEPATH` and `HEDRON_WORLD__CONTENTDIRECTORY` patterns (ASP.NET Core double-underscore env var override convention).

### Stage A Main Flow

1. `PersistenceBootstrap.StartAsync` opens (or creates) the SQLite database at `Persistence:DatabasePath`; bootstraps schema.
2. `LoadAllAsync` reads all rows from `entity_components`; groups by `entity_id`; restores each entity via `EntityService.RestoreEntity`; attaches components.
3. During entity restoration, when `PersistentEntity` is attached, `EntityService` registers the entity ID in its internal persistence set.
4. `EntityHydratedEvent` per restored ID, then `WorldLoadedEvent`. (Unchanged.)
5. During normal gameplay: any entity with `PersistentEntity` is flushed on the next `FlushDirtyAsync` cycle. No caller involvement.
6. When any system or handler calls `EntityService.DestroyEntity(id)`:
   - If the entity is in the persistence set: `DELETE FROM entity_components WHERE entity_id = id`.
   - Remove from the persistence set.
   - Proceed with normal ECS teardown.
7. Shutdown: `PersistenceBootstrap.StopAsync` calls `FlushAllAsync` — iterates the persistence set, writes all components tagged `[Persistent]` to SQLite.

### Stage A: files to create or modify

| File | Change |
|---|---|
| `Core/Systems/IPersistenceSystem.cs` | Simplify to 4 methods as above; remove `FlushAsync`, `MarkDirty`, `IsDirty`, `FlushActivePlayerFootprintAsync` |
| `Core/Systems/PersistenceSystem.cs` | Replace file I/O with `Microsoft.Data.Sqlite`; no footprint calculation; full sweep on every flush cycle |
| `Core/ECS/EntityService.cs` | Add internal persistence set; hook `AddComponent<PersistentEntity>` to register; hook `DestroyEntity` to delete |
| `Server/PersistenceBootstrap.cs` | No structural change; `LoadAllAsync`/`FlushAllAsync` calls remain; open DB connection |
| `Server/PersistenceFlushTimer.cs` | Remove `ISessionManager` dependency; remove footprint calculation; call `FlushDirtyAsync` directly |
| `Server/Program.cs` | Remove `Persistence:DataDirectory`-related wiring; no structural change |
| `appsettings.json` | Remove `Persistence:DataDirectory`; add `Persistence:DatabasePath` (default `data/hedron.db`) |
| `docs/architecture/06-persistence.md` | Full rewrite to reflect SQLite model and EntityService lifecycle integration |
| `docs/reference/systems.md` | Update `PersistenceSystem` and `EntityService` entries |

---

## Stage B — World Content Data Refresh + Startup Reform

### Goal
Remove `[Persistent]` from data-bearing world content components so YAML is always the authoritative source for room data. Remove `PersistentEntity` entirely from mobs and world-spawn items. Remove all `SaveEntityAsync` callsites except entity construction. The game world reloads cleanly from YAML every restart with no drift.

### Stage B Preconditions
Stage A is merged and green.

### Stage B Postconditions

- `LocationComponent` gains a new `[Persistent]` field `RoomBlueprintId` (`string?`). The existing `RoomEntityId` (`uint`) is no longer `[Persistent]` — it is resolved at startup from `RoomBlueprintId` by `CharacterHydrationHandler`.
- Room and area entities carry no `PersistentEntity` at all. They are fully YAML-driven. No SQLite rows for rooms or areas exist.
- `WorldContentLoader` always fresh-spawns all world content (rooms, areas, mobs, items) from templates. No "skip if already live" check for any world content entity type.
- `CharacterHydrationHandler` (on `WorldContentReadyEvent`): for each loaded player, look up `RoomBlueprintId` in the current live blueprint map. If found, set `RoomEntityId` to the current entity ID. If not found or null (instanced room, deleted room, first login), move player to the configured default room and update both `RoomBlueprintId` and `RoomEntityId`.
- All movement paths and room-placement operations (movement commands, `IItemSystem.MoveToRoom`, `WorldContentLoader.PlaceMobsInRooms`, `PlaceItemsInRooms`) set both `RoomEntityId` and `RoomBlueprintId`.
- All `SaveEntityAsync` calls removed from `DigCommand`, `SetCommand`, `MkItemCommand`, `SetItemCommand`, `MkMobCommand`, `SetMobCommand`. No admin command calls `SaveEntityAsync` — room durability is the YAML file, not a SQLite row.
- Mob entities: `PersistentEntity` not added; always fresh-spawned.
- World-spawn item entities: `PersistentEntity` not added; always fresh-spawned.
- `PersistenceFlushTimer` no longer depends on `ISessionManager`.

### Stage B Main Flow

#### Startup — all world content
1. `PersistenceBootstrap.StartAsync`: load all entities from SQLite. Only persistent entities (players, accounts, crops, player-owned items) are in SQLite. No room or mob entities.
2. `WorldContentLoader.LoadAndSpawnAsync`: load all YAML templates.
3. For each room/area template: spawn a fresh entity. Attach `RoomComponent` (name, description) and `BlueprintComponent` from template. No `PersistentEntity`.
4. Link exits from YAML declarations.
5. For each mob template: spawn a fresh entity. No `PersistentEntity`.
6. For each item template with `spawnRoomBlueprintId`: spawn a fresh entity. No `PersistentEntity`.
7. Attach `LocationComponent` (both `RoomEntityId` and `RoomBlueprintId`) for mobs and items from template spawn room.

#### Startup — persistent entity location resolution
8. `CharacterHydrationHandler` fires on `WorldContentReadyEvent`. Iterates **all persistent entities** (not just players) that carry `LocationComponent` with a `RoomBlueprintId`. For each: resolve `RoomBlueprintId` → current `RoomEntityId` via the live blueprint map. If the blueprint is missing or `RoomBlueprintId` is null, move the entity to the default room (players) or destroy it (non-player persistent entities with no valid room). Call `SaveEntityAsync` after any correction. This single pass handles players today and automatically covers future persistent world entities (crops, placed containers) without a second resolution pass.

#### Admin commands
9. `dig`: create room entity (no `PersistentEntity`), write YAML template. No `SaveEntityAsync` — YAML is the room's sole durable state.
10. `mkitem`, `mkmob`: write YAML template, spawn entity, no `PersistentEntity`, no `SaveEntityAsync`.
11. `set`, `setitem`, `setmob`: mutate data in the live entity and write the updated YAML template. No `SaveEntityAsync`.

### Stage B: files to create or modify

| File | Change |
|---|---|
| `Core/ECS/Components/LocationComponent.cs` | Add `[Persistent] string? RoomBlueprintId`; remove `[Persistent]` from `RoomEntityId` |
| `Core/ECS/Components/RoomComponent.cs` | Remove `[Persistent]` if present |
| `Core/ECS/Components/AreaComponent.cs` | Remove `[Persistent]` if present |
| `Core/Modules/World/Systems/WorldContentLoader.cs` | Always fresh-spawn all world content; no "skip if already live" check; set both `LocationComponent` fields on placement |
| `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs` | Resolve `RoomBlueprintId` → `RoomEntityId` after world content loads; fallback to default room |
| `Core/Modules/Admin/Systems/RoomBuilderSystem.cs` | Remove `PersistentEntity` from room entities |
| `Core/Modules/Admin/Commands/DigCommand.cs` | Remove `SaveEntityAsync` entirely — YAML is the room's durable state |
| `Core/Modules/Admin/Commands/SetCommand.cs` | Remove `SaveEntityAsync` |
| `Core/Modules/Items/Commands/MkItemCommand.cs` | Remove `PersistentEntity`; remove `SaveEntityAsync` |
| `Core/Modules/Items/Commands/SetItemCommand.cs` | Remove `SaveEntityAsync` |
| `Core/Modules/Mobs/Systems/MobBuilderSystem.cs` | Remove `PersistentEntity` from mob entities |
| `Core/Modules/Mobs/Commands/MkMobCommand.cs` | Remove `SaveEntityAsync` |
| `Core/Modules/Mobs/Commands/SetMobCommand.cs` | Remove `SaveEntityAsync` |
| Movement commands + `IItemSystem.MoveToRoom` | Set both `RoomEntityId` and `RoomBlueprintId` on every move |
| `Server/PersistenceFlushTimer.cs` | Remove `ISessionManager` dependency (covered by Stage A) |
| `docs/architecture/06-persistence.md` | Update domain table (two domains); add `LocationComponent` stable-reference note |
| `docs/architecture/flows/flow-01-server-startup.md` | Reflect always-fresh-spawn; remove room-from-SQLite path |
| `docs/architecture/flows/flow-04-persistence-flush-cycle.md` | Reflect lighter flush pool (no rooms) |
| `docs/architecture/flows/flow-08-admin-room-creation.md` | Remove `SaveEntityAsync`; YAML is durability |

---

## Stage C — Context-Driven Item Persistence + Spawn Slot Foundation

### Goal
Items gain or lose `PersistentEntity` based on the context they occupy. A spawn slot system on rooms/areas tracks slot vacancies via domain events and schedules respawns.

### Stage C Preconditions
Stage B is merged and green.

### Stage C Postconditions

- `ItemContextHandler` subscribes to item location-change events:
  - `ItemPickedUpEvent` → add `PersistentEntity` to item entity; do NOT clear `BlueprintComponent` (the spawn slot tracks the vacancy independently).
  - `ItemDroppedEvent` → remove `PersistentEntity` from item entity; item vanishes on restart.
  - `ItemPlacedInContainerEvent` (future) → add `PersistentEntity`.
  - `ItemRemovedFromContainerEvent` (future) → if item moves to player inventory, keep `PersistentEntity`; if moved to room floor, remove.
- `SpawnConfigComponent` (cross-cutting, `Core/ECS/Components/`) holds spawn rules for one template slot: `BlueprintId`, `MinCount`, `MaxCount`, `RespawnDelaySeconds`. A room/area entity may have multiple `SpawnConfigComponent` instances (one per spawn rule). Storage: a list-valued component or a repeating convention (see Open Questions).
- `SpawnTracker` (in-memory, per slot) maps `(entityId of room/area, blueprintId) → live entity ID?`. Not persisted.
- `SpawnSystem` subscribes to:
  - `MobDiedEvent` — find the slot this mob occupied; mark vacant; schedule respawn after `RespawnDelaySeconds`.
  - `ItemPickedUpEvent` — find the slot this item occupied; mark vacant; schedule respawn.
  - `HeartbeatTickEvent` — check pending respawn timers; spawn entities for due timers.
- On startup: `WorldContentLoader` populates `SpawnTracker` for each freshly-spawned mob/item entity.

### Stage C Main Flow

#### Item pickup — context-driven persistence
1. Player sends `get <item>` → `GetCommand` resolves item entity, calls `IItemSystem.MoveToInventory`.
2. `ItemInteractionHandler` (existing) receives `ItemPickedUpEvent`.
3. **New**: `ItemContextHandler` also receives `ItemPickedUpEvent` (higher priority than broadcast); calls `EntityService.AddComponent(itemEntityId, new PersistentEntity())` if not already present. The item entity is now in the flush pool.
4. **New**: `SpawnSystem` receives `ItemPickedUpEvent`; finds the spawn slot for this item (if any); marks it vacant; sets a respawn timer.

#### Item drop — context demotion
5. Player sends `drop <item>` → `DropCommand` → `IItemSystem.MoveToRoom`.
6. `ItemInteractionHandler` receives `ItemDroppedEvent`.
7. **New**: `ItemContextHandler` receives `ItemDroppedEvent`; calls `EntityService.RemoveComponent<PersistentEntity>(itemEntityId)`. Item is now out of the flush pool and will vanish on restart.
8. No spawn slot action on drop — the slot is already vacant (or will be handled by the existing mob/item system).

#### Mob death — slot vacancy
9. `CombatMobDeathHandler` (existing) calls `EntityService.DestroyEntity(mobEntityId)`.
10. `EntityService.DestroyEntity` fires internally (post-destroy hook or pre-destroy event) OR: `SpawnSystem` subscribes to `MobDiedEvent` which fires before destroy.
11. `SpawnSystem` marks the slot vacant; sets a respawn timer.
12. After `RespawnDelaySeconds`: `SpawnSystem` spawns a new entity from the template; registers it in `SpawnTracker`.

### Stage C: files to create or modify

| File | Change |
|---|---|
| `Core/ECS/Components/SpawnConfigComponent.cs` | New: `BlueprintId`, `MinCount`, `MaxCount`, `RespawnDelaySeconds` |
| `Core/Modules/Spawn/Systems/ISpawnSystem.cs` + `SpawnSystem.cs` | New: slot tracking, timer management, spawn-on-demand |
| `Core/Modules/Spawn/Handlers/ItemContextHandler.cs` | New: adds/removes `PersistentEntity` on item context events |
| `Core/Modules/Items/Events/ItemPlacedInContainerEvent.cs` | New (if containers land in this stage) |
| `Core/Modules/Spawn/SpawnModule.cs` | New DI entry point |
| `Core/Modules/World/Systems/WorldContentLoader.cs` | Populate `SpawnTracker` after mob/item spawning |
| `docs/reference/components.md` | Add `SpawnConfigComponent` |
| `docs/reference/systems.md` | Add `SpawnSystem` |
| `docs/reference/handlers.md` | Add `ItemContextHandler` |
| `docs/architecture/flows/flow-09-item-pickup.md` | Items journey (pickup + drop) — includes `ItemContextHandler` step |

---

## Events Fired

| Event | Publisher | When |
|---|---|---|
| `EntityHydratedEvent(uint EntityId)` | `PersistenceBootstrap.StartAsync` | Unchanged from slice 1 |
| `WorldLoadedEvent` | `PersistenceBootstrap.StartAsync` | Unchanged from slice 1 |
| `MobDiedEvent` | `CombatMobDeathHandler` | Existing; now also consumed by `SpawnSystem` |
| `ItemPickedUpEvent` | `ItemInteractionHandler` | Existing; now also consumed by `ItemContextHandler` + `SpawnSystem` |
| `ItemDroppedEvent` | `ItemInteractionHandler` | Existing; now also consumed by `ItemContextHandler` |

No new event types required for Stages A and B. Stage C requires `ItemPlacedInContainerEvent` and `ItemRemovedFromContainerEvent` only if container mechanics ship in this stage; otherwise defer to the container slice.

---

## Systems / Handlers Involved

| Surface | Stage | New or modified |
|---|---|---|
| `IPersistenceSystem` / `PersistenceSystem` | A | Modified (SQLite backend, simplified interface) |
| `EntityService` | A | Modified (persistence set, auto-delete hook) |
| `PersistenceBootstrap` | A | Modified (open DB connection) |
| `PersistenceFlushTimer` | A | Modified (remove footprint logic) |
| `WorldContentLoader` | B | Modified (split refresh/spawn paths) |
| `RoomBuilderSystem` | B | No change (room entity still gets `PersistentEntity`) |
| `MobBuilderSystem` | B | Modified (remove `PersistentEntity`) |
| Item builder systems | B | Modified (remove `PersistentEntity` from world-spawn items) |
| `ItemContextHandler` | C | New |
| `SpawnSystem` | C | New |

---

## Content Tooling Impact

Infrastructure only. No new admin commands, content file shapes, or `TemplateRegistry` entries. Stage C adds `SpawnConfigComponent` to room/area entities — this requires either a YAML field on room templates (`spawnRules: [...]`) or admin commands for managing spawn rules. If YAML extension is chosen, `RoomTemplate` and `RoomTemplateDeserializer` must be extended in the same Stage C PR. If deferred to a later slice, add to `backlog.md`.

---

## Cross-cutting Surfaces Stressed

| Surface | Classification | Notes |
|---|---|---|
| Persistence | **Gap closed** | This is the reform slice. SQLite backend, EntityService lifecycle integration, periodic full flush. |
| ECS (`EntityService`) | **Gap closed** | `DestroyEntity` gains persistence cleanup. `AddComponent<PersistentEntity>` gains registration. No new API beyond this. |
| Event bus | **Adequate** | No new event infrastructure; existing bus routes new subscriptions correctly. |
| Commands | **Adequate** | Existing commands lose `SaveEntityAsync` calls; shape unchanged. |
| Content templates / YAML | **Adequate** | `RoomTemplate` etc. unchanged for Stage B. Stage C may extend `RoomTemplate` for spawn rules — flag as open question. |
| Output | **Adequate** | No output changes. |
| Configuration | **Gap closed** | `Persistence:DatabasePath` added; `Persistence:DataDirectory` removed; Docker env-var override patterns documented. |
| Docker / deployment | **Acknowledged debt** | The Docker migration plan is a parallel artifact (see the Docker sub-agent output). Config keys are made container-friendly in Stage A; the actual `Dockerfile` and `docker-compose.yml` ship as a deployment slice (or alongside Stage A). Tracked in `backlog.md` if not shipped with Stage A. |

---

## Flows Introduced or Modified

| Flow | Change |
|---|---|
| Flow 1 — Server startup | Update to reflect SQLite load, room data refresh from YAML, fresh mob/item spawn (replace "SpawnMissingEntities with JSON check" with split paths) |
| Flow 4 — Persistence flush cycle | Full rewrite: full sweep of all `PersistentEntity` entities; no footprint logic; timer calls `FlushDirtyAsync` |
| Flow 8 — Admin room creation (`dig`) | Update: room entity still saved (ID stability); no `RoomComponent` data in SQLite |
| Flow 9 — Item pickup | Update (Stage C): add `ItemContextHandler` step; add `SpawnSystem` slot-vacancy step |
| Flow 10 — Item drop | Update (Stage C): add `ItemContextHandler` step (context demotion) |
| Flow 20 — Mob death + respawn (new) | New flow (Stage C): `MobDiedEvent` → `SpawnSystem` marks slot vacant → heartbeat tick → respawn |

---

## Reference Catalog Updates

- `docs/reference/systems.md`: update `PersistenceSystem` (SQLite, simplified interface); update `EntityService` (persistence set, auto-delete); add `SpawnSystem` (Stage C).
- `docs/reference/handlers.md`: add `ItemContextHandler` (Stage C).
- `docs/reference/components.md`: add `SpawnConfigComponent` (Stage C); update `RoomComponent` (remove `[Persistent]` notation); update `AreaComponent`.
- `docs/architecture/06-persistence.md`: full rewrite for SQLite model (Stage A); update domain table and startup flow (Stage B).

---

## Design Notes

- **`dig` no longer calls `SaveEntityAsync`.** The room's durability is the YAML file written by `dig`, not a SQLite row. On the next restart, `WorldContentLoader` reads the YAML and spawns the room fresh. Players who were in the room have `RoomBlueprintId` persisted; `CharacterHydrationHandler` resolves it to the new entity ID.

- **`LocationComponent` dual fields.** Every code path that places an entity in a room must set both `RoomEntityId` (runtime) and `RoomBlueprintId` (persistence). This includes movement commands, `IItemSystem.MoveToRoom`, `WorldContentLoader` placement, and `CharacterHydrationHandler`'s fallback. Failure to set `RoomBlueprintId` causes silent location loss on restart.

- **Instanced room handling on restart.** Entities (mobs, items) in an instanced room at restart are non-persistent and simply cease to exist — correct. Players with `RoomBlueprintId = null` or unresolved are moved to the default room. No error, no orphan data.

- **`AreaComponent` treatment.** Areas have no `PersistentEntity`. Data refreshed from YAML on startup, same as rooms.

- **Mob death pre-destroy ordering for `SpawnSystem`.** `SpawnSystem` must observe `MobDiedEvent` before `CombatMobDeathHandler` calls `EntityService.DestroyEntity`. Subscribe `SpawnSystem` at a lower priority number (runs first) than `CombatMobDeathHandler`. Alternatively, `EntityService.DestroyEntity` can fire a pre-destroy event for systems that need to inspect the entity before teardown.

- **Item entity continuity.** Items are NOT destroyed and recreated on pickup/drop. The same entity persists through all location changes. `PersistentEntity` is added/removed by `ItemContextHandler` based on context. This means an item entity picked up by a player retains its original entity ID — important if any other system has stored a reference to it.

- **Transient mob inventory items.** If a mob picks up an item (future mechanic), the item's context is the mob's inventory. Since the mob is non-persistent, the item remains non-persistent too. When the mob dies and the item is looted, `ItemContextHandler` promotes it to persistent if a player takes it.

- **`BlueprintComponent` is NOT cleared on item pickup** (contrast with the prior INV-21 guidance). The spawn slot system tracks slot vacancy by observing events, not by checking `BlueprintComponent` on live entities. `BlueprintComponent` remains on the item entity as an origin record. The spawn system's internal tracker is the slot state; it does not query `BlueprintComponent` on live entities.

  This supersedes the INV-21 blueprint-slot-clearing rule for items. INV-21 must be updated to reflect this. The spawn system's vacancy check is: "is there any live entity currently registered in this slot?" — stored in `SpawnTracker`, not derived from entity components.

- **Crop / player-created content** is a third persistent entity class (beyond players and accounts) that uses the same `PersistentEntity` + `[Persistent]` model. These are placed and authored at runtime; their entity is the durable state. If the parent room is deleted, a `RoomDeletedEvent` handler must cascade-destroy all `PersistentEntity`-carrying entities with `LocationComponent.RoomEntityId` pointing to that room (excluding players). This cascade handler ships with the room-deletion admin command slice, not here.

- **`ComponentTypeRegistry` / `ComponentSerializer`** may be simplified or reused for the SQLite serialization path. The existing `System.Text.Json` component serialization logic is retained; SQLite stores the same JSON strings in the `data` column. The file I/O layer is replaced; the serialization layer is not.

---

## Open Questions

1. **`SpawnConfigComponent` storage shape.** A room may have multiple spawn rules (wolves + guards + vampires). Since a single component type can only appear once per entity in the current ECS, either: (a) `SpawnConfigComponent` holds a `List<SpawnRule>` field, or (b) a new `SpawnRuleComponent` exists and multiple instances are allowed. Decide before Stage C implementation begins.

2. **YAML extension for spawn rules.** Does Stage C extend `RoomTemplate` to include spawn rule declarations, or are spawn rules authored via admin commands only? If YAML, `RoomTemplateDeserializer` must be extended in Stage C; if admin-command-only, YAML extension defers to a later content-tooling slice.

3. **`Microsoft.Data.Sqlite` project placement.** Should `PersistenceSystem` (in `Core/`) reference `Microsoft.Data.Sqlite`, or should the SQLite implementation live in `Server/` with `Core/` depending only on an `IPersistenceBackend` abstraction? For current scale, adding `Microsoft.Data.Sqlite` directly to `Core` is simpler. Flag if the separation becomes important (e.g., when a test project is added in Phase 4).

4. **Item entity in transient mob inventory at restart.** If a mob picks up an item and the server restarts, the mob respawns fresh (no item in inventory) and the item entity is non-persistent (vanishes). Is this acceptable? If the item was player-dropped (persistent), it was already non-persistent before mob pickup, so this is consistent. If future design requires mob-carried items to persist, that is a separate slice.

---

## Related

- [`persistence-substrate.md`](persistence-substrate.md) — original JSON file-per-entity model (slice 1)
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — `PersistentEntity` marker + area-scoped flush (slice 5b); this slice replaces that model
- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — introduces save-on-change calls in admin commands that Stage B removes
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — YAML content pipeline; Stage B modifies `WorldContentLoader`
- [`items-and-inventory.md`](../features/items/items.md) — item entity lifecycle; Stage C modifies pickup/drop flows
- [`mobs.md`](mobs.md) — mob entity construction; Stage B removes `PersistentEntity` from mob construction
- [`docs/architecture/06-persistence.md`](../architecture/06-persistence.md) — authoritative persistence model (rewritten in Stage A)
