# Use Case: Persistence Substrate

**Status:** implemented
**Actors:** System
**Module:** `Core/Modules/Persistence/`

---

## Description

Infrastructure slice that gives every future feature a working save/load substrate. `PersistenceSystem` discovers all `[Persistent]`-tagged components on an entity, serializes them to disk, and can reload them silently (without publishing events). Dirty-tracking ensures only mutated entities are flushed. The slice introduces no gameplay or player-visible UI; its value is purely as an unlock for all subsequent slices that need state to survive restart.

---

## Preconditions

- Phase 2 is complete: `EntityService`, `IEventBus`, and at least the three MVP components (`PlayerComponent`, `LocationComponent`, `RoomComponent`) exist.
- `PersistentEffectsComponent` and `TransientEffectsComponent` cross-cutting components have been added to `Core/ECS/Components/` (they may be stubs at this point — shape is defined here, content populated by later slices).
- A writable directory is available at a configured path (default `data/entities/`).

---

## Postconditions

- Every entity that has at least one `[Persistent]` component can be saved to disk and reloaded into a fresh `EntityService` without triggering any event-bus events during component attachment.
- Only `[Persistent]`-tagged components are written to disk; transient components are omitted.
- `PersistenceSystem.FlushAsync()` writes only entities whose dirty flag is set; clean entities are not written.
- On load, hydrated entities match their pre-save component data exactly.
- `TransientEffectsComponent` is never written to disk; `PersistentEffectsComponent` always is.
- No gameplay behaviour changes; no player-visible output is added.

---

## Main Flow

1. **Startup — hydration.** `PersistenceBootstrap` (an `IHostedService`) calls `IPersistenceSystem.LoadAllAsync()` in `StartAsync`. `LoadAllAsync` is a pure Core System operation: for each entity file found on disk, it calls `entityService.RestoreEntity(id)`, deserializes each stored component blob via `IComponentSerializer`, and attaches them silently via `entityService.AddComponent`. No events are published during component attachment; transient components are left absent. `LoadAllAsync` returns the list of restored entity IDs. `PersistenceBootstrap.StartAsync` then iterates the returned IDs and publishes `EntityHydratedEvent` for each. After the loop, `WorldLoadedEvent` is published once.

2. **Runtime — dirty-tracking.** `PersistenceHandler` subscribes at priority 90 to every state-change event that mutates `[Persistent]` data (e.g. `PlayerMovedEvent`, `PoolsChangedEvent`, `ItemEquippedEvent`). On receipt, it calls `IPersistenceSystem.MarkDirty(entityId)`, which sets a flag in an in-memory dirty set. Dirty-tracking is per-entity.

3. **Periodic flush.** `PersistenceFlushTimer` (a hosted background service) calls `IPersistenceSystem.FlushAsync()` on a configurable interval (read from `IConfiguration["Persistence:FlushIntervalSeconds"]`, default 60 s). `PersistenceSystem` iterates the dirty set, serializes each entity's `[Persistent]` components, performs an atomic write-and-rename (`entity-<id>.tmp` → `entity-<id>.json`), then clears the dirty flag. Errors for individual entities are logged and skipped (best-effort); a failed entity remains dirty and will retry on the next flush.

4. **Shutdown flush.** `PersistenceBootstrap.StopAsync` calls `FlushAsync()` synchronously before the process exits, ensuring no dirty entities are lost.

5. **Explicit save (admin path).** `IPersistenceSystem.SaveEntityAsync(uint entityId)` can be called directly by domain systems or admin commands to force an immediate single-entity flush without waiting for the timer. Marks the entity clean afterwards.

6. **Component discovery.** `PersistenceSystem` uses reflection once at startup to build a `Type → bool` map of every `IComponent` implementor, recording which types carry `[PersistentAttribute]`. Serialization iterates `entityService.GetAllComponents(entityId)` and filters by this map.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `EntityHydratedEvent(uint EntityId)` | `PersistenceBootstrap.StartAsync` (loop over IDs returned by `LoadAllAsync`) | Per entity | Fired after one entity's components are fully attached. Use for single-entity transient-state setup. **Handlers must not query other entities** — other entities may not be loaded yet. |
| `WorldLoadedEvent` | `PersistenceBootstrap.StartAsync` | Once | Fired after the `EntityHydratedEvent` loop completes and all entities are in-world. Safe for cross-entity startup work (occupancy indexes, re-establishing references). |
| `EntityPersistedEvent(uint EntityId)` | Caller of `IPersistenceSystem.SaveEntityAsync` (e.g. an admin command handler) | Per entity | Confirms a single entity was written. Informational; logging is the expected consumer. |

### Hydration event constraints

- `EntityHydratedEvent` handlers **must not query other entities** or publish further events. The world is partially loaded at time of fire.
- `WorldLoadedEvent` handlers are the approved place for cross-entity startup work.
- `LoadAllAsync` must complete — and `WorldLoadedEvent` must dispatch — **before `TelnetServer` begins accepting connections**.
- These constraints must hold even after combat-state persistence is added (Phase 3 slice 8+): at that point, mob threat tables referencing player entities must be rebuilt in a `WorldLoadedEvent` handler, not an `EntityHydratedEvent` handler.

---

## Systems / Handlers Involved

### IPersistenceSystem (new — core system)

```
IPersistenceSystem
  void MarkDirty(uint entityId)
  bool IsDirty(uint entityId)
  Task FlushAsync(CancellationToken ct = default)
  Task SaveEntityAsync(uint entityId, CancellationToken ct = default)
  Task LoadAllAsync(CancellationToken ct = default)
```

Lives at `Core/Systems/PersistenceSystem.cs` (cross-cutting). Depends on `EntityService`, `IComponentSerializer`, `IComponentTypeRegistry`, and `IEventBus`.

### IComponentSerializer (new — core utility)

```
IComponentSerializer
  string Serialize(IComponent component)
  IComponent Deserialize(string typeName, string data)
```

Default implementation uses `System.Text.Json`. Lives at `Core/Systems/ComponentSerializer.cs`.

### IComponentTypeRegistry (new — core utility)

```
IComponentTypeRegistry
  bool IsPersistent(Type componentType)
  Type? Resolve(string typeName)
  IReadOnlyList<Type> AllPersistentTypes()
```

Populated via reflection at startup over the assembly containing `IComponent`. Lives at `Core/Systems/ComponentTypeRegistry.cs`.

### PersistenceHandler (first real implementation — catalogued handler)

**Events subscribed:** state-change events that modify `[Persistent]` data. At this slice's scope the set is empty (no MVP component carries `[Persistent]` yet). The handler is wired and no-op initially; subsequent slices add their events.

**Priority:** 90 on all subscribed events.

Lives at `Core/Handlers/PersistenceHandler.cs`.

### PersistenceFlushTimer (new — hosted service)

`BackgroundService` calling `IPersistenceSystem.FlushAsync()` on the configured interval. Reads `IConfiguration["Persistence:FlushIntervalSeconds"]` with default 60. Lives at `Server/PersistenceFlushTimer.cs`.

### PersistenceBootstrap (new — hosted service)

`IHostedService` running `LoadAllAsync` in `StartAsync` and `FlushAsync` in `StopAsync`. Must be registered so that `StartAsync` completes before `TelnetServer.ExecuteAsync` begins accepting connections. Lives at `Server/PersistenceBootstrap.cs`.

---

## Design Notes

- **Atomic write.** Write to `<id>.tmp`, then `File.Move(..., overwrite: true)`. Avoids half-written files surviving a crash.
- **Silent hydration.** `LoadAllAsync` never calls `IEventBus.PublishAsync` during component attachment. `EntityHydratedEvent` fires only after all components for one entity are restored; `WorldLoadedEvent` fires once after all entities are loaded.
- **Conflict model (blueprint vs. persisted).** This slice does not implement blueprint-seeded world loading (Phase 3 slice 3). Hydration loads exactly what was saved.
- **Thread safety.** The dirty set is a `ConcurrentDictionary<uint, byte>`. `FlushAsync` snapshots the set under a brief lock, then writes outside the lock.
- **`[Persistent]` attribute.** `[PersistentAttribute]` is a sealed class at `Core/ECS/PersistentAttribute.cs`, `[AttributeUsage(AttributeTargets.Class)]` only.
- **No `[Persistent]` on MVP components in this slice.** `PlayerComponent`, `LocationComponent`, `RoomComponent` remain transient. They will be revisited when account/character-creation lands (slice 2).
- **Flush error policy.** Best-effort: a single-entity serialization failure is logged and skipped. The entity stays dirty and retries on the next flush.
- **No encryption or compression.** Plain UTF-8 JSON. Hardening deferred to Phase 4.
- **Storage path.** `IConfiguration["Persistence:DataDirectory"]` (default `data/entities/`). Directory created on startup if absent.
- **Configuration.** See `docs/architecture/05-configuration.md` — flush interval and data directory are Category 1 operational settings read from `IConfiguration`.

---

## Module Entry-Point

`Core/Modules/Persistence/PersistenceModule.cs` — `AddPersistenceModule(IServiceCollection services, IConfiguration config)` registers `PersistentAttribute`-scanning utilities, `IComponentTypeRegistry`, `IComponentSerializer`, `IPersistenceSystem`, and `PersistenceHandler`. `Server/Program.cs` calls it; `PersistenceBootstrap` and `PersistenceFlushTimer` are registered as hosted services separately in `Server/Program.cs`.

---

## Related

- `world-content-loading.md` — slice 2; introduces the blueprint-seeds-world conflict model deferred above, and the in-game admin substrate (Ticket B resolution).
- `account-character-creation.md` — slice 3; player entities created here will be the first real `[Persistent]` entities.
- `inventory-get-drop.md` — slice 4; `InventoryComponent` will carry `[Persistent]` and trigger dirty-marking on pick-up/drop.

For the current slice queue and ordering rationale, see [`../roadmap/plan.md`](../roadmap/plan.md).
