# Use Case: Persistence Two-Level Model

**Status:** implemented
**Actors:** System
**Module:** `Core/Systems/` (cross-cutting); `Core/Handlers/`; `Server/`

---

## Description

Redesigns the persistence layer to match the two-level model in [`../architecture/06-persistence.md`](../architecture/06-persistence.md). The central change is `PersistentEntity` — a zero-data `[Persistent]` marker component that explicitly opts an entity into persistence at the entity level. The private `WriteEntityAsync` is gated on `HasComponent<PersistentEntity>`: entities without it are silently skipped regardless of how many `[Persistent]`-tagged component types they carry.

The dirty-set flush strategy is replaced by two patterns:

- **Save-on-change** (`SaveEntityAsync`) — called directly by the initiator/handler that owns a mutation (room creation, exit authoring, room property set, account/character creation, logout). The entity is durable as soon as the operation completes; no flush-cycle dependency.
- **Area-scoped periodic flush** — `PersistenceFlushTimer` calls `FlushActivePlayerFootprintAsync`, which saves every `PersistentEntity`-carrying entity in rooms currently occupied by at least one player. Bounds flush cost to the active player population.

The earlier dirty-marking workarounds (`PersistenceHandler` subscriptions, `CharacterHydrationHandler`/`AccountSystem` `MarkDirty` calls) are removed. No new gameplay or player-visible behaviour is added.

---

## Preconditions

- Slices 1–5a are complete; `PersistenceSystem`, `PersistenceBootstrap`, `PersistenceFlushTimer` exist and function under the dirty-set model.
- `RoomComponent`, `LocationComponent`, `BlueprintComponent`, `AccountComponent`, `CharacterComponent` all carry `[Persistent]`.
- `EntityService` provides `GetAllComponents<T>()` and `HasComponent<T>(uint)`.

---

## Postconditions

- Every entity that should survive a restart carries `PersistentEntity`; entities without it are never written to disk. `SaveEntityAsync`, `FlushActivePlayerFootprintAsync`, and `FlushAllPersistentAsync` all route through a `WriteEntityAsync` that enforces this guard.
- `IPersistenceSystem` exposes `SaveEntityAsync`, `FlushActivePlayerFootprintAsync(occupiedRoomIds, ct)`, and `FlushAllPersistentAsync(ct)`. The dirty-set methods (`MarkDirty`, `IsDirty`, `FlushAsync`) are removed.
- `PersistenceFlushTimer` calls `FlushActivePlayerFootprintAsync`; `PersistenceBootstrap.StopAsync` calls `FlushAllPersistentAsync` (full sweep — shutdown must be complete regardless of footprint).
- `PersistenceHandler` is deleted; its save-on-change responsibilities move to direct `SaveEntityAsync` calls in the owning initiators/handlers (`DigCommand`, `SetCommand`, `PlayerSessionHandler`, `CharacterHydrationHandler`, `LoginFlow`).
- `AccountSystem` and `RoomBuilderSystem` no longer touch persistence — they attach `PersistentEntity` at construction and return the entity id; the initiator saves (INV-5).

---

## Main Flow

### A — Startup: entity hydration (gated internally)

1. `PersistenceBootstrap.StartAsync` calls `LoadAllAsync`; components are silently re-attached. `PersistentEntity` is among them, so hydrated entities re-acquire their opt-in marker.
2. `EntityHydratedEvent` per restored id, then `WorldLoadedEvent` once. (Unchanged.)
3. `WorldContentLoader.LoadAndSpawnAsync` calls `AddComponent(id, new PersistentEntity())` on newly spawned entities (blueprint exists, no hydrated counterpart) alongside `RoomComponent`/`BlueprintComponent` — YAML content must survive restart.
4. `CharacterHydrationHandler` (on `WorldContentReadyEvent`) calls `SaveEntityAsync` immediately when it resets a stale `LocationComponent.RoomEntityId`.

### B — Save-on-change: admin content authoring

5. `dig` → `DigCommand` calls `IRoomBuilderSystem.CreateRoom` (which adds `PersistentEntity`), publishes `RoomCreatedByAdminEvent`, then calls `SaveEntityAsync(newRoomId)` and `SaveEntityAsync(sourceRoomId)`.
6. `set` → `SetCommand` calls `SaveEntityAsync(roomId)` after the mutation.
7. Account/character creation → `LoginFlow` calls `IAccountSystem.CreateAccountAsync`/`CreateCharacterAsync` (which attach `PersistentEntity` and return ids), then saves — **character entity first, then account** (a crash between writes leaves a recoverable orphaned character, not a dangling account pointer). Publishes `AccountCreatedEvent`/`CharacterCreatedEvent` after the saves.

### C — Area-scoped periodic flush

8. `PersistenceFlushTimer` ticks → `ISessionManager.GetAll()` → read each player's `LocationComponent.RoomEntityId` → `FlushActivePlayerFootprintAsync(occupiedRoomIds, ct)`.
9. The system writes every entity in the occupied rooms (plus the players themselves) that carries `PersistentEntity`.

### D — Shutdown: full sweep

10. `PersistenceBootstrap.StopAsync` calls `FlushAllPersistentAsync`, iterating every `PersistentEntity` entity and writing it.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `EntityHydratedEvent(uint EntityId)` | `PersistenceBootstrap.StartAsync` | Per entity | Unchanged from slice 1. |
| `WorldLoadedEvent` | `PersistenceBootstrap.StartAsync` | Once | Unchanged from slice 1. |

No new events. `PersistenceHandler` is deleted, so its subscriptions disappear with it.

---

## Design Notes

- **`MarkDirty`/`IsDirty`/`FlushAsync` removal is breaking** — intentional; the compiler enforces that every former caller is updated.
- **Why drop `FlushAsync`?** The old global dirty-set sweep splits into two operations with different semantics: `FlushActivePlayerFootprintAsync` (runtime) and `FlushAllPersistentAsync` (shutdown). Keeping the old name would mislead callers.
- **`PlayerMovedEvent` dirty-marking removal.** Under the area-scoped model the periodic flush already writes all `PersistentEntity` entities in occupied rooms — including the player that just moved. The flush interval is the acceptable loss window.
- **Admin save-on-change is synchronous from the session's perspective** — `DigCommand` awaits `SaveEntityAsync`, so the new room is on disk before the admin sees confirmation. Desirable for content authoring.
- **`CharacterHydrationHandler` is a save-on-change case** — a hydration-time room correction is a lifecycle transition where crash-between-correction-and-flush is unacceptable.
- **No backfill loop** — no saved data predates `PersistentEntity`, so no migration step is implemented. Add a targeted migration later if a deployment needs it.
- **Character-before-account write order** — see Main Flow step 7.
- **Area-scoped flush scope** = entities with `LocationComponent.RoomEntityId` in the occupied set, plus the player entities. Entities in unoccupied rooms rely on save-on-change for durability.
- **`FlushActivePlayerFootprintAsync` query** uses `GetAllComponents<LocationComponent>()` + a `.Where(... occupiedRoomIds.Contains ...)` filter — linear in location-bearing entities, no secondary index. An inverted room→entities index can be added later without changing the interface.

---

## Related

- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; introduced the dirty-set model this slice replaces.
- [`bare-bones-content-spawning.md`](../features/world/world.md) — slice 5a; its admin-event `PersistenceHandler` subscriptions move to save-on-change here.
- [`account-character-creation.md`](../features/accounts/accounts.md) — slice 5; its `MarkDirty` call-sites are removed here.
- [`../architecture/06-persistence.md`](../architecture/06-persistence.md) — the two-level model this slice implements.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
