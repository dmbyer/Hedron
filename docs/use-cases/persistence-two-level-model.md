# Use Case: Persistence Two-Level Model

**Status:** implemented
**Actors:** System
**Module:** `Core/Systems/` (cross-cutting); `Core/Handlers/`; `Server/`

---

## Description

Redesigns the persistence layer to match the two-level model documented in `docs/architecture/08-persistence.md`. The central change is introducing `PersistentEntity` — a zero-data `[Persistent]` marker component that explicitly opts an entity into persistence at the entity level. `PersistenceSystem.WriteEntityAsync` is gated on `HasComponent<PersistentEntity>`: entities without it are silently skipped regardless of how many `[Persistent]`-tagged component types they carry.

The current dirty-set flush strategy is replaced by two patterns from `08-persistence.md`:

- **Save-on-change** (`SaveEntityAsync`) — called directly by admin-command handlers at the point of mutation (room creation, exit authoring, room property set, account/character creation). The entity is durable as soon as the operation completes; no flush-cycle dependency.
- **Area-scoped periodic flush** — `PersistenceFlushTimer` calls a new `FlushActivePlayerFootprintAsync` that saves every `PersistentEntity`-carrying entity in rooms currently occupied by at least one player. This replaces the global dirty-set sweep and bounds flush cost to the active player population.

The interim workarounds introduced in earlier slices are removed: `PersistenceHandler`'s subscriptions to `PlayerMovedEvent` and `PlayerTeleportedByAdminEvent` (dirty-marking hacks), `CharacterHydrationHandler`'s `_persistence.MarkDirty` call, and `AccountSystem.MarkDirty` call-sites. `PersistenceHandler`'s remaining save-on-change subscriptions are replaced by direct `SaveEntityAsync` calls in the handlers or command code that own each mutation.

No new gameplay or player-visible behaviour is added.

---

## Preconditions

- Slices 1–5a are complete.
- `PersistenceSystem`, `IPersistenceSystem`, `PersistenceHandler`, `PersistenceBootstrap`, and `PersistenceFlushTimer` exist and are functional.
- `RoomComponent`, `LocationComponent`, `BlueprintComponent`, `AccountComponent`, `CharacterComponent` all carry `[Persistent]` and are being serialized under the existing dirty-set model.
- `CharacterHydrationHandler` subscribes to `WorldContentReadyEvent` and calls `_persistence.MarkDirty` after correcting stale room references.
- `AccountSystem.CreateAccountAsync` and `CreateCharacterAsync` call `_persistence.MarkDirty`.
- `PersistenceHandler` subscribes to: `EntitySpawnedByAdminEvent`, `RoomExitAuthoredByAdminEvent`, `RoomCreatedByAdminEvent`, `RoomPropertySetByAdminEvent`, `AccountCreatedEvent`, `CharacterCreatedEvent`, `PlayerDisconnectedEvent`, `PlayerMovedEvent`, `PlayerTeleportedByAdminEvent`.
- `EntityService` provides `GetAllComponents<T>()` and `HasComponent<T>(uint)`.

---

## Postconditions

- Every entity that should survive a restart carries `PersistentEntity`; entities without it are never written to disk.
- `PersistenceSystem.WriteEntityAsync` (and `SaveEntityAsync`) skip any entity that lacks `PersistentEntity`.
- `IPersistenceSystem` exposes `SaveEntityAsync` (already exists) and a new `FlushActivePlayerFootprintAsync(IEnumerable<uint> occupiedRoomIds, CancellationToken ct)`.
- `PersistenceHandler` is deleted. `docs/reference/handlers.md` is updated to remove the entry.
- `PersistenceFlushTimer` calls `FlushActivePlayerFootprintAsync` instead of `FlushAsync`.
- `PersistenceBootstrap.StopAsync` calls `FlushAllPersistentAsync` (a full sweep of all `PersistentEntity` entities — shutdown must be complete regardless of footprint).
- `CharacterHydrationHandler` no longer calls `MarkDirty`; it calls `SaveEntityAsync` instead if it resets a stale room reference.
- `AccountSystem` no longer calls `MarkDirty`; account and character creation call `SaveEntityAsync`.
- The existing dirty-set machinery (`_dirtySet`, `MarkDirty`, `IsDirty`, `FlushAsync` in its current form) is removed from `PersistenceSystem` and `IPersistenceSystem`.
- All call-sites that previously called `MarkDirty` on an entity have been audited; each is either replaced by `SaveEntityAsync` (at an appropriate mutation point) or removed (interim workaround).
- `docs/reference/handlers.md`, `docs/reference/systems.md`, `docs/reference/components.md`, and `docs/architecture/06-flows.md` are updated to reflect the new model.
- `CharacterHydrationHandler`'s entry in `docs/reference/handlers.md` gains `IPersistenceSystem` in its `Uses` line (it was absent from the prior entry despite the `MarkDirty` call; this slice makes the dependency explicit and upgrades it to `SaveEntityAsync`).
- `AccountSystem`'s entry in `docs/reference/systems.md` is updated to remove all references to `MarkDirty` in its prose description.
- `PlayerSessionHandler`'s entry in `docs/reference/handlers.md` is updated to add `IPersistenceSystem` to its `Uses` line and to document the `SaveEntityAsync(characterEntityId)` call in the disconnect path.
- `PersistenceHandler`'s entry in `docs/reference/handlers.md` is removed entirely.

---

## Main Flow

### A — Startup: entity hydration (unchanged externally; gated internally)

1. `PersistenceBootstrap.StartAsync` calls `PersistenceSystem.LoadAllAsync`. Each entity file is deserialized; `EntityService.RestoreEntity` is called; components are silently re-attached. `PersistentEntity` is one of those components and will be restored from the snapshot — so hydrated entities automatically re-acquire their opt-in marker.
2. For each restored entity id, `PersistenceBootstrap` publishes `EntityHydratedEvent`. After the loop, `WorldLoadedEvent` is published. (Unchanged.)
3. `WorldContentBootstrap` calls `WorldContentLoader.LoadAndSpawnAsync`. For newly spawned entities (blueprint exists, no hydrated counterpart), the loader now calls `EntityService.AddComponent(id, new PersistentEntity())` alongside `RoomComponent` and `BlueprintComponent`. Entities spawned from YAML content must survive restart.
4. `CharacterHydrationHandler` handles `WorldContentReadyEvent`. If it resets a stale `LocationComponent.RoomEntityId`, it calls `_persistence.SaveEntityAsync(entityId, ct)` immediately — the correction must be durable now, not at the next flush.

### B — Save-on-change: admin content authoring

5. Admin creates a room via `dig`. `DigCommand` calls `IRoomBuilderSystem.CreateRoom`, which now adds `PersistentEntity` to the new room entity alongside `RoomComponent` + `BlueprintComponent`. After `RoomCreatedByAdminEvent` is published, the `DigCommand` (as Initiator) calls `await _persistence.SaveEntityAsync(newRoomId, ct)` and `await _persistence.SaveEntityAsync(sourceRoomId, ct)`. No `PersistenceHandler` subscription is needed.
6. Admin sets a room property via `set`. `SetRoomPropertyCommand` calls `_persistence.SaveEntityAsync(roomId, ct)` after the mutation. No `PersistenceHandler` subscription.
7. Account and character creation — `LoginFlow` (Initiator) calls `IAccountSystem.CreateAccountAsync` and `IAccountSystem.CreateCharacterAsync`, which return the newly allocated entity ids. After each domain system method returns, `LoginFlow` calls `await _persistence.SaveEntityAsync(entityId, ct)` on the returned ids. `AccountSystem` does not call `SaveEntityAsync`; it only creates entities, attaches components, and returns the id. `CreateCharacterAsync` write order: `LoginFlow` saves character entity first, then account entity. Character is written first because if the server crashes between the two writes, a dangling reference in the account file (account-first order) is more harmful than an orphaned character file (character-first order). Both `AccountCreatedEvent` and `CharacterCreatedEvent` are published by `LoginFlow` after the saves complete.

### C — Area-scoped periodic flush: runtime player state

8. `PersistenceFlushTimer` ticks. It calls `ISessionManager.GetAll()` to collect `PlayerEntityId` values for all connected sessions. For each player, it reads `LocationComponent.RoomEntityId` to collect the set of occupied room ids. It then calls `IPersistenceSystem.FlushActivePlayerFootprintAsync(occupiedRoomIds, ct)`.
9. `PersistenceSystem.FlushActivePlayerFootprintAsync` queries all entities whose `LocationComponent.RoomEntityId` is in the occupied set, plus the player entities themselves. It writes each that also carries `PersistentEntity`. This bounds flush scope to the active player footprint.

### D — Shutdown flush: full sweep

10. `PersistenceBootstrap.StopAsync` calls `IPersistenceSystem.FlushAllPersistentAsync(ct)`, which iterates every entity in `EntityService` that carries `PersistentEntity` and writes it. This guarantees no durable state is lost on clean shutdown regardless of area occupancy.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `EntityHydratedEvent(uint EntityId)` | `PersistenceBootstrap.StartAsync` | Per entity | Unchanged from slice 1. |
| `WorldLoadedEvent` | `PersistenceBootstrap.StartAsync` | Once | Unchanged from slice 1. |

No new events are introduced by this slice. `PersistenceHandler` is deleted, so its subscriptions disappear with it.

---

## Systems / Handlers Involved

### IPersistenceSystem (modified — core system)

The dirty-set operations are removed. Two new methods are added; `SaveEntityAsync` and `LoadAllAsync` are retained unchanged. The complete final interface is:

```
interface IPersistenceSystem
{
    // --- removed (dirty-set model — deleted in this slice) ---
    // void MarkDirty(uint entityId)
    // bool IsDirty(uint entityId)
    // Task FlushAsync(CancellationToken ct = default)

    // --- retained (unchanged) ---
    Task SaveEntityAsync(uint entityId, CancellationToken ct = default)
    Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default)

    // --- new (two-level model) ---
    Task FlushActivePlayerFootprintAsync(IEnumerable<uint> occupiedRoomIds, CancellationToken ct = default)
    Task FlushAllPersistentAsync(CancellationToken ct = default)
}
```

`WriteEntityAsync` (private implementation detail) gains a guard: if the entity does not carry `PersistentEntity`, return without writing. This guard applies to every code path that writes an entity — `SaveEntityAsync`, `FlushActivePlayerFootprintAsync`, and `FlushAllPersistentAsync` all route through `WriteEntityAsync`.

### PersistenceSystem (modified — core system)

- Remove `_dirtySet` (`ConcurrentDictionary<uint, byte>`), `MarkDirty`, `IsDirty`, `FlushAsync`.
- Add `FlushActivePlayerFootprintAsync`: collects the player entities in the session set, the entities whose `LocationComponent.RoomEntityId` is in `occupiedRoomIds`, and writes those carrying `PersistentEntity`.
- Add `FlushAllPersistentAsync`: iterates all entities in `EntityService`, writes those carrying `PersistentEntity`.
- `WriteEntityAsync` gains the `PersistentEntity` guard.

### PersistenceFlushTimer (modified — hosted service)

- Inject `ISessionManager` (new dependency).
- On each tick: collect active player entity ids from sessions → collect occupied room ids via `LocationComponent` → call `FlushActivePlayerFootprintAsync`.

### PersistenceBootstrap (modified — hosted service)

- `StopAsync` calls `FlushAllPersistentAsync` instead of `FlushAsync`.

### PersistenceHandler (deleted)

- All event subscriptions are removed and the handler class is deleted.
- Remove from `PersistenceModule` DI registration and from `Server/Program.cs`.

### WorldContentLoader (modified — domain system)

- `SpawnMissingEntities` / `SeedVoidRoom` / `CreateRoom` paths add `PersistentEntity` to each newly spawned room entity.

### RoomBuilderSystem (modified — domain system)

- `CreateRoom` calls `EntityService.AddComponent(id, new PersistentEntity())` alongside `RoomComponent` and `BlueprintComponent`. This is pure data mutation and is permitted in a domain system.
- `RoomBuilderSystem` does NOT call `SaveEntityAsync`. The save is performed by `DigCommand` (Initiator) after `CreateRoom` returns (INV-5).

### AccountSystem (modified — domain system)

- `CreateAccountAsync`: remove `_persistence.MarkDirty(entity.Id)` call. Add `PersistentEntity` to the account entity at creation and return the entity id. Save is performed by `LoginFlow` after this method returns (INV-5: domain systems do not call persistence).
- `CreateCharacterAsync`: remove both `_persistence.MarkDirty` calls. Add `PersistentEntity` to the character entity at creation and return the entity id. Save is performed by `LoginFlow` after this method returns, with character written before account (see Main Flow step 7 for rationale). `AccountSystem` does not call `SaveEntityAsync`.
- `RecordLogout` (currently no persistence call): this method updates `CharacterComponent.LastLoginUtc` and returns. After `RecordLogout` returns, `PlayerSessionHandler` calls `await _persistence.SaveEntityAsync(characterEntityId, ct)` so the logout timestamp is durable immediately. `AccountSystem` does not call `SaveEntityAsync`.

### PlayerSessionHandler (modified — handler)

- On `PlayerDisconnectedEvent`: after calling `IAccountSystem.RecordLogout(characterEntityId)`, call `await _persistence.SaveEntityAsync(characterEntityId, ct)` so the logout timestamp is durable immediately. This replaces the `PersistenceHandler` subscription to `PlayerDisconnectedEvent`.
- Inject `IPersistenceSystem` as a new dependency.

### CharacterHydrationHandler (modified — handler)

- Remove `MarkDirty` call.
- Replace with `await _persistence.SaveEntityAsync(entityId, ct)` when resetting a stale room (direct save, immediate durability).
- `IPersistenceSystem` is already injected (see Postconditions catalog note); only the call pattern changes from `MarkDirty` to `SaveEntityAsync`.

### DigCommand (modified — initiator)

- After calling `IRoomBuilderSystem.CreateRoom` and `LinkExits`, call `await _persistence.SaveEntityAsync(newRoomId, ct)` and `await _persistence.SaveEntityAsync(sourceRoomId, ct)`.
- Remove reliance on `PersistenceHandler` to do this work.

### SetCommand (modified — initiator)

- After calling `IRoomBuilderSystem.SetRoomName` / `SetRoomDescription`, call `await _persistence.SaveEntityAsync(roomId, ct)`.

---

## Content Tooling Impact

Pure infrastructure slice. No new gameplay state, no new admin commands, no new YAML schema, no new `TemplateRegistry` entries. The persistence model change is invisible to designers — authored content files and admin commands work identically from the outside. Content tooling impact: **none**.

---

## Cross-cutting Surfaces Stressed

**Persistence (IPersistenceSystem).**
Gap exposed — the current `IPersistenceSystem` interface carries `MarkDirty`, `IsDirty`, and `FlushAsync` which this slice removes. The area-scoped flush (`FlushActivePlayerFootprintAsync`) and shutdown sweep (`FlushAllPersistentAsync`) are new. This slice resolves the gap by updating the interface. `SaveEntityAsync` already exists and is adequate.

**ECS queries.**
Adequate — `EntityService.GetAllComponents<T>()`, `HasComponent<T>()`, and `TryGet<T>()` already exist and cover the `PersistentEntity` guard check and the player-footprint query. No new query pattern is introduced.

**Event bus.**
Adequate — no new events. `PersistenceHandler` subscriptions are removed; the bus is net lighter.

**Sessions / ISessionManager.**
Adequate — `ISessionManager.GetAll()` already exists (used by `BroadcastSystem`). `PersistenceFlushTimer` gains a new consumer of this interface, but the interface shape is unchanged.

**Time / periodic flush.**
Adequate — `PersistenceFlushTimer` is a `BackgroundService` using `PeriodicTimer`. Adding the footprint-resolution step before calling `FlushActivePlayerFootprintAsync` is a mechanical change within the existing hosted-service pattern.

**Commands (Initiators).**
Adequate — `DigCommand` and `SetCommand` already call domain systems. Adding a `SaveEntityAsync` call is consistent with the `INV-10` no-chain pattern: a command that makes a direct system call with no event fan-out needed. No new command framework surface is needed.

**Output.**
Adequate — no player-visible output changes.

**Configuration.**
Adequate — `Persistence:FlushIntervalSeconds` and `Persistence:DataDirectory` are unchanged.

**Content templates.**
Adequate — `WorldContentLoader` / `RoomBuilderSystem` already create entities; adding `PersistentEntity` to those paths is a one-line change per construction site.

**Modules.**
Adequate — `PersistenceModule` needs a DI registration change (remove `PersistenceHandler` if deleted; `PersistenceFlushTimer` gains `ISessionManager` as a dependency that is already registered).

### Persistence opt-in audit (mandatory)

| Component | `[Persistent]`? | Rationale |
|---|---|---|
| `PersistentEntity` | **yes** (self-referential) | Zero-data marker; must round-trip through snapshot so hydrated entities know they persist. |
| `RoomComponent` | yes (existing) | Authored room content must survive restart. |
| `LocationComponent` | yes (existing) | Player's current room must survive restart. |
| `BlueprintComponent` | yes (existing) | Blueprint id linkage must survive restart. |
| `AccountComponent` | yes (existing) | Account credentials must survive restart. |
| `CharacterComponent` | yes (existing) | Character identity and login timestamps must survive restart. |
| `PlayerComponent` | no (existing, correct) | Transient session reference; always re-attached on login. |
| `TransientEffectsComponent` | no (existing, correct) | Session-only effects; intentionally discarded on restart. |
| `PersistentEffectsComponent` | yes (existing) | Long-term effects must survive restart. |
| `AreaComponent` | yes (existing) | Area metadata is authored content. |

**`PersistentEntity` on hydrated entities — no migration required.**
There is no pre-existing saved data that predates `PersistentEntity`. The backfill loop is unnecessary tech debt and is not implemented. No migration path is required. If a future deployment needs to upgrade live data from a pre-`PersistentEntity` snapshot format, add a targeted migration at that time.

---

## Flows Introduced or Modified

### Flow 4 — Persistence flush cycle (modified)

The flush cycle changes from a global dirty-set sweep to an area-scoped player-footprint sweep. The timer now collects session room ids before calling the system. Shutdown transitions from `FlushAsync` to `FlushAllPersistentAsync`. The mermaid diagram and steps in `06-flows.md` must be updated to reflect:
- Removal of the dirty-set snapshot
- Addition of `ISessionManager.GetAll()` → room-id collection step
- Rename of the system method called (`FlushActivePlayerFootprintAsync`)
- Shutdown path update (`FlushAllPersistentAsync`)
- The `PersistentEntity` guard on `WriteEntityAsync`

### Flow 1 — Server startup (modified)

`WorldContentLoader.LoadAndSpawnAsync` now attaches `PersistentEntity` to newly spawned room entities. The startup diagram and step 7 in `06-flows.md` must note this addition. `PersistenceBootstrap.StopAsync` change is also a startup-flow concern; the `06-flows.md` Flow 1 steps must add a note that the shutdown path in `StopAsync` transitions from `FlushAsync` to `FlushAllPersistentAsync` — the old `FlushAsync` method is deleted and callers are updated in this slice.

### Flow 8 — Admin room creation (`dig`) (modified)

The `06-flows.md` Flow 8 entry must be updated as follows:

- Remove `PH` (`PersistenceHandler`) as a named participant from the mermaid sequence diagram entirely.
- `Cmd` (`DigCommand`) gains a direct `SaveEntityAsync` call to `PSys` (`PersistenceSystem`) after `Publish(RoomCreatedByAdminEvent)`. The step previously reading `Bus->>PH: HandleAsync (priority 90) → MarkDirty(newRoomId, sourceId)` is replaced by `Cmd->>PSys: SaveEntityAsync(newRoomId) + SaveEntityAsync(sourceRoomId)`.
- Remove the `Core/Handlers/PersistenceHandler.cs` file path cross-reference link from Flow 8's cross-references block. `PersistenceHandler` no longer exists after this slice.
- The prose step 5 in `06-flows.md` Flow 8 is updated to reflect: `AdminAuditHandler` (priority 80) logs one structured entry; `DigCommand` then calls `SaveEntityAsync` directly on both rooms — no `PersistenceHandler` subscription.

### Flow 2 — Player connection (modified)

The disconnect sequence in `06-flows.md` must be updated as follows:

- Remove `PH` (`PersistenceHandler`) as a named participant from the mermaid sequence diagram entirely. `PersistenceHandler` is deleted in this slice and must not appear.
- `PSH` (`PlayerSessionHandler`) gains an explicit `SaveEntityAsync` call to `PSys` (`PersistenceSystem`) immediately after `HandleAsync(PlayerDisconnectedEvent)` → `RecordLogout` returns. The sequence becomes: `Bus->>PSH: HandleAsync → RecordLogout + SaveEntityAsync(characterEntityId) + RemoveComponent<PlayerComponent> + departure broadcast`.
- The `Bus->>PH: HandleAsync → MarkIfPersistent(CharacterEntityId)` step is deleted entirely from the diagram and steps prose.
- The `03-events.md` forward-design example table entry showing `PersistenceHandler` at priority 90 subscribing to `PlayerDeathEvent` is removed in the same PR that ships this slice. That table entry is forward-design scaffolding from an earlier phase and is now superseded by the save-on-change model.

---

## Design Notes

- **`MarkDirty` / `IsDirty` removal is breaking.** Any future caller that used `MarkDirty` must be updated. The interface change makes the compiler enforce this. This is intentional.
- **Why remove `FlushAsync` from the interface?** The old `FlushAsync` was a global dirty-set sweep. The new model has two distinct operations (`FlushActivePlayerFootprintAsync` for runtime, `FlushAllPersistentAsync` for shutdown) with different semantics. Keeping the old name would mislead callers. `PersistenceBootstrap.StopAsync` is the only caller of the shutdown sweep; it is updated in this slice.
- **`PlayerMovedEvent` dirty-marking removal.** The old `PersistenceHandler` subscribed to `PlayerMovedEvent` and `PlayerTeleportedByAdminEvent` to ensure `LocationComponent` was eventually flushed. Under the area-scoped model this is unnecessary: the periodic flush already writes all `PersistentEntity` entities in occupied rooms, which includes the player entity that just moved. The flush interval is the acceptable loss window.
- **Admin-command save-on-change is synchronous from the session's perspective.** Because `DigCommand` awaits `SaveEntityAsync`, the admin cannot exit the command until the new room is on disk. This is desirable — admin content authoring should be immediately durable.
- **`AccountSystem` remains synchronous internally.** `CreateAccountAsync` and `CreateCharacterAsync` are declared `async Task` for interface symmetry but contain no awaits after this slice (the `SaveEntityAsync` awaits that made them genuinely async were in the prior `MarkDirty` model — they now belong to `LoginFlow`). The interface signatures do not change. `LoginFlow` is already async and adds the `SaveEntityAsync` awaits there.
- **`CharacterHydrationHandler` as a case of save-on-change.** A hydration-time room correction is a lifecycle transition — exactly the scenario `08-persistence.md` identifies for save-on-change. The correction is infrequent and crash-between-correction-and-flush is unacceptable.
- **`PersistenceHandler` fate.** The handler is deleted in this slice. Its `IEventHandler<T>` implementations collapse to zero once all subscriptions move to save-on-change call sites. The class has no residual responsibility. The DI registration is removed from `PersistenceModule`. If a future slice introduces a new cross-cutting persistence concern, a new handler can be added then.
- **No backfill loop.** There is no pre-existing saved data that predates `PersistentEntity`, so no backward-compatibility bootstrap step is implemented. If a future deployment needs to upgrade live data, add a targeted migration at that time.
- **Shutdown flush sweeps all entities.** `PersistenceBootstrap.StopAsync` calls `FlushAllPersistentAsync`, which sweeps every entity in `EntityService` that carries `PersistentEntity` — not just the active player footprint. This guarantees a clean shutdown regardless of which rooms are occupied.
- **Character-before-account write order.** After `AccountSystem.CreateCharacterAsync` returns, `LoginFlow` calls `SaveEntityAsync(characterEntityId)` before `SaveEntityAsync(accountEntityId)`. If the server crashes between the two writes, an orphaned character file is recoverable (no account pointer to a missing character); a dangling account pointer to a missing character file is more harmful.
- **Area-scoped flush scope definition.** "Active player footprint" = entities that carry `LocationComponent` where `RoomEntityId` is in the occupied-room set, plus the player entities themselves (which carry `LocationComponent`). Dropped items, room entities, and any other `PersistentEntity` entities in those rooms are included. Entities in rooms with no connected player are excluded from the periodic flush and rely on save-on-change for their durability.
- **`FlushActivePlayerFootprintAsync` query implementation.** The footprint is resolved via `EntityService.GetAllComponents<LocationComponent>()` + a LINQ `.Where(pair => occupiedRoomIds.Contains(pair.component.RoomEntityId))` filter. No secondary index is maintained. This scales linearly with the total count of location-bearing entities, which is acceptable at current world sizes. If entity counts grow large enough that this becomes a bottleneck, an inverted room→entities index can be introduced at that point without changing the public interface.
- **Thread safety.** `ConcurrentDictionary` dirty-set is removed. The footprint sweep runs on the background timer thread; `EntityService` must be thread-safe for concurrent reads (existing assumption). No new thread-safety surface is introduced.

---

## Related

- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; introduced the dirty-set model this slice replaces.
- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — slice 5a; introduced `RoomCreatedByAdminEvent` / `RoomPropertySetByAdminEvent` subscriptions in `PersistenceHandler` that this slice replaces with save-on-change.
- [`account-character-creation.md`](account-character-creation.md) — slice 5; introduced `AccountCreatedEvent` / `CharacterCreatedEvent` subscriptions and the `CharacterHydrationHandler` `MarkDirty` call that this slice removes.
- [`docs/architecture/08-persistence.md`](../architecture/08-persistence.md) — the two-level model this slice implements.
