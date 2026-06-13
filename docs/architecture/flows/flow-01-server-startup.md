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
    participant RVB as RegistryValidationBootstrap
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
    WCL->>WCL: LinkRoomAreas (set RoomComponent.AreaEntityId from RoomTemplate.AreaId)
    WCL->>WCL: ResolveStartingRoom (or void fallback)
    WCB->>Bus: Publish(WorldContentReadyEvent)
    Bus->>Bus: CharacterHydrationHandler (resolve RoomBlueprintId→RoomEntityId; fallback to starting room; migration guards)
    Host->>RVB: StartAsync
    RVB->>RVB: sweep registries — assert ability→effect/aspect refs, StartingAbilities→ability refs; verify AspectComposition normalization
    Note over RVB: fail-fast: throw + full report on any error; publish nothing on success (INV-10)
    Host->>FT: StartAsync (timer armed)
    Host->>TS: StartAsync (listener opens)
    Host->>HBS: StartAsync (heartbeat armed)
```

**Steps.**

1. `Program.Main` builds the generic host and registers DI singletons (`EntityService`, `IEventBus`, `ICommandDispatcher`, `ISessionManager`, broadcast, movement, world config) plus the persistence, world, and admin modules.
2. Hosted services are queued in this order: `PersistenceBootstrap`, `WorldContentBootstrap`, `RegistryValidationBootstrap`, `PersistenceFlushTimer`, `TelnetServer`, `HeartbeatBackgroundService`. The .NET host runs each `StartAsync` to completion before the next one starts.
3. Handler subscriptions to `IEventBus` are wired *after* `Build` and *before* `RunAsync` ([`Server/Program.cs`](../../../Server/Program.cs)).
4. `PersistenceBootstrap.StartAsync` calls `PersistenceSystem.LoadAllAsync`, which reads all rows from the SQLite `entity_components` table, groups them by `entity_id`, and silently re-attaches every `[Persistent]`-tagged component on each entity via `EntityService.RestoreEntity` (no events fired during hydration). It returns the restored entity ids.
5. For each restored id, `PersistenceBootstrap` publishes `EntityHydratedEvent`. **Constraint:** handlers must not query other entities at this point — the world is partially loaded.
6. After the loop, `PersistenceBootstrap` publishes `WorldLoadedEvent`. Cross-entity startup work belongs on this event, not on per-entity hydration.
7. `WorldContentBootstrap.StartAsync` calls `WorldContentLoader.LoadAndSpawnAsync`. This: (a) reads YAML files under `World:ContentDirectory` for kinds `area`, `room`, `item`, and `mob` and registers them with `TemplateRegistry` via per-kind `ITemplateDeserializer`s; (b) builds a live blueprint→entity map from existing `BlueprintComponent`s (at cold start this is empty — only persistent entities like players are in SQLite, and none carry `BlueprintComponent`); (c) spawns any template that has no live counterpart and returns the set of newly-spawned entity IDs — **no `PersistentEntity` is added to world content, and no `SaveEntityAsync` is called**; (d) populates `RoomComponent.Exits` by resolving each room template's blueprint-id exits to live entity ids; (e) attaches `LocationComponent { RoomEntityId, RoomBlueprintId }` to **newly-spawned item entities only** via `PlaceItemsInRooms`, resolving `ItemTemplate.SpawnRoomBlueprintId` — both fields are set so that `CharacterHydrationHandler` can resolve locations after restart; (f) attaches `LocationComponent { RoomEntityId, RoomBlueprintId }` to **newly-spawned mob entities only** via `PlaceMobsInRooms` — same rules; (g) sets `RoomComponent.AreaEntityId` on each room entity by resolving the room template's `AreaId` blueprint id to a live entity id via `LinkRoomAreas` — rooms whose template has no `AreaId` are left at 0; unknown `AreaId` references log a warning and leave `AreaEntityId` at 0; (h) sets `WorldConfiguration.StartingRoomEntityId` from `World:StartingRoomBlueprintId`. If the content directory is missing or empty, a single hardcoded `room.void` is seeded (no `PersistentEntity`, YAML is written immediately) and a warning is logged. After `LoadAndSpawnAsync` completes, `WorldContentBootstrap` publishes `WorldContentReadyEvent`. `CharacterHydrationHandler` (priority `HandlerPriority.Domain`) resolves each persistent entity's `LocationComponent.RoomBlueprintId` to the current live `RoomEntityId`; entities with an unresolvable blueprint fall back to `StartingRoomEntityId` (characters) or are destroyed (other persistent entities). Also attaches empty-default migration components (`InventoryComponent`, `EquipmentComponent`, `AttributesComponent`, `PoolsComponent`) to any character that lacks them.
8. `RegistryValidationBootstrap.StartAsync` sweeps every definition registry: for each `AbilityDefinition` it asserts that each `Effects` id resolves in `IEffectRegistry` and that any non-null `Aspect` composition resolves in `IAspectRegistry`; asserts every `StartingAbilities`-config id resolves in `IAbilityRegistry`; and asserts that every authored `AspectComposition` is either empty or sums exactly to 100. On any failure it emits a full error report and throws, refusing to complete startup (fail-fast, INV-10). On success it publishes nothing — it is a closed mechanical sweep with no side effects.
9. `PersistenceFlushTimer.StartAsync` arms a `PeriodicTimer` reading `Persistence:FlushIntervalSeconds` (default 60). See [Flow 4](flow-04-persistence-flush-cycle.md) for the full flush-cycle trace.
10. `TelnetServer.StartAsync` opens the TCP listener on `Server:Port`. Connections accepted from this point forward see a fully assembled world.
11. `HeartbeatBackgroundService.StartAsync` starts the `PeriodicTimer` on a background thread; `StartAsync` itself returns immediately. The first tick fires after `Heartbeat:IntervalMs` (default 2000 ms) — the world is fully assembled and the listener is open before any tick can land. See [Flow 16](flow-16-heartbeat-tick.md) for the tick-cycle trace.
12. **Shutdown path.** When the host shuts down, `PersistenceBootstrap.StopAsync` calls `PersistenceSystem.FlushAllAsync`, which iterates every entity in the internal persistence set and writes all `[Persistent]`-tagged component data to SQLite — a complete sweep. This is the last durability guarantee before the process exits.

**Cross-references.**
- [`docs/architecture/05-configuration.md`](../05-configuration.md) — startup-relevant config keys
- [`docs/reference/systems.md`](../../reference/systems.md) — `PersistenceSystem`, `WorldContentLoader`, `TemplateRegistry`
- [`docs/implementation-plans/persistence-substrate.md`](../06-persistence.md), [`docs/implementation-plans/world-content-loading-and-admin-substrate.md`](../../features/world/world.md) — slice specs
