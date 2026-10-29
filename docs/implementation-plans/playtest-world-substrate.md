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
  - `AccountSystem` already takes an injected `IClock` for every timestamp, so the brief's "wall-clock debt" is **already fixed**. Only the docs are stale: `checklist.md` INV-26 *Code* clause and [`07-testing.md`](../architecture/07-testing.md) "Current state". `SpawnSystem`, `ShopSystem` and `ShopExpiryTickHandler` also read `IClock`.
  - `TemplateRegistry.AllBlueprintIds()` enumerates a `ConcurrentDictionary<string,…>`, whose bucket order depends on per-process randomized string hashing. Boot spawn order, and therefore entity ids and target-resolution order, can differ between two processes.
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
4. ◆ `TemplateRegistry.AllBlueprintIds()` returns ids in ordinal (case-insensitive) order, whatever the registration order.
5. ◆ `IAccountSystem` owns the username, password and character-name validation rules. `LoginFlow` and the bot path both apply them, with error texts unchanged.
6. ◆ `SessionEntry.CreateCharacterAsync` creates the character, saves the character and then the account (INV-22 category 1, order preserved), publishes `AccountCreatedEvent` only when a new-account username is given, then `CharacterCreatedEvent`. `EnterWorldAsync` registers the session **before** publishing `PlayerConnectedEvent`, then flushes. `LeaveWorldAsync` publishes `PlayerDisconnectedEvent` and then unregisters. `TelnetSession`/`LoginFlow` use exactly these calls.
7. ◆ A playtest world boots from a **scratch copy** of its source content directory. The source directory is byte-identical after any run, including one that ran builder verbs. The scratch directory is deleted on dispose unless `KeepScratch` is set.
8. ◆ A playtest world persists to its own uniquely named in-memory SQLite database. No file is created at the base configuration's `Persistence:DatabasePath`.
9. ◆ A bot enters through `SessionEntry`: a fresh account and character, bound to a `BotSession` (`TransportKey == "playtest"`, deterministic `SessionId`). It lands at `WorldConfiguration.StartingRoomEntityId`, carries `PlayerComponent`, and its first observations include the starting room's `roomDescription` and a `prompt`.
10. ◆ One `StepAsync()` does the following in order:
    - dispatches at most `CommandsPerBotPerStep` queued lines per bot, in bot-creation order;
    - advances the `SteppedClock` by exactly `Heartbeat:IntervalMs`;
    - publishes `HeartbeatTickEvent(TickId = previous + 1, Timestamp = clock now, Elapsed = interval)`;
    - stamps every line delivered during the step with that step's tick.

    Spawn respawn delays elapse in stepped game time.
11. ◆ Setup (R6) runs through a privileged **setup session**, whose name is added to the world's `Admin:PrivilegedNames` overlay, dispatching ordinary admin verbs. A bot is never privileged: a bot name that matches a privileged name fails world creation.
12. ◆ **Determinism (R3, within the substrate):**
    - two worlds with the same seed, fixture and command script produce identical per-bot observation logs;
    - a different seed produces a different log over the chance-bearing fixture;
    - two same-seed worlds stepped **concurrently** produce logs identical to a sequential run.
13. ◆ Concurrent entry into one world's public API throws `InvalidOperationException`, because the world is confined to a single logical writer.
14. ◆ The agent formatter renders:
    - `PlainMessage` as `{type:"plain", category, severity, text}`;
    - any other typed message as its runtime type's public properties (camelCase, enums as names) with a unique `type` discriminator, minus `[JsonIgnore]` members.

    No emitted property name ends in `EntityId`. An unserializable message renders as `{type, category, error}` and is logged, never thrown. The registry's fallback is still telnet.
15. ◆ Each bot-critical narration site emits its typed `NarrationMessage` subclass to the same audience as before, with the facts listed in [Systems / handlers](#systems--handlers-involved). Telnet renders each one **byte-identical** to `PlainMessage(Text, Severity, Category)`, in color and without.
16. Every log line a world emits runs inside a scope that carries its label and seed (R28). The R27 rule lives in `design/balance.md` and is cited by `balance-tuning`. The 2026-07-17 record carries a "superseded by" pointer.

## Main flow

1. **Compose.** The caller passes `PlaytestWorldOptions` (seed, source content directory, base configuration, label) to `PlaytestWorldFactory.CreateAsync`. The factory:
   - copies the source directory to `<temp>/hedron-playtest/<worldId>/content`;
   - builds the overlay configuration: content dir → scratch; `Persistence:DatabasePath` → a unique shared-cache in-memory URI, held open by a keeper connection; balance standards and simulation paths → scratch; the setup actor appended to `Admin:PrivilegedNames`; an optional `World:StartingRoomBlueprintId` fallback;
   - calls `IPlaytestEngineComposer.ComposeAndBootAsync(config, overrides)`. The overrides register `IRandom` as `SeededRandom(seed)` and `IClock` as one `SteppedClock`.
2. **Boot** (`Server/Playtest/PlaytestEngineComposer`):
   - `AddLogging` → `AddSingleton<IConfiguration>` → `services.Register(config)` → apply the overrides → `AddWorldBootstrapHostedServices()` → build;
   - `provider.SubscribeGameplayHandlers()`;
   - `StartAsync` each bootstrap in registration order: `PersistenceBootstrap` (empty in-memory DB) → `WorldContentBootstrap` (`LoadAndSpawnAsync` + `WorldContentReadyEvent` fan-out, starting-room resolution) → `RegistryValidationBootstrap` (fail-fast).

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
   - (c) `IEventBus.PublishAsync(new HeartbeatTickEvent(++tick, now, interval))`. Existing priorities are unchanged; `OutputFlushTickHandler` (85) flushes every pending buffer;
   - (d) observations delivered during the step are moved to each bot's drainable log.
7. **Observe.** `Drain(botId)` returns and clears that bot's `Observation(Tick, Seq, Json)` list. `GetStatus(botId)` returns harness metadata only (tick, game time, queued count, in-world); it never reads world state (R4).
8. **Dispose.** Each bot calls `LeaveWorldAsync`. `PlayerSessionHandler` does the session-end save into the in-memory DB. Then each bootstrap runs `StopAsync`, the provider is disposed, the keeper connection is closed, and the scratch directory is deleted (unless kept).

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
| `TemplateRegistry.AllBlueprintIds` | core system | **changed** | Returns `OrderBy(id, StringComparer.OrdinalIgnoreCase)` |
| `IAccountSystem` / `AccountSystem` | domain system | **changed** | + `string? ValidateUsername(string)`, `string? ValidatePassword(string)`, `string? ValidateCharacterName(string)` (moved from `LoginFlow`, texts unchanged) |
| `ISessionEntry` / `SessionEntry` (`Core/Modules/Session/`) | Initiator-tier helper (publishes; not under `Systems/`) | **new** | `Task<uint> CreateCharacterAsync(uint accountId, string name, string? newAccountUsername, CancellationToken ct = default)`; `Task EnterWorldAsync(ISession session, uint accountId, string name)` (precondition: `session.PlayerEntityId` bound); `Task LeaveWorldAsync(ISession session, string name)`. Registered in `CompositionRoot` beside `PlayerSessionHandler` |
| `LoginFlow`, `TelnetSession`, `TelnetServer` | Initiators (Server) | **changed** | Wizard and prompts unchanged. The creation tail calls `SessionEntry.CreateCharacterAsync`; bind → `EnterWorldAsync`; `finally` → `LeaveWorldAsync`. `LoginFlow` drops `IEventBus`/`IPersistenceSystem` |
| `SteppedClock : IClock` (`Core/Systems/`) | core seam impl | **new** | `SteppedClock(DateTime epochUtc)`, `UtcNow`, `Advance(TimeSpan)`; fixed default epoch `2026-01-01T00:00:00Z` |
| `IPlaytestEngineComposer` (`Core/Modules/Playtest/`) / `PlaytestEngineComposer` (`Server/Playtest/`) | composition seam | **new** | `Task<PlaytestEngine> ComposeAndBootAsync(IConfiguration worldConfig, Action<IServiceCollection> overrides, CancellationToken ct)`. `PlaytestEngine` = `(ServiceProvider Provider, IReadOnlyList<IHostedService> Bootstraps)` (`IAsyncDisposable`) |
| `PlaytestWorldOptions` | record | **new** | `int Seed`, `string ContentSourceDirectory`, `IConfiguration? BaseConfiguration`, `string? StartingRoomFallbackId`, `string Label`, `int CommandsPerBotPerStep = 1`, `bool KeepScratch`, `ILoggerFactory? LoggerFactory`, `string SetupCharacterName = "Setup"` |
| `PlaytestWorldFactory` | harness (Initiator tier) | **new** | `Task<PlaytestWorld> CreateAsync(PlaytestWorldOptions, CancellationToken)`. Owns the scratch copy, the overlay, the keeper connection and the composer call |
| `PlaytestWorld` | harness (Initiator tier) | **new** | The world API: `Task<BotId> AddBotAsync(string name)`; `void Enqueue(BotId, string line)`; `Task StepAsync(int steps = 1)`; `IReadOnlyList<Observation> Drain(BotId)`; `BotStatus GetStatus(BotId)`; `Task<IReadOnlyList<Observation>> RunSetupAsync(IReadOnlyList<string> lines)`; `long Tick`; `IAsyncDisposable`. Has a re-entrancy guard on every member. Exposes no `EntityService` and no system (R4) |
| `PlaytestTickDriver` | Initiator (publishes `HeartbeatTickEvent`) | **new** | `Task StepAsync()` per Main flow step 6. Kept out of any `*.Systems` namespace (the no-bus-in-systems guard) |
| `BotSession : ISession` | transport | **new** | `TransportKey = "playtest"`, `SupportsColor = false`, deterministic `SessionId` (from bot index), `Bind(uint)`, `SendLineAsync` appends non-blank lines stamped with the driver's current tick |
| `Observation`, `BotId`, `BotStatus` | records | **new** | `Observation(long Tick, int Seq, string Json)`; `BotStatus(BotId, string Name, long Tick, TimeSpan GameTime, int Queued, bool InWorld)` |
| `AgentOutputFormatter : IOutputFormatter` (`Core/Output/`) | output framework | **new** | `TransportKey = "playtest"`. Compact one-line JSON per message (see Postcondition 14). Registered in `Register` **after** `TelnetOutputFormatter`, so the fallback is unchanged |
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
  8. Add the guard tests (subscription completeness, no `Subscribe` in `Program.cs`, no mutable static state).
- **Files:**
  - `Server/{Program.cs, CompositionRoot.cs, Sessions/LoginFlow.cs, Sessions/TelnetSession.cs, Sessions/TelnetServer.cs}`;
  - `Core/Events/EventBus.cs`; `Core/ECS/EcsManager.cs` (delete); `Core/Systems/TemplateRegistry.cs`;
  - `Core/Modules/Account/Systems/{IAccountSystem.cs, AccountSystem.cs}`;
  - `Core/Modules/Session/{ISessionEntry.cs, SessionEntry.cs}`;
  - tests under `Hedron.Tests/{Architecture, Composition, Events, Account, Session, Systems}/`.
- **Docs:**
  - flows 01, 02, 07;
  - [`01-layers.md`](../architecture/01-layers.md) §Registration (already drifted: it says modules subscribe in their extension); [`08-blazor.md`](../architecture/08-blazor.md) line 36; [`02-ecs.md`](../architecture/02-ecs.md), [`00-overview.md`](../architecture/00-overview.md), [`04-pitfalls.md`](../architecture/04-pitfalls.md) (`EcsManager` shorthand → injected `EntityService`, many worlds per process);
  - `checklist.md`: the INV-22 category-1 site name `LoginFlow` → `SessionEntry`;
  - the INV-26 *Code* clause and [`07-testing.md`](../architecture/07-testing.md) "Current state": drop the stale `AccountSystem`/`SpawnSystem` debt. **Skip this if slice 1 already corrected it.**
  - `features/accounts/{accounts.md, login-flow.md}`, `reference/systems.md` (`IAccountSystem`, `SessionEntry`, `TemplateRegistry` ordering, `EventBus` inspection);
  - each living `features/` doc that names `Program.cs` as the subscription home (grep: 13 files under `architecture/`, `features/`, `reference/`);
  - skills `add-handler` (step 2 + INV-22 wording), `add-event` (step 5), `add-command` (step 8 + the category-1 site), `edit-progression-system` (step 5).
- **Depends on:** nothing.
- **Out of scope:** anything under `Core/Modules/Playtest/`, the agent formatter, and message typing. Do not touch `CharacterHydrationHandler` (slice 1 changes its read).
- **Exit criterion:** T1–T10 green; `dotnet build` + `dotnet test` green; a manual telnet smoke (register, create, enter, `quit`) behaves exactly as before.

### WP2 — Playtest world: composition, tick driver, `BotSession`, agent formatter

- **Scope:**
  - `SteppedClock`;
  - the `Core/Modules/Playtest/` types (`PlaytestWorldOptions`, `PlaytestWorldFactory`, `PlaytestWorld`, `PlaytestTickDriver`, `BotSession`, `Observation`/`BotId`/`BotStatus`, `IPlaytestEngineComposer`/`PlaytestEngine`);
  - `Server/Playtest/PlaytestEngineComposer.cs`;
  - `AgentOutputFormatter` + its registration, and `[JsonIgnore]` on `RoomDescriptionMessage.RoomEntityId`;
  - the R28 per-step logging scope (`PlaytestWorld={Label} Seed={Seed}`);
  - the test fixture helper `Hedron.Tests/Playtest/PlaytestFixtureContent.cs`, which writes a minimal generic world to a temp dir: two linked rooms, a hostile mob with a spawn rule and a short respawn delay, a shopkeeper with one stock item and a found item. It mirrors the existing temp-content flow-test fixtures.
- **Files:** as listed above, plus `Core/Systems/SteppedClock.cs`, `Core/Output/AgentOutputFormatter.cs`, `Core/Output/RoomDescriptionMessage.cs`, `Server/CompositionRoot.cs` (formatter registration), and tests under `Hedron.Tests/Playtest/`, `Hedron.Tests/Output/`, `Hedron.Tests/Architecture/`.
- **Docs:**
  - the new [flow-34](#flows-introduced-or-modified) file + index row; flow-16 (second tick publisher, stepped `Elapsed`); flow-06 (`playtest` transport leg, blank-line drop);
  - [`features/output/output.md`](../features/output/output.md) (the agent formatter);
  - `reference/systems.md` (`SteppedClock`, playtest harness rows);
  - [`07-testing.md`](../architecture/07-testing.md): the harness table gains "playtest world (Tier 3 over real content)", and the parallelism posture.
- **Depends on:** WP1 (`SubscribeGameplayHandlers`, `AddWorldBootstrapHostedServices`, `SessionEntry`, `GetSubscriptions`).
- **Out of scope:** recorder, grader, scenario YAML, CLI run mode, bot policies, the MCP marshalling, and `AddPlaytestModule` (lands with its first DI consumer, the PT-2 run mode).
- **Exit criterion:** T11–T25 green; build + test green.

### WP3 — Bot-critical typed messages, telnet parity, R27 policy

- **Scope:**
  - `NarrationMessage` + its telnet case;
  - the eight typed messages and `RefusalKind`, and converting every site listed in Systems / handlers (≈59 sites, audiences unchanged);
  - updating `ProgressionAwardFlowTests` line 268 to the typed message;
  - **R27:**
    - a new **"Agent autonomy"** subsection in [`design/balance.md`](../design/balance.md) with the amended rule (agents may launch playtest runs and the sweeps they need, and report; every tuning, content or code change a finding implies is decided by a human);
    - `balance-tuning` lines 14–16 rewritten to cite it;
    - a one-line "Superseded 2026-10 by playtest program R27 — see `design/balance.md#agent-autonomy`" note under decision 1 of [`completed/balance-doc-layer.md`](../roadmap/completed/balance-doc-layer.md).
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
  - flows 09, 13, 17, 20, 30, 31, wherever a step names the narration as a `PlainMessage`;
  - `features/output/output.md` (the ratchet rule: a fact a bot or structured client must branch on is a `NarrationMessage` subclass);
  - skill `add-command` step 6 (prefer a typed refusal/narration where a client must branch);
  - `design/balance.md` rule 5 row for the autonomy rule.
- **Depends on:** WP2, for the agent-formatter assertions in T28. The telnet parity tests (T26) and handler tests (T27) can land before WP2.
- **Out of scope:** dispatcher refusals, ability-strike prose, `consider` (a Phase 5 slice; it should emit a `NarrationMessage` subclass when it lands), and fixing the combat-audience quirk (Open question 1).
- **Exit criterion:** T26–T29 green; build + test green; a manual telnet smoke of kill/buy/sell/wear/get/death shows unchanged text.

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
| **Output** | **Gap exposed → framework fixed in-slice (WP2/WP3); remainder acknowledged debt** | No structured formatter existed, and bot-critical facts were prose. Fixed by the agent formatter (the first non-telnet formatter, per [client-tier](../design/client-tier.md)) plus the `NarrationMessage` base, which makes each future conversion a one-type change with telnet parity by construction. The ~165 remaining sites are an **on-touch ratchet** (brief resolved decision 1). Backlog: add "Output structuring ratchet: remaining `PlainMessage` sites convert to `NarrationMessage` subclasses on touch". |
| **Commands** | Adequate | Bots and the setup actor use `ICommandDispatcher` unchanged. Privilege comes from the existing `Admin:PrivilegedNames` allowlist. |
| **Persistence** | Adequate | A per-world in-memory DB through `PersistenceOptions.DatabasePath`, using the proven shared-cache pattern, with no production code change. The flush timer is excluded by composition, which is fine for an in-memory store. See the audit below. |
| **ECS / static state** | **Gap exposed → fixed in-slice (WP1)** | `EcsManager` was the only mutable process-static, and is deleted. The guard (T4) pins "no mutable statics". See [Design notes](#static--shared-state-across-worlds-settled). |
| **Content templates (determinism)** | **Gap exposed → fixed in-slice (WP1)** | The registry's enumeration order was hash-randomized per process, which breaks cross-process R3. Fixed by sorting (T5). |
| **Time** | **Gap exposed → fixed in-slice (WP2)** | No steppable production clock existed (`FakeClock` is test-only). `SteppedClock` sits beside `SeededRandom` in `Core/Systems`. The heartbeat `Timestamp` and every `IClock` reader share one stepped source. |
| **Randomness** | Adequate | `SeededRandom` exists. One per world, registered as the `IRandom` singleton. |
| **Configuration** | Adequate | A per-world in-memory overlay over the caller's base configuration. `Heartbeat:IntervalMs` sets the step size. Nothing new in `appsettings.json`. |
| **Modules / DI** | Adequate (refined seam) | Core cannot reference `Server`'s `CompositionRoot`. `IPlaytestEngineComposer` (Core) is implemented in `Server`. `AddPlaytestModule` is deferred to PT-2. The DI-smoke guard covers `ISessionEntry` and the formatter. |
| **Broadcast** | Adequate, with one noted quirk | Audiences are unchanged. Pre-existing: `CombatHandler`'s miss line, and the mob-hit "hits you" line, go to the whole room (or everyone but the attacker), so bystanders read "You miss…" or "…hits you". This is preserved byte-for-byte as `Perspective = Room`/`Others`. See Open question 1. |
| **Residual nondeterminism** | **Acknowledged debt** | (a) Builder verbs (`dig`, `mkitem`, `mkmob`, `mkarea`) mint ad-hoc ids from `Guid.NewGuid()`. They are unused by PT-1's setup verbs (`teleport`, `setplayer`, `setwallet`, `spawn`, `ascend`), but PT-4's defect overlays run them. Backlog: "seed ad-hoc blueprint-id minting through `IRandom` before PT-4". (b) Event payload `OccurredAt`/`EventId`, which nothing reads today; PT-2's recorder must stamp with the world tick and never read them. (c) PBKDF2 password salts: never observed or reported. |
| **Flows** | Adequate | One new flow and five touched; see below. |

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
  - **Acknowledged, untouched:** `CharacterHydrationHandler`'s boot-time save for a character with an unresolvable room fits none of the three categories. It is already tracked in [`backlog.md`](../roadmap/backlog.md) (the `CharacterHydrationHandler` audit entry). It never fires in a playtest world, which boots on an empty DB. PT-1 does not modify the handler (slice 1 changes only its starting-room read).

## Flows introduced or modified

*(INV-17. Reference the flow; never reproduce its diagram here.)*

- **New: Flow 34 — Playtest world journey (compose · boot · bot entry · step · observe)**, `flow-34-playtest-world.md` (WP2). Trigger: a caller creates a world through `PlaytestWorldFactory`; in PT-1 that is tests only. The participants are Factory/Composer, World, Driver, `SessionEntry`, `IEventBus`, `BotSession` and `AgentOutputFormatter`, and the file follows Main flow steps 1–8. PT-2 extends it with recorder → grade → report and the `playtest` CLI trigger, retitling it the brief's "playtest run journey".
- **[Flow 1 — Server startup](../architecture/flows/flow-01-server-startup.md)** (WP1): the subscription step now calls `CompositionRoot.SubscribeGameplayHandlers`. The bootstraps are composed via `AddWorldBootstrapHostedServices` inside `AddGameplayHostedServices`. Any `EcsManager.SetWorld` mention is removed.
- **[Flow 2 — Player connection](../architecture/flows/flow-02-player-connection.md)** (WP1): steps 3 and 6 are bind → `SessionEntry.EnterWorldAsync` / `LeaveWorldAsync`. The flow notes that `BotSession` uses the same path.
- **[Flow 7 — Login journey](../architecture/flows/flow-07-login-character-flow.md)** (WP1): the step-5 creation tail is `SessionEntry.CreateCharacterAsync`; the step-2/5 validation rules live on `IAccountSystem`.
- **[Flow 6 — Output journey](../architecture/flows/flow-06-output-rendering.md)** (WP2/WP3): adds the `playtest` transport → `AgentOutputFormatter` leg, the `NarrationMessage` telnet case, and the bare separator line that `BotSession` drops.
- **[Flow 16 — Heartbeat tick](../architecture/flows/flow-16-heartbeat-tick.md)** (WP2): the trigger gains the second publisher, `PlaytestTickDriver`, with stepped `Elapsed` = interval and `Timestamp` from `SteppedClock`. Handler order is unchanged.
- **Flows 9, 13, 17, 20, 30, 31** (WP3): narration steps name the typed message where they currently say `PlainMessage`. There is no behavior change.

## Test plan / Verification

*(INV-25; rubric in [07-testing.md](../architecture/07-testing.md).)*

| # | Tier | Target | Asserts | Post. |
|---|---|---|---|---|
| T1 | guard (source scan) | `Program_main_has_no_bus_subscriptions` | `Server/Program.cs` contains no `.Subscribe<` | 1 |
| T2 | guard (DI + bus) | `SubscribeGameplayHandlers_subscribes_every_registered_handler` | build `Register` + `SubscribeGameplayHandlers`; every DI-registered concrete type implementing `IEventHandler<T>` has a subscription for each `T` (allowlist, expected empty); 69 distinct pairs present at the time of writing | 1 |
| T3 | system-unit | `EventBusTests.GetSubscriptions_*` | returns `(event, handler, priority)` in dispatch (priority) order; reflects Unsubscribe | 1, 2 |
| T4 | guard (reflection) | `No_mutable_static_state_in_engine_assemblies` | no non-`const`, non-`readonly` static field (compiler-generated excluded) in `Core` or `Server`; `static readonly` mutable-collection/array fields ⊆ the reviewed allowlist (`EntityStateService._rules/_autoExits`, `PowerScaling._formulas`, `PreferenceRegistry.ById`, `ProgressionConstants.CombatTracks`, `AscensionConstants.TrackedScores`, `TemplateConformanceSystem.MobKnobs`, `PowerCommand.AllScores`, `ConfigCommand.On/OffTokens`, `AreaLayoutSystem.DirectionOrder`, `ContentReferenceIndex.DeclaredEdges`, `ContentValidator.ReservedWindowsNames`, `ContentDefinitionCatalog.EmptyRoomAreaMap`) | 3 |
| T5 | system-unit | `TemplateRegistryTests.AllBlueprintIds_is_ordinal_sorted` | registering in shuffled order yields ordinal-ignore-case order | 4 |
| T6 | system-unit | `AccountSystemTests` (extend) | the three validators: boundary lengths, character classes, the uniqueness check for character names; texts as before | 5 |
| T7 | handler (initiator) | `SessionEntryTests.CreateCharacterAsync_*` | with a recording persistence fake + `RecordingEventBus`: save order character → account; `AccountCreatedEvent` only when a username is given, and before `CharacterCreatedEvent`; returns the new id | 6 |
| T8 | handler (initiator) | `SessionEntryTests.Enter/Leave_*` | the `PlayerConnectedEvent` handler sees `ISessionManager.GetSession(id)` non-null (register-before-publish); buffer flushed; leave publishes `PlayerDisconnectedEvent` and then the session is unregistered | 6 |
| T9 | guard (existing, extended) | `HostCompositionTests` | `AddWorldBootstrapHostedServices` = the 3 bootstraps in order; the gameplay set is still 6; the content-bootstrap set is still 2 | 1 |
| T10 | guard (existing) | DI-smoke | resolves `ISessionEntry` and the formatter registry with both formatters | — |
| T11 | system-unit | `SteppedClockTests` | fixed epoch; `Advance` accumulates; never reads the wall clock | 10 |
| T12 | flow (playtest world, temp fixture) | `PlaytestWorldEntryTests` | the bot lands at `StartingRoomEntityId` with `PlayerComponent` + `BotSession` transport `playtest`; first drained observations contain `type:"roomDescription"` (no `roomEntityId`) and `type:"prompt"`; two bots in one room each see the other's arrival | 9, 14 |
| T13 | flow | `PlaytestTickDriverTests` | a queued line dispatches before the tick of the same step; `TickId` increments by 1 per step; clock delta per step = `Heartbeat:IntervalMs`; at most N lines per bot per step; a killed mob respawns after exactly ⌈delay ÷ interval⌉ steps | 10 |
| T14 | flow | `PlaytestWorldScratchTests` | a setup `dig` writes a room YAML under scratch; the source fixture directory hash is unchanged; dispose deletes scratch; `KeepScratch` retains it | 7 |
| T15 | flow | `PlaytestWorldSetupTests` | `RunSetupAsync(["setwallet <bot> coin 50"])` → the bot's `score` observation shows the wallet; a bot's own `setwallet` is refused (unauthorized); a bot named like a privileged name → `CreateAsync`/`AddBotAsync` throws | 11 |
| T16 | flow (determinism) | `PlaytestDeterminismTests.Same_seed_same_script_same_log` / `Different_seed_differs` | per-bot `(Tick, Seq, Json)` logs equal across two worlds; a different seed differs over the combat script | 12 |
| T17 | flow (isolation) | `PlaytestDeterminismTests.Concurrent_worlds_match_sequential` | two same-seed worlds stepped via `Task.WhenAll` produce logs equal to each other and to a sequential run | 12 |
| T18 | guard | `Playtest_world_subscriptions_equal_live_composition` | `GetSubscriptions()` of a world's bus = that of a `Register` + `SubscribeGameplayHandlers` container (PT-2 relaxes this to "+ playtest-namespace observers at `Ai`") | 2 |
| T19 | persistence | `PlaytestWorldPersistenceTests` | the world's `PersistenceOptions.DatabasePath` is a unique `mode=memory` URI (two worlds differ); after bot entry the in-memory DB holds the character and account rows; no file exists at the base config's `DatabasePath` | 8 |
| T20 | system-unit | `AgentOutputFormatterTests` | `PlainMessage` shape; typed record → camelCase props + string enums + `type` discriminator; `[JsonIgnore]` honored; a message with an unserializable member → `{type, category, error}` and no throw | 14 |
| T21 | guard (reflection) | `Every_output_message_type_serializes_for_agents` | every concrete `IOutputMessage` in `Core` (uninitialized instance) formats without throwing; discriminators are unique; no emitted property ends in `EntityId` | 14 |
| T22 | system-unit | `OutputFormatterRegistryTests` | the composition-root formatter order resolves an unknown key to telnet and `"playtest"` to the agent formatter | 14 |
| T23 | system-unit | `BotSessionTests` | blank/whitespace lines dropped; tick/seq stamping; `SessionId` deterministic per index | 9, 12 |
| T24 | flow | `PlaytestWorldConfinementTests` | overlapping `StepAsync` calls on one world → `InvalidOperationException` | 13 |
| T25 | flow (fixture smoke) | `PlaytestDevWorldSmokeTests` | boots the repo's `content/dev-world/` (located by walking up to `Hedron.sln`) through a scratch copy; the bot's location equals `WorldConfiguration.StartingRoomEntityId` (green before slice 1 via the void fallback, and after via the `world.yaml` manifest); the source tree is unchanged | 7, 9 |
| T26 | system-unit | `TelnetNarrationParityTests` | for each of the 8 new message types, sample instances: `Telnet.Format(typed) == Telnet.Format(new PlainMessage(typed.Text, typed.Severity, typed.Category))`, color on and off | 15 |
| T27 | handler | `CombatHandlerTests`, `DeathNarrationHandlerTests`, `ShopInteractionHandlerTests`, `ItemInteractionHandlerTests`, `EquipmentInteractionHandlerTests`, `CurrencyAwardNarrationHandlerTests` (new, on-touch ratchet), `ProgressionNarrationHandlerTests` (extend) | each event → the typed message with the right `Kind`/`Perspective`/facts to the same audience as before (self vs. others; the `Room` quirk pinned as-is) | 15 |
| T28 | flow (playtest world) | `PlaytestNarrationObservationTests` | a bot `kill`s the fixture mob over steps → observes `combatNarration` `engaged`/`slain` (`perspective:"self"`) plus `currencyAwarded` and `progressionNarration` `xpAwarded`; `kill` with no mob → `commandRefusal` `targetNotFound`; `list` → `shopListing` rows; `buy` → `shopTransaction` `bought`, and with too little coin → `commandRefusal` `declined`; `get`/`wear` → `itemNarration` | 15 |
| T29 | flow (existing, updated) | `ProgressionAwardFlowTests` (line 268) | asserts the typed `ProgressionNarrationMessage.NewImprovementCount` instead of prose `Contains` | 15 |

**Skipped (with reason):**
- Exact narration prose (presentation; T26 pins parity, not wording).
- `LoginFlow`'s prompts and wizard branching (transport plumbing, unchanged; its domain decisions are T6/T7).
- `TelnetSession`/`TelnetServer` (socket I/O).
- `PlaytestEngineComposer` and `PlaytestWorldFactory` internals beyond what T12–T19 exercise (host plumbing).
- The R28 log scope (exact log strings).
- R27 policy prose.
- The thin records `Observation`/`BotId`/`BotStatus` and the typed-message records themselves (pure data).

**Testability gaps:** none. Time comes from `SteppedClock` and chance from `SeededRandom`. The scratch directory and DB name are unique per world and unobserved.

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

The bootstraps run by `StartAsync` in registration order, exactly what the generic host does, so boot order has one definition (`AddWorldBootstrapHostedServices`). Subscriptions are made before bootstraps, matching the live host, where `Main` subscribes before `RunAsync`.

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
- **Merge interaction:** slice 1 adds one `AdminAuditHandler` subscription (`StartingRoomSetByAdminEvent`) to `Program.cs`. Whichever slice merges second places that line in `SubscribeGameplayHandlers`; T1 fails if it is left in `Program.cs`.
- Slice 1's `RunModeConfiguration` and `appsettings.DevWorld.json` are not needed by PT-1. PT-2's CLI uses them to default the source directory to dev-world. The world always overrides `Persistence:DatabasePath`, so the dev-world profile's DB file is never touched.
- The stale INV-26 doc text is corrected by whichever slice lands first.

### R28 identifiability

- Harness sessions carry `TransportKey = "playtest"`.
- Every log line a world emits is inside a `PlaytestWorld={Label} Seed={Seed}` scope.
- PT-2's reports stamp `automated: true`.
- No session-listing command exists today (there is no `who`). Harness worlds have their own `SessionManager`, so a live listing can never include a bot.
- The account-level marker stays deferred (brief), pending the backlog entry in Open question 2.

### Bots and the setup actor

- Bot names are letters only (2–16), e.g. `Bota`, `Botb`, and unique per world.
- Usernames are derived (`bot_<name>`), and the password is a fixed harness constant. PBKDF2 hashing costs tens of milliseconds per bot. That is acceptable at N ≤ dozens; PT-3 sweeps should note it.
- The setup actor enters and leaves like any player, so bots in the start room observe its arrival and departure. This is deterministic and real.

## Open questions

1. **Combat audience quirk.** The mob-miss and mob-hit lines reach bystanders worded in the second person ("…misses you", "You miss…"). With N bots in a room (R5), a bystander bot misperceives. **Default:** preserve it byte-for-byte in PT-1 (`Perspective = Room`/`Others`, pinned by T27), and add a backlog entry: "Fix `CombatHandler` round-narration audiences (split self/others) — playtest R5 depends on it". **Alternative:** fix it in WP3, which changes bystander text, so the parity rule would be relaxed for those two sites. *(User decision.)*
2. **Backlog entries to add on your confirmation:**
   - (a) the brief's proposed "Account-level automation marker for live-server bots";
   - (b) "Output structuring ratchet: remaining `PlainMessage` sites → `NarrationMessage` on touch";
   - (c) "Seed ad-hoc blueprint-id minting through `IRandom` before PT-4 defect overlays".
3. **`AbilityStrikeHandler` narration.** **Default:** excluded (on-touch). Include it only if you want the walker to reason about `kick` damage from perception rather than from PT-2's recorder.

## Related

- [`playtest-automation-program.md`](playtest-automation-program.md): the program, PT-1 row; R2–R6, R27–R28; the brief.
- [`dev-world-foundation.md`](dev-world-foundation.md): Phase 5 slice 1 (the `world.yaml` manifest; the interaction above).
- [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md): the A1–A8 acceptance contract (R26–R29) the message list is derived from.
- Features: [`output/output.md`](../features/output/output.md) · [`accounts/accounts.md`](../features/accounts/accounts.md) · [`accounts/login-flow.md`](../features/accounts/login-flow.md) · [`simulation/simulation.md`](../features/simulation/simulation.md) (the `SandboxWorld` precedent)
- Design: [`balance.md`](../design/balance.md) (R27 home) · [`client-tier.md`](../design/client-tier.md) (structured-output direction)
- Architecture: [`07-testing.md`](../architecture/07-testing.md) · [`checklist.md`](../architecture/checklist.md) (INV-5, 10, 12, 17, 18, 19, 20, 22, 25, 26, 27, 31)
- Backlog: the `CharacterHydrationHandler` INV-22 audit entry; the world-state threading decision (INV-31).
