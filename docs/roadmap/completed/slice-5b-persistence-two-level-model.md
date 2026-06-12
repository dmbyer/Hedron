# Phase 3 slice 5b — Persistence two-level model (completed)

> Implemented on branch `claude/zealous-darwin-61b4b5` (PR #76). Full feature spec: [`../../implementation-plans/persistence-two-level-model.md`](../../implementation-plans/persistence-two-level-model.md).

## Outcome

The persistence layer is redesigned from a global dirty-set sweep to a two-level opt-in model. `PersistentEntity` — a new zero-data `[Persistent]` marker component — explicitly opts an entity into persistence; `PersistenceSystem.WriteEntityAsync` is now gated on `HasComponent<PersistentEntity>` and silently skips any entity that lacks the marker regardless of how many `[Persistent]`-typed components it carries. The periodic flush changes from a full dirty-set sweep to an area-scoped player footprint (`FlushActivePlayerFootprintAsync`), which bounds flush cost to the active player population; shutdown uses `FlushAllPersistentAsync` for a complete sweep. `PersistenceHandler` is deleted entirely; save-on-change (`SaveEntityAsync`) is now called directly by the admin commands and lifecycle handlers that own each mutation.

## Shipped pieces

| Surface | Location |
|---|---|
| `PersistentEntity` zero-data marker component (tagged `[Persistent]`) | `Core/ECS/Components/PersistentEntity.cs` |
| `IPersistenceSystem` — removed `MarkDirty`, `IsDirty`, `FlushAsync`; added `FlushActivePlayerFootprintAsync` and `FlushAllPersistentAsync` | `Core/Systems/IPersistenceSystem.cs` |
| `PersistenceSystem` — removed `_dirtySet`; `WriteEntityAsync` gains `PersistentEntity` guard; new `FlushActivePlayerFootprintAsync` and `FlushAllPersistentAsync` methods | `Core/Systems/PersistenceSystem.cs` |
| `PersistenceHandler` — deleted | ~~`Core/Handlers/PersistenceHandler.cs`~~ |
| `PersistenceModule` — removed `PersistenceHandler` DI registration | `Core/Modules/Persistence/PersistenceModule.cs` |
| `PersistenceBootstrap.StopAsync` — changed `FlushAsync` → `FlushAllPersistentAsync` | `Server/PersistenceBootstrap.cs` |
| `PersistenceFlushTimer` — injected `ISessionManager`; collects occupied room ids; calls `FlushActivePlayerFootprintAsync` | `Server/PersistenceFlushTimer.cs` |
| `AccountSystem.CreateAccountAsync` — removed `MarkDirty`; added `PersistentEntity` to account entity | `Core/Modules/Account/Systems/AccountSystem.cs` |
| `AccountSystem.CreateCharacterAsync` — removed `MarkDirty`; added `PersistentEntity` to character entity | `Core/Modules/Account/Systems/AccountSystem.cs` |
| `AccountSystem.RecordLogout` — removed `MarkDirty` (save delegated to `PlayerSessionHandler`) | `Core/Modules/Account/Systems/AccountSystem.cs` |
| `PlayerSessionHandler` — injected `IPersistenceSystem`; added `SaveEntityAsync(characterEntityId)` after `RecordLogout` on disconnect | `Core/Modules/Session/Handlers/PlayerSessionHandler.cs` |
| `CharacterHydrationHandler` — replaced `MarkDirty` with `await SaveEntityAsync` on stale room correction | `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs` |
| `LoginFlow` — injected `IPersistenceSystem`; saves character-then-account after `CreateCharacterAsync`; `AccountCreatedEvent` deferred until after both saves | `Server/Sessions/LoginFlow.cs` |
| `TelnetSession` — passes `IPersistenceSystem` to `LoginFlow` | `Server/Sessions/TelnetSession.cs` |
| `TelnetServer` — injected `IPersistenceSystem`; passes to `TelnetSession` | `Server/Sessions/TelnetServer.cs` |
| `DigCommand` — added `SaveEntityAsync(newRoomId)` + `SaveEntityAsync(sourceRoomId)` after room creation | `Core/Modules/Admin/Commands/DigCommand.cs` |
| `SetCommand` — added `SaveEntityAsync(roomId)` after property mutation | `Core/Modules/Admin/Commands/SetCommand.cs` |
| `RoomBuilderSystem.CreateRoom` — added `PersistentEntity` to new room entity | `Core/Modules/Admin/Systems/RoomBuilderSystem.cs` |
| `WorldContentLoader.SpawnMissingEntities` / `SeedVoidRoom` — added `PersistentEntity` to every spawned entity | `Core/Modules/World/Systems/WorldContentLoader.cs` |
| `Program.cs` — removed 9 `bus.Subscribe` calls for `PersistenceHandler`; removed `persistenceHandler` local | `Server/Program.cs` |
| `docs/reference/handlers.md` — `PersistenceHandler` entry removed; `PlayerSessionHandler` + `CharacterHydrationHandler` updated with `IPersistenceSystem` in Uses and save-on-change description | — |
| `docs/reference/systems.md` — `PersistenceSystem` interface updated; `AccountSystem` description updated; `WorldContentLoader` and `RoomBuilderSystem` notes added | — |
| `docs/architecture/06-flows.md` — Flow 1 (startup `PersistentEntity` addition + shutdown note), Flow 2 (disconnect save path), Flow 4 (full rewrite to area-scoped model), Flow 7 (save-before-publish login), Flow 8 (remove `PH`, direct `SaveEntityAsync`) updated | — |
| `docs/architecture/03-events.md` — `PersistenceHandler` removed from `PlayerDeathEvent` subscriber table; save-on-change note added | — |
| `docs/implementation-plans/persistence-two-level-model.md` — Status set to `implemented` | — |

## Spec-review provenance

**Spec-mode gate:** Passed before implementation. No blocking findings. Key pre-implementation decisions confirmed in the spec:
- Character-before-account write order rationale documented (crash-safety: orphaned character file is recoverable; dangling account pointer is not).
- `AccountSystem` confirmed to not call `SaveEntityAsync` — all persistence calls belong to `LoginFlow` (INV-5).
- `PlayerMovedEvent` dirty-marking removal justified: area-scoped flush naturally covers player entities in occupied rooms; the flush interval is the acceptable loss window.
- No backfill migration required (no pre-existing data predates `PersistentEntity`).

**Code-mode gate:** Required before merge per Phase 3 ground rule 6.

## Notable design points

- **Character-before-account write order.** `LoginFlow` calls `SaveEntityAsync(characterEntityId)` before `SaveEntityAsync(accountEntityId)`. If the server crashes between the two writes, an orphaned character file is recoverable by the admin; a dangling account pointer to a missing character file is more harmful and harder to detect.
- **`AccountCreatedEvent` deferred until after saves.** The registration path previously published `AccountCreatedEvent` immediately after `CreateAccountAsync` returned. Under the new model the event is deferred until both the character and account are on disk — handlers that subscribe to `AccountCreatedEvent` can rely on the entities being durable.
- **Area-scoped flush bounds cost.** The periodic flush writes only entities in rooms occupied by at least one connected player. Entities in unoccupied rooms rely on save-on-change for durability. Admin commands and lifecycle transitions (`dig`, `set`, login, logout) call `SaveEntityAsync` directly, so the flush interval only represents the loss window for player state changes (movement, future stat updates) — not authored content.
- **`PersistenceHandler` had no residual responsibility.** Once all `MarkDirty` call-sites were replaced with direct `SaveEntityAsync` calls or `PersistentEntity` construction-time additions, the handler's event subscriptions collapsed to zero. The class was deleted rather than left empty.
- **`FlushAsync` removed from interface.** The old method was a global dirty-set sweep — semantically distinct from both new methods. Keeping the name would have misled future callers. `PersistenceBootstrap.StopAsync` (the sole caller) was updated to `FlushAllPersistentAsync`.
- **No new thread-safety surface.** The `ConcurrentDictionary` dirty-set is removed. The footprint sweep runs on the background timer thread; `EntityService` is already assumed thread-safe for concurrent reads.

## Deviations from the use-case doc

None. All postconditions were satisfied as written. The implementation matches the spec's stated interface, component construction sites, save order, and event deferral.

## Follow-ups unlocked

- **Slice 6 — Items + inventory.** The persistence model is now correct for new entity types: any slice that creates entities simply adds `PersistentEntity` at construction and the two-level model handles the rest without changes to `PersistenceSystem`.
- **Future persistence improvements.** A secondary room→entity index (for faster footprint queries at large world sizes) can be added without changing the public `IPersistenceSystem` interface.
- **Save-on-change pattern is established.** Future handlers and commands that own state mutations follow the same pattern: domain system mutates, initiator/handler calls `SaveEntityAsync`. No new `PersistenceHandler`-style cross-cutting subscriber is needed.
