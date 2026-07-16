# Simulation

> Deterministic, offline batch combat simulation that validates real outcomes against the
> balance-standards registry. **Status:** live (headless CLI only; a Blazor editor surface lands
> sim-3).

## What it is

A designer runs `dotnet run --project Server -- simulate --scenario <path> [--seed N]` to pit two
combatants (an authored mob template, a sim-1 standards reference build, or an inline stat block)
against each other hundreds of times and see whether the fight matches expectations: does an
equal-power matchup land near a 50/50 split? Does a higher-tier build actually win more often? The
answer comes back as a console summary (win rates, time-to-kill, pass/fail verdicts) and a JSON
report file a designer can keep, diff, or discard — the same posture as the `generate` bulk-content
tool's output.

Nothing about a simulation run touches the live game world. It composes its own throwaway world per
run, drives combat through the exact same systems a live fight uses, and discards everything when
the run ends.

## How it works

`Program.Main` recognizes the `simulate` token and branches before the telnet listener or heartbeat
ever starts — a one-shot Initiator, like `generate`. `ISimScenarioStore` loads and validates the
scenario file (unknown kind/policy, wrong combatant count, non-positive iteration/tick counts all
fail fast before a single run executes). `ISimulationRunner` resolves both combatants once — reading
the mob catalog or the balance-standards registry — then fans out every iteration in parallel, each
against its own freshly built `SandboxWorld` (a private `EntityService` plus the full combat/stats/
effects/aspects/abilities/entity-state/regeneration/progression/ascension system graph). Each run's
combat plays out on synthetic ticks via `CombatScenarioExecutor` — no event bus, no heartbeat — until
one side dies or the tick cap is hit (a draw). The runner reduces every run's outcome deterministically
(same seed → byte-identical result, regardless of how many worlds ran in parallel) and hands the
aggregates to `ISimOutcomeEvaluator`, which checks them against the sim-1 balance-standards registry's
tolerances. `ISimReportWriter` writes the whole thing as a JSON artifact.

## Systems

| System | Role |
|---|---|
| [`simulation-engine.md`](simulation-engine.md) | Scenario model, sandbox world factory, combatant resolution, the batch runner, outcome evaluation, and the report artifact — the whole engine. |
| [`power-budget-system.md`](../progression/power-budget-system.md) | The sim-1 balance-standards registry this engine reads its reference builds and expected-outcome tolerances from. |

## Surfaces

- CLI run-mode: `simulate --scenario <path> [--seed N]` (see [`../../../README.md`](../../../README.md#balance-simulation-headless)).
- Config: `Simulation:ReportDirectory` — see [`reference/systems.md`](../../reference/systems.md).
- Content: scenario YAML (`data/sim/scenarios/`, checked-in example at `example-equal-cell.yaml`); report JSON (`data/sim/reports/`, gitignored, run-generated).
- No new commands, events, or persistent components — the engine composes existing gameplay systems and touches no live entity.

## Flows

- [Flow 33 — Simulation run journey](../../architecture/flows/flow-33-simulation-run.md) — the full CLI-to-report call chain.

## Related

- [`../../roadmap/completed/simulation-engine-core.md`](../../roadmap/completed/simulation-engine-core.md) — as-built history, decisions, and the discovered tier-baseline calibration gap.
- [`../../implementation-plans/balance-simulator.md`](../../implementation-plans/balance-simulator.md) — the five-sub-slice program this is `sim-2` of (`sim-3` editor integration, `sim-4` progression-rate scenarios, `sim-5` conformance tooling remain).
- [`../progression/progression.md`](../progression/progression.md) · [`../progression/ascension-system.md`](../progression/ascension-system.md) — the progression/tier mechanics a reference-build combatant exercises.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-2, INV-5, INV-10, INV-12 (named sandbox-world nuance), INV-19, INV-26.
