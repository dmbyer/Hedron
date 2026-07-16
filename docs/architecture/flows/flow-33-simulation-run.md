# Simulation Run Journey

> [Back to flows index](README.md)

**Source:** [`../../features/progression/power-budget-system.md`](../../features/progression/power-budget-system.md) (the sim-1 standards registry this engine consumes)

**Summary.** A headless `simulate` run-mode composes DI without gameplay hosted services, loads and validates one scenario, runs it through `ISimulationRunner` — which resolves combatants once, fans out isolated per-run sandbox worlds in parallel, drives each to completion via a synthetic per-tick sequence with no event bus, and reduces the results deterministically — then writes a JSON report artifact and prints a console summary. No live entity, no `EcsManager`, no `IEventBus` publish anywhere in the run (INV-12 nuance, INV-5, INV-10).

**Trigger:** `dotnet run --project Server -- simulate --scenario <path> [--seed N]`

```mermaid
sequenceDiagram
    participant CLI as Program.Main
    participant RM as SimulateRunMode
    participant Store as ISimScenarioStore
    participant Runner as ISimulationRunner
    participant CF as ISimCombatantFactory
    participant SWF as ISandboxWorldFactory
    participant Exec as CombatScenarioExecutor
    participant Eval as ISimOutcomeEvaluator
    participant W as ISimReportWriter

    CLI->>RM: Matches(args) → RunAsync(args, config)
    RM->>RM: CompositionRoot.Register (no gameplay hosted services)
    RM->>Store: Load(path, seedOverride)
    Store->>Store: deserialize + structural validation (fail-fast)
    Store-->>RM: ScenarioDefinition
    RM->>Runner: Run(scenario)
    Runner->>CF: Resolve(sideA spec) / Resolve(sideB spec)  — once per scenario
    CF-->>Runner: ResolvedCombatant × 2
    loop per run index i (parallel, bounded)
        Runner->>Runner: SimSeeds.DeriveRunSeed(scenario.Seed, i) → SeededRandom
        Runner->>SWF: Create(random) → fresh EntityService + system graph
        Runner->>CF: Materialize(world, resolved) × 2 → entity ids
        Runner->>Exec: ExecuteRun(world, entityA, entityB, policyA, policyB, maxTicks, i)
        Exec->>Exec: per tick: AdvanceTick effects → AdvanceCooldowns → randomized-initiative actions → ApplyTickRegen
        Exec-->>Runner: RunRecord (indexed by i)
    end
    Runner->>Runner: index-ordered reduce → win counts, TTK/damage distributions
    Runner->>Eval: Evaluate(resolvedA, resolvedB, wins, wins, draws)
    Eval-->>Runner: SimVerdict[]
    Runner-->>RM: SimulationReport
    RM->>W: WriteAsync(report)  (atomic tmp → rename)
    W-->>RM: report file path
    RM->>CLI: print summary; return 0 / 1 / 2
```

**Steps.**
1. `Program.Main` detects the `simulate` token and branches before building the listener host (mirrors `generate`). `--scenario` is required; missing → exit 2.
2. `SimulateRunMode` composes DI via `CompositionRoot.Register` (plus `AddLogging`, since `ISimCombatantFactory` resolves the real `IContentDefinitionCatalog` for the mob-template combatant source) — no `AddGameplayHostedServices`, so no telnet, no heartbeat, no world-content spawn.
3. `ISimScenarioStore.Load` deserializes the scenario YAML and runs fail-fast structural validation (unknown kind, unknown policy id, wrong combatant count per side, non-positive iterations/maxTicks, unresolvable source discriminator). Any violation → exit 2 before a single run executes.
4. `ISimulationRunner.Run` dispatches on `ScenarioKind` (only `Combat` has an executor; any other kind throws — the sim-4 seam) and resolves each side's `CombatantSpec` **once** via `ISimCombatantFactory.Resolve` — mob-template catalog reads and standards-registry reads happen here, never inside the per-run hot path.
5. Runs fan out in parallel (bounded by `maxParallelism`, default processor count). Each run derives its own seed via `SimSeeds.DeriveRunSeed(scenarioSeed, runIndex)` (a stable, non-`HashCode.Combine` mix — INV-26), builds a fresh isolated `SandboxWorld` via `ISandboxWorldFactory.Create` (never the host's live world — INV-12 nuance), and materializes both resolved combatants into it via `ISimCombatantFactory.Materialize`.
6. `CombatScenarioExecutor.ExecuteRun` drives one run to completion: each synthetic tick advances effects (due periodics applied directly, no `EffectExpiredEvent`), advances ability cooldowns, draws a randomized initiative order from the run's own `IRandom` (avoiding a structural first-strike bias), and lets each living combatant's `ISimCombatantPolicy` choose melee or an ability (activated via `IAbilitySystem.Activate` + `ICombatSystem.ResolveAbilityStrike` for offensive abilities — mirroring `AbilityInvocationPipeline` minus every bus publish), then applies regeneration. Ends on a `MobDied` outcome or the `maxTicksPerRun` cap (a draw).
7. The runner reduces every `RunRecord` **in run-index order** (independent of completion scheduling) into win counts, draw count, and `DistributionStats` (mean/median/p10/p90/min/max) for time-to-kill and per-side damage dealt.
8. `ISimOutcomeEvaluator.Evaluate` compares the reduced win rates against `IBalanceStandardsRegistry.OutcomesFor` tolerances — an equal-cell check when both sides share a (Tier, Band) cell, a one-band-higher floor check when their global band indexes differ by exactly one, or a skipped-with-reason verdict otherwise. Verdict math lives here, not in the CLI or a future editor page (INV-19).
9. `SimulateRunMode` calls `ISimReportWriter.WriteAsync` (atomic tmp → rename JSON into `Simulation:ReportDirectory`), prints a console summary (win rates, TTK, verdicts), and returns 0. An engine-level exception (e.g. `NotSupportedException` for an unbuilt scenario kind) is caught and mapped to exit 1.

---

## Invariants

- INV-2: the engine's inputs are computed values (`ResolvedCombatant.Scores`, an `IBalanceStandardsRegistry.ReferenceSnapshot`) — the sim never reaches into `PowerBudgetSystem`'s snapshot-only contract from a domain angle.
- INV-5 / INV-10: `ISimulationRunner`, `CombatScenarioExecutor`, and every Simulation-module system return results only; `SimulateRunMode` is a no-chain Initiator. Guard-tested: no `Core/Modules/Simulation/` type references `IEventBus`.
- INV-12 (named nuance): a sandbox world is explicitly **not** the "one live world" — the engine never calls `EcsManager.SetWorld` and never resolves the host's `EntityService`. Guard-tested by source scan.
- INV-19: verdict math and the standards-registry read live in the engine, so the CLI (today), the sim-3 editor, and the promoted CI invariants can never drift onto different expected-outcome math.
- INV-22 (by absence): no `SaveEntityAsync` call site anywhere in the Simulation module — sandbox entities are discarded with their `EntityService` at the end of each run.
- INV-26: every chance path flows through a per-run `SeededRandom` derived by `SimSeeds.DeriveRunSeed` (a stable SplitMix64-style mix, never `HashCode.Combine`/`Random.Shared`); a fixed (scenario, seed) pair reproduces byte-identically regardless of `maxParallelism`.
- **Isolation is the concurrency model.** Worlds share nothing except immutable, `EntityService`-free singletons (`IAbilityRegistry`, `IEffectRegistry`, `IPowerBudgetSystem`); per-run records land in an index-keyed array slot, so the reduce is independent of scheduling.

## Cross-references

- Systems: [`../../reference/systems.md`](../../reference/systems.md) — `ISimScenarioStore`, `ISandboxWorldFactory`, `ISimCombatantFactory`, `ISimCombatantPolicy` (+ built-ins), `ISimulationRunner`, `ISimOutcomeEvaluator`, `ISimReportWriter`.
- Feature: [`../../features/progression/power-budget-system.md`](../../features/progression/power-budget-system.md) (the sim-1 standards registry this engine's reference builds and verdicts read).
- Related flow: [Flow 29 — content-tooling journey](flow-29-bulk-content-generation.md) (the `generate` run-mode precedent this mirrors) · [Flow 17 — combat journey](flow-17-kill-mob-combat-initiation.md) (the live per-tick sequence the executor's synthetic heartbeat deliberately mirrors, minus the bus) · [Flow 24 — abilities journey](flow-24-ability-activation.md) (the `Activate` + `ResolveAbilityStrike` pairing the executor drives directly).
