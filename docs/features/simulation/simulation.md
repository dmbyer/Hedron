# Simulation

> Deterministic, offline batch simulation that validates real outcomes against the balance-standards
> registry — combat matchups (sim-2) and progression-rate sweeps (sim-4), both through a headless CLI
> and a Blazor Simulation editor page (sim-3). **Status:** live.

## What it is

A designer runs `dotnet run --project Server -- simulate --scenario <path> [--seed N]`, **or**
composes and launches the same run from the Blazor **Simulation page** (`/simulation`) in
`Hedron.Web`, against one of two scenario kinds:

- **Combat** — pit two combatants (an authored mob template, a sim-1 standards reference build, or
  an inline stat block) against each other hundreds of times and see whether the fight matches
  expectations: does an equal-power matchup land near a 50/50 split? Does a higher-tier build
  actually win more often?
- **Progression-rate** (sim-4) — pit a subject against a victim and ask "how many kills / how much
  XP does it take to improve a track N times, farming this victim?" — a deterministic sweep over the
  real `IProgressionSystem.AwardCombatExperience` seam, no combat rounds simulated.

The answer comes back as a console summary (CLI) or a live-updating status + verdict-rendered report
view (editor) — win rates and time-to-kill for combat, kills/XP-to-target and per-track distributions
for progression-rate, pass/fail verdicts either way — plus, in both cases, the identical JSON report
artifact a designer can keep, diff, or discard (one artifact class, two kinds, two producers).

The editor page also composes scenarios (save/load through the same YAML store the CLI reads),
offers **"Simulate vs reference"** buttons on the mob and item editors that prefill a combat matchup
against the reference build of the authored content's `(Tier, Band)` cell, a **"Re-run baseline
sweep"** button on the Standards page that enqueues one equal-cell and one adjacent-pair scenario per
cell, and — for a progression-rate scenario — a **"prefill ticks per kill"** dropdown that reads a
chosen combat report's mean time-to-kill.

Nothing about a simulation run touches the live game world, from either surface. It composes its own
throwaway world per run, drives its kind through the exact same systems a live fight/kill uses, and
discards everything when the run ends.

## How it works

`Program.Main` recognizes the `simulate` token and branches before the telnet listener or heartbeat
ever starts — a one-shot Initiator, like `generate`. `ISimScenarioStore` loads and validates the
scenario file (unknown kind, wrong combatant count, non-positive iteration/tick counts, and — for a
progression-rate scenario — its kind-gated `progression:` section, all fail fast before a single run
executes). `ISimulationRunner` dispatches on `ScenarioKind` to one of two executors and resolves both
sides once — reading the mob catalog or the balance-standards registry — then fans out every iteration
in parallel, each against its own freshly built `SandboxWorld` (a private `EntityService` plus the
full combat/stats/effects/aspects/abilities/entity-state/regeneration/progression/ascension system
graph). A **Combat** run plays out on synthetic ticks via `CombatScenarioExecutor` — no event bus, no
heartbeat — until one side dies or the tick cap is hit (a draw). A **ProgressionRate** run (sim-4)
instead loops kill-events via `ProgressionScenarioExecutor`, each one calling
`IProgressionSystem.AwardCombatExperience(subject, victim)` directly — no combat rounds — until the
target track's improvement count reaches the target or the kill cap is hit. Either way the runner
reduces every run's outcome deterministically (same seed → byte-identical result, regardless of how
many worlds ran in parallel) and hands the aggregates to `ISimOutcomeEvaluator`, which checks combat
outcomes against the sim-1 balance-standards registry's tolerances, or produces a standards-free
`targetReached` verdict plus a permanently-skipped `progressionRateExpectation` verdict for a
progression-rate run (no designer-authored tolerance exists yet — see
[`../../roadmap/backlog.md`](../../roadmap/backlog.md)). `ISimReportWriter` writes the whole thing as
a JSON artifact either way — the combat scalar fields and the additive `ProgressionRate` payload are
mutually exclusive on any one report.

The editor page (sim-3) is a thin caller over this same engine — the content-tooling precedent of how
`MobEditor`/`ItemEditor` call `IContentGenerationSystem`/`IBalanceAuditSystem`. `SimulationRunService`
(a `Hedron.Web`-only singleton) enqueues a scenario after the identical `ISimScenarioStore.Validate`,
runs it on a background task via the same `ISimulationRunner.Run` (now with an optional cancellation
token and progress callback — additive engine seams, inert for the CLI), and writes the report through
the same `ISimReportWriter`. The page polls the service for live status, can cancel a queued or active
run, and reads report history through the new `ISimReportReader` — no verdict math, validation, or
report shape is duplicated anywhere in the web host. See [`../../architecture/08-blazor.md`](../../architecture/08-blazor.md)
"Background tooling jobs" for the shape.

## Systems

| System | Role |
|---|---|
| [`simulation-engine.md`](simulation-engine.md) | Scenario model, sandbox world factory, combatant resolution, both kind executors (combat, progression-rate), the batch runner, outcome evaluation, and the report artifact — the whole engine, plus its sim-3 (cancellation/progress, report reader, scenario save/list, verdict-cell fallback) and sim-4 (progression-rate kind, `ticksPerKill` bridge) additive seams. |
| [`power-budget-system.md`](../progression/power-budget-system.md) | The sim-1 balance-standards registry this engine reads its reference builds and combat expected-outcome tolerances from. |
| [`../progression/progression-system.md`](../progression/progression-system.md) | The `IProgressionSystem` seam the sim-4 progression-rate executor sweeps directly (`AwardCombatExperience`/`TryImprove`) — no re-implemented award math. |

## Surfaces

- CLI run-mode: `simulate --scenario <path> [--seed N]` (see [`../../../README.md`](../../../README.md#balance-simulation-headless)).
- Editor: the Simulation page (`/simulation`) in `Hedron.Web` — compose/save/load a scenario of either kind, launch it in the background, watch progress, cancel, and browse/open report history; "Simulate vs reference" entry points on `MobEditor`/`ItemEditor`; "Re-run baseline sweep" on the Standards page; a "prefill ticks per kill" dropdown on progression-rate scenarios (see [`../../../README.md`](../../../README.md#content-authoring-web-ui)).
- Config: `Simulation:ReportDirectory`, `Simulation:ScenarioDirectory` (sim-3) — see [`reference/systems.md`](../../reference/systems.md).
- Content: scenario YAML (`data/sim/scenarios/`, checked-in examples `example-equal-cell.yaml` (combat) and `example-progression-rate.yaml` (sim-4), editor-saved files alongside them); report JSON (`data/sim/reports/`, gitignored, run-generated by either surface).
- No new commands, events, or persistent components — the engine composes existing gameplay systems and touches no live entity; the editor surface adds no live-world touch either (INV-22 by absence).

## Flows

- [Flow 33 — Simulation run journey](../../architecture/flows/flow-33-simulation-run.md) — the full CLI-to-report call chain for both kinds, plus the sim-3 editor trigger leg, cancellation path, and report-read leg.

## Related

- [`../../roadmap/completed/simulation-engine-core.md`](../../roadmap/completed/simulation-engine-core.md) · [`../../roadmap/completed/simulation-editor-integration.md`](../../roadmap/completed/simulation-editor-integration.md) · [`../../roadmap/completed/progression-rate-scenarios.md`](../../roadmap/completed/progression-rate-scenarios.md) — as-built history, decisions, and the discovered tier-baseline calibration gap (sim-2), the editor integration decisions (sim-3), the progression-rate executor decisions (sim-4).
- The `prog-4` balance-simulator program (five sub-slices, `sim-1`–`sim-5`, all shipped) this feature is `sim-2`/`sim-3`/`sim-4` of; `sim-5` (template conformance tooling) lives in [`../progression/power-budget-system.md`](../progression/power-budget-system.md) — see [`../../roadmap/completed/conformance-tooling.md`](../../roadmap/completed/conformance-tooling.md) for its as-built record.
- [`../progression/progression.md`](../progression/progression.md) · [`../progression/progression-system.md`](../progression/progression-system.md) · [`../progression/ascension-system.md`](../progression/ascension-system.md) — the progression/tier mechanics a reference-build combatant exercises, and the seam the sim-4 executor sweeps directly.
- [`../../architecture/08-blazor.md`](../../architecture/08-blazor.md) — the background-tooling-job shape the editor page introduced.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-2, INV-5, INV-10, INV-12 (named sandbox-world nuance), INV-19, INV-26.
