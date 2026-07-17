# Balance standards registry (slice sim-1, completed)

> Implemented on branch `claude/balance-standards-registry-24608c`, 2026-07-13. Living docs:
> [`features/progression/power-budget-system.md`](../../features/progression/power-budget-system.md) ·
> [`features/admin-authoring/content-authoring.md`](../../features/admin-authoring/content-authoring.md) ·
> [`design/power-model.md`](../../design/power-model.md).

## Outcome

Promotes the balance criteria from compiled constants to a designer-editable, YAML-authored
**balance-standards registry** (the balance-simulator program's Spine F seam). `PowerBudgetConstants`
and `BalanceAuditConstants` are retired: `PowerBudgetSystem` now takes exactly one constructor
parameter — the plain-data `PowerBudgetTunables` record — composed by `BalanceInspectionModule` from
a new `IBalanceStandardsRegistry`, itself backed by `IBalanceStandardsStore`'s YAML load/validate/save
(compiled defaults as fallback, fail-fast structural validation, load-time mirror-drift warnings
against `AscensionConstants`/`CharacterDefaultsOptions`, atomic tmp→rename write). The oracle stays
snapshot-only and zero-domain-dependency (INV-2) — the tunables record is data, not a service. A new
Blazor **Standards page** (`/standards`) authors tunables, per-(Tier, Band) reference builds (gear
bonuses + a shaped-but-inert ability-kit field), the band-drift audit tolerance, and expected-outcome
tolerances, with a live derived-target-range preview (a throwaway oracle instance) and a refuse-vs-warn
save (restart-to-apply). `IBalanceAuditSystem`, `powerband`, and both content editors' band-mismatch
flags now read tolerance/index math from the registry instead of a compiled constant. First sub-slice
of the expanded `prog-4` balance-simulator program (all five sub-slices now shipped — see
[`conformance-tooling.md`](conformance-tooling.md) for the final one).

## Behavior digest

- **Oracle ctor-injection (INV-2 preserved by construction).** `PowerBudgetSystem`'s single ctor
  parameter is `PowerBudgetTunables` — a plain-data record (`Weights`, `BandSpan`, `BandsPerTier`,
  `ReferenceBaseScores`, `MaxTier`, `TierBaselineStep`, `TrackedScores`, plus the pure
  `GlobalBandIndex(tier, band)` method). `PowerBudgetTunables.Default` is the compiled fallback;
  every `Estimate`/`Classify`/`TargetRange` result under `Default` is numerically identical to the
  pre-slice constant-backed output (golden-number tests re-run unchanged).
- **Standards document, store, registry.** `BalanceStandardsDocument` (`Tunables`,
  `BandDriftTolerance`, global `Outcomes`, sparse `Cells: IReadOnlyList<BalanceStandard>`);
  `BalanceStandard` (`Tier`, `Band`, `ReferenceBuild`, optional `OutcomesOverride`);
  `ReferenceBuildDefinition` (`GearBonuses` + inert `AbilityKit`); `OutcomeTolerances`
  (`EqualCellWinRate`/`WinRateTolerance`/`HigherBandWinRateFloor`, inert until sim-2).
  `BalanceStandardsStore.Load()` reads `Balance:StandardsPath` (default
  `data/balance/standards.yaml`), falls back to `BalanceStandardsDefaults.Document` when absent,
  runs structural validation (unknown score id, out-of-range/duplicate cell, `BandSpan` calibration
  violation, negative tolerance — throws `InvalidOperationException`), then mirror-drift + unknown-
  ability-kit-id validation (returns warnings, never silent). `SaveAsync` re-validates
  structurally (refuses the write on failure, no partial file) then writes atomically.
  `BalanceStandardsRegistry` dense-fills every (Tier, Band) cell in
  `[0, MaxTier] × [1, BandsPerTier]` at construction — sparse-authored cells fill with an
  empty-gear build and the global outcomes.
- **Weights/ReferenceBaseScores are merged over compiled defaults, not replaced wholesale.**
  Discovered while writing the mirror-drift test: authoring one score's weight or reference base
  must not silently drop every other score from the table. `MergeScoreMap` starts from a copy of
  `PowerBudgetTunables.Default`'s map and overlays only the authored entries.
- **Eager boot resolution.** `RegistryValidationBootstrap` (already the "fail fast on bad content
  at boot" hosted service on both hosts) takes `IBalanceStandardsRegistry` as an otherwise-unused
  constructor dependency — forcing its singleton factory to run during hosted-service construction,
  before any `StartAsync` executes, so a structurally invalid standards file fails boot instead of
  the first admin command/editor page hit.
- **DI composition.** `BalanceInspectionModule` registers the store, a load-once
  `IBalanceStandardsRegistry` factory (logs returned warnings + an info summary), projects
  `PowerBudgetTunables` from the registry, then the oracle, `BalanceAuditSystem` (now taking
  `PowerBudgetTunables` + an injected `int bandDriftTolerance`, both sourced from the registry),
  and the two inspector commands.
- **Standards page.** `/standards` lists the full cell table with derived target ranges (a
  throwaway `PowerBudgetSystem` over the in-progress candidate tunables — never the composed,
  DI-registered instance), edits tunables/cell reference builds/tolerances, and saves through
  `IBalanceStandardsStore.SaveAsync` with refuse-vs-warn rendering and a restart-to-apply notice.
  Verified end-to-end in-browser: default save (no warnings), a `maxTier` edit producing a real
  mirror-drift warning, and a negative-tolerance edit correctly refused with no file write.
- **`docs/design/power-model.md` amended (doc-first, INV-15).** "Never gains a constructor
  dependency" narrowed to "never gains a *service or domain* dependency — the single
  caller-composed plain-data tunables record is the one permitted constructor input." The
  architecture-guard test enforces exactly that shape (one ctor param of type
  `PowerBudgetTunables`; no `Core/Modules/<Feature>/` import beyond `Stats` across the oracle's six
  files, `PowerBudgetTunables.cs` swapped in for the retired `PowerBudgetConstants.cs`).

## Shipped pieces

| Surface | Location |
|---|---|
| `PowerBudgetTunables` (new, replaces `PowerBudgetConstants`) | `Core/Systems/PowerBudgetTunables.cs` |
| `PowerBudgetSystem`/`IPowerBudgetSystem` — ctor-injected tunables | `Core/Systems/PowerBudgetSystem.cs` · `IPowerBudgetSystem.cs` |
| `BalanceStandardsDocument`/`BalanceStandard`/`ReferenceBuildDefinition`/`OutcomeTolerances` (new) | `Core/Modules/BalanceInspection/Standards/BalanceStandardsDocument.cs` |
| `BalanceStandardsDefaults` (new, compiled fallback document) | `Core/Modules/BalanceInspection/Standards/BalanceStandardsDefaults.cs` |
| `IBalanceStandardsStore`/`BalanceStandardsStore` (new, YAML load/validate/save) | `Core/Modules/BalanceInspection/Standards/IBalanceStandardsStore.cs` · `BalanceStandardsStore.cs` |
| `IBalanceStandardsRegistry`/`BalanceStandardsRegistry` (new, dense-filled cell table) | `Core/Modules/BalanceInspection/Standards/IBalanceStandardsRegistry.cs` · `BalanceStandardsRegistry.cs` |
| `BalanceOptions` (new, `Balance:StandardsPath`) | `Core/Modules/BalanceInspection/BalanceOptions.cs` |
| `BalanceAuditSystem` — tunables + injected `bandDriftTolerance` | `Core/Modules/BalanceInspection/Systems/BalanceAuditSystem.cs` |
| `PowerbandCommand` — table bounds from injected tunables | `Core/Modules/BalanceInspection/Commands/PowerbandCommand.cs` |
| `BalanceInspectionModule` — store/registry load-once factory, tunables projection | `Core/Modules/BalanceInspection/BalanceInspectionModule.cs` |
| `RegistryValidationBootstrap` — eager `IBalanceStandardsRegistry` resolution | `Server/RegistryValidationBootstrap.cs` |
| `CompositionRoot` — `BalanceOptions` configuration | `Server/CompositionRoot.cs` |
| `appsettings.json` (both hosts) — `Balance:StandardsPath` default | `Server/appsettings.json` · `Hedron.Web/appsettings.json` |
| Blazor `Standards.razor` (new page) + nav link | `Hedron.Web/Components/Pages/Standards.razor` · `Hedron.Web/Components/Layout/MainLayout.razor` |
| `ItemEditor`/`MobEditor` — mismatch flag reads `IBalanceStandardsRegistry` | `Hedron.Web/Components/Pages/ItemEditor.razor` · `MobEditor.razor` |
| `docs/design/power-model.md` — amended constructor-dependency rule | `docs/design/power-model.md` |

## Tests shipped

- **Tier 1** — `PowerBudgetSystemTests` re-golded against `new PowerBudgetSystem(PowerBudgetTunables.Default)`
  (identical expected values, proving Postcondition 1); two new tests proving a synthetic
  non-default tunables record actually shifts `Estimate`/`Classify`/`TargetRange` (injection is
  real, not decorative). `BalanceStandardsRegistryTests` (new): sparse-fill, `ReferenceSnapshot` =
  base + gear, `OutcomesFor` per-cell-override preference, dense `AllIds` coverage.
  `BalanceStandardsStoreTests` (new): band-ordering calibration violation throws; unknown score id
  / duplicate cell / out-of-range tier-band / negative tolerance each throw with detail; missing
  file returns the compiled defaults with no warnings; a drifted `maxTier` + `referenceBaseScores`
  entry loads successfully with one warning per field; an unknown ability-kit id warns, not fails.
  `BalanceAuditSystemTests` extended: the flagged set changes with the injected
  `bandDriftTolerance`.
- **Tier 2** — `PowerbandCommandTests` re-golded against injected `PowerBudgetTunables.Default`;
  new test proves row count derives from a genuinely non-default `MaxTier`/`BandsPerTier` tunables
  record, not `Default`.
- **Tier 4** — `BalanceStandardsStoreTests` save→load round-trip (scalar + dictionary fields);
  atomic-write check (no leftover `.tmp`, no partial file on a refused save).
- **Tier 5** — `ArchitectureGuardTests.PowerBudgetSystem_has_no_domain_module_dependency` revised:
  exactly one ctor param of type `PowerBudgetTunables`; file-scan swaps `PowerBudgetConstants.cs` →
  `PowerBudgetTunables.cs`. `RegistryValidationTests` updated for the new
  `RegistryValidationBootstrap` constructor shape (constructs a `BalanceStandardsRegistry` over
  compiled defaults).
- **Manual verification** — both hosts booted clean against no standards file (info log, zero
  warnings); the Standards page verified end-to-end in-browser (default save, a real mirror-drift
  warning on a `maxTier` edit, a refused save on a negative tolerance with no file written).
- `dotnet build` and `dotnet test` green — 1064 tests total (up from 1045 pre-slice).

## Decisions

- **Weights/ReferenceBaseScores merge over defaults, not replace (found during implementation).**
  A partial `tunables.referenceBaseScores` YAML block that names only one score must not silently
  zero out every other score's reference base — `MergeScoreMap` starts from
  `PowerBudgetTunables.Default`'s map and overlays only the authored keys. `ParseScoreMap` (start
  from empty) stays correct for cell `gearBonuses`, which are purely additive, not a base.
- **Target ranges stay derived, never authored (seed OQ1, resolved).** No per-cell authored
  min/max — `TargetRange` remains the oracle's pure derivation from the (now data-backed) tunables,
  avoiding a dual-truth drift class.
- **Standalone Spine F registry, not a fifth `ContentKind` (seed OQ1, resolved).** The standards
  store deliberately sits outside `IContentDefinitionCatalog` (no blueprint id, no delete-cascade)
  but mirrors its validate-then-write / refuse-vs-warn / atomic-write posture — instance #1 of the
  backlogged "YAML-authored definition pipeline for registry families" generalization.
  `BalanceStandardsRegistry` reuses `IRegistry<PowerBand, BalanceStandard>` for the cell table.
  `GlobalBandIndex` moved onto `PowerBudgetTunables` as a pure method — one home for the index math
  (INV-27), replacing the retired `BalanceAuditConstants.GlobalBandIndex`.
- **Eager boot resolution via `RegistryValidationBootstrap`, not a new hosted service.** Rather
  than adding a dedicated bootstrap, the standards registry rides the existing "fail fast on bad
  content at boot" hosted service already registered on both hosts — an unused constructor
  dependency forces DI to construct (and thus validate) the registry before any hosted service's
  `StartAsync` runs. Verified against the real server/web-host boot log: the standards-load info
  line appears first, before `PersistenceBootstrap`.
- **Restart-to-apply, live-reload deferred (seed OQ2, resolved).** The oracle's ctor-injection
  means live tunables mutation would require re-composing the registry/oracle singletons; a saved
  edit applies on the next host start. A live-reload path is a named backlog entry, not scope —
  the editing cadence doesn't yet justify it.
- **Sparse-cell fill semantics (seed OQ4, resolved).** Missing cells default to an empty-gear
  build + the global outcomes; a criteria file need not author all ~21 cells on day one.
- **Reference builds: scores + gear-equivalent bonuses now, ability kit shaped-but-inert (seed
  OQ2, resolved).** `AbilityKit` exists in the schema from day one (validated as a warning,
  consumed by nothing) so sim-2+ activates it without a schema break.
- **`docs/design/power-model.md` amendment kept narrow (Open question 5, verified at spec gate).**
  The wording change permits exactly one plain-data ctor input — a service or domain dependency is
  still prohibited. The revised architecture-guard test enforces this so the relaxation can't widen
  silently in a later slice.

## Deviations / Follow-ups

- **No deviations from the plan.** All three work packages (oracle tunables promotion; standards
  document/store/registry/host composition; Standards page + living-docs updates) shipped as
  scoped; every Test-plan item (1–12) is present and green.
- **Follow-up:** `sim-2` (simulation engine core) is next in the expanded `prog-4`
  balance-simulator program — it consumes this registry's reference builds and outcome tolerances
  as its default combatants and expected-vs-actual oracle. Live balance-standards reload, and the
  YAML-authored-definition-pipeline generalization (this slice is instance #1; the ≥3-instance
  trigger is not yet crossed), are tracked in [`../backlog.md`](../backlog.md).
