# Balance Standards Registry (sim-1)

**Status:** planned
**Actors:** Administrator/Designer (authors standards in the Blazor editor, inspects via `powerband`/Integrity) · System (both hosts load standards at boot; oracle/audit/editors consume them) · Future consumers (sim-2 engine, sim-5 conformance, procedural generator)
**Module:** `Core/Modules/BalanceInspection/` (standards store + registry + audit) · `Core/Systems/` (oracle tunables as injected plain data) · `Server`/`Hedron.Web` (host composition) · `Hedron.Web` (Standards page)

> Sub-slice **sim-1** of the [balance-simulator program seed](balance-simulator.md). The seed's Architecture brief and resolved decisions (advisor intake 2026-07-13 + user resolutions of seed OQ 1/2/7, 2026-07-13) are authoritative inputs — folded into Design notes below, not relitigated.

---

## Description

Promotes the balance criteria from compiled constants to a designer-editable, YAML-authored **balance-standards registry** (the program's Spine F): per-(Tier, Band) reference builds (gear-equivalent stat bonuses + a shaped-but-inert ability-kit field), expected-outcome tolerances (equal-cell win rate ± tolerance, higher-band win-rate floor), the band-drift audit tolerance, and the `IPowerBudgetSystem` tunables (`Weights`/`BandSpan`/`BandsPerTier`/`ReferenceBaseScores`/`MaxTier`/`TierBaselineStep`/`TrackedScores`) from which target power ranges are **derived**. The oracle stays snapshot-only and zero-domain-dependency: its tunables arrive as one constructor-supplied plain-data record composed at startup by the hosts, never a registry/loader/domain reference. Compiled defaults (the current constant values) are the fallback when no file exists; structural violations fail boot fast; drift between the data file and the compiled gameplay mirrors (`AscensionConstants`, `CharacterDefaultsOptions`) is a load-time **warning**, never silent absorption. `IBalanceAuditSystem`, `powerband`, and both editors' band-mismatch flags read the new data; a **Standards page** in `Hedron.Web` ships in the same slice for authoring + readout (INV-18).

---

## Preconditions

- prog-3b shipped: `IPowerBudgetSystem` (Tier×Band `Classify`/`TargetRange`), `PowerBudgetConstants`, `IBalanceAuditSystem`, `powerband`/`power`, editor readouts, Integrity drift table — all live.
- `BalanceInspectionModule` registered via `CompositionRoot.Register` (both hosts already resolve the oracle + audit).
- `IRegistry<TKey,TDef>`/`DefinitionRegistry<TKey,TDef>` layer exists (`Core/Systems/DefinitionRegistry.cs`).
- Architecture-guard test `PowerBudgetSystem_has_no_domain_module_dependency` currently asserts a **zero-parameter** constructor — must be revised in-slice (see Test plan).
- [`docs/design/power-model.md`](../design/power-model.md) currently says the oracle "never gains a constructor dependency" — amended in-slice, doc-first (INV-15), to permit exactly the plain-data tunables record (see Design notes).

## Postconditions

1. `PowerBudgetSystem` takes exactly **one** constructor parameter, the plain record `PowerBudgetTunables` (`Core/Systems/`); it imports no `Core/Modules/<Feature>/` type beyond the allowlisted `Stats` (`ScoreId`); every estimate/classify/range result under `PowerBudgetTunables.Default` is **numerically identical** to pre-slice output (golden numbers unchanged).
2. `PowerBudgetConstants` and `BalanceAuditConstants` are retired; no compiled-constant read of `Weights`/`BandSpan`/`BandsPerTier`/`ReferenceBaseScores`/`MaxTier`/`TierBaselineStep`/`TrackedScores`/`BandDriftTolerance` remains outside `PowerBudgetTunables.Default` and the compiled standards defaults.
3. Both hosts boot with **no standards file present** and behave exactly as today (compiled defaults, info log).
4. A structurally invalid standards file (unknown score id, out-of-range/duplicate cell, `BandSpan ≥ tierSpan/BandsPerTier`, negative tolerance) **fails boot fast** with a message naming the file and violation.
5. A standards file whose mirror fields drift from `AscensionConstants`/`CharacterDefaultsOptions` boots successfully and logs one **warning per drifted field** (never silently absorbed).
6. `IBalanceStandardsRegistry` is resolvable in both hosts: dense (Tier, Band) cell table (sparse-authored cells fill with empty gear + global outcomes), `Tunables`, `BandDriftTolerance`, per-cell `OutcomesFor`, and `ReferenceSnapshot(tier, band)` = `ReferenceBaseScores + cell gear bonuses` (tier baseline enters via `Estimate`'s tier argument, as today).
7. `IBalanceAuditSystem`, the `powerband` command, the Integrity page, and both editors' mismatch flags read tolerance + index math from the standards data (via registry/tunables), not from any compiled constant.
8. The Blazor **Standards page** lists the full cell table with derived target ranges, edits tunables/reference builds/tolerances, previews derived ranges live from candidate tunables, and saves — structural failure refuses the write; mirror drift warns but allows; the write is atomic (tmp → rename) and round-trips (save → load → equal document).
9. A saved standards edit takes effect on next host start (restart-to-apply); the page states this explicitly.
10. The reference-build schema carries `AbilityKit` (list of ability ids) from day one; it is validated for shape (known ability ids → warning if unknown) but consumed by nothing until a later slice — adding consumption is additive, not a schema break.

---

## Main flow

1. **Boot (either host):** DI resolution of `IBalanceStandardsRegistry` invokes `IBalanceStandardsStore.Load()` once.
2. The store reads `Balance:StandardsPath` (default `data/balance/standards.yaml`); file absent → compiled defaults document (info log); file present → YAML-deserialize into `BalanceStandardsDocument`.
3. The store runs structural validation (score ids, cell ranges/duplicates, band-ordering rule, tolerance ranges) — any failure throws and the host fails fast at boot.
4. The store runs mirror-drift validation — `Tunables.MaxTier`/`TierBaselineStep`/`TrackedScores` vs `AscensionConstants`, `Tunables.ReferenceBaseScores` vs `CharacterDefaultsOptions` (+ the base `AttackPower = Body/2`, `Defense = Body/4` derivations) — and returns warnings, which the DI factory logs.
5. `BalanceInspectionModule` composes the graph: `BalanceStandardsRegistry` (from the document), `PowerBudgetTunables` (projected from the registry), `PowerBudgetSystem` (constructed with the tunables record). Consumers — `powerband`, `power`, `IBalanceAuditSystem`, `ItemEditor`/`MobEditor` mismatch flags, Integrity — all read through the registry/oracle.
6. **Authoring:** designer opens `/standards` in `Hedron.Web`; the page renders tunables, the ~21-cell table (derived `TargetRange` per cell, gear bonuses, ability kit, outcome tolerances), and the drift tolerance.
7. Designer edits; the page previews derived target ranges by constructing a throwaway `PowerBudgetSystem` over the candidate tunables (the oracle is a pure class — no DI mutation).
8. Designer saves; the store validates (structural failure → refuse, no write; mirror/ability-kit drift → warnings, write proceeds) and writes atomically.
9. The page confirms the write, renders warnings distinctly, and shows the restart-to-apply notice; on next boot, step 1 closes the loop.

---

## Events fired

**None.** Standards are boot-composed data; the store and registry return results (INV-5); the Blazor page is a thin surface outside the bus; no live-world fact occurs. (Matches the seed's observer analysis: "the engine publishes nothing" — true a fortiori for sim-1.)

## Systems / handlers involved

| Piece | Kind | Status |
|---|---|---|
| `IPowerBudgetSystem` / `PowerBudgetSystem` (`Core/Systems/`) | Core system | **Changed** — ctor gains `PowerBudgetTunables`; algorithm untouched |
| `PowerBudgetTunables` (`Core/Systems/`) | Plain data record + `Default` | **New** — replaces `PowerBudgetConstants`; carries `GlobalBandIndex(tier, band)` (pure, uses only `BandsPerTier`) |
| `IBalanceStandardsStore` / `BalanceStandardsStore` (`Core/Modules/BalanceInspection/Standards/`) | Domain system (tooling-tier) | **New** — Load / Validate / SaveAsync over the standards YAML; owns mirror-drift checks (may import `Ascension`/`Account` — it is domain-tier) |
| `IBalanceStandardsRegistry` / `BalanceStandardsRegistry` (`…/Standards/`) | Registry (`IRegistry<PowerBand, BalanceStandard>` + extras) | **New** — dense cell table, `Tunables`, `BandDriftTolerance`, `OutcomesFor`, `ReferenceSnapshot` |
| `BalanceStandardsDocument` / `BalanceStandard` / `ReferenceBuildDefinition` / `OutcomeTolerances` / `BalanceStandardsDefaults` (`…/Standards/`) | Data + compiled defaults | **New** |
| `IBalanceAuditSystem` / `BalanceAuditSystem` | Domain system | **Changed** — tolerance + index math from registry/tunables |
| `PowerbandCommand` / `PowerCommand` | Commands (existing verbs) | **Changed** — table bounds from injected tunables; verbs/output shape unchanged |
| `BalanceInspectionModule` | DI module | **Changed** — registers store, registry factory (load-once + warning log), tunables projection |
| `BalanceOptions` (`Server`-configured section `Balance`) | Category-1 config | **New** — `StandardsPath` |
| Blazor: `Standards.razor`; `Integrity.razor` / `ItemEditor.razor` / `MobEditor.razor`; nav | Surface (thin) | **New page + touched pages** |

**No handlers, no events, no components, no archetypes, no entities.**

---

## Implementation plan — work packages

### WP-1 — Oracle tunables promotion (pure refactor, defaults-only)

**Scope:** `PowerBudgetTunables` record in `Core/Systems/` (`Weights`, `BandSpan`, `BandsPerTier`, `ReferenceBaseScores`, `MaxTier`, `TierBaselineStep`, `TrackedScores`; static `Default` = current constant values; instance `GlobalBandIndex(tier, band)`). `PowerBudgetSystem` ctor-injects it; delete `PowerBudgetConstants` and `BalanceAuditConstants`; migrate every reader (`PowerbandCommand`, `BalanceAuditSystem`, `ItemEditor.razor`/`MobEditor.razor` code blocks, `PowerbandMessage`/`PowerBand` doc comments, all tests). `BandDriftTolerance` moves to a temporary compiled default in `BalanceInspectionModule` (superseded by WP-2's document). Update the architecture-guard test (exactly one ctor param of type `PowerBudgetTunables`; file scan swaps `PowerBudgetConstants.cs` → `PowerBudgetTunables.cs`). Amend `docs/design/power-model.md` wording in the same package (doc-first, INV-15).
**Files:** `Core/Systems/*`, `Core/Modules/BalanceInspection/*`, `Hedron.Web/Components/Pages/{Item,Mob}Editor.razor`, `Hedron.Tests/Architecture/ArchitectureGuardTests.cs`, `Hedron.Tests/Modules/BalanceInspection/*`, `docs/design/power-model.md`.
**Out of scope:** any YAML, any loader, any UI beyond mechanical call-site migration.
**Exit criterion:** build + full test suite green; golden-number tests pass unchanged against `PowerBudgetTunables.Default`; guard test enforces the new ctor shape.

### WP-2 — Standards document, store, registry, host composition

**Scope:** `BalanceStandardsDocument` (+ `BalanceStandard`, `ReferenceBuildDefinition` with `GearBonuses` + inert `AbilityKit`, `OutcomeTolerances` global + optional per-cell override, `BandDriftTolerance`, tunables DTO); `BalanceStandardsDefaults` (compiled document = today's values); `IBalanceStandardsStore` (YAML load with defaults fallback, structural fail-fast validation, mirror-drift + unknown-ability-kit warnings, atomic `SaveAsync`); `BalanceStandardsRegistry` (dense fill, `ReferenceSnapshot`, `OutcomesFor`); DI wiring in `BalanceInspectionModule` (load-once factory, warning logging, `PowerBudgetTunables` projected from registry — `Default` no longer directly registered); `BalanceOptions` + `Balance:StandardsPath` in both hosts' configuration + `appsettings.json` default; audit/commands switch tolerance reads to the registry. Depends on WP-1.
**Out of scope:** Blazor page; flow/catalog doc updates beyond code comments.
**Exit criterion:** boot-with-no-file, fail-fast-on-invalid, warn-on-drift, and save→load round-trip all covered by green tests; both hosts boot.

### WP-3 — Standards page + living-docs updates

**Scope:** `Hedron.Web` `/standards` page (table readout with derived ranges, tunables/cell/tolerance editing, candidate-tunables live preview, save with refuse-vs-warn rendering, restart-to-apply notice) + nav link; Integrity page and editors confirmed reading live tolerance; docs: `flows/README.md` + flow-01 (startup gains standards-load step) and flow-29 (standards-editing leg), `05-configuration.md` (OD-2 resolved **for the power-budget/balance-standards family only** — other Category-3 families (`ProgressionConstants`, `CombatConstants`, …) stay compiled; the demonstrated trigger — a live designer edit surface + ≥3 data-hungry consumers — generalizes case-by-case; standards-file family documented), `reference/systems.md`/`reference/commands.md` rows, `features/progression/power-budget-system.md` + `features/admin-authoring/content-authoring.md` sections, `roadmap/backlog.md` entries (live standards reload; YAML-registry pipeline generalization), `.claude` skill/agent guidance stale against this slice (INV-20) — concretely: **`.claude/skills/edit-progression-system/SKILL.md` lines 34 and 57 cite `PowerBudgetConstants.Weights` by name** (a type this slice retires) and must be updated to the `PowerBudgetTunables` injected-instance phrasing (spec-gate blocking finding, 2026-07-13); also sweep `.claude/skills/add-core-system`, `add-domain-system`, and `architecture-advisor` for any "zero constructor dependencies" oracle phrasing. Depends on WP-2.
**Exit criterion:** page functional end-to-end against a temp data dir; docs pass the architecture-reviewer drift checks.

**Primary agent runs `architecture-reviewer` (code mode) across the combined diff once all three land.**

---

## Content tooling impact (INV-18)

- **New data-file shape:** `data/balance/standards.yaml` (path via `Balance:StandardsPath`) — one document: `tunables:` (weights map keyed by `ScoreId` name, `bandSpan`, `bandsPerTier`, `referenceBaseScores`, `maxTier`, `tierBaselineStep`, `trackedScores`), `bandDriftTolerance:`, `outcomes:` (global `equalCellWinRate`, `winRateTolerance`, `higherBandWinRateFloor`), `cells:` (list of `{tier, band, gearBonuses, abilityKit, outcomes?}`). Gitignored like all `data/`; compiled defaults make the file optional. **Not** a fifth `ContentKind` — see Design notes.
- **Authoring surface (same slice):** the Blazor Standards page (author + inspect + preview). Readout also via the existing `powerband` command (now data-backed) and the Integrity drift table.
- **No `TemplateRegistry` entries, no admin commands added.** No `reload` support for standards (restart-to-apply; backlogged).

## Cross-cutting surfaces stressed (INV-19 / ground rule 9)

| Surface | Classification | Notes |
|---|---|---|
| Commands | **Adequate** — `powerband`/`power` keep verbs, gating, and output shapes; only their data source changes. |
| Output | **Adequate** — `PowerbandMessage` unchanged; no new telnet output. |
| Persistence | **Adequate (untouched)** — opt-in audit: Level 1: no entity construction paths (standards are boot data, not entities). Level 2: no components introduced or touched. Level 3: no `SaveEntityAsync` anywhere; the standards write is YAML-side, mirroring the catalog's `SaveAsync` posture (INV-22/23 not in play). |
| Event bus | **Adequate (untouched)** — no events; store/registry return results (INV-5). |
| ECS queries | **Adequate (untouched)**. |
| Content templates / authoring pipeline | **Acknowledged debt** — the standards store hand-rolls a focused YAML load/validate/save path *outside* `IContentDefinitionCatalog` (deliberate: seed OQ1 resolution — the catalog's `IEntityTemplate`/spawn/delete-cascade semantics don't fit balance data) and outside the compiled-rows `DefinitionRegistry` construction. This is instance #1 of a "YAML-authored definition pipeline for registry families" (Ability/Effect/Aspect definitions are the future #2/#3 — the ≥3 trigger). **Backlog entry required in WP-3**; the store's Load/Validate/Save shape is written to be extractable when the trigger fires. |
| Configuration | **Adequate** — `Balance:StandardsPath` is a clean Category-1 key; the promotion itself is the long-predicted OD-2 resolution and updates `05-configuration.md` in-slice (INV-15/27). |
| Sessions / broadcast / time | **Adequate (untouched)** — no clock, no randomness, no sessions. |
| Modules / DI | **Adequate** — stays in `BalanceInspectionModule` (registered in `CompositionRoot.Register`, so both hosts get it for free); no new module needed (seed left this to the planner — resolved: no `Balance` module split until the Simulation module lands and proves a need). |

## Flows introduced or modified (INV-17)

- **[Flow 1 — Server startup](../architecture/flows/flow-01-server-startup.md), modified:** startup gains the standards-load step (path resolve → defaults fallback → structural fail-fast → mirror-drift warnings → registry/tunables/oracle composition). Applies to both hosts; the flow doc and `flows/README.md` row are updated in WP-3.
- **[Flow 29 — Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md), modified:** the offline-edit leg gains the Standards page (edit → validate → refuse-or-warn → atomic write → restart-to-apply), alongside the existing catalog-backed editors.
- **No new flow file** — no new recurring runtime path beyond these two extensions.

## Test plan / Verification (INV-25)

System-unit tier (`Hedron.Tests/Modules/BalanceInspection/`, plus `Core.Systems` mirrors):

1. **Oracle golden numbers (migrated)** — existing `PowerBudgetSystemTests` re-run against `new PowerBudgetSystem(PowerBudgetTunables.Default)`; identical expected values prove Postcondition 1's "numerically identical" claim.
2. **Oracle with non-default tunables (new)** — a synthetic tunables record shifts `Estimate`/`Classify`/`TargetRange` as predicted (proves injection is real, not decorative).
3. **Band-ordering structural rule** — `BandSpan ≥ tierSpan/BandsPerTier` in a document → store `Load`/`Validate` **throws** (fail-fast test; this rule was previously only a test assertion, now data can break it at runtime).
4. **Store: defaults fallback** — missing file → document equals `BalanceStandardsDefaults.Document`, no warnings (Postcondition 3).
5. **Store: structural fail-fast** — unknown score id / duplicate cell / out-of-range tier-band / negative tolerance → throws with the offending detail (Postcondition 4).
6. **Store: mirror-drift warnings** — a document with `maxTier` ≠ `AscensionConstants.MaxTier` and a drifted `referenceBaseScores` entry loads successfully and returns one warning per drifted field (Postcondition 5).
7. **Store: save→load round-trip** — `SaveAsync` to a temp dir then `Load` yields an equal document; write is atomic (no partial file on injected failure) (Postcondition 8).
8. **Registry: dense fill + composition** — sparse-authored cells fill with empty gear + global outcomes; `ReferenceSnapshot(t, b)` = base + gear; `OutcomesFor` prefers the per-cell override (Postcondition 6).
9. **Audit tolerance from data** — `BalanceAuditSystemTests` migrated: a registry with `BandDriftTolerance = 0` vs `= 2` changes the flagged set (Postcondition 7).
10. **`PowerbandCommand` (migrated)** — row counts derive from injected tunables, including a non-default `MaxTier`/`BandsPerTier`.
11. **Architecture-guard (revised)** — `PowerBudgetSystem` has exactly one ctor param of type `PowerBudgetTunables`; no `Core/Modules/<Feature>/` import beyond `Stats` across the oracle file set including `PowerBudgetTunables.cs` (Postconditions 1–2).
12. **Ability-kit shape** — unknown ability id in `AbilityKit` → load **warning**, not failure (Postcondition 10).

**Testability seams:** none missing — no randomness, no wall-clock (INV-26 trivially satisfied); the store takes an explicit path (temp-dir testable, matching the content-writer test precedent).

**Skipped:** Blazor Standards page markup/interaction (presentation-only, per the established editor-page posture — logic under test lives in store/registry); exact log/warning prose; YamlDotNet serialization internals (covered implicitly by round-trip).

---

## Design notes

> Folded from the seed's Architecture brief + the 2026-07-13 sim-1 resolutions. Durable; disintegrates into `features/progression/power-budget-system.md`, a new standards system doc, and `docs/design/` on ship.

- **Standalone Spine F registry, not a fifth `ContentKind` (seed OQ1, resolved).** Standards are balance data, not world content — `IContentDefinitionCatalog`'s `IEntityTemplate`/spawn/delete-cascade semantics don't fit a single-document criteria file. The registry reuses `IRegistry<PowerBand, BalanceStandard>` for the cell table; the store reuses the catalog's *posture* (validate-then-write, refuse-vs-warn, atomic write) without its machinery. This deliberately seeds the backlogged **YAML-authored definition pipeline for registry families** — extract the shared loader when Ability/Effect/Aspect definitions go YAML (the ≥3 signal).
- **The oracle stays snapshot-only; the tunables are plain injected data (seed decision 2 — the spec gate's central check).** `PowerBudgetSystem` gains no registry, no loader, no domain import: one `PowerBudgetTunables` record, composed by `BalanceInspectionModule` from the loaded document, `Default` as the compiled fallback. **`docs/design/power-model.md` is amended in WP-1 (doc-first, INV-15):** "never gains a constructor dependency" becomes "never gains a *service or domain* dependency — the single caller-composed plain-data tunables record is the one permitted constructor input." The guard test is revised to enforce exactly that shape, so the relaxation cannot widen silently.
- **Target ranges stay derived, never authored (planner decision — see Open question 1).** The seed lists "target power ranges" as a registry fact family; storing explicit per-cell min/max alongside the tunables they're computed from would create dual truth and a new drift class. `TargetRange` remains the oracle's derivation from the (now data-backed) tunables; the registry and the Standards page *expose* the derived ranges per cell.
- **Reference builds: scores + gear-equivalent bonuses day one; ability kit shaped-but-inert (seed OQ2, resolved).** `ReferenceBuildDefinition.AbilityKit` exists in the schema from day one (validated as a warning, consumed by nothing) so sim-2+ activates it without a schema break. A cell's snapshot = `ReferenceBaseScores + GearBonuses`; the tier baseline enters via `Estimate`'s tier argument exactly as live snapshots do.
- **`BandDriftTolerance` joins the standards data (seed OQ7, resolved).** Same fact family; `BalanceAuditConstants` retires; the Integrity page and editor mismatch flags read it live through the registry. `GlobalBandIndex` moves to a pure method on `PowerBudgetTunables` (its only input is `BandsPerTier`) — one home for the index math (INV-27).
- **Mirror-sync becomes load-time validation (seed design note).** The mirrored facts (`MaxTier`/`TierBaselineStep`/`TrackedScores` ↔ `AscensionConstants`; `ReferenceBaseScores` ↔ `CharacterDefaultsOptions` + the base `AttackPower`/`Defense` derivations) are compared at load by the **domain-tier store** (which may legally import both modules) — the comment discipline becomes a warning, and the warning is the drift surface, never silent absorption.
- **Restart-to-apply.** The oracle's ctor-injection (resolved) means live tunables mutation would require re-composition; standards edits therefore apply on next host start, stated on the page. A live-reload path (re-compose registry + oracle on `reload`, or a provider indirection) is a **backlog entry**, not scope — the editing cadence at this stage doesn't justify it.
- **Expected-outcome tolerances land now, consumed at sim-2.** Global defaults + optional per-cell overrides honor the seed's "per (Tier, Band)" framing without forcing 21 rows of duplication. They are inert data until the sim engine compares expected-vs-actual — landing them here is what keeps sim-2 from hardcoding criteria (the seed's ordering rationale).
- **No events, no entities, no persistence.** Standards are boot-composed data + a YAML write path; nothing here touches the live world, SQLite, or the bus.

## Related

- [`balance-simulator.md`](balance-simulator.md) — the program seed (sim-2…sim-5 frame against it; sim-2 consumes this registry's reference builds + outcome tolerances).
- [`progression-and-balance.md`](progression-and-balance.md) — the parent program; prog-3/3b built the oracle this slice promotes.
- [`../design/power-model.md`](../design/power-model.md) — snapshot-only principle (amended in WP-1).
- [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) — current oracle/consumer map (updated in WP-3).
- [`../architecture/05-configuration.md`](../architecture/05-configuration.md) — OD-2, resolved by this slice for the power-budget/balance-standards family.
- [`../features/admin-authoring/content-tooling.md`](../features/admin-authoring/content-tooling.md) / [`content-authoring.md`](../features/admin-authoring/content-authoring.md) — the validate-then-write posture and editor-page discipline the Standards page mirrors.

## Open questions

> Questions 1–4 were put to the user on 2026-07-13 (post-planning, pre-spec-gate) and are resolved; 5 remains a spec-gate verification item.

1. **Derived-only target ranges** — ✅ **RESOLVED (2026-07-13): derived-only.** Ranges always compute from reference builds + the data-backed tunables via `TargetRange`; **no** authored per-cell min/max override field (dual-truth risk). Moving a range = editing the reference build or tunables. An optional override field remains an additive schema option if a future need is proven.
2. **Restart-to-apply** — ✅ **RESOLVED (2026-07-13): accepted** as this slice's application model (matching the existing YAML content-authoring cadence); live standards reload is the backlog entry. Provider indirection into the oracle rejected — it works against the resolved ctor-injection shape.
3. **Day-one outcome-tolerance fields** — ✅ **RESOLVED (2026-07-13): the proposed three** (`equalCellWinRate`, `winRateTolerance`, `higherBandWinRateFloor`). Inert until sim-2; further fields (e.g. TTK envelopes) are added additively when a scenario kind demands them, calibrated by real sim output rather than guessed now.
4. **Sparse-cell fill semantics** — ✅ **RESOLVED (2026-07-13): sparse-fill.** Missing cells default to compiled defaults (empty gear + global outcomes, per Postcondition 6); the standards page distinguishes authored vs default cells. Complete-or-fail rejected — a criteria file shouldn't demand 21 rows on day one.
5. **`power-model.md` amendment scope** — the doc-first wording change (plain-data ctor input permitted) is a consequence of seed decision 2, not a relitigation; flagged here so the spec gate verifies the amendment stays exactly that narrow (one record, no service/domain types).
