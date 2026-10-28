# Playtest automation — agentic harness (program)

- **Status:** planned (pre-refinement strategy seed — awaiting `/refine`, then `/advise`)
- **Actors:** System (headless harness, scripted bots, LLM persona agents), Administrator (designer reading reports), Player (simulated)
- **Module:** to be placed by the advisor. Likely a headless `playtest` run mode in `Server/` beside `simulate`/`generate`, with harness pieces in a new `Core/Modules/Playtest/` or a `Hedron.Playtest` project. Agent-side tooling goes in `.claude/`. Feature home TBD (likely a sibling of [`features/simulation/`](../features/simulation/simulation.md)).

**Description.** Automated play that measures the *player experience and outcomes* the existing test and balance tooling can't see, so the maintainer doesn't have to playtest by hand for long stretches. Two layers:

- **Scripted bots** run in a headless, fast-forwarded world and report measurable outcomes: pacing, progression rate, deaths, economy flow, stuck states.
- **LLM persona agents** play goal-directed sessions over the text interface and report qualitative friction: confusion, dead ends, unclear feedback, exploits.

Both run against seeded, replayable worlds and report against designer-stated expectations. This is a Phase 5 program: the functional-MVP acceptance test ("a stranger can fight, progress, buy/sell, die and recover without admin help") is meant to be *executed by bots*, not walked by hand.

> **Provenance.** Strategy drafted 2026-10 from a status-and-automation review; nothing below has been through `/refine` or `/advise` yet. It is grounding plus proposals, not decided scope. The [Decisions to settle](#decisions-to-settle-at-refine--advise) section lists what the intake must resolve.

## Why: the coverage gap

| Tier | Who plays | Answers | Status |
|---|---|---|---|
| 0 | xUnit suite (system/handler/flow/persistence/guard) | Defects, regressions | ✅ live, CI-enforced (INV-25) |
| 1 | Balance simulator ([`features/simulation/`](../features/simulation/simulation.md)) | Combat numbers per (Tier, Band) cell, analytical progression rate, template conformance | ✅ live |
| **2** | **Scripted bots, headless world, stepped time** | Measured outcomes: XP/hour, kills per improvement, deaths/hour, time-to-respawn, currency in/out, rooms reached, stuck/no-progress detection, unreachable content, failed-command rate | **new** |
| **3** | **LLM persona agents** | Qualitative experience: discoverability, clarity of feedback, dead ends, "felt unfair", exploits | **new** |
| 4 | The maintainer | Fun and feel. Targeted, replaying flagged moments from a seed | manual, reduced volume |

The simulator is deliberately scoped to isolated sandbox fights. By its own design ([`design/balance.md`](../design/balance.md), [`features/simulation/`](../features/simulation/simulation.md)) it can't see:

- navigation and travel between fights
- kill-loop pacing and respawn timing
- the loot → shop → gear economy loop
- death and recovery friction
- multi-mob fights
- use-based XP accrual
- command discoverability

Those are exactly what the Phase 5 acceptance test asks about, and what manual playtesting currently covers.

## Grounding — what already exists to build on

*Verified against code at drafting time; the advisor re-checks.*

| Seam | Where | Harness use |
|---|---|---|
| `ISession`: five members, transport-neutral | `Core/Sessions/ISession.cs`; test `StubSession` | A socketless `BotSession` |
| `ICommandDispatcher.DispatchAsync(session, line)` | `Core/Commands/` | Bots act through the real parse/gate/command path |
| `CommandExecutedEvent(Invoker, Verb, Args, Outcome)` | `Core/Commands/Events/` | Free per-action telemetry: failed-command and discoverability rates |
| `IVerbRegistry` (+ `ArgumentSchema`/`Usage`), `IAbilityVerbResolver` | `Core/Commands/`, `Core/Modules/Abilities/` | The action space, which becomes LLM tool definitions |
| Typed output messages (`RoomDescriptionMessage`, `PromptMessage`, `ScoreDisplayMessage`, …) + per-transport `IOutputFormatter` | `Core/Output/` | Structured observations via an `"agent"` transport key |
| `CompositionRoot.Register` (pure DI) without `AddGameplayHostedServices` | `Server/CompositionRoot.cs` | Compose the engine with no telnet listener or wall-clock heartbeat; `simulate`/`generate` already do this |
| `HeartbeatTickEvent` + `IClock` + `IRandom` (INV-26) | `Core/Modules/Time/`, `Core/Systems/` | Stepped, fast-forwarded time; one seed replays a whole session |
| `IEventBus.Subscribe<T>`, reserved `HandlerPriority.Ai = 95` | `Core/Events/` | A metrics recorder that observes without mutating |
| Outcome events: combat start/round/end, `MobDiedEvent`, `ExperienceAwardedEvent`, `TrackImprovedEvent`, `AscendedEvent`, `CurrencyAwardedEvent`, `ItemBoughtEvent`/`ItemSoldEvent`, `PlayerIncapacitatedEvent`/`PlayerDiedEvent`/`PlayerRespawnedEvent`, `PlayerMovedEvent`, `AbilityActivatedEvent` | feature modules | The report's raw signal |
| `ISimCombatantPolicy` (stateless decision policy) + per-run `SandboxWorld` confined to one thread | `Core/Modules/Simulation/Systems/` | Precedent for a bot "brain" interface and for one-world-per-thread parallelism |
| Admin verbs (`teleport`, `setplayer`, `setwallet`, `spawn`, `ascend`) | `Core/Modules/Admin/` | Scenario setup: start a bot at Tier N, near a shop, and so on |

## Known gaps

*Ranked by how much they block.*

1. **No content to play.** The repo ships no game world ([content ownership](../features/world/world-content.md#content-ownership--the-engine-ships-no-game-world)). `content/dev-world/` is an empty fixture, and the `generate` CLI produces no shops, currency loot or respawn points. Bots need a world; this couples the program to the Phase 5 content work.
2. **Gameplay handler wiring lives in `Server/Program.cs` `Main`** (~69 `Subscribe` lines), not in a reusable composition method. A harness would have to copy it and the copy would drift. Extracting it is the INV-19 framework piece.
3. **No world-boot helper.** Nothing loads content, runs bootstraps, and connects a player without hosted services. `PersistenceTestHarness` (in-memory SQLite) is a starting point.
4. **Much combat and death output is still `PlainMessage` prose,** and `IOutputFormatter.Format` returns `string`. A JSON agent formatter would also be the structured-output path [`design/client-tier.md`](../design/client-tier.md) already calls for, so it isn't throwaway work.
5. **Output is buffered per tick** (`SessionOutputBuffer`), so an agent needs an explicit turn boundary (flush + prompt). `LoginFlow` is bound to a `StreamReader`.
6. **Concurrency.** `ComponentRepository` is unguarded, and the world-state threading decision is open ([`backlog.md`](../roadmap/backlog.md), INV-31). Many bots in one live world, over telnet or otherwise, widen that race.

## Proposed approach

*Proposals for the advisor to accept, revise or reject.*

- **Headless `playtest` run mode,** modelled on `simulate`: `dotnet run --project Server -- playtest --scenario <yaml> --seed N`. An INV-10 Initiator that composes DI, runs, writes a JSON report and exits.
- **One world per thread; parallelism comes from many worlds.** Each `PlaytestWorld` gets its own `EntityService`, `SeededRandom` and stepped clock, confined to one thread. This mirrors `SandboxWorld` (INV-12 nuance, INV-31 "confine" posture). It sidesteps the threading decision rather than waiting on it, and it doubles as evidence for the "marshal" option there.
- **A tick driver as the single writer.** Each step dispatches every bot's queued command, then publishes `HeartbeatTickEvent` and advances the clock by the same `Elapsed`.
- **`BotSession` + agent output formatter,** plus an **`IBotPolicy`** (observation in, command line out) modelled on `ISimCombatantPolicy`:
  - Scripted policies: explorer, grinder, shopper, die-and-recover, and a newcomer that never reads `help`.
  - An LLM policy adapter: the tick pauses while the model thinks, so model latency never distorts game time.
- **A playtest recorder** that subscribes to events at the `Ai` priority (handler-only, INV-5) and writes a report beside the sim reports.
- **Outcome expectations live in the standards document, not in tests.** This is [`design/balance.md`](../design/balance.md) maintenance rule 4. The harness grades runs against designer-stated tolerances: time to first kill, deaths per hour at band, a currency floor after N kills, and so on. It never invents expectations: an ungraded metric is reported descriptively, the sim-4 precedent.
- **CI:**
  - a small seeded golden playtest on the dev-world fixture (INV-25);
  - larger sweeps on demand or per content drop;
  - verdicts stay advisory, like band-drift audits.
- **LLM persona layer (Tier 3):**
  - **Tool surface:** the harness is exposed as a small tool surface, e.g. an MCP server with `look` / `act(command)` / `status` / `wait(ticks)`.
  - **Personas** are `.claude/agents/`: confused newcomer, min-maxer, explorer, economy exploiter, impatient player. Each gets a goal, a command budget and behavioral constraints (no `help` for the first N actions, limited memory), and returns structured findings with seed + tick for replay.
  - **Analyst:** a `playtest-analyst` agent deduplicates findings across runs, ranks them, routes them to the backlog, and diffs against the previous run.
  - **Calibration before trust:** seeded-defect scenarios check that personas actually report known-bad content, e.g. a room with no exits, a 10×-priced shop, an unwinnable mob.
  - **Limits:** LLM players are patient and literal. They're good for clarity, dead ends and exploits, and weak for "is this fun", so the maintainer keeps Tier 4 for feel.
- **Content tooling (INV-18):**
  - the scenario YAML;
  - a dev-world fixture sized to the acceptance loop (generic: a few rooms, one shop, a handful of banded mobs, respawn points);
  - possibly `generate` profile support for shops, loot and respawns.

## Suggested sequencing inside Phase 5

Interleave the harness with content; don't put it first.

1. **Phase 5 kickoff (`/refine` → `/advise`).** Write the acceptance test as bot-checkable expectations. Decide which world it runs against (see below).
2. **Substrate slice:**
   - the handler-wiring extraction;
   - `PlaytestWorld` + tick driver + `BotSession` + agent formatter + recorder;
   - one scripted policy that walks the acceptance loop;
   - a seeded CI golden.
3. **A minimal dev-world fixture** for that loop, then scripted persona policies, the sweep runner, and the first authored outcome expectations.
4. **The LLM layer:** tool surface, persona agents, analyst, and seeded-defect calibration.

## Decisions to settle at `/refine` / `/advise`

- **Reopening the "no autonomous sweeps" decision.** The prog-5 close-out ([`completed/balance-doc-layer.md`](../roadmap/completed/balance-doc-layer.md)) ruled that running sim sweeps is a designer/admin surface, "not a step an agent takes autonomously in the dev loop". Playtest agents run sessions autonomously by design, so the maintainer needs to explicitly reopen or bound that ruling. One option: agents run playtests and report; humans decide tuning changes.
- **Which world the acceptance test runs against:** the operator's private world (not in the repo, so not usable in CI) or the generic `content/dev-world/` fixture. Possibly both, with the fixture as the CI golden.
- **How LLM play is paid for:** Claude Code subagents over a local tool surface (cheap to start, interactive), or headless batch runs through the Claude API (scales, costs per run). The choice affects where the persona layer lives.
- **Where qualitative findings live.** The standards document holds numeric tolerances only. Persona findings need a home and a triage path (backlog entries? a findings log beside reports?).
- **Relationship to the backlogged balance-reviewer agent** ([`backlog.md`](../roadmap/backlog.md)): fold it into the analyst role, or keep it separate.
- **Relationship to the world-state threading decision:** confirm the confine-per-world posture is sufficient, and that live-server telnet bots stay out of scope until that decision lands.
- **Anti-automation tension:** [`design/feature-horizon.md`](../design/feature-horizon.md) lists anti-cheat/automation detection as a future feature. Harness sessions should be clearly identifiable so that later work doesn't fight the harness.

## Constraints to respect

- **No second combat model, and the real systems throughout.** The harness drives real commands and systems; it doesn't reimplement rules (the sim program's rule).
- **INV-26.** No `DateTime.Now` or `Random.Shared`; all time and chance come through the stepped clock and `IRandom`.
- **INV-31.** Declare the confine posture; no new thread mutates a live world's components.
- **INV-5.** The recorder and policies observe and act through events and commands; systems stay bus-free.
- **INV-18.** Each slice ships the tooling to author and inspect what it adds: scenarios, expectations, fixtures.
- **Content ownership.** Fixture content stays generic and lives in `content/dev-world/`; operator worlds are never committed.
- **Advisory, not gating.** Audits and playtest verdicts never become hard CI gates beyond the thin pinned golden subset.
