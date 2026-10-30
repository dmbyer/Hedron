# Playtest run mode (PT-2)

- **Status:** planned
- **Actors:** Designer (authors scenarios and playtest expectations, runs `playtest`, reads reports), System (the `playtest` run mode, the runner, the recorder, the grader, CI), Player (simulated: the acceptance-walker bot), Administrator (simulated setup actor, R6, inherited from PT-1)
- **Module:**
  - `Core/Modules/Playtest/`: `AddPlaytestModule` (first DI consumer, per PT-1), scenario model + store, recorder handler, metrics system, grader, report builder/writer, runner, `IBotPolicy` + the acceptance walker (`Policies/`);
  - `Core/Modules/BalanceInspection/Standards/`: the `playtestExpectations` standards family;
  - `Core/Commands/`: `CommandOutcome.Rejected` + `CommandContext.RefuseAsync` + the per-dispatch `CommandOutcomeSink` (gap fix, see resolved decision 2);
  - `Core/Systems/Definitions/`: the shared YAML definition store (**extract-or-consume**, WP1);
  - `Server/PlaytestRunMode.cs`; `Hedron.Web/Components/Pages/Standards.razor` (expectations section) + `Hedron.Web/Services/StandardsForm.cs` (the extracted document↔form mapping);
  - `content/dev-world/playtest/` (the committed smoke scenario, resolved decision 7); `Hedron.Tests/Playtest/` (the golden tier).

  Feature home on ship: `docs/features/playtest/` (created by PT-1; this slice adds `playtest-run.md`).
- **Program:** slice PT-2 of [`playtest-automation-program.md`](playtest-automation-program.md#slice-decomposition). Covers **R1, R7 (acceptance walker only), R10–R14, R25**. Consumes Phase 5 **R26–R29** ([`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md#behavioral-requirements)). Depends on [PT-1](playtest-world-substrate.md) and [Phase 5 slice 1](../roadmap/completed/dev-world-foundation.md).

## Description

PT-2 turns the PT-1 substrate into a designer tool and a CI gate. A designer writes a **playtest scenario** (YAML: world, seed, tick budget, setup lines, bots with their policy and plan, the A-criteria in scope, an optional standards file) and runs `dotnet run --project Server -- playtest --scenario <path> [--seed N]`. Scenarios for a committed world live beside it (`content/dev-world/playtest/*.yaml`, resolved decision 7); operator scenarios live under `Playtest:ScenarioDirectory` (`data/playtest/scenarios/`, gitignored) or anywhere `--scenario` can point. The run mode boots one playtest world (dev-world by default), adds the bots, and loops: drain each bot's observations → its **policy** decides the next command line → one tick step. A per-world **recorder** handler at `Ai` priority turns domain events into tick-stamped facts. After the run a pure **metrics system** derives the R10 metrics and the A1–A7 achievements, and a pure **grader** compares them against the new **`playtestExpectations`** family of the balance-standards document: a metric with a threshold gets pass/fail, a metric without one is reported descriptively. The run writes a JSON report plus a Markdown summary under `data/playtest/reports/` and exits. The first policy is the **acceptance walker**, a scripted executor of a scenario-declared phase plan (onboard, walk, hunt, collect, sell, buy, seek-death, wait). A seeded **smoke golden** walks dev-world's hub skeleton on every PR and asserts A1 passes, in **≤ 10 s wall time** (resolved decision 1). Scenario YAML rides the shared YAML definition store: this slice extracts it if Phase 5 `help-topics` hasn't yet (the "whoever is third extracts" rule).

## Preconditions

- PT-1 is merged. This slice extends PT-1 types, never re-implements them: `PlaytestWorld`, `PlaytestWorldFactory`, `PlaytestWorldOptions`, `PlaytestTickDriver`, `BotSession`, `Observation`, `IPlaytestEngineComposer`/`PlaytestEngineComposer`, `AgentOutputFormatter`, the typed `NarrationMessage` subclasses and `CommandRefusalMessage`/`RefusalKind`, `SteppedClock`, `CompositionRoot.SubscribeGameplayHandlers`.
- Phase 5 slice 1 is merged: `content/dev-world/world.yaml` declares `room.hub.square`; the hub skeleton (`room.hub.square` / `.north` / `.south`, two-way exits) exists; `RunModeConfiguration.Build` layers `appsettings.{env}.json` for run modes.
- Verified in code (2026-10-30):
  - `CommandDispatcher` publishes `CommandExecutedEvent` **after** `ExecuteAsync` returns (`CommandDispatcher.cs:219`), so effect events a command publishes precede it. Business refusals (target not found, can't afford, no exit) all report `CommandOutcome.Success` today: the outcome taxonomy is `Success, ParseFailed, Unauthorized, Threw, Refused` (the last = incapacitated gate only).
  - `ICommand.ExecuteAsync(CommandContext)` returns `Task`; no command reports a result.
  - `PlayerDiedEvent` is published only by `DeathTickHandler` (bleed-out) with `KillerEntityId: 0`. `PlayerIncapacitatedEvent` and `CombatStartedEvent` carry no opponent blueprint. `MobDiedEvent` carries `BlueprintId` and `KillerEntityId`.
  - `MobTemplate.Tier`/`.Band` are template fields, readable through `ITemplateRegistry.TryGet`.
  - Every event stamps `OccurredAt = DateTime.UtcNow` and `EventId = Guid.NewGuid()` (PT-1's recorder constraint applies).
  - `CommandContext` is a positional `sealed record (Session, InvokerEntityId, Args, Output, Services)`, constructed once by the dispatcher (`CommandDispatcher.cs:214`) and directly by ~17 command tests.
  - No test references `CommandDispatcher` today: its outcome branches (`ParseFailed` ×4, `Refused`, `Unauthorized`, `Success`, `Threw`) are unpinned.
  - `WorldContentLoader` (and the authoring `ContentDefinitionCatalog` / `ContentReferenceIndex`) read only the `areas/`, `rooms/`, `items/` and `mobs/` subdirectories of a content root, so a `playtest/` or `balance/` subdirectory beside them is never loaded as templates.
  - Hand-rolled YAML stores: `BalanceStandardsStore` (#1, single document), `SimScenarioStore` (#2, per-file directory), `WorldManifestStore` (#3, slice 1, accepted bounded debt). Template (de)serializers and content writers are a separate family and out of scope. The [backlog entry](../roadmap/backlog.md) schedules the extraction in Phase 5 slice 4 (`help-topics`, not yet planned).
  - `BalanceStandardsDocument` is a positional record. `Standards.razor` rebuilds the whole document in `BuildDocument()` (lines 449–469), so any field the page doesn't carry is **dropped on save**.
  - `data/balance/standards.yaml` is gitignored (`/data/*`), so CI always grades against the compiled defaults unless a scenario names a committed standards file (resolved decision 5). `content/` is committed, so world-local scenarios need no `.gitignore` change (resolved decision 7).
  - `SimulateRunMode` is the run-mode precedent: exit `0` clean run (whatever the verdicts), `1` engine failure, `2` usage or invalid scenario. `SimReportWriter` names files `<UTC timestamp>-<name>-<seed>.json`.
  - `Heartbeat:IntervalMs` defaults to 2000, so one step is 2 s of game time.
  - xUnit 2.9: `[Fact(Timeout)]` isn't reliable under parallel execution, so the golden's wall-time ceiling is asserted with a `Stopwatch` (Test plan G7).

## Postconditions

*(The coverage contract. ◆ = asserts internal state and maps to a named test in the Test plan.)*

1. ◆ **Shared YAML store.** The scenario store is built over the shared YAML definition store (`YamlDocumentFile<TDto>` / `YamlDefinitionDirectory<TDto>`). If PT-2 performs the extraction (WP1 branch B), `BalanceStandardsStore`, `SimScenarioStore` and `WorldManifestStore` are migrated onto it with their existing test suites unchanged and green, and no `new DeserializerBuilder()` / `new SerializerBuilder()` remains in those three stores.
2. ◆ **Scenario load is fail-fast and aggregated.** `IPlaytestScenarioStore.Load(path, seedOverride)` returns a `PlaytestScenario` or throws `InvalidOperationException` listing **every** structural error. Covered errors: unknown policy id (from the DI-collected `IBotPolicy` set), unknown criterion, unknown phase kind, unknown `expect` form, missing step `do`, duplicate or invalid bot names, `maxTicks ≤ 0`, zero bots, a hunt/seek-death phase without `target`, a buy/sell/collect phase without `item`. `seedOverride` replaces the file seed. The scenario's `world:` and `standards:` paths resolve **relative to the scenario file's directory** (so `content/dev-world/playtest/hub-smoke.yaml` writes `world: ..`) and are returned absolute; a missing `world:` stays `null` (the run mode falls back to `Playtest:DefaultWorld`). `List(string? directory = null)` lists `directory` (default `Playtest:ScenarioDirectory`), returns ordinal-sorted summaries and never throws.
3. ◆ **Missing content fails before the run.** After world boot and **before** any bot is added, every blueprint id the scenario names (room ids in `expect.movedTo`, hunt/seek-death `target` mobs, collect/sell/buy `item`s) is probed. Any miss throws a `PlaytestScenarioContentException` listing all misses; no bot enters, no step runs, no report file is written, and the CLI exits `2`.
4. ◆ **Expectations family.** The balance-standards document gains `PlaytestExpectations` (global thresholds keyed by metric, plus per-(Tier, Band) rows for band-scoped metrics). Each threshold is `{min?, max?}`. Absent section → `PlaytestExpectations.Empty`. Save → load round-trips. Validation errors: unknown metric id; neither bound set; `min > max`; a band row out of `[0, MaxTier] × [1, BandsPerTier]`; duplicate band row; a non-band-scoped metric inside a band row. `IBalanceStandardsRegistry.PlaytestExpectations` serves the loaded value. The Standards page's document↔form mapping lives in a plain `Hedron.Web/Services/StandardsForm` class (extracted from `Standards.razor`'s `LoadForm`/`BuildDocument`) that carries the section through load → edit → build, so saving any other field never drops it.
5. ◆ **Recorder.** Every playtest world subscribes `PlaytestRecorder` at `HandlerPriority.Ai` (95), for exactly the event set in [Events fired](#events-fired). The recorder:
   - appends a `PlaytestFact(Tick, Seq, BotId, Kind, …)` **only** for events whose subject or killer is a bot (the setup actor and all other entities are ignored);
   - stamps facts with the driver's current step tick, never `OccurredAt`/`EventId`;
   - resolves a killed mob's `(Tier, Band)` from its template by the event's `BlueprintId` (a definition read);
   - mutates nothing and writes no output.

   The live host subscribes no `Hedron.Core.Modules.Playtest` handler.
6. ◆ **Rejected outcome.** A command that refuses through `CommandContext.RefuseAsync(CommandRefusalMessage)` writes the message unchanged (telnet bytes identical) and marks the dispatch's `CommandOutcomeSink`; the dispatcher reads the sink after `ExecuteAsync` and publishes `CommandExecutedEvent(…, CommandOutcome.Rejected)`. The `CommandContext` record stays immutable: the sink is a dispatcher-owned per-dispatch object the context only references (a context built without one, as in direct command tests, just writes). Every PT-1 `CommandRefusalMessage` site (24) goes through `RefuseAsync`; no `CommandRefusalMessage` is written any other way (source guard). Commands that don't refuse still publish `Success`; a throw after `RefuseAsync` publishes `Threw`. The dispatcher's existing outcomes (`ParseFailed`, `Unauthorized`, `Refused`) are unchanged and now pinned by test.
7. ◆ **Metrics (R10).** `IPlaytestMetricsSystem.Compute` derives, per bot, from the facts + phase spans + expectations-of-steps + tick interval:
   - **global:** `gameTimeToFirstKill`, `gameTimeToFirstImprovement`, `killsToFirstImprovement`, `killsPerImprovement`, `xpPerHour`, `deathsPerHour`, `recoveryTime` (respawn → next engagement, mean), `coinEarned`, `coinSpent`, `coinToFirstUpgrade`, `roomsReached`, `failedCommandRate`;
   - **per (Tier, Band):** `fights`, `kills`, `losses`, `lossRate`, `deaths`, `deathsPerHour`;
   - **reported unavailable** (value `null`, reason given, never graded): `stuckEvents` (PT-3, R8) and `considerLabels` (needs an event from the `consider` slice; resolved decision 4).

   All times are game time = ticks × `Heartbeat:IntervalMs`. "Per hour" divides by game hours (run total globally; band dwell time per band). Failed commands = outcomes other than `Success` **plus** walker steps whose declared `expect` went unmet, over all commands the bot issued. A metric whose denominator is zero is `null` ("no data"), not `0`.
8. ◆ **Achievements (A1–A7).** The same system derives each achievement as `Achieved | NotAchieved(reason)`. The definitions are in [Design notes → A-criteria](#a-criteria-as-built-definitions). A8 is always `Unavailable` in PT-2 (resolved decision 3).
9. ◆ **Grading (R12).** `IPlaytestGrader.Grade` returns one `PlaytestVerdict(Name, bool? Passed, Reason)` per (bot, metric with a stated threshold) and per (bot, criterion):
   - a metric with a threshold → pass/fail with the observed value and bound in `Reason`; a `null` value against a threshold → fail ("no data");
   - a metric with no threshold → **no verdict** (descriptive only);
   - an out-of-scope criterion → `Passed = null` ("not in scenario scope");
   - an in-scope criterion → `Passed = achievement ∧ every stated threshold on that criterion's linked metrics` ([linkage](#a-criteria-as-built-definitions));
   - an `Unavailable` achievement → `Passed = null`.

   The grader never fabricates a threshold.
10. ◆ **Report (R13, R3, R28).** `PlaytestReport` has `schemaVersion: 1`, `automated: true`, the scenario echo (name, seed, world label, `maxTicks`, criteria, bots), `status` (`completed` | `budgetExhausted`), `ticksRun`, `gameTime`, `tickFaults`, per-bot metrics/achievements/phase spans/action log, and verdicts. The body has **no wall-clock field**: two runs with the same scenario, seed and content serialize byte-identically. The writer names the file `<UTC>-<name>-<seed>.json` using a write time supplied by the run mode, and writes `<same stem>.summary.md` beside it, both via `AtomicFileWrite`.
11. ◆ **Runner loop.** `PlaytestRunner.RunAsync` runs in this order: boot world → probe content (Post. 3) → `RunSetupAsync(setup)` → `AddBotAsync` per bot in file order → loop. Each loop step: for each bot in creation order, `Drain` → `policy.Next(observations)` → `Enqueue` (a command) or nothing (wait). Then `StepAsync()`. The loop ends when every policy is `Done` (`completed`) or `maxTicks` steps have run (`budgetExhausted`). It then grades, disposes the world and returns the report. A walker scenario runs with `CommandsPerBotPerStep = 1`, so `(bot, tick)` identifies at most one command (the correlation key for step expectations).
12. ◆ **Acceptance walker (R7).** Given a plan and observation batches, `AcceptanceWalkerPolicy` emits command lines per the [phase table](#acceptance-walker-phase-plan). It branches only on typed observation facts (`type`, `kind`, `actorName`/`targetName`, `refusal kind`), never on prose. Per PT-1's audience note, it keys combat hit/miss on `actorName`/`targetName`, not `perspective`. It reports its current phase (index, kind, tier/band) to the runner after each decision.
13. ◆ **CLI (R1, R25).** `playtest --scenario <path> [--seed N] [--content <dir>] [--keep-scratch]` runs and exits `0` (run completed or budget exhausted, any verdicts), `1` (engine failure), or `2` (usage error, invalid scenario, missing content). `--scenario` always takes a file path (cwd-relative or absolute), so a world-local scenario such as `content/dev-world/playtest/hub-smoke.yaml` runs as-is. `playtest --list [--dir <directory>]` prints the scenarios in `<directory>` (default `Playtest:ScenarioDirectory`; e.g. `--dir content/dev-world/playtest`). `playtest --validate --scenario <path> [--content <dir>]` loads, boots and probes without running (exit `0`, or `2` on an invalid scenario or a missing blueprint). Before boot, the run mode checks that the resolved world directory exists (from `--content`, the scenario's `world:`, or `Playtest:DefaultWorld`) and, when the scenario names one, that the resolved `standards:` file exists; a miss exits `2` naming the path, writes no report, and never surfaces as an unhandled `DirectoryNotFoundException`/`FileNotFoundException` (exit `1`). The run mode's outer provider registers **no `IHostedService`** (no telnet listener, heartbeat service, flush timer or bootstrap); its service wiring is a public static `PlaytestRunMode.ConfigureServices(IServiceCollection, IConfiguration)` so a test can inspect it.
14. ◆ **Smoke golden (R14, resolved decision 1).** `DevWorldHubSmokeGolden` runs the committed `content/dev-world/playtest/hub-smoke.yaml` (resolved decision 7) against a scratch copy of the repo's `content/dev-world/` at its pinned seed and asserts: `status == completed`; criterion `A1` `Passed == true`; `A2`–`A8` `Passed == null`; **elapsed wall time ≤ 10 s** (boot through dispose). It asserts no metric value.
15. ◆ **Cross-process determinism (R3).** Two separate `Server` processes running the smoke scenario produce byte-identical report bodies. This closes the two-process check PT-1 deferred to PT-2.

## Main flow

1. **Invoke.** `Program.Main` → `PlaytestRunMode.Matches(args)` → configuration from `RunModeConfiguration.Build` (slice 1) + `HEDRON_` env vars → `PlaytestRunMode.ConfigureServices(services, configuration)` (`AddLogging` + `Register`; no hosted service) → build the outer provider.
2. **Load.** `IPlaytestScenarioStore.Load(path, seedOverride)` (fail-fast, Post. 2). World source = `--content` > the scenario's `world:` > `Playtest:DefaultWorld` (`content/dev-world`). CLI flags and config paths are cwd-relative, matching every other option path; paths **inside** a scenario file resolve against the scenario file's directory, so a world-local scenario (`content/dev-world/playtest/hub-smoke.yaml` with `world: ..`) runs from any cwd. The standards source = the scenario's `standards:` > `Balance:StandardsPath`. Expectations come from `IBalanceStandardsStore.Load()` over that source (the outer store with `Balance:StandardsPath` overlaid when the scenario names one). Both resolved paths are checked for existence before boot; a miss exits `2` (Post. 13).
3. **Boot.** `PlaytestRunner.RunAsync(scenario, runOptions)` → `PlaytestWorldFactory.CreateAsync(options)` with `Seed`, `ContentSourceDirectory`, `StandardsSourcePath` (new), `Label = scenario.Name`, `CommandsPerBotPerStep = 1`. The factory registers the recorder, `IPlaytestTickSource` and `IPlaytestBotRegistry` in the world's overrides. `PlaytestEngineComposer` calls `SubscribeGameplayHandlers()` and then the new `SubscribePlaytestObservers()`.
4. **Probe.** For each referenced blueprint id: `world.Probe(id)` → `DefinitionProbe(Exists, Kind, Name, Keyword, Tier, Band, RoomEntityId)`. Misses throw (Post. 3). Hits resolve the walker's typed nouns (the target keyword) and the room entity ids for `movedTo` expectations.
5. **Setup and entry.** `world.RunSetupAsync(scenario.Setup)` (R6, admin verbs) → `world.AddBotAsync(name)` per bot. The world registers each bot's character id in `IPlaytestBotRegistry`, which is how the recorder filters.
6. **Loop.** Per step, for each bot: `obs = world.Drain(bot)` → `decision = run.Next(obs)`:
   - `Command(line, expect?)` → `world.Enqueue(bot, line)`, and the runner records `StepExpectation(bot, tick, expect)` when present;
   - `Wait` → nothing;
   - `Done` → the bot is finished.

   The runner records phase transitions from `run.CurrentPhase`. Then `world.StepAsync()`: dispatch → the `CommandExecutedEvent`/effect events → recorder facts stamped with the step tick → `HeartbeatTickEvent` → tick-driven events (combat rounds, bleed-out, respawn) → recorder → output flush (85) → observations.
7. **Grade.** `facts = world.Record` → `IPlaytestMetricsSystem.Compute(facts, phaseSpans, stepExpectations, worldInfo)` → `BotMeasurements` per bot → `IPlaytestGrader.Grade(measurements, expectations, scenario.Criteria)` → verdicts → `PlaytestReportBuilder.Build(…)`.
8. **Dispose.** `await world.DisposeAsync()` (bots leave; the in-memory DB and scratch directory go away, PT-1).
9. **Write.** `IPlaytestReportWriter.WriteAsync(report, writtenAtUtc)` → JSON + `.summary.md` under `Playtest:ReportDirectory` (`data/playtest/reports`). The console prints the summary; exit `0`.

## Events fired

**No new event types.** The recorder only observes. The one bus-facing change is the additive `CommandOutcome.Rejected` value on the existing `CommandExecutedEvent`.

| Event | Publisher | Subscribers (new in **bold**) | Fact recorded |
|---|---|---|---|
| `CommandExecutedEvent(Invoker, Verb, Args, Outcome)` | `CommandDispatcher` (now also `Rejected`) | `CommandLoggingHandler`, **`PlaytestRecorder`** | `Command(verb, args, outcome)` |
| `PlayerMovedEvent(Player, From, To, Direction)` | movement | existing, **recorder** | `Moved(from, to)` |
| `MobDiedEvent(Mob, BlueprintId, Killer)` | `CombatMobDeathHandler` | existing, **recorder** (killer = bot) | `Killed(blueprintId, tier, band)` |
| `ExperienceAwardedEvent(Entity, Track, Amount, Source)` | progression | existing, **recorder** | `XpAwarded(track, amount, source)` |
| `TrackImprovedEvent(Entity, Track, NewCount)` | progression | existing, **recorder** | `Improved(track, count)` |
| `CurrencyAwardedEvent(Recipient, Currency, Amount)` | economy | existing, **recorder** | `CoinAwarded(amount)` |
| `ItemPickedUpEvent(Player, Item, Room)` | items | existing, **recorder** | `PickedUp(itemEntity)` |
| `ItemEquippedEvent(Player, Item, Slots)` | items | existing, **recorder** | `Equipped(itemEntity)` |
| `ItemBoughtEvent(Player, Shop, Item, Room, Price, Currency)` | shopping | existing, **recorder** | `Bought(itemEntity, price)` |
| `ItemSoldEvent(Player, Shop, Item, Room, Price, Currency)` | shopping | existing, **recorder** | `Sold(itemEntity, price)` |
| `CombatStartedEvent(Attacker, Defender, Room)` | `KillCommand`, `AbilityInvocationPipeline` | existing, **recorder** | `Engaged` |
| `CombatEndedEvent(Attacker, Defender, Outcome, Room, …)` | combat | existing, **recorder** (bot is either party) | `FightEnded(outcome)` |
| `PlayerIncapacitatedEvent(Player, Room)` | combat/effects | existing, **recorder** | `Incapacitated` |
| `PlayerDiedEvent(Player, Room, Killer)` | `DeathTickHandler` | existing, **recorder** | `Died` |
| `PlayerRespawnedEvent(Player, RespawnRoom)` | `PlayerDeathHandler` | existing, **recorder** | `Respawned(room)` |

## Systems / handlers involved

| Piece | Kind | Status | Shape / change |
|---|---|---|---|
| `YamlDocumentFile<TDto>` / `YamlDefinitionDirectory<TDto>` / `YamlReadResult<TDto>` / `DefinitionValidation` (`Core/Systems/Definitions/`) | core infrastructure | **new (branch B) / reused (branch A)** | See [WP1](#wp1--scenario-yaml-on-the-shared-store-expectations-family). Per-call YamlDotNet (de)serializers (not thread-safe; `WorldManifestStore` precedent), camelCase, `IgnoreUnmatchedProperties`, opt-in null omission, `AtomicFileWrite`, ordinal `List`, malformed → `Error` (never throws on read) |
| `BalanceStandardsStore`, `SimScenarioStore`, `WorldManifestStore` | stores | **changed (branch B only)** | Their YAML I/O moves onto the shared types. DTO↔domain mapping and validation stay per family; public behavior is unchanged |
| `PlaytestScenario` + `BotSpec` + `WalkerPlan`/`WalkerPhase`/`WalkerStep`/`StepExpectation` + `AcceptanceCriterion {A1…A8}` (`Core/Modules/Playtest/`) | records | **new** | `PlaytestScenario(int SchemaVersion, string Name, string? World, string? Standards, int Seed, int MaxTicks, IReadOnlyList<AcceptanceCriterion> Criteria, IReadOnlyList<string> Setup, IReadOnlyList<BotSpec> Bots)`; `BotSpec(string Name, string PolicyId, WalkerPlan? Plan)` |
| `IPlaytestScenarioStore` / `PlaytestScenarioStore` (`Systems/`) | domain system (store) | **new** | `PlaytestScenario Load(string path, int? seedOverride = null)` (any path; `world:`/`standards:` resolved against the file's directory); `void Validate(PlaytestScenario)`; `IReadOnlyList<ScenarioFileSummary> List(string? directory = null)` (default `Playtest:ScenarioDirectory`); `IReadOnlyCollection<string> ReferencedBlueprintIds(PlaytestScenario)`. No `SaveAsync` (the editor page is PT-5) |
| `PlaytestOptions` (`Playtest:` section) | options | **new** | `ScenarioDirectory = "data/playtest/scenarios"` (operator scenarios, gitignored; committed world scenarios live in `<world>/playtest/`), `ReportDirectory = "data/playtest/reports"`, `DefaultWorld = "content/dev-world"` |
| `PlaytestExpectations`, `MetricThreshold(double? Min, double? Max)`, `BandPlaytestExpectations(int Tier, int Band, IReadOnlyDictionary<PlaytestMetricId, MetricThreshold>)`, `PlaytestMetricId` enum (`BalanceInspection/Standards/`) | standards family | **new** | `PlaytestExpectations(IReadOnlyDictionary<PlaytestMetricId, MetricThreshold> Global, IReadOnlyList<BandPlaytestExpectations> Bands)`, `static Empty` (joins PT-1's T4 read-only static-record allowlist); `MetricThreshold? ThresholdFor(PlaytestMetricId, PowerBand? band)` (no fallback between scopes). The metric vocabulary lives in BalanceInspection because the standards document owns it; Playtest depends on it (domain → domain, the `SimOutcomeEvaluator` precedent) |
| `BalanceStandardsDocument` | record | **changed** | + trailing `PlaytestExpectations? PlaytestExpectations = null` (normalized to `Empty` by the store and registry) |
| `BalanceStandardsStore` | store | **changed** | parse/validate/save `playtestExpectations` (Post. 4) |
| `IBalanceStandardsRegistry` / `BalanceStandardsRegistry` | registry | **changed** | + `PlaytestExpectations PlaytestExpectations { get; }` |
| `Standards.razor` | Blazor page | **changed** | A "Playtest expectations" section: a global table (one row per `PlaytestMetricId`, optional min/max, blank = descriptive) and a per-band table for band-scoped metrics. Binding only; the page delegates load/build to `StandardsForm`; validation is the store's |
| `StandardsForm` (`Hedron.Web/Services/`) | web form model | **new (extracted)** | The `ProgressionSettingsForm`/`CombatantForm` precedent. `static StandardsForm FromDocument(BalanceStandardsDocument)` (today's `LoadForm`) and `BalanceStandardsDocument ToDocument()` (today's `BuildDocument`), now carrying `PlaytestExpectations` as global + band rows. Plain class, no Blazor dependency, so the round-trip is unit-tested |
| `CommandOutcome.Rejected` | enum value | **new (additive)** | "The command ran and rejected the request with a typed `CommandRefusalMessage` (any `RefusalKind`, including `RefusalKind.Declined`)". Distinct from the existing `Refused` (dispatcher incapacitation gate; the command never ran). Named `Rejected`, not `Declined`, so it does not collide with PT-1's `RefusalKind.Declined` (resolved decision 2's substance unchanged) |
| `CommandOutcomeSink` (`Core/Commands/`) | command framework | **new** | Sealed class, one per dispatch, created by `CommandDispatcher`. `void MarkRejected()`; `bool Rejected { get; }`. Not shared across dispatches; confined to the dispatching flow |
| `CommandContext` | record | **changed (additive)** | + trailing optional positional `CommandOutcomeSink? OutcomeSink = null`. The record stays immutable (it holds a reference, never a mutable flag of its own); the ~17 direct constructions in tests keep compiling |
| `CommandContext.RefuseAsync(CommandRefusalMessage)` | command framework | **new** | `await Output.WriteAsync(message); OutcomeSink?.MarkRejected();`. The dispatcher reads `sink.Rejected` after `ExecuteAsync` returns and publishes `Rejected` instead of `Success`; an exception still publishes `Threw` |
| `KillCommand`, `FleeCommand`, `BuyCommand`, `SellCommand`, `ListCommand`, `WearCommand`, `GetCommand` | commands | **changed** | Their 24 `CommandRefusalMessage` writes become `await context.RefuseAsync(…)`. Text, severity, kind and audience are unchanged. `WearCommand`'s pre-existing `SaveEntityAsync` is untouched |
| `CommandLoggingHandler` | handler | **unchanged** | logs the new value through its existing outcome formatting |
| `PlaytestRecorder` (`Handlers/`) | handler (observation-only) | **new** | `IEventHandler<T>` for the 15 events above, at `HandlerPriority.Ai`. Ctor: `IPlaytestTickSource`, `IPlaytestBotRegistry`, `ITemplateRegistry`. Appends to `PlaytestRecordBuffer`. Registered **only** through the playtest world's overrides, never in `Register` (keeps PT-1 T2 green) |
| `IPlaytestTickSource`, `IPlaytestBotRegistry`, `PlaytestRecordBuffer` | harness seams | **new** | `long CurrentStepTick`, updated by `PlaytestTickDriver` before dispatch (the same tick `BotSession` stamps); `bool TryGetBot(uint entityId, out BotId)`; an append-only `List<PlaytestFact>` with a snapshot read |
| `PlaytestFact` + `FactKind` | records | **new** | `PlaytestFact(long Tick, int Seq, BotId Bot, FactKind Kind, string? Verb, string? Args, CommandOutcome? Outcome, uint? FromRoom, uint? ToRoom, uint? ItemEntityId, long? Amount, string? BlueprintId, int? Tier, int? Band, string? Detail)`. Entity ids stay inside the harness and are never compared across runs |
| `CompositionRoot.SubscribePlaytestObservers(this IServiceProvider)` | composition | **new** | Subscribes `PlaytestRecorder` for its event set if registered. Called by `PlaytestEngineComposer` right after `SubscribeGameplayHandlers` |
| `PlaytestWorld` (PT-1) | harness | **changed** | + `PlaytestRecord Record { get; }` (fact snapshot); + `DefinitionProbe Probe(string blueprintId)` (a template/definition read, plus the room's spawned entity id for room ids); + `PlaytestWorldInfo Info` (`StartingRoomEntityId`, `TickInterval`, `Seed`). None of these is ever handed to a policy (R4) |
| `PlaytestWorldOptions` (PT-1) | record | **changed** | + `string? StandardsSourcePath` (copied into scratch; replaces the base path in the overlay) |
| `PlaytestWorldFactory` (PT-1) | harness | **changed** | Overrides also register the recorder, the tick source, the bot registry and the buffer |
| `IPlaytestMetricsSystem` / `PlaytestMetricsSystem` (`Systems/`) | domain system (pure) | **new** | `IReadOnlyList<BotMeasurements> Compute(PlaytestRecord record, IReadOnlyList<PhaseSpan> phases, IReadOnlyList<StepExpectationRecord> expectations, PlaytestWorldInfo info)`. `BotMeasurements(BotId, string Name, IReadOnlyList<MetricValue> Global, IReadOnlyList<BandMetricValues> Bands, IReadOnlyDictionary<AcceptanceCriterion, Achievement> Achievements, IReadOnlyList<string> Warnings)`. `MetricValue(PlaytestMetricId Id, double? Value, string? UnavailableReason)` |
| `IPlaytestGrader` / `PlaytestGrader` (`Systems/`) | domain system (pure) | **new** | `IReadOnlyList<PlaytestVerdict> Grade(IReadOnlyList<BotMeasurements>, PlaytestExpectations, IReadOnlyCollection<AcceptanceCriterion> scope)`; `PlaytestVerdict(string Bot, string Name, bool? Passed, string Reason)` |
| `PlaytestReport` + `PlaytestReportBuilder` (`Systems/`, pure) / `IPlaytestReportWriter` / `PlaytestReportWriter` (`Systems/`, I/O) | report | **new** | `Task<PlaytestReportPaths> WriteAsync(PlaytestReport, DateTimeOffset writtenAtUtc, CancellationToken)`; JSON options are a `static readonly JsonSerializerOptions` (`SimReportJson` precedent; immutable once used) |
| `IBotPolicy` / `IBotPolicyRun` / `BotDecision` / `BotPolicyContext` / `PolicyPhase` (`Policies/`) | driver contract | **new** | `IBotPolicy { string PolicyId; IBotPolicyRun Start(BotPolicyContext ctx); }`; `IBotPolicyRun { BotDecision Next(IReadOnlyList<Observation> observations); PolicyPhase CurrentPhase { get; } }`; `BotDecision = Command(string Line, StepExpectation? Expect) \| Wait \| Done`. `BotPolicyContext(BotSpec Spec, IReadOnlyDictionary<string, DefinitionProbe> Resolved)` carries probed nouns only, never world state. DI-collected (the `ISimCombatantPolicy` precedent) |
| `AcceptanceWalkerPolicy` (`Policies/`) | scripted driver | **new** | `PolicyId = "acceptanceWalker"`. The [phase plan](#acceptance-walker-phase-plan) |
| `ObservationView` (`Policies/`) | helper | **new** | Parses `Observation.Json` once (`JsonDocument`) and exposes `Type`, `Kind`, `GetString(name)`; the only JSON reader policies use |
| `PlaytestRunner` (`Core/Modules/Playtest/`, **not** `Systems/`) | Initiator-tier harness | **new** | `Task<PlaytestReport> RunAsync(PlaytestScenario, PlaytestRunOptions, CancellationToken)`; `PlaytestRunOptions(string ContentSourceDirectory, string? StandardsSourcePath, PlaytestExpectations Expectations, bool KeepScratch, ILoggerFactory? LoggerFactory)`. Ctor: `PlaytestWorldFactory`, `IEnumerable<IBotPolicy>`, `IPlaytestMetricsSystem`, `IPlaytestGrader`, `PlaytestReportBuilder`. Reused by PT-3 sweeps and PT-5 |
| `PlaytestModule.AddPlaytestModule` | DI | **new** | Options, store, metrics, grader, builder, writer, policies, `PlaytestWorldFactory`, `PlaytestRunner`. Called from `CompositionRoot.Register`. **Registers no `IEventHandler`** |
| `IPlaytestEngineComposer` → `PlaytestEngineComposer` | DI | **changed** | Registered in `CompositionRoot.Register` (Server owns the type), so the run mode, tests and (later) PT-5 resolve one composer |
| `PlaytestRunMode` (`Server/`) | Initiator (run mode) | **new** | `Matches(args)`, `RunAsync(args, configuration)`, `static ConfigureServices(IServiceCollection, IConfiguration)` (no hosted service; G8); Post. 13. Branches in `Program.Main` beside `simulate` |

## Implementation plan — work packages

### WP1 — Scenario YAML on the shared store; expectations family

- **Scope:**
  1. **YAML store: extract or consume.** Check whether Phase 5 `help-topics` has merged.
     - **Branch A (merged):** build `PlaytestScenarioStore` on the shared store as shipped; nothing else.
     - **Branch B (not merged, the expected case):** scenarios would be hand-rolled family #4, the first past instance #3, so per the brief **PT-2 performs the extraction** and `help-topics` consumes it.
       - Add `Core/Systems/Definitions/{YamlDocumentFile.cs, YamlDefinitionDirectory.cs, YamlReadResult.cs, DefinitionValidation.cs}`:
         - `YamlDocumentFile<TDto>(string path, YamlCodecOptions? options = null)`: `string Path`; `YamlReadResult<TDto> Read()` → `(TDto? Dto, bool Exists, string? Error)`; `Task WriteAsync(TDto, CancellationToken)`.
         - `YamlDefinitionDirectory<TDto>(string directory, YamlCodecOptions? options = null)`: `IReadOnlyList<YamlFileEntry> List(Func<TDto, string?> nameOf)` (ordinal, name falls back to the filename, unparseable files listed rather than thrown); `YamlReadResult<TDto> Read(string path)`; `Task<string> WriteAsync(string name, TDto dto, CancellationToken)` (sanitized filename, the `SimScenarioStore.Sanitize` rule).
         - `DefinitionValidation.Failed(string subject, IReadOnlyList<string> errors)` builds the existing `"… failed validation:\n  • …"` exception text.
       - Migrate `BalanceStandardsStore` (single document), `SimScenarioStore` (directory) and `WorldManifestStore` (single document, retiring slice 1's bounded debt). Each family's mapping, validation and warnings stay in place.
       - The layered engine + world roots for help topics are **not** built here; `help-topics` composes two `YamlDefinitionDirectory` instances. The types take one root, which does not preclude that.
  2. `PlaytestScenario` model + `PlaytestScenarioStore` + `PlaytestOptions` (Post. 2), including the walker plan DTOs (see [Content tooling impact](#content-tooling-impact) for the schema).
  3. `PlaytestExpectations` family: domain types, the document field, store parse/validate/save, the registry accessor, the Standards page section (Post. 4).
  4. Extract `Standards.razor`'s `LoadForm`/`BuildDocument` into `Hedron.Web/Services/StandardsForm` (pure refactor first, then the new section), so the "a save never drops the section" postcondition is unit-tested (U12).
- **Files:**
  - `Core/Systems/Definitions/*` (branch B);
  - `Core/Modules/BalanceInspection/Standards/{BalanceStandardsDocument.cs, BalanceStandardsStore.cs, BalanceStandardsRegistry.cs, IBalanceStandardsRegistry.cs, BalanceStandardsDefaults.cs, PlaytestExpectations.cs}`;
  - `Core/Modules/Simulation/Systems/SimScenarioStore.cs`, `Core/Modules/World/Systems/WorldManifestStore.cs` (branch B);
  - `Core/Modules/Playtest/{PlaytestScenario.cs, PlaytestOptions.cs, Systems/IPlaytestScenarioStore.cs, Systems/PlaytestScenarioStore.cs}`;
  - `Hedron.Web/Components/Pages/Standards.razor`, `Hedron.Web/Services/StandardsForm.cs`;
  - tests under `Hedron.Tests/{Systems/Definitions, Modules/BalanceInspection/Standards, Simulation, World, Playtest, Web}/` (U4/U5 extend the existing `Modules/BalanceInspection/Standards/` suites).
- **Docs:**
  - `design/balance.md`: a knob-catalog row for the playtest-expectations family; the Observability row for the `playtest` CLI and its reports (rule 5); rule 4 gains "playtest outcomes are graded from `playtestExpectations`";
  - `features/progression/power-budget-system.md` (the standards document section);
  - flow-29 (Standards-page leg: the expectations section; the page maps through `StandardsForm`);
  - `reference/systems.md`: updated `BalanceStandardsRegistry` row (+ `PlaytestExpectations` accessor) and `BalanceStandardsStore` row (+ the `playtestExpectations` family and its validation); a new `PlaytestScenarioStore` row (domain systems; `Load` any path, `List(directory?)`);
  - **branch B only:**
    - the backlog "YAML-authored definition pipeline" entry ("extraction landed in PT-2; help topics consume it; the manifest's bounded debt is closed");
    - the Phase 5 program's slice-4 row ("consumes the shared store from PT-2" instead of "extraction");
    - `reference/systems.md` rows for the shared types and the three migrated stores;
    - skills `add-core-system`/`add-domain-system` (a new YAML family builds on `Core/Systems/Definitions/`).
- **Depends on:** PT-1, slice 1 (only for migrating `WorldManifestStore`).
- **Out of scope:** scenario `SaveAsync` and the editor page (PT-5); help-topic layering (slice 4); any change to standards *semantics* beyond the new family.
- **Exit criterion:** U1–U5, U12 green; the existing `BalanceStandardsStore` / `SimScenarioStore` / `WorldManifestStore` tests green with their existing cases unmodified (branch B); build + test green; manual: the Standards page loads, edits and saves an expectations row, and a cell edit preserves it.

### WP2 — Measurement: declined outcome, recorder, metrics, grader, report

- **Scope:**
  - `CommandOutcome.Rejected` + `CommandOutcomeSink` + `CommandContext.OutcomeSink`/`RefuseAsync`, the dispatcher's per-dispatch sink and post-execute read, and the 24 site conversions (Post. 6);
  - the new `CommandDispatcherTests` (U6), including on-touch ratchet coverage of the dispatcher's existing outcomes;
  - `IPlaytestTickSource`, `IPlaytestBotRegistry`, `PlaytestRecordBuffer`, `PlaytestFact`, `PlaytestRecorder`;
  - `CompositionRoot.SubscribePlaytestObservers`; the PT-1 factory and composer wiring; `PlaytestWorld.Record`/`Probe`/`Info`; `PlaytestWorldOptions.StandardsSourcePath`;
  - `PlaytestMetricsSystem` (metrics + achievements), `PlaytestGrader`, `PlaytestReport` + builder + writer.
- **Files:**
  - `Core/Commands/{CommandOutcome.cs, CommandOutcomeSink.cs, CommandContext.cs, CommandDispatcher.cs}`; the seven commands listed above;
  - `Core/Modules/Playtest/{Handlers/PlaytestRecorder.cs, IPlaytestTickSource.cs, IPlaytestBotRegistry.cs, PlaytestRecordBuffer.cs, PlaytestFact.cs, PlaytestWorld.cs, PlaytestWorldFactory.cs, PlaytestWorldOptions.cs, PlaytestTickDriver.cs, DefinitionProbe.cs, PlaytestWorldInfo.cs}`;
  - `Core/Modules/Playtest/Systems/{IPlaytestMetricsSystem.cs, PlaytestMetricsSystem.cs, IPlaytestGrader.cs, PlaytestGrader.cs, PlaytestReport.cs, PlaytestReportBuilder.cs, IPlaytestReportWriter.cs, PlaytestReportWriter.cs, PlaytestReportJson.cs}`;
  - `Server/{CompositionRoot.cs, Playtest/PlaytestEngineComposer.cs}`;
  - `Hedron.Tests/Commands/CommandDispatcherTests.cs` (**new**), `Hedron.Tests/Architecture/ArchitectureGuardTests.cs` (G1, G2), the rest under `Hedron.Tests/Playtest/`.
- **Docs:**
  - [flow-03](../architecture/flows/flow-03-player-command-lifecycle.md): body step 7 (execute) gains "a command refusing through `RefuseAsync` marks the dispatch's `CommandOutcomeSink`; the dispatcher publishes `Rejected` instead of `Success`", and body step 8 (publish) reads "`Success`, or `Rejected` when the sink was marked"; **and** the mermaid's execute branch becomes an `alt` over three outcomes — `Success`, `Rejected` (via `RefuseAsync` → sink) and `Threw` — adding the `Threw` edge the current diagram is missing (pre-existing drift against step 7);
  - `features/commands/commands.md` + `command-framework.md`: the outcome table lists all six values with a one-line meaning each, and explicitly distinguishes `Refused` (dispatcher incapacitation gate; the command never ran) from `Rejected` (the command ran and wrote a typed `CommandRefusalMessage` via `RefuseAsync`, whatever its `RefusalKind`, including `RefusalKind.Declined`); documents `RefuseAsync` and the per-dispatch sink;
  - `reference/commands.md` (the seven verbs' refusals report `Rejected`);
  - `reference/handlers.md`: the `PlaytestRecorder` row (priority `Ai`, "playtest worlds only", subscribed by `SubscribePlaytestObservers`); the `CommandLoggingHandler` row (line 169) outcome list rewritten to the full set `Success, ParseFailed, Unauthorized, Threw, Refused, Rejected`, fixing the pre-existing omission of `Refused` in the same edit;
  - `reference/systems.md`: rows for `PlaytestMetricsSystem`, `PlaytestGrader`, `PlaytestReportBuilder` (pure) and `PlaytestReportWriter` (I/O) under domain systems; `PlaytestRunner` under "Background Services / Initiators";
  - skill `add-command`: write a typed refusal with `context.RefuseAsync`, never `Output.WriteAsync(new CommandRefusalMessage…)`;
  - skill **`add-handler`** (`.claude/skills/add-handler/SKILL.md`, INV-20): a **playtest-only observer** (a handler that measures but must never run in the live host) is registered through `PlaytestWorldFactory`'s overrides, subscribed in `CompositionRoot.SubscribePlaytestObservers` at `HandlerPriority.Ai`, and added to G3's expected event set; it never goes in `CompositionRoot.Register`, `SubscribeGameplayHandlers` or any `Add<Feature>Module`. Cite Phase 5 slice 5's `MobConsideredEvent` subscription as the next consumer.
- **Depends on:** WP1 (`PlaytestExpectations`, `PlaytestMetricId`).
- **Out of scope:** stuck detection (PT-3); converting non-typed refusals (movement "no exit", ability refusals) to `RefuseAsync` (on-touch ratchet with the output ratchet); per-band attribution from events (see Design notes).
- **Exit criterion:** U6–U10, G1–G4 green; PT-1's T18 updated per G3 and green; PT-1's T28a extended to assert `Rejected`; build + test green; a manual telnet smoke of `kill nothing` / `buy nothing` shows unchanged text.

### WP3 — Walker, runner, CLI, smoke golden, docs

- **Scope:**
  - `IBotPolicy` contract, `ObservationView`, `AcceptanceWalkerPolicy`;
  - `PlaytestRunner`; `AddPlaytestModule` + its `Register` call; the composer registration;
  - `Server/PlaytestRunMode.cs` + the `Program.Main` branch;
  - the committed `content/dev-world/playtest/hub-smoke.yaml` (resolved decision 7; `content/` is committed, so no `.gitignore` change);
  - `PlaytestFixtureContent` (PT-1 test helper) extended with a band-tagged weak mob, a band-tagged deadly mob, a found item with `value > 0`, and a shop stocking one wearable upgrade;
  - the golden, the runner flow tests, and the cross-process test.
- **Files:**
  - `Core/Modules/Playtest/{PlaytestModule.cs, PlaytestRunner.cs, PlaytestRunOptions.cs, Policies/IBotPolicy.cs, Policies/BotDecision.cs, Policies/ObservationView.cs, Policies/AcceptanceWalkerPolicy.cs}`;
  - `Server/{PlaytestRunMode.cs, Program.cs, CompositionRoot.cs}`;
  - `content/dev-world/playtest/hub-smoke.yaml`;
  - `Hedron.Tests/Playtest/{PlaytestFixtureContent.cs, AcceptanceWalkerPolicyTests.cs, PlaytestRunnerTests.cs, PlaytestReportDeterminismTests.cs, PlaytestRunModeTests.cs, Golden/DevWorldHubSmokeGolden.cs, PlaytestCliDeterminismTests.cs}`; `Hedron.Tests/Composition/HostCompositionTests.cs` (G8); `Hedron.Tests/Architecture/ArchitectureGuardTests.cs` (G5).
- **Docs:**
  - [flow-34](#flows-introduced-or-modified) retitled and extended into the **playtest run journey**;
  - new `features/playtest/playtest-run.md` (scenario schema, scenario-relative path resolution, where scenarios live, walker phases, A-criteria definitions, metrics, report, CLI), linked from `features/playtest/README`/index;
  - `features/world/world-content.md`, the content-ownership table: the `content/dev-world/` row states the dev world owns its committed playtest scenarios (`content/dev-world/playtest/`) and acceptance standards (`content/dev-world/balance/standards.yaml`, resolved decision 5), that the content loader reads only the kind subdirectories so neither loads as templates, and that both stay generic like the rest of the fixture; a new `data/playtest/` row ("operator scenarios and reports; **No**, `/data/*` is gitignored");
  - `reference/systems.md`: an `IBotPolicy + built-ins (Playtest module)` row with `AcceptanceWalkerPolicy` (the `ISimCombatantPolicy + built-ins` precedent: location, DI-collected as `IEnumerable<IBotPolicy>` in `AddPlaytestModule`, keyed by `PolicyId` in `PlaytestScenarioStore` and `PlaytestRunner`), plus `IBotPolicyRun`/`BotDecision`;
  - `07-testing.md`: registers **Tier 7 — playtest golden** (what it asserts: criterion verdicts only, never metric values; the ≤ 10 s budget; re-pin only when the loop breaks) and the process-level determinism test;
  - root `README.md`: a "Playtest (headless)" section beside "Balance simulation (headless)";
  - `05-configuration.md`: the `Playtest:` section (`ScenarioDirectory` is for operator scenarios; committed world scenarios live in `<world>/playtest/`);
  - skill `add-tests`: when to add a playtest golden vs. a flow test; **and** the process-level companion (P1): when a determinism or exit-code claim must hold across processes, launch the built `Server.dll` with `ProcessStartInfo`, set overrides (e.g. `HEDRON_Playtest__ReportDirectory`) only on `ProcessStartInfo.Environment` (never `Environment.SetEnvironmentVariable`, which leaks into the parallel test process), redirect output to a per-test temp directory, and keep it to the minimum number of launches;
  - skill `balance-tuning`: a playtest-expectations edit re-runs the relevant playtest scenario (R27 policy unchanged).
- **Depends on:** WP1, WP2.
- **Out of scope:** the other five behaviours, stuck detection, N-bot sweeps, findings files (PT-3); MCP (PT-4); the editor page (PT-5); the acceptance golden over A1–A8 (Phase 5 slice 9).
- **Exit criterion:** U11, F1–F3, G5–G8, P1 green; build + test green; manual `dotnet run --project Server -- playtest --scenario content/dev-world/playtest/hub-smoke.yaml` from the repo root writes a report with A1 PASS; `playtest --list --dir content/dev-world/playtest` and `--validate` behave per Post. 13.

**Primary agent:** once all three packages land, run `architecture-reviewer` (code mode) across the combined diff.

## Content tooling impact

*(INV-18.)*

**New data file: playtest scenario.** Where scenarios live (resolved decision 7):
- **Committed world scenarios** live beside their world: `content/dev-world/playtest/*.yaml` (dev-world's own, beside its `balance/standards.yaml` from decision 5). The content loader never reads `playtest/`.
- **Operator scenarios** live under `Playtest:ScenarioDirectory` (`data/playtest/scenarios/`, gitignored) or anywhere else; `--scenario` takes any path.

Paths inside a scenario resolve against the scenario file's directory. The committed smoke scenario, `content/dev-world/playtest/hub-smoke.yaml`:

```yaml
schemaVersion: 1
name: dev-world-hub-smoke
world: ..                         # optional; relative to this file (→ content/dev-world); default Playtest:DefaultWorld
# standards: ../balance/standards.yaml   # optional; relative to this file; default Balance:StandardsPath.
#                                        # Slice 9 / the B1 drop sets it once the file exists (decision 5).
seed: 1
maxTicks: 40                      # step budget (2 s game time each); hitting it marks budgetExhausted
criteria: [A1]                    # A-criteria graded; others report "not in scenario scope"
setup: []                         # admin verb lines run by the setup actor before bots enter (R6)
bots:
  - name: Bota
    policy: acceptanceWalker
    plan:
      - phase: onboard            # A1's phase
        steps:
          - { do: look }
          - { do: score }
          - { do: inventory }
          - { do: north, expect: { movedTo: room.hub.north } }
          - { do: south, expect: { movedTo: room.hub.square } }
          - { do: south, expect: { movedTo: room.hub.south } }
          - { do: north, expect: { movedTo: room.hub.square } }
          - { do: help }
```

The full walker plan vocabulary (documented in `features/playtest/playtest-run.md`; exercised by F1 against the test fixture):

```yaml
      - phase: walk               # travel; same step/expect syntax; not A1-graded
        steps: [ { do: east, expect: moved } ]
      - phase: hunt
        tier: 0
        band: 1
        target: mob.b1.rat        # probed; the walker types the template's keyword
        kills: 3
        maxTicks: 200             # phase budget; exhausting it ends the phase as not achieved
      - phase: collect            # pick up a found item here (A5 source)
        item: item.junk.bone
      - phase: sell               # at a shop in this room
        item: item.junk.bone
      - phase: buy                # list → buy → wear
        item: item.b1.blade
        wear: true
      - phase: seekDeath          # fight the target until death and respawn
        tier: 0
        band: 3
        target: mob.b3.brute
        maxTicks: 400
      - phase: wait
        ticks: 20                 # recover in the hub
```

`expect` forms: omitted (default `succeeded`: outcome `Success`), `moved` (any `PlayerMovedEvent` by the bot that tick), `{ movedTo: <room id> }` (the move landed in that room; the id is probed).

**New standards family: `playtestExpectations`** (in the balance-standards document, `Balance:StandardsPath`):

```yaml
playtestExpectations:
  global:
    gameTimeToFirstKill: { max: 600 }    # seconds of game time
    failedCommandRate:   { max: 0.05 }
  bands:
    - tier: 0
      band: 3
      lossRate: { min: 0.3 }
```

**Authoring and inspection surfaces:**

| Surface | Author | Inspect |
|---|---|---|
| Scenario YAML (hand-edited) | everything above | `playtest --validate`, `playtest --list [--dir <dir>]`, the report's scenario echo |
| `playtest` CLI | — | JSON report + `.summary.md`, console summary |
| Standards page (`/standards`) | the playtest-expectations section (global + per band) | the same page; the report's verdict reasons quote each bound |
| Hand-edited standards YAML | yes | Standards page; load validation errors |

- **Restart-to-apply:** expectations are read per run from the file, not composed into a live singleton, so a CLI run always sees the saved file.
- No new admin command, no `TemplateRegistry` kind, no `[Persistent]` component.
- **Committed:** `content/dev-world/playtest/hub-smoke.yaml` only, inside the already-committed dev world (no `.gitignore` change; `world-content.md`'s ownership table gains the row, WP3). It is generic fixture content under the dev-world rules. Full-loop example plans live in the feature doc and the F1 fixture until dev-world has the content to run them (Phase 5 drops B1–B3).

## Cross-cutting surfaces stressed

*(INV-19, INV-31, INV-17.)*

| Surface | Classification | Notes |
|---|---|---|
| **YAML definition store** | **Gap exposed → fixed in-slice (branch B)** | Scenarios would be the fourth hand-rolled family (after `BalanceStandardsStore`, `SimScenarioStore`, `WorldManifestStore`; template deserializers and content writers are a separate family). Per the brief's "whoever is third extracts" rule, PT-2 extracts if `help-topics` hasn't merged, and migrates the existing three. Branch A is plain reuse. Either way the extraction happens once. |
| **Commands (outcome taxonomy)** | **Gap exposed → fixed in-slice** *(resolved decision 2)* | R10's failed-command rate and the edge case "a failed command is recorded" need business refusals on an event. Resolved decision 1 forbids measuring from perception. `RefuseAsync` + `Rejected` is the thin publishing-seam fact the brief prescribes, reported through a dispatcher-owned per-dispatch `CommandOutcomeSink` (the `CommandContext` record stays immutable), and it is guarded so a new refusal can't bypass it. The dispatcher had no tests; U6 lands them (on-touch ratchet). Non-typed refusals stay `Success` until the output ratchet converts them (backlog entry amended). |
| **Event bus** | Adequate | No new event types. One observer handler at the reserved `Ai` priority, subscribed only in playtest worlds through a dedicated composition method (`SubscribePlaytestObservers`), pinned by G3. |
| **Output / perception** | Adequate | The walker reads PT-1's typed observations through one helper (`ObservationView`). No prose parsing (G5 scans the policy for `"text"` access). |
| **ECS queries / world-state reads** | Adequate (bounded) | The recorder reads no component. It does one **definition** read (`ITemplateRegistry.TryGet(blueprintId)` → `MobTemplate.Tier/Band`) keyed by an event-carried id. `PlaytestWorld.Probe` reads templates plus a room's spawned entity id, for the runner only, before bots act. Neither is a live-state query, and policies never see either (R4). |
| **Per-band attribution** | **Acknowledged debt** | Kills are attributed by the victim's template band (event-carried). Fights, losses, deaths and dwell time are attributed by the walker phase's declared `(tier, band)`, a harness fact. A kill whose template band differs from its phase's band adds a report **warning**. Event-carried opponent attribution (an opponent blueprint on `CombatStartedEvent`) is deferred until a policy fights outside a declared phase (PT-3 explorer/grinder). Backlog entry below. |
| **Persistence** | Adequate | No new persistent state; see the audit below. |
| **Time** | Adequate | All game time is ticks × interval from PT-1's `SteppedClock`. The run mode supplies a wall-clock write time for the report **filename** only (an Initiator; INV-26 covers gameplay logic). The golden's `Stopwatch` measures the test, not the game. |
| **Randomness** | Adequate | One `SeededRandom(seed)` per world (PT-1). The walker and grader draw nothing. |
| **Content templates** | Adequate | Probes go through the existing registry. Missing references fail fast (Post. 3). World-local scenarios in `<world>/playtest/` sit outside the kind subdirectories every content reader scans, so they never load as templates (resolved decision 7). |
| **Configuration** | Adequate | A new `Playtest:` options section (defaults in code). The run mode uses slice 1's `RunModeConfiguration`. |
| **Sessions** | Adequate | Bots and setup use PT-1's `SessionEntry` path unchanged. |
| **Modules / DI** | Adequate | `AddPlaytestModule` registers no event handler, so PT-1's T2 holds. The composer is registered in `Register` (Server-owned). The DI-smoke guard resolves the new registrations (G4). |
| **Web host (INV-8)** | Adequate | The Standards page only binds the new section; document↔form mapping moves to `StandardsForm` (a plain, tested class), and parse and validation live in `BalanceStandardsStore`. |
| **Concurrency (INV-31)** | Adequate (posture below) | — |
| **Flows** | Adequate | Flow 34 extended; flows 03 and 29 touched. See below. |

### INV-31 posture

- **The run mode is one logical flow.** It composes, runs one world through `PlaytestRunner` (the world's single writer, PT-1's confine posture), grades after the loop, disposes, then writes. There is no new thread, timer or `BackgroundService`.
- **The recorder** runs inside the world's flow (it is a handler on that world's bus), so its buffer is confined with the world. `Record` snapshots are taken between steps by the same flow.
- **Policies** are per-bot run objects driven by the runner's flow; they hold no shared state.
- **The shared YAML types** keep no cached state and build (de)serializers per call. Concurrent web-circuit use of the migrated stores is unchanged.
- **Static state:** `PlaytestExpectations.Empty` and the report JSON options join PT-1's T4 allowlist (immutable).
- **Registered everywhere, invoked only by the run mode and tests.** `AddPlaytestModule` and the composer are called from `CompositionRoot.Register`, so the playtest services exist in **every** composition: the live telnet host, the Web host, and each playtest world's nested container. In PT-2 they are resolved and invoked **only** by `PlaytestRunMode` and by tests (G4 resolves them for the DI smoke only). No session, heartbeat, handler or web-circuit path resolves them, and no playtest world ever resolves the `PlaytestWorldFactory` or `PlaytestRunner` in its own nested container. Every registration is stateless or creates fresh per-call state (the store and writer build per call; metrics, grader and builder are pure; the factory returns a new, self-confined world per call), so mere registration adds no shared mutable state. Kept in `Register` rather than scoped to run-mode/test compositions so the run mode, tests and PT-5 resolve one composer. **PT-5's web path** (the editor's run button) is the first non-run-mode caller and must declare its own INV-31 posture (web-circuit → background run, confinement of the world) in its plan.
- **The command outcome sink** is created per dispatch by `CommandDispatcher` and referenced only by that dispatch's `CommandContext`; it is confined to the dispatching flow (session or tick driver) and never shared.

### Persistence opt-in audit (INV-14 / INV-22 / INV-23)

- **Level 1 (domains):** no new entity construction. Bots and the setup actor are PT-1's persistent entities in the world's in-memory DB. World content is fresh-spawned from the scratch copy.
- **Level 2 (components):** no component added or reshaped. Scenarios, expectations, facts and reports are files or harness memory, never components.
- **Level 3 (save sites):** no new `SaveEntityAsync`. `RefuseAsync` edits `WearCommand`'s refusal sites only; its pre-existing post-equip save (acknowledged backlog debt, PT-1 audit) is untouched.

## Flows introduced or modified

*(INV-17. Reference the flow; never reproduce its diagram here.)*

- **[Flow 34](../architecture/flows/flow-34-playtest-world.md) — retitled "Playtest run journey (compose · boot · probe · setup · loop · record · grade · report)"** (WP3). New trigger: `dotnet run --project Server -- playtest --scenario <path>` (tests still use the world API directly). New steps follow Main flow 1–9: scenario load, content probe, the runner's drain → decide → enqueue → step loop, recorder facts at `Ai`, metrics → grade → report → write. Participants stay ≤ 7: RunMode/Runner, World, Driver, `IEventBus`, Recorder, Policy, Grader/Writer. The `flows/README.md` index row's title and trigger are updated.
- **[Flow 3 — Command journey](../architecture/flows/flow-03-player-command-lifecycle.md)** (WP2): body step 7 (execute) gains `CommandContext.RefuseAsync` → the per-dispatch `CommandOutcomeSink` → `CommandOutcome.Rejected` on the published `CommandExecutedEvent`; body step 8 (publish) changes from a hard-coded `Success` to "`Success`, or `Rejected` when the sink was marked"; the mermaid diagram's execute branch becomes an `alt` over `Success`, `Rejected` and `Threw`, adding the `Threw` edge the current diagram lacks (pre-existing drift), so body and diagram stay in step.
- **[Flow 29 — Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md)** (WP1): the Standards-page leg includes the playtest-expectations section.
- **Flows 01 and 33 (WP1 branch B only):** where they name a store's hand-rolled YAML load, they now cite the shared store. There is no behavior change.

## Test plan / Verification

*(INV-25; rubric in [07-testing.md](../architecture/07-testing.md).)*

| # | Tier | Target | Asserts | Post. |
|---|---|---|---|---|
| U1 | system-unit (temp dir) | `YamlDocumentFileTests`, `YamlDefinitionDirectoryTests` (branch B) | missing → `Exists=false`, no error; malformed → `Error`, no throw; write → read round-trip; null omission honored when opted in; `List` ordinal with filename fallback for unparseable files; `WriteAsync` sanitizes names and is atomic (no partial file on a cancelled write) | 1 |
| U2 | system-unit (existing) | `BalanceStandardsStoreTests`, `SimScenarioStoreTests`, `WorldManifestStoreTests` | existing cases **unmodified** and green after migration (U4 adds new cases to `BalanceStandardsStoreTests`) (behavior pin); plus a source guard that the three stores construct no YamlDotNet builder | 1 |
| U3 | system-unit | `PlaytestScenarioStoreTests` | the committed `content/dev-world/playtest/hub-smoke.yaml` loads to the expected model, its `world: ..` resolving to the absolute `content/dev-world` path regardless of cwd; a temp scenario's relative `standards:` resolves against its own directory; a scenario without `world:` → `World == null`; `List(dir)` over a temp directory and `List()` over the configured default; a file with every listed error throws once with **all** errors; `seedOverride` wins; `List` ordinal; `ReferencedBlueprintIds` returns every room/mob/item id the plan names | 2 |
| U4 | system-unit (temp dir) | `BalanceStandardsStoreTests` (extend) | `playtestExpectations` round-trips; absent → `Empty`; each Post. 4 validation error is reported; existing documents without the section load unchanged | 4 |
| U5 | system-unit | `BalanceStandardsRegistryTests` (extend) | `PlaytestExpectations.ThresholdFor(metric, null)` returns the global row; `(metric, band)` returns only that band's row (no fallback) | 4 |
| U6 | handler (initiator) | `CommandDispatcherTests` (**new**; no dispatcher test exists today) | with a recording bus and test commands, one case per outcome: **new behavior:** a command calling `RefuseAsync` → message written unchanged + `CommandExecutedEvent` with `Rejected`; a plain command → `Success`; a throw after `RefuseAsync` → `Threw` (throw wins); two consecutive dispatches don't share a sink (a refusal in the first leaves the second `Success`). **On-touch ratchet of existing outcomes:** an unknown verb → `ParseFailed`; a known verb with unparseable arguments → `ParseFailed`; an admin-gated verb from a non-admin → `Unauthorized`; an incapacitated invoker on a non-`UsableWhileIncapacitated` verb → `Refused` (and the command never runs); a direct `CommandContext` without a sink → `RefuseAsync` still writes, no throw | 6 |
| U7 | system-unit | `PlaytestMetricsSystemTests` | from synthetic fact lists: each Post. 7 metric (times = ticks × interval; per-hour on game hours; recovery mean; coin earned = awards + sales; coin to first upgrade = earned before the first bought-then-equipped item; rooms reached = distinct `ToRoom` + entry room; failed rate counts non-`Success` outcomes and unmet expectations); zero denominators → `null`; per-band values come from phase spans; a band-mismatched kill → warning; `stuckEvents`/`considerLabels` → `null` with reason | 7 |
| U8 | system-unit | `PlaytestMetricsSystemTests.Achievements_*` | A1–A7 achieved and not-achieved cases per the [definitions](#a-criteria-as-built-definitions), including: A1 fails on one unmet `movedTo`, on a `Rejected` command in the onboard phase, and when the first move does not start from the starting room; A4 fails when the bought item is never equipped; A6 fails without a post-respawn engagement; A8 → `Unavailable` | 8 |
| U9 | system-unit | `PlaytestGraderTests` | threshold met → pass, missed → fail, `null` value → fail "no data"; no threshold → no verdict; out-of-scope criterion → `null`; criterion = achievement ∧ linked thresholds; `Unavailable` → `null`; empty expectations + achieved A1 → exactly one verdict (A1 pass) and no metric verdicts | 9 |
| U10 | system-unit (temp dir) | `PlaytestReportBuilderTests`, `PlaytestReportWriterTests` | `schemaVersion 1`, `automated true`, no `DateTime`/`DateTimeOffset` property anywhere in the report graph (reflection); filename stem from the supplied write time + name + seed; `.summary.md` beside the JSON; atomic write | 10 |
| G1 | guard (source scan; extends `ArchitectureGuardTests`) | `CommandRefusalMessage_is_only_written_via_RefuseAsync` | precheck: the scan found ≥ 1 `.cs` file under `Core/` (and ≥ 1 `RefuseAsync` call), else fail loudly "scanned zero files"; then no `WriteAsync(new CommandRefusalMessage` in `Core/` outside `CommandContext.cs`. Known gap, accepted: a refusal built into a local and written via `Output.WriteAsync(r)` passes the text scan; the `add-command` skill and code review cover it, and a syntax-tree guard is the upgrade if it recurs | 6 |
| G2 | guard (source scan; extends `ArchitectureGuardTests`) | `Playtest_module_never_reads_event_wall_clock_or_ids` | precheck: ≥ 1 `.cs` file found under `Core/Modules/Playtest/`, else fail loudly; then no `.OccurredAt` / `.EventId` there | 5, 10 |
| G3 | guard (PT-1 T18, updated) | `Playtest_world_subscriptions_equal_live_plus_observers` | world subscriptions = the live `Register` + `SubscribeGameplayHandlers` set ∪ `{(T, PlaytestRecorder, 95)}` for exactly the 15 events; the live set contains no `Hedron.Core.Modules.Playtest` handler | 5 |
| G4 | guard (existing DI-smoke) | `CompositionRoot_resolves_all_…` | resolves the store, metrics, grader, writer, policies, `PlaytestRunner`, `IPlaytestEngineComposer` | — |
| H1 | handler | `PlaytestRecorderTests` | with a fake tick source and bot registry: each of the 15 events for a bot → one fact with the current tick and increasing `Seq`; the same events for a non-bot or the setup actor → nothing; `MobDiedEvent` with a bot killer → `Tier/Band` from the template; an unknown blueprint → fact with `null` band, no throw | 5 |
| U11 | system-unit | `AcceptanceWalkerPolicyTests` | over synthetic `Observation` JSON: onboard emits steps in order with their expectations, then advances; hunt emits `kill <keyword>`, emits nothing while engaged, re-kills after `combatNarration slain`, waits one step on `commandRefusal targetNotFound` (respawn), ends the phase at `kills` or `maxTicks`; buy emits `list` → `buy <keyword>` → `wear <keyword>`; seekDeath ends after `deathNarration respawned`; `CurrentPhase` tracks the plan; `Done` after the last phase; no read of the `text` property (G5) | 12 |
| G5 | guard (source scan; extends `ArchitectureGuardTests`) | `Policies_never_read_observation_text` | precheck: ≥ 1 `.cs` file found under `Core/Modules/Playtest/Policies/`, else fail loudly; then none reads `"text"` | 12 |
| F1 | flow (playtest world, extended PT-1 fixture) | `PlaytestRunnerTests` | a full walker plan (onboard → hunt B1 → collect → sell → buy+wear → seekDeath B3 → wait → hunt B1) → A1–A6 `Achieved`, A7 per the fixture, status `completed`; `maxTicks` too small → `budgetExhausted` with in-scope criteria failed for budget; a scenario naming an unknown mob and an unknown room → `PlaytestScenarioContentException` listing both, no bot entered (capturing composer), no report file | 3, 8, 11 |
| F2 | flow (determinism) | `PlaytestReportDeterminismTests` | the same scenario + seed twice in one process → byte-identical report JSON; a different seed over the fixture combat → different | 10 |
| F3 | flow | `PlaytestRunnerTests.Probe_*` | `Probe` returns kind, keyword and band for a mob, and the room entity id for a room; a miss → `Exists=false` | 3 |
| G6 | flow (CLI plumbing smoke) | `PlaytestRunModeTests` | `--validate --scenario <repo>/content/dev-world/playtest/hub-smoke.yaml` (repo-root-absolute, as P1; the test runs from the bin directory) → `0`, no report; a missing `--scenario` → `2`; `--validate` on a temp scenario (world = a scratch copy of dev-world) naming `mob.does.not.exist` and `room.does.not.exist` → exit `2`, both ids on stderr, no report file in the temp report dir; `--validate` with `--content <missing dir>` → exit `2` naming the path, no report; `--list --dir <temp>` prints the names | 13, 3 |
| G7 | **Tier 7 — playtest golden** | `DevWorldHubSmokeGolden` | Post. 14 over `content/dev-world/playtest/hub-smoke.yaml`: `completed`; A1 `true`; A2–A8 `null`; `Stopwatch` (boot → dispose) ≤ 10 s, failing with a message citing resolved decision 1 | 14 |
| G8 | guard (composition; extends `HostCompositionTests`) | `PlaytestRunMode_composes_no_hosted_service` | a `ServiceCollection` passed through `PlaytestRunMode.ConfigureServices(services, config)` has **zero** `IHostedService` descriptors (in particular no `TelnetServer`, `HeartbeatBackgroundService`, `PersistenceFlushTimer`), the `AddContentBootstrapHostedServices` assertion style | 13 |
| P1 | process (Tier 7 companion) | `PlaytestCliDeterminismTests` | runs `dotnet <Server.dll> playtest --scenario <repo>/content/dev-world/playtest/hub-smoke.yaml` twice, reports redirected to a per-test temp dir by setting `HEDRON_Playtest__ReportDirectory` **only on each child's `ProcessStartInfo.Environment`** (never `Environment.SetEnvironmentVariable`, which would leak into the parallel test process); each child also gets `HEDRON_Balance__StandardsPath` pinned to an empty temp path, so a developer's local `data/balance/standards.yaml` cannot add thresholds that flip A1; both exit `0`; report bodies are byte-identical; one run with `--content <missing dir>` → exit `2` and no report | 15, 3, 13 |
| U12 | system-unit (Web form model) | `StandardsFormTests` (`Hedron.Tests/Web/`) | a `BalanceStandardsDocument` with a non-empty `PlaytestExpectations` (global + one band row) → `FromDocument` → edit an unrelated field (e.g. a band power target) → `ToDocument` → `PlaytestExpectations` equal to the original and the edited field changed; a document without the section → `Empty` after the round-trip; an edited expectations cell round-trips | 4 |

**Skipped (with reason):**
- `Standards.razor` markup (presentation; the mapping is U12 over `StandardsForm`, the parse/validation is U4).
- The `.summary.md` prose and console summary (presentation).
- `PlaytestRunMode` argument parsing beyond G6 (thin plumbing).
- The pure-data records (`PlaytestScenario`, `PlaytestFact`, `PlaytestVerdict`, `DefinitionProbe`, `PlaytestReport`).
- `ObservationView` on its own (covered through U11).
- `CommandLoggingHandler`'s rendering of `Rejected` (one log template; no decision).

**Testability notes:**
- Every system here is pure over records (metrics, grader, builder) or temp-dir I/O (stores, writer). The walker is pure over observation batches.
- Time is ticks; chance is the world's seeded random. No new seam is needed.
- Wall time appears only in the report filename (injected `writtenAtUtc`) and the golden's `Stopwatch`.
- **Golden budget design (≤ 10 s):** 1 bot, 8 commands, `maxTicks: 40`, a three-room world. The run ends when the walker is `Done` (about 9 steps). The expected cost is dominated by container build + content load + two PBKDF2 hashes (well under 3 s locally), which leaves headroom on slow CI runners. P1's two processes are a separate test with their own budget (about 2 × process start).

## Design notes

*(Slice rationale. The seam reasoning lives in the program's [Design notes](playtest-automation-program.md#design-notes) and [brief](playtest-automation-program.md#architecture-brief).)*

### Resolved decisions (PT-2 planning)

1. **Golden runtime budget: the per-PR smoke golden runs in ≤ 10 s wall time** inside the normal `dotnet test` run. *(User, 2026-10-30; resolves the program's "Golden runtime budget" open question.)* The smoke scenario is sized to it (one bot, about 9 steps, a 40-step budget, the three-room hub skeleton), and G7 asserts it. The Phase 5 acceptance golden (slice 9) inherits the same ceiling: it must size its A1–A8 plan to ≤ 10 s, or split, or move the long tail to advisory sweeps (R15).
2. **Business refusals report a distinct command outcome in this slice.** PT-2 lands `CommandContext.RefuseAsync` + the new outcome over PT-1's 24 typed refusal sites, guarded by G1 (WP2), so R10's failed-command rate is measured from events. *(User, 2026-10-30; rejected: deferring to PT-3 and counting only framework failures.)* **Naming (spec-review follow-up, not a change of substance):** the value is `CommandOutcome.Rejected`, not `Declined`, so it does not collide with PT-1's `RefusalKind.Declined` (one refusal kind among twelve); `command-framework.md` documents `Refused` (dispatcher gate) vs. `Rejected` (command-written refusal). The outcome is carried by a dispatcher-owned per-dispatch `CommandOutcomeSink`, not a flag on the immutable `CommandContext` record.
3. **A8 depends on PT-3.** PT-2 reports A8 `Unavailable`; the program's acceptance-golden row now depends on PT-2, **PT-3**, and drops B1–B3. *(User, 2026-10-30; rejected: a minimal no-progress window in PT-2.)*
4. **`consider` labels per band (R10, A7) are reserved `Unavailable` in PT-2.** Phase 5 slice 5's plan adds a thin `MobConsideredEvent(PlayerEntityId, MobBlueprintId, DangerLabel)` published by `ConsiderCommand` (an Initiator), and the recorder subscribes in that slice. *(User, 2026-10-30.)*
5. **Dev-world acceptance thresholds get a committed home** at `content/dev-world/balance/standards.yaml`, referenced by the dev-world scenarios' `standards:` field and by `appsettings.DevWorld.json`'s `Balance:StandardsPath`, authored on the Standards page under the `DevWorld` profile. PT-2 needs no values (the smoke has no thresholds); slice 9 / the B1 drop authors them. *(User, 2026-10-30; a dev-world content-policy touch owned by that slice.)*
6. **The cross-process determinism test (P1) runs in every PR's `dotnet test`** (about 2–5 s, separate from the ≤ 10 s smoke budget). *(User, 2026-10-30; rejected: tagging it out to per-drop runs.)*
7. **Committed dev-world playtest scenarios live beside their world**, e.g. `content/dev-world/playtest/hub-smoke.yaml`, consistent with decision 5's `content/dev-world/balance/standards.yaml`. `Playtest:ScenarioDirectory` (`data/playtest/scenarios/`, gitignored) stays the home for operator scenarios. No `.gitignore` un-ignore is added. Consequences: `--scenario` takes any path; paths inside a scenario resolve against the scenario file's directory (`world: ..`); `--list` takes an optional `--dir`; `world-content.md`'s ownership table records that dev-world owns its scenarios and standards. *(User, 2026-10-30; resolves the spec review's content-policy finding: committed scenarios under `data/` would have widened the `/data/*` exception.)*

### Runner vs. world: who knows what

- The **world** keeps R4: policies receive only `Observation` batches and the probed nouns their plan names.
- The **runner** is a harness Initiator. It may read the world's measurement surfaces (`Record`, `Probe`, `Info`), but it never hands them to a policy.
- Step expectations and phase spans are harness facts (the scenario author's declared intent), so grading against them doesn't contradict "measurement comes from events": the *outcome* side of every comparison is an event fact.
- One command per bot per step makes `(bot, tick)` an exact correlation key. A command's effect events and its `CommandExecutedEvent` share the step's tick, because the dispatcher publishes after execution and before the tick.

### A-criteria as-built definitions

*(An adopted default. Slice 9 may tighten these without a schema break; the linked metrics are each criterion's thresholdable numbers.)*

| Criterion | Achieved iff (from facts + phases) | Linked metrics (graded only if a threshold is stated) |
|---|---|---|
| A1 | the bot's first `Moved.from` (or, with no move, its entry) is `Info.StartingRoomEntityId`; **every** onboard-phase step met its `expect`; the onboard phase has no non-`Success` outcome | `failedCommandRate` |
| A2 | a `Killed` fact in a band-1 hunt phase (template band 1), followed in the same tick by `XpAwarded(CombatKill)` and `CoinAwarded` | `gameTimeToFirstKill` |
| A3 | ≥ 1 `Improved` | `gameTimeToFirstImprovement`, `killsToFirstImprovement`, `killsPerImprovement` |
| A4 | a `Bought(item)` with `coinEarned` before it ≥ its price, then `Equipped(item)` | `coinToFirstUpgrade` |
| A5 | a `PickedUp(item)` and later a `Sold(item)` of the same item entity | `coinEarned` |
| A6 | `Died` during a band-3 seekDeath phase, then `Respawned(room == StartingRoomEntityId)`, then an `Engaged` | `recoveryTime` |
| A7 | every band 1–3 has ≥ 1 fight, and `lossRate` is non-decreasing B1 → B2 → B3 with B3 > B1. The `consider` leg is `Unavailable` until the slice-5 `MobConsideredEvent` lands (resolved decision 4) | per-band `lossRate`, `deathsPerHour` |
| A8 | `Unavailable` in PT-2 (resolved decision 3) | `stuckEvents` |

Respawn-room equality uses the starting room because dev-world's respawn point is the hub start (Phase 5 R15). A world whose respawn differs would need a manifest respawn field; slice 1 explicitly leaves that unbuilt.

### Why the walker is a plan executor

The A1–A8 loop is content-shaped: routes, targets, shop items. Hard-coding dev-world knowledge into C# would couple the engine to a fixture and break on every content drop. The walker therefore executes a **declared plan** (phases + steps), and the content knowledge lives in the scenario YAML beside the world it walks. The C# owns only generic reactions to typed observations (engaged/slain/refused/respawned). It is a stateful run object (a cursor through the plan); the `ISimCombatantPolicy` "stateless where possible" precedent is honored by keeping `IBotPolicy` itself stateless and putting per-bot state in `IBotPolicyRun`.

### Expectations: no fallback, no defaults

`ThresholdFor(metric, band)` never falls back from band to global, because the two describe different measurements (a band's loss rate vs. the run's). The compiled default is `Empty`, so CI grades nothing numeric until a designer states a value (R12; the "missing threshold never fails CI" edge case). `OutcomeTolerances` fills defaults, so it is the wrong model to copy here: an unstated playtest expectation must stay unstated.

### Smoke golden scope

The hub skeleton has no mobs, shop or found items, so only A1 is in scope. A2–A8 are asserted `null` ("not in scenario scope"), which pins the grader's non-invention rule on real content. The golden widens to the full loop as Phase 5 drops land; slice 9 owns that.

## Open questions

None open. The planning forks and the spec review's content-policy fork were resolved by the user on 2026-10-30; see [Resolved decisions](#resolved-decisions-pt-2-planning) 1–7.

**Backlog entries (firm deliverables of this slice):**
- "Event-carried opponent attribution for playtest metrics: add the opponent blueprint (or `(Tier, Band)`) to `CombatStartedEvent` when a playtest policy fights outside a declared phase (PT-3 explorer/grinder). Until then, per-band fights/losses/deaths are attributed by the walker's declared phase band, with a mismatch warning."
- Amend "Output structuring ratchet" (PT-1): each converted refusal site uses `context.RefuseAsync` so it reports `Rejected`.
- If WP1 branch B lands: amend "YAML-authored definition pipeline" (extraction done in PT-2; manifest debt closed).

## Related

- [`playtest-automation-program.md`](playtest-automation-program.md): the PT-2 row; R1, R7, R10–R14, R25; brief resolved decisions 1–4.
- [`playtest-world-substrate.md`](playtest-world-substrate.md): PT-1; the world API, tick driver, agent formatter, typed refusals, T18/T28 updated here.
- [`dev-world-foundation.md`](../roadmap/completed/dev-world-foundation.md): Phase 5 slice 1; `world.yaml`, the hub skeleton the smoke walks, `RunModeConfiguration`, `WorldManifestStore` (migrated in branch B).
- [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md): R25–R29 (A1–A8), slice 4 (`help-topics`, the YAML store), slice 5 (`consider`), slice 9 (the acceptance golden).
- [`spawn-authoring.md`](spawn-authoring.md): spawn slots the walker's hunt phase waits on.
- Features: [`simulation/simulation.md`](../features/simulation/simulation.md) (run-mode, store and report precedents) · [`commands/commands.md`](../features/commands/commands.md) · [`output/output.md`](../features/output/output.md)
- Design: [`balance.md`](../design/balance.md) (rule 4, rule 5, the standards document)
- Architecture: [`07-testing.md`](../architecture/07-testing.md) (Tier 7) · [`checklist.md`](../architecture/checklist.md) (INV-5, 8, 10, 12, 17, 18, 19, 22, 25, 26, 31)
- Backlog: "YAML-authored definition pipeline"; "Output structuring ratchet"; the `WearCommand` save-on-change entry.
