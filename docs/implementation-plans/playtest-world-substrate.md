# Playtest world substrate (PT-1)

- **Status:** planned
- **Actors:** System (the playtest world, its tick driver, scripted callers in tests), Player (simulated, via `BotSession`), Administrator (simulated setup actor applying R6 positioning through admin verbs), Maintainer (reads the R27 policy)
- **Module:**
  - new `Core/Modules/Playtest/` — the world, its factory, the tick driver, `BotSession`, the composition seam interface;
  - `Server/` — `CompositionRoot.SubscribeGameplayHandlers`, `AddWorldBootstrapHostedServices`, and `Server/Playtest/PlaytestEngineComposer.cs`;
  - `Core/Modules/Session/` — the shared session-entry path (`SessionEntry`);
  - `Core/Output/` — the agent formatter (`playtest` transport) and the `NarrationMessage` base;
  - `Core/Systems/` — `SteppedClock`;
  - the bot-critical message conversions in `Combat`, `Death`, `Progression`, `Economy`, `Shopping`, `Items`.

  Feature home on ship: a new `docs/features/playtest/` (sibling of `simulation/`).
- **Program:** slice PT-1 of [`playtest-automation-program.md`](playtest-automation-program.md#slice-decomposition). Covers **R2–R6 and R27–R28**, plus the brief's "build now" seams: the subscription and session-entry extractions, per-world composition, the tick driver as single writer, and the agent formatter with the bot-critical message ratchet.

## Description

PT-1 makes the real engine bootable as many isolated, headless, seeded worlds in one process, playable only through the player command path. Two pieces of the live host are extracted first, so a playtest world never carries a drifting second copy (INV-19):

- the ~69 gameplay `bus.Subscribe` calls in `Server/Program.cs` `Main` move into `CompositionRoot.SubscribeGameplayHandlers`;
- the post-authentication "character is created / enters / leaves the world" tail of `LoginFlow`/`TelnetSession` moves into a transport-neutral `SessionEntry`.

A **playtest world** composes the same container as the live host, with these differences:

- no hosted listener, heartbeat or flush timer;
- a seeded `IRandom` and a `SteppedClock`;
- a per-world in-memory SQLite database;
- a scratch copy of the content directory, so every content write lands in the copy.

A **tick driver**, the world's single writer, advances it in steps: dispatch each bot's queued command line → publish `HeartbeatTickEvent` with `Elapsed` = the configured interval → output flush (handler 85) → drain observations. Bots are `BotSession`s on a new `playtest` transport. An **agent formatter** renders each output message as one JSON observation. The messages a scripted walker must branch on are converted from `PlainMessage` to typed messages through a new `NarrationMessage` base, so telnet output is byte-identical by construction. The R27 autonomy amendment gets its one living home in `design/balance.md`. Tests drive worlds with command lines. There are no bot policies, recorder, grader, scenarios or CLI (those are PT-2).

## Preconditions

- The program brief's Placement, Seams, Observers/ordering and Resolved decisions are settled ([program](playtest-automation-program.md#architecture-brief)).
- Verified in code (2026-10-29):
  - All gameplay subscriptions are in `Server/Program.cs` `Main`, lines 81–207: 69 `Subscribe` calls. No `Subscribe` call exists anywhere in `Core/` or `Hedron.Web/`. The `generate`/`simulate` run modes subscribe nothing. `Hedron.Web` deliberately subscribes nothing ([08-blazor.md](../architecture/08-blazor.md)).
  - `CompositionRoot.Register` is pure DI except for one process-static write: `EcsManager.SetWorld(world)`. No production code reads `EcsManager.World`.
  - `AccountSystem` already takes an injected `IClock` for every timestamp, so the brief's "wall-clock debt" is **already fixed** in code. The stale doc text (checklist INV-26, `07-testing.md`, `architecture-reviewer.md`) is **owned by dev-world-foundation WP1**, not this slice. `SpawnSystem`, `ShopSystem` and `ShopExpiryTickHandler` also read `IClock`.
  - `TemplateRegistry.AllBlueprintIds()` enumerates a `ConcurrentDictionary<string,…>`, whose bucket order depends on per-process randomized string hashing. Boot spawn order, and therefore entity ids and target-resolution order, can differ between two processes. `WorldContentLoader.LoadKindAsync` also registers files in unordered `Directory.GetFiles` order (precedents for ordinal sorting: `SimScenarioStore.cs:190`, `GenerationRunMode.cs:202`).
  - `WearCommand` (~line 90) and `RemoveCommand` (~line 80) already call `SaveEntityAsync` after equip/unequip, outside INV-22's three categories. This is pre-existing and tracked in the backlog.
  - `SessionOutputBuffer.FlushAsync` renders each message through `IOutputFormatterRegistry.Resolve(session)`, then appends a composed `PromptMessage` (preceded by a bare `string.Empty` line when anything was flushed). `OutputFormatterRegistry`'s fallback is the **first registered** formatter (telnet).
  - `CommandDispatcher.DispatchAsync` flushes the invoker's buffer in its `finally`. `OutputFlushTickHandler` (priority `OutputFlush` = 85) flushes every pending buffer on each heartbeat.
  - `PersistenceTestHarness` (tests only) proves the in-memory pattern: a keeper connection to `file:<unique>?mode=memory&cache=shared` plus `PersistenceOptions.DatabasePath` set to the same URI. `PersistenceSystem` I/O is synchronous.
  - There are no `TelnetOutputFormatter` tests today.
- [Phase 5 slice 1](dev-world-foundation.md) is planned, not built. PT-1 composes cleanly either way; see [Design notes → slice-1 interaction](#interaction-with-phase-5-slice-1-dev-world-foundation).

## Postconditions

*(The coverage contract. ◆ = asserts internal state and maps to a named test in the Test plan.)*

1. ◆ `Server/Program.cs` contains no `Subscribe` call. The live host's subscriptions are exactly those made by `CompositionRoot.SubscribeGameplayHandlers(IServiceProvider)`, and every DI-registered `IEventHandler<T>` is subscribed for each `T` it implements (explicit allowlist for deliberate exceptions, expected empty).
2. ◆ A playtest world's bus holds exactly the same `(event type, handler type, priority)` set as the live composition (`Register` + `SubscribeGameplayHandlers`).
3. ◆ No engine assembly (`Core`, `Server`) has a non-`readonly` static field. Every `static readonly` field of a mutable collection or array type is on a reviewed allowlist of never-written tables. `EcsManager` is deleted.
4. ◆ `TemplateRegistry.AllBlueprintIds()` returns ids in ordinal (case-insensitive) order, whatever the registration order, and the `ITemplateRegistry` contract states it. `WorldContentLoader.LoadKindAsync` reads each kind's files in ordinal path order.
5. ◆ `IAccountSystem` owns the username, password and character-name validation rules. `LoginFlow` and the bot path both apply them, with error texts unchanged. An invalid bot name fails `AddBotAsync` with a harness error (`ArgumentException` carrying the validator's text) before any entity is created.
6. ◆ `SessionEntry.CreateCharacterAsync` creates the character, saves the character and then the account (INV-22 category 1, order preserved), publishes `AccountCreatedEvent` only when a new-account username is given, then `CharacterCreatedEvent`. `EnterWorldAsync` registers the session **before** publishing `PlayerConnectedEvent`, then flushes. `LeaveWorldAsync` publishes `PlayerDisconnectedEvent` and then unregisters. `TelnetSession`/`LoginFlow` use exactly these calls.
7. ◆ A playtest world boots from a **scratch copy** of its source content directory. The source directory is byte-identical after any run, including one that ran builder verbs. The scratch directory is deleted on dispose unless `KeepScratch` is set.
8. ◆ A playtest world persists to its own uniquely named in-memory SQLite database. No file is created at the base configuration's `Persistence:DatabasePath`.
9. ◆ A bot enters through `SessionEntry`: a fresh account and character, bound to a `BotSession` (`TransportKey == "playtest"`). Every `BotSession.SessionId`, the setup session's included, is deterministic: setup uses reserved index 0 and bots use 1…N. It lands at `WorldConfiguration.StartingRoomEntityId`, carries `PlayerComponent`, and its first observations include the starting room's `roomDescription` and a `prompt`.
10. ◆ One `StepAsync()` does the following in order:
    - dispatches at most `CommandsPerBotPerStep` queued lines per bot, in bot-creation order;
    - advances the `SteppedClock` by exactly `Heartbeat:IntervalMs`;
    - publishes `HeartbeatTickEvent(TickId = previous + 1, Timestamp = clock now, Elapsed = interval)`;
    - stamps every line delivered during the step with that step's tick.

    Spawn respawn delays elapse in stepped game time. An exception thrown while publishing the tick is caught and logged, the world's `TickFaults` counter increments, and the step still completes the drain. This mirrors `HeartbeatBackgroundService`. Command exceptions are already caught by the dispatcher (`CommandOutcome.Threw`).
11. ◆ Setup (R6) runs through a privileged **setup session**, whose name is added to the world's `Admin:PrivilegedNames` overlay, dispatching ordinary admin verbs. A bot is never privileged: `AddBotAsync` throws `InvalidOperationException` for a bot name that matches any privileged name, before any entity is created.
12. ◆ **Determinism (R3, within the substrate):**
    - two worlds with the same seed, fixture and command script produce identical per-bot observation logs;
    - a different seed produces a different log over the chance-bearing fixture;
    - two same-seed worlds stepped **concurrently** produce logs identical to a sequential run.
13. ◆ Concurrent entry into one world's public API throws `InvalidOperationException`, because the world is confined to a single logical writer. This covers overlapping `StepAsync` calls, and `Enqueue` (or `Drain`) during an in-flight `StepAsync`.
14. ◆ The agent formatter renders:
    - `PlainMessage` as `{type:"plain", category, severity, text}`;
    - any other typed message as its runtime type's public properties (camelCase, enums as names) with a unique `type` discriminator, minus `[JsonIgnore]` members.

    No emitted property name ends in `EntityId`. An unserializable message renders as `{type, category, error}` and is logged, never thrown. The registry's fallback is still telnet.
15. ◆ Each bot-critical narration site emits its typed `NarrationMessage` subclass to the same audience as before, with the facts listed in [Systems / handlers](#systems--handlers-involved).
    - Each converted site's `Text` equals its pre-conversion string, pinned by characterization assertions (T27 for handler sites, T28 for every `CommandRefusalMessage` site).
    - Telnet renders each type **byte-identical** to `PlainMessage(Text, Severity, Category)`, in color and without (T26).
16. Every log line a world emits, at boot, `AddBotAsync`, setup, each step, and dispose, runs inside a scope that carries its label and seed (R28). The R27 rule lives in `design/balance.md` and is cited by `balance-tuning`. The 2026-07-17 record carries a "superseded by" pointer.

## Main flow

1. **Compose.** The caller passes `PlaytestWorldOptions` (seed, source content directory, base configuration, label) to `PlaytestWorldFactory.CreateAsync`. The factory:
   - copies the source directory to `<temp>/hedron-playtest/<worldId>/content`;
   - builds the overlay as a plain `IReadOnlyDictionary<string, string?>`: content dir → scratch; `Persistence:DatabasePath` → a unique shared-cache in-memory URI, held open by a keeper connection; balance standards and simulation paths → scratch; the setup actor appended to `Admin:PrivilegedNames`; an optional `World:StartingRoomBlueprintId` fallback;
   - calls `IPlaytestEngineComposer.ComposeAndBootAsync(baseConfiguration, overlay, overrides, ct)`. The overrides register `IRandom` as `SeededRandom(seed)` and `IClock` as one `SteppedClock`.
2. **Boot** (`Server/Playtest/PlaytestEngineComposer`):
   - builds the world `IConfiguration` (base + `AddInMemoryCollection(overlay)`), then `AddLogging` → registers the caller's shared `PlaytestWorldOptions.LoggerFactory` as the container's `ILoggerFactory` (the R28 scopes are opened on it) → `AddSingleton<IConfiguration>` → `services.Register(config)` → apply the overrides → `AddWorldBootstrapHostedServices()` → build;
   - `provider.SubscribeGameplayHandlers()`;
   - **constructs every hosted service first** (`GetServices<IHostedService>().ToList()`), exactly as the generic host does. This matters because `RegistryValidationBootstrap`'s constructor forces the balance-standards load (flow-01 step 2);
   - then `StartAsync` each bootstrap in registration order: `PersistenceBootstrap` (empty in-memory DB) → `WorldContentBootstrap` (`LoadAndSpawnAsync` + `WorldContentReadyEvent` fan-out, starting-room resolution) → `RegistryValidationBootstrap` (fail-fast);
   - returns an `IPlaytestEngine` (`IServiceProvider Services` + `DisposeAsync`, which stops the bootstraps in reverse order and disposes the provider). The Server owns the `ServiceProvider` and `IHostedService` types, so Core takes no new package reference.

   The telnet listener, the wall-clock heartbeat and the flush timer are never registered.
3. **Enter a bot** (`PlaytestWorld.AddBotAsync(name)`):
   - `IAccountSystem` validators run, and an invalid name is a harness error;
   - `CreateAccountAsync(username, harness password)`, then `SessionEntry.CreateCharacterAsync(accountId, name, newAccountUsername)`;
   - `BotSession.Bind(characterId)`, then `SessionEntry.EnterWorldAsync(session, accountId, name)`, which registers the session, publishes `PlayerConnectedEvent` (`PlayerSessionHandler` attaches `PlayerComponent`, announces arrival, sends the room description), and flushes.

   Lines arrive on `BotSession.SendLineAsync`. Blank lines are dropped; the rest are stamped `(Tick, Seq)`.
4. **Setup (optional, R6).** `RunSetupAsync(lines)` enters the setup session the same way, dispatches each line through `ICommandDispatcher` (admin verbs pass the privilege gate), returns its transcript, then calls `LeaveWorldAsync`.
5. **Act.** `Enqueue(botId, line)` appends to the bot's FIFO queue.
6. **Step** (`PlaytestTickDriver.StepAsync`, the only writer):
   - (a) for each bot in creation order, dequeue up to `CommandsPerBotPerStep` lines → `ICommandDispatcher.DispatchAsync(session, line)` (the dispatcher's `finally` flushes that bot);
   - (b) `SteppedClock.Advance(interval)`;
   - (c) `IEventBus.PublishAsync(new HeartbeatTickEvent(++tick, now, interval))` inside a catch-and-log (increments `TickFaults`, as the live heartbeat does). Existing priorities are unchanged; `OutputFlushTickHandler` (85) flushes every pending buffer;
   - (d) observations delivered during the step are moved to each bot's drainable log.
7. **Observe.** `Drain(botId)` returns and clears that bot's `Observation(Tick, Seq, Json)` list. `GetStatus(botId)` returns harness metadata only (tick, game time, queued count, in-world); it never reads world state (R4).
8. **Dispose.** Each bot calls `LeaveWorldAsync`. `PlayerSessionHandler` does the session-end save into the in-memory DB. Then `IPlaytestEngine.DisposeAsync` runs, the keeper connection is closed, and the scratch directory is deleted (unless kept).

Steps 1–8 all run inside the world's R28 logging scope.

## Events fired

| Event | Publisher | Subscribers | Status |
|---|---|---|---|
| `HeartbeatTickEvent(TickId, Timestamp, Elapsed)` | `PlaytestTickDriver` (new Initiator; second publisher beside `HeartbeatBackgroundService`) | unchanged set | reused |
| `AccountCreatedEvent`, `CharacterCreatedEvent` | `SessionEntry.CreateCharacterAsync` (moved from `LoginFlow`) | unchanged (none today) | reused |
| `PlayerConnectedEvent` / `PlayerDisconnectedEvent` | `SessionEntry.EnterWorldAsync` / `LeaveWorldAsync` (moved from `TelnetSession`) | `PlayerSessionHandler` | reused |
| `WorldLoadedEvent`, `EntityHydratedEvent`, `WorldContentReadyEvent` | the existing bootstraps, now also started by the composer | unchanged | reused |
| `CommandExecutedEvent` | `CommandDispatcher` | `CommandLoggingHandler` | reused |

There are **no new events**, consistent with the brief's "no new domain events".

## Systems / handlers involved

| Piece | Kind | Status | Shape / change |
|---|---|---|---|
| `CompositionRoot.SubscribeGameplayHandlers(this IServiceProvider)` | composition | **new** | Lines 81–207 of `Main`, moved verbatim (same order) |
| `CompositionRoot.AddWorldBootstrapHostedServices` | composition | **new** | `PersistenceBootstrap`, `WorldContentBootstrap`, `RegistryValidationBootstrap` in order. `AddGameplayHostedServices` = this + flush timer + telnet + heartbeat (still 6) |
| `EventBus.GetSubscriptions()` | core infra | **new (read-only inspection)** | `IReadOnlyList<(Type EventType, Type HandlerType, int Priority)>` snapshot under the lock, in dispatch order. On the concrete type only; `IEventBus` unchanged |
| `EcsManager` | static global | **deleted** | The `SetWorld` call is removed from `Register`. The living docs that use `EcsManager.World` as shorthand are updated |
| `ITemplateRegistry.AllBlueprintIds` / `TemplateRegistry` | core system | **changed** | Returns `OrderBy(id, StringComparer.OrdinalIgnoreCase)`. The interface XML doc states the ordering as part of the contract |
| `WorldContentLoader.LoadKindAsync` | domain system | **changed** | `Directory.GetFiles(...)` is ordered `StringComparer.Ordinal` (matching the `SimScenarioStore`/`GenerationRunMode` precedents) |
| `IAccountSystem` / `AccountSystem` | domain system | **changed** | + `string? ValidateUsername(string)`, `string? ValidatePassword(string)`, `string? ValidateCharacterName(string)` (moved from `LoginFlow`, texts unchanged) |
| `ISessionEntry` / `SessionEntry` (`Core/Modules/Session/`) | Initiator-tier helper (publishes; not under `Systems/`) | **new** | `Task<uint> CreateCharacterAsync(uint accountId, string name, string? newAccountUsername, CancellationToken ct = default)`; `Task EnterWorldAsync(ISession session, uint accountId, string name)` (precondition: `session.PlayerEntityId` bound); `Task LeaveWorldAsync(ISession session, string name)`. Registered in `CompositionRoot` beside `PlayerSessionHandler` |
| `LoginFlow`, `TelnetSession`, `TelnetServer` | Initiators (Server) | **changed** | Wizard and prompts unchanged. The creation tail calls `SessionEntry.CreateCharacterAsync`; bind → `EnterWorldAsync`; `finally` → `LeaveWorldAsync`. `LoginFlow` drops `IEventBus`/`IPersistenceSystem` |
| `SteppedClock : IClock` (`Core/Systems/`) | core seam impl | **new** | `SteppedClock(DateTime epochUtc)`, `UtcNow`, `Advance(TimeSpan)`; fixed default epoch `2026-01-01T00:00:00Z` |
| `IPlaytestEngineComposer`, `IPlaytestEngine` (`Core/Modules/Playtest/`) / `PlaytestEngineComposer` (`Server/Playtest/`) | composition seam | **new** | `Task<IPlaytestEngine> ComposeAndBootAsync(IConfiguration? baseConfiguration, IReadOnlyDictionary<string, string?> overlay, Action<IServiceCollection> overrides, CancellationToken ct)`. `IPlaytestEngine : IAsyncDisposable { IServiceProvider Services { get; } }`. Core uses only abstractions it already references (DI and Configuration abstractions). `ConfigurationBuilder`, `ServiceProvider` and `IHostedService` stay in `Server`, so Core gains **no package reference** |
| `PlaytestWorldOptions` | record | **new** | `int Seed`, `string ContentSourceDirectory`, `IConfiguration? BaseConfiguration`, `string? StartingRoomFallbackId`, `string Label`, `int CommandsPerBotPerStep = 1`, `bool KeepScratch`, `ILoggerFactory? LoggerFactory`, `string SetupCharacterName = "Setup"` |
| `PlaytestWorldFactory` | harness (Initiator tier) | **new** | `Task<PlaytestWorld> CreateAsync(PlaytestWorldOptions, CancellationToken)`. Owns the scratch copy, the overlay, the keeper connection and the composer call |
| `PlaytestWorld` | harness (Initiator tier) | **new** | The world API: `Task<BotId> AddBotAsync(string name)`; `void Enqueue(BotId, string line)`; `Task StepAsync(int steps = 1)`; `IReadOnlyList<Observation> Drain(BotId)`; `BotStatus GetStatus(BotId)`; `Task<IReadOnlyList<Observation>> RunSetupAsync(IReadOnlyList<string> lines)`; `long Tick`; `int TickFaults`; `IAsyncDisposable`. Has a re-entrancy guard on every member. Exposes no `EntityService`, no provider and no system (R4) |
| `PlaytestTickDriver` | Initiator (publishes `HeartbeatTickEvent`) | **new** | `Task StepAsync()` per Main flow step 6, with catch-and-log around the tick publish. Kept out of any `*.Systems` namespace (the no-bus-in-systems guard) |
| `BotSession : ISession` | transport | **new** | `TransportKey = "playtest"`, `SupportsColor = false`, deterministic `SessionId` (from the session index: 0 = setup, 1…N = bots), `Bind(uint)`, `SendLineAsync` appends non-blank lines stamped with the driver's current tick |
| `Observation`, `BotId`, `BotStatus` | records | **new** | `Observation(long Tick, int Seq, string Json)`; `BotStatus(BotId, string Name, long Tick, TimeSpan GameTime, int Queued, bool InWorld)` |
| `AgentOutputFormatter : IOutputFormatter` (`Core/Output/`) | output framework | **new** | `TransportKey = "playtest"`. Compact one-line JSON per message (see Postcondition 14). Registered in `Register` **after** `TelnetOutputFormatter`, so the fallback is unchanged. Rule (documented in `output.md`): every public property of a typed message must be player-visible or `[JsonIgnore]` |
| `NarrationMessage` (`Core/Output/`) | output base record | **new** | `abstract record NarrationMessage(string Text, OutputSeverity Severity, OutputCategory Category) : IOutputMessage`. Telnet adds one case rendering exactly as `PlainMessage`. This is the on-touch structuring ratchet's unit |
| `RoomDescriptionMessage.RoomEntityId` | output | **changed** | `[JsonIgnore]` (not player-visible; telnet unaffected) |
| `CombatNarrationMessage` (`Core/Modules/Combat/`) | typed message | **new** | `Kind {Engaged, Missed, Hit, Slain, Fled, Incapacitated}`, `Perspective {Self, Others, Room}`, `ActorName`, `TargetName`, `int? Damage`. Replaces all 11 `CombatHandler` sites |
| `DeathNarrationMessage` (`Core/Modules/Death/`) | typed message | **new** | `Kind {Incapacitated, Bleeding, Died, Respawned}`, `Perspective`, `SubjectName`, `int? CurrentHp`, `int? HpFloor`, `string? RoomName`. Replaces the 7 `DeathNarrationHandler` sites |
| `ProgressionNarrationMessage` (`Core/Modules/Progression/`) | typed message | **new** | `Kind {XpAwarded, Improved}`, `string Track` (score or ability id), `bool IsAbility`, `int? NewImprovementCount`. 1 site (4 texts) |
| `CurrencyAwardedMessage` (`Core/Modules/Economy/`) | typed message | **new** | `long Amount`, `CurrencyId Currency`. 1 site |
| `ShopTransactionMessage` (`Core/Modules/Shopping/`) | typed message | **new** | `Kind {Bought, Sold}`, `Perspective`, `ActorName`, `ItemName`, `long Price`, `CurrencyId Currency`. Replaces the 4 `ShopInteractionHandler` sites |
| `ShopListingMessage` (`Core/Modules/Shopping/`) | typed message | **new** | `IReadOnlyList<ShopListingLine(ItemName, Price, Currency, IsBuyBack)>`. Replaces `ListCommand`'s listing and "nothing for sale" (Text kept verbatim) |
| `ItemNarrationMessage` (`Core/Modules/Items/`) | typed message | **new** | `Kind {PickedUp, Dropped, Worn, Removed}`, `Perspective`, `ActorName`, `ItemName`. Replaces 4 `ItemInteractionHandler` + 4 `EquipmentInteractionHandler` sites |
| `CommandRefusalMessage` (`Core/Output/`) | typed message | **new** | `string Verb`, `RefusalKind Kind {MissingArgument, NoLocation, TargetNotFound, AlreadyInCombat, NotInCombat, TargetProtected, StateBlocked, NoShopkeeper, NotStocked, NotCarrying, NotWearable, Declined}`, original severity kept. Replaces the business refusals in `KillCommand` (5), `FleeCommand` (1), `BuyCommand` (5), `SellCommand` (5), `ListCommand` (2), `WearCommand` (3) and `GetCommand` (3). `Declined` carries the system's `FailureReason` text (e.g. "You cannot afford that.") |
| `PlayerSessionHandler`, `CharacterHydrationHandler`, `PersistenceSystem`, `SpawnSystem`, every gameplay handler priority | — | **unchanged** | — |

**Deliberately left as `PlainMessage` (on-touch ratchet):**
- the dispatcher's framework refusals, which already carry `Severity.Error`;
- `AbilityStrikeHandler` narration (the walker branches on combat end, not strike prose);
- arrival/departure lines;
- ascension, admin and builder output;
- about 165 other sites.

## Implementation plan — work packages

### WP1 — Composition and session-entry extractions; per-container proof

- **Scope:**
  1. Add `SubscribeGameplayHandlers`, a verbatim move. `Program.Main` calls `host.Services.SubscribeGameplayHandlers()`.
  2. Add `AddWorldBootstrapHostedServices`; `AddGameplayHostedServices` composes it.
  3. Add `EventBus.GetSubscriptions()`.
  4. Delete `EcsManager` and its `SetWorld` call.
  5. Make `TemplateRegistry.AllBlueprintIds` ordinal-sorted.
  6. Move the validators onto `IAccountSystem`.
  7. Add `ISessionEntry`/`SessionEntry` and rewire `LoginFlow`/`TelnetSession`/`TelnetServer`.
  8. Add the guard tests (subscription completeness, no `Subscribe` in `Program.cs`, no mutable static state). Retarget the existing `ArchitectureGuardTests.Simulation_module_does_not_reference_EventBus_or_EcsManager` (lines 743–785): it is renamed `Simulation_module_does_not_reference_EventBus`, and its `EcsManager` clause is dropped, because the deleted type can no longer be referenced and T4 covers any successor.
  9. Order `WorldContentLoader.LoadKindAsync`'s `Directory.GetFiles` result ordinally.
- **Files:**
  - `Server/{Program.cs, CompositionRoot.cs, Sessions/LoginFlow.cs, Sessions/TelnetSession.cs, Sessions/TelnetServer.cs}`;
  - `Core/Events/EventBus.cs`; `Core/ECS/EcsManager.cs` (delete); `Core/Systems/{ITemplateRegistry.cs, TemplateRegistry.cs}`; `Core/Modules/World/Systems/WorldContentLoader.cs`;
  - `Core/Modules/Account/Systems/{IAccountSystem.cs, AccountSystem.cs}`;
  - `Core/Modules/Session/{ISessionEntry.cs, SessionEntry.cs}`;
  - tests under `Hedron.Tests/{Architecture, Composition, Events, Account, Session, Systems}/`, including the retargeted guard in `Hedron.Tests/Architecture/ArchitectureGuardTests.cs`.
- **Docs:**
  - flows 01, 02, 07;
  - [`01-layers.md`](../architecture/01-layers.md) §Registration (already drifted: it says modules subscribe in their extension); [`08-blazor.md`](../architecture/08-blazor.md) line 36.
  - **`EcsManager` deletion:**
    - **INV-15 doc-first change:** [`00-overview.md`](../architecture/00-overview.md) line 5 calls `EcsManager.World` the idealized API. It is rewritten *before* the code change, so the target becomes "inject `EntityService`; there is no process-global world accessor, because many worlds may share a process (INV-12 nuance)". In the same file, line 3 ("two hosts" boot `Register`) gains the playtest composer as the third composition, and line 63's code-layout table drops `EcsManager.cs`.
    - Then [`02-ecs.md`](../architecture/02-ecs.md) and [`04-pitfalls.md`](../architecture/04-pitfalls.md), the same shorthand;
    - [`reference/components.md`](../reference/components.md) line 89 and [`reference/systems.md`](../reference/systems.md) line 796;
    - [`features/simulation/simulation-engine.md`](../features/simulation/simulation-engine.md) lines 72–73;
    - [flow-33](../architecture/flows/flow-33-simulation-run.md) lines 7 and 152. Its stated invariant "never calls `EcsManager.SetWorld`" becomes "uses no process-global world: the sandbox `EntityService` is per run".
  - **`LoginFlow` → `SessionEntry` (INV-22 category-1 site rename):**
    - `checklist.md` INV-22 item 1;
    - [`06-persistence.md`](../architecture/06-persistence.md) lines 85–88 (the category-1 explanation and code sample now show `SessionEntry.CreateCharacterAsync`);
    - [`features/accounts/account-system.md`](../features/accounts/account-system.md) lines 7, 26, 34; `features/accounts/{accounts.md, login-flow.md}`;
    - [`features/mobs/mob-system.md`](../features/mobs/mob-system.md) line 57 (the `LoginFlow` save-ordering precedent now cites `SessionEntry.CreateCharacterAsync`);
    - [`reference/systems.md`](../reference/systems.md): the `AccountSystem`/`IAccountSystem` row (~line 346) gains the validators and the creation-save caller; the `ISession` row (line 184) gains a note on the `BotSession` implementation and the `playtest` transport key (WP2). `SessionEntry` goes under **"Background Services / Initiators"**, not domain systems. The `TemplateRegistry` row gains its ordering, and the `EventBus` row gains its inspection method.
  - Each living `features/` doc that names `Program.cs` as the subscription home (grep: 13 files under `architecture/`, `features/`, `reference/`).
  - **INV-26 wall-clock doc text: owned by dev-world-foundation WP1.**
  - **Skills (INV-20):**
    - `add-handler`: step 2 and the INV-22 wording;
    - `add-event`: step 5;
    - `add-command`: step 8 and the category-1 site;
    - `edit-progression-system`: step 5;
    - `add-core-system` (line 89) and `add-domain-system` (line 115): `AddWorldBootstrapHostedServices`, and the playtest world as a third composition of `Register` beside the Server and Web hosts.
- **Depends on:** nothing.
- **Out of scope:** anything under `Core/Modules/Playtest/`, the agent formatter, and message typing. Do not touch `CharacterHydrationHandler` (slice 1 changes its read).
- **Composition note:** if PT-1 merges before slice 1, slice 1's new `AdminAuditHandler` subscription (`StartingRoomSetByAdminEvent`) goes into `CompositionRoot.SubscribeGameplayHandlers`, not `Program.cs`. T1 and T2 enforce this.
- **Exit criterion:** T1–T9, T10a, T30 and T31 green; `dotnet build` + `dotnet test` green; a manual telnet smoke (register, create, enter, `quit`) behaves exactly as before.

### WP2 — Playtest world: composition, tick driver, `BotSession`, agent formatter

- **Scope:**
  - `SteppedClock`;
  - the `Core/Modules/Playtest/` types (`PlaytestWorldOptions`, `PlaytestWorldFactory`, `PlaytestWorld`, `PlaytestTickDriver`, `BotSession`, `Observation`/`BotId`/`BotStatus`, `IPlaytestEngineComposer`/`IPlaytestEngine`);
  - `Server/Playtest/PlaytestEngineComposer.cs`;
  - `AgentOutputFormatter` + its registration, and `[JsonIgnore]` on `RoomDescriptionMessage.RoomEntityId`;
  - the R28 logging scope (`PlaytestWorld={Label} Seed={Seed}`) around boot, `AddBotAsync`, setup, each step, and dispose;
  - test helpers in `Hedron.Tests/Playtest/`:
    - `PlaytestFixtureContent.cs` writes a minimal generic world to a temp dir: two linked rooms, a hostile mob with a spawn rule and a short respawn delay, a shopkeeper with one stock item, and a found item. It mirrors the existing temp-content flow-test fixtures.
    - `CapturingEngineComposer.cs` is the test-only inspection seam (see Test plan).
- **Files:** as listed above, plus `Core/Systems/SteppedClock.cs`, `Core/Output/AgentOutputFormatter.cs`, `Core/Output/RoomDescriptionMessage.cs`, `Server/CompositionRoot.cs` (formatter registration), and tests under `Hedron.Tests/Playtest/`, `Hedron.Tests/Output/`, `Hedron.Tests/Architecture/`. **No new package references in `Core.csproj`:** the composer seam uses only the DI and Configuration abstractions Core already references.
- **Docs:**
  - the new [flow-34](#flows-introduced-or-modified) file + index row; flow-16 (second tick publisher, stepped `Elapsed`, catch-and-log); flow-06 (`playtest` transport leg, blank-line drop);
  - [`features/output/output.md`](../features/output/output.md): the agent formatter, plus the R4 rule that every public property of a typed message is player-visible or `[JsonIgnore]`;
  - `reference/systems.md`: `SteppedClock` with the core seams. `PlaytestTickDriver` and the playtest world go under **"Background Services / Initiators"**;
  - [`07-testing.md`](../architecture/07-testing.md): the harness table gains "playtest world (Tier 3 over real content)" and the capturing-composer inspection seam, plus the parallelism posture;
  - skill `add-tests`: when to use a playtest world as a Tier-3 harness, `PlaytestFixtureContent`, and the capturing composer;
  - **backlog entry (firm deliverable):** "Seed builder-verb ad-hoc id minting (`AdhocBlueprintId`, `Room/Area/Item/MobBuilderSystem`, currently `Guid.NewGuid()`) through the `IRandom` seam **before PT-4** (defect overlays run builder verbs in graded, replayable worlds)."
- **Depends on:** WP1 (`SubscribeGameplayHandlers`, `AddWorldBootstrapHostedServices`, `SessionEntry`, `GetSubscriptions`).
- **Out of scope:** recorder, grader, scenario YAML, CLI run mode, bot policies, the MCP marshalling, and `AddPlaytestModule` (lands with its first DI consumer, the PT-2 run mode).
- **Exit criterion:** T10b, T11–T25, T32 and T33 green; build + test green.

### WP3 — Bot-critical typed messages, telnet parity, R27 policy

- **Scope:**
  - `NarrationMessage` + its telnet case;
  - the eight typed messages and `RefusalKind`, and converting every site listed in Systems / handlers (≈59 sites, audiences unchanged);
  - **R27:**
    - a new **"Agent autonomy"** subsection in [`design/balance.md`](../design/balance.md) with the amended rule (agents may launch playtest runs and the sweeps they need, and report; every tuning, content or code change a finding implies is decided by a human);
    - `balance-tuning` lines 14–16 rewritten to cite it;
    - a one-line "Superseded 2026-10 by playtest program R27 — see `design/balance.md#agent-autonomy`" note under the first Decisions bullet ("No `run-simulation` skill") of [`completed/balance-doc-layer.md`](../roadmap/completed/balance-doc-layer.md).
- **Files:**
  - `Core/Output/{NarrationMessage.cs, CommandRefusalMessage.cs, TelnetOutputFormatter.cs}`;
  - `Core/Modules/Combat/{CombatNarrationMessage.cs, Handlers/CombatHandler.cs, Commands/KillCommand.cs, Commands/FleeCommand.cs}`;
  - `Core/Modules/Death/{DeathNarrationMessage.cs, Handlers/DeathNarrationHandler.cs}`;
  - `Core/Modules/Progression/{ProgressionNarrationMessage.cs, Handlers/ProgressionNarrationHandler.cs}`;
  - `Core/Modules/Economy/{CurrencyAwardedMessage.cs, Handlers/CurrencyAwardNarrationHandler.cs}`;
  - `Core/Modules/Shopping/{ShopTransactionMessage.cs, ShopListingMessage.cs, Handlers/ShopInteractionHandler.cs, Commands/{Buy,Sell,List}Command.cs}`;
  - `Core/Modules/Items/{ItemNarrationMessage.cs, Handlers/{ItemInteraction,EquipmentInteraction}Handler.cs, Commands/{Wear,Get}Command.cs}`;
  - tests.
- **Docs:**
  - flow-06 (`NarrationMessage` case);
  - **flow edits:**
    - [flow-30](../architecture/flows/flow-30-shopping.md) step 3: buy/sell narration → `ShopTransactionMessage`; refusals → `CommandRefusalMessage`; listing → `ShopListingMessage`;
    - [flow-17](../architecture/flows/flow-17-kill-mob-combat-initiation.md) narration steps (initiation, round pulse, flee) → `CombatNarrationMessage`; `kill`/`flee` refusals → `CommandRefusalMessage`;
    - flows 09, 13, 20 and 31, wherever a step names the narration as a `PlainMessage`;
  - [`reference/handlers.md`](../reference/handlers.md): the rows for `CombatHandler`, `DeathNarrationHandler`, `ProgressionNarrationHandler`, `CurrencyAwardNarrationHandler`, `ShopInteractionHandler`, `ItemInteractionHandler` and `EquipmentInteractionHandler` name their typed message;
  - [`reference/commands.md`](../reference/commands.md): `kill`, `flee`, `buy`, `sell`, `list`, `wear` and `get` note typed refusals (`CommandRefusalMessage` + `RefusalKind`); `list` notes `ShopListingMessage`;
  - `features/output/output.md`: the ratchet rule (a fact a bot or structured client must branch on is a `NarrationMessage` subclass) and the `RefusalKind` evolution note (Design notes);
  - **skills (INV-20):**
    - `add-command`: step 6 and the "Rules" bullet at line 190 on `PlainMessage` (prefer a typed refusal or narration where a client must branch);
    - `add-handler`: the `NarrationMessage` ratchet rule for narration handlers;
  - `design/balance.md` rule 5 row for the autonomy rule;
  - **backlog entries (firm deliverables):**
    - "Output structuring ratchet: the remaining ~165 `PlainMessage` sites convert to `NarrationMessage` subclasses on touch (dispatcher refusals, ability-strike prose, arrival/departure, ascension, admin/builder output)."
    - "Fix `CombatHandler` round-narration audiences: the mob-miss/hit and player-miss lines reach bystanders in the second person ('…misses you', 'You miss…'). Split self/others like the other combat lines. Must land **before PT-3's multi-bot sweeps and PT-4**. Until then, PT-2's walker keys misses and hits on `actorName`/`targetName`, never on `perspective`."
- **Depends on:** WP2, for the agent-formatter assertions in T28. The telnet parity tests (T26) and handler tests (T27) can land before WP2.
- **Out of scope:** dispatcher refusals, ability-strike prose, `consider` (a Phase 5 slice; it should emit a `NarrationMessage` subclass when it lands), and fixing the combat-audience quirk (decided: backlog entry above). Do not touch `WearCommand`'s `SaveEntityAsync` (pre-existing INV-22 debt; see the persistence audit).
- **Exit criterion:** T26–T28 green; build + test green; a manual telnet smoke of kill/buy/sell/wear/get/death shows unchanged text.

**Primary agent:** once all three packages land, run `architecture-reviewer` (code mode) across the combined diff.

## Content tooling impact

*(INV-18.)*

PT-1 adds **no authorable gameplay state**: no content-file shape, no admin command, no `TemplateRegistry` kind, no `[Persistent]` component.

The world is driven through a code API (`PlaytestWorldOptions` + the world API). Its outputs are inspectable as JSON observation logs.

The brief's INV-18 line ("scenario YAML, example scenarios and the `playtest` CLI ship with the substrate") is satisfied by **PT-2**, per the brief's own slice-decomposition row, which places scenario YAML, the store and the CLI there. PT-1 is the engine half of the substrate, and its tests drive worlds by command lines.

Test fixture content is generated by test code into temp directories. Nothing is committed under `content/`, and `content/dev-world/` is only **read** (via a scratch copy) by T25.

## Cross-cutting surfaces stressed

*(INV-19, INV-31, INV-17.)*

| Surface | Classification | Notes |
|---|---|---|
| **Event bus (subscriptions)** | **Gap exposed → fixed in-slice (WP1)** | 69 hand-wired `Subscribe` calls in one host's `Main` cannot be shared with a second composition. Disposition: extract to `SubscribeGameplayHandlers`, pinned by T1/T2/T18, with the skills repointed. `EventBus.GetSubscriptions()` is a read-only inspection seam, not a behavior change. |
| **Sessions (entry)** | **Gap exposed → fixed in-slice (WP1)** | The create/enter/leave tail was spread across `LoginFlow` and `TelnetSession`. `SessionEntry` is transport-neutral. Validators move to the domain system so both paths apply them. `LoginFlow` keeps only the stream wizard. |
| **Output** | **Gap exposed → framework fixed in-slice (WP2/WP3); remainder acknowledged debt** | No structured formatter existed, and bot-critical facts were prose. Fixed by the agent formatter (the first non-telnet formatter, per [client-tier](../design/client-tier.md)) plus the `NarrationMessage` base, which makes each future conversion a one-type change with telnet parity by construction. The ~165 remaining sites are an **on-touch ratchet** (brief resolved decision 1). Backlog entry "Output structuring ratchet" is a **firm WP3 deliverable**. |
| **Commands** | Adequate | Bots and the setup actor use `ICommandDispatcher` unchanged. Privilege comes from the existing `Admin:PrivilegedNames` allowlist. |
| **Persistence** | Adequate | A per-world in-memory DB through `PersistenceOptions.DatabasePath`, using the proven shared-cache pattern, with no production code change. The flush timer is excluded by composition, which is fine for an in-memory store. See the audit below. |
| **ECS / static state** | **Gap exposed → fixed in-slice (WP1)** | `EcsManager` was the only mutable process-static, and is deleted. The guard (T4) pins "no mutable statics". See [Design notes](#static--shared-state-across-worlds-settled). |
| **Content templates (determinism)** | **Gap exposed → fixed in-slice (WP1)** | The registry's enumeration order was hash-randomized per process, and the loader's file order is filesystem-dependent. Both break cross-process R3. Fixed by sorting both, with the ordering stated in the `ITemplateRegistry` contract (T5). |
| **Time** | **Gap exposed → fixed in-slice (WP2)** | No steppable production clock existed (`FakeClock` is test-only). `SteppedClock` sits beside `SeededRandom` in `Core/Systems`. The heartbeat `Timestamp` and every `IClock` reader share one stepped source. |
| **Randomness** | Adequate | `SeededRandom` exists. One per world, registered as the `IRandom` singleton. |
| **Configuration** | Adequate | A per-world in-memory overlay over the caller's base configuration. `Heartbeat:IntervalMs` sets the step size. Nothing new in `appsettings.json`. |
| **Modules / DI** | Adequate (refined seam) | Core cannot reference `Server`'s `CompositionRoot`. `IPlaytestEngineComposer`/`IPlaytestEngine` (Core) are implemented in `Server`, with only abstraction types crossing the seam, so Core gains no package reference. `AddPlaytestModule` is deferred to PT-2. The DI-smoke guard covers `ISessionEntry` and the formatter. |
| **Broadcast** | **Acknowledged debt (decided)** | Audiences are unchanged. Pre-existing: `CombatHandler`'s miss line, and the mob-hit "hits you" line, go to the whole room (or everyone but the attacker), so bystanders read "You miss…" or "…hits you". This is preserved byte-for-byte as `Perspective = Room`/`Others` and pinned by T27. A backlog entry (**firm WP3 deliverable**) schedules the fix before PT-3's multi-bot sweeps and PT-4. Until then, PT-2's walker keys misses on `actorName`, not `perspective`. |
| **Residual nondeterminism (a): builder-verb ids** | **Acknowledged debt** | Builder verbs (`dig`, `mkitem`, `mkmob`, `mkarea`) mint ad-hoc ids from `Guid.NewGuid()`. PT-1's R6 positioning verbs (`teleport`, `setplayer`, `setwallet`, `spawn`, `ascend`) mint none, and determinism tests T16/T17 use no builder verb. Only T14 runs `dig`, and it asserts file placement, not ids or logs. PT-4's defect overlays depend on them, so the backlog entry "seed ad-hoc id minting through the random seam before PT-4" is a **firm WP2 deliverable**. |
| **Residual nondeterminism (b) and (c): event `OccurredAt`/`EventId`, PBKDF2 salts** | Adequate (unobserved) | (b) Event payload `OccurredAt`/`EventId` stamp wall-clock time and random GUIDs, but no handler, formatter or observation reads them. (c) PBKDF2 password salts are random but only verified, never emitted. Neither reaches an observation. **Constraint on PT-2's recorder:** it stamps facts with the world tick and never reads `OccurredAt`/`EventId` or account credentials, so neither enters a report. |
| **Flows** | Adequate | One new flow (34). Touched: 01, 02, 06, 07, 16, 17, 30, 33, plus the narration-type wording in 09, 13, 20, 31. See below. |

### INV-31 posture

- **A playtest world is confined.** Every public member enters through a re-entrancy guard, and concurrent entry throws (T24).
  - The tick driver is the **single logical writer**. A step's work, including handler `await`s (`ConfigureAwait(false)` continuations may resume on pool threads), runs strictly sequentially, and no two operations ever overlap in one world.
  - "One thread per world" means one logical flow of control, not a pinned OS thread.
  - PT-4's MCP server marshals tool calls through the same guarded API.
- **Parallel worlds share no mutable state.** Each has its own container: `EventBus`, `EntityService`, `SessionManager`, `SessionBufferRegistry`, `TemplateRegistry`, `ComponentRepository`, `WorldConfiguration`. Each has its own in-memory DB (unique name) and scratch directory (unique path). `EcsManager` is gone, and the T4 guard keeps it so.
- **No new thread, timer or `BackgroundService`.** A world never resolves anything from a live host's provider and never touches the telnet server's world. Live-server bots stay out of scope.
- **`BotSession`'s inbox** is written only inside its world's flow (dispatch flush or tick flush), so it is confined with the world.

### Persistence opt-in audit (INV-14 / INV-22 / INV-23)

- **Level 1 (domains):**
  - Bot accounts and characters, and the setup actor, are **persistent entities** created by the unchanged `AccountSystem` (`PersistentEntity` attached there), written to the world's in-memory DB.
  - World content is fresh-spawned from the scratch YAML and never carries `PersistentEntity`. Pickup/drop transitions go through the unchanged `ItemContextHandler`.
- **Level 2 (components):** no component is added or reshaped. `PlayerComponent` (holds the `BotSession`) stays transient. Observations, queues and the clock are harness state, never components.
- **Level 3 (save sites):**
  - The construction saves (character, then account) **move** from `LoginFlow` to `SessionEntry.CreateCharacterAsync`. This is INV-22 category 1 with the same calls and order, and the checklist wording is updated.
  - The session-end save stays in `PlayerSessionHandler` (category 3), reached through `LeaveWorldAsync`.
  - No new `SaveEntityAsync`.
  - **Acknowledged, untouched: `WearCommand`.** It calls `SaveEntityAsync` after an equip (~line 90), an ordinary player command outside INV-22's three categories. This is **pre-existing acknowledged debt** that PT-1 does not change: WP3 edits only its three refusal sites, and the bot `wear` path (T28) runs the save into the world's in-memory DB. It is tracked by the backlog entry ["Persistence save-on-change cleanup + manual `save`/`quit` commands"](../roadmap/backlog.md#-persistence-save-on-change-cleanup--manual-savequit-commands) (cleanup item 1, which also lists `RemoveCommand`; PT-1 does not touch that command). The call is **not** removed here.
  - **Acknowledged, untouched: `CharacterHydrationHandler`.** Its boot-time save for a character with an unresolvable room fits none of the three categories. It is already tracked in [`backlog.md`](../roadmap/backlog.md) (the `CharacterHydrationHandler` audit entry). It never fires in a playtest world, which boots on an empty DB. PT-1 does not modify the handler (slice 1 changes only its starting-room read).

## Flows introduced or modified

*(INV-17. Reference the flow; never reproduce its diagram here.)*

- **New: Flow 34 — Playtest world journey (compose · boot · bot entry · step · observe)**, `flow-34-playtest-world.md` (WP2). Trigger: a caller creates a world through `PlaytestWorldFactory`; in PT-1 that is tests only. The participants are Factory/Composer, World, Driver, `SessionEntry`, `IEventBus`, `BotSession` and `AgentOutputFormatter`, and the file follows Main flow steps 1–8. PT-2 extends it with recorder → grade → report and the `playtest` CLI trigger, retitling it the brief's "playtest run journey".
- **[Flow 1 — Server startup](../architecture/flows/flow-01-server-startup.md)** (WP1): the subscription step now calls `CompositionRoot.SubscribeGameplayHandlers`. The bootstraps are composed via `AddWorldBootstrapHostedServices` inside `AddGameplayHostedServices`. Any `EcsManager.SetWorld` mention is removed.
- **[Flow 2 — Player connection](../architecture/flows/flow-02-player-connection.md)** (WP1): steps 3 and 6 are bind → `SessionEntry.EnterWorldAsync` / `LeaveWorldAsync`. The flow notes that `BotSession` uses the same path.
- **[Flow 7 — Login journey](../architecture/flows/flow-07-login-character-flow.md)** (WP1): the step-5 creation tail is `SessionEntry.CreateCharacterAsync`; the step-2/5 validation rules live on `IAccountSystem`.
- **[Flow 6 — Output journey](../architecture/flows/flow-06-output-rendering.md)** (WP2/WP3): adds the `playtest` transport → `AgentOutputFormatter` leg, the `NarrationMessage` telnet case, and the bare separator line that `BotSession` drops.
- **[Flow 16 — Heartbeat tick](../architecture/flows/flow-16-heartbeat-tick.md)** (WP2): the trigger gains the second publisher, `PlaytestTickDriver`, with stepped `Elapsed` = interval, `Timestamp` from `SteppedClock`, and the same catch-and-log-per-tick policy. Handler order is unchanged.
- **[Flow 33 — Simulation run](../architecture/flows/flow-33-simulation-run.md)** (WP1): lines 7 and 152 drop `EcsManager`. The invariant becomes "no process-global world; the sandbox `EntityService` is per run".
- **[Flow 30 — Shopping](../architecture/flows/flow-30-shopping.md)** (WP3): step 3's narration is `ShopTransactionMessage`, its refusals are `CommandRefusalMessage`, and `list` renders `ShopListingMessage`.
- **[Flow 17 — Combat journey](../architecture/flows/flow-17-kill-mob-combat-initiation.md)** (WP3): the initiation, round-pulse and flee narration steps emit `CombatNarrationMessage`; `kill`/`flee` refusals emit `CommandRefusalMessage`.
- **Flows 9, 13, 20, 31** (WP3): narration steps name the typed message where they currently say `PlainMessage`. There is no behavior change.

## Test plan / Verification

*(INV-25; rubric in [07-testing.md](../architecture/07-testing.md).)*

| # | Tier | Target | Asserts | Post. |
|---|---|---|---|---|
| T1 | guard (source scan) | `Program_main_has_no_bus_subscriptions` | `Server/Program.cs` contains no `.Subscribe<` | 1 |
| T2 | guard (DI + bus) | `SubscribeGameplayHandlers_subscribes_every_registered_handler` | build `Register` + `SubscribeGameplayHandlers`; every DI-registered concrete type implementing `IEventHandler<T>` has a subscription for each `T` (allowlist, expected empty). The pair set is **derived from DI**, never a hard-coded count, so slice 1's added pair (69 → 70) needs no test edit | 1 |
| T3 | system-unit | `EventBusTests.GetSubscriptions_*` | returns `(event, handler, priority)` in dispatch (priority) order; reflects Unsubscribe | 1, 2 |
| T4 | guard (reflection) | `No_mutable_static_state_in_engine_assemblies` | Scans static fields in `Core` and `Server`. **Exclusions:** only compiler-generated *closure/lambda caches* (fields of `[CompilerGenerated]` nested display classes such as `<>c`) and the compiler-emitted `<PrivateImplementationDetails>` type's fields (constant-data blobs). Auto-property backing fields (`<X>k__BackingField`) **are** checked, because a static auto-property with a setter is mutable state. **Assertions:** (a) no non-`const`, non-`readonly` static field; (b) every `static readonly` field whose type is not a known-immutable type (primitives, `string`, `TimeSpan`, `Regex`, `JsonSerializerOptions`, enums) is on the reviewed allowlist, each entry with a rationale. **Allowlist, never-written tables:** `EntityStateService._rules/_autoExits`, `PowerScaling._formulas`, `PreferenceRegistry.All/ById`, `ProgressionConstants.Rules/CombatTracks`, `AscensionConstants.TrackedScores/UnlocksForTier`, `TemplateConformanceSystem.MobKnobs`, `PowerCommand.AllScores`, `ConfigCommand.On/OffTokens`, `AreaLayoutSystem.DirectionOrder`, `ContentReferenceIndex.DeclaredEdges`, `ContentValidator.ReservedWindowsNames`, `ContentDefinitionCatalog.EmptyRoomAreaMap`. **Allowlist, read-only static records that wrap collections:** `AspectComposition.Empty` (its dictionary is exposed only as `IReadOnlyDictionary`, has no mutator, and is shared as the empty value), `TemplateConformanceSystem.EmptySnapshot` (same, an empty `PowerSnapshot`), `BalanceStandardsDefaults.Document`/`.Outcomes` (positional records over `IReadOnlyList`, replaced wholesale by the store, never mutated in place), `PowerBudgetTunables.Default`, `ParsedArguments.Empty`, `CommandArgumentSchema.Empty`, `ProgressionSystem.NoRows` (an empty array exposed as `IReadOnlyList`), `BalanceStandardsRegistry.EmptyReferenceBuild` (an immutable empty record). **Allowlist, backing fields of get-only static auto-properties** (readonly, holding immutable result values): `ValidationReport.Ok`, `Effect.cs` `Immune`/`StackingBlocked`, `SimAction.Melee`. **Merge-order note:** slice 1's `WorldManifest.Empty`, and any static default `Unresolved` starting-room snapshot it adds, join this allowlist in whichever slice merges second (`dev-world-foundation.md` says the same). Adding a new entry requires the same one-line rationale | 3 |
| T5 | system-unit | `TemplateRegistryTests.AllBlueprintIds_is_ordinal_sorted` | registering in shuffled order yields ordinal-ignore-case order | 4 |
| T6 | system-unit | `AccountSystemTests` (extend) | the three validators: boundary lengths, character classes, the uniqueness check for character names; texts as before | 5 |
| T7 | handler (initiator) | `SessionEntryTests.CreateCharacterAsync_*` | with a recording persistence fake + `RecordingEventBus`: save order character → account; `AccountCreatedEvent` only when a username is given, and before `CharacterCreatedEvent`; returns the new id | 6 |
| T8 | handler (initiator) | `SessionEntryTests.Enter/Leave_*` | the `PlayerConnectedEvent` handler sees `ISessionManager.GetSession(id)` non-null (register-before-publish); buffer flushed; leave publishes `PlayerDisconnectedEvent` and then the session is unregistered | 6 |
| T9 | guard (existing, extended) | `HostCompositionTests` | `AddWorldBootstrapHostedServices` = the 3 bootstraps in order; the gameplay set is still 6; the content-bootstrap set is still 2 | 1 |
| T10a | guard (existing) | DI-smoke (WP1) | resolves `ISessionEntry` | — |
| T10b | guard (existing, extended) | DI-smoke (WP2) | resolves the formatter registry with both formatters (telnet first, agent second); pairs with T22 | 14 |
| T11 | system-unit | `SteppedClockTests` | fixed epoch; `Advance` accumulates; never reads the wall clock | 10 |
| T12 | flow (playtest world, temp fixture; state via the capturing composer) | `PlaytestWorldEntryTests` | the bot lands at `StartingRoomEntityId` with `PlayerComponent` + `BotSession` transport `playtest`; first drained observations contain `type:"roomDescription"` (no `roomEntityId`) and `type:"prompt"`; two bots in one room each see the other's arrival | 9, 14 |
| T13 | flow (capturing composer for clock/spawn state) | `PlaytestTickDriverTests` | a queued line dispatches before the tick of the same step; `TickId` increments by 1 per step; clock delta per step = `Heartbeat:IntervalMs`; at most N lines per bot per step; a killed mob respawns after exactly ⌈delay ÷ interval⌉ steps | 10 |
| T14 | flow | `PlaytestWorldScratchTests` | a setup `dig` writes a room YAML under scratch; the source fixture directory hash is unchanged; dispose deletes scratch; `KeepScratch` retains it | 7 |
| T15 | flow (capturing composer for config) | `PlaytestWorldSetupTests` | `RunSetupAsync(["setwallet <bot> coin 50"])` → the bot's `score` observation shows the wallet; a bot's own `setwallet` is refused (unauthorized); `AddBotAsync` with a name in the privileged set throws `InvalidOperationException` and creates no entity | 11 |
| T16 | flow (determinism) | `PlaytestDeterminismTests.Same_seed_same_script_same_log` / `Different_seed_differs` | per-bot `(Tick, Seq, Json)` logs equal across two worlds; a different seed differs over the combat script | 12 |
| T17 | flow (isolation) | `PlaytestDeterminismTests.Concurrent_worlds_match_sequential` | two same-seed worlds stepped via `Task.WhenAll` produce logs equal to each other and to a sequential run | 12 |
| T18 | guard | `Playtest_world_subscriptions_equal_live_composition` | `GetSubscriptions()` of a world's bus = that of a `Register` + `SubscribeGameplayHandlers` container (PT-2 relaxes this to "+ playtest-namespace observers at `Ai`") | 2 |
| T19 | persistence (capturing composer for options + `IPersistenceSystem`) | `PlaytestWorldPersistenceTests` | the world's `PersistenceOptions.DatabasePath` is a unique `mode=memory` URI (two worlds differ); after bot entry the in-memory DB holds the character and account rows; no file exists at the base config's `DatabasePath` | 8 |
| T20 | system-unit | `AgentOutputFormatterTests` | `PlainMessage` shape; typed record → camelCase props + string enums + `type` discriminator; `[JsonIgnore]` honored; a message with an unserializable member → `{type, category, error}` and no throw | 14 |
| T21 | guard (reflection) | `Every_output_message_type_serializes_for_agents` | every concrete `IOutputMessage` in `Core` (uninitialized instance) formats without throwing; discriminators are unique; no emitted property ends in `EntityId` | 14 |
| T22 | system-unit | `OutputFormatterRegistryTests` | the composition-root formatter order resolves an unknown key to telnet and `"playtest"` to the agent formatter | 14 |
| T23 | system-unit | `BotSessionTests` | blank/whitespace lines dropped; tick/seq stamping; `SessionId` deterministic per index | 9, 12 |
| T24 | flow | `PlaytestWorldConfinementTests` | on one world, each of these throws `InvalidOperationException`: overlapping `StepAsync` calls, and `Enqueue` issued while a `StepAsync` is in flight (the step is held open by a gate: through the capturing composer, the test subscribes a test-only `HeartbeatTickEvent` handler that awaits a `TaskCompletionSource`. This world is never used for T18's equality check) | 13 |
| T25 | flow (fixture smoke) | `PlaytestDevWorldSmokeTests` | boots the repo's `content/dev-world/` (located by walking up to `Hedron.sln`) through a scratch copy; the bot's location equals `WorldConfiguration.StartingRoomEntityId` (green before slice 1 via the void fallback, and after via the `world.yaml` manifest); the source tree is unchanged | 7, 9 |
| T26 | system-unit | `TelnetNarrationParityTests` | for each of the 8 new message types, sample instances: `Telnet.Format(typed) == Telnet.Format(new PlainMessage(typed.Text, typed.Severity, typed.Category))`, color on and off | 15 |
| T27 | handler (characterization) | **New, on-touch ratchet (none exist today):** `CombatHandlerTests`, `DeathNarrationHandlerTests`, `ShopInteractionHandlerTests`, `ItemInteractionHandlerTests`, `EquipmentInteractionHandlerTests`, `CurrencyAwardNarrationHandlerTests`. **Extended:** `ProgressionNarrationHandlerTests` | Each event → the typed message with the right `Kind`/`Perspective`/facts, to the same audience as before (self vs. others; the `Room` quirk pinned as-is). **Plus `Text` characterization for every converted handler site:** all 11 `CombatHandler` sites, the 7 death sites, the 4 progression texts, currency award, 4 shop sites and 8 item/equipment sites. The expected strings are copied from the pre-conversion code, so Postcondition 15's parity claim is pinned end to end (T26 covers rendering, T27 covers text). This is a deliberate, scoped exception to "no prose assertions" | 15 |
| T28 | flow + table-driven characterization | `CommandRefusalCharacterizationTests` (table-driven) + `PlaytestNarrationObservationTests` | **(a) Table-driven over all 24 `CommandRefusalMessage` sites** (`KillCommand` 5, `FleeCommand` 1, `BuyCommand` 5, `SellCommand` 5, `ListCommand` 2, `WearCommand` 3, `GetCommand` 3): each row sets up the refusal condition, then asserts `Verb`, `RefusalKind`, severity and the pre-conversion `Text`. **(b) Playtest world:** a bot `kill`s the fixture mob over steps and observes `combatNarration` `engaged`/`slain` (`perspective:"self"`), plus `currencyAwarded` and `progressionNarration` `xpAwarded`; `list` → `shopListing` rows; `buy` → `shopTransaction` `bought`; `get`/`wear` → `itemNarration` (the `wear` path runs `WearCommand`'s pre-existing save into the in-memory DB) | 15 |
| T30 | guard (existing, retargeted) | `ArchitectureGuardTests.Simulation_module_does_not_reference_EventBus` (renamed from `…_or_EcsManager`, lines 743–785) | the simulation module still references no `IEventBus`; the `EcsManager` clause is removed (the type no longer exists; T4 covers successors) | 3 |
| T31 | flow (temp content dir) | `WorldContentLoaderTests.Loads_files_in_ordinal_order` | with a recording template-registry fake, files written in non-ordinal creation order register in ordinal path order | 4 |
| T32 | flow (playtest world) | `PlaytestWorldEntryTests.Invalid_bot_name_is_a_harness_error` | `AddBotAsync("B0t")` / `AddBotAsync("x")` throw `ArgumentException` carrying the validator text, and no account or character entity is created (checked through the capturing composer) | 5 |
| T33 | system-unit | `PlaytestTickDriverTests.Tick_publish_fault_is_logged_and_step_completes` | the driver is built over a bus fake that throws on `HeartbeatTickEvent`; the step completes, `TickFaults` is 1, the next step still advances `TickId`/clock, and observations from the dispatch phase are drained | 10 |

**Test-only inspection seam.** R4 forbids world state on the world API, and Core has no `InternalsVisibleTo`. Tests that assert internal state (T12, T13, T15, T19, T32) therefore build the factory over `CapturingEngineComposer`, a test-only decorator in `Hedron.Tests/Playtest/` that wraps the real `PlaytestEngineComposer` and keeps the returned `IPlaytestEngine.Services`. Production code never sees the provider; no production API is added for tests.

**Skipped (with reason):**
- New narration wording (presentation). Prose is asserted only as the scoped characterization of the converted sites (T27, T28a), which pins parity.
- `LoginFlow`'s prompts and wizard branching (transport plumbing, unchanged; its domain decisions are T6/T7).
- `TelnetSession`/`TelnetServer` (socket I/O).
- `PlaytestEngineComposer` and `PlaytestWorldFactory` internals beyond what T12–T19 exercise (host plumbing).
- The R28 log scope (exact log strings). Its coverage of boot, `AddBotAsync`, setup, step and dispose is a code-review check.
- R27 policy prose.
- The thin records `Observation`/`BotId`/`BotStatus` and the typed-message records themselves (pure data).

**Testability notes:**
- Time comes from `SteppedClock` and chance from `SeededRandom`. The scratch directory and DB name are unique per world and unobserved.
- **Inspecting world state** needs the test-only capturing composer described above. It is a deliberate seam, not a gap: the alternative, a state accessor on the world API, would violate R4.
- **Cross-process determinism** (R3 across separate runs) cannot be observed inside one test process, because string hashing is randomized per process but constant within it. It is covered indirectly: T5 and T31 pin the two ordering fixes that would otherwise make runs diverge. A true two-process replay check belongs to PT-2's CLI golden.

## Design notes

*(Slice-level rationale. The seam reasoning lives in the program's [Design notes](playtest-automation-program.md#design-notes) and [Architecture brief](playtest-automation-program.md#architecture-brief) and is not repeated.)*

### Static / shared state across worlds (settled)

Every candidate was checked in code:

| State | Where | Disposition |
|---|---|---|
| `EcsManager._world` (mutable static, last-writer-wins) | `Core/ECS/EcsManager.cs`, set in `Register` | **Removed.** It has no production reader, so deleting it is free, and two containers in one process would otherwise race on it. T4 prevents a successor. |
| `EventBus` | DI singleton | Per container (each world builds its own provider). |
| `TemplateRegistry`, `ComponentTypeRegistry`, `SessionManager`, `SessionBufferRegistry`, `WorldConfiguration`, `EntityService` | DI singletons / instances | Per container. `WorldConfiguration` and `EntityService` are `new`ed **inside** `Register`, so each call produces fresh ones. |
| `ThreadLocal<IDeserializer>` in the four template deserializers | **instance** fields | Per container; there are no static deserializer caches. |
| `ComponentSerializer.Options`, `SimReportJson.Options` (`static readonly JsonSerializerOptions`) | static | Immutable once first used (STJ freezes options); thread-safe. |
| `TelnetOutputFormatter.MarkerPattern`, `ContentValidator.FilenameSafePattern` (static `Regex`) | static | Immutable; thread-safe. |
| Static lookup tables (`EntityStateService._rules/_autoExits`, `PowerScaling._formulas`, `PreferenceRegistry.All/ById`, `ProgressionConstants.Rules/CombatTracks`, `AscensionConstants.*`, `BalanceStandardsDefaults.*`, `PowerBudgetTunables.Default`, …) | static readonly | Initialized once and never written (verified by grep). Records are immutable. Pinned by T4's allowlist. |
| `AsyncLocal` / `[ThreadStatic]` | — | None in engine code. The R28 logging scope uses the logging framework's own `AsyncLocal`, which is per async flow and therefore per world. |
| SQLite shared-cache in-memory DB names, and the pool keyed by connection string | process-global, inside Microsoft.Data.Sqlite | Unique name per world, so no sharing (T19). |
| Content directory files | filesystem | A unique scratch directory per world (T14). |
| `SandboxWorldFactory` precedent | `Core/Modules/Simulation/Systems/` | It hand-news one `EntityService` and about 12 systems per run, sharing only immutable singletons. A content-booting world needs the **whole** graph: commands, dispatcher, handlers, loaders, persistence, output. Hand-newing that would be the drifting second copy INV-19 forbids, so a playtest world is a full per-world DI container built by the shared `Register`. |

### Composition seam

The world, factory and driver live in `Core/Modules/Playtest/` per the brief. Core cannot reference `Server`, where `CompositionRoot` lives. `IPlaytestEngineComposer` is therefore the inversion point: the Server implementation is the only code that calls `Register`, `AddWorldBootstrapHostedServices` and `SubscribeGameplayHandlers` for a world. Tests (which reference `Server`) and PT-2's run mode construct it.

**Only abstraction types cross the seam:**
- in: `IConfiguration?`, an overlay dictionary, `Action<IServiceCollection>`;
- out: `IPlaytestEngine` (`IServiceProvider` + `IAsyncDisposable`).

The Server builds the configuration (`ConfigurationBuilder` + `AddInMemoryCollection`) and owns the `ServiceProvider` and `IHostedService` lifecycle. Core therefore takes **no** new package reference (no Hosting, no concrete DI container, no Configuration builder).

Boot mirrors the generic host exactly:
1. subscribe;
2. **construct every hosted service** (`GetServices<IHostedService>().ToList()`);
3. `StartAsync` each in registration order.

Construction must finish before any start, because `RegistryValidationBootstrap`'s constructor forces the balance-standards load (flow-01 step 2). Boot order then has one definition (`AddWorldBootstrapHostedServices`). Subscriptions are made before the bootstraps, matching the live host, where `Main` subscribes before `RunAsync`.

### Session-entry boundary (settled)

**Moves to `SessionEntry`:**
- creation + construction saves + creation events;
- session registration + `PlayerConnectedEvent` + first flush;
- `PlayerDisconnectedEvent` + unregister.

**Moves to `IAccountSystem`:** the validation rules (pure decisions).

**Stays in `LoginFlow`:** the transport-bound wizard (banner, prompts, attempt limits, roster rendering, password confirmation).

**Stays in the transport:** session binding (`PlayerEntityId` is the transport's own state).

**Not part of entry at all:**
- `PlayerSessionHandler` (unchanged reaction to the events);
- `CharacterHydrationHandler` (a boot-time `WorldContentReadyEvent` reaction, not an entry step; slice 1 changes only its snapshot read);
- the starting-room and respawn defaults (`AccountSystem.CreateCharacterAsync` already seeds them from the resolved starting room).

A bot thus exercises exactly the creation → arrival code a telnet player does. A1 "lands in the hub starting room" is real. Only password authentication and stream prompts are not exercised.

### Tick and turn boundary

Per step, each bot gets at most N (default 1) command lines. Because the dispatcher flushes in its `finally`, a command's immediate response, plus a prompt, is observable before the tick. Tick-driven output (combat rounds, bleed-out, regeneration) arrives through `OutputFlushTickHandler` (85) in the same step.

Game time per step equals the configured heartbeat interval, and the clock advances by the same `Elapsed` before publishing. Spawn timers (`IClock`), effect, cooldown and restock accumulators (`Elapsed`), and regeneration (`TickId`) therefore all behave exactly as live.

### Observation shape

One JSON object per output message, plus a `(Tick, Seq)` stamp added by the world. Typed messages are fully structured. `PlainMessage` passes through as text plus category and severity (brief).

Entity ids are not player-visible and are excluded (`[JsonIgnore]`, guarded by T21), so perception matches what a telnet player sees (R4).

`NarrationMessage` keeps `Text` as the canonical prose, so telnet parity holds by construction and LLM personas (PT-4) still read the words. The structured facts are what scripted bots branch on.

### `RefusalKind`: a known shape smell, accepted for now

`RefusalKind` is a closed enum in `Core/Output/` (framework) that holds **domain words** from several modules: `NoShopkeeper`, `NotWearable`, `TargetProtected`. That is a layering smell: a new module adding a refusal must edit a framework enum.

It is accepted in PT-1 because:
- one closed vocabulary keeps the 24 converted sites and their table-driven test (T28a) uniform;
- PT-2's walker needs exactly these cases;
- with 12 values, a registry would be premature.

**Planned evolution (recorded in `output.md`):** when a module outside the current seven commands needs a new refusal, or the enum passes about 20 values, `Kind` becomes a namespaced string code (`"shopping.no-shopkeeper"`) owned by each module, with constants on the module. The `RefusalKind` names map 1:1 onto those codes, so the JSON wire form (camelCase names) stays stable. The trigger is stated in `output.md`, so the ratchet owner sees it.

### Bot-critical list (derived from Phase 5 A1–A8)

| Criterion | Messages a walker must branch on |
|---|---|
| A1 | `roomDescription`, `movement` and `help*` (already typed); dispatcher refusals (Error severity) |
| A2 | `combatNarration` (engaged / hit / missed / slain), `commandRefusal` (target not found, protected, already fighting), `currencyAwarded`, `progressionNarration` (xpAwarded) |
| A3 | `progressionNarration` (improved); `progressDisplay` (already typed) |
| A4 | `shopListing`, `shopTransaction` (bought), `commandRefusal` (declined, not stocked, no shopkeeper), `itemNarration` (worn), `inventoryList`/`equipmentDisplay` (already typed) |
| A5 | `itemNarration` (picked up), `shopTransaction` (sold), `commandRefusal` (not carrying) |
| A6 | `combatNarration` (incapacitated), `deathNarration` (incapacitated, bleeding, died, respawned), `prompt` (already typed; state label and pools) |
| A7 | `deathNarration` counts; `consider` is a later Phase 5 slice and must emit a typed message |
| A8 | measured by PT-2's recorder from events, not perception |

### Interaction with Phase 5 slice 1 (`dev-world-foundation`)

- **Before slice 1:** a world resolves its start through config (`World:StartingRoomBlueprintId`, settable via `StartingRoomFallbackId` in the overlay), then void, then any room. Dev-world's `room.void.yaml` resolves through the void fallback.
- **After slice 1:** `world.yaml` sits at the content root, so the scratch copy carries it. The manifest store is rooted at `WorldOptions.ContentDirectory` (now the scratch path), so each world resolves its own manifest independently, and the overlay fallback becomes only a fallback. Slice 1's resolver is pure and its snapshot lives on the per-container `WorldConfiguration`, so nothing is shared. T25 is written to pass in both states.
- `startroom here` (slice 1) run during setup writes the **scratch** `world.yaml`.
- **Merge interaction:** slice 1 adds one `AdminAuditHandler` subscription (`StartingRoomSetByAdminEvent`).
  - If **PT-1 merges first**, slice 1's subscription moves into `CompositionRoot.SubscribeGameplayHandlers` rather than `Program.cs`. T1 (no `Subscribe` in `Program.cs`) and T2 (every registered handler subscribed for each event it handles) enforce this.
  - If slice 1 merges first, WP1 moves the line with the rest.
  - Either way, T2 derives the pair set from DI, so the count change (69 → 70) needs no test edit.
- Slice 1's `RunModeConfiguration` and `appsettings.DevWorld.json` are not needed by PT-1. PT-2's CLI uses them to default the source directory to dev-world. The world always overrides `Persistence:DatabasePath`, so the dev-world profile's DB file is never touched.
- The INV-26 wall-clock doc text is owned by dev-world-foundation WP1.

### R28 identifiability

- Harness sessions carry `TransportKey = "playtest"`.
- Every log line a world emits is inside a `PlaytestWorld={Label} Seed={Seed}` scope.
  - The composer registers `PlaytestWorldOptions.LoggerFactory` as the world container's `ILoggerFactory`, replacing the one `AddLogging` would create. That factory is shared by the caller and every world it creates, and defaults to `NullLoggerFactory`.
  - The world opens its scopes on that same factory around boot (the composer call, so bootstrap and loader logs carry the scope), `AddBotAsync`, `RunSetupAsync`, each step, and dispose.
  - Logging scopes flow through `AsyncLocal`, so worlds stepped in parallel on one shared factory never mix scopes.
- PT-2's reports stamp `automated: true`.
- No session-listing command exists today (there is no `who`). Harness worlds have their own `SessionManager`, so a live listing can never include a bot.
- The account-level marker stays deferred (brief). Its backlog entry is the one optional item in Open question 2.

### Bots and the setup actor

- Bot names are letters only (2–16), e.g. `Bota`, `Botb`, and unique per world.
- Usernames are derived (`bot_<name>`), and the password is a fixed harness constant. PBKDF2 hashing costs tens of milliseconds per bot. That is acceptable at N ≤ dozens; PT-3 sweeps should note it.
- The setup actor enters and leaves like any player, so bots in the start room observe its arrival and departure. This is deterministic and real.
- Session ids are deterministic for every `BotSession`: setup is index 0 and bots are 1…N. `SessionBufferRegistry`'s `ConcurrentDictionary<Guid,…>` flush order therefore never depends on random GUIDs.

## Open questions

1. ~~**Combat audience quirk.**~~ **Decided (spec review, 2026-10-29):** the text is unchanged in PT-1 (`Perspective = Room`/`Others`, pinned by T27). The fix is a **firm WP3 backlog entry** that must land before PT-3's multi-bot sweeps and PT-4. Until then, PT-2's walker keys misses on `actorName`, not `perspective`.
2. **Backlog entries.**
   - **Decided, firm deliverables:**
     - (b) "Output structuring ratchet" (WP3 Docs);
     - (c) "Seed builder-verb ad-hoc id minting through the random seam before PT-4" (WP2 Docs);
     - the combat-audience fix (WP3 Docs).
   - **Still optional, your call:** (a) the brief's proposed "Account-level automation marker for live-server bots". It is added only on your confirmation.
3. **`AbilityStrikeHandler` narration.** **Default:** excluded (on-touch). Include it only if you want the walker to reason about `kick` damage from perception rather than from PT-2's recorder.

## Related

- [`playtest-automation-program.md`](playtest-automation-program.md): the program, PT-1 row; R2–R6, R27–R28; the brief.
- [`dev-world-foundation.md`](dev-world-foundation.md): Phase 5 slice 1 (the `world.yaml` manifest; the interaction above).
- [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md): the A1–A8 acceptance contract (R26–R29) the message list is derived from.
- Features: [`output/output.md`](../features/output/output.md) · [`accounts/accounts.md`](../features/accounts/accounts.md) · [`accounts/login-flow.md`](../features/accounts/login-flow.md) · [`simulation/simulation.md`](../features/simulation/simulation.md) (the `SandboxWorld` precedent)
- Design: [`balance.md`](../design/balance.md) (R27 home) · [`client-tier.md`](../design/client-tier.md) (structured-output direction)
- Architecture: [`07-testing.md`](../architecture/07-testing.md) · [`checklist.md`](../architecture/checklist.md) (INV-5, 10, 12, 17, 18, 19, 20, 22, 25, 26, 27, 31)
- Backlog: the `CharacterHydrationHandler` INV-22 audit entry; the world-state threading decision (INV-31).
