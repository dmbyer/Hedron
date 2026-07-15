# Simulation engine core (slice sim-2, completed)

> Implemented on branch `claude/simulation-engine-core-9ahn2l`, 2026-07-15. Living docs:
> [`features/simulation/simulation.md`](../../features/simulation/simulation.md) ·
> [`features/simulation/simulation-engine.md`](../../features/simulation/simulation-engine.md) ·
> [`features/progression/power-budget-system.md`](../../features/progression/power-budget-system.md).

## Outcome

Lands the deterministic batch combat simulator as a Core module (`Core/Modules/Simulation/`) — the
engine behind every later balance-simulator surface (CLI now; Blazor editor sim-3; progression-rate
kind sim-4; template conformance sim-5). A designer runs `dotnet run --project Server -- simulate
--scenario <path> [--seed N]` to pit two combatants (mob template, sim-1 standards reference build,
or inline stat block) against each other over N iterations in isolated sandbox worlds, gets a
console summary (win rates, time-to-kill, expected-vs-actual verdicts), and a JSON report artifact.
The engine composes the same combat/stats/effects/aspects/abilities/entity-state/regeneration/
progression/ascension systems a live fight uses — no parallel math path — and never touches the
live world, the event bus, or persistence. Along the way, the very first real simulated combat run
surfaced a genuine calibration gap in the shipped Ascension tier baseline (see Decisions).

## Behavior digest

- **Postcondition 1** — `simulate` exits 0 (clean), 1 (engine failure), 2 (usage/scenario-invalid);
  publishes nothing; starts no listener/heartbeat.
- **Postcondition 2** — structural scenario violations (unknown kind, unknown policy id, empty
  side, ≠1 combatant per side for `Combat`, non-positive iterations/maxTicks, unresolvable
  combatant source) fail fast before any run executes.
- **Postcondition 3** — a fixed `(scenario, seed)` pair is byte-reproducible regardless of
  `maxParallelism`.
- **Postcondition 4** — every run gets its own `EntityService` + system graph; no sandbox entity
  ever carries `PersistentEntity`; the Simulation module never references `EcsManager`/`IEventBus`.
- **Postcondition 5** — all randomness derives from `SimSeeds.DeriveRunSeed(scenarioSeed, runIndex)`
  (never `HashCode.Combine`/`Random.Shared`).
- **Postcondition 6** — all three combatant sources resolve correctly; a reference-build
  combatant's `IStatSystem.Get` values match `ReferenceSnapshot(tier, band)` plus the tier baseline.
- **Postcondition 7** — the three built-in policies resolve by id; every one degrades to melee with
  an empty ability kit.
- **Postcondition 8** — the report carries schema version, scenario echo, win counts/rates + draws,
  TTK/damage distributions, and expected-vs-actual verdicts, computed inside the engine.
- **Postcondition 9** — the report is an atomically-written, timestamp/scenario/seed-keyed JSON
  file that round-trips on re-read.
- **Postcondition 10** — promoted CI invariants run the engine live at a fixed seed/small N against
  registry-sourced tolerances.
- **Postcondition 11** — all docs references to the superseded dedicated-`Hedron.Sim`-project shape
  updated to the Core-module shape (INV-20).

## Shipped pieces

| Surface | Location |
|---|---|
| `ScenarioKind`/`CombatantSourceKind`/`InlineStatBlock`/`CombatantSpec`/`ScenarioSide`/`ScenarioDefinition` (new) | `Core/Modules/Simulation/ScenarioDefinition.cs` |
| `SimulationOptions` (new, `Simulation:ReportDirectory`) | `Core/Modules/Simulation/SimulationOptions.cs` |
| `DistributionStats` (new) | `Core/Modules/Simulation/DistributionStats.cs` |
| `SimVerdict`/`SimulationReport` (new, schema v1) | `Core/Modules/Simulation/SimulationReport.cs` |
| `ISimScenarioStore`/`SimScenarioStore` (new) | `Core/Modules/Simulation/Systems/ISimScenarioStore.cs` · `SimScenarioStore.cs` |
| `ISandboxWorldFactory`/`SandboxWorldFactory`/`SandboxWorld` (new) | `Core/Modules/Simulation/Systems/ISandboxWorldFactory.cs` · `SandboxWorldFactory.cs` · `SandboxWorld.cs` |
| `ISimCombatantFactory`/`SimCombatantFactory`/`ResolvedCombatant` (new) | `Core/Modules/Simulation/Systems/ISimCombatantFactory.cs` · `SimCombatantFactory.cs` |
| `ISimCombatantPolicy`/`SimAction` + `MeleeOnlyPolicy`/`RoundRobinPolicy`/`CooldownFirstPolicy` (new) | `Core/Modules/Simulation/Systems/ISimCombatantPolicy.cs` (+ 3 files) |
| `CombatScenarioExecutor`/`RunRecord` (new) | `Core/Modules/Simulation/Systems/CombatScenarioExecutor.cs` |
| `SimSeeds.DeriveRunSeed` (new) | `Core/Modules/Simulation/Systems/SimSeeds.cs` |
| `ISimulationRunner`/`SimulationRunner` (new) | `Core/Modules/Simulation/Systems/ISimulationRunner.cs` · `SimulationRunner.cs` |
| `ISimOutcomeEvaluator`/`SimOutcomeEvaluator` (new) | `Core/Modules/Simulation/Systems/ISimOutcomeEvaluator.cs` · `SimOutcomeEvaluator.cs` |
| `ISimReportWriter`/`SimReportWriter` (new) | `Core/Modules/Simulation/Systems/ISimReportWriter.cs` · `SimReportWriter.cs` |
| `SimulationModule.AddSimulationModule` (new) | `Core/Modules/Simulation/SimulationModule.cs` |
| `SimulateRunMode` (new run-mode) + `Program.Main` branch | `Server/SimulateRunMode.cs` · `Server/Program.cs` |
| `CompositionRoot` — `SimulationOptions` configuration + module registration | `Server/CompositionRoot.cs` |
| `appsettings.json` (both hosts) — `Simulation:ReportDirectory` default | `Server/appsettings.json` · `Hedron.Web/appsettings.json` |
| Example scenario (new, checked in despite `/data/` being gitignored — see Decisions) | `data/sim/scenarios/example-equal-cell.yaml` |
| `ArchitectureGuardTests.Simulation_module_does_not_reference_EventBus_or_EcsManager` (new Tier-5 guard) | `Hedron.Tests/Architecture/ArchitectureGuardTests.cs` |
| `flow-33-simulation-run.md` (new) + index row | `docs/architecture/flows/flow-33-simulation-run.md` · `flows/README.md` |
| `docs/features/simulation/` (new feature) | `docs/features/simulation/simulation.md` · `simulation-engine.md` |
| Reference rows (7 new systems) | `docs/reference/systems.md` |
| `README.md` — `simulate` run-mode section + config table row | `README.md` |

## Tests shipped

All under `Hedron.Tests/Simulation/` (new) unless noted; 64 new tests, `dotnet test` green at 1128
total (up from 1064 pre-slice).

- **Tier 1** — `SimScenarioStoreTests` (valid load; seed override; each structural violation named
  in Postcondition 2 throws; missing file throws); `SimSeedsTests` (5 golden `DeriveRunSeed` values;
  stability across repeated calls; 200-sample distinctness); `SandboxWorldFactoryTests` (two worlds
  disjoint; tier-baseline fold reaches `IStatSystem.Get` through a freshly-built graph);
  `SimCombatantFactoryTests` (all three sources resolve/materialize correctly incl. the tier-baseline
  fidelity check against `ReferenceSnapshot`; unknown mob id / ability id / score id each throw);
  `SimPolicyTests` (all three built-ins, incl. cooldown/affordability skip logic and the
  empty-kit-degrades-to-melee case); `SimOutcomeEvaluatorTests` (equal-cell pass/fail/boundary,
  draws excluded from the ratio, +1-band pass/fail, missing-cell and undefined-gap skip reasons).
- **Tier 3** — `CombatScenarioExecutorTests` (lopsided matchup resolves deterministically on tick 1;
  damage-vs-HP-lost consistency under clamping; `maxTicksPerRun` cap yields a draw record; run index
  carried through); `SimulationRunnerTests` (same-scenario-same-seed and `maxParallelism` 1-vs-N both
  produce equivalent reports — compared field-by-field, see Decisions; schema version 1);
  `SimulationInvariantTests` (the two promoted CI invariants — see Decisions);
  `SimulateRunModeTests` (valid scenario → exit 0 + report file; seed override honored; structural
  invalid / missing file / missing arg → exit 2; token recognition).
- **Tier 4** — `SimReportWriterTests` (write→re-read round-trips schema version + aggregates; no
  leftover `.tmp`; two writes → two files).
- **Tier 5** — `ArchitectureGuardTests.Simulation_module_does_not_reference_EventBus_or_EcsManager`
  (reflection scan for `IEventBus` ctor params/fields + source-text scan for `EcsManager`).
- **Manual verification** — the example scenario ran end to end on a clean checkout (200 iterations,
  0 draws, `equalCellWinRate` verdict passed); a 10k-iteration local timing came in under 4 seconds
  wall-clock (dominated by process startup, not sandbox construction — no pooling needed).

## Decisions

- **`AttackPower`/`Defense` fold as synthetic permanent effects, not component overwrites.** Both
  are computed scores (`StatSystem` derives them from raw `Body`), so a reference build's gear bonus
  beyond the base derivation applies via `IEffectSystem.Apply` with `Duration: -1f` (→
  `EffectLifetime.UntilRemoved`) and the `"fixed"` power-scaling formula — the same seam
  `EquipmentEffectContributor` reads. This keeps `IStatSystem.Get` the only path a value reaches a
  sandbox combatant (no bypass write), matching the plan's "must surface via `IStatSystem.Get`"
  constraint.
- **Policies take a `roundIndex` parameter instead of holding mutable state.** The plan didn't fully
  specify `RoundRobinPolicy`'s cycling mechanism; since `ISimCombatantPolicy` instances are DI-shared
  across every concurrently-running sandbox world, any per-actor memory had to be either
  world-state-derived or parameter-derived, never an instance field (would break both determinism
  and thread-safety under `Parallel.For`). `ChooseAction(world, selfId, opponentId, roundIndex)`
  computes the cycle position as `roundIndex % known.Count` — pure, stateless, trivially testable.
- **`SimulationReport`'s array/list fields break record-generated equality — tests compare fields,
  not the whole record.** `ScenarioDefinition.Sides` and `SimulationReport.Verdicts` are
  lists/arrays; C#'s compiler-generated record `Equals` compares them by reference. Two independently
  produced (but content-identical) reports therefore fail a naive `Assert.Equal(reportA, reportB)`.
  `SimulationRunnerTests.AssertReportsEquivalent` compares the scalar/`DistributionStats` fields
  directly and the verdicts by `(Name, Passed, Reason)` tuple sequence instead.
- **Discovered mid-implementation: the Ascension tier baseline has no measurable effect on real
  combat outcomes.** The first real simulated fight between a Tier-3 and a Tier-2 reference build
  (the one global-band-index-gap pair that also crosses a tier boundary) produced an *identical*
  win/loss sequence to the equal-cell control at the same seed — proof, not just suspicion, that the
  tier gap changes nothing. Root cause: `AscensionEffectContributor` folds
  `TierBaselineStep × tier` onto `ScoreId.Body`/`HpMax` via `IStatSystem.Get`, but
  `StatSystem.GetEffectiveAttackPower`/`GetEffectiveDefense` read the **raw** `AttributesComponent.Body`
  (not `Get(Body)`), and `CombatSystem`'s HP/death check reads the **raw** `PoolsComponent` values
  (not `Get(HpMax)`). This is pre-existing shipped behavior from `prog-2`/`prog-3b`, not a sim-2
  regression. **User-directed resolution (asked via `AskUserQuestion` mid-implementation): pin
  today's real number rather than fix the mechanic or skip the test.** The promoted
  `OneBandHigher_ReferenceBuild_WinRate_PinnedPendingBalanceTuning` invariant asserts the actual
  measured 53%-vs-47% split (and `verdict.Passed == false` against the standards' 65% floor)
  as a golden regression pin, with the finding recorded in
  [`../backlog.md`](../backlog.md#-ascension-tier-baseline-has-no-real-combat-effect-calibration-gap-found-by-sim-2)
  for a future balance-tuning slice to resolve (extend `TrackedScores` to cover `AttackPower`/
  `Defense`, recalibrate the floor, or both). Recalibrating shipped gameplay balance constants was
  judged out of scope for an engine-plumbing slice.
- **Example scenario checked in under `/data/`, which is otherwise gitignored.** INV-18 requires the
  scenario YAML shape to ship with a working example, but the repo's `.gitignore` ignores the whole
  `/data/` tree (runtime/local content, mirroring `data/balance/standards.yaml`'s precedent of
  *not* being checked in). Rather than relocating the example under `content/` (breaking the plan's
  literal `data/sim/scenarios/` path) or leaving it untracked (failing INV-18), `.gitignore` gained a
  narrow, verified negation chain (`/data/*` → `!/data/sim/` → `/data/sim/*` → `!/data/sim/scenarios/`
  → `/data/sim/scenarios/*` → `!/data/sim/scenarios/example-equal-cell.yaml`) that tracks exactly
  this one file while leaving `data/content/`, `data/hedron.db`, and `data/sim/reports/` ignored —
  confirmed via `git check-ignore`/`git add -n` before committing.
- **`services.AddLogging()` added to `SimulateRunMode`'s minimal DI setup, unlike `GenerationRunMode`.**
  `ISimCombatantFactory` resolves the real `IContentDefinitionCatalog` (for the mob-template
  combatant source), whose deserializers take `ILogger<T>` by constructor injection.
  `GenerationRunMode` never hits this because it side-steps DI-resolving the catalog (its own
  validation path constructs deserializers directly with a null logger). Found by actually running
  the example scenario end to end, not just by unit tests (which each construct their own
  dependencies directly and never exercised the full `SimulateRunMode.RunAsync` DI graph until
  `SimulateRunModeTests` was written).
- **1v1 enforced structurally, not just documented.** `ISimScenarioStore.Validate` throws for a
  `Combat`-kind scenario with other than exactly 2 sides or other than exactly 1 combatant per side —
  matching `CombatStateComponent`'s single-opponent model. N-vs-N stays additive data (`ScenarioSide.Combatants`
  is already a list) for a later slice.

## Deviations / Follow-ups

- **No deviations from the plan's shape.** All three work packages (scenario model + sandbox
  substrate; batch runner + statistics + reports; `simulate` run-mode + CI invariants + docs)
  shipped as scoped; every Test-plan item (1–13) is present, and the two resolved open questions
  (ability-kit activation, regeneration in the tick pipeline) were implemented exactly as the plan
  specified.
- **Follow-up (tracked in `backlog.md`):** the Ascension tier-baseline calibration gap discovered
  above needs a balance-tuning slice to resolve — either extend the tier-baseline contributor's
  tracked scores to reach `AttackPower`/`Defense` (and the HP/death check to read `Get(HpMax)`), or
  recalibrate `HigherBandWinRateFloor` down to what the current mechanic can clear, or both.
- **Follow-up:** `sim-3` (Blazor editor integration) is next in the `balance-simulator` program — it
  composes/launches scenarios from `Hedron.Web`, adds "simulate this" entry points on `MobEditor`/
  `ItemEditor`, and gains a run-history viewer over this slice's report artifacts.
- The sandbox-factory ↔ test-harness unification and the `SimScenarioStore`/`BalanceStandardsStore`
  YAML-pipeline generalization remain acknowledged debt at their pre-existing backlog entries — this
  slice added an instance count, didn't build the framework (per the seed's own family-disposition
  table).
