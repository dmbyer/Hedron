# Simulation engine

> The deterministic batch-combat engine behind the `simulate` run-mode and the sim-3 Simulation
> editor page: scenario model, sandbox world factory, two-phase combatant resolution, the parallel
> batch runner, expected-vs-actual outcome evaluation, and the JSON report artifact.
> **Authoring checkpoint:** slice sim-2; four additive seams (cancellation/progress, report reader,
> scenario save/list, verdict-cell fallback) added sim-3. Living document.

## What it is / does

**Domain-tier, tooling-tier** (like `IContentGenerationSystem`) — `Core/Modules/Simulation/`. The
engine's one job: given a validated `ScenarioDefinition`, run every iteration to completion in an
**isolated sandbox world it builds and discards per run** (never the host's live world — INV-12's
"one live world" is about the game world; sandbox worlds are deliberately not it), and reduce the
outcomes into a statistical report with expected-vs-actual verdicts. It publishes nothing (INV-5);
the `simulate` run-mode (`Server/SimulateRunMode.cs`) is the no-chain Initiator that calls it
(INV-10).

## How it works

### Scenario model — data-keyed, kind-generic from day one

`ScenarioDefinition` (`Kind`, `Name`, `Seed`, `Iterations`, `MaxTicksPerRun`, `Sides`) is plain data —
YAML-authorable today, editor-composable at sim-3, generator-constructable later. `ScenarioKind`
carries `Combat` (built) and `ProgressionRate` (reserved for sim-4) so a second kind is an additive
executor + report payload, never a change to the store/runner/writer shell. A `ScenarioSide` is a
list of `CombatantSpec`s from day one — sim-2 validates exactly one per side (1v1; the current
`CombatStateComponent` model is single-opponent), N-vs-N is additive data for a future slice.

A `CombatantSpec` resolves through one of three sources:

- **`MobTemplate`** — an authored mob blueprint id, read via `IContentDefinitionCatalog.Load` (no
  live spawn, no `TemplateRegistry`). Its cell for verdict purposes is the template's own
  `Tier`/`Band` tag when `Band >= 1`; when the template is unbanded, the scenario spec's own
  `Tier`/`Band` annotation resolves as the verdict cell instead (sim-3 fallback — the authored tag
  still wins whenever it exists).
- **`ReferenceBuild`** — a `(Tier, Band)` cell from the sim-1 `IBalanceStandardsRegistry`. This *is*
  the cell for verdict purposes.
- **`Inline`** — a caller-authored `ScoreId → int` map plus an ability-kit list, taken as-is. The
  future procedural mob generator's validate-a-candidate entry point. An optional `Tier`/`Band`
  annotation supplies its verdict cell.

`ISimScenarioStore` loads and structurally validates a scenario (unknown kind, unknown policy id,
empty/wrong-count side, non-positive iteration/tick counts, unresolvable source discriminator) —
posture mirrors `BalanceStandardsStore`: validate-then-use, every violation named in one thrown
exception. The known-policy-id set is DI-collected from every registered `ISimCombatantPolicy`,
never hardcoded. **Sim-3 addition:** the same store gained `SaveAsync` (validate-then-write,
atomic tmp→rename, camelCase YAML — the identical hand-authored shape, upsert by sanitized scenario
name) and `List` (the editor's load dropdown) — the store owns the YAML DTO shape, so this is the
only place a scenario is ever serialized, never a second page-side dialect.

### Sandbox worlds — hand-built, isolated, never the live world

`ISandboxWorldFactory.Create(IRandom)` news up a fresh `EntityService` plus the full system graph
(`EffectSystem` with its four contributors, `AttributeSystem`, `StatSystem`, `AspectSystem`,
`CombatSystem`, `AbilitySystem`, `EntityStateService`, `RegenerationSystem`, `ProgressionSystem`,
`AscensionSystem`) by hand — mirroring, not reusing, the composition `Hedron.Tests`' harness proves
is per-instance composable (resolved decision: no per-run scoped DI container; construction cost is
cheap — a 10k-run local timing came in under 4 seconds total, dominated by process startup, not
world construction). It shares only immutable, `EntityService`-free singletons across every world it
creates: `IAbilityRegistry`, `IEffectRegistry`, `IPowerBudgetSystem`, `IOptions<DeathOptions>`. It
never calls `EcsManager.SetWorld` and never resolves the host's `EntityService` — guard-tested by a
Tier-5 reflection + source scan (`ArchitectureGuardTests.Simulation_module_does_not_reference_EventBus_or_EcsManager`).

### Combatant materialization — real seams, not a parallel math path

`ISimCombatantFactory` is two-phase: `Resolve(CombatantSpec)` reads the catalog/registry **once per
scenario** (never per run — the hot path does no file/registry I/O), producing a `ResolvedCombatant`
(name, scores, ability kit, tier, policy id, optional verdict cell). `Materialize(SandboxWorld,
ResolvedCombatant)` stamps that into a specific run's world: `MobDataComponent` (so `CombatSystem`
recognizes a `MobDied` outcome), `AttributesComponent`/`PoolsComponent` from the resolved scores, an
`AscensionComponent { Tier }` when the resolved tier is positive (folded automatically by the shared
`AscensionEffectContributor` — no bespoke tier-injection path), and `Learn` per ability-kit id.

`AttackPower`/`Defense` are **computed** scores (`StatSystem.GetEffectiveAttackPower`/`GetEffectiveDefense`
derive them from raw `Body`, not stored directly), so a reference build's gear-equivalent bonus on
either folds through a synthetic **permanent** `StatModifier` effect via `IEffectSystem.Apply`
(`Duration: -1f` → `EffectLifetime.UntilRemoved`, `PowerScalingFormula: "fixed"`) — exactly the seam
worn gear rides (`EquipmentEffectContributor`). `IStatSystem.Get` is the only path any value reaches
a sandbox combatant; nothing is written straight onto a component the stat pipeline wouldn't also
see for a live entity.

### Policies — pure, stateless, shared across concurrent worlds

`ISimCombatantPolicy.ChooseAction(world, selfId, opponentId, roundIndex)` returns `SimAction.Melee`
or `SimAction.Ability(id)`. Because a policy instance is DI-shared across every concurrently-running
world, it holds **no mutable state** — `RoundRobinPolicy`'s cycle position is `roundIndex % known.Count`,
not an instance counter. Three built-ins ship day one: `MeleeOnlyPolicy`, `RoundRobinPolicy` (cycles
known abilities in `Known` order), `CooldownFirstPolicy` (first known ability that is off cooldown,
`Activation.Active`, and affordable; melee otherwise). A future `IAISystem` adapter binds behind this
same seam (backlogged, not built).

### The executor — a synthetic heartbeat with no bus

`CombatScenarioExecutor.ExecuteRun` drives one 1v1 run to completion, mirroring the live heartbeat's
per-tick order (effects → cooldowns → actions → regen) by calling system methods directly:

1. `IEffectSystem.AdvanceTick` + apply due periodics directly (no `EffectExpiredEvent` — no bus).
2. `IAbilitySystem.AdvanceCooldowns`.
3. Draw a **randomized** initiative order from the run's own `IRandom` — a fixed order would give
   one side a structural first-strike advantage, silently biasing the equal-cell 50% expectation the
   CI invariant exists to catch.
4. Each living combatant's policy chooses an action; melee calls `ICombatSystem.ExecuteRound`
   directly, an ability calls `IAbilitySystem.Activate(resolveOffensiveExternally: isOffensive)` then
   `ICombatSystem.ResolveAbilityStrike` for an offensive hit — mirroring `AbilityInvocationPipeline`'s
   steps 3 and 5 minus every event publish. A failed activation (cooldown/cost) is a passed action,
   matching live UX.
5. `IRegenerationSystem.ApplyTickRegen` (suppressed while `InCombat` — near-free, included for
   fidelity with the live order).

Ends on a `MobDied` `CombatRoundOutcome` (or a periodic dropping either side to ≤0 HP) or the
scenario's `maxTicksPerRun` (a draw). Both combatants are mob-archetype entities — including
"synthetic players" built from a reference build — so there is no `IDeathSystem` in the sandbox
graph and no incapacitate/bleed/respawn lifecycle to simulate.

### The runner — parallel fan-out, deterministic reduce

`ISimulationRunner.Run` dispatches on `ScenarioKind` (only `Combat` has an executor), resolves both
sides once, then fans `Iterations` runs out via `Parallel.For` (bounded by `maxParallelism`, default
processor count). Each run derives its own seed via `SimSeeds.DeriveRunSeed(scenarioSeed, runIndex)` —
a stable SplitMix64-style mix, deliberately **not** `HashCode.Combine` (process-randomized) or
`Random.Shared` — so a fixed `(scenario, seed)` pair reproduces byte-identically regardless of
`maxParallelism`. Runs land in an array slot keyed by run index; the reduce into win counts and
`DistributionStats` (mean/median/p10/p90/min/max for time-to-kill and per-side damage) iterates that
array in order, so the report is independent of completion scheduling. **Sim-3 addition:** `Run`
gained an optional `CancellationToken` (wired directly into `ParallelOptions.CancellationToken` — an
already-canceled token throws before any run executes; a live cancel is checked cooperatively between
per-iteration runs) and an optional `onRunCompleted` callback invoked once per completed iteration
from worker threads (thread-safe, cheap, non-throwing by contract — it carries no data, so it cannot
perturb the seed, the scheduling, or the reduced report; pinned by
`SimulationRunnerTests.Run_WithAndWithoutCallback_ProducesEquivalentReports_DeterminismUnperturbed`).
Both parameters default to inert, so `SimulateRunMode` and every sim-2 call site are unchanged.

### Outcome evaluation — one function, every surface

`ISimOutcomeEvaluator.Evaluate` reads `IBalanceStandardsRegistry.OutcomesFor` and computes: an
**equal-cell** verdict when both sides share a `(Tier, Band)` cell (win rate within tolerance of
`EqualCellWinRate`), a **one-band-higher** verdict when their `GlobalBandIndex`es differ by exactly
one (the higher side's win rate at or above `HigherBandWinRateFloor`), or a skipped-with-reason
verdict otherwise (no cell, no decisive runs, or an undefined band-index gap). Win rate excludes
draws (decisive-run share). This lives in the engine, not the CLI — the sim-3 editor and the
promoted CI invariants read the same verdict rows (INV-19); the math can never fork per surface.

### The report — a third durable artifact class, outside SQLite and world YAML

`SimulationReport` (`SchemaVersion: 1`; additive fields never bump it, breaking changes do) echoes
the scenario, the win/draw counts and rates, the two `DistributionStats`, and the verdict list.
`ISimReportWriter` serializes it to JSON (atomic tmp→rename) under `Simulation:ReportDirectory`,
filename `{timestamp}-{scenarioName}-{seed}.json`. Run history is the directory listing — the same
posture as the `generate` run-mode's output, deliberately outside SQLite (INV-14 is for live entity
state) and world YAML. **Sim-3 addition:** `ISimReportReader` (`List`/`Read`) is the read-side
counterpart, sharing the writer's extracted `SimReportJson` serializer options so a report reads back
identically whether the CLI or the editor wrote it — `List` never throws on an unparseable file
(flags it `Readable: false` instead), since a bad file must not break the whole history listing.

## Interface

- [`ScenarioDefinition.cs`](../../../Core/Modules/Simulation/ScenarioDefinition.cs) — `ScenarioKind`,
  `CombatantSourceKind`, `InlineStatBlock`, `CombatantSpec`, `ScenarioSide`, `ScenarioDefinition`.
- [`ISimScenarioStore.cs`](../../../Core/Modules/Simulation/Systems/ISimScenarioStore.cs) /
  [`SimScenarioStore.cs`](../../../Core/Modules/Simulation/Systems/SimScenarioStore.cs) — YAML load + validate.
- [`ISandboxWorldFactory.cs`](../../../Core/Modules/Simulation/Systems/ISandboxWorldFactory.cs) /
  [`SandboxWorldFactory.cs`](../../../Core/Modules/Simulation/Systems/SandboxWorldFactory.cs) /
  [`SandboxWorld.cs`](../../../Core/Modules/Simulation/Systems/SandboxWorld.cs) — per-run isolated world + graph.
- [`ISimCombatantFactory.cs`](../../../Core/Modules/Simulation/Systems/ISimCombatantFactory.cs) /
  [`SimCombatantFactory.cs`](../../../Core/Modules/Simulation/Systems/SimCombatantFactory.cs) — `ResolvedCombatant`, two-phase resolution.
- [`ISimCombatantPolicy.cs`](../../../Core/Modules/Simulation/Systems/ISimCombatantPolicy.cs) (+ `SimAction`) /
  [`MeleeOnlyPolicy.cs`](../../../Core/Modules/Simulation/Systems/MeleeOnlyPolicy.cs) /
  [`RoundRobinPolicy.cs`](../../../Core/Modules/Simulation/Systems/RoundRobinPolicy.cs) /
  [`CooldownFirstPolicy.cs`](../../../Core/Modules/Simulation/Systems/CooldownFirstPolicy.cs).
- [`CombatScenarioExecutor.cs`](../../../Core/Modules/Simulation/Systems/CombatScenarioExecutor.cs) (+ `RunRecord`) —
  single-run driver.
- [`SimSeeds.cs`](../../../Core/Modules/Simulation/Systems/SimSeeds.cs) — deterministic per-run seed derivation.
- [`ISimulationRunner.cs`](../../../Core/Modules/Simulation/Systems/ISimulationRunner.cs) /
  [`SimulationRunner.cs`](../../../Core/Modules/Simulation/Systems/SimulationRunner.cs) — batch orchestration.
- [`ISimOutcomeEvaluator.cs`](../../../Core/Modules/Simulation/Systems/ISimOutcomeEvaluator.cs) /
  [`SimOutcomeEvaluator.cs`](../../../Core/Modules/Simulation/Systems/SimOutcomeEvaluator.cs).
- [`SimulationReport.cs`](../../../Core/Modules/Simulation/SimulationReport.cs) — `SimVerdict`, `SimulationReport`.
- [`DistributionStats.cs`](../../../Core/Modules/Simulation/DistributionStats.cs).
- [`ISimReportWriter.cs`](../../../Core/Modules/Simulation/Systems/ISimReportWriter.cs) /
  [`SimReportWriter.cs`](../../../Core/Modules/Simulation/Systems/SimReportWriter.cs) /
  [`SimReportJson.cs`](../../../Core/Modules/Simulation/Systems/SimReportJson.cs) (sim-3, shared serializer options).
- [`ISimReportReader.cs`](../../../Core/Modules/Simulation/Systems/ISimReportReader.cs) /
  [`SimReportReader.cs`](../../../Core/Modules/Simulation/Systems/SimReportReader.cs) — sim-3, `List`/`Read`.
- [`SimulationOptions.cs`](../../../Core/Modules/Simulation/SimulationOptions.cs) — `Simulation:ReportDirectory`,
  `Simulation:ScenarioDirectory` (sim-3).
- [`SimulationModule.cs`](../../../Core/Modules/Simulation/SimulationModule.cs) — registered from
  `Server/CompositionRoot.Register` (not `Program.cs`), so `Hedron.Web` resolves `ISimulationRunner`
  directly (sim-3).
- [`Server/SimulateRunMode.cs`](../../../Server/SimulateRunMode.cs) — the CLI Initiator.
- [`Hedron.Web/Services/SimulationRunService.cs`](../../../Hedron.Web/Services/SimulationRunService.cs) /
  [`BaselineSweep.cs`](../../../Hedron.Web/Services/BaselineSweep.cs) /
  [`SimulationPrefill.cs`](../../../Hedron.Web/Services/SimulationPrefill.cs) — sim-3, the editor's
  background-run registry + scenario/prefill composers (this engine's second caller, alongside the CLI).
- [`Hedron.Web/Components/Pages/Simulation.razor`](../../../Hedron.Web/Components/Pages/Simulation.razor) —
  sim-3, the editor page.

## Considerations

- **Determinism (INV-26):** every chance path flows through a per-run `SeededRandom` derived by
  `SimSeeds.DeriveRunSeed`; no wall-clock reaches a run outcome (`IClock` only stamps the report's
  `GeneratedAt`). Parallel sweeps share no `IRandom`/`EntityService` across worlds.
- **Persistence:** none. Sandbox entities never carry `PersistentEntity`, there is no `SaveEntityAsync`
  call site in the module (INV-22 by absence), and the whole `EntityService` is discarded per run.
- **`SimulationReport`'s collection fields break record equality.** `Scenario.Sides` and `Verdicts`
  are lists/arrays; the compiler-generated `Equals` compares them by reference, not value. Tests that
  need "are these two reports the same" compare the scalar/`DistributionStats` fields and the
  verdicts' content directly — see `SimulationRunnerTests.AssertReportsEquivalent`.
- **Known calibration gap (tracked, not fixed here):** the Ascension tier baseline currently has no
  measurable effect on real combat outcomes — `AscensionEffectContributor` folds it onto `Body`/
  `HpMax` via `IStatSystem.Get`, but `StatSystem.GetEffectiveAttackPower`/`GetEffectiveDefense` read
  raw `Body`, and the combat HP/death check reads raw `PoolsComponent` values, never `Get(...)`. The
  promoted one-band-higher CI invariant pins today's actual number (53% at the fixed seed) rather
  than asserting the standards' 65% floor, which the mechanic can't currently clear. See
  [`../../roadmap/backlog.md`](../../roadmap/backlog.md#-ascension-tier-baseline-has-no-real-combat-effect-calibration-gap-found-by-sim-2).
- **Acknowledged debt (carried from the seed, deepened sim-3):** `SimScenarioStore`'s hand-rolled
  YAML load/validate/save is a second instance of the backlogged "YAML-authored definition pipeline
  for registry families" generalization (`BalanceStandardsStore` was the first); sim-3's `SaveAsync`
  is a second hand-rolled serializer, not a third family — still below the ≥3 trigger. The sandbox
  factory deliberately mirrors, rather than shares, the `Hedron.Tests` harness composition —
  unification is shape-for-later on a real ≥3 duplication signal.
- **Background execution in `Hedron.Web` (sim-3, watch item):** `SimulationRunService` is the web
  host's first background-job pattern (queue/progress/cancel over a long-running call). It is
  deliberately sim-specific, not a generic web-job framework — see
  [`../../architecture/08-blazor.md`](../../architecture/08-blazor.md) "Background tooling jobs" for
  the shape and the named promotion trigger (a second long-running editor job generalizes it).

## Extensibility

- **A second scenario kind (sim-4, progression-rate)** is an additive executor + report payload
  section keyed by `ScenarioKind` — the store, runner shell, and report envelope don't change.
- **N-vs-N combat** is additive data (`ScenarioSide.Combatants` is already a list); it activates when
  the combat model supports multi-opponent state (`CombatStateComponent` is single-opponent today).
- **A live-player-snapshot combatant source** is a fourth `CombatantSourceKind` — deferred, not
  built.
- **Template/instance conformance (sim-5)** reuses `IItemPowerProjectionSystem`/`IMobPowerProjectionSystem`
  and this engine's validated target ranges; nothing here needs to change for it to land.
- **A real `IAISystem` policy adapter** binds behind `ISimCombatantPolicy` without touching the
  executor or runner (already backlogged).

## Related

- Feature: [`simulation.md`](simulation.md)
- Flow: [Flow 33 — Simulation run journey](../../architecture/flows/flow-33-simulation-run.md)
- [`power-budget-system.md`](../progression/power-budget-system.md) — the sim-1 standards registry
  this engine's reference builds and verdicts read; the tier-baseline contributor referenced in the
  calibration-gap note above.
- Reference rows: [`systems.md`](../../reference/systems.md).
- [`../../implementation-plans/balance-simulator.md`](../../implementation-plans/balance-simulator.md) —
  the five-sub-slice program brief this is `sim-2` of (`sim-3` — this editor integration — is next).
- [`../../roadmap/completed/simulation-engine-core.md`](../../roadmap/completed/simulation-engine-core.md) ·
  [`../../roadmap/completed/simulation-editor-integration.md`](../../roadmap/completed/simulation-editor-integration.md) —
  as-built history and decisions for sim-2 and sim-3.
- [`../../architecture/08-blazor.md`](../../architecture/08-blazor.md) — the background-tooling-job
  shape sim-3's `SimulationRunService` introduced.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-2, INV-5, INV-10, INV-12
  (named sandbox-world nuance), INV-19, INV-22 (by absence), INV-25/26.
