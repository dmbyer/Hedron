# Canonical Flows

> Living catalog of end-to-end runtime flows in Hedron. The static architecture lives in [00-overview.md](../00-overview.md) through [05-configuration.md](../05-configuration.md); the inventory of components/systems/handlers lives under [`../reference/`](../../reference/). **This file traces what actually happens at runtime** — the dynamic call chains a developer or designer needs to understand "if I do X, what executes, and in what order?"
>
> **Update rule.** Every slice's PR must update this file to reflect the as-built code for any flow it introduces, modifies, or extends. CLAUDE.md ground rule 9 makes this a merge gate; the architecture-reviewer agent verifies the doc matches the diff.

---

## Index

| # | Flow | Trigger | Slice introduced |
|---|---|---|---|
| 1 | [Server startup](#flow-1--server-startup) | `dotnet run --project Server` | Phase 2 (extended in slice 2) |
| 2 | [Player connection](#flow-2--player-connection) | TCP client connects on the configured port | Phase 2 |
| 3 | [Player command lifecycle](#flow-3--player-command-lifecycle) | Player sends a line of input | Phase 2 (replaced by slice 3 command framework; output leg updated in slice 4; prefix resolution added in slice 3a) |
| 4 | [Persistence flush cycle](#flow-4--persistence-flush-cycle) | `PersistenceFlushTimer` ticks, or shutdown | Phase 3 slice 1 |
| 5 | [Content reload](#flow-5--content-reload-reload) | Privileged session sends `reload` | Phase 3 slice 2 (gate moved to dispatcher in slice 3) |
| 6 | [Output rendering](#flow-6--output-rendering) | A command/system writes a typed `IOutputMessage` | Phase 3 slice 4 |
| 7 | [Login / character flow](#flow-7--login--character-flow) | TCP client connects, new or returning player | Phase 3 slice 5 |
| 8 | [Admin room creation (`dig`)](#flow-8--admin-room-creation-dig) | Privileged session sends `dig <direction> [name]` | Phase 3 slice 5a |
| 9 | [Item pickup (`get`)](#flow-9--item-pickup-get) | Player sends `get <item>` | Phase 3 slice 6 |
| 10 | [Item drop (`drop`)](#flow-10--item-drop-drop) | Player sends `drop <item>` | Phase 3 slice 6 |
| 11 | [Inventory display (`inventory`)](#flow-11--inventory-display-inventory) | Player sends `inventory` / `inv` / `i` | Phase 3 slice 6 |
| 12 | [Admin item creation (`mkitem`)](#flow-12--admin-item-creation-mkitem) | Privileged session sends `mkitem [name]` | Phase 3 slice 6 |
| 13 | [`wear <item>`](#flow-13--wear-item) | Player sends `wear <item>` | Phase 3 slice 7 |
| 14 | [`remove <item>`](#flow-14--remove-item) | Player sends `remove <item>` | Phase 3 slice 7 |
| 15 | [Admin mob creation (`mkmob`)](#flow-15--admin-mob-creation-mkmob) | Privileged session sends `mkmob [name]` | Phase 3 slice 8 |
| 16 | [Heartbeat tick](#flow-16--heartbeat-tick) | `PeriodicTimer` fires in `HeartbeatBackgroundService` | Phase 3 slice 9-b |

Flows that don't yet exist (combat round, player death, mob wander tick, etc.) get added by the slice that introduces them.

---

## Flow 1 — Server startup

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
8. `PersistenceFlushTimer.StartAsync` arms a `PeriodicTimer` reading `Persistence:FlushIntervalSeconds` (default 60). See [Flow 4](#flow-4--persistence-flush-cycle) for the full flush-cycle trace.
9. `TelnetServer.StartAsync` opens the TCP listener on `Server:Port`. Connections accepted from this point forward see a fully assembled world.
10. `HeartbeatBackgroundService.StartAsync` starts the `PeriodicTimer` on a background thread; `StartAsync` itself returns immediately. The first tick fires after `Heartbeat:IntervalMs` (default 2000 ms) — the world is fully assembled and the listener is open before any tick can land. See [Flow 16](#flow-16--heartbeat-tick) for the tick-cycle trace.
11. **Shutdown path.** When the host shuts down, `PersistenceBootstrap.StopAsync` calls `PersistenceSystem.FlushAllPersistentAsync`, which iterates every entity carrying `PersistentEntity` and writes it to disk — a complete sweep regardless of which rooms are occupied. This replaced the old `FlushAsync` (dirty-set sweep) when the two-level persistence model was introduced.

**Cross-references.**
- [`docs/architecture/05-configuration.md`](../05-configuration.md) — startup-relevant config keys
- [`docs/reference/systems.md`](../../reference/systems.md) — `PersistenceSystem`, `WorldContentLoader`, `TemplateRegistry`
- [`docs/use-cases/persistence-substrate.md`](../../use-cases/persistence-substrate.md), [`docs/use-cases/world-content-loading-and-admin-substrate.md`](../../use-cases/world-content-loading-and-admin-substrate.md) — slice specs

---

## Flow 2 — Player connection

**Summary.** A new TCP connection produces a per-session task that runs the `LoginFlow` state machine (banner → register/authenticate → character select/create), binds the resulting character entity to the session, then enters the main I/O loop. Disconnect records logout, removes the transient `PlayerComponent`, and broadcasts departure. The character entity is **not** destroyed. See [Flow 7](#flow-7--login--character-flow) for the full login state machine detail.

**Trigger.** Inbound TCP connection on `Server:Port` (default 4000).

```mermaid
sequenceDiagram
    participant Client
    participant TS as TelnetServer
    participant Sess as TelnetSession
    participant LF as LoginFlow
    participant AccSys as IAccountSystem
    participant ES as EntityService
    participant SM as SessionManager
    participant Bus as IEventBus
    participant PSH as PlayerSessionHandler
    participant PSys as IPersistenceSystem

    Client->>TS: TCP connect
    TS->>Sess: spawn task (PlayerEntityId=0)
    Sess->>LF: RunAsync(ct)
    Note over LF,AccSys: login state machine (see Flow 7)
    LF-->>Sess: LoginResult(CharacterEntityId, AccountEntityId, CharacterName)
    Sess->>ES: AddComponent(PlayerComponent{DisplayName,Session})
    Sess->>SM: Register(session)
    Sess->>Bus: Publish(PlayerConnectedEvent)
    Bus->>PSH: HandleAsync → announce arrival + SendRoomDescriptionAsync
    loop main I/O loop (PlayerEntityId != 0)
        Client->>Sess: input line
        Sess->>Sess: DispatchAsync (Flow 3)
    end
    Client--xSess: disconnect
    Sess->>SM: Unregister
    Sess->>Bus: Publish(PlayerDisconnectedEvent)
    Bus->>PSH: HandleAsync → RecordLogout + SaveEntityAsync(characterEntityId) + RemoveComponent<PlayerComponent> + departure broadcast
```

**Steps.**

1. `TelnetServer` (a `BackgroundService`) accepts the TCP client and spawns a fire-and-forget per-session `TelnetSession` task. `PlayerEntityId` is 0 until login completes — the `CommandDispatcher` guard `if (session.PlayerEntityId == 0) return;` prevents commands from being dispatched during login.
2. `TelnetSession` delegates immediately to `LoginFlow.RunAsync`. The login flow drives the full interactive state machine (banner, registration or authentication, character selection or creation) and returns a `LoginResult` — or `null` if the client disconnects or exceeds the login attempt limit. See [Flow 7](#flow-7--login--character-flow) for detail.
3. On a valid `LoginResult`: `TelnetSession` sets `PlayerEntityId = result.CharacterEntityId`, attaches the transient `PlayerComponent { DisplayName, Session }`, calls `SessionManager.Register(session)`, and publishes `PlayerConnectedEvent(PlayerEntityId, CharacterName, AccountEntityId)`.
4. `PlayerSessionHandler` (priority `HandlerPriority.Domain`) handles `PlayerConnectedEvent`: broadcasts the arrival message to the room and calls `BroadcastSystem.SendRoomDescriptionAsync` for the connecting player.
5. The session enters its main I/O loop. Each input line is forwarded to `CommandDispatcher.DispatchAsync` (see Flow 3).
6. On disconnect, `SessionManager.Unregister` removes the session, then `PlayerDisconnectedEvent` is published. `PlayerSessionHandler` calls `IAccountSystem.RecordLogout` (updates `CharacterComponent.LastLoginUtc`), then immediately calls `IPersistenceSystem.SaveEntityAsync(characterEntityId)` so the logout timestamp is durable without waiting for the next flush cycle, removes `PlayerComponent` via `EntityService.RemoveComponent<PlayerComponent>`, and broadcasts the departure.

**Cross-references.**
- [`Server/Sessions/TelnetServer.cs`](../../../Server/Sessions/TelnetServer.cs), [`Server/Sessions/TelnetSession.cs`](../../../Server/Sessions/TelnetSession.cs), [`Server/Sessions/LoginFlow.cs`](../../../Server/Sessions/LoginFlow.cs)
- [`docs/reference/handlers.md`](../../reference/handlers.md) — `PlayerSessionHandler`
- [Flow 7](#flow-7--login--character-flow) — full login state machine
- [`docs/use-cases/account-character-creation.md`](../../use-cases/account-character-creation.md) — slice 5 spec

---

## Flow 3 — Player command lifecycle

**Summary.** Input bytes become a verb + raw tail; the dispatcher performs a two-phase verb lookup (exact first, prefix second), checks authorization via `IAuthorizationChecker`, parses arguments via `ICommandArgumentParser`, constructs a `CommandContext`, calls `ICommand.ExecuteAsync(context)`, and publishes `CommandExecutedEvent` for every outcome. The `Verb` field in `CommandExecutedEvent` always carries the **resolved canonical name** (e.g. `look`), never the raw typed prefix (`lo`). Output goes through the formatter-backed `IOutputWriter` (see Flow 6 for the rendering trace).

**Trigger.** A line of input arrives on a session's read stream.

```mermaid
sequenceDiagram
    participant Client
    participant Sess as TelnetSession
    participant CD as CommandDispatcher
    participant Auth as IAuthorizationChecker
    participant Parser as ICommandArgumentParser
    participant Cmd as ICommand impl
    participant Bus as IEventBus

    Client->>Sess: input line
    Sess->>CD: DispatchAsync(session, input)
    CD->>CD: trim + split → verb, rawTail
    alt verb exact-miss
        CD->>CD: prefix scan (Partial-mode commands only, sorted A–Z)
        alt zero prefix matches
            CD->>Sess: WriteAsync(PlainMessage "Unknown command…")
            CD->>Bus: Publish(CommandExecutedEvent ParseFailed)
        else ambiguous prefix (2+)
            CD->>Sess: WriteAsync(PlainMessage "Ambiguous command…all matches listed")
            CD->>Bus: Publish(CommandExecutedEvent ParseFailed)
        else unique prefix match → canonicalVerb = command.Name
        end
    else verb exact hit (name or alias) → canonicalVerb = command.Name
    end
    loop RequiredPrivileges
        CD->>Auth: IsSatisfied(req, session)
    end
    alt unauthorized
        CD->>Sess: WriteAsync(PlainMessage "Not authorized")
        CD->>Bus: Publish(CommandExecutedEvent Unauthorized, Verb=canonicalVerb)
    else authorized
        CD->>Parser: Parse(ArgumentSchema, rawTail, resolverContext)
        alt parse failed
            CD->>Sess: WriteAsync(PlainMessage reason + help hint)
            CD->>Bus: Publish(CommandExecutedEvent ParseFailed, Verb=canonicalVerb)
        else parsed
            CD->>Cmd: ExecuteAsync(CommandContext)
            Cmd->>Cmd: domain call / event publish
            Cmd->>Sess: WriteAsync(IOutputMessage) via IOutputWriter
            CD->>Bus: Publish(CommandExecutedEvent Success, Verb=canonicalVerb)
            Bus->>Bus: priority-ordered handlers (20 → 80 → 90 → 95)
        end
    end
```

**Steps.**

1. `TelnetSession` reads a line and calls `CommandDispatcher.DispatchAsync(session, input)`.
2. **Two-phase verb lookup.**
   - **Phase 1 (exact):** `_byVerb.TryGetValue(verb)` — checks primary names and all declared aliases. If found, `canonicalVerb = command.Name` and skip to step 3. Static aliases like `d` → `down` resolve here; prefix resolution is never reached.
   - **Phase 2 (prefix):** Collect all commands where `MatchingMode == Partial` and `command.Name.StartsWith(verb, OrdinalIgnoreCase)`. Sort alphabetically. Zero matches → write `PlainMessage("Unknown command: <verb>. Type 'help' for a list.")`, publish `CommandExecutedEvent(ParseFailed)`, return. Two or more matches → write `PlainMessage("Ambiguous command '<verb>'. Did you mean: <all names, comma-separated>?")`, publish `CommandExecutedEvent(ParseFailed)`, return. Exactly one match → `canonicalVerb = command.Name`.
3. **Privilege gate.** The dispatcher iterates `command.RequiredPrivileges` and calls `IAuthorizationChecker.IsSatisfied(req, session)` for each. Any unsatisfied requirement writes a rejection `PlainMessage` via `IOutputWriter` and publishes `CommandExecutedEvent(Unauthorized, Verb=canonicalVerb)`.
4. **Argument parse.** `ICommandArgumentParser.Parse(command.ArgumentSchema, rawTail, resolverContext)` does single-pass tokenization (whitespace + double-quoted groups), walks the declarative argument list, and coerces each token to its CLR type (`string`, `int`, `uint`, `Direction`). Enum-prefix matching works from day one (`n`/`no`/`nor` → `North`). String `Token` arguments that declare a non-null `IArgumentResolver` have prefix matching applied against the candidate list. The resolver returns `IReadOnlyList<ResolvedCandidate>?` where each `ResolvedCandidate(string MatchString, string CanonicalValue)` allows keyword aliases to map to a canonical item name; the parser deduplicates by `CanonicalValue` after prefix matching so multiple keyword aliases for the same item do not produce false ambiguity. Concrete resolvers (`ItemInRoomResolver`, `ItemInInventoryResolver`) ship in slice 6. On failure: the reason + `"Type 'help <canonicalVerb>' for usage."` is written; `CommandExecutedEvent(ParseFailed, Verb=canonicalVerb)` is published.
5. **Execute.** The dispatcher constructs `CommandContext(Session, InvokerEntityId, ParsedArguments, IOutputWriter, IServiceProvider)` and calls `command.ExecuteAsync(context)`. The body reads typed args via `context.Args.Get<T>(name)`, calls domain systems or publishes events via injected `IEventBus`, and writes all output via `context.Output.WriteAsync(IOutputMessage)`. No `session.SendLineAsync` in command bodies.
6. **Formatter-backed output.** `IOutputWriter.WriteAsync` resolves the session's formatter from `IOutputFormatterRegistry`, calls `formatter.Format(message, session)` (transport-correct ANSI or stripped plain text based on `session.SupportsColor`), and awaits `session.SendLineAsync(rendered)`. See [Flow 6](#flow-6--output-rendering) for the full rendering trace.
7. **Exception trap.** Any uncaught exception is caught, logged at `Error` with a full stack trace, a `PlainMessage("Something went wrong. The error has been logged.")` is written, and `CommandExecutedEvent(Threw)` is published. No stack trace reaches the session.
8. **`CommandExecutedEvent`.** Published on every dispatch path — success, parse-fail, unauthorized, threw. The `Verb` field carries the **resolved canonical command name** (e.g. `look` when the player typed `lo`), not the raw typed prefix. This makes log lines stable regardless of what the player typed. `CommandLoggingHandler` (priority 80) writes one structured-log line per command via `ILogger`. `AdminAuditHandler` keeps subscribing to the four richer slice-2 admin events and does **not** subscribe to `CommandExecutedEvent`.

**Cross-references.**
- [`Core/Commands/CommandDispatcher.cs`](../../../Core/Commands/CommandDispatcher.cs), [`Core/Commands/ICommand.cs`](../../../Core/Commands/ICommand.cs)
- [`Core/Commands/Authorization/IAuthorizationChecker.cs`](../../../Core/Commands/Authorization/IAuthorizationChecker.cs), [`Core/Commands/CommandArgumentParser.cs`](../../../Core/Commands/CommandArgumentParser.cs)
- [`Core/Output/OutputWriter.cs`](../../../Core/Output/OutputWriter.cs), [`Core/Handlers/CommandLoggingHandler.cs`](../../../Core/Handlers/CommandLoggingHandler.cs)
- [`subsystems/commands.md`](../subsystems/commands.md) — command framework design
- [`subsystems/output.md`](../subsystems/output.md) — output framework design
- [`docs/use-cases/command-framework.md`](../../use-cases/command-framework.md) — slice 3 spec; [`docs/use-cases/output-framework.md`](../../use-cases/output-framework.md) — slice 4 spec
- [`docs/reference/handlers.md`](../../reference/handlers.md) — handler priority tiers

---

## Flow 4 — Persistence flush cycle

**Summary.** The flush timer resolves the active player footprint (rooms occupied by at least one connected player) and writes all `PersistentEntity`-carrying entities in those rooms. On shutdown `PersistenceBootstrap.StopAsync` runs a full sweep of every `PersistentEntity` entity. Authored content and lifecycle transitions use save-on-change (`SaveEntityAsync`) called directly by the command or handler that made the mutation; they do not depend on this cycle.

**Trigger.** `PersistenceFlushTimer` periodic tick (`Persistence:FlushIntervalSeconds`, default 60) or `PersistenceBootstrap.StopAsync` (shutdown).

```mermaid
sequenceDiagram
    participant Timer as PersistenceFlushTimer
    participant SM as ISessionManager
    participant ES as EntityService
    participant PSys as PersistenceSystem
    participant TR as IComponentTypeRegistry
    participant CS as IComponentSerializer
    participant Disk

    Timer->>SM: GetAll() → sessions
    loop per session
        Timer->>ES: TryGet<LocationComponent>(playerEntityId) → roomId
    end
    Timer->>PSys: FlushActivePlayerFootprintAsync(occupiedRoomIds)
    PSys->>ES: GetAllComponents<LocationComponent>() filtered by occupiedRoomIds
    loop per entity in footprint
        PSys->>ES: HasComponent<PersistentEntity>(entityId)
        alt has PersistentEntity
            PSys->>ES: GetAllComponentsForEntity
            PSys->>TR: filter to [Persistent] types
            loop per persistent component
                PSys->>CS: Serialize → JSON
            end
            PSys->>Disk: write {id}.tmp
            PSys->>Disk: File.Move(.tmp, ..., overwrite=true)
        end
    end
```

**Steps.**

1. `PersistenceFlushTimer.ExecuteAsync` ticks. It calls `ISessionManager.GetAll()` to collect all connected sessions; for each, reads `LocationComponent.RoomEntityId` to build the set of occupied room ids. If no sessions are connected the flush is skipped.
2. Calls `PersistenceSystem.FlushActivePlayerFootprintAsync(occupiedRoomIds, ct)`.
3. `FlushActivePlayerFootprintAsync` queries `EntityService.GetAllComponents<LocationComponent>()` and filters to entities whose `RoomEntityId` is in the occupied set. This naturally includes both player entities (whose location is one of the occupied rooms) and any other `LocationComponent`-bearing entities in those rooms.
4. For each entity in the footprint, the system checks `HasComponent<PersistentEntity>(entityId)`. Entities without the marker are silently skipped — the two-level model guard.
5. For entities that pass the guard: `EntityService.GetAllComponentsForEntity` returns all attached components; the set is filtered through `IComponentTypeRegistry.IsPersistent`; each surviving component is serialized via `IComponentSerializer` (System.Text.Json, camelCase). The envelope `{ entityId, components: [{ typeName, data }, …] }` is written atomically via `.tmp`→rename.
6. `PersistenceSystem` publishes no events.
7. **Shutdown path.** `PersistenceBootstrap.StopAsync` calls `PersistenceSystem.FlushAllPersistentAsync(ct)`, which iterates `GetAllComponents<PersistentEntity>()` and writes every entity — not just those in occupied rooms — guaranteeing a complete snapshot regardless of player positions.

**Two-level model.** An entity is written only if it carries `PersistentEntity` (level 1). Among its components, only those tagged `[Persistent]` are included in the snapshot (level 2). `PlayerComponent` (transient session ref) and `TransientEffectsComponent` (session-only) are untagged and are never written.

**Cross-references.**
- [`Core/Systems/PersistenceSystem.cs`](../../../Core/Systems/PersistenceSystem.cs), [`Server/PersistenceFlushTimer.cs`](../../../Server/PersistenceFlushTimer.cs), [`Server/PersistenceBootstrap.cs`](../../../Server/PersistenceBootstrap.cs)
- [`docs/use-cases/persistence-substrate.md`](../../use-cases/persistence-substrate.md), [`docs/use-cases/persistence-two-level-model.md`](../../use-cases/persistence-two-level-model.md)

---

## Flow 5 — Content reload (`reload`)

**Summary.** A privileged session re-scans the content directory and refreshes the template registry. Templates with no live counterpart are seeded; **existing live entities are not mutated**. The pass is additive only.

**Trigger.** Privileged session sends `reload`.

```mermaid
sequenceDiagram
    participant Session
    participant CD as CommandDispatcher
    participant Auth as IAuthorizationChecker
    participant RC as ReloadCommand
    participant WCL as WorldContentLoader
    participant Reg as ITemplateRegistry
    participant Bus as IEventBus
    participant Audit as AdminAuditHandler

    Session->>CD: "reload"
    CD->>Auth: IsSatisfied(AdminRequirement, session)
    alt unauthorized
        CD->>Session: rejection (via IOutputWriter)
    else authorized
        CD->>RC: ExecuteAsync(CommandContext)
        RC->>WCL: ReloadAsync
        WCL->>Reg: snapshot previous ids
        WCL->>Reg: Clear
        WCL->>WCL: re-scan + re-deserialize → register
        WCL->>WCL: BuildLiveBlueprintMap
        WCL->>WCL: SpawnMissingEntities (skip-on-conflict)
        WCL->>WCL: LinkRoomExits (new entities only)
        WCL->>WCL: PlaceItemsInRooms (newlySpawned only)
        WCL->>WCL: PlaceMobsInRooms (newlySpawned only)
        WCL-->>RC: ContentReloadResult{ loaded, unchanged, removed }
        RC->>Session: confirmation (via IOutputWriter)
        RC->>Bus: Publish(ContentReloadedEvent)
        Bus->>Audit: HandleAsync (structured log)
    end
```

**Steps.**

1. `CommandDispatcher` routes `reload` to `ReloadCommand`.
2. **Authorization gate.** The dispatcher calls `IAuthorizationChecker.IsSatisfied(AdminRequirement, session)` **before** invoking `ReloadCommand`. Non-privileged sessions receive a rejection `PlainMessage` via `IOutputWriter` and `CommandExecutedEvent(Unauthorized)`; `ReloadCommand.ExecuteAsync` never runs. This is the slice-3 structural replacement for slice-2's per-command `IsPrivileged` convention.
3. The command calls `IWorldContentLoader.ReloadAsync(ct)`.
4. The loader snapshots the previous template ids, clears the registry, and re-scans `World:ContentDirectory`. Each YAML file is re-deserialized via the cross-cutting `IContentSerializer` → kind-specific `ITemplateDeserializer` and re-registered.
5. Loaded / unchanged / removed counts are computed by set difference against the previous snapshot.
6. `BuildLiveBlueprintMap` enumerates every entity that has a `BlueprintComponent`. For each registered template that has no entry in the map, `SpawnMissingEntities` calls `TemplateRegistry.Spawn(blueprintId)` (which allocates an entity, attaches `BlueprintComponent`, and runs `IEntityTemplate.Apply`).
7. `LinkRoomExits` populates `RoomComponent.Exits` for the newly spawned entities only — existing live rooms are not touched.
8. `PlaceItemsInRooms` attaches `LocationComponent { RoomEntityId }` to newly-spawned item entities only. If a YAML `spawnRoomBlueprintId` changed for an existing live entity, a warning is logged — live entities are never mutated by reload.
9. `PlaceMobsInRooms` applies the same pass for newly-spawned mob entities. Same constraint and warning behavior as items.
10. `ReloadAsync` returns `ContentReloadResult { loaded, unchanged, removed }`.
11. The command writes a confirmation `PlainMessage` via `CommandContext.Output` (`IOutputWriter`) and publishes `ContentReloadedEvent` (thin payload — the three counts).
12. `AdminAuditHandler` (priority `HandlerPriority.Notification` = 80) writes one structured-log entry with stable event name `AdminCommandExecuted`.

**Constraint.** Live entities are never mutated by reload. To pick up edits to a live room's description or components, restart the host; or use `dig` for exit changes that should apply immediately.

**Cross-references.**
- [`Core/Modules/Admin/Commands/ReloadCommand.cs`](../../../Core/Modules/Admin/Commands/ReloadCommand.cs), [`Core/Modules/World/Systems/WorldContentLoader.cs`](../../../Core/Modules/World/Systems/WorldContentLoader.cs)
- [`docs/use-cases/world-content-loading-and-admin-substrate.md`](../../use-cases/world-content-loading-and-admin-substrate.md)

---

## Flow 6 — Output rendering

**Summary.** Any command body or handler that calls `IOutputWriter.WriteAsync(IOutputMessage)` or `IBroadcastSystem.SendToRoomAsync`/`SendToAllAsync` triggers this chain. A typed message is resolved to the session's transport formatter, rendered into an ANSI (or plain-text) string, and transmitted. Every future gameplay slice's output plugs into this chain without touching transport code.

**Trigger.** Any call to `IOutputWriter.WriteAsync`, `IBroadcastSystem.SendToRoomAsync`, `IBroadcastSystem.SendToAllAsync`, or `IBroadcastSystem.SendRoomDescriptionAsync`.

```mermaid
sequenceDiagram
    participant Caller as Command / Handler
    participant OW as IOutputWriter
    participant Reg as IOutputFormatterRegistry
    participant Fmt as IOutputFormatter (TelnetOutputFormatter)
    participant Sess as ISession

    Caller->>OW: WriteAsync(IOutputMessage)
    OW->>Reg: Resolve(session)
    Reg-->>OW: IOutputFormatter
    OW->>Fmt: Format(message, session)
    Fmt->>Fmt: pattern-match shape
    Fmt->>Fmt: apply palette + inline markers (or strip if !SupportsColor)
    Fmt-->>OW: rendered string
    OW->>Sess: SendLineAsync(rendered)
```

**Steps.**

1. A command calls `context.Output.WriteAsync(message)` or a handler calls `_broadcast.SendToRoomAsync(roomId, message, filter?)`. For broadcast, `BroadcastSystem` enumerates eligible recipients and calls `_writerFactory.Create(session).WriteAsync(message)` for each.
2. `OutputWriter.WriteAsync` calls `IOutputFormatterRegistry.Resolve(session)` to obtain the formatter whose `TransportKey` matches `session.TransportKey` (e.g. `"telnet"`). Falls back to the first registered formatter if no exact match (safe while only telnet exists).
3. `IOutputFormatter.Format(message, session)` pattern-matches the message shape:
   - `PlainMessage` — wraps text in a severity-appropriate color marker (`<error>`, `<system>`, or plain).
   - `RoomDescriptionMessage` — room name in `<room-name>`, exit keys in `<direction>`, description and occupants plain; if `Items` is non-empty, appends an `"Items: X, Y, Z"` line; if `Mobs` is non-empty, appends a `"<Name> is here."` line per mob. `BroadcastSystem.SendRoomDescriptionAsync` populates `Items` by iterating all `ItemDataComponent` entities whose `LocationComponent.RoomEntityId` matches the room, and populates `Mobs` by iterating all `MobDataComponent` entities in the same room.
   - `MovementMessage(Blocked)` — "You cannot go that way." in `<system>`.
   - `InventoryListMessage` — `"You are carrying:"` header in `<system>` followed by a plain-text item list (one per line, two-space indent). Only sent when inventory is non-empty; empty case is a `PlainMessage("You are carrying nothing.")` from the command body.
   - `EquipmentDisplayMessage` — `"You are wearing:"` header in `<system>` followed by slot label (left-padded to 14 chars) + item name rows, ordered by `WornSlot` enum ordinal. Only sent when at least one slot is occupied; empty case is a `PlainMessage("You are not wearing anything.")` from the command body. (slice 7)
   - `HelpIndexMessage` — section headers in `<system>`, verb names in `<room-name>` (padded before colorizing).
   - `HelpEntryMessage` — verb/alias header in `<room-name>`.
4. **Color application.** If `session.SupportsColor` is `true`, inline markers (`<role>text</role>`) are replaced with ANSI escape codes + reset. If `false`, markers are stripped and only the inner text remains. See [`subsystems/output.md`](../subsystems/output.md) for the palette table.
5. The rendered string is passed to `session.SendLineAsync(rendered)`. The session acquires its write lock and writes the UTF-8 bytes to the TCP stream.

**Broadcast fan-out.** For `SendToRoomAsync`, step 1 iterates `LocationComponent` entities in the room, applies the optional `Func<uint,bool>? audienceFilter` predicate (e.g. `id => id != movingPlayer`), and runs steps 2–5 for each surviving recipient. Each recipient gets their own formatter resolution so a future mixed-transport world renders correctly per client.

**Cross-references.**
- [`Core/Output/OutputWriter.cs`](../../../Core/Output/OutputWriter.cs), [`Core/Output/TelnetOutputFormatter.cs`](../../../Core/Output/TelnetOutputFormatter.cs), [`Core/Output/OutputFormatterRegistry.cs`](../../../Core/Output/OutputFormatterRegistry.cs)
- [`Core/Systems/BroadcastSystem.cs`](../../../Core/Systems/BroadcastSystem.cs)
- [`subsystems/output.md`](../subsystems/output.md) — full output framework design
- [`docs/use-cases/output-framework.md`](../../use-cases/output-framework.md) — slice 4 spec

---

## Flow 7 — Login / character flow

**Summary.** The `LoginFlow` Initiator (session-layer, `Server/Sessions/LoginFlow.cs`) drives the multi-step interactive wizard that runs between TCP accept and the main I/O loop. It handles account registration or authentication, then character selection or creation. Domain work (entity allocation, hashing, persistence marking) is delegated to `IAccountSystem`. Events are published by the flow itself (Initiator tier) after each successful state transition.

**Trigger.** `TelnetSession.RunAsync` after TCP accept (see [Flow 2](#flow-2--player-connection) step 2).

```mermaid
sequenceDiagram
    participant Client
    participant LF as LoginFlow
    participant OW as IOutputWriter
    participant AccSys as IAccountSystem
    participant PSys as IPersistenceSystem
    participant Bus as IEventBus

    LF->>OW: banner + "new account?" prompt
    Client->>LF: yes/no
    alt new account (registration path)
        LF->>OW: "Username:"
        Client->>LF: username (validated: 3–20, alphanumeric+_)
        LF->>AccSys: UsernameExists → reject if taken
        LF->>OW: "Choose a password:" + confirm
        Client->>LF: password (≥6 chars, must match confirm)
        LF->>AccSys: CreateAccountAsync → AccountEntityId
        Note over LF,AccSys: AccountCreatedEvent deferred until after saves (see character creation)
        LF->>LF: → character creation path (newAccountUsername set)
    else returning account (auth path, up to 3 attempts)
        LF->>OW: "Username:" + "Password:"
        Client->>LF: credentials
        LF->>AccSys: AuthenticateAsync → AuthResult
        alt success
            LF->>LF: → character selection path
        else fail
            LF->>OW: "Invalid credentials. N attempt(s) remaining."
        end
    end
    alt character selection
        LF->>AccSys: GetCharacterList(accountId)
        alt has characters
            LF->>OW: numbered roster + "new" option
            Client->>LF: number or "new"
            alt pick existing
                LF-->>LF: return LoginResult
            else new
                LF->>LF: → character creation path
            end
        else no characters
            LF->>LF: → character creation path
        end
    end
    alt character creation
        LF->>OW: "Enter a name for your character:"
        Client->>LF: name (2–16 letters, unique)
        LF->>AccSys: CharacterNameExists → reject if taken
        LF->>AccSys: CreateCharacterAsync → CharacterEntityId
        Note over AccSys: creates entity, attaches CharacterComponent + LocationComponent + AttributesComponent + PoolsComponent + PersistentEntity
        LF->>PSys: SaveEntityAsync(CharacterEntityId) [character saved first]
        LF->>PSys: SaveEntityAsync(AccountEntityId) [account saved second]
        alt newAccountUsername set (registration path)
            LF->>Bus: Publish(AccountCreatedEvent)
        end
        LF->>Bus: Publish(CharacterCreatedEvent)
        LF-->>LF: return LoginResult
    end
```

**Steps.**

1. `LoginFlow` is constructed by `TelnetSession` with the raw `StreamReader` (so it can read lines before the session is registered) and `IOutputWriterFactory` (so prompts are rendered through the formatter pipeline).
2. **Banner.** The flow writes `"Welcome to Hedron.\nDo you have an existing account? (yes/no)"` via `IOutputWriter`. Any yes/y/login answer → auth path; anything else → registration.
3. **Registration path.** Prompts for username; validates 3–20 chars, alphanumeric + underscore; calls `UsernameExists` and rejects if taken. Prompts for password (≥6 chars) with confirmation. Calls `IAccountSystem.CreateAccountAsync` → allocates an entity, attaches `AccountComponent` and `PersistentEntity`, returns `AccountEntityId`. `AccountCreatedEvent` is **not** published yet — it is deferred until after both entities are saved (see step 6). Falls through to character creation.
4. **Auth path.** Up to `MaxLoginAttempts` (3) rounds of username + password. Calls `IAccountSystem.AuthenticateAsync` (PBKDF2-SHA256 verify via `IPasswordHasher`). On success → character selection. On exhaustion → writes rejection and returns `null` (session task exits).
5. **Character selection.** Calls `GetCharacterList(accountId)`. If the list is empty, falls through to character creation. Otherwise renders a numbered roster + "new" option. Validates input; enforces `Account:MaxCharactersPerAccount` (default 5). Picking a number returns `LoginResult(CharacterEntityId, AccountEntityId, CharacterName)` immediately.
6. **Character creation.** Prompts for a name; validates 2–16 letters only, globally unique via `CharacterNameExists`. Calls `IAccountSystem.CreateCharacterAsync` → allocates an entity, attaches `CharacterComponent { AccountEntityId, CharacterName, CreatedAtUtc }`, `LocationComponent { RoomEntityId = WorldConfiguration.StartingRoomEntityId }`, `AttributesComponent { Level=1, Strength=10, Dexterity=10, Constitution=10 }`, `PoolsComponent { MaxHp=100, CurrentHp=100 }` (extended in slice 8a), and `PersistentEntity`; appends id to `AccountComponent.CharacterEntityIds`. Returns `CharacterEntityId`. `LoginFlow` then calls `IPersistenceSystem.SaveEntityAsync(characterEntityId)` first (character written before account — if the server crashes between the two writes, an orphaned character file is recoverable but a dangling account pointer to a missing character is not), then `SaveEntityAsync(accountEntityId)`. After both saves complete, if this is a new account `AccountCreatedEvent` is published, then `CharacterCreatedEvent`. Returns `LoginResult`.
7. A `null` return from `LoginFlow.RunAsync` (disconnect, exceeded attempts) causes `TelnetSession` to exit without entering the I/O loop. `HandleDisconnectAsync` is still called but skips publishing because `PlayerEntityId == 0`.

**Cross-references.**
- [`Server/Sessions/LoginFlow.cs`](../../../Server/Sessions/LoginFlow.cs), [`Server/Sessions/TelnetSession.cs`](../../../Server/Sessions/TelnetSession.cs)
- [`Core/Modules/Account/Systems/AccountSystem.cs`](../../../Core/Modules/Account/Systems/AccountSystem.cs)
- [`Core/Modules/Account/Systems/IAccountSystem.cs`](../../../Core/Modules/Account/Systems/IAccountSystem.cs)
- [`docs/reference/systems.md`](../../reference/systems.md) — `AccountSystem`, `PasswordHasher`
- [`docs/use-cases/account-character-creation.md`](../../use-cases/account-character-creation.md) — slice 5 spec

---

## Flow 8 — Admin room creation (`dig`)

**Summary.** A privileged session sends `dig <direction> [name]`. `DigCommand` checks for an existing exit, delegates entity creation and exit wiring to `IRoomBuilderSystem`, publishes `RoomCreatedByAdminEvent` (caught by `AdminAuditHandler`), calls `IPersistenceSystem.SaveEntityAsync` directly on both rooms (save-on-change), then publishes `PlayerMovedEvent` to auto-move the admin into the new room via the existing `PlayerMovedHandler`.

**Trigger.** Privileged session sends `dig <direction> [name]`.

```mermaid
sequenceDiagram
    participant Sess as TelnetSession
    participant CD as CommandDispatcher
    participant Auth as IAuthorizationChecker
    participant Cmd as DigCommand
    participant RBS as IRoomBuilderSystem
    participant PSys as IPersistenceSystem
    participant Bus as IEventBus
    participant Audit as AdminAuditHandler
    participant PMH as PlayerMovedHandler

    Sess->>CD: "dig north Garden"
    CD->>Auth: IsSatisfied(AdminRequirement, session)
    alt unauthorized
        CD->>Sess: rejection PlainMessage
    else authorized
        CD->>Cmd: ExecuteAsync(CommandContext)
        Cmd->>Cmd: check Exits[North] on current room
        alt exit exists
            Cmd->>Sess: error PlainMessage
        else no exit
            Cmd->>RBS: CreateRoom("Garden")
            RBS-->>Cmd: RoomCreationResult(newRoomId, "room.adhoc.a1b2c3")
            Cmd->>RBS: LinkExits(sourceId, North, newRoomId, true)
            Cmd->>Bus: Publish(RoomCreatedByAdminEvent)
            Bus->>Audit: HandleAsync (priority 80) → structured log
            Cmd->>PSys: SaveEntityAsync(newRoomId)
            Cmd->>PSys: SaveEntityAsync(sourceRoomId)
            Cmd->>Bus: Publish(PlayerMovedEvent)
            Bus->>PMH: HandleAsync → departure broadcast + arrival broadcast + look
            Cmd->>Sess: confirmation PlainMessage
        end
    end
```

**Steps.**

1. `CommandDispatcher` routes `dig` to `DigCommand` after the privilege gate (`AdminRequirement` via `IAuthorizationChecker`).
2. `DigCommand.ExecuteAsync` reads `LocationComponent.RoomEntityId` and checks `RoomComponent.Exits` for the requested direction. If an exit already exists, writes a `PlainMessage` error and returns.
3. Calls `IRoomBuilderSystem.CreateRoom(name)` — allocates an entity, attaches `RoomComponent` + `BlueprintComponent` + `PersistentEntity`, registers a minimal `RoomTemplate`, returns `RoomCreationResult(newRoomId, blueprintId)`.
4. Calls `IRoomBuilderSystem.LinkExits(sourceId, direction, newRoomId, bidirectional: true)` — sets `Exits` on both room entities and mirrors to both in-memory `RoomTemplate` exit maps.
5. Publishes `RoomCreatedByAdminEvent`. `AdminAuditHandler` (priority 80) logs one structured entry. `DigCommand` then calls `IPersistenceSystem.SaveEntityAsync(newRoomId)` and `SaveEntityAsync(sourceRoomId)` directly — save-on-change means both rooms are durable before the admin sees confirmation. No `PersistenceHandler` subscription.
6. Publishes `PlayerMovedEvent(adminId, sourceId, newRoomId, direction)`. `PlayerMovedHandler` fires: departure broadcast to the source room (excluding the admin), arrival broadcast to the new room, `look` sent to the admin.
7. Writes a confirmation `PlainMessage` (e.g. `"Room 'Garden' (room.adhoc.a1b2c3) created to the north."`).

**Cross-references.**
- [`Core/Modules/Admin/Commands/DigCommand.cs`](../../../Core/Modules/Admin/Commands/DigCommand.cs), [`Core/Modules/Admin/Systems/RoomBuilderSystem.cs`](../../../Core/Modules/Admin/Systems/RoomBuilderSystem.cs)
- [`Core/Modules/Admin/Events/RoomCreatedByAdminEvent.cs`](../../../Core/Modules/Admin/Events/RoomCreatedByAdminEvent.cs)
- [`Core/Modules/Admin/Handlers/AdminAuditHandler.cs`](../../../Core/Modules/Admin/Handlers/AdminAuditHandler.cs)
- [`docs/use-cases/bare-bones-content-spawning.md`](../../use-cases/bare-bones-content-spawning.md)

---

## Flow 9 — Item pickup (`get`)

**Summary.** Player sends `get <item>`. `GetCommand` uses `ItemInRoomResolver` to prefix-match the token against items in the player's room, calls `IItemSystem.MoveToInventory` to transfer the item from ground to inventory, publishes `ItemPickedUpEvent`, and saves both item and player. `ItemInteractionHandler` broadcasts the pickup messages.

**Trigger.** Player sends `get <item>`.

```mermaid
sequenceDiagram
    participant Client
    participant CD as CommandDispatcher
    participant Parser as ICommandArgumentParser
    participant Resolver as ItemInRoomResolver
    participant Cmd as GetCommand
    participant IS as IItemSystem
    participant PSys as IPersistenceSystem
    participant Bus as IEventBus
    participant IIH as ItemInteractionHandler
    participant Broadcast as IBroadcastSystem

    Client->>CD: "get sword"
    CD->>Parser: Parse(schema, "sword", resolverContext)
    Parser->>Resolver: GetCandidates(resolverContext)
    Resolver->>IS: GetItemsInRoom(playerRoomId)
    Resolver-->>Parser: [ResolvedCandidate("a short sword","a short sword"), ResolvedCandidate("sword","a short sword")]
    Parser-->>CD: ParsedArguments{item="a short sword"}
    CD->>Cmd: ExecuteAsync(CommandContext)
    Cmd->>IS: TryFindItemInRoom(roomId, "a short sword", out itemEntityId)
    IS-->>Cmd: true, itemEntityId
    Cmd->>IS: MoveToInventory(itemEntityId, playerEntityId)
    Cmd->>Bus: Publish(ItemPickedUpEvent)
    Bus->>IIH: HandleAsync (priority 80)
    IIH->>Broadcast: SendToRoomAsync(roomId, "Bob picks up a short sword.", id≠player)
    IIH->>Broadcast: SendToRoomAsync(roomId, "You pick up a short sword.", id==player)
    Cmd->>PSys: SaveEntityAsync(itemEntityId)
    Cmd->>PSys: SaveEntityAsync(playerEntityId)
```

**Steps.**

1. `CommandDispatcher` routes `get` to `GetCommand`. No privilege requirement.
2. **Argument resolution.** `ICommandArgumentParser` calls `ItemInRoomResolver.GetCandidates(resolverContext)`, which reads the invoker's `LocationComponent.RoomEntityId`, calls `IItemSystem.GetItemsInRoom`, and emits `ResolvedCandidate(MatchString, CanonicalValue)` pairs — one for each item name and each keyword. The parser prefix-matches the token against all `MatchString` values, deduplicates by `CanonicalValue`, and substitutes the canonical item name into `ParsedArguments.item` (unique match) or fails with an ambiguity error (two+ distinct canonical values).
3. **Entity resolve.** `IItemSystem.TryFindItemInRoom(roomId, canonicalName, out itemEntityId)` performs a final entity lookup. If not found (race condition: item taken between resolve and pickup), writes "You don't see that here." and returns.
4. **Pickup mutation.** `IItemSystem.MoveToInventory(itemEntityId, playerEntityId)` — removes `LocationComponent` from the item (no-op if already absent), appends item id to `InventoryComponent.ItemEntityIds`.
5. **Event.** Publishes `ItemPickedUpEvent(PlayerEntityId, ItemEntityId, RoomEntityId)`.
6. **Handler.** `ItemInteractionHandler` (priority 80) broadcasts `"<name> picks up <item>."` to the room excluding the picker, then `"You pick up <item>."` to the picker via `SendToRoomAsync` with opposite filters.
7. **Save.** `SaveEntityAsync(itemEntityId)` then `SaveEntityAsync(playerEntityId)` — both are durable immediately (save-on-change pattern).

**Cross-references.**
- [`Core/Modules/Items/Commands/GetCommand.cs`](../../../Core/Modules/Items/Commands/GetCommand.cs), [`Core/Modules/Items/Systems/ItemSystem.cs`](../../../Core/Modules/Items/Systems/ItemSystem.cs)
- [`Core/Modules/Items/Resolvers/ItemInRoomResolver.cs`](../../../Core/Modules/Items/Resolvers/ItemInRoomResolver.cs)
- [`Core/Modules/Items/Handlers/ItemInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/ItemInteractionHandler.cs)
- [`docs/use-cases/items-and-inventory.md`](../../use-cases/items-and-inventory.md) — slice 6 spec, flow B-1

---

## Flow 10 — Item drop (`drop`)

**Summary.** Player sends `drop <item>`. `DropCommand` uses `ItemInInventoryResolver` to prefix-match against carried items, calls `IItemSystem.DropToRoom` to move the item from inventory to the ground, publishes `ItemDroppedEvent`, and saves only the player entity (item intentionally not saved — dropped items vanish on restart by design). `ItemInteractionHandler` broadcasts the drop messages.

**Trigger.** Player sends `drop <item>`.

```mermaid
sequenceDiagram
    participant Client
    participant CD as CommandDispatcher
    participant Parser as ICommandArgumentParser
    participant Resolver as ItemInInventoryResolver
    participant Cmd as DropCommand
    participant IS as IItemSystem
    participant PSys as IPersistenceSystem
    participant Bus as IEventBus
    participant IIH as ItemInteractionHandler
    participant Broadcast as IBroadcastSystem

    Client->>CD: "drop sword"
    CD->>Parser: Parse(schema, "sword", resolverContext)
    Parser->>Resolver: GetCandidates(resolverContext)
    Resolver->>IS: GetItemsInInventory(playerEntityId)
    Resolver-->>Parser: [ResolvedCandidate("a short sword","a short sword"), ResolvedCandidate("sword","a short sword")]
    Parser-->>CD: ParsedArguments{item="a short sword"}
    CD->>Cmd: ExecuteAsync(CommandContext)
    Cmd->>IS: TryFindItemInInventory(playerEntityId, "a short sword", out itemEntityId)
    IS-->>Cmd: true, itemEntityId
    Cmd->>IS: DropToRoom(itemEntityId, playerEntityId, roomEntityId)
    Cmd->>Bus: Publish(ItemDroppedEvent)
    Bus->>IIH: HandleAsync (priority 80)
    IIH->>Broadcast: SendToRoomAsync(roomId, "Bob drops a short sword.", id≠player)
    IIH->>Broadcast: SendToRoomAsync(roomId, "You drop a short sword.", id==player)
    Cmd->>PSys: SaveEntityAsync(playerEntityId)
    Note over Cmd,PSys: item entity NOT saved — dropped items vanish on restart by design
```

**Steps.**

1. `CommandDispatcher` routes `drop` to `DropCommand`. No privilege requirement.
2. **Argument resolution.** `ItemInInventoryResolver.GetCandidates` reads the invoker's `InventoryComponent.ItemEntityIds` and builds `ResolvedCandidate` pairs for each carried item's name and keywords. Deduplication and substitution are the same as in Flow 9.
3. **Entity resolve.** `IItemSystem.TryFindItemInInventory(playerEntityId, canonicalName, out itemEntityId)`. Not found → "You aren't carrying that."
4. **Drop mutation.** `IItemSystem.DropToRoom(itemEntityId, playerEntityId, roomEntityId)` — removes item id from `InventoryComponent.ItemEntityIds`, attaches `LocationComponent { RoomEntityId }` to the item.
5. **Event.** Publishes `ItemDroppedEvent(PlayerEntityId, ItemEntityId, RoomEntityId)`.
6. **Handler.** `ItemInteractionHandler` broadcasts drop messages (same filter pattern as pickup, reversed flavour text).
7. **Save.** Only `SaveEntityAsync(playerEntityId)`. The item entity is intentionally not saved — its last-persisted state has no `LocationComponent` (saved during pickup), so it reverts to that state on restart. Template items are re-placed in their `spawnRoomId` by `PlaceItemsInRooms` on next startup; `mkitem` items simply vanish. See the persistence design note in [`docs/use-cases/items-and-inventory.md`](../../use-cases/items-and-inventory.md).

**Cross-references.**
- [`Core/Modules/Items/Commands/DropCommand.cs`](../../../Core/Modules/Items/Commands/DropCommand.cs), [`Core/Modules/Items/Systems/ItemSystem.cs`](../../../Core/Modules/Items/Systems/ItemSystem.cs)
- [`Core/Modules/Items/Resolvers/ItemInInventoryResolver.cs`](../../../Core/Modules/Items/Resolvers/ItemInInventoryResolver.cs)
- [`Core/Modules/Items/Handlers/ItemInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/ItemInteractionHandler.cs)
- [`docs/use-cases/items-and-inventory.md`](../../use-cases/items-and-inventory.md) — slice 6 spec, flow B-2

---

## Flow 11 — Inventory display (`inventory`)

**Summary.** Player sends `inventory` (or `inv` / `i`). `InventoryCommand` reads `InventoryComponent.ItemEntityIds`, resolves each to a display name via `ItemDataComponent`, and writes either a `PlainMessage("You are carrying nothing.")` or an `InventoryListMessage`. No events fired; no persistence.

**Trigger.** Player sends `inventory`, `inv`, or `i`.

```mermaid
sequenceDiagram
    participant Client
    participant CD as CommandDispatcher
    participant Cmd as InventoryCommand
    participant IS as IItemSystem
    participant ES as EntityService
    participant OW as IOutputWriter

    Client->>CD: "inv"
    CD->>Cmd: ExecuteAsync(CommandContext)
    Cmd->>IS: GetItemsInInventory(playerEntityId)
    alt empty
        Cmd->>OW: PlainMessage("You are carrying nothing.")
    else non-empty
        loop per itemEntityId
            Cmd->>ES: TryGet<ItemDataComponent>(itemEntityId) → name
        end
        Cmd->>OW: InventoryListMessage([names])
    end
```

**Steps.**

1. `CommandDispatcher` routes `inventory`/`inv`/`i` to `InventoryCommand`. No privilege requirement.
2. `IItemSystem.GetItemsInInventory(playerEntityId)` returns entity ids from `InventoryComponent.ItemEntityIds` (empty list if the component is absent).
3. If the list is empty, writes `PlainMessage("You are carrying nothing.", System)` and returns.
4. For each item id, `EntityService.TryGet<ItemDataComponent>` resolves the display name. Items whose component is missing are silently skipped.
5. Writes `InventoryListMessage(names)`. `TelnetOutputFormatter` renders it as `"You are carrying:\n  item1\n  item2"`.

**Cross-references.**
- [`Core/Modules/Items/Commands/InventoryCommand.cs`](../../../Core/Modules/Items/Commands/InventoryCommand.cs)
- [`Core/Output/InventoryListMessage.cs`](../../../Core/Output/InventoryListMessage.cs), [`Core/Output/TelnetOutputFormatter.cs`](../../../Core/Output/TelnetOutputFormatter.cs)
- [`docs/use-cases/items-and-inventory.md`](../../use-cases/items-and-inventory.md) — slice 6 spec, flow B-3

---

## Flow 12 — Admin item creation (`mkitem`)

**Summary.** A privileged session sends `mkitem [name]`. `MkitemCommand` delegates entity creation to `IItemBuilderSystem`, publishes `ItemCreatedByAdminEvent` (caught by `AdminAuditHandler`), calls `IPersistenceSystem.SaveEntityAsync` on the new item, and writes a confirmation showing the blueprint id.

**Trigger.** Privileged session sends `mkitem [name]`.

```mermaid
sequenceDiagram
    participant Sess as TelnetSession
    participant CD as CommandDispatcher
    participant Auth as IAuthorizationChecker
    participant Cmd as MkitemCommand
    participant IBS as IItemBuilderSystem
    participant PSys as IPersistenceSystem
    participant Bus as IEventBus
    participant Audit as AdminAuditHandler

    Sess->>CD: "mkitem a rusty dagger"
    CD->>Auth: IsSatisfied(AdminRequirement, session)
    alt unauthorized
        CD->>Sess: rejection PlainMessage
    else authorized
        CD->>Cmd: ExecuteAsync(CommandContext)
        Cmd->>IBS: CreateItem("a rusty dagger", roomEntityId)
        IBS-->>Cmd: ItemCreationResult(itemEntityId, "item.adhoc.x1y2z3")
        Cmd->>Bus: Publish(ItemCreatedByAdminEvent)
        Bus->>Audit: HandleAsync (priority 80) → structured log
        Cmd->>PSys: SaveEntityAsync(itemEntityId)
        Cmd->>Sess: confirmation PlainMessage (blueprint id shown)
    end
```

**Steps.**

1. `CommandDispatcher` routes `mkitem` to `MkitemCommand` after the privilege gate (`AdminRequirement` via `IAuthorizationChecker`).
2. `MkitemCommand.ExecuteAsync` reads `LocationComponent.RoomEntityId` from the invoker. If absent (no location), writes a `PlainMessage` error and returns.
3. Calls `IItemBuilderSystem.CreateItem(name, roomEntityId)` — allocates an entity, attaches `ItemDataComponent { Name, DamageBonus: 0 }` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent { RoomEntityId }`, registers a minimal `ItemTemplate`, returns `ItemCreationResult(itemEntityId, blueprintId)`. Blueprint id format: `item.adhoc.<8-char-base36>`.
4. Publishes `ItemCreatedByAdminEvent(adminId, itemEntityId, blueprintId, roomEntityId)`. `AdminAuditHandler` (priority 80) logs one structured entry.
5. Calls `IPersistenceSystem.SaveEntityAsync(itemEntityId)` directly — save-on-change; the item is durable before the admin sees confirmation.
6. Writes a confirmation `PlainMessage` (e.g. `"Item 'a rusty dagger' created. Blueprint id: item.adhoc.x1y2z3"`).

**Cross-references.**
- [`Core/Modules/Items/Commands/MkitemCommand.cs`](../../../Core/Modules/Items/Commands/MkitemCommand.cs), [`Core/Modules/Items/Systems/ItemBuilderSystem.cs`](../../../Core/Modules/Items/Systems/ItemBuilderSystem.cs)
- [`Core/Modules/Items/Events/ItemCreatedByAdminEvent.cs`](../../../Core/Modules/Items/Events/ItemCreatedByAdminEvent.cs)
- [`Core/Modules/Admin/Handlers/AdminAuditHandler.cs`](../../../Core/Modules/Admin/Handlers/AdminAuditHandler.cs)
- [`docs/use-cases/items-and-inventory.md`](../../use-cases/items-and-inventory.md) — slice 6 spec

---

## Flow 13 — `wear <item>`

**Summary.** Player wears a named item from their inventory. The item moves from `InventoryComponent` into `EquipmentComponent.Slots`. If any target slot is already occupied, the existing item is silently displaced back to inventory first. The player entity is saved; the room is notified.

**Trigger.** Player sends `wear <item>`.

```mermaid
sequenceDiagram
    participant Player
    participant Dispatcher as CommandDispatcher
    participant Cmd as WearCommand
    participant ItemSys as IItemSystem
    participant EqSys as IEquipmentSystem
    participant Bus as IEventBus
    participant Handler as EquipmentInteractionHandler

    Player->>Dispatcher: "wear sword"
    Dispatcher->>Cmd: ExecuteAsync(context)
    Cmd->>ItemSys: TryFindItemInInventory(playerEntityId, "sword")
    ItemSys-->>Cmd: itemEntityId
    Cmd->>EqSys: GetWornSlots(itemEntityId)
    EqSys-->>Cmd: [MainHand]
    Cmd->>EqSys: EquipItem(playerEntityId, itemEntityId)
    Note over EqSys: RemoveFromSlot for each occupied slot → inv;<br/>remove item from inv; place in Slots
    Cmd->>Bus: PublishAsync(ItemEquippedEvent)
    Bus->>Handler: HandleAsync(ItemEquippedEvent) [priority 80]
    Handler-->>Player: "You wear a short sword."
    Handler-->>Player: (others) "Korin wears a short sword."
    Cmd->>Persistence: SaveEntityAsync(playerEntityId)
```

**Steps.**

1. `CommandDispatcher` routes `wear` to `WearCommand`.
2. `ItemInInventoryResolver` builds `ResolvedCandidate` list from the invoker's `InventoryComponent.ItemEntityIds`; prefix-match selects the canonical item name.
3. `WearCommand.ExecuteAsync` calls `IItemSystem.TryFindItemInInventory(playerEntityId, canonicalName)`. On miss: "You aren't carrying that."
4. Calls `IEquipmentSystem.GetWornSlots(itemEntityId)` — reads `ItemDataComponent.WornSlots`. Empty → "You can't wear that."
5. Calls `IEquipmentSystem.EquipItem(playerEntityId, itemEntityId)`. Internally: for each declared slot, `RemoveFromSlot` displaces any existing item (silently, no event); then removes item id from `InventoryComponent`; places item id in each `EquipmentComponent.Slots` entry. Command never iterates slots.
6. Publishes `ItemEquippedEvent(playerEntityId, itemEntityId, slots)`.
7. `EquipmentInteractionHandler` (priority 80): reads `LocationComponent` from the player to find the room; broadcasts `"<PlayerName> wears <ItemName>."` to others; writes `"You wear <ItemName>."` to the player.
8. `WearCommand` calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)`.

**Cross-references.**
- [`Core/Modules/Items/Commands/WearCommand.cs`](../../../Core/Modules/Items/Commands/WearCommand.cs)
- [`Core/Modules/Items/Systems/EquipmentSystem.cs`](../../../Core/Modules/Items/Systems/EquipmentSystem.cs)
- [`Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs)
- [`docs/use-cases/equipment.md`](../../use-cases/equipment.md) — slice 7 spec

---

## Flow 14 — `remove <item>`

**Summary.** Player removes a worn item from their equipment slots. The item moves from `EquipmentComponent.Slots` back to `InventoryComponent`. The player entity is saved; the room is notified.

**Trigger.** Player sends `remove <item>`.

```mermaid
sequenceDiagram
    participant Player
    participant Dispatcher as CommandDispatcher
    participant Cmd as RemoveCommand
    participant EqSys as IEquipmentSystem
    participant Bus as IEventBus
    participant Handler as EquipmentInteractionHandler

    Player->>Dispatcher: "remove sword"
    Dispatcher->>Cmd: ExecuteAsync(context)
    Cmd->>EqSys: TryFindEquippedItem(playerEntityId, "sword")
    EqSys-->>Cmd: itemEntityId
    Cmd->>EqSys: GetWornSlots(itemEntityId)
    EqSys-->>Cmd: [MainHand]
    Cmd->>EqSys: RemoveItem(playerEntityId, itemEntityId)
    Note over EqSys: Clears slot(s) in EquipmentComponent;<br/>appends itemEntityId to InventoryComponent
    Cmd->>Bus: PublishAsync(ItemUnequippedEvent)
    Bus->>Handler: HandleAsync(ItemUnequippedEvent) [priority 80]
    Handler-->>Player: "You remove a short sword."
    Handler-->>Player: (others) "Korin removes a short sword."
    Cmd->>Persistence: SaveEntityAsync(playerEntityId)
```

**Steps.**

1. `CommandDispatcher` routes `remove` to `RemoveCommand`.
2. `ItemInEquipmentResolver` builds `ResolvedCandidate` list from the invoker's `EquipmentComponent.Slots.Values`; prefix-match selects the canonical item name.
3. `RemoveCommand.ExecuteAsync` calls `IEquipmentSystem.TryFindEquippedItem(playerEntityId, canonicalName)`. On miss: "You aren't wearing that."
4. Calls `IEquipmentSystem.GetWornSlots(itemEntityId)` to capture the slot list for the event payload.
5. Calls `IEquipmentSystem.RemoveItem(playerEntityId, itemEntityId)`: clears all `EquipmentComponent.Slots` entries that map to this item, appends the item id to `InventoryComponent.ItemEntityIds`.
6. Publishes `ItemUnequippedEvent(playerEntityId, itemEntityId, slots)`.
7. `EquipmentInteractionHandler` (priority 80): reads `LocationComponent` from the player; broadcasts `"<PlayerName> removes <ItemName>."` to others; writes `"You remove <ItemName>."` to the player.
8. `RemoveCommand` calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)`.

**Cross-references.**
- [`Core/Modules/Items/Commands/RemoveCommand.cs`](../../../Core/Modules/Items/Commands/RemoveCommand.cs)
- [`Core/Modules/Items/Systems/EquipmentSystem.cs`](../../../Core/Modules/Items/Systems/EquipmentSystem.cs)
- [`Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs)
- [`docs/use-cases/equipment.md`](../../use-cases/equipment.md) — slice 7 spec

---

## Flow 15 — Admin mob creation (`mkmob`)

**Summary.** A privileged session sends `mkmob [name]`. `MkMobCommand` delegates entity creation to `IMobBuilderSystem`, writes the YAML blueprint file via `IMobContentWriter` (YAML first — the template is durable before the entity id is persisted), calls `IPersistenceSystem.SaveEntityAsync` on the new mob entity, publishes `MobCreatedByAdminEvent` (caught by `AdminAuditHandler`), and writes a confirmation showing the blueprint id.

**Trigger.** Privileged session sends `mkmob [name]`.

```mermaid
sequenceDiagram
    participant Sess as TelnetSession
    participant CD as CommandDispatcher
    participant Auth as IAuthorizationChecker
    participant Cmd as MkMobCommand
    participant MBS as IMobBuilderSystem
    participant MCW as IMobContentWriter
    participant PSys as IPersistenceSystem
    participant Bus as IEventBus
    participant Audit as AdminAuditHandler

    Sess->>CD: "mkmob a kobold"
    CD->>Auth: IsSatisfied(AdminRequirement, session)
    alt unauthorized
        CD->>Sess: rejection PlainMessage
    else authorized
        CD->>Cmd: ExecuteAsync(CommandContext)
        Cmd->>MBS: CreateMob("a kobold", roomEntityId)
        MBS-->>Cmd: MobCreationResult(mobEntityId, "mob.adhoc.x1y2z3", template)
        Cmd->>MCW: WriteAsync(template)
        MCW-->>Cmd: (YAML written atomically to mobs/mob.adhoc.x1y2z3.yaml)
        Cmd->>PSys: SaveEntityAsync(mobEntityId)
        Cmd->>Bus: Publish(MobCreatedByAdminEvent)
        Bus->>Audit: HandleAsync (priority 80) → structured log
        Cmd->>Sess: confirmation PlainMessage (blueprint id shown)
    end
```

**Steps.**

1. `CommandDispatcher` routes `mkmob` to `MkMobCommand` after the privilege gate (`AdminRequirement` via `IAuthorizationChecker`).
2. `MkMobCommand.ExecuteAsync` reads `LocationComponent.RoomEntityId` from the invoker. If absent (no location), writes a `PlainMessage` error and returns.
3. Calls `IMobBuilderSystem.CreateMob(name, roomEntityId)` — allocates an entity, attaches `MobDataComponent { Name }` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent { RoomEntityId }`, registers a minimal `MobTemplate`, returns `MobCreationResult(mobEntityId, blueprintId, template)`. Blueprint id format: `mob.adhoc.<8-char-base36>`.
4. Calls `IMobContentWriter.WriteAsync(template)` — serializes the template to YAML and writes it atomically (tmp→rename) to `{contentDir}/mobs/{blueprintId}.yaml`. YAML is written before the entity is persisted so the blueprint definition is durable first; if the server crashes between step 4 and step 5, the YAML file is orphaned (discoverable on next `reload`) rather than an entity existing with no blueprint.
5. Calls `IPersistenceSystem.SaveEntityAsync(mobEntityId)` directly — save-on-change; the mob entity is durable before the admin sees confirmation.
6. Publishes `MobCreatedByAdminEvent(adminId, mobEntityId, blueprintId, roomEntityId)`. `AdminAuditHandler` (priority 80) logs one structured entry.
7. Writes a confirmation `PlainMessage` (e.g. `"Mob 'a kobold' created. Blueprint id: mob.adhoc.x1y2z3"`).

**Cross-references.**
- [`Core/Modules/Mobs/Commands/MkMobCommand.cs`](../../../Core/Modules/Mobs/Commands/MkMobCommand.cs), [`Core/Modules/Mobs/Systems/MobBuilderSystem.cs`](../../../Core/Modules/Mobs/Systems/MobBuilderSystem.cs)
- [`Core/Modules/Mobs/Events/MobCreatedByAdminEvent.cs`](../../../Core/Modules/Mobs/Events/MobCreatedByAdminEvent.cs)
- [`Core/Modules/Admin/Handlers/AdminAuditHandler.cs`](../../../Core/Modules/Admin/Handlers/AdminAuditHandler.cs)
- [`docs/use-cases/mobs.md`](../../use-cases/mobs.md) — slice 8 spec

---

## Flow 16 — Heartbeat tick

**Summary.** `HeartbeatBackgroundService` fires a `PeriodicTimer` at `Heartbeat:IntervalMs` (default 2000 ms), increments a monotonic counter, and publishes `HeartbeatTickEvent` to `IEventBus`. No game logic lives here; handlers subscribe independently. In slice 9-b no handlers are registered; the first subscriber (`CombatRoundHandler`) lands in slice 9.

**Trigger.** `PeriodicTimer.WaitForNextTickAsync` returns in `HeartbeatBackgroundService.ExecuteAsync`.

```mermaid
sequenceDiagram
    participant Timer as PeriodicTimer
    participant HBS as HeartbeatBackgroundService
    participant Bus as IEventBus
    participant H1 as (future handlers...)

    Timer->>HBS: WaitForNextTickAsync → true
    HBS->>HBS: increment _tickId, capture Timestamp, compute Elapsed
    HBS->>Bus: PublishAsync(HeartbeatTickEvent{TickId, Timestamp, Elapsed})
    Bus->>H1: HandleAsync (priority N) [slice 9+]
    HBS->>HBS: WaitForNextTickAsync (next tick)
```

**Steps.**

1. `PeriodicTimer.WaitForNextTickAsync(stoppingToken)` returns `true` (or throws `OperationCanceledException` on host shutdown → service exits).
2. `HeartbeatBackgroundService` increments `_tickId` (starts at 1 on first tick), captures `DateTimeOffset.UtcNow` as `now`, computes `Elapsed = now - _lastTimestamp`, and updates `_lastTimestamp = now`. `_lastTimestamp` is initialized to `DateTimeOffset.UtcNow` before the loop so the first tick's `Elapsed` reflects the actual interval.
3. Publishes `HeartbeatTickEvent { TickId, Timestamp, Elapsed }` via `IEventBus.PublishAsync`. Any uncaught exception from handlers is caught and logged at `Error`; the loop continues.
4. Event bus dispatches to all subscribed handlers in priority order. In this slice no handlers are registered.
5. Control returns to `WaitForNextTickAsync`. `PeriodicTimer` schedules the next tick relative to its period, not relative to when handler execution completed — overruns cause the next tick to fire immediately after the current one completes.

**Overrun.** If handler execution takes longer than `IntervalMs`, `PeriodicTimer` fires the next tick immediately after the current completes (no drift accumulation, but no backpressure either). Acknowledged for Phase 4 hardening.

**Thread safety.** `ExecuteAsync` runs on a background thread. `IEventBus.PublishAsync` is called from that thread — the same cross-thread pattern used by `PersistenceFlushTimer` and `WorldContentBootstrap`. Single `PeriodicTimer` means no concurrent self-publish. Phase 4 thread-safety review covers the event bus under concurrent background-service access.

**Cross-references.**
- [`Core/Modules/Time/Events/HeartbeatTickEvent.cs`](../../../Core/Modules/Time/Events/HeartbeatTickEvent.cs)
- [`Server/HeartbeatBackgroundService.cs`](../../../Server/HeartbeatBackgroundService.cs)
- [`docs/use-cases/time-system.md`](../../use-cases/time-system.md) — slice 9-b spec

---

## Adding a new flow

When a slice introduces a recurring runtime call chain (combat round, player death, item pickup, mob wander tick, save-on-mutation pulse, etc.), add a new section here following the format above:

1. **Summary** (1–3 sentences)
2. **Trigger**
3. **Mermaid sequence diagram** — keep participants to ≤ 7 boxes; if the flow is too wide for that, you're describing two flows
4. **Steps** — numbered prose with file references
5. **Cross-references** — links to the relevant systems, handlers, and use cases
6. **Update the index** at the top of this file

The use-case-planner agent surfaces flow additions as part of its workflow; the architecture-reviewer agent verifies the doc matches the diff. Drift between code and this file is a merge gate.

---

## When to split this file

This single file is the flows catalog today. It is **pre-wired to split**: when it crosses **~12 flows or ~900 lines**, promote each flow to its own `flows/flow-<n>-<name>.md` and keep this `README.md` as the index (the table above). Inbound references already point at `flows/README.md` (the index) and cite flows by number, so the split is mechanical — only this index gains the per-flow links; no external reference changes.
