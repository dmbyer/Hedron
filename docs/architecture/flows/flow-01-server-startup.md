# Flow 1 — Server startup

> [Back to flows index](README.md)

**Summary.** From `dotnet run --project Server` to "telnet listener accepting connections," the host runs DI registration, then drives hosted services in registration order. Persistence hydrates first; world content loads second; flush timer arms third; the listener opens last so a connection cannot land in a half-built world.

**Trigger.** Process start.

```mermaid
sequenceDiagram
    participant Process
    participant Program as Program.Main
    participant Host as Generic Host
    participant PB as PersistenceBootstrap
    participant PSys as PersistenceSystem
    participant Bus as IEventBus
    participant WCB as WorldContentBootstrap
    participant WCL as WorldContentLoader
    participant Reg as TemplateRegistry
    participant FT as PersistenceFlushTimer
    participant TS as TelnetServer
    participant HBS as HeartbeatBackgroundService

    Process->>Program: Main(args)
    Program->>Host: ConfigureServices (DI registration)
    Program->>Host: Build → handler subscriptions wired
    Program->>Host: RunAsync()
    Host->>PB: StartAsync
    PB->>PSys: LoadAllAsync
    PSys-->>PB: hydrated entity ids
    loop per hydrated id
        PB->>Bus: Publish(EntityHydratedEvent)
    end
    PB->>Bus: Publish(WorldLoadedEvent)
    Host->>WCB: StartAsync
    WCB->>WCL: LoadAndSpawnAsync
    WCL->>Reg: register area templates
    WCL->>Reg: register room templates
    WCL->>Reg: register item templates (kind: item)
    WCL->>Reg: register mob templates (kind: mob)
    WCL->>WCL: SpawnMissingEntities (skip-on-conflict; returns newlySpawned set)
    loop per newly-spawned entity
        WCL->>PSys: SaveEntityAsync (make ID durable immediately)
    end
    WCL->>WCL: LinkRoomExits
    WCL->>WCL: PlaceItemsInRooms (newlySpawned only — attach LocationComponent from spawnRoomId)
    WCL->>WCL: PlaceMobsInRooms (newlySpawned only — attach LocationComponent from spawnRoomId)
    WCL->>WCL: ResolveStartingRoom (or void fallback)
    WCB->>Bus: Publish(WorldContentReadyEvent)
    Bus->>Bus: CharacterHydrationHandler (validate character locations; migration guards for attributes)
    Host->>FT: StartAsync (timer armed)
    Host->>TS: StartAsync (listener opens)
    Host->>HBS: StartAsync (heartbeat armed)
```

**Steps.**

1. `Program.Main` builds the generic host and registers DI singletons (`EntityService`, `IEventBus`, `ICommandDispatcher`, `ISessionManager`, broadcast, movement, world config) plus the persistence, world, and admin modules.
2. Hosted services are queued in this order: `PersistenceBootstrap`, `WorldContentBootstrap`, `PersistenceFlushTimer`, `TelnetServer`, `HeartbeatBackgroundService`. The .NET host runs each `StartAsync` to completion before the next one starts.
3. Handler subscriptions to `IEventBus` are wired *after* `Build` and *before* `RunAsync` ([`Server/Program.cs`](../../../Server/Program.cs)).
4. `PersistenceBootstrap.StartAsync` calls `PersistenceSystem.LoadAllAsync`, which scans `Persistence:DataDirectory` for `entity-*.json` files and silently re-attaches every component on each entity (no events fired during hydration). It returns the restored entity ids.
5. For each restored id, `PersistenceBootstrap` publishes `EntityHydratedEvent`. **Constraint:** handlers must not query other entities at this point — the world is partially loaded.
6. After the loop, `PersistenceBootstrap` publishes `WorldLoadedEvent`. Cross-entity startup work belongs on this event, not on per-entity hydration.
7. `WorldContentBootstrap.StartAsync` calls `WorldContentLoader.LoadAndSpawnAsync`. This: (a) reads YAML files under `World:ContentDirectory` for kinds `area`, `room`, `item`, and `mob` and registers them with `TemplateRegistry` via per-kind `ITemplateDeserializer`s (`AreaTemplateDeserializer`, `RoomTemplateDeserializer`, `ItemTemplateDeserializer`, `MobTemplateDeserializer`); (b) builds a live blueprint→entity map from existing `BlueprintComponent`s; (c) spawns any template that has no live counterpart, adds `PersistentEntity` to each, and returns the set of newly-spawned entity IDs; (d) **immediately calls `SaveEntityAsync` for every newly-spawned entity** — this makes entity IDs durable regardless of whether the server shuts down gracefully; without this step, room entity IDs would change on each restart and items' `LocationComponent.RoomEntityId` references would go stale; (e) populates `RoomComponent.Exits` by resolving each room template's blueprint-id exits to live entity ids; (f) attaches `LocationComponent { RoomEntityId }` to **newly-spawned item entities only** (those in the `newlySpawned` set) via `PlaceItemsInRooms`, resolving `ItemTemplate.SpawnRoomBlueprintId` to a live entity id — entities restored from persistence keep their saved `LocationComponent` (room or inventory slot) unchanged; if the spawn room is missing a warning is logged and the item is created without a location; (g) attaches `LocationComponent { RoomEntityId }` to **newly-spawned mob entities only** via `PlaceMobsInRooms`, resolving `MobTemplate.SpawnRoomBlueprintId` — same rules as items; if the spawn room changed in YAML but the live entity already exists, a warning is logged and a restart is required to move it; (h) sets `WorldConfiguration.StartingRoomEntityId` from `World:StartingRoomBlueprintId`. If the content directory is missing or empty, a single hardcoded `room.void` is seeded (also gets `PersistentEntity` and is saved immediately) and a warning is logged. After `LoadAndSpawnAsync` completes, `WorldContentBootstrap` publishes `WorldContentReadyEvent`. `CharacterHydrationHandler` (priority `HandlerPriority.Domain`) validates every hydrated character entity's `LocationComponent.RoomEntityId` at this point, resetting stale references to `StartingRoomEntityId`. Also attaches empty-default `AttributesComponent` and `PoolsComponent` to any character that lacks them (migration guards for entities persisted before slice 8a — extended in slice 8a).
8. `PersistenceFlushTimer.StartAsync` arms a `PeriodicTimer` reading `Persistence:FlushIntervalSeconds` (default 60). See [Flow 4](flow-04-persistence-flush-cycle.md) for the full flush-cycle trace.
9. `TelnetServer.StartAsync` opens the TCP listener on `Server:Port`. Connections accepted from this point forward see a fully assembled world.
10. `HeartbeatBackgroundService.StartAsync` starts the `PeriodicTimer` on a background thread; `StartAsync` itself returns immediately. The first tick fires after `Heartbeat:IntervalMs` (default 2000 ms) — the world is fully assembled and the listener is open before any tick can land. See [Flow 16](flow-16-heartbeat-tick.md) for the tick-cycle trace.
11. **Shutdown path.** When the host shuts down, `PersistenceBootstrap.StopAsync` calls `PersistenceSystem.FlushAllPersistentAsync`, which iterates every entity carrying `PersistentEntity` and writes it to disk — a complete sweep regardless of which rooms are occupied. This replaced the old `FlushAsync` (dirty-set sweep) when the two-level persistence model was introduced.

**Cross-references.**
- [`docs/architecture/05-configuration.md`](../05-configuration.md) — startup-relevant config keys
- [`docs/reference/systems.md`](../../reference/systems.md) — `PersistenceSystem`, `WorldContentLoader`, `TemplateRegistry`
- [`docs/use-cases/persistence-substrate.md`](../../use-cases/persistence-substrate.md), [`docs/use-cases/world-content-loading-and-admin-substrate.md`](../../use-cases/world-content-loading-and-admin-substrate.md) — slice specs
