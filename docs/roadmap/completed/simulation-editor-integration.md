# Simulation editor integration (slice sim-3, completed)

> Implemented on branch `claude/sim-editor-integration-plan-pi2wbq`, 2026-07-16. Living docs:
> [`features/simulation/simulation.md`](../../features/simulation/simulation.md) ·
> [`features/simulation/simulation-engine.md`](../../features/simulation/simulation-engine.md) ·
> [`architecture/08-blazor.md`](../../architecture/08-blazor.md).

## Outcome

Gives the sim-2 engine its second surface: a Blazor **Simulation page** (`/simulation`) in
`Hedron.Web` where a designer composes any structurally valid `ScenarioDefinition`, launches it as a
background run with live status and cancellation, and browses/opens the same JSON report artifacts
the CLI writes. A new host-singleton `SimulationRunService` owns a single-flight FIFO run queue; four
small, additive, backward-compatible engine seams (cancellation + progress on `ISimulationRunner.Run`,
a new `ISimReportReader`, `SaveAsync`/`List` on `ISimScenarioStore`, and a verdict-cell fallback in
`SimCombatantFactory`) let the page reuse the engine exactly, never fork its validation or verdict
math. "Simulate vs reference" entry points on `MobEditor`/`ItemEditor` and a "Re-run baseline sweep"
button on the Standards page compose scenarios through two new pure web-side composers and enqueue
through the same run service.

## Behavior digest

- **Postcondition 1** — the composer surfaces every structural violation inline, before any run
  executes, via the same `ISimScenarioStore.Validate` the CLI uses.
- **Postcondition 2** — launch runs off the UI thread via `SimulationRunService`; batches are
  single-flight FIFO (never concurrent); the page stays responsive; statuses are observable from any
  page/circuit and survive navigation.
- **Postcondition 3** — live status shows state, completed/total progress, and start/finish
  timestamps (via `IClock`), refreshed by polling while any run is active.
- **Postcondition 4** — cancelling a queued run marks it `Canceled` without executing; cancelling an
  active run stops cooperatively between iterations, writes no report, and records the completed
  count at cancellation.
- **Postcondition 5** — a completed editor run produces the identical artifact class a CLI run
  does — same writer, schema, filename convention, directory, and verdict math (INV-19).
- **Postcondition 6** — run history lists every readable report artifact newest-first (unreadable
  files flagged per-file, never failing the listing); opening one renders rates, distributions, and
  verdicts. CLI-written and editor-written reports are indistinguishable in the history.
- **Postcondition 7** — a composed scenario can be saved to / loaded from `Simulation:ScenarioDirectory`
  through `ISimScenarioStore` (validate-then-write, atomic tmp→rename, camelCase YAML identical to
  the hand-authored shape); a page-saved scenario is `simulate --scenario <path>`-runnable unchanged.
- **Postcondition 8** — `MobEditor`/`ItemEditor` gain a "Simulate vs reference" entry navigating to a
  prefilled composer: authored content vs. the `ReferenceBuild` of its authored `(Tier, Band)` cell,
  falling back to the computed cell when unbanded. Launch stays an explicit click.
- **Postcondition 9** — a mob-template combatant whose template is unbanded but whose `CombatantSpec`
  carries `Tier`/`Band` annotations resolves those as its verdict cell (authored template tag still
  wins when banded).
- **Postcondition 10** — the Standards page "Re-run baseline sweep" enqueues one equal-cell scenario
  per `(Tier, Band)` cell and one adjacent-pair scenario per consecutive global-band-index pair
  through the same run service.
- **Postcondition 11** — engine invariants hold unchanged: the Simulation module still references no
  `IEventBus`/`EcsManager`; a fixed `(scenario, seed)` pair stays byte-identical with or without the
  progress callback; no `PersistentEntity`/`SaveEntityAsync` anywhere in the slice.
- **Postcondition 12** — docs current on ship: flow-33 gains the editor legs, feature/reference docs
  updated, README config table gains `Simulation:ScenarioDirectory`, `08-blazor.md` documents the
  background-tooling-job shape, `07-testing.md`/`add-tests` reflect the new `Hedron.Web` test
  reference, and seed OQ5 is marked resolved.

## Shipped pieces

| Surface | Location |
|---|---|
| `ISimulationRunner.Run` — additive `CancellationToken` + `onRunCompleted` params | `Core/Modules/Simulation/Systems/ISimulationRunner.cs` · `SimulationRunner.cs` |
| `ISimReportReader`/`SimReportReader`/`SimReportSummary` (new) | `Core/Modules/Simulation/Systems/ISimReportReader.cs` · `SimReportReader.cs` |
| `SimReportJson` (new, extracted shared serializer options) | `Core/Modules/Simulation/Systems/SimReportJson.cs` |
| `ISimScenarioStore.SaveAsync`/`List`/`ScenarioFileSummary` (new) | `Core/Modules/Simulation/Systems/ISimScenarioStore.cs` · `SimScenarioStore.cs` |
| `SimCombatantFactory` — verdict-cell fallback for unbanded mob templates | `Core/Modules/Simulation/Systems/SimCombatantFactory.cs` |
| `SimulationOptions.ScenarioDirectory` (new) | `Core/Modules/Simulation/SimulationOptions.cs` |
| `SimulationModule` — registers `ISimReportReader` | `Core/Modules/Simulation/SimulationModule.cs` |
| `SimulationRunService`/`SimRunState`/`SimRunStatus` (new, singleton) | `Hedron.Web/Services/SimulationRunService.cs` |
| `BaselineSweep` (new, static composer) | `Hedron.Web/Services/BaselineSweep.cs` |
| `SimulationPrefill` (new, static composer) | `Hedron.Web/Services/SimulationPrefill.cs` |
| `CombatantForm` (new, composer form model) | `Hedron.Web/Services/CombatantForm.cs` |
| `Simulation.razor` (new page) | `Hedron.Web/Components/Pages/Simulation.razor` |
| `CombatantSideEditor.razor` (new, extracted per-side form component) | `Hedron.Web/Components/Shared/CombatantSideEditor.razor` |
| `MobEditor.razor`/`ItemEditor.razor` — "Simulate vs reference" button + `?source=&id=` prefill target | `Hedron.Web/Components/Pages/MobEditor.razor` · `ItemEditor.razor` |
| `Standards.razor` — "Re-run baseline sweep" button | `Hedron.Web/Components/Pages/Standards.razor` |
| `MainLayout.razor` — nav link | `Hedron.Web/Components/Layout/MainLayout.razor` |
| `Program.cs` — registers `SimulationRunService` singleton | `Hedron.Web/Program.cs` |
| `appsettings.json` (both hosts) — `Simulation:ScenarioDirectory` | `Server/appsettings.json` · `Hedron.Web/appsettings.json` |
| `Hedron.Tests.csproj` — new `ProjectReference` to `Hedron.Web` | `Hedron.Tests/Hedron.Tests.csproj` |
| Reference rows (2 new systems, 3 extended) | `docs/reference/systems.md` |
| `flow-33-simulation-run.md` — editor trigger leg, cancellation path, report-read leg, prefill hop | `docs/architecture/flows/flow-33-simulation-run.md` |
| `08-blazor.md` — "Background tooling jobs" section | `docs/architecture/08-blazor.md` |

## Tests shipped

64 new/extended tests, `dotnet test` green at 1159 total (up from 1128 pre-slice).

- **Tier 1** — `SimulationRunnerTests` extended (pre-canceled token throws before completing;
  `onRunCompleted` fires exactly `Iterations` times; with/without callback produce equivalent
  reports — determinism unperturbed); `SimulationInvariantTests` extended (the extended `Run`
  signature reproduces the golden CLI expectations at the fixed seed); `SimScenarioStoreTests`
  extended (`SaveAsync` writes atomically and round-trips a field-equal definition; invalid scenario
  refused, writes nothing; `List` returns saved files; same-name upsert overwrites);
  `SimReportReaderTests` (new — round-trips schema/aggregates/verdicts; `List` orders newest-first
  and flags, never throws on, an unreadable file; tolerates unknown additive JSON fields);
  `SimCombatantFactoryTests` extended (unbanded + spec annotation → annotation is the cell; banded
  template ignores the annotation; unbanded + no annotation → no cell); `SimulationRunServiceTests`
  (new, `Hedron.Tests/Web/`, faked runner/store/writer/clock — invalid scenario throws and queues
  nothing; Queued→Running→Completed with report path; runner exception → Failed, no writer call;
  cancel-while-queued → Canceled, runner never invoked; cancel-while-active → Canceled, no writer
  call; single-flight — second enqueue stays Queued until the first terminates; progress counter
  reaches total); `BaselineSweepTests` (new — one equal-cell scenario per cell, one adjacent-pair
  scenario per consecutive global-band-index pair, every composed scenario validates, deterministic
  seeds/names); `SimulationPrefillTests` (new — banded mob prefills the authored cell, unbanded falls
  back to the computed cell, item prefill's `Inline` scores equal the per-score sum of
  `ReferenceSnapshot` + the item's projection, every composed prefill validates).
- **Tier 5** — existing `Simulation_module_does_not_reference_EventBus_or_EcsManager` guard stays
  green over the extended module.
- **Manual verification** — `dotnet run --project Hedron.Web` served `/`, `/standards`, and
  `/simulation` with no unhandled exceptions on initial render (curl-fetched HTML checked for a
  `<title>` and absence of Blazor error markers); the mob browser's Simulation nav link resolved.
  Full interactive walk (compose → save → launch → progress → cancel → relaunch → open report; all
  three entry points; a sweep) was not driven through a live browser session in this environment —
  flagged per the Test plan's own skip rubric ("a bug in `.razor` markup/interaction is obvious on
  first page load; no bUnit harness in the repo").

## Decisions

- **Single background drain loop, not per-run tasks.** `SimulationRunService` runs one `Task.Run`
  loop guarded by a `_draining` flag under `lock (_lock)`; `Enqueue` starts the loop only if it isn't
  already running. This is what makes single-flight structural rather than a policy the caller has to
  respect — the engine already saturates cores per batch, so a second concurrent batch would
  oversubscribe and confound wall-clock comparisons between reports.
- **Retention evicts terminal entries only.** `TrimRetention` (~50 cap) never removes a `Queued`/
  `Running` entry — only `Completed`/`Failed`/`Canceled` ones, oldest first. Durable history is the
  report directory; the in-memory registry is a bounded recent-activity window, not a source of truth.
  Not exercised by an automated test (no test enqueues 50+ runs) — a low-risk gap given the eviction
  logic is a straightforward linear scan and the report-directory listing is the actual source of
  truth for anything evicted.
- **`SimReportSummary.List` never throws on a bad file.** Each file is parsed independently inside a
  `try/catch`; a JSON failure produces a `Readable: false` row with the exception message rather than
  aborting the whole listing — matching Postcondition 6's literal wording and the writer/reader
  symmetry principle (one artifact class, two producers, neither privileged).
- **`CombatantForm` extracted as a plain class, not kept as a private nested type.** The Simulation
  page and the extracted `CombatantSideEditor` child component both need to read/mutate the same
  per-side form state; a private nested class in one `.razor` file's `@code` block isn't visible from
  a sibling component, so it moved to `Hedron.Web/Services/CombatantForm.cs` — plain mutable data, no
  validation logic (that stays exclusively in `ISimScenarioStore.Validate`).
- **`SimulationPrefill`/`BaselineSweep` are static, dependency-free composers, not DI services.**
  Both take their few dependencies (`IPowerBudgetSystem`, projection systems, `PowerBudgetTunables`)
  as plain parameters rather than constructor-injecting themselves — there's no per-call state to
  own, and static functions are trivially unit-testable without a DI container in
  `Hedron.Tests/Web/`.
- **The item prefill sums two snapshots rather than re-deriving power math.** `SimulationPrefill.ForItem`
  builds an `Inline` combatant whose scores are `IBalanceStandardsRegistry.ReferenceSnapshot(cell)` +
  `IItemPowerProjectionSystem.Project(template)`, per-score — "a baseline character of this cell
  wearing this item." No weight, band, or verdict math is re-derived; the `Inline` source exists
  exactly for this caller-composed-candidate use.
- **`.NET 8 SDK` was not preinstalled in this execution environment** and had to be installed via
  `apt-get install dotnet-sdk-8.0` (the official `dotnet-install.sh` script's download host was
  blocked by the session's outbound proxy policy) before `dotnet build`/`dotnet test` could run at
  all. Unrelated to the slice's design, noted here only because it affected how verification was
  carried out.

## Deviations / Follow-ups

- **No deviations from the plan's shape.** All three work packages (additive engine seams; run
  service + Simulation page; entry points + baseline sweep + docs) shipped as scoped; every Test-plan
  item (1–9, 6b, 9) is present and green.
- **Deferred, as planned:** in-page report deletion/pruning (the report directory is gitignored local
  data a designer can clear manually); any auto-run-on-navigation affordance; the conformance/auto-fit
  UI (sim-5); progression-rate scenario rendering (sim-4 — the report view already renders schema-v1
  fields generically).
- **Promotion trigger recorded, not built:** if a second long-running editor job (candidate: sim-5's
  bulk conformance apply) wants the same queue/progress/cancel shape, generalize
  `SimulationRunService` into a shared web-job service — tracked in
  [`../backlog.md`](../backlog.md#-web-background-job-service-generalization-promotion-trigger-recorded-sim-3).
- **Acknowledged debt deepened, not created:** `SimScenarioStore` gaining `SaveAsync` is a second
  hand-rolled per-family YAML save path (`BalanceStandardsStore` was the first) — still two
  instances, below the backlogged ≥3-instance generalization trigger.
- **Follow-up:** `sim-4` (progression-rate scenarios) is next in the `balance-simulator` program —
  the Simulation page's report view already renders schema-v1 fields kind-generically, so it should
  need no UI change to show a `ProgressionRate` report once that executor lands.
