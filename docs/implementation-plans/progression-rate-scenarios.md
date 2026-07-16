# Progression-Rate Scenarios (sim-4)

**Status:** implemented
**Actors:** Administrator/Designer (authors + runs progression-rate scenarios from CLI or editor) · System (`simulate` run-mode, promoted CI invariants) · Blazor editor (`Hedron.Web` Simulation page — compose, run, render)
**Module:** `Core/Modules/Simulation/` (feature: [`../features/simulation/simulation.md`](../features/simulation/simulation.md)); thin-caller touches in `Server/SimulateRunMode.cs` and `Hedron.Web` (Simulation page)

> **Program seed:** this is sub-slice **sim-4** of [`balance-simulator.md`](balance-simulator.md). The seed's Design notes, `## Architecture brief`, and Resolved decisions (esp. #4: *combat scenarios first; progression-rate in-program on the same runner via the explicit scenario-kind seam*) govern this plan — nothing here relitigates them. The seed's invariants-in-tension list (INV-5/10, INV-12, INV-19, INV-25/26, INV-18, INV-28) is inherited wholesale.

---

## Description

Activates the reserved `ScenarioKind.ProgressionRate` on the existing sim-2 runner: deterministic sweeps answering *"how many kills / how much XP does it take a subject build to improve a track N times under the compiled `ProgressionConstants` curves, farming a given victim?"* Each run materializes a **subject** (reference build / mob template / inline block — the existing three combatant sources) and a **victim** into an isolated sandbox world and advances XP by calling the real `IProgressionSystem.AwardCombatExperience(subject, victim)` once per modeled kill-event — the exact live award path (`MobDiedEvent` → `ExperienceAwardHandler` → `AwardCombatExperience` → `TryImprove`) minus the bus, mirrored the same way `CombatScenarioExecutor` mirrors `AbilityInvocationPipeline`. No combat rounds are executed per kill (see Design notes — the central decision of this slice). An optional authored `ticksPerKill` rate (prefillable in the editor from a combat report's time-to-kill mean — the "consume kill-rate outputs" integration) converts kill counts into synthetic time. Reports ride the **same** `SimulationReport` envelope as an additive payload section and render in the same editor page, proving the kind-generic report shape out; the CLI runs a progression-rate scenario file exactly like a combat one. Verdicts are descriptive-first: a standards-free `targetReached` check plus an explicitly-skipped expectation verdict naming the not-yet-authored standards tolerance family (see Design notes).

---

## Preconditions

- sim-1/sim-2/sim-3 shipped: `IBalanceStandardsRegistry` loaded, `ISimulationRunner`/`ISimScenarioStore`/`ISimReportWriter`/`ISimReportReader` registered via `SimulationModule` from `CompositionRoot.Register`, Simulation page + `SimulationRunService` live.
- `ScenarioKind.ProgressionRate` exists as a reserved enum member; `SimulationRunner.Run` currently throws `NotSupportedException` for it.
- `ISandboxWorldFactory.Create` already composes `ProgressionSystem` (+ `ProgressionEffectContributor` among the four contributors) and `AscensionSystem` into every sandbox world.
- `ProgressionConstants` / `AscensionConstants` compiled as today (no data promotion in this slice).

## Postconditions

> The coverage contract — every player/designer-invisible assertion below maps to a named test in the Test plan.

1. A `kind: progressionRate` scenario loads, validates, saves, and lists through the **unchanged** `ISimScenarioStore` shell; every structural violation is named in one thrown exception: missing `progression` section for the kind, `progression` section present on a `combat` scenario, target track not in `ProgressionConstants.CombatTracks`, non-positive `targetImprovements`/`maxKillsPerRun`, non-positive `ticksPerKill` when present, side count ≠ 2 or combatant count ≠ 1 per side.
2. `SimulationRunner.Run` dispatches `ProgressionRate` to a new `ProgressionScenarioExecutor`; the `Combat` path is behaviorally unchanged (existing golden pins still pass, combat reports carry a `null` progression payload).
3. Within a run, XP advances **exclusively** through `IProgressionSystem.AwardCombatExperience` on materialized sandbox entities with the run's `SeededRandom` — no re-implemented anti-grind/threshold/award math anywhere in the Simulation module.
4. A run terminates when the target track's improvement count reaches `targetImprovements`, or at `maxKillsPerRun` (target not reached); the run record carries reached/not, kills-to-target, kills-at-each-milestone (improvement 1..N of the target track), and final per-`CombatTracks` XP + improvement counts.
5. The report envelope stays one class: `SimulationReport` gains an additive optional `ProgressionRateResult?` field; `SchemaVersion` stays 1; every pre-existing report artifact reads back unchanged through `ISimReportReader`.
6. A fixed `(scenario, seed)` pair reproduces an equivalent report regardless of `maxParallelism` or the cancellation-token/progress-callback parameters (same bar as sim-2/sim-3).
7. Both verdicts are produced by `ISimOutcomeEvaluator` (never a surface): `targetReached` (pass = every run reached the target before the cap; fail otherwise, reason carries the reached share) and `progressionRateExpectation` (skipped, reason names the missing standards tolerance family).
8. Anti-grind fidelity is executable: a victim below `AntiGrindFloorRatio` of the subject's power yields zero awards in every run, cap reached, `targetReached` FAIL — the sweep provably exercises the real curve, not a copy.
9. When `ticksPerKill` is authored, the payload carries a ticks-to-target `DistributionStats` (per-run `killsToTarget × ticksPerKill`, then reduced); absent → the field is `null` and nothing else changes.
10. CLI parity: `dotnet run --project Server -- simulate --scenario <progression.yaml> [--seed N]` runs end-to-end — same exit-code contract (0/1/2), same JSON artifact path convention, console summary shows the progression payload.
11. Editor parity: the Simulation page composes (kind switch), saves/loads, runs, cancels, and renders progression-rate scenarios and reports through the same store/run-service/reader — no forked validation, verdict, or serialization path; a `ticksPerKill` prefill reads a selected readable combat report's `TicksToKill.Mean`.
12. A thin promoted CI-invariant subset pins: kills-to-first-improvement golden numbers at a fixed seed, milestone-gap monotonicity (threshold growth ⇒ kills between successive improvements never decrease), and cross-signature determinism — fixed seed + small N, heavy sweeps stay out of CI (sim-2 precedent).
13. The Simulation module still references no `IEventBus`, `EcsManager`, host `EntityService`, or `SaveEntityAsync` (existing Tier-5 guards keep passing over the new code).

---

## Main flow

*(CLI leg; the editor leg rejoins at `SimulationRunService.Enqueue` exactly as flow-33's sim-3 diagram shows — only scenario composition differs.)*

1. `Program.Main` recognizes `simulate` and calls `SimulateRunMode.RunAsync` (unchanged — no-chain Initiator, INV-10).
2. `ISimScenarioStore.Load` deserializes the YAML — now including the kind-gated `progression:` section — and fail-fast validates per Postcondition 1. Any violation → exit 2, nothing runs.
3. `SimulationRunner.Run` dispatches on `ScenarioKind.ProgressionRate` and resolves the subject (`Sides[0]`) and victim (`Sides[1]`) **once** via `ISimCombatantFactory.Resolve` (catalog/registry I/O never in the hot path).
4. Runs fan out via the existing `Parallel.For` shell; each run derives its seed (`SimSeeds.DeriveRunSeed`), builds a fresh `SandboxWorld`, and materializes subject + victim via `ISimCombatantFactory.Materialize` (unchanged).
5. `ProgressionScenarioExecutor.ExecuteRun` loops kill-events: each event calls `world.Progression.AwardCombatExperience(subjectId, victimId)`, records the kill count at each threshold crossing of the target track, and stops on target-reached or `maxKillsPerRun`. The victim is never destroyed — one award models one kill of a fresh identical spawn, which is exactly what live template respawn produces.
6. The runner reduces the `ProgressionRunRecord`s in run-index order into a `ProgressionRateResult` (kills/XP-to-target distributions, reached count, mean milestone kills, optional ticks conversion); the report's combat scalar fields hold their empty defaults (see Design notes).
7. `ISimOutcomeEvaluator.EvaluateProgressionRate` produces the `targetReached` + skipped `progressionRateExpectation` verdicts.
8. `ISimReportWriter.WriteAsync` writes the same JSON artifact class; `SimulateRunMode.PrintSummary` prints the progression branch; exit 0.

## Events fired

**None.** The engine publishes nothing (INV-5); both run-modes are no-chain Initiators (INV-10); editor run completion remains `SimulationRunService` state, not a bus fact. Unchanged from sim-2/sim-3, guard-tested.

## Systems / handlers involved

| Piece | Status | Role |
|---|---|---|
| `ScenarioDefinition` + new `ProgressionSettings` | extended | additive optional `Progression` member; kind-gated |
| `ISimScenarioStore` / `SimScenarioStore` | extended | `progression:` DTO section + kind-specific validation branches; shell (Load/Validate/SaveAsync/List) unchanged |
| `ISimCombatantFactory` | reused as-is | resolves/materializes subject + victim (all three sources work unchanged) |
| `ISandboxWorldFactory` / `SandboxWorld` | reused as-is | graph already contains `ProgressionSystem`/`AscensionSystem` |
| `ProgressionScenarioExecutor` (new, `Core/Modules/Simulation/Systems/`) | new | per-run kill-event loop over `IProgressionSystem.AwardCombatExperience`; returns `ProgressionRunRecord` |
| `ISimulationRunner` / `SimulationRunner` | extended | kind dispatch (two-way branch) + progression reduce; combat path untouched |
| `ISimOutcomeEvaluator` / `SimOutcomeEvaluator` | extended | `EvaluateProgressionRate` — verdict math stays in the engine (INV-19) |
| `SimulationReport` + new `ProgressionRateResult`/`ProgressionTrackResult` | extended | additive payload section, `SchemaVersion` 1 |
| `ISimReportWriter` / `ISimReportReader` / `SimReportJson` | reused as-is | optional property serializes/deserializes with no change |
| `Server/SimulateRunMode` | extended | summary printer gains a progression branch; parse/exit contract unchanged |
| `Hedron.Web`: `Simulation.razor`, `SimulationPrefill` | extended | kind switch + progression compose fields + report payload rendering + `ticksPerKill` prefill helper |
| `SimulationRunService`, `BaselineSweep` | reused as-is | enqueue/cancel/poll are kind-agnostic already |
| `IProgressionSystem`, `IAscensionSystem`, `IPowerBudgetSystem` | reused as-is | the real seams under test — no signature changes |

No handlers, no commands, no components change.

### New data shapes (engine)

```csharp
// ScenarioDefinition gains: ProgressionSettings? Progression = null  (additive optional)
public sealed record ProgressionSettings(
    ScoreId TargetTrack,          // must be in ProgressionConstants.CombatTracks
    int TargetImprovements,       // > 0
    int MaxKillsPerRun,           // > 0 — the progression analog of MaxTicksPerRun
    double? TicksPerKill = null); // > 0 when present; the kill-rate bridge from combat reports

public sealed record ProgressionRunRecord(
    int RunIndex, bool ReachedTarget, int Kills,
    IReadOnlyList<int> MilestoneKills,                     // kill count at improvement 1..N of TargetTrack
    IReadOnlyDictionary<ScoreId, int> FinalXp,             // per CombatTracks
    IReadOnlyDictionary<ScoreId, int> FinalImprovements);

// SimulationReport gains: ProgressionRateResult? ProgressionRate = null  (additive optional)
public sealed record ProgressionTrackResult(
    ScoreId Track, DistributionStats Xp, DistributionStats Improvements);
public sealed record ProgressionRateResult(
    ScoreId TargetTrack, int TargetImprovements, int RunsReachedTarget,
    DistributionStats KillsToTarget,                       // over runs that reached the target
    IReadOnlyList<double> MeanMilestoneKills,              // mean kills at improvement 1..N
    IReadOnlyList<ProgressionTrackResult> Tracks,
    double? TicksPerKill, DistributionStats? TicksToTarget);
```

---

## Implementation plan — work packages

### WP1 — Engine: kind activation (Core/Modules/Simulation)

- **Scope:** `ProgressionSettings` on `ScenarioDefinition` (additive optional positional param — existing call sites compile); `SimScenarioStore` DTO `progression:` section + Postcondition-1 validation branches (policy-id validation becomes Combat-only — see Design notes); new `ProgressionScenarioExecutor`; `SimulationRunner` kind dispatch + progression reduce; `ProgressionRateResult`/`ProgressionTrackResult` payload on `SimulationReport`; `ISimOutcomeEvaluator.EvaluateProgressionRate` + implementation; checked-in example `data/sim/scenarios/example-progression-rate.yaml`.
- **Files:** `Core/Modules/Simulation/ScenarioDefinition.cs`, `SimulationReport.cs`, `Systems/SimScenarioStore.cs`, `Systems/ISimScenarioStore.cs` (doc comments only), new `Systems/ProgressionScenarioExecutor.cs`, `Systems/SimulationRunner.cs`, `Systems/ISimOutcomeEvaluator.cs`, `Systems/SimOutcomeEvaluator.cs`; tests in `Hedron.Tests/Simulation/` (store, executor, evaluator, runner, writer/reader round-trip — see Test plan 1–6).
- **Dependencies:** none. **Out of scope:** CLI printer, web, CI-invariant pins.
- **Exit criterion:** `dotnet test` green; a progression scenario runs deterministically through `ISimulationRunner.Run` in-tests with populated payload and both verdicts.

### WP2 — CLI parity, promoted CI invariants, canonical docs

- **Scope:** `SimulateRunMode.PrintSummary` progression branch; end-to-end run-mode test over the example YAML; `SimulationInvariantTests` additions (golden pin, milestone monotonicity, extended-signature determinism — fixed seed, small N); update `docs/architecture/flows/flow-33-simulation-run.md` (kind-dispatch step + progression executor leg + prefill note) and `docs/reference/systems.md` rows (`ISimulationRunner` kind dispatch, new executor, evaluator method, store validation additions); **INV-20:** update `.claude/skills/edit-progression-system/SKILL.md` — (a) the "tune progression" recipe gains a note that sim-4 CI-pins progression golden numbers (`SimulationInvariantTests`), so a deliberate tuning change to `ThresholdBase`/`ThresholdIncrement`/`CombatAwardMin/Max`/`AntiGrindFloorRatio` re-pins them **in the same commit** (the sim-2 regression-pin posture); (b) the "validate a tuning change at scale" line points at the now-concrete `progressionRate` scenario kind.
- **Files:** `Server/SimulateRunMode.cs`, `Hedron.Tests/Simulation/SimulateRunModeTests.cs`, `SimulationInvariantTests.cs`, `SimulationTestFixtures.cs` (progression fixture), the two docs, `.claude/skills/edit-progression-system/SKILL.md`.
- **Dependencies:** WP1. **Out of scope:** web.
- **Exit criterion:** CLI runs the example scenario exit-0 with a progression summary; new invariant pins green; flow-33 matches the shipped path (architecture-reviewer blocks on drift otherwise).

### WP3 — Editor: compose + render (Hedron.Web)

- **Scope:** `Simulation.razor` — scenario-kind selector; for `ProgressionRate`: side editors relabeled Subject/Victim (reuse `CombatantSideEditor`; policy selector hidden — unused by the kind), `TargetTrack`/`TargetImprovements`/`MaxKillsPerRun`/`TicksPerKill` fields, `BuildScenario`/`ApplyDefinition` round-trip the kind + settings (loading a saved progression scenario must not mangle it) — the kind + settings ↔ form mapping lives in a **testable composer helper** (`ProgressionSettingsForm` or an extension of the existing `CombatantForm`/`SimulationPrefill` service pattern — 07-testing.md's web-host-services carve-out), unit-tested for round-trip fidelity rather than skipped as razor markup; report view renders the `ProgressionRate` payload section when non-null (combat section only when `Kind == Combat`); `SimulationPrefill.TicksPerKillFrom(SimulationReport)` (pure static: `Kind == Combat` + decisive-runs guard → `TicksToKill.Mean`) with a "prefill from combat report" dropdown of readable history rows.
- **Files:** `Hedron.Web/Components/Pages/Simulation.razor`, `Hedron.Web/Components/Shared/CombatantSideEditor.razor` (parameter to hide policy/relabel), `Hedron.Web/Services/SimulationPrefill.cs` (+ the settings-form composer, same directory); `Hedron.Tests/Web/SimulationPrefillTests.cs`.
- **Dependencies:** WP1 (independent of WP2). **Out of scope:** any change to `SimulationRunService` (kind-agnostic already); no "simulate progression" entry points on other editor pages (not owed by the seed).
- **Exit criterion:** compose → save → reload → run → cancel → open-report all work for a progression scenario against the running web host; prefill helper unit-tested.

**Primary agent runs `architecture-reviewer` (code mode) across the combined diff once all packages land.**

---

## Content tooling impact

- **Scenario YAML gains a kind-gated section** (the only authored-state change):

  ```yaml
  kind: progressionRate
  name: t2b2-vs-t2-mob-improvement-rate
  seed: 1234
  iterations: 200
  maxTicksPerRun: 1        # unused by this kind; validated > 0 as today
  progression:
    targetTrack: body
    targetImprovements: 5
    maxKillsPerRun: 500
    ticksPerKill: 12.4     # optional; prefillable from a combat report
  sides:
    - combatants: [{ source: referenceBuild, tier: 2, band: 2 }]   # subject (policyId not required for this kind)
    - combatants: [{ source: mobTemplate, mobBlueprintId: mob.wolf }]  # victim
  ```

- **Authoring + inspection ship in-slice (INV-18):** authored via the same editor compose form (WP3) and `ISimScenarioStore.SaveAsync`; inspected via the same report page / CLI summary / JSON artifact. Checked-in example: `data/sim/scenarios/example-progression-rate.yaml`.
- **No** new admin commands, `TemplateRegistry` entries, config keys, or standards-document fields. The balance-standards YAML is untouched (see Design notes on the deferred tolerance family).

## Cross-cutting surfaces stressed (INV-19 audit)

| Surface | Classification | Rationale |
|---|---|---|
| Scenario store / YAML authoring | **Adequate** | Additive DTO section on the existing store; shell unchanged (the sim-2 extensibility promise holds). Hand-rolled-YAML family count stays at 2 (`BalanceStandardsStore`, `SimScenarioStore`) — the backlogged ≥3 generalization trigger is **not** hit. |
| Report artifact pipeline | **Acknowledged debt** | The envelope's combat scalar fields (`SideAWins`, `TicksToKill`, …) are vestigial-but-harmless on progression reports (empty defaults; additive posture keeps `SchemaVersion` 1 and old readers working). Named trigger: a **third** scenario kind refactors the envelope into per-kind payload sections (`CombatResult?`/`ProgressionRateResult?`, `SchemaVersion` 2). Backlog entry ships with this slice. |
| Runner kind dispatch | **Acknowledged debt** (same backlog entry) | A two-way branch in `SimulationRunner.Run`, not a strategy interface — rule-of-three; the third kind lands the executor-strategy seam together with the envelope refactor. |
| Balance-standards registry | **Acknowledged debt** | No progression-rate tolerance family exists and none is invented here (Design notes). The evaluator's skipped verdict names the gap on every report; backlog entry: "promote progression-rate expectations into the standards document once observed rates exist." INV-18 not triggered — no new authored state ships for it. |
| Event bus / broadcast / sessions / commands | **Adequate** (not exercised) | Engine publishes nothing; no in-game surface. |
| ECS / entity model | **Adequate** | Sandbox-only entity construction through the existing factory; no queries change. |
| Configuration | **Adequate** | No new keys; `Simulation:ReportDirectory`/`ScenarioDirectory` reused. |
| Time | **Adequate** | Synthetic only — `ticksPerKill` is authored arithmetic; `IClock` still only stamps `GeneratedAt`; no wall-clock reaches an outcome (INV-26). |
| Testing seams | **Adequate** | All chance flows through the per-run `SeededRandom` already; `ProgressionSystem`'s injected `IRandom` is the seam — no new un-injected dependency, **no testability gap**. |

### Persistence opt-in audit (INV-22/23)

- **Level 1 — entity domains:** the only construction path touched is sandbox materialization (subject/victim via `ISimCombatantFactory.Materialize`). Sandbox entities belong to neither persistence domain — never `PersistentEntity`, whole `EntityService` discarded per run (the established sim-2 nuance). No runtime domain transitions.
- **Level 2 — components:** **no new components** (`ProgressionSettings`/`ProgressionRateResult` are scenario/report data records, not `IComponent`s). Touched existing components: `ProgressionComponent` (`[Persistent]` — correct for its live player use; on sandbox entities it is never snapshotted because the entity never opts in — the two-level model working as designed), `AscensionComponent`, `AttributesComponent`, `PoolsComponent` — all statuses unchanged and correct. No gap.
- **Level 3 — saves:** zero `SaveEntityAsync` call sites anywhere in the module, before and after (INV-22 by absence; guard-tested).

## Flows introduced or modified

- **[Flow 33 — Simulation run journey](../architecture/flows/flow-33-simulation-run.md) — modified (no new flow).** Step 4's kind dispatch note ("only `Combat` has an executor — the sim-4 seam") is replaced by the two-executor dispatch; a short progression-executor leg is added (kill-event loop, `AwardCombatExperience`, target/cap termination); the editor leg's prefill note gains the `ticksPerKill`-from-report affordance. The invariants section is already kind-agnostic. Updated in WP2's PR.
- Flow 31 (progression award) and flow 20 (mob death) are **referenced, not modified** — the executor mirrors flow 31's system leg without its bus/handler leg, exactly as flow 33 already describes for combat vs flow 17.

## Test plan / Verification (INV-25)

| # | Tier | Target | Asserts |
|---|---|---|---|
| 1 | T1 system-unit | `SimScenarioStore` | progression YAML load→validate→`SaveAsync`→reload round-trip (kind + settings intact); each Postcondition-1 violation named (missing section, section-on-combat, untracked track, non-positive targets/cap/ticksPerKill); policy id **not** required for progression sides but still required for combat (regression) |
| 2 | T1 system-unit | `ProgressionScenarioExecutor` | with a `FakeRandom` roll script: kills-to-first-improvement matches hand-computed threshold math (`ThresholdBase`/`ThresholdIncrement`); milestone kill counts recorded per crossing; multi-threshold single-award crossing handled; cap termination sets `ReachedTarget=false`; **anti-grind floor victim ⇒ zero awards, cap hit** (Postcondition 8); XP mutation happens only via the world's `IProgressionSystem` (assert final component state equals `GetXp`/`GetImprovementCount` reads) |
| 3 | T1 system-unit | `SimOutcomeEvaluator.EvaluateProgressionRate` | `targetReached` pass (all runs reached) / fail (share in reason); `progressionRateExpectation` skipped with the standards-family reason |
| 4 | T3 flow | `SimulationRunner` (ProgressionRate) | fixed (scenario, seed): payload populated, combat scalars at empty defaults, verdicts attached; `maxParallelism: 1` vs default produce equivalent reports; extended signature (CT + callback) unperturbed (Postcondition 6); a **Combat** run still yields `ProgressionRate == null` (envelope regression); **both `ticksPerKill` branches (Postcondition 9):** a fixture with `ticksPerKill` asserts `TicksToTarget` equals the per-run `killsToTarget × ticksPerKill` reduce, a fixture without asserts `TicksToTarget == null` |
| 5 | T1 system-unit | `SimReportWriter`/`SimReportReader` | write→read round-trip of a report with a populated `ProgressionRateResult`; a **sim-3-era combat report JSON (no progression property)** still deserializes (Postcondition 5) |
| 6 | T3 flow | `SimulateRunMode` | end-to-end over `example-progression-rate.yaml`: exit 0, artifact written, summary contains the progression section; invalid progression scenario → exit 2 |
| 7 | T3 promoted CI invariants | `SimulationInvariantTests` | golden pin: kills-to-first-improvement distribution values at fixed (seed, N) for a reference-build subject vs an equal-cell victim; milestone-gap monotonicity (kills between improvement k and k+1 never decrease — the threshold-growth curve, executable); pins are regression pins, not hypothesis tests (sim-2 posture) |
| 8 | T1 (web) | `SimulationPrefill.TicksPerKillFrom` + settings-form composer | prefill: returns `TicksToKill.Mean` for a decisive combat report; `null` for a progression report or a zero-decisive report. Composer: kind + `ProgressionSettings` ↔ form round-trip fidelity (Postcondition 11's must-not-mangle, testable per the WP3 composer extraction) |
| 9 | T5 guard | existing `ArchitectureGuardTests` | no new test — the existing Simulation-module bus/`EcsManager` source scan and no-ambient-nondeterminism guards cover the new files by construction; verified green |

**Skipped, and why:** `Simulation.razor` markup/rendering and the CLI summary prose (presentation — Tier-2 output rule: never exact prose); the YAML DTO classes and new data records (pure data, INV-3 posture); `SimulationRunService` (untouched — kind-agnostic, already tested); per-module DI registration (DI-smoke guard covers it). No persistence round-trip owed: no new `[Persistent]` shape (Level-2 audit above).

---

## Design notes

### Analytical kill-events over the real `IProgressionSystem` — not simulated combat, and not parallel math (the central decision)

Three candidate executors were weighed:

1. **Full combat per kill** — run `CombatScenarioExecutor` to a `MobDied` per kill-event, subject retaining its `ProgressionComponent` across fights. Honest in principle (combat variance; power growth feeding back into kill speed) but: (a) cost is multiplicative — kills-to-target (hundreds) × ticks-per-fight (tens–hundreds) × iterations, turning a sub-second sweep into minutes; and (b) **the feedback loop it would buy is currently severed by the known calibration gap** — progression improvements fold onto `Body`/`HpMax` via `IStatSystem.Get`, but `StatSystem.GetEffectiveAttackPower`/`GetEffectiveDefense` read raw `Body` and combat's HP checks read raw `PoolsComponent`, so accrued improvements change real combat outcomes exactly as much as the tier baseline does today: not at all (see `docs/roadmap/backlog.md`; pinned by `SimulationInvariantTests`). Paying the honest price for fidelity the mechanics can't currently express is waste.
2. **Re-derived formula** — compute awards analytically from `ProgressionConstants`. Rejected outright: a second copy of the anti-grind/threshold math is precisely the INV-19 drift the one-engine posture exists to prevent.
3. **Chosen: analytical kill-events over the real seams.** Materialize subject + victim; per kill-event call `world.Progression.AwardCombatExperience(subject, victim)` directly. This *is* the live award path minus the bus — the same mirroring relationship `CombatScenarioExecutor` has to `AbilityInvocationPipeline`. Fidelity is exact, not approximate, today: the anti-grind proxy reads **raw** `AttributesComponent` (which improvements never mutate — they are contribute-on-read), so per-kill award distributions are stationary on the live path too; the sandbox victim is never destroyed, which models exactly what live template respawn produces. Chance stays on the run's `SeededRandom` through `ProgressionSystem`'s injected `IRandom` (INV-26, no new seam).

**Named re-evaluation trigger:** when the calibration-gap fix lands (improvements/tier actually moving combat outcomes), a hybrid mode — real combat per kill, or a periodic TTK re-measure — becomes worth its cost. The backlog calibration entry gains a pointer to this note; the scenario shape (sides + settings) already accommodates it additively.

### Kill-rate integration: through the scenario field, human/editor-mediated — not engine-chained

`ticksPerKill` is authored scenario data (so CLI parity is free), prefillable in the editor from a chosen combat report's `TicksToKill.Mean` via a pure static helper. The engine never reads report files as *inputs* — reports remain output artifacts (the seed's third-artifact-class posture), the hot path keeps doing zero I/O, and the store/runner/writer shell stays unchanged. Deeper automation (a scenario referencing a report path; auto-chained combat→progression pipelines) is deferred until a real workflow demands it.

### Verdicts: descriptive-first, with the gap named on every report

The sim-1 posture says expected-outcome tolerances live in the standards document. That fact family exists for combat (authored in sim-1, consumed in sim-2) — but **no progression-rate expectation has ever been stated by a designer**; inventing "kills-to-improvement should be 8–15" numbers now would ship speculative authored state plus a standards-editor surface (INV-18) for data nobody can yet ground. Instead: (a) `targetReached` — a real, standards-free verdict (did the sweep complete under the cap — catches anti-grind-floored farming setups immediately); (b) `progressionRateExpectation` — **skipped**, reason naming the missing standards family. Both live in `ISimOutcomeEvaluator`, so when the tolerance family is later promoted into the standards document (backlog entry, data-driven once designers have observed real rates), only the evaluator and the standards store/editor change — no surface forks (INV-19 by construction, same as sim-2). This mildly defers the seed's "tolerances live in the standards" note rather than contradicting it: nothing is hardcoded in the sim; the fact family is deferred wholesale. *Flagged as an open question for explicit user sign-off.*

### "Time-to-tier" is not mechanically measurable today — target is improvements-only

`CanAscend` gates only on `AtMaxTier`; ascension is admin-triggered and the player-facing Objective gate is deferred (`IObjectiveSystem` unbuilt). There is no XP/improvement condition that *causes* a tier-up, so "kills until the next tier" has no defined answer — simulating `TryAscend` would measure an admin command. This slice therefore ships `targetImprovements` on a tracked score as the only target kind (the only progression clock that exists), with milestone data making any threshold question answerable from the artifact. A `timeToTier` target kind activates additively (a second target discriminator in `ProgressionSettings`) when an XP/objective-based ascension gate lands. This narrows the seed row's "time-to-improvement / time-to-tier" phrasing to what the shipped mechanics define — *surfaced as an open question rather than silently narrowed.*

### Scenario shape: reuse `Sides` (subject = side 0, victim = side 1) + a kind-gated settings record

Reusing `ScenarioSide`/`CombatantSpec` keeps all three combatant sources, `ISimCombatantFactory`'s two-phase resolution, the YAML dialect, and the editor side forms working unchanged — and is exactly the shape the future hybrid (real-combat) mode needs. The alternative (a kind-specific `subject:`/`victim:` section) duplicates the combatant DTO for zero semantic gain. `PolicyId` is meaningless when no actions are chosen, so store validation requires a known policy id **only for kinds that execute actions** (Combat) — a progression side may omit it (empty string accepted), and the editor hides the selector. `MaxTicksPerRun` remains globally validated (> 0) but is unused by this kind; the progression cap is the explicit `maxKillsPerRun` — clearer than overloading tick semantics.

### Report envelope: additive payload section, `SchemaVersion` stays 1

`SimulationReport` gains `ProgressionRateResult? ProgressionRate = null`. Old artifacts deserialize unchanged; combat reports carry `null`; progression reports leave the combat scalars at empty defaults (`DistributionStats.From([])` is all-zeros by design). This is the cheapest shape that honors "additive fields never bump the version" — the honest cost (vestigial combat fields on progression artifacts) and its refactor trigger (third kind → per-kind payload sections, `SchemaVersion` 2, executor-strategy seam in the runner) are recorded as acknowledged debt with a backlog entry. Renderers dispatch on `Scenario.Kind`/payload-null, which is the "kind-generic report shape proves out" the seed asks this slice to demonstrate.

## Related

- [`balance-simulator.md`](balance-simulator.md) — the program seed (Architecture brief + Resolved decisions govern this plan).
- [`../features/simulation/simulation.md`](../features/simulation/simulation.md) · [`../features/simulation/simulation-engine.md`](../features/simulation/simulation-engine.md) — the as-built engine this extends (disintegration target for behavior/design content).
- [`../features/progression/progression.md`](../features/progression/progression.md) · [`../features/progression/progression-system.md`](../features/progression/progression-system.md) · [`../features/progression/ascension-system.md`](../features/progression/ascension-system.md) — the mechanics under sweep; the DI-cycle/raw-attribute guard the executor's fidelity argument rests on.
- [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) — the sim-1 standards registry (verdict-tolerance home; the deferred progression-tolerance family lands there).
- [`../architecture/flows/flow-33-simulation-run.md`](../architecture/flows/flow-33-simulation-run.md) — the flow this slice modifies · [flow-31](../architecture/flows/flow-31-progression-award.md) (mirrored, not modified).
- [`../roadmap/completed/simulation-engine-core.md`](../roadmap/completed/simulation-engine-core.md) · [`../roadmap/completed/simulation-editor-integration.md`](../roadmap/completed/simulation-editor-integration.md) — sim-2/sim-3 decisions this plan builds on.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — the Ascension calibration gap (re-evaluation trigger for the hybrid executor); this slice adds two entries (kind-payload refactor trigger; progression-tolerance promotion).

---

## Open questions (for the user, before the spec gate)

1. **Verdict posture** — confirm descriptive-first (skip verdict + backlogged standards promotion) over authoring speculative progression tolerances into the standards document now. Recommendation: descriptive-first; the seed's tolerances-live-in-standards note is honored by deferring the fact family, not by hardcoding.
2. **Time-to-tier deferral** — confirm narrowing the seed row's "time-to-tier" to improvements-targets until an XP/objective-based ascension gate exists (no mechanical gate to measure today).
3. **Kill-rate integration depth** — confirm the authored-`ticksPerKill` + editor-prefill shape over engine-chained report consumption.
4. **Editor compose parity** — confirm full compose UI for the kind (recommended; prevents the page mangling loaded progression scenarios) vs. YAML-only authoring with render-only editor support.
