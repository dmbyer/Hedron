# Simulation engine core (sim-2)

**Status:** planned
**Actors:** Administrator/Designer (authors scenario YAML, runs `simulate`, inspects report artifacts) · System (batch runner, promoted CI invariants) · Future callers: Blazor Simulation page (sim-3), progression-rate scenarios (sim-4), procedural mob generator (inline-spec validation entry)
**Module:** `Core/Modules/Simulation/` (new); `Server` (`simulate` run-mode); reads `Core/Modules/BalanceInspection/Standards/` (sim-1 registry); composes existing Combat/Stats/Effects/Aspects/Abilities/Progression/Ascension/Regeneration/EntityState systems into sandbox worlds

> Sub-slice **sim-2** of the [balance-simulator program seed](balance-simulator.md) (prog-4, advisor intake 2026-07-13). The seed's Design notes, Architecture brief, and Resolved decisions are authoritative inputs to this plan; three further user-confirmed decisions (2026-07-14) are folded in below — hand-built sandbox factory (seed OQ3), JSON report artifacts (seed OQ4a), live small-scale CI runs (seed OQ4b).

---

## Description

Lands the deterministic batch combat simulator as a Core module — the engine behind every later surface (CLI now; Blazor page sim-3; progression-rate kind sim-4; generator validation entry later). A data-keyed `ScenarioDefinition` (YAML-authorable, editor-composable, generator-constructable) names a scenario **kind** (combat now; the kind seam is explicit from day one), sides-as-lists of combatant specs (1v1 enforced in sim-2; N-vs-N is additive data), per-combatant policies, termination condition, iteration count, and seed. Each run composes an **isolated sandbox world** — a fresh `EntityService` plus a hand-built system graph mirroring the `Hedron.Tests` harness composition — never the host's live world (the INV-12 nuance: sim worlds are explicitly *not* the "one live world"; the engine never resolves the host's `EntityService` and never calls `EcsManager.SetWorld`). Per-run `IRandom` instances are derived deterministically from scenario seed + run index (INV-26); runs advance on synthetic ticks (the heartbeat is never involved) and fan out in parallel because worlds share nothing. The runner aggregates win rates, time-to-kill, and damage distributions, evaluates expected-vs-actual verdicts against the sim-1 standards registry's outcome tolerances, and returns a versioned report record; the `simulate` run-mode (no-chain Initiator, INV-10, precedent: `generate`) writes it as a JSON artifact to a reports directory — run history is a directory listing. A thin promoted CI-invariant subset in `Hedron.Tests` invokes the engine directly with fixed seeds and small iteration counts.

---

## Preconditions

- sim-1 shipped: `IBalanceStandardsRegistry` (reference builds via `ReferenceSnapshot(tier, band)`, `OutcomesFor(tier, band)` tolerances), `PowerBudgetTunables`-injected oracle, compiled defaults as fallback.
- Combat/stats/effects/aspects/abilities/progression/ascension/regeneration/entity-state systems all take `EntityService` (and each other) by constructor — proven per-instance composable by the existing test harness (`CombatFlowTests.TestWorld`).
- `SeededRandom : IRandom` exists (`Core/Systems/`, from bulk-content-generation).
- `IContentDefinitionCatalog.Load(ContentKind.Mob, blueprintId)` reads an authored mob template from YAML without touching the live world.
- The `generate` run-mode precedent exists in `Server/Program.cs` + `GenerationRunMode.cs`.

## Postconditions

1. `dotnet run --project Server -- simulate --scenario <path> [--seed N]` runs one scenario end to end and exits 0 (clean run), 1 (engine failure), 2 (usage/scenario-invalid) — publishing nothing, starting no listener/heartbeat (INV-10).
2. A structurally invalid scenario file (unknown kind, unknown policy id, empty side, ≠1 combatant per side, non-positive iterations/maxTicks, unresolvable combatant source) fails fast with a named violation before any run executes.
3. A fixed (scenario, seed) pair is reproducible end to end: two `ISimulationRunner.Run` calls produce identical run outcomes and aggregates (win counts, per-run TTK sequence, damage totals) — regardless of parallelism degree.
4. Each run executes in its own `EntityService` + hand-built system graph; entities created in one run's world are invisible to every other run and to any host world; no sandbox entity ever carries `PersistentEntity`; the Simulation module never references `EcsManager` or `IEventBus`.
5. Per-run randomness comes only from a `SeededRandom` seeded by a stable pure derivation `DeriveRunSeed(scenarioSeed, runIndex)` (never `HashCode.Combine`, never `Random.Shared` — INV-26).
6. Combatant specs resolve from all three day-one sources: (a) authored mob-template id, (b) sim-1 reference build (base scores + gear bonuses + tier baseline folded exactly as live snapshots fold them), (c) inline stat block. A reference-build combatant's `IStatSystem.Get` values inside the sandbox match `IBalanceStandardsRegistry.ReferenceSnapshot(tier, band)` + the tier baseline.
7. Policies resolve by id (`melee-only`, `round-robin`, `cooldown-first`); a combatant with no usable ability degrades to melee under every built-in; the `ISimCombatantPolicy` seam is shaped so a future `IAISystem` adapter binds behind it (backlogged, not built).
8. The runner's `SimulationReport` carries: schema version (`1`), scenario echo, per-side win counts/rates + draw count, TTK distribution (mean/median/p10/p90/min/max), per-combatant damage dealt/taken distributions, and expected-vs-actual verdicts computed from `IBalanceStandardsRegistry.OutcomesFor` whenever both sides resolve to a (Tier, Band) cell (skipped-with-reason otherwise). Verdict math lives in the engine, not the caller (INV-19 — CLI, editor, and CI can never drift).
9. The report artifact is a JSON file in the configured reports directory (`Simulation:ReportDirectory`, default `data/sim/reports`), atomically written, filename-keyed by timestamp + scenario name + seed; re-reading it round-trips the aggregates.
10. Promoted CI invariants in `Hedron.Tests` run the engine live at fixed seed/small N and assert the registry's tolerances (equal-cell win rate within tolerance; +1-band winner ≥ floor); no committed threshold artifacts.
11. All docs references to the superseded dedicated-`Hedron.Sim` project shape are updated to the Core-module shape (INV-20; ship-time edits — see Flows/docs section).

---

## Main flow (CLI batch run)

1. Admin runs `dotnet run --project Server -- simulate --scenario data/sim/scenarios/equal-cell-t2.yaml [--seed N]`. `Program.Main` branches to `SimulateRunMode` before the listener host is built (precedent: `generate`).
2. The run-mode composes DI via `CompositionRoot.Register` (no gameplay hosted services), resolves `ISimScenarioStore`, and loads + validates the scenario YAML (fail-fast → exit 2). `--seed` overrides the file's seed.
3. The run-mode calls `ISimulationRunner.Run(scenario)`. The runner dispatches on `ScenarioKind` (combat → `CombatScenarioExecutor`; any other kind → fail-fast — the sim-4 seam).
4. The runner resolves every combatant spec **once** into a `ResolvedCombatant` intermediate (mob template via `IContentDefinitionCatalog`, reference build via `IBalanceStandardsRegistry`, inline block as-is) — so the per-run hot path does no file/registry I/O.
5. Runs fan out in parallel (bounded by processor count; worlds share nothing). For each run index *i*: derive `DeriveRunSeed(seed, i)` → new `SeededRandom` → `ISandboxWorldFactory.Create(random)` → a fresh `EntityService` + hand-built graph (Effect+contributors, Attribute, Stat, Aspect, Combat, Ability, EntityState, Regeneration, Progression, Ascension) → `ISimCombatantFactory` stamps each `ResolvedCombatant` into the world (attributes, pools, mob archetype, gear-bonus fold, tier via `AscensionComponent`, ability kit learned).
6. Each run: `StartCombat` + enter `InCombat` state; then loop synthetic ticks — advance effects (`IEffectSystem.AdvanceTick` + apply due periodics), advance ability cooldowns, draw initiative order from the run's `IRandom`, each living combatant's policy chooses an action (melee → `ExecuteRound`; ability → `Activate` + `ResolveAbilityStrike` for offensive), apply regeneration — until one side is eliminated (`MobDied`) or `maxTicksPerRun` is hit (draw/timeout).
7. The runner collects per-run records **indexed by run index** and reduces them deterministically into the `SimulationReport` (win rates, TTK/damage distributions), then attaches expected-vs-actual verdicts via `ISimOutcomeEvaluator` against `IBalanceStandardsRegistry.OutcomesFor` when both sides carry a resolvable cell.
8. The run-mode calls `ISimReportWriter.WriteAsync(report)` (atomic tmp→rename JSON into `Simulation:ReportDirectory`), prints a console summary (outcome counts, TTK, verdicts), and exits 0.

---

## Events fired

**None.** The engine's systems return results (INV-5); the `simulate` run-mode is a no-chain Initiator (INV-10). No live-world observer exists for an offline sim — editor-run completion (sim-3) is a UI concern in `Hedron.Web`, not a bus fact. An architecture-guard test enforces that no `Core/Modules/Simulation/` type references `IEventBus`.

## Systems / handlers involved

**New (all in `Core/Modules/Simulation/`, registered by `SimulationModule.AddSimulationModule` from `CompositionRoot.Register` so `Hedron.Web` resolves them at sim-3):**

- `ISimScenarioStore` / `SimScenarioStore` — YAML load + fail-fast structural validation of `ScenarioDefinition` (posture mirrors `BalanceStandardsStore`: validate-then-use, named violations). `Load(path)`; `Validate(ScenarioDefinition)` also callable on an in-memory definition (the sim-3 editor and generator entry).
- `ISandboxWorldFactory` / `SandboxWorldFactory` — `Create(IRandom random) → SandboxWorld`. Hand-news-up per world: `EntityService`, `EffectSystem` (with per-world `EquipmentEffectContributor`, `AbilityEffectContributor`, `ProgressionEffectContributor`, `AscensionEffectContributor`), `AttributeSystem`, `StatSystem`, `AspectSystem`, `CombatSystem(ecs, stats, attributes, aspects, random)`, `AbilitySystem`, `EntityStateService`, `RegenerationSystem`, `ProgressionSystem`, `AscensionSystem`. Shares only immutable singletons across worlds (`IAbilityRegistry`, `IEffectRegistry`, `IAspectRegistry`, `IPowerBudgetSystem`, `IOptions<DeathOptions>`). Never calls `EcsManager.SetWorld`. `SandboxWorld` is a plain record-of-systems facade the executor drives.
- `ISimCombatantFactory` / `SimCombatantFactory` — two-phase: `Resolve(CombatantSpec) → ResolvedCombatant` (reads `IContentDefinitionCatalog` / `IBalanceStandardsRegistry` once per scenario); `Materialize(SandboxWorld, ResolvedCombatant) → uint entityId` (stamps `MobDataComponent` + `AttributesComponent` + `PoolsComponent` + `LocationComponent` (shared sandbox arena room entity) + `AscensionComponent { Tier }` when the spec carries a tier + gear-bonus fold + `Learn` per ability-kit id). Fail-fast on unresolvable template id / unknown ability id / unknown score id.
- `ISimCombatantPolicy` (seam) + built-ins `MeleeOnlyPolicy`, `RoundRobinPolicy`, `CooldownFirstPolicy` — `PolicyId` + `ChooseAction(SandboxWorld, selfId, opponentId) → SimAction` (`MeleeAttack` | `UseAbility(abilityId)`); DI-collected `IEnumerable<ISimCombatantPolicy>` keyed by id in a small policy registry. Default when unspecified: `cooldown-first` (degrades to melee with an empty kit). The future `IAISystem` adapter binds behind this same seam (already backlogged — not built).
- `ISimulationRunner` / `SimulationRunner` — kind dispatch, spec pre-resolution, parallel fan-out, deterministic reduce, verdict attachment. Internal `CombatScenarioExecutor.ExecuteRun(SandboxWorld, combatants, policies, maxTicks, IRandom) → RunRecord` is separately unit-testable. Optional `maxParallelism` parameter (tests pin it to 1 vs N for the determinism proof).
- `ISimOutcomeEvaluator` / `SimOutcomeEvaluator` — pure: aggregates + `OutcomeTolerances` → verdict rows (`equalCellWinRate` within `WinRateTolerance` of `EqualCellWinRate`; `higherBandWinRateFloor` when cells differ by exactly one global band index; skipped-with-reason otherwise). Win rate for verdicts = decisive-run share (draws reported separately, excluded from the ratio).
- `ISimReportWriter` / `SimReportWriter` — serializes `SimulationReport` to JSON (`System.Text.Json`, camelCase), atomic tmp→rename into `Simulation:ReportDirectory`; filename `{yyyyMMdd-HHmmssZ}-{scenarioName}-{seed}.json`.
- `SimulationOptions` — `Simulation:ReportDirectory` (Category 1 config key, default `data/sim/reports`), bound in `CompositionRoot`.
- Static pure `SimSeeds.DeriveRunSeed(int scenarioSeed, int runIndex)` — SplitMix64-style stable mix; explicitly **not** `HashCode.Combine` (process-randomized) — golden-tested.

**Data records (module root):** `ScenarioDefinition` (`Kind`, `Name`, `Seed`, `Iterations`, `MaxTicksPerRun`, `Sides: IReadOnlyList<ScenarioSide>`), `ScenarioSide` (`Combatants: IReadOnlyList<CombatantSpec>`), `CombatantSpec` (`Source: MobTemplate|ReferenceBuild|Inline`, source fields, optional `Tier`/`Band` cell, `PolicyId`), `ScenarioKind` enum (`Combat`; `ProgressionRate` reserved for sim-4), `SimulationReport` + nested stat records (`SchemaVersion = 1`), `RunRecord`, `SimVerdict`.

**Reused (unchanged interfaces):** `ICombatSystem`, `IStatSystem`, `IAttributeSystem`, `IEffectSystem` (+ the four contributors), `IAspectSystem`, `IAbilitySystem`, `IEntityStateService`, `IRegenerationSystem`, `IProgressionSystem`, `IAscensionSystem`, `IBalanceStandardsRegistry`, `IContentDefinitionCatalog`, `SeededRandom`, `IClock` (report metadata timestamp only — never run outcomes), `IPowerBudgetSystem` (via `ProgressionSystem`).

**New Initiator:** `Server/SimulateRunMode.cs` + the `Program.Main` branch (mirrors `GenerationRunMode.Matches`/`RunAsync`).

**Handlers:** none new; none reused — the executor performs the per-tick sequence directly (effects → cooldowns → actions → regen), deliberately mirroring the heartbeat handlers' order without the bus.

**Components:** none new. Sandbox entities compose existing components (`MobDataComponent`, `AttributesComponent`, `PoolsComponent`, `LocationComponent`, `AscensionComponent`, `CombatStateComponent`, `EntityStateComponent`, effects state). No `[Persistent]` changes.

---

## Implementation plan — work packages

### WP-1 — Scenario model + sandbox substrate

**Scope:** `Core/Modules/Simulation/` skeleton: `ScenarioDefinition`/`ScenarioSide`/`CombatantSpec`/`ScenarioKind` records; `ISimScenarioStore` (YAML DTO + validation); `ISandboxWorldFactory` + `SandboxWorld`; `ISimCombatantFactory` (Resolve/Materialize); `ISimCombatantPolicy` + three built-ins + policy registry; `SimulationOptions`; `SimulationModule.AddSimulationModule` wired in `CompositionRoot.Register` + `appsettings.json` (both hosts).
**Files:** `Core/Modules/Simulation/*.cs`, `Core/Modules/Simulation/Systems/*.cs`, `Server/CompositionRoot.cs`, `Server/appsettings.json`, `Hedron.Web/appsettings.json`.
**Out of scope:** runner, reports, run-mode.
**Exit criterion:** Tier-1 tests green — scenario validation matrix; two sandbox worlds isolated; reference-build combatant's `IStatSystem.Get` matches `ReferenceSnapshot` + tier baseline; policy decision table.

### WP-2 — Batch runner + statistics + report artifacts

**Scope:** `SimSeeds.DeriveRunSeed`; `CombatScenarioExecutor` (synthetic-tick pipeline, initiative draw, termination); `ISimulationRunner` (kind dispatch, pre-resolution, parallel fan-out with `maxParallelism` knob, deterministic index-ordered reduce); distribution math (mean/median/p10/p90/min/max); `ISimOutcomeEvaluator`; `SimulationReport` schema v1; `ISimReportWriter` (atomic JSON).
**Depends on:** WP-1.
**Out of scope:** CLI, CI-invariant promotion.
**Exit criterion:** Tier-1 + Tier-3 tests green — seed-derivation golden; single-run executor on a lopsided matchup; same-seed twice → identical report; parallelism 1 vs N → identical report; Tier-4 report write→read round-trip.

### WP-3 — `simulate` run-mode + promoted CI invariants + docs

**Scope:** `Server/SimulateRunMode.cs` + `Program.Main` branch; example scenario at `data/sim/scenarios/example-equal-cell.yaml`; `SimulateRunModeTests` (exit-code contract + report artifact, mirroring `GenerationRunModeTests`); promoted CI-invariant tests (live small-N runs against registry tolerances from compiled defaults); architecture-guard test (no `IEventBus`/`EcsManager` reference in the Simulation module); new `flow-33` file + index row; reference-catalog rows (`systems.md`); `README.md` run-mode section gains the `simulate` entry beside `generate`; INV-20 sweep — update `Hedron.Sim` references in `docs/roadmap/backlog.md` and `docs/implementation-plans/progression-and-balance.md` to the Core-module shape (`.claude/` verified clean by grep — no tooling edits needed); manual 10k-run construction-cost check recorded in the completed record.
**Depends on:** WP-2.
**Exit criterion:** `simulate` runs the example scenario end to end on a clean checkout; `dotnet test` green; primary agent runs `architecture-reviewer` (code mode) across the combined diff.

**Dependency order:** WP-1 → WP-2 → WP-3.

---

## Content tooling impact (INV-18)

- **New authored data shape: scenario YAML.** CamelCase, same convention as content/profile files; documented shape (kind, name, seed, iterations, maxTicksPerRun, sides → combatants → source/policy/cell) ships with a working example at `data/sim/scenarios/example-equal-cell.yaml` (reference build T2·B2 vs itself, cooldown-first). Designers author by hand in sim-2; the Blazor compose/launch surface is **sim-3 by program order** (the engine-before-editor rationale in the seed) — the CLI + example file is the sim-2 authoring/inspection surface.
- **How a designer runs it:** `dotnet run --project Server -- simulate --scenario <path> [--seed N]`.
- **How a designer inspects it:** the console summary (outcomes, TTK, verdicts) plus the JSON report artifact in `data/sim/reports/` — run history is the directory listing (same posture as `generate` output: files to read, diff, keep, or discard; not SQLite, not world YAML).
- **New config key:** `Simulation:ReportDirectory` (Category 1, default `data/sim/reports`), both hosts' `appsettings.json`.
- No `TemplateRegistry` entries, no admin commands, no archetype changes.

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Notes |
|---|---|---|
| Commands | **Adequate** | No in-game command; the CLI run-mode follows the established `generate` Initiator precedent. |
| Output | **Adequate** | Console summary + file artifacts, matching `generate`; no `IOutputMessage`/broadcast involvement. |
| Persistence | **Adequate** | Untouched. Sandbox entities are a deliberately ephemeral third domain: never `PersistentEntity`, never `SaveEntityAsync`, no SQLite rows; worlds are discarded after the run. No component `[Persistent]` status changes. See Persistence audit below. |
| Event bus | **Adequate** | Engine publishes nothing (INV-5); run-mode is no-chain (INV-10); enforced by a new architecture-guard test. |
| ECS queries / world model | **Adequate — named INV-12 nuance** | Sandbox worlds are explicitly not the "one live world"; the engine never resolves the host `EntityService` and never calls `EcsManager.SetWorld` (guard-tested). The feature doc states this boundary on ship. |
| Broadcast / sessions | **Adequate** | Untouched. |
| Time | **Adequate** | Synthetic tick counter; heartbeat never involved; `IClock` only stamps report metadata (outcome determinism is seed-only, INV-26). |
| Content templates | **Adequate** | Mob-template combatant source reads through the existing `IContentDefinitionCatalog.Load` — no live spawn, no `TemplateRegistry`. |
| Configuration | **Adequate** | One new Category-1 key via the standard `IOptions` pattern. |
| Modules | **Adequate** | Standard `AddSimulationModule` extension registered in `CompositionRoot` (both hosts resolve it — the sim-3 prerequisite). |
| YAML definition loading | **Acknowledged debt** | `SimScenarioStore` is hand-rolled load/validate — instance #2 of the backlogged "YAML-authored definition pipeline for registry families" generalization (`BalanceStandardsStore` was #1; `GenerationRunMode`'s profile DTO is a cousin). The ≥3 trigger is now adjacent; already tracked in `backlog.md` — this slice adds the instance count to that entry, does not build the framework. |
| Sandbox factory ↔ test harness | **Acknowledged debt (seed-resolved)** | The factory deliberately mirrors `Hedron.Tests` composition (resolved decision: hand-built, no scoped DI container). Unification is shape-for-later on a real ≥3 duplication signal — per the seed's family table; no new backlog entry needed. |

**Persistence opt-in audit.** Level 1: the only entity-construction path added is sandbox combatants + one arena room per world — neither world content (no YAML/`TemplateRegistry` provenance in the live world) nor persistent (no `PersistentEntity`, ever); the whole `EntityService` is discarded post-run; no domain transition is possible (sandbox entities never enter a player inventory — no session exists). Level 2: zero new components; all touched components keep their existing `[Persistent]` status (sandbox instances of them are unreachable by the flush because the persistence system only sweeps the host world's `EntityService`). Level 3: no `SaveEntityAsync` call sites anywhere in the slice — compliant with INV-22 by absence.

## Flows introduced or modified (INV-17)

- **New: `flow-33-simulation-run.md` — Simulation run journey.** Trigger: `dotnet run --project Server -- simulate --scenario <path>`. Traces: run-mode DI composition (no hosted services) → scenario load/validate → kind dispatch → per-run sandbox world + seeded random + combatant materialization → synthetic-tick combat loop → deterministic reduce → verdict evaluation → JSON artifact + console summary. Index row added. (Kept separate from flow-29's content-tooling journey; sim-3 extends flow-33 with the editor leg.)
- **Unmodified but verified:** flow-01 (server startup — `AddSimulationModule` adds registrations only, no boot step, no eager resolution); flow-16/17 (heartbeat/combat — untouched; the executor mirrors, never subscribes).

## Test plan / Verification (INV-25 / INV-26)

Tier names per [`../architecture/07-testing.md`](../architecture/07-testing.md).

1. **Tier 1 — `SimScenarioStoreTests`:** valid file loads with expected fields; each structural violation (unknown kind, unknown policy, empty side, 2 combatants on a side, iterations ≤ 0, maxTicks ≤ 0, unknown source discriminator) throws with a named violation (Postcondition 2); seed override honored.
2. **Tier 1 — `SimSeedsTests`:** golden values for `DeriveRunSeed` (stability across processes is the point); distinct run indexes → distinct seeds for a sample range.
3. **Tier 1 — `SandboxWorldFactoryTests`:** two `Create` calls yield disjoint worlds (entity in one invisible in the other — Postcondition 4); graph wiring smoke (a combatant's gear/ability/tier folds appear in `IStatSystem.Get`).
4. **Tier 1 — `SimCombatantFactoryTests`:** reference-build source → `Get` values equal `ReferenceSnapshot(tier, band)` + tier baseline (Postcondition 6); mob-template source mirrors template attrs/pools/tier/band; inline source stamps declared values; ability kit learned (`IAbilitySystem.IsKnown`); unknown template id / ability id / score id throws.
5. **Tier 1 — policy tests:** `CooldownFirstPolicy` picks the first off-cooldown, affordable ability, else melee; `RoundRobinPolicy` cycles; all three return melee with an empty kit (Postcondition 7).
6. **Tier 1 — `SimOutcomeEvaluatorTests`:** equal-cell pass/fail at the tolerance boundary; +1-band floor pass/fail; skipped-with-reason when a side has no cell; draws excluded from the decisive-share ratio.
7. **Tier 3 — `CombatScenarioExecutorTests`:** lopsided matchup (high-Body vs 1 HP) terminates with the expected winner within a tick bound; TTK and damage totals in the `RunRecord` are consistent with HP lost; maxTicks cap yields a draw record.
8. **Tier 3 — `SimulationRunnerTests` (determinism, Postconditions 3, 5):** same scenario + seed run twice → byte-equal aggregates and identical per-run outcome sequence; `maxParallelism: 1` vs `N` → identical report.
9. **Tier 3 — promoted CI invariants (`SimulationInvariantTests`, Postcondition 10):** live engine runs at fixed seed, small N (≈200), registry composed from `BalanceStandardsDefaults` — equal-cell reference 1v1 win rate within `OutcomesFor` tolerance; +1-band attacker ≥ `HigherBandWinRateFloor`. Deliberately thin — heavy sweeps stay out of CI (seed brief).
10. **Tier 4 — `SimReportWriterTests`:** write → re-read round-trips schema version + aggregates (Postcondition 9); atomic write leaves no `.tmp`; two writes → two files (run history).
11. **Tier 5 — architecture guard:** reflection/file-scan asserting no `Core/Modules/Simulation/` type references `IEventBus` or `EcsManager` (Postcondition 4, plus Postcondition 1's no-publish clause).
12. **Tier 3 — `SimulateRunModeTests` (Postcondition 1, mirroring `GenerationRunModeTests`):** valid example scenario → exit 0 and a report artifact written to the configured directory; structurally invalid scenario → exit 2; missing/unreadable scenario path → exit 2.
13. **Manual verification (recorded in the completed record, not CI):** the example scenario end-to-end on a clean checkout; a 10k-iteration local run timing the sandbox construction cost (resolved decision 1's "validate at 10k-run scale" — expected fine given `EntityService` is a plain in-memory store; if construction dominates, pooling is a named follow-up, not a redesign).

**Skipped, with rationale:** CLI flag-parsing details beyond the exit-code contract exercised by `SimulateRunModeTests` (presentation-level plumbing); exact console prose (presentation); the reused combat/stats/effects systems' own logic (already covered by their suites — the sim composes, doesn't reimplement); statistical significance of tolerance checks at CI's small N (the seed + N are chosen once so the fixed-seed outcome is a regression pin, not a hypothesis test — documented in the test).

**Testability gaps:** none — every chance path flows through the injected `IRandom` (per-run `SeededRandom`); no wall-clock reaches run outcomes; the runner exposes `maxParallelism` so determinism is provable single-threaded.

---

## Design notes

> Durable rationale — disintegrates into `docs/features/` (new `simulation` feature doc + `simulation-engine.md` system doc) and the seed's notes on ship (INV-28).

- **Hand-built sandbox factory, no per-world DI container (resolved 2026-07-14, seed OQ3).** The factory news-up the graph exactly as `CombatFlowTests.TestWorld` does — the composition that already proves these systems are per-instance composable. A scoped-container-per-run would add container overhead ×10k and a second registration surface to keep in sync. Factory↔harness unification stays shape-for-later (mirror deliberately; unify on a real ≥3 duplication signal).
- **The executor is a synthetic heartbeat, not a subscriber.** It performs the per-tick sequence (effects → cooldowns → actions → regen) in the same order the heartbeat handlers run, but calls system methods directly — no bus, no handlers, no `HeartbeatTickEvent`. This is what makes runs synchronous, deterministic, and parallelizable.
- **Combatants are mob-archetype entities — including "synthetic players".** `CombatRoundOutcome.MobDied` gives clean termination; the player death lifecycle (incapacitate → bleed → respawn) is deliberately out of sim scope (no `IDeathSystem` in the graph). A reference build *is* a synthetic player statistically — base scores + gear fold + tier baseline reach `IStatSystem.Get` through the same contributor/effect seams a live player's do, which is the fidelity that matters for balance math.
- **Reference-build fidelity via real seams, not snapshot injection.** Tier enters as `AscensionComponent { Tier }` folded by `AscensionEffectContributor`; gear bonuses fold through the effect pipeline (synthetic permanent `StatModifier` fold mirroring worn gear — exact mechanism implementer's choice, constrained to: must surface via `IStatSystem.Get`, never bypass it). This keeps the sim honest against the live stat pipeline instead of a parallel math path.
- **Ability-kit activation (see Open question 1).** The sim-1 schema shipped `AbilityKit` shaped-but-inert; this plan activates it for sandbox combatants (`IAbilitySystem.Learn` per id at materialization) because the policy seam is meaningless without abilities to choose. Default reference builds have empty kits, so the CI equal-cell invariant is melee-only and unaffected.
- **Initiative is drawn per tick from the run's `IRandom`.** A fixed actor order gives the first side a structural first-strike advantage that would silently bias the equal-cell 50% expectation — the exact class of artifact the CI invariant exists to catch. Randomized initiative makes symmetric scenarios statistically symmetric.
- **Deterministic parallelism.** Worlds share nothing (isolation *is* the concurrency model — seed brief); shared singletons crossing into runs are immutable registries and the pure oracle only. Per-run records land in an array slot keyed by run index; the reduce is index-ordered, so the report is independent of scheduling. Spec resolution (file/registry reads) happens once before fan-out.
- **Verdicts live in the engine (INV-19 by construction).** `ISimOutcomeEvaluator` runs inside `Run`, reading tolerances from the sim-1 registry — the CLI summary, the sim-3 editor page, and the CI invariants all read the same verdict rows; the expected-outcome math cannot fork per surface. Verdicts are advisory in the run-mode (exit code ignores them, matching `IBalanceAuditSystem`'s soft posture); a `--check` flag that fails the exit code on verdict failure is trivially additive if agent/script workflows want it.
- **Reports are JSON artifacts with a schema version (resolved 2026-07-14, seed OQ4a).** `schemaVersion: 1` at the root; additive fields never bump it, breaking shape changes do — old reports stay readable, and the sim-3 history page can render mixed versions. Files, not SQLite (INV-14 is for live entity state), not world YAML — `generate`'s posture. No retention policy in sim-2: history is the directory, cleanup is manual.
- **CI invariants are live small-scale runs (resolved 2026-07-14, seed OQ4b).** The promoted subset invokes `ISimulationRunner` directly with fixed seeds and small N, asserting tolerances read from the standards registry (compiled defaults in the test environment) — no committed threshold artifacts to drift.
- **The kind seam is a dispatch table, not speculation.** `ScenarioKind` + per-kind executor; sim-4's progression-rate kind adds an executor and a report payload section without touching the scenario store, runner shell, writer, or report envelope (the report is `kind` + kind-keyed payload).
- **N-vs-N is additive data.** Sides are lists from day one; sim-2's executor validates exactly one combatant per side (fail-fast) because 1v1 is what the current combat model and `CombatStateComponent` (single opponent) support. Group scenarios arrive when grouping exists (seed family table: shape-for-later).
- **INV-20 scope confirmed by grep:** `Hedron.Sim` references exist only in `docs/roadmap/backlog.md` (3 entries), `docs/roadmap/plan.md` (handled by ship-time `sync-roadmap`), and `docs/implementation-plans/progression-and-balance.md`; `.claude/` skills/agents/commands are clean. WP-3 updates the backlog + program-brief wording to the Core-module shape.

---

## Open questions

1. **Ability-kit activation in sim-2** — ✅ **RESOLVED (2026-07-14): activate now, as planned above.** The seed shaped `AbilityKit` to activate "at a later slice without a schema break" but did not name which. This plan activates it now because the `ISimCombatantPolicy` built-ins (`cooldown-first`, `round-robin`) are inert without abilities, and activation is one `Learn` loop at materialization. Default reference builds have empty kits, so the CI equal-cell invariant stays melee-only and unaffected.
2. **Regeneration in the tick pipeline** — ✅ **RESOLVED (2026-07-14): include.** Combatants are `InCombat`, so `RegenerationSystem.ApplyTickRegen` is suppressed for them and the call is near-free — included for fidelity with the live heartbeat order and so a future flee/out-of-combat scenario kind doesn't re-plumb the loop.

## Related

- [`balance-simulator.md`](balance-simulator.md) — the prog-4 program seed (authoritative brief; sim-2 row).
- [`progression-and-balance.md`](progression-and-balance.md) — parent program brief (its `Hedron.Sim` wording is updated by this slice, INV-20).
- [`../roadmap/completed/balance-standards-registry.md`](../roadmap/completed/balance-standards-registry.md) + [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) — sim-1 as-built: the registry/tolerances this engine consumes.
- [`../features/admin-authoring/content-tooling.md`](../features/admin-authoring/content-tooling.md) + [Flow 29](../architecture/flows/flow-29-bulk-content-generation.md) — the `generate` run-mode precedent.
- [`../architecture/07-testing.md`](../architecture/07-testing.md) — the harness composition the sandbox factory mirrors; tier rubric.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — the deferred `IAISystem` policy adapter; the YAML-definition-pipeline generalization (instance #2 recorded by this slice); live standards reload.
- Checklist invariants in tension: INV-2, INV-5, INV-10, INV-12 (named nuance), INV-17, INV-18, INV-19, INV-20, INV-22 (by absence), INV-25, INV-26, INV-28 ([`../architecture/checklist.md`](../architecture/checklist.md)).
