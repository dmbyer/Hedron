# Playtest automation — agentic harness (program)

- **Status:** planned (requirements + architecture brief — awaiting `/new-plan` per slice)
- **Actors:** System (headless harness, scripted bots, LLM persona agents), Administrator (designer reading reports), Player (simulated)
- **Module:** a new `Core/Modules/Playtest/` module (world factory, tick driver, `BotSession`, recorder, grader, scenario store, bot policies). Also:
  - `Server/` run modes `playtest` and `playtest-mcp`;
  - an agent formatter in `Core/Output/`;
  - `.claude/agents/` personas and the analyst;
  - a findings ledger under `docs/playtest/`.

  Feature home: a new [`features/playtest/`](../features/) folder beside `simulation/`. Slices are listed in the [Architecture brief](#slice-decomposition).

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

## Design notes

*(Durable seam rationale. These survive disintegration into the slices' `completed/` records and the feature doc.)*

- **One world API, many drivers.** A *playtest world* exposes one small API: act (queue a player command for a bot), look/observe (drain that bot's observations), status, and step (advance N ticks).
  - Every way of playing is a **driver** of that API. In-process scripted bot policies, the `playtest-mcp` server for Claude Code personas, and later a headless Claude API runner are interchangeable.
  - **Why:** the persona layer, the scripted bots and the future API runner differ only in *who decides the next command*. Putting that seam at the world API keeps the harness single-sourced (one tick driver, one recorder, one grader) and makes the deferred API runner a new driver, not a new harness.
  - Scripted policies follow the `ISimCombatantPolicy` precedent: observation in, command line out, stateless where possible.
- **Perception and measurement are separate channels.**
  - **Measurement** comes from domain events. A per-world recorder handler observes the existing outcome events (kills, XP awarded/improved, currency, buy/sell, incapacitation/death/respawn, movement, `CommandExecutedEvent` for failed commands, ability activation). It runs last, at the reserved `Ai` priority, and never mutates anything. Grading therefore never parses prose.
  - **Perception** is player-visible output only (R4). It is rendered for a new `playtest` transport key by an **agent formatter** that emits one JSON observation per typed output message. Fully typed messages are fully structured. `PlainMessage` passes through as text plus its `OutputCategory`/`OutputSeverity`.
  - **Structuring ratchet:** about 225 `PlainMessage` call sites exist. The substrate slice converts only the messages the scripted bots must branch on (combat ended, incapacitation/death/respawn, XP award and improvement narration, buy/sell results) to typed messages. Every later conversion is on-touch.
  - This is the same direction [`design/client-tier.md`](../design/client-tier.md) prescribes for a structured client (*shape for later*): the agent formatter is the first non-telnet formatter, and the typed messages it needs are reusable by a future web client.
- **A playtest world is a self-contained, confined copy of the engine.**
  - **Composition:** each world is composed from the same composition root as the live host, with these differences:
    - no hosted services, and no telnet or wall-clock heartbeat;
    - a seeded `IRandom` and a stepped clock;
    - in-memory persistence (the persistence test-harness precedent);
    - a **scratch copy of the content directory**, so setup, defect overlays and builder verbs never write into the committed fixture, and parallel worlds never share files.
  - **Threading (INV-31):** each world is confined to one thread, and the **tick driver is its single writer**: per step it dispatches each bot's queued command, then publishes the heartbeat tick, then drains the output buffers into observations. Parallel sweeps are many worlds, never many threads in one world. The MCP server receives tool calls on its transport thread and **marshals** them onto the world's thread, so the confine posture holds and the world stays paused while a model thinks.
  - This mirrors the simulator's `SandboxWorld` (one `EntityService` per run, the INV-12 nuance already accepted there), but boots *real content* rather than ad-hoc combatants.
- **Two extractions land first, because a second copy would drift (INV-19).**
  1. **Gameplay handler subscriptions.** About 70 `Subscribe` calls live in `Server/Program.cs` `Main`. They move into a composition-root method that both the live host and every playtest world call, pinned by a guard test that both subscribe the identical set.
  2. **Session entry.** The post-authentication "character enters the world" sequence is extracted from `LoginFlow` / the telnet path into a shared piece. A bot then enters through exactly the path a player does, which is what A1 ("lands in the hub starting room") actually tests. `LoginFlow` keeps the transport-coupled wizard.
- **Expectations are standards, and findings are a committed ledger.**
  - Graded thresholds are a new `playtestExpectations` family in the balance-standards document, authored on the Standards page (R11, balance.md rule 4). A grader that finds no threshold reports the metric descriptively (R12).
  - Qualitative findings are a different kind of record. They go to a **committed findings ledger** under `docs/playtest/`, maintained by the analyst agent through PRs, because sessions are ephemeral and `data/` is gitignored. This is a new documentation bucket, registered in [`09-documentation.md`](../architecture/09-documentation.md) (INV-30). Raw run reports stay gitignored.
- **Calibration uses defect overlays, not committed broken content.** A calibration scenario boots dev-world and applies its defect during setup through the normal admin and authoring verbs, on the scratch copy. Examples: dig a room with no exits, set an item's value to 10×, raise a mob far past its band. No broken fixture is committed, and dev-world stays the only committed world.

## Architecture brief

*(In-flight. Trimmed on ship.)*

### Placement

| Piece | Home | Layer / role |
|---|---|---|
| `playtest` run mode (CLI), `playtest-mcp` run mode | `Server/PlaytestRunMode.cs`, `Server/PlaytestMcpRunMode.cs` (beside `SimulateRunMode`) | Initiators (INV-10): compose, run, write the report, exit or serve |
| Gameplay-subscription extraction | `Server/CompositionRoot.cs` (a subscribe-handlers method) | Composition; guard-tested |
| Session-entry extraction | Shared helper beside the account/session code (the planner places it; it must not be transport-bound) | Initiator-tier helper |
| Playtest world factory, world, tick driver, `BotSession` | `Core/Modules/Playtest/` (new module; `AddPlaytestModule`) | Harness infrastructure; the tick driver is an Initiator (it publishes `HeartbeatTickEvent`) |
| Agent formatter (`playtest` transport) | `Core/Output/` beside `TelnetOutputFormatter` | Output framework |
| Recorder | `Core/Modules/Playtest/Handlers/` | Handler at `Ai` priority, observation-only |
| Grader, report writer, scenario store, stuck detector | `Core/Modules/Playtest/Systems/` | Domain systems: pure, return results (INV-5) |
| Bot policies (six scripted behaviours) | `Core/Modules/Playtest/Policies/` | Drivers: emit command lines only |
| `playtestExpectations` standards family | `BalanceInspection/Standards` document + Standards page | Data + editor |
| Personas, analyst | `.claude/agents/persona-*.md`, `.claude/agents/playtest-analyst.md`; project MCP config for `playtest-mcp` | Agent tooling |
| Findings ledger | `docs/playtest/` (new bucket) | Durable doc |
| Golden | `Hedron.Tests/Playtest/` | Test tier (registered in [`07-testing.md`](../architecture/07-testing.md)) |
| Feature doc | `docs/features/playtest/` (sibling of `simulation/`) | Living doc on ship |

### Seams and dispositions

| Seam | Disposition |
|---|---|
| World API (act / observe / status / step) with interchangeable drivers | **Build now**; the API-runner driver is **shaped for later** |
| Handler-subscription extraction | **Build now** (INV-19; the precondition for any second world) |
| Session-entry extraction | **Build now** (INV-19; A1 must test the real entry path) |
| Per-world composition (stepped clock, seeded random, in-memory persistence, scratch content copy) | **Build now** |
| Tick driver as the single writer; MCP calls marshalled onto the world thread | **Build now** (INV-31 confine posture) |
| Agent JSON formatter + typed conversion of the bot-critical messages | **Build now**; full structuring is an **on-touch ratchet** (client-tier direction) |
| Event-sourced recorder + pure grader + standards family | **Build now** |
| Scenario YAML store | **Build now** over the shared YAML definition store from Phase 5 `help-topics`. If this slice lands first, *it* is the third family and does the extraction instead. Either way the extraction happens once. |
| N bots per world | **Build now** (the tick driver steps every bot; the golden uses 1) |
| Automation marker on live accounts (R28 beyond harness worlds) | **Defer**: harness worlds are already isolated and tagged by transport key and report. An account-level marker lands with live-server bots, after the threading decision. Proposed backlog entry below. |
| Headless Claude API persona runner | **Defer** (resolved at refine; a new driver behind the world API when it lands) |

### Observers, ordering, events

- **No new domain events.** The recorder consumes the existing ones. If a metric needs a fact no event carries, the planner adds a thin past-tense event at the publishing seam rather than letting the recorder query state. The exception is the stuck detector, which is harness *measurement*: it may read spawn-slot state to tell "waiting" from "stuck".
- **Tick order (INV-7):** queued commands are dispatched → `HeartbeatTickEvent` (existing handler priorities unchanged) → the output-flush handler drains the buffers → the driver collects observations. The recorder at `Ai` (95) sees each event after every gameplay handler.
- **Game time per step** equals the configured heartbeat interval, with the stepped clock advanced by the same `Elapsed`, so respawn delays, regeneration and bleed-out behave exactly as live.

### Invariants in tension

- **INV-12:** many worlds per process. This is the sandbox nuance already accepted for `SandboxWorld`; the feature doc states it explicitly.
- **INV-26:** all gameplay time and chance come through the stepped clock and the seeded `IRandom`. The known wall-clock debt in `AccountSystem` (creation timestamps) must not reach any reported or graded value. Either the report excludes it, or the substrate slice fixes that debt with the injected clock (the planner chooses, and should prefer the fix).
- **INV-31:** confine per world; the MCP transport marshals; no harness thread touches a live server world.
- **INV-5 / INV-9 / INV-10:** the recorder is a handler; the grader and stores are pure systems; the run modes and tick driver are initiators; policies act only by dispatching command lines.
- **INV-19:** three frameworks land in the substrate (subscriptions, session entry, agent formatter + message ratchet), plus the YAML store if it lands third.
- **INV-18:** scenario YAML, example scenarios and the `playtest` CLI ship with the substrate. The standards family ships with its Standards-page rows. The Blazor Playtest page is a later slice of this program, as resolved at refine.
- **INV-25:** each harness system gets Tier-1 tests; determinism is pinned (same seed → identical report); and the guard test pins the subscription set. The golden is a new test tier, registered in `07-testing.md`.
- **INV-17:** a new flow, the **playtest run journey** (`playtest` / `playtest-mcp` → world boot → tick loop → recorder → grade → report). It also touches flow-01 (the subscriptions move), flow-02/07 (the session-entry extraction) and flow-06 (the new transport formatter).
- **INV-20:**
  - **New:** a `run-playtest` skill (scenario authoring, running, reading reports, the autonomy rule).
  - **Updated:** `balance-tuning` (R27 autonomy amendment), `add-tests` (the golden tier), `add-command` and `add-handler` (new gameplay subscriptions go through the extracted method).
- **INV-30 / INV-27:**
  - the findings ledger is a new bucket, registered in `09-documentation.md`;
  - the R27 autonomy rule gets one living home ([balance.md](../design/balance.md), cited by the skills), while the 2026-07-17 record stays as history.

### Slice decomposition

| # | Slice | Contents | Requirements | Depends on |
|---|---|---|---|---|
| PT-1 | `playtest-world-substrate` | Subscription + session-entry extractions; per-world composition; tick driver; `BotSession` + agent formatter + bot-critical message conversion; R27 policy amendment; tests drive a world by command lines, with no bots yet | R2–R6, R27–R28 | — (can start alongside Phase 5 slice 1; boots through `world.yaml` once that lands, or config until then) |
| PT-2 | `playtest-run-mode` | Recorder; grader + `playtestExpectations` family; scenario YAML + store; `playtest` CLI; report writer; acceptance-walker policy; a smoke golden on the hub skeleton | R1, R7 (walker), R10–R14, R25 | PT-1; Phase 5 slice 1 |
| PT-3 | `scripted-bots` | Explorer, grinder, shopper, die-and-recover, newcomer; stuck detection; N bots per world; multi-seed sweep runner; per-run findings files | R7–R9, R15, R21 | PT-2 |
| — | Acceptance golden (= Phase 5 slice 9) | A1–A8 graded on dev-world | R14 + Phase 5 R26–R29 | PT-2; Phase 5 drops B1–B3 |
| PT-4 | `persona-layer` | `playtest-mcp` run mode + `report_finding` tool; five personas; defect-overlay calibration; analyst agent + ledger (including the balance-reviewer role); `run-playtest` skill | R16–R24 | PT-2 (PT-3 recommended first) |
| PT-5 | `playtest-editor-page` | Blazor Playtest page: compose, launch, reports, ledger browser | R26 | PT-2 |

Interleaving with Phase 5: PT-1 runs in parallel with Phase 5 slices 1–5. PT-2 is ready as soon as the hub skeleton exists, so bots check each content drop from B1 onward. The acceptance golden closes both programs.

### Resolved decisions (advisor intake)

*(Confirmed by the user on 2026-10-29. Do not relitigate.)*

1. **Perception:** split channels. Metrics come from events; bots perceive JSON observations; only the bot-critical messages are structured now, and the rest on-touch. Rejected: structuring all output first; bots parsing text.
2. **Findings ledger:** committed under `docs/playtest/`, maintained through PRs; raw reports stay gitignored. Rejected: a gitignored ledger.
3. **Persona transport:** a `playtest-mcp` run mode with `look`/`act`/`status`/`wait`/`report_finding`, connected through a project MCP config. Rejected: a hand-rolled CLI step loop.
4. **Calibration:** defect overlays applied in setup on a scratch content copy. Rejected: committed broken fixtures.

**Advisor calls** *(cheap to veto at planning)*:
- the world-API/driver seam;
- per-world scratch content copy and in-memory persistence;
- the two extractions in PT-1;
- `Core/Modules/Playtest/` as the home;
- the YAML-store "whoever is third extracts" rule;
- the account automation marker deferred.

**Proposed backlog entry** *(added on your confirmation)*: "🔵 Account-level automation marker for live-server bots (deferred from the playtest program): harness worlds are isolated and transport-tagged; when bots ever run against a live server, after the world-state threading decision, accounts carry an automation marker so anti-automation detection (horizon) can tell them apart."

---

*Pre-refinement strategy notes (advisor input, superseded by the brief above wherever they differ) follow.*

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

- **Static or shared state across worlds.** Confirm that no registry, cache or singleton is process-static in a way that two playtest worlds in one process would share and mutate. Where one is (for example a static registry populated at boot), PT-1 makes it per-container or proves it immutable. *(Planner, PT-1, with code access; load-bearing for parallel sweeps.)*
- **Bot-critical message list.** The exact `PlainMessage` sites PT-1 converts, derived from what the acceptance walker must branch on. *(Planner, PT-1.)*
- **Session-entry extraction boundary.** Which steps of the post-login sequence (hydration, `PlayerConnectedEvent`, starting room and respawn defaults) move into the shared piece. *(Planner, PT-1.)*
- **`AccountSystem` wall-clock debt.** Fix it with the injected clock in PT-1 (preferred), or exclude account timestamps from reports. *(Planner, PT-1.)*
- **Golden runtime budget.** The maximum wall time for the per-PR golden, which bounds the scenario's length and tick count. *(User + planner, PT-2.)*
- **Stuck-detection window and budget defaults.** Tuning values, set during PT-3. *(Planner/designer.)*
- **MCP SDK choice and project MCP config shape** for `playtest-mcp`. *(Planner, PT-4.)*
