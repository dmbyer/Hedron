# Phase 3 slice 1 — Persistence substrate (completed)

> Implemented and merged on `master`. The full feature spec lives in [`../../implementation-plans/persistence-substrate.md`](../../implementation-plans/persistence-substrate.md). This file records the as-built state and any deviations from the spec.

## Outcome

`PersistenceSystem` discovers all `[Persistent]`-tagged components on an entity, serializes them to disk via `System.Text.Json`, and reloads them silently (without publishing events). Dirty-tracking ensures only mutated entities are flushed. No gameplay changes; pure infrastructure unlock for every subsequent slice that needs save-survival.

## Shipped pieces

| Surface | Location |
|---|---|
| `IPersistenceSystem` / `PersistenceSystem` | `Core/Systems/PersistenceSystem.cs` |
| `IComponentSerializer` / `ComponentSerializer` (System.Text.Json, camelCase, `JsonStringEnumConverter`) | `Core/Systems/ComponentSerializer.cs` |
| `IComponentTypeRegistry` / `ComponentTypeRegistry` (reflection-built) | `Core/Systems/ComponentTypeRegistry.cs` |
| `[PersistentAttribute]` | `Core/ECS/PersistentAttribute.cs` |
| `PersistenceHandler` (priority 90, no-op until later slices add events) | `Core/Handlers/PersistenceHandler.cs` |
| `PersistenceFlushTimer` (`BackgroundService`) | `Server/PersistenceFlushTimer.cs` |
| `PersistenceBootstrap` (`IHostedService`) | `Server/PersistenceBootstrap.cs` |
| Module entry point | `Core/Modules/Persistence/PersistenceModule.cs` |
| Events (`EntityHydratedEvent`, `WorldLoadedEvent`, `EntityPersistedEvent`) | `Core/Modules/Persistence/Events/` |

## Configuration

Read via `IConfiguration` per [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md):

- `Persistence:FlushIntervalSeconds` — default 60
- `Persistence:DataDirectory` — default `data/entities/`

## Notable design points (recap)

- **Atomic writes.** Write to `<id>.tmp`, then `File.Move(..., overwrite: true)`.
- **Silent hydration.** `LoadAllAsync` never publishes events during component attachment. `EntityHydratedEvent` fires after one entity is fully restored; `WorldLoadedEvent` fires once after the loop completes. `LoadAllAsync` and `WorldLoadedEvent` complete before `TelnetServer` accepts connections.
- **Hydration constraint.** `EntityHydratedEvent` handlers must not query other entities or publish further events — world is partially loaded. Cross-entity startup work belongs on `WorldLoadedEvent`.
- **Best-effort flush errors.** A single-entity serialization failure is logged and skipped; the entity stays dirty and retries.
- **No `[Persistent]` on MVP components yet.** `PlayerComponent`, `LocationComponent`, `RoomComponent` remain transient; revisited when account/character creation lands.

## Deviations from the use-case doc

None at time of merge.

## Follow-ups unlocked by this slice

- Account/character creation (next slice) is the first slice that will tag a component `[Persistent]` and exercise the dirty-tracking pipeline.
- World content loading slice introduces the blueprint-vs-persisted conflict model deferred here.
