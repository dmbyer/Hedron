# Simulation Editor Integration (sim-3)

**Status:** planned
**Actors:** Administrator/Designer (composes, launches, cancels, and inspects simulation runs from `Hedron.Web`) · System (`SimulationRunService` background execution; the sim-2 engine as pure callee)
**Module:** `Core/Modules/Simulation/` (small additive seams only) · `Hedron.Web` (Simulation page, run service, editor entry points) · Feature: [`../features/simulation/simulation.md`](../features/simulation/simulation.md)

> **Sub-slice of the [`balance-simulator.md`](balance-simulator.md) program seed (prog-4, row sim-3).** The seed's Design notes, Architecture brief, and resolved decisions (advisor intake 2026-07-13) are authoritative and are not relitigated here. This plan **resolves seed open question 5** (editor long-run execution model) — see Design notes. Seed OQ4 (report schema/retention) was resolved by sim-2 (JSON, `SchemaVersion: 1`, `{timestamp}-{scenarioName}-{seed}.json` under `Simulation:ReportDirectory`, run history = directory listing) and is consumed as-is.

---

## Description

Gives the sim-2 engine its second surface: a Blazor **Simulation page** in `Hedron.Web` where a designer composes a `ScenarioDefinition` (all three combatant sources), launches it as a **background run** with live status and cancellation, and browses/opens the JSON report artifacts with verdict rendering; **"Simulate vs reference"** entry points on `MobEditor`/`ItemEditor` that prefill the composer pitting the authored content against the reference build of its authored `(Tier, Band)` cell (computed cell when unbanded); and a **"Re-run baseline sweep"** affordance on the Standards page that enqueues per-cell equal-cell and adjacent-pair scenarios through the same service. The page is a **thin caller** over the one engine (content-tooling precedent: how the editors call `IContentGenerationSystem`/`IBalanceAuditSystem`): validation stays in `ISimScenarioStore.Validate`, verdict math stays in `ISimOutcomeEvaluator`, artifacts come from the same `ISimReportWriter`. Four small **additive** engine seams are required and audited below: cancellation + progress parameters on `ISimulationRunner.Run`, a report reader (`ISimReportReader`), scenario save/list on `ISimScenarioStore` (+ `Simulation:ScenarioDirectory`), and a verdict-cell annotation fallback for unbanded mob-template combatants.

---

## Preconditions

- sim-1 shipped: `IBalanceStandardsRegistry` (`ReferenceSnapshot`, `OutcomesFor`, `Tunables`, `BandDriftTolerance`) resolvable in both hosts; Standards page live.
- sim-2 shipped: `SimulationModule` registered via `Server/CompositionRoot.Register`, so `Hedron.Web` already resolves `ISimulationRunner`/`ISimScenarioStore`/`ISimReportWriter` directly (stated sim-3 hook).
- `Hedron.Web` runs loopback-only with `AddContentBootstrapHostedServices` (no heartbeat, no telnet, no persistence flush) — unchanged.
- `Simulation:ReportDirectory` configured in both hosts' `appsettings.json` (sim-2).

## Postconditions

1. A designer can compose any structurally valid `ScenarioDefinition` on `/simulation` — all three `CombatantSourceKind`s, any DI-registered policy id, seed/iterations/maxTicksPerRun — and every structural violation surfaces inline **before any run executes**, produced by the same `ISimScenarioStore.Validate` the CLI uses (no per-surface validation fork).
2. Launch executes off the UI/circuit thread via the singleton `SimulationRunService`; batches run **single-flight FIFO** (the engine already saturates cores via `Parallel.For`; concurrent batches never overlap); the page stays responsive; run statuses are observable from any circuit/page and survive navigation.
3. Live status shows state (`Queued`/`Running`/`Completed`/`Failed`/`Canceled`), completed-runs / total-runs progress, and start/finish timestamps (via `IClock`), refreshed by polling while any run is active.
4. Cancelling a **queued** run marks it `Canceled` without executing; cancelling an **active** run stops cooperatively between per-iteration runs, writes **no report artifact** (report files are always complete), and marks it `Canceled` with the count completed at cancellation.
5. A completed editor run produces the **identical artifact class** a CLI run does — same `ISimReportWriter`, same schema v1, same filename convention, same directory — and its verdicts are computed by the same `ISimOutcomeEvaluator` (INV-19: editor, CLI, and CI can never read different verdict math).
6. Run history lists every readable report artifact in `Simulation:ReportDirectory` (newest first; unreadable files flagged per-file, never failing the listing); opening one renders win/draw counts and rates, TTK/damage `DistributionStats`, and each `SimVerdict` as pass / fail / skipped-with-reason. CLI-written reports appear identically (one artifact class, two producers).
7. A composed scenario can be **saved to / loaded from** `Simulation:ScenarioDirectory` through `ISimScenarioStore` (validate-then-write, atomic tmp→rename, camelCase YAML identical to the hand-authored shape); a page-saved scenario is CLI-runnable via `simulate --scenario <path>` with no changes.
8. `MobEditor`/`ItemEditor` gain a "Simulate vs reference" entry that navigates to `/simulation` with the composer prefilled: the authored content on side A vs the `ReferenceBuild` of its authored `(Tier, Band)` cell on side B — falling back to the **computed** cell (the editors' existing oracle readout) when the content is unbanded (`Band == 0`). Launch remains an explicit click (prefill-only, never auto-run).
9. A mob-template combatant whose template is unbanded but whose `CombatantSpec` carries `Tier`/`Band` annotations resolves those annotations as its **verdict cell** (authored template tag still wins when `Band >= 1`), so entry-point runs on unbanded mobs get verdicts instead of skipped-with-reason.
10. The Standards page "Re-run baseline sweep" enqueues, through the same run service, one equal-cell scenario per `(Tier, Band)` cell and one adjacent-pair scenario per consecutive global-band-index pair (fixed seeds, defaults below); results land as ordinary report artifacts in the history.
11. Engine invariants hold unchanged: the Simulation module still references no `IEventBus`/`EcsManager` (Tier-5 guard green); a fixed `(scenario, seed)` pair remains byte-identical whether launched from CLI or editor, with or without the progress callback (INV-26); no `PersistentEntity`/`SaveEntityAsync` anywhere in the slice (INV-22 by absence).
12. Docs current on ship: flow-33 gains the editor legs, `flows/README.md` row updated, feature/reference docs updated, `README.md` config table gains `Simulation:ScenarioDirectory`, `architecture/08-blazor.md` documents the background-tooling-job shape (amending its "presentation only" claim), `architecture/07-testing.md` + the `add-tests` skill reflect the test project's new `Hedron.Web` reference, and the seed's open question 5 is marked resolved.

## Main flow

1. Designer opens `/simulation` (new nav link). The page loads the saved-scenario list (`ISimScenarioStore.List`), the report history (`ISimReportReader.List`), the policy ids (injected `IEnumerable<ISimCombatantPolicy>`), the mob summaries (`IContentDefinitionCatalog.List(ContentKind.Mob)`), and the cell table bounds (`IBalanceStandardsRegistry.Tunables`).
2. Designer composes a scenario in the form (or loads a saved one, or arrives prefilled from an editor entry point) and clicks **Run**; optionally clicks **Save scenario** first (`ISimScenarioStore.SaveAsync` — validate-then-write; errors inline).
3. The page calls `SimulationRunService.Enqueue(scenario)`, which runs `ISimScenarioStore.Validate` immediately — a structural violation throws, the page renders the named errors inline, and nothing is queued.
4. The service's single background drain loop dequeues the run, marks it `Running`, and calls `ISimulationRunner.Run(scenario, maxParallelism: null, cancellationToken: <per-run CTS>, onRunCompleted: <thread-safe increment>)` on a background task — the engine itself stays synchronous per batch (seed brief: ordering & timing).
5. While any run is active the page polls `SimulationRunService.Snapshot()` on a ~750 ms timer (`InvokeAsync(StateHasChanged)`), rendering per-run progress; **Cancel** signals the run's CTS (queued runs cancel in place; an active batch throws `OperationCanceledException` between runs and is marked `Canceled`, no report written).
6. On successful completion the service calls `ISimReportWriter.WriteAsync(report)`, records the artifact path plus a verdict summary on the status, and marks it `Completed`; a thrown engine error is captured as `Failed` with the message.
7. The page refreshes the history (`ISimReportReader.List`); the designer opens a report (`ISimReportReader.Read(path)`) and reviews rates, distributions, and verdicts.
8. Entry points rejoin at step 2/3: `MobEditor`/`ItemEditor` navigate to `/simulation?source=mob|item&id=<blueprintId>` (the page builds the prefill from the saved template via `SimulationPrefill` — catalog + projection + registry); the Standards page **Re-run baseline sweep** composes the per-cell scenario list (web-side `BaselineSweep.Compose`) and enqueues each, linking to `/simulation` for status.

## Events fired

**None.** The engine publishes nothing (INV-5); editor-run completion is UI state held by `SimulationRunService` in `Hedron.Web`, not a bus fact — no live-world observer exists for an offline sim (seed Architecture brief, "Observers, contributors & event granularity"; INV-10 — there is no Initiator here in the bus sense, only a web host calling a tooling-tier system).

## Systems / handlers involved

**Existing (reused, unchanged behavior):** `ISimScenarioStore` (validate/load), `ISimulationRunner` (batch execution), `ISimOutcomeEvaluator` (verdicts — inside the runner), `ISimReportWriter` (artifacts), `ISimCombatantFactory` (resolution — inside the runner), `ISimCombatantPolicy` built-ins (policy-id list), `IBalanceStandardsRegistry` (cells, tunables), `IContentDefinitionCatalog` (mob dropdown, entry-point template loads), `IItemPowerProjectionSystem`/`IMobPowerProjectionSystem` + `IPowerBudgetSystem.Classify` (computed-cell fallback for prefill — the editors' existing readout path), `IClock` (status timestamps).

**Extended (additive engine seams — see Design notes for each rationale):**
- `ISimulationRunner.Run(scenario, int? maxParallelism = null, CancellationToken cancellationToken = default, Action? onRunCompleted = null)` — CT wired to `ParallelOptions.CancellationToken`; callback invoked once per completed run from worker threads (documented contract: thread-safe, cheap, non-throwing).
- `ISimScenarioStore.SaveAsync(ScenarioDefinition, ct)` + `IReadOnlyList<ScenarioFileSummary> List()`; `SimulationOptions.ScenarioDirectory` (default `data/sim/scenarios`).
- `SimCombatantFactory.Resolve` — `MobTemplate` source honors optional `CombatantSpec.Tier`/`Band` as verdict-cell **fallback** when the template's own `Band == 0`.

**New:**
- `ISimReportReader`/`SimReportReader` (`Core/Modules/Simulation/Systems/`) — `List()` over `Simulation:ReportDirectory` + `Read(path)`, sharing the writer's `JsonSerializerOptions` via an extracted internal `SimReportJson`.
- `SimulationRunService` (`Hedron.Web/Services/`) — singleton FIFO background-run registry: `Enqueue`, `Snapshot`, `Cancel`; per-run `SimRunStatus` record; bounded in-memory retention (durable history stays the report directory).
- `BaselineSweep` (`Hedron.Web/Services/`, static composer) — cell table → scenario list for the Standards-page sweep.
- `SimulationPrefill` (`Hedron.Web/Services/`, static composer) — entry-point prefill composition: mob/item template → prefilled scenario (authored-cell vs computed-cell fallback; the item snapshot sum), kept out of the razor pages so the decision logic is testable.
- Pages: `Simulation.razor` (new), edits to `MobEditor.razor`, `ItemEditor.razor`, `Standards.razor`, `MainLayout.razor` (nav link).

No handlers, no commands, no components, no archetype changes.

## Implementation plan — work packages

### WP1 — Additive engine seams (Core + tests)

- **Scope:** the four seams above + `SimulationModule` registers `ISimReportReader`; extract `SimReportJson` (shared `JsonSerializerOptions`) from `SimReportWriter`; `SimulationOptions.ScenarioDirectory` + `appsettings.json` key in both hosts; `SimScenarioStore` gains a camelCase YAML **serializer** mirroring its existing DTOs (upsert by sanitized scenario name, atomic tmp→rename, `Validate` before write).
- **Files:** `Core/Modules/Simulation/Systems/ISimulationRunner.cs`/`SimulationRunner.cs`/`ISimScenarioStore.cs`/`SimScenarioStore.cs`/`SimCombatantFactory.cs`/`SimReportWriter.cs` (+ new `ISimReportReader.cs`/`SimReportReader.cs`/`SimReportJson.cs`), `Core/Modules/Simulation/SimulationOptions.cs`/`SimulationModule.cs`, `Server/appsettings.json`, `Hedron.Web/appsettings.json`, `Hedron.Tests/Simulation/*`.
- **Out of scope:** any change to report schema, verdict math, scenario validation rules, executor, or CLI behavior (`SimulateRunMode` untouched — new parameters default).
- **Exit criterion:** `dotnet test` green including new Tier-1/Tier-4 tests (Test plan 1–4, 9); `simulate` CLI runs the example scenario byte-identically to pre-slice at the same seed.

### WP2 — Run service + Simulation page (Hedron.Web + tests)

- **Scope:** `SimulationRunService` (single drain loop, per-run CTS, `Interlocked` progress, `IClock` timestamps, error capture, bounded retention ~50) registered singleton in `Hedron.Web/Program.cs`; `Simulation.razor` (composer for all three sources + policy dropdowns, save/load scenario, launch, polling status list with cancel, history list + report view incl. verdict rendering, `?source=&id=` prefill); `MainLayout` nav link; `Hedron.Tests` gains a `ProjectReference` to `Hedron.Web` and `Hedron.Tests/Web/SimulationRunServiceTests` against faked `ISimulationRunner`/`ISimScenarioStore`/`ISimReportWriter`.
- **Depends on:** WP1.
- **Out of scope:** entry-point buttons and sweep (WP3); report deletion (deferred — see Content tooling impact); bUnit/page-render tests (skipped per rubric).
- **Exit criterion:** manual: compose → launch → watch progress → cancel mid-run (no artifact) → re-launch → open report; `SimulationRunServiceTests` green (Test plan 5).

### WP3 — Entry points, baseline sweep, docs (Hedron.Web + docs)

- **Scope:** "Simulate vs reference" buttons on `MobEditor`/`ItemEditor` (navigate to prefilled composer; hint: "simulates the last-saved version"); entry-point prefill composition extracted into the testable `SimulationPrefill` helper (`Hedron.Web/Services/`, parity with `BaselineSweep`) — the item prefill composes an `Inline` spec = per-score sum of `ReferenceSnapshot(cell)` + `IItemPowerProjectionSystem.Project(template)` annotated with the item's cell, vs a `ReferenceBuild` of the same cell, and both prefills carry the authored-cell vs computed-cell fallback branch — plus `SimulationPrefillTests`; `BaselineSweep.Compose` + Standards-page button + `BaselineSweepTests`; doc updates — `flow-33` editor legs + `flows/README.md` row, `features/simulation/simulation.md`/`simulation-engine.md`, `reference/systems.md` rows, root `README.md` config table, `architecture/08-blazor.md` (documents the new background-tooling-job interaction shape — host-singleton state, polling, no bus, no hosted service, engine as pure callee — and amends the "presentation only" claim it contradicts), `architecture/07-testing.md` §The test harness (test project now also references `Hedron.Web`; where web-service tests live and their tier), `.claude/skills/add-tests/SKILL.md` (same — "where tests live" + web-service tier note), seed `balance-simulator.md` OQ5 marked resolved, backlog note for the web-background-job promotion trigger.
- **Depends on:** WP1, WP2.
- **Out of scope:** any auto-run on navigation; any conformance/auto-fit affordance (sim-5); progression-rate scenario UI (sim-4 — the report view renders only what schema v1 carries; kind-generic rendering proves out at sim-4).
- **Exit criterion:** manual walk of all three entry points incl. an unbanded mob (verdict present via the WP1 fallback); `BaselineSweepTests` + `SimulationPrefillTests` green (Test plan 6, 6b); architecture-reviewer doc-drift checks clean.

**Defaults (resolved, not open):** entry-point and sweep scenarios use `iterations: 200`, `maxTicksPerRun: 100`, `policyId: cooldown-first`, fixed `seed: 1234` (mirrors `example-equal-cell.yaml`); all values editable in the composer before launch. Sweep breadth: **all** cells `[0..MaxTier] × [1..BandsPerTier]` (~21 equal-cell + ~20 adjacent-pair scenarios ≈ 8.2k runs — seconds, per sim-2's measured 10k-run timing), not authored-only.

## Content tooling impact

**This slice is the content-tooling slice for scenarios (INV-18).** Scenario YAML (`data/sim/scenarios/`) — hand-authorable since sim-2 — becomes editor-authorable: composed, validated, saved, and re-loaded on the Simulation page through the same `ISimScenarioStore`, and every saved file remains CLI-runnable (`simulate --scenario`). Report artifacts become inspectable in-editor (history + verdict rendering) rather than raw-JSON-only. New config key: `Simulation:ScenarioDirectory` (Category 1, default `data/sim/scenarios`, both hosts). No `TemplateRegistry` entries, no admin commands, no new data-file shapes — the scenario and report shapes are sim-2's, unchanged. **Deferred:** in-page report deletion/pruning (the directory is gitignored local data a designer can clear manually; a delete affordance would push write semantics into the reader — revisit if sweep clutter proves real).

## Cross-cutting surfaces stressed

| Surface | Classification | Rationale |
|---|---|---|
| Commands | **Adequate** | No telnet commands; the surface is Blazor-only, matching the Standards/Integrity precedent. |
| Output | **Adequate** | No game output; Blazor rendering only (presentation, untested per rubric). |
| Persistence | **Adequate** | None. **Persistence opt-in audit:** no entity construction path is added or modified — sandbox entities remain non-persistent and discarded with their `EntityService` (unchanged sim-2 posture); no component is added or touched, so no `[Persistent]` classification arises; no `SaveEntityAsync` call site anywhere in the slice (INV-22 by absence — Level 3 vacuously satisfied). Scenarios/reports are the established file-artifact class, deliberately outside SQLite (INV-14) and world YAML. |
| Event bus | **Adequate** | Engine still publishes nothing (INV-5); run completion is web-service state per the seed brief; Tier-5 guard continues to enforce no `IEventBus` in the module. |
| ECS queries | **Adequate** | None new; the sandbox world composition is untouched. |
| Broadcast / sessions | **Adequate** | Untouched; web host has no game sessions. |
| Time | **Adequate** | Report `GeneratedAt` stays on `IClock` (sim-2); `SimulationRunService` injects `IClock` for status timestamps (testable, INV-26-consistent); the poll timer is pure presentation cadence, never an outcome input. |
| Content templates | **Adequate** | Mob dropdown and entry-point loads use existing `IContentDefinitionCatalog` reads; no catalog write path is touched. |
| Configuration | **Adequate** | One additive key on the existing `SimulationOptions` class, documented in both hosts + README. |
| Modules | **Adequate** | Additive registrations on the existing `SimulationModule`; web-only services register in `Hedron.Web/Program.cs` (host composition, mirroring how hosted services are per-host). |
| Scenario/standards YAML store pattern | **Acknowledged debt** | `SimScenarioStore` gaining a hand-rolled save deepens the second instance of the backlogged "YAML-authored definition pipeline for registry families" generalization (`BalanceStandardsStore` first, `SimScenarioStore` second). Still two families — below the ≥3 trigger; existing backlog entry stands. |
| Background execution in `Hedron.Web` | **Adequate (watch item)** | First background-job pattern in the web host — a single-instance, deliberately sim-specific service; a generic web-job framework now would be premature (INV-19's bar is ≥3 or a new player-facing surface *framework* need — this is one designer-facing instance). **Named promotion trigger recorded in `backlog.md` (WP3):** if sim-5's bulk conformance apply (or any second long-running editor job) wants queue/progress/cancel, generalize `SimulationRunService` into a shared web-job service rather than hand-rolling a second one. |

## Flows introduced or modified

- **[Flow 33 — Simulation run journey](../architecture/flows/flow-33-simulation-run.md) — modified** (no new flow file): gains the editor trigger leg (Simulation page → `SimulationRunService.Enqueue` → validate → background `Run(ct, progress)` → `WriteAsync`), the cancellation path, the report-read leg (`ISimReportReader`), and the entry-point/sweep prefill hop — the flow-29 precedent of one content-tooling journey carrying multiple triggers. `flows/README.md` row 33 trigger column updated to "CLI `simulate` **or** Simulation page in `Hedron.Web`".
- **[Flow 29 — Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md) — touched only by cross-reference** (a pointer from the editor-surface leg to flow-33's editor leg; no structural change).

The WP3 PR updates both files; the architecture-reviewer blocks on drift (INV-17).

## Test plan / Verification

Per the [07-testing.md](../architecture/07-testing.md) rubric — Postconditions asserting designer-invisible internal state each map to a named test:

1. **T1 — `SimulationRunnerTests` (extended):** (a) a pre-canceled token throws `OperationCanceledException` without completing the batch; (b) `onRunCompleted` fires exactly `Iterations` times on success; (c) **determinism unperturbed** — same scenario/seed with and without the callback produces equivalent reports (`AssertReportsEquivalent`) [Postconditions 4, 11].
2. **T1 — `SimScenarioStoreTests` (extended):** `SaveAsync` of a valid scenario writes atomically (no leftover `.tmp`) and `Load` of the written path round-trips a field-equal definition; an invalid scenario is refused with named errors and writes nothing; `List` returns saved files; upsert-by-name overwrites [Postcondition 7].
3. **T1 — `SimReportReaderTests` (new):** `Read` of a `SimReportWriter`-written file round-trips schema version, aggregates, and verdict tuples; `List` orders newest-first and flags (never throws on) an unreadable file; unknown JSON fields are tolerated (additive-schema posture) [Postcondition 6].
4. **T1 — `SimCombatantFactoryTests` (extended):** unbanded mob template + spec `Tier`/`Band` annotation → resolved verdict cell = annotation; banded template ignores the annotation (authored tag wins); unbanded + no annotation → no cell (unchanged) [Postcondition 9].
5. **T1 — `SimulationRunServiceTests` (new, `Hedron.Tests/Web/`, faked runner/store/writer/clock):** invalid scenario → `Enqueue` throws and nothing is queued; `Queued → Running → Completed` transition with report path recorded; runner exception → `Failed` with message, no writer call; cancel-while-queued → `Canceled`, runner never called; cancel-while-active (fake runner observes CT) → `Canceled`, **no writer call**; single-flight — second enqueue stays `Queued` until the first terminates; progress counter reaches total [Postconditions 2–5].
6. **T1 — `BaselineSweepTests` (new):** composes exactly one equal-cell scenario per cell and one adjacent-pair scenario per consecutive global-band-index pair over the tunables' cell table; every composed scenario passes `ISimScenarioStore.Validate`; seeds/names are deterministic [Postcondition 10].
6b. **T1 — `SimulationPrefillTests` (new):** a banded mob prefills side B with the `ReferenceBuild` of its authored cell; an unbanded mob falls back to its computed cell; the item prefill's `Inline` scores equal the per-score sum of `ReferenceSnapshot(cell)` and the item's projected snapshot, annotated with the item's cell; every composed prefill passes `ISimScenarioStore.Validate` [Postcondition 8].
7. **T5 — existing guards:** `Simulation_module_does_not_reference_EventBus_or_EcsManager` stays green over the extended module; DI-smoke resolves `ISimReportReader` [Postcondition 11].
8. **Manual verification:** full page walk (compose all three sources → save → launch → progress → cancel → relaunch → open report); all three entry points including an unbanded mob; a sweep; a CLI-written report appearing in the history.
9. **T1 — determinism cross-surface pin:** one test runs the example scenario through the extended `Run` signature and asserts equivalence with the sim-2 golden expectations (CLI/editor byte-identity at fixed seed) [Postcondition 11].

**Skipped, with reasons:** `.razor` markup and interaction (presentation; no bUnit harness in the repo — a bug is obvious on first page load, per the rubric's discriminator); poll-timer cadence (presentation); exact report-rendering prose (exact-wording assertions are brittle/low-value); heavy sweeps stay out of CI (seed rule — CI keeps only sim-2's promoted thin invariants). **Testability gaps: none** — no new un-injected seam; the run service takes `IClock` and interface fakes; the engine stays synchronous and seeded.

## Design notes

> Folds the seed's seam decisions; durable content disintegrates into `features/simulation/*` on ship (INV-28).

- **Seed OQ5 resolved: background task + polling, not streamed progress.** A singleton `SimulationRunService` owns run state; pages poll `Snapshot()` on a timer while active. Rationale: (a) Blazor Server circuits are transient — polling a host-singleton survives navigation/disconnect/reconnect and lets any page observe the same runs, while streamed/callback push binds run lifetime to one circuit; (b) run history is already a pull model (directory listing) — status polling is the same posture; (c) batches are short (sim-2 measured 10k runs < 4 s *including* process startup — in-process far less), so sub-second polling is indistinguishable from streaming and SignalR streaming machinery buys nothing at loopback; (d) the poll reads in-memory singleton state — no I/O per tick. **Cancellation semantics:** cooperative, checked between per-iteration runs via `ParallelOptions.CancellationToken`; a canceled batch throws `OperationCanceledException`, writes **no** report (artifacts are always complete — schema untouched), and discards its sandbox worlds; queued runs cancel in place. Progress is a per-run-completion callback counted with `Interlocked` — carries no data, cannot perturb seeds, scheduling of the index-ordered reduce, or report content (Test 1c pins this).
- **Why the engine gains four seams, and why they are additive.** Cancellation cannot live web-side (abandoning a `Parallel.For` thread is not cancellation); the report **reader** must share the writer's `JsonSerializerOptions` or deserialization forks per surface (the INV-19 posture applied to serialization — hence `SimReportJson` extracted, reader beside writer in the module); scenario **save** must live in the store because the store owns the YAML DTO shape — a page-side serializer would be a second YAML dialect; the **verdict-cell fallback** belongs in `SimCombatantFactory.Resolve` because cell resolution is engine semantics the CLI and CI also see (an unbanded mob scenario hand-authored with annotations now gets verdicts everywhere, identically). All four are backward-compatible: default parameters, new interface, additive option, fallback-only semantics — `SimulateRunMode` and every sim-2 test path are untouched.
- **One composer, thin entry points.** The editor buttons never launch runs directly — they navigate to the Simulation page with prefill (`?source=&id=`), keeping a single compose/launch/status surface and making "review before run" structural. They read the **last-saved** template (catalog load), matching the semantics of every other cross-page link in the editor; snapshotting unsaved form edits is deliberately not done — **resolved**: it would require serializing form state across navigation for marginal gain, and last-saved semantics match every other cross-page editor link (confirmed at the spec gate).
- **The item entry point is data composition, not forked math.** An item is not a combatant; its prefill builds an `Inline` spec whose scores are the per-score **sum** of two existing computed seams — `IBalanceStandardsRegistry.ReferenceSnapshot(cell)` + `IItemPowerProjectionSystem.Project(template)` — i.e. "a baseline character of this cell wearing this item," vs a bare `ReferenceBuild` of the same cell. Summing two snapshots in the page is composition of engine-computed values (the `Inline` source exists exactly for caller-composed candidates); no weight, band, or verdict math is re-derived.
- **Baseline sweep composition lives web-side.** `BaselineSweep.Compose` is scenario *selection*, not verdict math — the engine's INV-19 guarantee covers validation and verdicts, which the sweep reuses untouched. Today it has one consumer; if the CLI ever wants `simulate --sweep` (second consumer) it promotes into the module. Recorded as a watch item, not built speculatively.
- **Single-flight execution.** One batch at a time: the engine already parallelizes across all cores per batch; concurrent batches would oversubscribe and confound wall-clock comparisons between reports. FIFO queueing keeps sweep launches simple (enqueue N, watch them drain).
- **No bus events, no hosted service.** Completion/failure is `SimRunStatus` state — the seed brief's explicit call ("editor-run completion is a UI concern in `Hedron.Web`, not a bus fact"). The drain loop is a plain background task inside the singleton, not an `IHostedService` — it does no work when the queue is empty and needs no lifecycle beyond the host's.
- **Test project now references `Hedron.Web`.** Required to unit-test `SimulationRunService`/`BaselineSweep` where they live (the seed places them in the web host, not Core). The reference adds no test-tier ambiguity: web services get Tier-1-style decision tests; razor markup stays skipped.

## Related

- [`balance-simulator.md`](balance-simulator.md) — the prog-4 program seed (authoritative brief; OQ5 resolved by this plan).
- [`../features/simulation/simulation.md`](../features/simulation/simulation.md) · [`../features/simulation/simulation-engine.md`](../features/simulation/simulation-engine.md) — the sim-2 engine this slice is a thin caller of.
- [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) — the sim-1 standards registry (reference builds, cells, editor readout the entry points anchor to).
- [`../roadmap/completed/simulation-engine-core.md`](../roadmap/completed/simulation-engine-core.md) · [`../roadmap/completed/balance-standards-registry.md`](../roadmap/completed/balance-standards-registry.md) — prerequisite as-built histories.
- [`../architecture/flows/flow-33-simulation-run.md`](../architecture/flows/flow-33-simulation-run.md) · [`flow-29`](../architecture/flows/flow-29-bulk-content-generation.md) — the journeys this slice extends/mirrors.
- Next in program: `sim-4` (progression-rate scenarios — renders in this slice's report view) · `sim-5` (conformance tooling — candidate second consumer of the background-run pattern).
