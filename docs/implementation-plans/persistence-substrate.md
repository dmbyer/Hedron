# Use Case: Persistence Substrate

**Status:** implemented
**Actors:** System
**Module:** `Core/Modules/Persistence/`

> **Note — flush model superseded.** The dirty-set flush, `MarkDirty`, and `PersistenceHandler` described below are the slice-1 design and are now historical. The current persistence model is the two-level opt-in in [`persistence-two-level-model.md`](persistence-two-level-model.md). The *hydration* model (silent load, `EntityHydratedEvent`/`WorldLoadedEvent`), the `ComponentSerializer`, and the `ComponentTypeRegistry` described here remain current.

---

## Description

Infrastructure slice that gives every future feature a working save/load substrate. `PersistenceSystem` discovers all `[Persistent]`-tagged components on an entity, serializes them to disk, and can reload them silently (without publishing events). The slice introduces no gameplay or player-visible UI; its value is purely as an unlock for all subsequent slices that need state to survive restart.

---

## Preconditions

- Phase 2 is complete: `EntityService`, `IEventBus`, and at least the three MVP components (`PlayerComponent`, `LocationComponent`, `RoomComponent`) exist.
- A writable directory is available at a configured path (default `data/entities/`).

---

## Postconditions

- Every entity that has at least one `[Persistent]` component can be saved to disk and reloaded into a fresh `EntityService` without triggering any event-bus events during component attachment.
- Only `[Persistent]`-tagged components are written to disk; transient components are omitted.
- On load, hydrated entities match their pre-save component data exactly.
- `EffectsComponent` is tagged `[Persistent]`; its `[JsonConverter]` writes only `UntilRemoved` effects — timed and other transient-lifetime effects are dropped at serialization time.
- No gameplay behaviour changes; no player-visible output is added.

---

## Main Flow

> The flush steps below reflect the slice-1 dirty-set model, now replaced — see the banner and [`persistence-two-level-model.md`](persistence-two-level-model.md). The hydration steps (1, 6) remain accurate.

1. **Startup — hydration.** `PersistenceBootstrap` (an `IHostedService`) calls `IPersistenceSystem.LoadAllAsync()` in `StartAsync`. For each entity file on disk it calls `entityService.RestoreEntity(id)`, deserializes each stored component via `IComponentSerializer`, and attaches them silently via `entityService.AddComponent` — no events published during attachment, transient components left absent. `LoadAllAsync` returns the restored entity ids. `PersistenceBootstrap` then publishes `EntityHydratedEvent` per id, and `WorldLoadedEvent` once after the loop.
2. *(superseded)* Runtime dirty-tracking via `PersistenceHandler` → `MarkDirty`.
3. *(superseded)* Periodic `FlushAsync()` over the dirty set.
4. *(superseded)* Shutdown `FlushAsync()`.
5. **Explicit save (admin path).** `IPersistenceSystem.SaveEntityAsync(uint entityId)` forces an immediate single-entity flush. (Retained in the two-level model.)
6. **Component discovery.** `PersistenceSystem` uses reflection once at startup to build a `Type → bool` map of every `IComponent` implementor, recording which carry `[Persistent]`. Serialization iterates `entityService.GetAllComponents(entityId)` and filters by this map.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `EntityHydratedEvent(uint EntityId)` | `PersistenceBootstrap.StartAsync` (loop over ids from `LoadAllAsync`) | Per entity | Fired after one entity's components are fully attached. **Handlers must not query other entities** — others may not be loaded yet. |
| `WorldLoadedEvent` | `PersistenceBootstrap.StartAsync` | Once | Fired after the hydration loop. Safe for cross-entity startup work. |

### Hydration event constraints

- `EntityHydratedEvent` handlers must not query other entities or publish further events — the world is partially loaded.
- `WorldLoadedEvent` handlers are the approved place for cross-entity startup work.
- `LoadAllAsync` must complete — and `WorldLoadedEvent` must dispatch — before `TelnetServer` begins accepting connections.

---

## Design Notes

- **Atomic write.** Write to `<id>.tmp`, then `File.Move(..., overwrite: true)`. Avoids half-written files surviving a crash.
- **Silent hydration.** `LoadAllAsync` never publishes during component attachment. `EntityHydratedEvent` fires only after all components for one entity are restored; `WorldLoadedEvent` fires once after all entities are loaded.
- **`[Persistent]` attribute.** `[PersistentAttribute]` is a sealed class at `Core/ECS/PersistentAttribute.cs`, `[AttributeUsage(AttributeTargets.Class)]`.
- **No encryption or compression.** Plain UTF-8 JSON. Hardening deferred to Phase 4.
- **Storage path.** `IConfiguration["Persistence:DataDirectory"]` (default `data/entities/`). Created on startup if absent.

---

## Related

- [`world-content-loading-and-admin-substrate.md`](../features/world/world.md) — slice 2; blueprint-seeded world loading and the in-game admin substrate.
- [`account-character-creation.md`](account-character-creation.md) — slice 5; the first real `[Persistent]` user entities.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; replaces this slice's dirty-set flush with the two-level opt-in model.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
