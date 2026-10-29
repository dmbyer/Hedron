# Playtest automation — agentic harness (program)

- **Status:** planned (requirements seed — awaiting `/advise`)
- **Actors:** System (headless harness, scripted bots, LLM persona agents), Administrator (designer reading reports), Player (simulated)
- **Module:** to be placed by the advisor. Likely a headless `playtest` run mode in `Server/` beside `simulate`/`generate`, with harness pieces in a new `Core/Modules/Playtest/` or a `Hedron.Playtest` project. Agent-side tooling goes in `.claude/`. Feature home TBD (likely a sibling of [`features/simulation/`](../features/simulation/simulation.md)).

**Description.** Automated play that measures the *player experience and outcomes* the existing test and balance tooling can't see, so the maintainer doesn't have to playtest by hand for long stretches. Two layers:

- **Scripted bots** run in a headless, fast-forwarded world and report measurable outcomes: pacing, progression rate, deaths, economy flow, stuck states.
- **LLM persona agents** play goal-directed sessions over the text interface and report qualitative friction: confusion, dead ends, unclear feedback, exploits.

Both run against seeded, replayable worlds and report against designer-stated expectations. This is a Phase 5 program: the functional-MVP acceptance test ("a stranger can fight, progress, buy/sell, die and recover without admin help") is meant to be *executed by bots*, not walked by hand.

> **Provenance.** Strategy drafted 2026-10 from a status-and-automation review, then refined through `/refine` on 2026-10-29. The **Requirements** section below is decided scope. The sections after it (coverage gap, grounding, known gaps, proposed approach, sequencing, constraints) are the original **pre-refinement strategy notes**, kept as input for `/advise`: proposals, not decisions.

## Requirements

*(In-flight requirements tier, produced by the `requirements-detailing` intake on 2026-10-29. The advisor extends this file with the architecture brief and slice decomposition; the planner(s) absorb the relevant R-numbers per slice. Cite items as R-numbers. The pre-refinement strategy sections further down are advisor input, not decided design.)*

### User stories

- *As the maintainer*, I want a seeded bot to walk the whole functional-MVP loop on dev-world and grade it against my thresholds, so I know the game is playable without sitting through a long manual playtest.
- *As the maintainer*, I want scripted bots that explore, grind, shop, die and stumble like a newcomer, and report pacing, economy, death-friction and stuck-state numbers per band. That way every content drop is checked against the outcomes I expect.
- *As the maintainer*, I want LLM persona agents to play goal-directed sessions and tell me where the game confused, stalled or exploited them, with a seed and tick I can replay. Then I spend my own play time on fun and feel, not on finding dead ends.
- *As a designer*, I want to write a playtest scenario (world, bots, seed, length, starting conditions) as YAML, run it from a CLI, and read a JSON plus human-readable report. Later I want to do the same from the editor.
- *As a designer*, I want outcome thresholds kept in the standards document, so a metric with no threshold is reported but never invents a pass or fail of its own.
- *As the analyst agent*, I want findings from every run in one ledger: deduplicated, ranked, and diffed against the previous run. Nothing is promoted to the backlog or a slice without the maintainer's approval.
- *As CI*, I want one small seeded acceptance golden on every PR that fails only when the loop breaks, not when a content drop moves a number.

### Scope

**In scope:**
- **A headless playtest run mode** over the real engine and dev-world content: seeded, with stepped and fast-forwarded time, and several bots per world (R1–R6).
- **Scripted bots:** six behaviours, including the A1–A8 acceptance walker (R7–R9).
- **Outcome metrics and grading** against a new standards-document family (R10–R13).
- **The CI acceptance golden plus advisory sweeps** (R14–R15).
- **LLM persona agents run from Claude Code** over a local tool surface, with calibration before their findings are trusted (R16–R20).
- **A findings ledger with an analyst agent,** which also absorbs the backlogged balance-reviewer role (R21–R24).
- **Authoring and inspection:** scenario YAML + CLI first; a Blazor Playtest page later in this program (R25–R26).
- **Amending the agent-autonomy ruling** (R27).

**Out of scope (confirmed exclusions):**
- **Bots on the live telnet server.** This waits for the world-state threading decision in the [backlog](../roadmap/backlog.md); harness worlds are separate and headless.
- **A headless Claude API batch runner for personas.** It remains a later option behind the same tool surface.
- **Judging fun or feel.** That stays with the maintainer (Tier 4).
- **Agents making changes.** No agent changes tuning, content or code on its own; it reports, and the maintainer decides.
- **A Blazor Playtest page in the first slice.** It lands later in this program.
- **Gating the Phase 5 MVP on persona findings.** The MVP is gated by the scripted acceptance golden.
- **A second combat or rules model.** The harness drives the real commands and systems.

### Behavioral requirements

**A. Harness and run mode**
- **R1.** A designer runs `playtest` headless from the CLI with a scenario file and an optional seed override. The run boots the configured world (dev-world by default, through its `world.yaml` per the [Phase 5 brief](phase-5-content-baseline-program.md)), runs the scenario to completion, writes a report, and exits. No telnet listener and no wall-clock heartbeat.
- **R2.** Time in a playtest world is **stepped and fast-forwarded**: a session of hours of game time completes in a small fraction of that in real time. All chance and time inside the world come from the run's seed and stepped clock.
- **R3.** **Determinism:** the same scenario, seed and content produce an identical report.
- **R4.** Bots act **only through the real player command path** and see only what a player would see: room, prompt, and command output, as structured observations. Nothing reaches into world state directly.
- **R5.** A scenario can place **N bots in one world** (default 1), each with its own character and behaviour, so contention can be tested (shared spawns, shop stock).
- **R6.** Scenario **setup can position bots**: create a fresh character, or start one with given progression, coin, gear or location, so a run can begin mid-game. Setup uses the existing admin and authoring verbs, never ad-hoc state edits.

**B. Scripted bots**
- **R7.** Six built-in scripted behaviours:
  - **acceptance walker** — the Phase 5 A1–A8 loop;
  - **explorer** — maximise rooms visited; detect unreachable rooms and dead ends;
  - **grinder** — hunt one band and measure the progression rate;
  - **shopper** — find, sell, earn and buy upgrades;
  - **die-and-recover** — deliberately over-reach and measure death friction;
  - **newcomer** — never types `help`, and relies only on room text, prompts and output, to test whether the core verbs can be discovered.
- **R8.** **Stuck and no-progress detection:** a bot that makes no measurable progress (no new room, XP, coin or kill) for a configurable window is reported as stuck, with seed and tick. A temporarily empty, respawning area counts as waiting, not stuck.
- **R9.** Every bot action and its outcome are recorded, so a finding can name the exact command and tick where it happened.

**C. Metrics and grading**
- **R10.** Each run reports, per bot and per band where relevant:
  - time and kills to the first kill and to the first improvement;
  - kills per improvement;
  - XP per hour;
  - deaths per hour;
  - time to recover after death;
  - coin earned and coin spent;
  - coin to the first upgrade;
  - rooms reached;
  - failed-command rate;
  - stuck events;
  - the `consider` labels seen per band.
- **R11.** Outcome thresholds live in a **new standards-document family** (playtest expectations), authored on the existing Standards page / standards file. This program owns that family; the Phase 5 program states the values as each band lands.
- **R12.** A metric with a threshold is **graded pass/fail**. A metric without one is **reported descriptively**, with no verdict. The harness never invents an expectation.
- **R13.** Reports are **JSON plus a human-readable summary**, written beside the simulation reports, so they can be kept, diffed or discarded the same way.

**D. CI and sweeps**
- **R14.** One small seeded **acceptance golden** runs on every PR against dev-world. It asserts the **graded pass/fail of each A1–A8 criterion** at a pinned seed, never exact metric values. A content drop re-pins it only when it breaks the loop.
- **R15.** Larger sweeps (many seeds, every bot type, several bots per world) run **on demand and per content drop**, and are advisory: they never block a merge.

**E. LLM persona agents**
- **R16.** Personas run **from Claude Code** as agents driving a **local tool surface** onto a playtest world: look, act (type a command), status, and wait (advance N ticks). The world is paused while the agent thinks, so model latency never distorts game time.
- **R17.** Five personas: **confused newcomer**, **min-maxer**, **explorer**, **economy exploiter**, **impatient player**. Each declares a goal, a command budget and behavioural constraints (for example, no `help` for the first N actions, or limited memory).
- **R18.** A persona run returns **structured findings**: category (confusion, dead end, unclear feedback, exploit, perceived unfairness), severity, a description, and the seed + tick + command to replay it.
- **R19.** **Calibration before trust:** a set of seeded-defect scenarios (a room with no exits, a 10×-priced shop, an unwinnable mob, at minimum) must be reported by the relevant personas before persona findings are treated as signal. Calibration is re-run when a persona definition changes.
- **R20.** Persona findings are **advisory** and do not gate the Phase 5 MVP.

**F. Findings and the analyst**
- **R21.** Every run (scripted or persona) writes its findings, with replay coordinates, beside its report.
- **R22.** An **analyst agent** deduplicates and ranks findings across runs into a **findings ledger**, and diffs each run against the previous one (new, resolved, recurring).
- **R23.** The analyst **promotes** a finding to a backlog entry or a gap slice **only with the maintainer's approval**.
- **R24.** The analyst also covers the backlogged **balance-reviewer** role. When a slice changes balance numbers or banded content, it runs the relevant sim sweep and playtest checks and reports outliers against the standards. That backlog entry closes into this program.

**G. Authoring and inspection**
- **R25.** Scenarios are **YAML**: world, bots and behaviours, seed, duration, setup, and which standards family to grade against. They are inspectable through the CLI and their reports. Example scenarios ship in the repo, as the sim's do.
- **R26.** A **Blazor Playtest page** lands in a later slice of this program: compose and launch scenarios, view reports, and browse the findings ledger. This mirrors how the Simulation page followed the `simulate` CLI.

**H. Policy**
- **R27.** The 2026-07-17 ruling ([`completed/balance-doc-layer.md`](../roadmap/completed/balance-doc-layer.md)) is amended from "agents never run sweeps" to "**agents may launch playtest runs and the sweeps they need, and report; every tuning, content or code change a finding implies is decided by a human**". The policy text and the `balance-tuning` skill are updated in the first slice.
- **R28.** Harness sessions are **identifiable** as automated (in reports, logs and any session listing), so future anti-automation work can tell them apart.

### Edge cases & failure behavior

- **A bot's command fails:** it is recorded as a failed command (counted in R10). The bot's behaviour decides whether to retry or move on; a failure never crashes the run.
- **A bot dies mid-scenario:** normal soft death and respawn apply. The die-and-recover metrics capture it.
- **A scenario names missing content** (an unknown world, room or mob): the run fails fast before starting, with a clear error and no partial report. *(Adopted default.)*
- **A run exceeds its duration or command budget:** it ends and reports what it measured, with "budget exhausted" marked.
- **A persona breaks its constraints** (for example, uses `help` too early): the run records the violation in its findings and continues.
- **Several bots compete for one spawn or item:** this is real contention, with first-come resolution through the normal command path, and is reported as such.
- **The golden fails because a criterion has no threshold yet:** criteria without a threshold are reported, not graded (R12), so a missing value never fails CI.

### Content & authoring needs

- Scenario YAML (R25) and example scenarios.
- The playtest-expectations standards family (R11), authored on the existing Standards page.
- The seeded-defect calibration scenarios and their small broken fixtures (R19). These are generic and committed with the harness, kept apart from the dev-world baseline.
- Persona definitions (goal, budget, constraints) as `.claude/agents/` files.
- The findings ledger format, readable by the maintainer and the analyst.

### Grounding notes

- **Exists:**
  - the simulator's headless run mode, YAML scenarios, JSON reports and Blazor page ([`features/simulation/`](../features/simulation/simulation.md));
  - the standards document and Standards page ([balance.md](../design/balance.md));
  - the command framework, help, and output messages ([`features/commands/`](../features/commands/commands.md), [`features/output/`](../features/output/output.md)).
- **Consumes:** the Phase 5 program's acceptance contract R26–R29 (A1–A8) and its advisor call on golden churn ([`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md)). It also needs that program's slice 1 (dev-world foundation, `world.yaml`) before a bot can boot a world.
- **Absorbs:** the backlog's balance-reviewer agent (R24).
- **Collides with (both resolved):**
  - the 2026-07-17 no-autonomous-sweeps ruling, amended by R27;
  - the horizon's anti-cheat/automation detection, covered by R28.
- **Depends on, but stays clear of:** the world-state threading decision. Headless harness worlds sidestep it; live-server bots stay out of scope until it lands.

### Resolved decisions

*(Confirmed by the user during intake on 2026-10-29. Do not relitigate.)*

1. **Autonomy:** agents may run playtests and sweeps and report; humans decide every change. Rejected: agents proposing fix PRs; keeping the ruling unchanged.
2. **LLM layer timing:** in Phase 5, after the scripted bots and the golden, and **not gating** the MVP. Rejected: persona-gated MVP; deferring personas to Phase 6.
3. **LLM runtime:** Claude Code agents over a local tool surface, run on demand. Rejected for now: a headless API batch runner from day one; building both.
4. **Findings:** a findings log beside each run, plus an analyst-maintained ledger; promotion to the backlog or slices needs maintainer approval. Rejected: filing GitHub issues directly; report-only.
5. **Balance-reviewer:** folded into the analyst; the backlog entry closes into this program. Rejected: keeping it separate.
6. **Bots per world:** N, default 1. Rejected: one per world in v1.
7. **Authoring:** YAML + CLI first; the Blazor Playtest page later in this program. Rejected: the page in v1; no page at all.
8. **Defaults approved as proposed:**
   - six scripted behaviours;
   - the R10 metric set;
   - seed-reproducible reports with replay coordinates;
   - thresholds in a new standards family;
   - per-PR graded golden plus advisory sweeps;
   - five personas with goal, budget and constraints;
   - calibration before trust;
   - identifiable harness sessions;
   - the exclusions as listed.

**Adopted defaults** *(cheap to veto at advisor framing)*:
- fail fast on missing scenario content;
- record persona constraint violations and continue;
- calibration fixtures committed apart from the dev-world baseline.

---

*Pre-refinement strategy notes (advisor input) follow.*

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

Resolved at `/refine` (2026-10-29). See [Resolved decisions](#resolved-decisions). Which world the acceptance test runs against was settled by the Phase 5 intake (dev-world only). Items left for the advisor are under [Open questions](#open-questions).

## Constraints to respect

- **No second combat model, and the real systems throughout.** The harness drives real commands and systems; it doesn't reimplement rules (the sim program's rule).
- **INV-26.** No `DateTime.Now` or `Random.Shared`; all time and chance come through the stepped clock and `IRandom`.
- **INV-31.** Declare the confine posture; no new thread mutates a live world's components.
- **INV-5.** The recorder and policies observe and act through events and commands; systems stay bus-free.
- **INV-18.** Each slice ships the tooling to author and inspect what it adds: scenarios, expectations, fixtures.
- **Content ownership.** Fixture content stays generic and lives in `content/dev-world/`; operator worlds are never committed.
- **Advisory, not gating.** Audits and playtest verdicts never become hard CI gates beyond the thin pinned golden subset.

## Open questions

- **Threading posture confirmation.** Confirm that one world per thread, with the tick driver as the single writer, satisfies INV-31, and that the harness adds no new writer to any live server world. *(Advisor, with code access.)*
- **Harness placement.** Where the run mode, the harness, the bot behaviours and the tool surface live: the `Server` run mode, a `Core/Modules/Playtest/` module or a separate project, and the `.claude/` agents. *(Advisor.)*
- **Observation format.** What a bot or persona "sees": structured output messages through an agent formatter (see the strategy notes' known gap #4 on remaining prose output), and how much remaining `PlainMessage` prose needs structuring for bots to read outcomes reliably. *(Advisor; this overlaps the client-tier doc's structured-output direction.)*
- **Tool-surface transport.** How Claude Code reaches a playtest world: an MCP server versus a CLI step loop. *(Advisor.)*
- **Slice decomposition and ordering** against the Phase 5 slices. This program's substrate needs Phase 5 slice 1 (`world.yaml`, a bootable dev-world). The acceptance golden needs content drops B1–B3. *(Advisor.)*
- **Handler-wiring extraction.** The strategy notes' known gap #2: gameplay subscriptions live in `Server/Program.cs`, so a harness would copy them. Confirm the extraction lands in the substrate slice. *(Advisor; an INV-19 framework piece.)*
- **Findings-ledger location and format.** Where the ledger lives: beside reports under `data/`, or in `docs/`, given that run artifacts are not committed and the ledger is a durable record. *(Advisor + user.)*
- **Stuck-detection window and budget defaults.** These are tuning values, set during the first scripted-bot slice. *(Planner/designer.)*

