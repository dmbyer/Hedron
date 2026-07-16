# Progression-rate scenarios (slice sim-4, completed)

> Implemented on branch `claude/progression-rate-scenarios-review-k5wl0o`, 2026-07-16. Living docs:
> [`features/simulation/simulation.md`](../../features/simulation/simulation.md) ·
> [`features/simulation/simulation-engine.md`](../../features/simulation/simulation-engine.md).

## Outcome

Activates the reserved `ScenarioKind.ProgressionRate` on the sim-2/sim-3 simulation engine: a
deterministic sweep answering "how many kills / how much XP does it take a subject build to improve
a track N times, farming a given victim?" Each run materializes a subject and a victim into an
isolated sandbox world and advances XP by calling the real `IProgressionSystem.AwardCombatExperience`
once per modeled kill-event — the live award path minus the bus, no combat rounds simulated. Reports
ride the same `SimulationReport` envelope as an additive payload section, proving the kind-generic
report shape out; the CLI runs a progression-rate scenario file exactly like a combat one, and the
Simulation editor page gained full compose/render parity with the combat kind, including a
"prefill ticks per kill from a combat report" affordance. Verdicts are descriptive-first: a
standards-free `targetReached` check plus an explicitly-skipped expectation verdict naming the
not-yet-authored standards tolerance family.

## Behavior digest

- **Postcondition 1** — a `kind: progressionRate` scenario loads, validates, saves, and lists through
  the unchanged `ISimScenarioStore` shell; every structural violation is named in one thrown
  exception (missing `progression` section, section present on a `combat` scenario, untracked target
  track, non-positive `targetImprovements`/`maxKillsPerRun`/`ticksPerKill`, wrong side/combatant
  count).
- **Postcondition 2** — `SimulationRunner.Run` dispatches `ProgressionRate` to a new
  `ProgressionScenarioExecutor`; the `Combat` path is behaviorally unchanged.
- **Postcondition 3** — XP advances exclusively through `IProgressionSystem.AwardCombatExperience` on
  materialized sandbox entities with the run's `SeededRandom` — no re-implemented anti-grind/
  threshold/award math anywhere in the Simulation module.
- **Postcondition 4** — a run terminates when the target track's improvement count reaches
  `targetImprovements`, or at `maxKillsPerRun` (target not reached); the run record carries
  reached/not, kills-to-target, kills-at-each-milestone, and final per-`CombatTracks` XP +
  improvement counts.
- **Postcondition 5** — the report envelope stays one class: `SimulationReport` gains an additive
  optional `ProgressionRateResult?` field; `SchemaVersion` stays 1; every pre-existing report
  artifact reads back unchanged.
- **Postcondition 6** — a fixed `(scenario, seed)` pair reproduces an equivalent report regardless of
  `maxParallelism` or the cancellation-token/progress-callback parameters.
- **Postcondition 7** — both verdicts are produced by `ISimOutcomeEvaluator` (never a surface):
  `targetReached` (pass = every run reached the target before the cap) and
  `progressionRateExpectation` (skipped, reason names the missing standards tolerance family).
- **Postcondition 8** — anti-grind fidelity is executable: a victim below `AntiGrindFloorRatio` of the
  subject's power yields zero awards in every run, cap reached, `targetReached` FAIL.
- **Postcondition 9** — when `ticksPerKill` is authored, the payload carries a ticks-to-target
  `DistributionStats`; absent → the field is `null` and nothing else changes.
- **Postcondition 10** — CLI parity: `simulate --scenario <progression.yaml>` runs end-to-end with the
  same exit-code contract, JSON artifact convention, and a progression console summary branch.
- **Postcondition 11** — editor parity: the Simulation page composes (kind switch), saves/loads, runs,
  cancels, and renders progression-rate scenarios and reports through the same store/run-service/
  reader — no forked validation, verdict, or serialization path; a `ticksPerKill` prefill reads a
  selected readable combat report's `TicksToKill.Mean`.
- **Postcondition 12** — a thin promoted CI-invariant subset pins kills-to-first-improvement at a
  fixed seed, milestone-gap monotonicity, and cross-signature determinism.
- **Postcondition 13** — the Simulation module still references no `IEventBus`/`EcsManager`/host
  `EntityService`/`SaveEntityAsync` (existing Tier-5 guards keep passing over the new code).

## Shipped pieces

| Surface | Location |
|---|---|
| `ProgressionSettings` (new record on `ScenarioDefinition`) | `Core/Modules/Simulation/ScenarioDefinition.cs` |
| `ProgressionTrackResult`/`ProgressionRateResult` (new records) + `SimulationReport.ProgressionRate` (additive) | `Core/Modules/Simulation/SimulationReport.cs` |
| `SimScenarioStore` — `progression:` YAML DTO section + kind-gated Postcondition-1 validation; policy-id validation now Combat-only | `Core/Modules/Simulation/Systems/SimScenarioStore.cs` |
| `ProgressionScenarioExecutor`/`ProgressionRunRecord` (new) | `Core/Modules/Simulation/Systems/ProgressionScenarioExecutor.cs` |
| `SimulationRunner` — kind dispatch (`RunCombat`/`RunProgressionRate`) + progression reduce | `Core/Modules/Simulation/Systems/SimulationRunner.cs` |
| `ISimOutcomeEvaluator.EvaluateProgressionRate` + implementation | `Core/Modules/Simulation/Systems/ISimOutcomeEvaluator.cs` · `SimOutcomeEvaluator.cs` |
| `SimulateRunMode.PrintSummary` — progression console-summary branch | `Server/SimulateRunMode.cs` |
| `Simulation.razor` — kind selector, progression fields, prefill dropdown, report payload rendering | `Hedron.Web/Components/Pages/Simulation.razor` |
| `CombatantSideEditor.razor` — `ShowPolicy` parameter (hidden for `ProgressionRate`) | `Hedron.Web/Components/Shared/CombatantSideEditor.razor` |
| `SimulationPrefill.TicksPerKillFrom` (new static method) | `Hedron.Web/Services/SimulationPrefill.cs` |
| `ProgressionSettingsForm` (new, kind + settings ↔ form composer) | `Hedron.Web/Services/ProgressionSettingsForm.cs` |
| Checked-in example scenario | `data/sim/scenarios/example-progression-rate.yaml` |
| `flow-33-simulation-run.md` — two-executor dispatch, progression-rate leg diagram, `ticksPerKill` prefill note | `docs/architecture/flows/flow-33-simulation-run.md` |
| Reference rows (`ISimScenarioStore`, `ISimulationRunner`, new `ProgressionScenarioExecutor`, `ISimOutcomeEvaluator`, `ISimReportWriter`, `SimulationPrefill`) | `docs/reference/systems.md` |
| `edit-progression-system` skill — CI-pin re-pinning note, `progressionRate` scenario pointer | `.claude/skills/edit-progression-system/SKILL.md` |
| Backlog entry — progression-rate expectation tolerances (deferred) | `docs/roadmap/backlog.md` |

## Tests shipped

`dotnet test` green at 1198 total (up from 1159 pre-slice).

- **Tier 1** — `ProgressionScenarioExecutorTests` (new — kills-to-first-improvement matches
  hand-computed threshold math; multiple milestones recorded per crossing; cap-reached-before-target
  yields no milestones; anti-grind-floor victim yields zero awards and cap reached; final XP/
  improvements equal direct `world.Progression` reads; run index carried into the record);
  `SimScenarioStoreTests` extended (valid progression round-trip with policy id not required; each
  Postcondition-1 violation named; side-count violation); `SimOutcomeEvaluatorTests` extended
  (`EvaluateProgressionRate` pass/fail/reason-share, expectation always skipped naming the standards
  gap); `SimReportWriterTests`/`SimReportReaderTests` extended (progression payload round-trips
  through real JSON serialization; a pre-sim-4 report JSON with the `progressionRate` property
  stripped entirely still deserializes); `SimulationPrefillTests` extended (`TicksPerKillFrom`
  decisive/progression/zero-decisive cases; `ProgressionSettingsForm` `ApplyFrom`→`ToSettings`
  round-trip fidelity, including the no-`ticksPerKill` case).
- **Tier 3** — `SimulationRunnerTests` extended (same-scenario-and-seed and parallelism-1-vs-N produce
  equivalent progression reports; combat scalars stay at empty defaults while the payload populates;
  extended `Run` signature matches the bare call and fires the callback per iteration; a `Combat` run
  still yields `ProgressionRate == null`; both `ticksPerKill` branches — present computes
  `TicksToTarget` from `KillsToTarget`, absent leaves it `null`); `SimulateRunModeTests` extended
  (a progression scenario exits 0 with the payload in the written JSON; the checked-in example
  scenario file runs end-to-end; a structurally invalid progression scenario exits 2).
- **Tier 3 promoted CI invariants** — `SimulationInvariantTests` extended: kills-to-first-improvement
  golden pin at a fixed (seed 2026, N 200) — captured by actually running the suite (see Decisions);
  milestone-gap monotonicity, made a mathematical certainty rather than a statistical tendency by
  choosing parameters (equal power, generous cap) that guarantee every run reaches the full target,
  so every milestone's mean is computed over an identical population; cross-signature determinism at
  a fixed seed.
- **Tier 5** — existing `Simulation_module_does_not_reference_EventBus_or_EcsManager` guard covers the
  new files by construction; verified green.

## Decisions

- **Analytical kill-events over the real seam, not simulated combat, not re-derived math.** Three
  executors were weighed: full combat per kill (rejected — cost is multiplicative, and the feedback
  loop it would buy is currently severed by the Ascension tier-baseline calibration gap, so accrued
  improvements don't yet change real combat outcomes at all); a re-derived formula (rejected outright
  — a second copy of the anti-grind/threshold math is exactly the INV-19 drift the one-engine posture
  exists to prevent); and the chosen approach — materialize subject + victim, call
  `IProgressionSystem.AwardCombatExperience` directly per kill-event. This mirrors exactly how
  `CombatScenarioExecutor` mirrors `AbilityInvocationPipeline`. Named re-evaluation trigger: when the
  calibration-gap fix lands, a hybrid mode (real combat per kill, or periodic TTK re-measure) becomes
  worth its cost.
- **Verdicts: descriptive-first, gap named on every report.** Confirmed with the user before
  implementation. `targetReached` is a real, standards-free verdict; `progressionRateExpectation` is
  permanently skipped rather than shipping invented tolerance numbers, since no designer has ever
  stated a progression-rate expectation. A backlog entry ("Progression-rate expectation tolerances")
  records the promotion trigger.
- **`targetImprovements` is the only target kind; time-to-tier deferred.** Confirmed with the user.
  `CanAscend` gates only on `AtMaxTier`, ascension is admin-triggered, and the player-facing Objective
  gate is unbuilt — there is no XP/improvement condition that *causes* a tier-up today, so "kills
  until the next tier" has no defined answer. A `timeToTier` target kind activates additively when
  that gate exists.
- **`ticksPerKill` is authored scenario data with an editor-side prefill, not engine-chained report
  consumption.** Confirmed with the user. The engine never reads report files as inputs — reports stay
  output-only artifacts, the hot path stays zero-I/O, and the store/runner/writer shell stays
  unchanged. `SimulationPrefill.TicksPerKillFrom` is a pure static helper the editor calls explicitly.
- **Scenario shape reuses `Sides` (subject = side 0, victim = side 1) plus a kind-gated settings
  record**, rather than a `subject:`/`victim:` section — keeps all three combatant sources, the
  two-phase resolution, the YAML dialect, and the editor side forms working unchanged, and is the
  shape a future hybrid (real-combat) mode would need anyway. `PolicyId` is meaningless when no
  actions are chosen, so store validation requires a known policy id only for `Combat`.
- **Report envelope stays additive; runner dispatch stays a plain two-way branch.** Both are named,
  recorded acknowledged debt (not oversights) with a shared trigger: a third scenario kind is the
  point to refactor the envelope into per-kind payload sections (`SchemaVersion` 2) and land an
  executor-strategy seam in the runner, together.
- **Editor compose parity was full, not YAML-only-with-render-only-editor.** Confirmed with the user
  (recommended option) — prevents the page mangling a loaded progression scenario, and a testable
  `ProgressionSettingsForm` composer proves the kind + settings ↔ form round-trip rather than being
  skipped as untestable razor markup.
- **No .NET SDK was preinstalled in this execution environment**, and the official `dotnet-install.sh`
  download host was blocked by the session's outbound proxy policy (403, not something to route
  around). Docker *was* available but had no running daemon (no systemd); starting `dockerd` directly
  worked, and `docker run --network host` (so the container can reach the host-local proxy) combined
  with mounting the proxy's CA bundle as `SSL_CERT_FILE` let `mcr.microsoft.com/dotnet/sdk:8.0`
  restore/build/test through the proxy successfully. Unrelated to the slice's design, noted here
  because — unlike sim-3's record, where the SDK was installed natively via `apt-get` — the whole
  implementation was authored and manually reviewed for compile-correctness *before* any build/test
  could run at all in this environment; the Docker path was only discovered afterward, and every test
  in the slice passed on the very first real run.
- **The literal golden-pin exact values (Postcondition 12, `SimulationInvariantTests`) were captured
  by an intentional-failure dump** (a throwaway test asserting a wrong value to surface the real one
  in the xUnit failure message via `dotnet test --filter`) once the Docker build path was confirmed —
  the same "run once, read back the observed numbers" method the sim-2 combat pins used, not a
  hand-derived approximation.

## Deviations / Follow-ups

- **No deviations from the plan's shape.** All three work packages (engine kind activation; CLI
  parity + promoted CI invariants + docs; editor compose + render) shipped as scoped; every
  Postcondition (1–13) and Test-plan item (1–9) has a corresponding, present, green test.
- **Architecture-review fixes folded in before this record was written:** the review's one blocking
  finding (the plan's Related section promised two backlog entries; only one existed) was resolved by
  adding the "Progression-rate expectation tolerances" backlog entry; a non-blocking finding (the
  golden-pin test asserting bounds rather than exact values, from having no SDK available at authoring
  time) was resolved once the Docker build path was found, per the Decisions note above; a stale
  README sentence was updated.
- **Deferred, as planned:** `timeToTier` target kind (needs an XP/objective-based ascension gate —
  `IAscensionSystem.CanAscend` is the named seam, see the ascension unlock-grant backlog entry);
  engine-chained report-to-scenario consumption (the editor prefill hop is the deliberate depth for
  now); a hybrid real-combat-per-kill executor mode (re-evaluation trigger: the Ascension
  calibration-gap fix).
- **Promotion trigger recorded, not built:** a **third** scenario kind is the point to refactor the
  report envelope into per-kind payload sections (`SchemaVersion` 2) and land an executor-strategy
  seam in `SimulationRunner` — tracked in
  [`../backlog.md`](../backlog.md#-progression-rate-sweep--event-source-generalization-forward-notes-from-sim-4-planning).
- **Next in the `balance-simulator` program:** `sim-5` (template/instance conformance tooling) is the
  only remaining sub-slice.
