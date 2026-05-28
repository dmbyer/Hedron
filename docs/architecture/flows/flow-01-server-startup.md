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
    WCL->>WCL: SpawnMissingEntities (skip if already live; returns newlySpawned set)
    Note over WCL: No PersistentEntity on world content; no SaveEntityAsync
    WCL->>WCL: LinkRoomExits (both RoomEntityId and RoomBlueprintId set on LocationComponent)
    WCL->>WCL: PlaceItemsInRooms (newlySpawned only — attach LocationComponent{RoomEntityId,RoomBlueprintId})
    WCL->>WCL: PlaceMobsInRooms (newlySpawned only — attach LocationComponent{RoomEntityId,RoomBlueprintId})
    WCL->>WCL: ResolveStartingRoom (or void fallback)
    WCB->>Bus: Publish(WorldContentReadyEvent)
    Bus->>Bus: CharacterHydrationHandler (resolve RoomBlueprintId→RoomEntityId; fallback to starting room; migration guards)
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
7. `WorldContentBootstrap.StartAsync` calls `WorldContentLoader.LoadAndSpawnAsync`. This: (a) reads YAML files under `World:ContentDirectory` for kinds `area`, `room`, `item`, and `mob` and registers them with `TemplateRegistry` via per-kind `ITemplateDeserializer`s; (b) builds a live blueprint→entity map from existing `BlueprintComponent`s (at cold start this is empty — only persistent entities like players are in SQLite, and none carry `BlueprintComponent`); (c) spawns any template that has no live counterpart and returns the set of newly-spawned entity IDs — **no `PersistentEntity` is added to world content, and no `SaveEntityAsync` is called**; (d) populates `RoomComponent.Exits` by resolving each room template's blueprint-id exits to live entity ids; (e) attaches `LocationComponent { RoomEntityId, RoomBlueprintId }` to **newly-spawned item entities only** via `PlaceItemsInRooms`, resolving `ItemTemplate.SpawnRoomBlueprintId` — both fields are set so that `CharacterHydrationHandler` can resolve locations after restart; (f) attaches `LocationComponent { RoomEntityId, RoomBlueprintId }` to **newly-spawned mob entities only** via `PlaceMobsInRooms` — same rules; (g) sets `WorldConfiguration.StartingRoomEntityId` from `World:StartingRoomBlueprintId`. If the content directory is missing or empty, a single hardcoded `room.void` is seeded (no `PersistentEntity`, YAML is written immediately) and a warning is logged. After `LoadAndSpawnAsync` completes, `WorldContentBootstrap` publishes `WorldContentReadyEvent`. `CharacterHydrationHandler` (priority `HandlerPriority.Domain`) resolves each persistent entity's `LocationComponent.RoomBlueprintId` to the current live `RoomEntityId`; entities with an unresolvable blueprint fall back to `StartingRoomEntityId` (characters) or are destroyed (other persistent entities). Also attaches empty-default migration components (`InventoryComponent`, `EquipmentComponent`, `AttributesComponent`, `PoolsComponent`) to any character that lacks them.
8. `PersistenceFlushTimer.StartAsync` arms a `PeriodicTimer` reading `Persistence:FlushIntervalSeconds` (default 60). See [Flow 4](flow-04-persistence-flush-cycle.md) for the full flush-cycle trace.
9. `TelnetServer.StartAsync` opens the TCP listener on `Server:Port`. Connections accepted from this point forward see a fully assembled world.
10. `HeartbeatBackgroundService.StartAsync` starts the `PeriodicTimer` on a background thread; `StartAsync` itself returns immediately. The first tick fires after `Heartbeat:IntervalMs` (default 2000 ms) — the world is fully assembled and the listener is open before any tick can land. See [Flow 16](flow-16-heartbeat-tick.md) for the tick-cycle trace.
11. **Shutdown path.** When the host shuts down, `PersistenceBootstrap.StopAsync` calls `PersistenceSystem.FlushAllPersistentAsync`, which iterates every entity carrying `PersistentEntity` and writes it to disk — a complete sweep regardless of which rooms are occupied. This replaced the old `FlushAsync` (dirty-set sweep) when the two-level persistence model was introduced.

**Cross-references.**
- [`docs/architecture/05-configuration.md`](../05-configuration.md) — startup-relevant config keys
- [`docs/reference/systems.md`](../../reference/systems.md) — `PersistenceSystem`, `WorldContentLoader`, `TemplateRegistry`
- [`docs/use-cases/persistence-substrate.md`](../../use-cases/persistence-substrate.md), [`docs/use-cases/world-content-loading-and-admin-substrate.md`](../../use-cases/world-content-loading-and-admin-substrate.md) — slice specs
