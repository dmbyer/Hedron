# Persistence

Hedron uses a two-level persistence model backed by SQLite. The questions "should this entity survive a restart?" and "which of its components are worth saving?" have separate, independent answers.

---

## Two Persistence Domains

| Domain | Representative entities | `PersistentEntity`? | Persisted components | On startup |
|---|---|---|---|---|
| **World content** | Rooms, areas, mobs, world-spawn items | Stage B: No | None | Always fresh-spawned or refreshed from YAML/templates |
| **Persistent entities** | Players, accounts, player-owned items, crops, items in persistent containers | Yes | All `[Persistent]`-tagged components | Loaded fully from SQLite; `CharacterHydrationHandler` resolves `RoomBlueprintId` → `RoomEntityId` after world content loads |

> **Stage A note.** Room and area entities still carry `PersistentEntity` in Stage A. Stage B removes it — world content will be fully YAML-driven with no SQLite rows.

**Cross-domain stable reference:** `LocationComponent` carries `RoomBlueprintId` (`string?`, `[Persistent]`) as the cross-restart room reference and `RoomEntityId` (`uint`, NOT `[Persistent]`) as the runtime entity ID resolved on startup. See Stage B in the [persistence-reform use case](../../use-cases/persistence-reform.md) for full details.

---

## Level 1 — Does this entity participate in persistence?

Add the `PersistentEntity` marker component to any entity that must survive a server restart. `PersistentEntity` is a zero-data component tagged `[Persistent]`, so it round-trips through the SQLite snapshot and is restored on hydration — the entity knows it persists without any extra bookkeeping.

Entities **without** `PersistentEntity` are never written to SQLite.

```csharp
// authored player character — survives restart
var player = _entityService.CreateEntity();
_entityService.AddComponent(player.Id, new CharacterComponent { ... });
_entityService.AddComponent(player.Id, new PersistentEntity());   // opts in

// spawned mob — session-only, fresh-spawned from template on restart
var mob = _entityService.CreateEntity();
_entityService.AddComponent(mob.Id, new MobDataComponent { ... });
// no PersistentEntity — not saved
```

---

## Level 2 — Which components are included in the snapshot?

`[Persistent]` on a component *type* tells `PersistenceSystem` to include that component when serializing an entity that **already** has `PersistentEntity`. It does not cause any entity to be saved on its own.

```
Entity has PersistentEntity?
  No  → never written, full stop.
  Yes → write all components tagged [Persistent] on that entity.
```

Some components must be excluded even for persistent entities. `PlayerComponent` holds a transient session reference. `TransientEffectsComponent` is session-only by design. Those stay untagged; `PersistenceSystem` skips them.

---

## SQLite schema

```sql
CREATE TABLE IF NOT EXISTS entity_components (
    entity_id  INTEGER NOT NULL,
    type_name  TEXT    NOT NULL,
    data       TEXT    NOT NULL,
    PRIMARY KEY (entity_id, type_name)
);
```

Each row stores one component for one entity. `data` is the JSON string produced by `IComponentSerializer`. On save: the entity's existing rows are deleted, then fresh rows are inserted (delete-then-insert within a transaction). On load: rows are grouped by `entity_id`; each group is restored via `EntityService.RestoreEntity` + `EntityService.AddComponent`.

---

## EntityService lifecycle integration

`EntityService` tracks which entities have `PersistentEntity` in an internal set:

- `AddComponent<PersistentEntity>` / `AddComponent(Type = PersistentEntity, ...)` → registers entity in the set.
- `RemoveComponent<PersistentEntity>` → removes entity from the set (item context demotion in Stage C).
- `DestroyEntity` → if the entity is in the set, fires `OnPersistentEntityDestroying` (which `PersistenceSystem` registers to issue `DELETE FROM entity_components WHERE entity_id = ?`) **before** ECS teardown.

No handler or command ever calls a delete method. `DestroyEntity` is the single exit point.

---

## Save patterns

### Save-on-change (construction time only)

Use **only** at entity construction: admin content creation (`dig`, `mkitem`, `mkmob`) and account/character creation. These are rare, deliberate boundary crossings where immediate durability is required (a crash between write and flush would lose newly authored content).

```csharp
// in LoginFlow, after CreateAccountAsync
await _persistence.SaveEntityAsync(accountEntityId, ct);
```

**No other code path calls `SaveEntityAsync`.** Runtime mutations (combat, movement, stat changes) are covered by the periodic flush.

### Periodic full flush

`PersistenceFlushTimer` fires on the configured interval (`Persistence:FlushIntervalSeconds`, default 60 s) and calls `IPersistenceSystem.FlushDirtyAsync`. This writes **all** `PersistentEntity`-carrying entities — no footprint calculation, no dirty tracking. The flush pool is small enough that a full sweep is always cheap.

### Shutdown flush

`PersistenceBootstrap.StopAsync` calls `IPersistenceSystem.FlushAllAsync`, which performs the same full sweep as `FlushDirtyAsync`. Guarantees a complete snapshot on graceful shutdown regardless of the flush timer position.

---

## Configuration

| Key | Default | Docker env var |
|---|---|---|
| `Persistence:DatabasePath` | `data/hedron.db` | `HEDRON_PERSISTENCE__DATABASEPATH` |
| `Persistence:FlushIntervalSeconds` | `60` | `HEDRON_PERSISTENCE__FLUSHINTERVALSECONDS` |
| `World:ContentDirectory` | `data/content/` | `HEDRON_WORLD__CONTENTDIRECTORY` |

Mount `data/` as a Docker volume to make both the database and content files durable across container restarts.

---

## Adding a new persistent entity class

Before writing any code, answer two questions explicitly:

1. **Should instances of this entity class persist?** → If yes, the construction path adds `PersistentEntity`. If some instances persist and others don't (e.g. authored vs generated), the construction path diverges at that decision point — not at the component type level.

2. **For each component on this entity: should it be included in the snapshot?** → If yes, tag the component class `[Persistent]`. If no (transient ref, session-only state, derived/recomputed on load), leave it untagged.

Do not use `[Persistent]` to control whether an entity persists. That is `PersistentEntity`'s job.

---

## Serializers

Two serializers, two audiences — they do not share code:

- **`System.Text.Json`** — component snapshots. Machine round-trip. Used by `PersistenceSystem` via `IComponentSerializer`. Stored in the SQLite `data` column.
- **`YamlDotNet`** — designer-authored content files under `data/content/`. Human-readable. Used by `WorldContentLoader`.
