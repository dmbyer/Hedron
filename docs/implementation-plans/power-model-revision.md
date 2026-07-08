# Power model revision — Tier × Band + calibration + audit tooling (slice prog-3b)

**Status:** planned
**Actors:** Administrator/Designer (authors + classifies content by Tier×Band; audits drift) · System (the core oracle — classify a snapshot, invert a target) · Simulator (slice-4 consumer of the *revised* oracle — out of scope here, but the reason 3b exists before prog-4)
**Module:** `Core/Systems/` (`IPowerBudgetSystem` / `PowerBudgetConstants` revision) · `Core/Modules/Mobs/` + `Core/Modules/Items/` (the `TierBand` → `Tier`+`Band` field split: components, templates, builders, writers/deserializers, `setmob`/`setitem band`) · `Core/Modules/BalanceInspection/` (`power` / `powerband` inspectors + output messages) · `Hedron.Web` (both editors' readout + the Integrity sweep page) · **new** `docs/design/power-model.md` (the named extensibility principle) + `.claude/` INV-20 tooling updates

> **Architecture seed** (per the `architecture-advisor` skill). Holds the durable seam rationale + forward brief only; `/new-plan` (`implementation-planner`) extends this into the full plan (Preconditions/Postconditions, Main flow, Events, Systems/handlers, work packages, Content-tooling impact, Cross-cutting surfaces, Flows, Test plan). Framed against [`progression-and-balance.md`](progression-and-balance.md) **Open questions 7–12** (all resolved except the calibration *numbers*, now dispositioned below). Revises the shipped slice-3 oracle — [`../roadmap/completed/power-budget-inspector.md`](../roadmap/completed/power-budget-inspector.md), [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) — and reopens **nothing** in prog-1/prog-2. The shipped weighted-sum engine, the three-consumer framework, and the anti-grind rewire all hold up unchanged; only the Band/calibration/audit surface is revised.

---

## Description

Slice prog-3 shipped a power-budget oracle whose `Classify` collapsed two distinct concepts into one 0–6 number. This slice separates them into a **D&D-Challenge-Rating-style two-axis model**: **Tier** (0–6, the coarse Ascension-gated character scalar, unchanged) × **Band** (1–3 — low/mid/high *within* each tier, a finer subdivision that "feels meaningful without introducing a whole leveling system"). The oracle gains a two-axis `Classify` output and its inverse — `(Tier, Band) → target power range` — for forward design (and, later, procedural generation); the authored `TierBand` content tag splits into a `Tier`+`Band` pair across both content kinds and every authoring surface; the authored-vs-computed mismatch flag becomes a **band-count-tolerance drift audit** (soft — an editor flag plus a bulk Integrity-sweep report that doubles as free "how much content exists at power level X" reporting, never a build-blocking gate); the placeholder `PowerBudgetConstants` are recalibrated for real headroom and deliberate band spacing (**oracle estimation only** — gameplay power is untouched); and the oracle's snapshot-only extensibility rule is written down as a **named design principle** so future power inputs never teach the core oracle a domain concept.

---

## Preconditions

- The shipped slice-3 oracle exists: `IPowerBudgetSystem.Estimate/Classify/BandAnchor` (`Core/Systems/`), `PowerBudgetConstants`, `PowerSnapshot`. `Classify(power)` returns a single `int` (0–6) today — the one-axis model this slice revises.
- Item/mob content carries a single `TierBand` `int` on the component (`ItemDataComponent`/`MobDataComponent`, both `[Persistent]`), the template (`ItemTemplate`/`MobTemplate`), and YAML (`band:`); `setitem band`/`setmob band` set it (dual-write via `IItemBuilderSystem.SetItemBand`/`IMobBuilderSystem.SetMobBand`); both Blazor editors show a single-axis power/band readout with an exact-match mismatch flag.
- `Estimate` is a pure weighted sum over a caller-supplied snapshot plus the optional tier baseline; the `ProgressionSystem.GetEffectivePower` anti-grind proxy calls `Estimate` only (no `Classify`, no tier).
- `AscensionConstants` (`TierBaselineStep`/`TrackedScores`/`MaxTier` — the real gameplay tier power) is authoritative and **out of scope**; `PowerBudgetConstants` mirrors it as co-located constants under a "keep in sync" contract, and mirrors `CharacterDefaultsOptions` as `ReferenceBaseScores`.
- Content is thin and pre-release: discarding today's authored band tags is accepted (clean break, no migration).
- `power`/`powerband`/`setitem`/`setmob` are admin/designer-gated; the `Hedron.Web` host resolves `IPowerBudgetSystem` (registered in `Server/CompositionRoot`).
- The INV-2 architecture-guard test (`PowerBudgetSystem_has_no_domain_module_dependency`) is green and pins the oracle's four core files to zero `Core/Modules/<Feature>/` imports (bar `Stats`).

## Postconditions

> The coverage contract — every item asserting player-invisible internal state maps to a named test in **Test plan / Verification**.

- **Two-axis classify.** `Classify(power)` returns `PowerBand(int Tier, int Band)` with `Tier ∈ [0, MaxTier]` and `Band ∈ {1,2,3}`. Computed band is **never 0** — `0` is exclusively the *authored* "unbanded" sentinel.
- **Partition within tier, overlap only at tier boundaries.** Within a tier the three bands partition the tier's power span into thirds (low/mid/high, no intra-tier overlap); the shipped `BandSpan` overlap is retained **only** as the tier-boundary hysteresis (a maxed lower tier reaching into the next tier before ascending).
- **Inverse query.** `TargetRange(int tier, int band)` returns `PowerRange(int MinPower, int MaxPower)` = the cell's power sub-range; band 3's `MaxPower` abuts the next tier's band-1 `MinPower` (partition); an out-of-range `(tier, band)` is rejected fail-fast.
- **Strictly-increasing anchors.** The ~21 `(Tier, Band)` cell floors are strictly increasing across the whole table (the calibration invariant `BandSpan < tierSpan/3` holds).
- **`Estimate` algorithm & signature unchanged (output shifts with recalibration).** `Estimate(snapshot, tier)`'s algorithm and signature are untouched; its *numeric output* shifts with the recalibrated `Weights`. The anti-grind proxy's **ratio semantics** (floor/peer/cap) are preserved because the fixtures use proportional/identical stat profiles, so the three `ProgressionSystemTests` cases hold **unmodified**, and the DI-cycle guard regression still passes. (Not "byte-for-byte identical" — only the method body is.)
- **INV-2 preserved & guarded.** The oracle's core files import no `Core/Modules/<Feature>/` type other than `Stats`; `PowerBand`/`PowerRange`/`TargetRange` add no domain import. The guard test's hardcoded file array is **extended** to include the new `PowerBand.cs`/`PowerRange.cs`, so the new types are actually scanned (not merely asserted).
- **Field split (clean break — lossy, not a wipe).** `ItemDataComponent`/`MobDataComponent` and `ItemTemplate`/`MobTemplate` carry `Tier` (int, 0–6) + `Band` (int, 0–3) **replacing** `TierBand`; unbanded default (`Band = 0`) on read. Because the new `band:` key reuses the old key name, a legacy YAML `band:` value in `[1,3]` is silently *reinterpreted* as new-axis `(tier 0, band N)`, while `[4,6]` warns-and-untags; the legacy persisted `TierBand` JSON key (a distinct name) simply no longer maps → untagged. All acceptable under the accepted thin-content clean break, but reinterpretation is the honest description.
- **Authoring parity.** `setitem`/`setmob` expose `tier` (0–6) and `band` (0–3) property branches, each dual-writing template + live component and emitting exactly one existing admin-audit event (no new event; `tier` and `band` are **separate branches**, each composing its own value into the free-form `NewValue` — the event schema is unchanged); out-of-range is rejected with no mutation.
- **Round-trips.** YAML `tier:`+`band:` round-trip losslessly (warn-and-default out-of-range); a player-owned item's `ItemDataComponent.Tier`+`Band` survive a SQLite save→load round-trip.
- **Readouts.** `power <target>` displays computed `(Tier, Band)` + authored `(Tier, Band)`; `powerband [tier]` lists the ~21 cells (or one tier's three) with each cell's `TargetRange`.
- **Shared projection seam (INV-19).** One **item** projection (spanning both authored-`ItemTemplate` and live `ItemDataComponent` inputs, keyed on `StatBonuses`) feeds the `power` item path, `ItemEditor`, and the audit; one **mob** *template* projection feeds `MobEditor` and the audit. `power`'s self/mob paths keep their existing **live `IStatSystem` snapshot** (`LiveEntitySnapshot`) — a distinct, correct projection **not** folded into the template seam (folding it would wrongly reroute a live read through a template). No per-caller re-roll of the *template/component* projection.
- **Drift audit (soft).** `IBalanceAuditSystem.Audit()` returns a report of every item/mob whose global band-index drift (`|index(authored) − index(computed)|`, `index(T,B) = T·BandsPerTier + (B−1)`) exceeds `BandDriftTolerance` (authored `Band = 0` excluded — no assertion), plus counts bucketed by `(Tier, Band)`; recomputed on demand, never cached or persisted; **no build/reload/CI gate**.
- **Presentation.** Both editors show tier + band inputs, computed `(Tier, Band)`, and a band-count-tolerance mismatch flag; the Integrity page renders the audit report.
- **Discipline docs.** `docs/design/power-model.md` states the snapshot-only extensibility principle (distinct from `gameplay-model.md` §6's effect-`Power`); `add-domain-system`/`add-core-system`/`architecture-advisor` ask "does this affect power, and how does its contribution enter the snapshot?" (INV-20).

## Main flow

> The designer classify-and-audit journey this slice enables. No new runtime flow — the union of flow-03 (`power`/`powerband`), flow-08 (`setitem`/`setmob`), and the `Hedron.Web` content-tooling surface.

1. **Author the intended cell.** Designer sets a mob/item's intended tier and band via `setmob tier <bp> <0-6>` / `setmob band <bp> <0-3>` (or the Blazor editor). Each branch range-validates at the edge, calls the builder's dual-write (template + live component), and publishes the existing `MobPropertySetByAdminEvent`/`ItemPropertySetByAdminEvent` (the `NewValue` string carries the set value); `AdminAuditHandler` records it.
2. **Spot-check the classification.** Designer runs `power <target>` (self/item/mob) or opens the editor. The command/editor builds a `PowerSnapshot` via the **shared projection seam**, calls `Estimate` then `Classify`, and displays computed `(Tier, Band)` next to the authored `(Tier, Band)`.
3. **Design forward.** Designer runs `powerband [tier]` (or a consumer calls `TargetRange`). The oracle reflects the anchor table to show each `(Tier, Band)` cell's target `PowerRange`, so the designer knows the power window to build stats toward.
4. **Iterate to converge.** Designer adjusts stats in the editor; the live readout recomputes; the mismatch flag clears once computed band is within `BandDriftTolerance` of the authored band.
5. **Bulk-audit drift.** Designer opens the Blazor Integrity page and sweeps. `IBalanceAuditSystem.Audit` enumerates every item/mob template (`ITemplateRegistry.AllBlueprintIds` + `TryGet` + type filter), projects each via the shared seam, classifies via the oracle, and computes the band-index drift against each authored band.
6. **Read the report.** The page renders the audit: every content past `BandDriftTolerance` (authored vs computed cell) plus counts bucketed by `(Tier, Band)` — the free "how much content exists at power level X" view.
7. **Fix and re-sweep (advisory).** Designer re-authors band or adjusts stats and re-sweeps; the report is advisory only — nothing blocks build, reload, or CI (soft enforcement).

## Events fired

**None new.** The oracle, the inverse query, the projections, the audit, and the inspectors are pure read tools (INV-5). `setitem`/`setmob` reuse the existing `ItemPropertySetByAdminEvent`/`MobPropertySetByAdminEvent` — **payload shape unchanged**: `NewValue` is a free-form string, so the `tier`/`band` branches compose their value into it with no schema change (a refinement of the seed's "gain a second value in the payload" — no field is added). The audit is a scan, never a subscription or a materialized/cached classification (INV-24 spirit — derived-on-read).

## Systems / handlers involved

| Piece | Tier | Disposition |
|---|---|---|
| `IPowerBudgetSystem` / `PowerBudgetSystem` | Core | **Revise** — `Classify` → `PowerBand`; new `TargetRange`; `BandAnchor`/subdivision math; `Estimate` **algorithm unchanged** (output shifts with recalibrated `Weights`) |
| `PowerBudgetConstants` | Core (Category-3) | **Revise** — recalibrate non-mirror knobs (`Weights`, `BandSpan`, `BandsPerTier`, subdivision); mirror constants stay locked to their domain sources |
| `PowerBand` (`readonly record struct(int Tier, int Band)`), `PowerRange(int MinPower, int MaxPower)` | Core | **New** — result value types (mirror the `AscendEligibility` idiom) |
| Item power-projection · Mob power-projection | Domain (Items/Mobs) | **New** — the INV-19 shared `template/component → PowerSnapshot` seam |
| `IBalanceAuditSystem` / `BalanceAuditSystem` | Domain (BalanceInspection) | **New** — the shared band-drift audit method (Blazor + future headless + sim consume it) |
| `IItemBuilderSystem` / `IMobBuilderSystem` | Domain | **Revise** — `SetItemBand`/`SetMobBand` → `SetItemTier`+`SetItemBand` / `SetMobTier`+`SetMobBand` |
| `PowerCommand` / `PowerbandCommand` | Initiator | **Revise** — two-axis readout / ~21-cell listing |
| `SetitemCommand` / `SetMobCommand` | Initiator | **Revise** — `band` branch → `tier` + `band` branches |
| `PowerReadoutMessage` / `PowerBandRow`+`PowerbandMessage` / `TelnetOutputFormatter` | Output | **Revise** — carry `(Tier, Band)` + ranges |
| `ItemContentWriter`/`ItemTemplateDeserializer` · `MobContentWriter`/`MobTemplateDeserializer` | Domain | **Revise** — `band:` → `tier:`+`band:` YAML (clean break) |
| `Hedron.Web` `ItemEditor`/`MobEditor` · `Integrity` page | Presentation | **Revise** — tier+band inputs, two-axis readout, tolerance flag; audit report section |
| `AdminAuditHandler` | Handler | **Unchanged** — consumes the reused audit events |

No new handler, no heartbeat work, no `IEffectContributor`, no `IRandom`/`IClock` seam.

## Implementation plan — work packages

> Three independently-executable packages, sequenced by dependency. The primary agent runs `architecture-reviewer` (code mode) across the combined diff after all three land.

### WP-A — Core two-axis oracle + calibration + design principle

- **Scope:** `PowerBand`/`PowerRange` result types; `BandsPerTier` constant; `Classify(power) → PowerBand` (tier via the retained `BandAnchor` floors + `BandSpan` overlap, then within-tier third → band 1/2/3); `TargetRange(tier, band) → PowerRange`; recalibrate the **non-mirror** `PowerBudgetConstants` for real headroom + deliberate 3-band spacing (`Weights`, `BandSpan`, subdivision); `Estimate` untouched. Write `docs/design/power-model.md` (snapshot-only extensibility principle). Re-gold `PowerBudgetSystemTests`.
- **Files:** `Core/Systems/IPowerBudgetSystem.cs`, `PowerBudgetSystem.cs`, `PowerBudgetConstants.cs`, `PowerBand.cs`+`PowerRange.cs` (new); `docs/design/power-model.md` (new); `Hedron.Tests/Modules/BalanceInspection/PowerBudgetSystemTests.cs`; `Hedron.Tests/Architecture/ArchitectureGuardTests.cs` (**extend** the hardcoded oracle-file array with `PowerBand.cs`/`PowerRange.cs` — else the new types are not scanned).
- **Depends on:** nothing.
- **Out of scope:** any `Core/Modules/` change; `AscensionConstants`/`CharacterDefaultsOptions` (and their mirrors) — untouched; the field split; the audit; the INV-20 skill/advisor edits (WP-C).
- **Exit criterion:** `Classify` returns a `(Tier, Band)` cell and `TargetRange` its inverse; the ~21-anchor golden tests pass under the locked recalibrated numbers; the INV-2 guard test (**array extended** to `PowerBand`/`PowerRange`) still passes; `Estimate`'s algorithm is unchanged (its own golden numbers **re-gold** with the recalibrated `Weights`); the anti-grind ratio cases hold.

### WP-B — Field split + authoring parity + shared projection seam

- **Scope (atomic — the split ripples through the whole chain):** `TierBand` → `Tier`+`Band` on `ItemDataComponent`/`MobDataComponent` and `ItemTemplate`/`MobTemplate` (+ `Apply`, `Clone`); builders `SetItemTier`/`SetItemBand`, `SetMobTier`/`SetMobBand`; `setitem`/`setmob` `tier`+`band` branches (compose both into the reused audit event's `NewValue`); writers/deserializers `tier:`+`band:` (clean break — legacy `band:` in `[1,3]` reinterpreted as `(tier 0, band N)`, `[4,6]` untagged); the **shared item/mob power-projection seam** (extract the inline *template/component* snapshot builds into one domain method each — the **item** projection spans `ItemTemplate` + live `ItemDataComponent` (both expose `StatBonuses`) and serves `PowerCommand`'s item path + `ItemEditor` + the audit; the **mob** projection is template-sourced and serves `MobEditor` + the audit; `PowerCommand`'s self/mob **live** `IStatSystem` snapshot stays as-is, not folded in — the INV-19 fix); re-gold `PowerCommand`/`PowerbandCommand` output + messages for two-axis; Blazor editors' tier+band inputs, two-axis readout, tolerance flag. Re-verify command/round-trip/persistence tests.
- **Files:** `Core/ECS/Components/ItemDataComponent.cs`, `MobDataComponent.cs`; `Core/Modules/Items/**` (`ItemTemplate`, `IItemBuilderSystem`/`ItemBuilderSystem`, `SetitemCommand`, `ItemContentWriter`, `ItemTemplateDeserializer`, projection); `Core/Modules/Mobs/**` (mirror); `Core/Modules/BalanceInspection/Commands/*`; `Core/Output/PowerReadoutMessage.cs`, `PowerbandMessage.cs`, `TelnetOutputFormatter.cs`; `Hedron.Web/Components/Pages/ItemEditor.razor`, `MobEditor.razor`; the mirrored test files.
- **Depends on:** WP-A (`PowerBand`/`TargetRange` shape + recalibrated numbers for the re-golded readouts).
- **Out of scope:** the audit system + Integrity report + INV-20 tooling (WP-C); `AscensionConstants`.
- **Exit criterion:** author tier+band via command / YAML / editor; `power`/`powerband` show two-axis; the extracted projection is the single *template/component* snapshot source for its consumers (item: `power` item path + `ItemEditor` + audit; mob: `MobEditor` + audit), with `power`'s live self/mob snapshot left as-is; YAML + SQLite round-trips green; legacy `band:` in `[1,3]` reinterprets to `(tier 0, band N)` (else untags), legacy persisted `TierBand` untags.

### WP-C — Band-drift audit (shared method + Blazor report) + INV-20 tooling

- **Scope:** `IBalanceAuditSystem.Audit() → BalanceAuditReport` (enumerate item/mob templates, project via the WP-B seam, classify via the oracle, compute global band-index drift vs authored band, bucket by `(Tier, Band)`); `BandDriftTolerance` constant; register in `BalanceInspectionModule`; extend the Blazor `Integrity` page with the audit report (past-tolerance list + `(Tier, Band)` bucket counts); update `add-domain-system`/`add-core-system` skills + `architecture-advisor` to ask the power-contribution question.
- **Files:** `Core/Modules/BalanceInspection/Systems/IBalanceAuditSystem.cs`+`BalanceAuditSystem.cs` (new), `BalanceAuditReport.cs` (new), `BalanceInspectionModule.cs`; `Hedron.Web/Components/Pages/Integrity.razor`; `.claude/skills/add-domain-system/`, `.claude/skills/add-core-system/`, `.claude/skills/architecture-advisor/`; `Hedron.Tests/Modules/BalanceInspection/BalanceAuditSystemTests.cs` (new).
- **Depends on:** WP-A + WP-B.
- **Out of scope:** the headless/admin audit **command** (deferred — see Open question 3); the balance catalog + `balance-tuning`/`run-simulation` skills (stay prog-5); promoting `PowerBudgetConstants` to YAML (OD-2, prog-4).
- **Exit criterion:** `Audit()` returns a report keyed by drift + bucketed by cell; the Integrity page renders it; the three tooling docs ask the power question; DI-smoke resolves `IBalanceAuditSystem` **in the `Hedron.Web` host container** — which must register `ITemplateRegistry` + the WP-B projection systems (or the projections are static/stateless needing no registration), else the Integrity report throws at runtime.

## Content tooling impact

> INV-18 — the field split ships full authoring parity across **both** content kinds, in this PR.

- **Data shape (clean break — lossy).** YAML `band: <int>` → `tier: <0-6>` + `band: <0-3>` on both `item` and `mob` definitions. Legacy `band:`-only files are **not cleanly wiped** (the new `band:` reuses the old key name): a legacy `band:` in `[1,3]` is reinterpreted as new `(tier 0, band N)`, `[4,6]` warns-and-untags — acceptable under the thin-content clean break. Writers emit `tier:`/`band:` only when non-zero (mirrors the existing omit-when-default convention).
- **Admin commands.** `setitem tier|band <value>` and `setmob tier|band <value>` (replacing the single `band` branch on each), range-validated at the edge (tier 0–6, band 0–3), dual-writing template + live component, each emitting one reused admin-audit event. Usage/`LongDescription` strings updated.
- **Blazor editors.** `ItemEditor`/`MobEditor` gain separate **Tier** (0–6) and **Band** (0–3) inputs and a two-axis computed readout (`(Tier, Band)` + `TargetRange`), with the mismatch flag upgraded from exact-match to **band-count tolerance**.
- **Inspect/report.** `power`/`powerband` render two-axis; the Blazor **Integrity** page gains a bulk band-drift audit report (past-tolerance list + `(Tier, Band)` bucket counts) — the free "how much content exists at power level X" surface. The headless/admin bulk-audit **command** is deferred (Open question 3); the shared `Audit()` method ships so it is a thin later caller.
- **No `TemplateRegistry` archetype change** — the split is field-level on existing item/mob templates.

## Cross-cutting surfaces stressed

> INV-19 ground-rule-9 audit. Each surface classified **Adequate** / **Gap exposed** / **Acknowledged debt**.

- **Commands — Adequate.** `power`/`powerband` re-gold and the `tier`/`band` property branches extend the existing `ICommand` + property-switch pattern; no framework gap.
- **Output — Adequate.** `PowerReadoutMessage`/`PowerBandRow`/`PowerbandMessage` + `TelnetOutputFormatter` extend the typed-message pattern to carry `(Tier, Band)` + ranges.
- **Content templates & projection — GAP EXPOSED → land the seam in WP-B.** "Project an authored item/mob (template or live component) to a `PowerSnapshot`" is hand-rolled inline at **three** template/component sites (`PowerCommand.ItemSnapshot` on `ItemDataComponent`, `ItemEditor.ComputedPower` on `ItemTemplate`, `MobEditor.ComputedPower` on `MobTemplate`); the audit adds more. The **item** projection alone reaches three consumers (command item path + editor + audit) → crosses the INV-19 threshold, and the field split touches every site anyway. **Disposition:** extract one shared **item** projection (spanning template + live `ItemDataComponent`, both keyed on `StatBonuses`) and one shared **mob** *template* projection (domain-tier, in the Items/Mobs modules — keeps the oracle core-tier, INV-2), consumed by `power`'s item path, both editors, and the audit. **Not folded in:** `PowerCommand`'s self/mob paths use a live `IStatSystem` snapshot (`LiveEntitySnapshot`), a distinct correct projection that stays put. "Framework lands with the slice" (WP-B), not absorbed silently — see Open question 4.
- **Persistence — Adequate (with a called-out clean break).** The only `[Persistent]` shape change is `ItemDataComponent.Tier`+`Band` replacing `TierBand`; existing player-owned-item snapshots with a `TierBand` key no longer map and default to untagged (accepted, thin pre-release content — Design notes "Clean-break field split"). `MobDataComponent` is `[Persistent]` but mobs never carry `PersistentEntity`, so its fields never persist. No `SaveEntityAsync` anywhere in this slice (world-content authoring is YAML-only; the audit is read-only) — INV-22 clean.
- **Event bus — Adequate.** No new event; the reused admin-audit events need no shape change (free-form `NewValue` string).
- **ECS queries — Adequate.** The audit enumerates via `ITemplateRegistry.AllBlueprintIds()` + `TryGet` + type filter (existing accessor); live reads via `EntityService`/`IStatSystem` unchanged.
- **Configuration — Adequate (with a sharpened calibration boundary).** `PowerBudgetConstants` recalibration is Category-3. **Calibration may only move the non-mirror knobs** (`Weights`, `BandSpan`, `BandsPerTier`, subdivision); the mirror constants (`ReferenceBaseScores` ↔ `CharacterDefaultsOptions`; `TierBaselineStep`/`TrackedScores`/`MaxTier` ↔ `AscensionConstants`) stay locked to their domain sources, or their "keep in sync" contract breaks and (for the Ascension mirrors) gameplay power is touched — explicitly deferred to prog-4. See Open question 2. YAML promotion (OD-2) stays deferred.
- **Modules — Adequate.** `BalanceInspection`'s new audit system references `ItemTemplate`/`MobTemplate`; `PowerCommand` already references Items + Combat, so the `MobTemplate` use **adds a `BalanceInspection`→`Mobs` domain→domain reference** alongside them — permitted (INV-2 governs core→domain, not domain→domain; no tier violation), just newly present.
- **Time / Broadcast / Sessions — Not exercised.** Pure math, no wall-clock, no chance → no `IRandom`/`IClock` seam (INV-26 re-asserted by the golden test); no broadcast; no session state.
- **Testability — no gap.** Every new decision (classify, inverse, projection, audit) is a pure function of injected/plain inputs; nothing needs an un-injected randomness/clock/I-O seam.

## Flows introduced or modified

**None.** `power`/`powerband` plug into [flow-03 command journey](../architecture/flows/flow-03-player-command-lifecycle.md); `setitem`/`setmob` into [flow-08 admin authoring journey](../architecture/flows/flow-08-admin-room-creation.md); the Integrity audit report is a synchronous Blazor read on the existing content-tooling surface ([flow-29](../architecture/flows/flow-29-bulk-content-generation.md)-adjacent). No diagram drifts: `Estimate` is unchanged so [flow-31 progression](../architecture/flows/flow-31-progression-award.md)'s anti-grind leg is untouched, and no flow file references single- vs two-axis band internals. **Confirmed: no `flows/README.md` index row is added or changed.** (Reference catalogs `components.md`/`systems.md`/`commands.md` do update for the split fields + new query/system per INV-16 — a catalog diff, not a flow.)

## Test plan / Verification

> INV-25, per the rubric in [`../architecture/07-testing.md`](../architecture/07-testing.md). Every Postcondition asserting invisible state maps to a named test.

**System-unit (Tier 1)**
- `PowerBudgetSystemTests` **re-gold**: `Classify(power) → (Tier, Band)` across the ~21 cells; within-tier third boundaries (band 1/2/3); tier-boundary `BandSpan` overlap retained; below-floor → `(0, 1)`, computed band never 0; strictly-increasing cell floors (`BandSpan < tierSpan/3`).
- `TargetRange(tier, band)` new tests: cell `(min, max)` correctness; band-3 max abuts next-tier band-1 min; out-of-range `(tier, band)` fail-fast.
- Weight-table sanity retained (combat scores dominate); `Estimate`'s weighted-sum + tier-baseline **algorithm** is unchanged, but its golden numbers **re-gold** under the recalibrated `Weights` (method body untouched; only the constants move).
- Item/mob **projection seam**: template/component → snapshot correctness (item `StatBonuses`; mob attrs + `Body/2`, `Body/4` derivations); a regression pin that the extracted seam equals the pre-extraction inline result.
- `BalanceAuditSystemTests` (new): band-index drift delta; within-tolerance excluded, past-tolerance included; authored `Band = 0` excluded (no assertion); `(Tier, Band)` bucket counts; empty registry → empty report.

**Handler/command (Tier 2)**
- `PowerCommandTests` **re-gold**: readout carries computed `(Tier, Band)` + authored `(Tier, Band)`; self (no authored band) / item / mob; golden numbers under the recalibrated constants.
- `PowerbandCommandTests` **re-gold**: ~21-cell listing vs single-tier (three cells); invalid-tier rejection; admin-gate declaration.
- `SetitemCommandBandTests` / `SetMobCommandBandTests` **re-gold**: `tier` and `band` branches each dual-write + emit one audit event; range validation (tier 0–6, band 0–3); no mutation on invalid input.

**Persistence round-trip (Tier 4)**
- `ItemTierBandRoundTripTests` **re-gold**: `tier:`+`band:` write→YAML→read (warn-and-default out-of-range); **clean-break** cases — a legacy `band: 2`-only definition is *reinterpreted* as `(tier 0, band 2)` (pins the lossy break — not a silent untag), and a legacy `band: 5`-only definition warns-and-untags (`Band = 0`); SQLite round-trip of a player-owned item's `ItemDataComponent.Tier`+`Band`.
- `MobTierBandRoundTripTests` **re-gold**: `tier:`+`band:` YAML round-trip + `Apply` seeding.

**Anti-grind equivalence (on-touch ratchet)**
- The three `ProgressionSystemTests` cases (floor/peer/cap) **re-verified unmodified** — the anti-grind *ratio* is invariant to weight rescaling (fixtures use proportional/identical profiles), so recalibrating `Weights` does not move them; the worn-gear DI-cycle-guard regression still passes. (Any stale `// power N` fixture comments are updated, not asserted.)

**Architecture-guard (Tier 5)**
- `ArchitectureGuardTests.PowerBudgetSystem_has_no_domain_module_dependency` **extended**: its hardcoded scan array grows from four files to include `PowerBand.cs`/`PowerRange.cs`, then asserts all import no `Core/Modules/<Feature>/` type but `Stats` (without the array edit the new types go unscanned); DI-smoke resolves the new `IBalanceAuditSystem` **in the `Hedron.Web` host container** (see WP-C exit).

**Skipped (with reason)**
- Exact telnet prose (`TelnetOutputFormatter` strings) and Blazor markup — presentation; the values they render are asserted at the message/system tier.
- `PowerBand`/`PowerRange`/`BalanceAuditReport` as pure-data records — exercised transitively by the system tests that produce them.
- Thin command plumbing beyond the golden-number/validation assertions above.

---

## Design notes

> Durable seam rationale — the non-obvious "why" a cold-start session must not re-derive or reverse. Survives disintegration into `docs/features/progression/` + `docs/design/` on ship (INV-28).

### Tier is *mechanical*; Band is *descriptive* — this asymmetry is the whole revision

The load-bearing distinction, and the thing slice 3 conflated: **Tier grants power; Band only describes it.** Tier (0–6) is the Ascension scalar that confers the additive baseline through `AscensionEffectContributor` (INV-24) — a real mechanical input to `IStatSystem.Get`. **Band (1–3) is a pure classification device** — a Challenge-Rating label answering "where in this tier does this build sit?" It grants no power, feeds no contributor, and gates nothing mechanically. That asymmetry is *why* the resolutions below fall out cleanly: Band can be a soft, auditable content tag (it has no gameplay consequence to protect), and the two-axis change is confined to the oracle's **output**, never its input.

### The revision is output-only — `Estimate` is untouched, so the oracle stays core-tier and snapshot-only (INV-2)

`Estimate(snapshot, tier)` — the input side — does **not** change: power is still a weighted sum over a caller-supplied `PowerSnapshot` plus the optional tier baseline. Only the **classification/inverse** surface moves:
- `Classify(power)` returns a `(Tier, Band)` pair instead of one `int`.
- a new `TargetRange(tier, band)` inverts a target cell to a `(minPower, maxPower)` range.
- the band anchors subdivide each tier gap into 3 (≈21 anchors instead of 7).

Because no new *input* enters the oracle, INV-2 is preserved by construction — the oracle's core files (plus the new `PowerBand.cs`/`PowerRange.cs`) import no `Core/Modules/<Feature>/` domain type (the architecture-guard test `PowerBudgetSystem_has_no_domain_module_dependency` continues to hold once its file array is extended to the two new types). A pleasant consequence: the **anti-grind proxy** (`ProgressionSystem.GetEffectivePower`) calls `Estimate` only, so its **code** needs **zero** changes — the two-axis work is invisible to it, and its computed anti-grind *ratio* is invariant to the `Weights` recalibration (proportional/identical fixtures), so its three equivalence tests hold unmodified even though `Estimate`'s raw output shifts.

### The inverse query returns a *power range*, not a reverse-engineered stat block

"Get a stat range to design toward" (OQ7's phrasing) is satisfied by returning the **power** bounds of a `(Tier, Band)` cell; the designer converges an actual stat block by iterating in the editor, whose readout already shows computed power/band live. The oracle never inverts the weighted sum back to a unique score vector (it isn't invertible, and a *stat-synthesis* generator is procedural-generation scope — Spine D, already on the feature horizon — shaped-for-later, not built here). Build the power-range inverse now (it is the near-free reflection of the anchor table and has ≥2 today-consumers: forward authoring + the audit report); leave stat synthesis to procgen.

### Calibration is **oracle estimation only** — gameplay power is not touched (resolved fork, 2026-07-07)

Recalibrate `PowerBudgetConstants` (`Weights`, `BandSpan`, the reference anchors, the 3-band subdivision, and numeric headroom) so classification reads sensibly and bands are well-separated. **Leave `AscensionConstants.TierBaselineStep` / `TrackedScores` / `MaxTier` — the real additive power an Ascension grants in-game — untouched**; `PowerBudgetConstants`' mirror of them stays "keep in sync." Real *gameplay-power* tuning is deferred to **prog-4**, where `Hedron.Sim` can validate numbers against actual combat outcomes — hand-guessing gameplay balance now is the exact "guessed constants" trap OQ10 warns against, and prog-4 would immediately re-tune it. Note the diagnosed symptom: a near-blank character reading Tier 4 is almost certainly its admin-set `AscensionComponent.Tier` feeding the self-snapshot via the contributor (Ascension tier is admin-set today, independent of progression XP), **not** a formula bug — verify with `score` before calibrating, then calibrate for *resolution/headroom*, not to "fix" that read.

### Clean-break field split — no migration (resolved fork, 2026-07-07)

`TierBand` (single `int`) → a `Tier`+`Band` pair is a **breaking** change with **no migration path**: content is thin and pre-release, so old values are not carried forward and content is re-authored against Tier×Band. The break is **lossy, not a clean wipe**: because the new `band:` key reuses the old key name, a legacy YAML `band:` value in `[1,3]` is silently *reinterpreted* as new-axis `(tier 0, band N)` (and `[4,6]` warns-and-untags), rather than cleanly ignored; the distinct persisted `TierBand` JSON key no longer maps → unbanded. This keeps the deserializer/persistence spec simple; the trade is discarding/reinterpreting today's band tags, which the owner has accepted at this stage.

### The drift-audit *computation* is a shared seam, not Blazor-buried logic

OQ9 keeps the authored band persisted (a persisted tag makes "how much content at power level X" and "what drifted after a retune" a cheap grouped scan) and enforces it **softly** in two layers, **neither a build gate**: (a) the existing per-item/mob editor mismatch flag, upgraded from exact-match to **band-count tolerance**; (b) a **bulk audit report** extending the existing Integrity sweep page that lists all content past tolerance and buckets counts by `(Tier, Band)`. The band-count metric (OQ8) — "more than N bands off" — is chosen over a raw-power-percentage so it stays self-consistent with the Tier×Band vocabulary and needs no separate tunable. **Seam decision:** the tolerance check + bucketed counts land as a **callable audit method** (core/domain, wherever the snapshot-gathering already lives), so the Blazor page, a possible headless/admin audit command, and the prog-4 sim all consume **one** implementation — the same INV-19 "one function, many callers" discipline the oracle itself follows. A CI hard-fail gate was considered and rejected: within-band variance is expected, not a bug.

### The extensibility principle gets its own doc home — distinct from effect-`Power`

OQ11's rule — *the oracle never gains a domain import to learn about abilities/effects/speed/defense; future power sources fold in either as (a) a new stat-like `ScoreId` callers weight, or (b) a richer source that computes its own estimated contribution and a caller sums it into the snapshot before calling `Estimate`, mirroring the `IEffectContributor` precedent* — must be **written down as a named principle**, not left as prose in one feature file, so a later slice can't silently violate it (as slice-3's code review caught once with `AscensionConstants`). **Home:** a new short `docs/design/power-model.md`. This is deliberately *not* folded into `gameplay-model.md` §6, which already defines a **different** `Power` — the effect system's *potency / stack-rank* (R5). The two share the word and nothing else; co-locating them would invite exactly the conflation INV-27/INV-30 (one fact, one home) exist to prevent. `power-budget-system.md` links to the new note rather than restating it.

---

## Architecture brief

> In-flight forward analysis; trimmed on ship. Feeds the planner's Design notes + ground-rule-9 audit.

### Placement & layers

| Piece | Layer | Home |
|---|---|---|
| `Classify` → `(Tier, Band)` output; `BandAnchor` subdivision (≈21 anchors) | **Core** | `Core/Systems/PowerBudgetSystem.cs` — output-only change, no new input (INV-2 preserved) |
| `TargetRange(tier, band) → (min, max)` inverse query | **Core** | `IPowerBudgetSystem` — new method, near-free reflection of the anchor table |
| `PowerBand` result type (`readonly record struct(int Tier, int Band)`) + `BandsPerTier` constant | **Core** | `Core/Systems/` — mirrors the `AscendEligibility` result-record idiom |
| Recalibrated `Weights` / `BandSpan` / reference anchors / headroom | **Core** (Category-3 constants) | `Core/Systems/PowerBudgetConstants.cs` — **oracle only**; `TierBaselineStep`/`TrackedScores`/`MaxTier` mirror of `AscensionConstants` unchanged |
| `TierBand` → `Tier`+`Band` pair | Component + template (data) | `MobDataComponent` / `MobTemplate` · `ItemDataComponent` / `ItemTemplate` — clean break, unbanded default |
| `SetMobBand` / `SetItemBand` → dual-value | Domain (builder) | `IMobBuilderSystem` / `IItemBuilderSystem` (+ writers/deserializers; `setmob`/`setitem band` command branches) — INV-18 authoring parity |
| Band-count drift audit (tolerance check + `(Tier,Band)` bucket counts) | **shared callable method** (Initiator/UI consume) | one home the Blazor Integrity page, a possible headless command, and the prog-4 sim all call (INV-19) |
| `power` / `powerband` readout of `(Tier, Band)` + inverse range | Initiator (command) + output message | `Core/Modules/BalanceInspection/` (`PowerReadoutMessage` / `PowerbandMessage` / `TelnetOutputFormatter`) |
| Both editors' readout + Integrity-sweep audit report | Presentation | `Hedron.Web` `ItemEditor` / `MobEditor` + the Integrity page |
| Named extensibility principle | Design doc | **new** `docs/design/power-model.md` |
| INV-20 tooling: "does this affect power, and how does its contribution enter the snapshot?" | Agent tooling | `add-domain-system` / `add-core-system` skills + `architecture-advisor` (this skill) / reviewer — the OQ12 pull-forward |

### Family disposition

| Concern | Disposition |
|---|---|
| Two-axis `(Tier, Band)` classify + subdivided anchors | **Build now** — the core of the revision |
| `TargetRange` power-range inverse query | **Build now** — ≥3 consumers (forward authoring, audit report today; sim + procgen later → INV-19) |
| `Tier`+`Band` field split across both content kinds + all authoring surfaces | **Build now** — INV-18 content-tooling parity |
| Band-count drift audit as a shared callable seam + Blazor Integrity report | **Build now** — the OQ9 soft-enforcement + free reporting |
| Oracle recalibration (headroom + deliberate band spacing) | **Build now** — hand-derived, real-headroom, deliberately-spaced, documented still-heuristic |
| Named snapshot-only extensibility principle (`docs/design/power-model.md`) + INV-20 refresh | **Build now** — the OQ11/OQ12 pull-forward |
| Headless/admin bulk-audit **command** (vs. Blazor-only now) | **Shape for later** — build the audit *method* now so the command is a thin later caller; whether the verb ships in 3b is planner scope |
| Stat-block synthesis from a target range (reverse the weighted sum) | **Defer** — procedural-generation scope (Spine D); the power-range inverse shapes it, already on the feature horizon (no new backlog line needed) |
| Real *gameplay-power* tuning (`AscensionConstants`) | **Defer to prog-4** — sim-validated, not hand-guessed |
| `PowerBudgetConstants` → tunable YAML (OD-2) | **Defer** — already tracked; sim (prog-4) is the promotion trigger |

### Observers, contributors & event granularity

- **No new events.** Like slice 3, the oracle and inspectors are pure read tools (INV-5); the `tier`/`band` branches reuse the existing `MobPropertySetByAdminEvent` / `ItemPropertySetByAdminEvent` audit events with **no payload-shape change** — `NewValue` is a free-form string each branch composes its own value into (a refinement of the seed's earlier "second value in the payload": no field is added).
- **No new contributor.** The oracle is *not* an `IEffectContributor` and does not become one (it reads snapshots, it does not fold power into `IStatSystem`) — the OQ11 principle makes that permanent.
- **The audit is a scan, not a subscription** — it recomputes on demand (Integrity sweep / command / sim), never a materialized/cached classification (INV-24 spirit: classification is derived-on-read).

### Ordering & timing

None. The revision adds no heartbeat work, no new handler on a shared event, and no chance/time-dependent logic — power/classification/inversion remain pure functions of inputs, so no `IRandom`/`IClock` seam is introduced (INV-26 unchanged; re-asserted by the re-goldened math tests).

### Invariants in tension

- **[INV-2](../architecture/checklist.md)** — the two-axis change is **output-only**; the oracle stays core-tier with no domain import. *Extend the guard test's hardcoded file array to include `PowerBand.cs`/`PowerRange.cs`, then re-validate all oracle files import nothing new* (else the new types go unscanned).
- **[INV-19](../architecture/checklist.md)** — the inverse query and the audit method each land as **one** shared function with ≥3 consumers, not hand-rolled per caller.
- **[INV-14](../architecture/checklist.md) / [INV-23](../architecture/checklist.md)** — the `Tier`+`Band` split changes a `[Persistent]` shape on player-owned items (`ItemDataComponent`) and the YAML shape on world content; the **clean-break** disposition means unbanded-default on read, no migration.
- **[INV-16](../architecture/checklist.md) / [INV-17](../architecture/checklist.md)** — reference catalogs (`components.md`, `systems.md`, `commands.md`) update for the split fields + new query; `power`/`powerband` plug into flow-03 with no new flow (read tools), planner to confirm no flow diagram drifts.
- **[INV-18](../architecture/checklist.md)** — the field split ships full authoring parity across both content kinds (builder + writer + command + editor).
- **[INV-20](../architecture/checklist.md)** — the OQ12 pull-forward: teach `add-domain-system`/`add-core-system`/the advisor to ask the power-contribution question; land `docs/design/power-model.md`. (The balance catalog + `balance-tuning`/`run-simulation` skills stay at prog-5 — `run-simulation` has no meaning before `Hedron.Sim`.)
- **[INV-25](../architecture/checklist.md)** — re-gold `PowerBudgetSystemTests` for the two-axis anchors (and `Estimate`'s own golden numbers, which move with the recalibrated `Weights`); add inverse-query + band-tolerance-audit coverage; re-verify `PowerCommandTests`/`PowerbandCommandTests`/`Set{Item,Mob}CommandBandTests`/round-trips against the split fields; the anti-grind equivalence cases hold unchanged (ratio invariant to weight rescaling).
- **[INV-26](../architecture/checklist.md)** — pure math, no seam; re-asserted by the golden test.
- **[INV-27](../architecture/checklist.md) / [INV-30](../architecture/checklist.md)** — the extensibility principle lives in exactly one new home, explicitly distinct from `gameplay-model.md` §6's effect-`Power`.
- **[INV-28](../architecture/checklist.md)** — on ship, this plan disintegrates: durable design → `power-budget-system.md` (+ the new `power-model.md`); as-built → `roadmap/completed/power-model-revision.md`; catalog diffs → `reference/`.

### Resolved decisions

| # | Decision | Source |
|---|---|---|
| Tier vs Band | **Tier (0–6) = mechanical Ascension scalar; Band (1–3) = purely descriptive CR-style subdivision.** Band grants no power, gates nothing. | OQ7 |
| Classify output | `Classify` returns `(Tier, Band)`; anchors subdivide each tier gap into 3 (≈21). | OQ7 |
| Inverse query | `(Tier, Band) → target **power** range` — build now; stat-block synthesis deferred to procgen. | OQ7 |
| Tolerance metric | **Band-count** ("more than N bands off"), not raw-power-percentage. | OQ8 |
| Authored band | **Keep persisted** (Option A); enforce **softly** — upgraded editor flag + bulk Integrity report; **no CI gate**. | OQ9 |
| Audit seam | Tolerance check + `(Tier,Band)` bucket counts as **one callable method**; Blazor + (planner-scoped) headless + sim consume it. | OQ9 |
| Calibration scope | **Oracle estimation only** — recalibrate `PowerBudgetConstants`; `AscensionConstants` gameplay power untouched; sim (prog-4) owns gameplay tuning. | OQ10 + fork (2026-07-07) |
| Field migration | **Clean break** — no migration; old `TierBand` ignored → unbanded default; content re-authored. | fork (2026-07-07) |
| Extensibility | Oracle stays **snapshot-only**; future power = new weighted `ScoreId` **or** caller-summed contribution; **never** a domain import. Written down as a **named principle** in a new `docs/design/power-model.md`. | OQ11 |
| Scope pull-forward | Only the INV-20 piece OQ11 depends on moves into 3b; balance catalog + sim skills stay at prog-5. | OQ12 |

---

## Open questions

> Load-bearing for the planner / spec gate; none blocks framing. Planner recommendations added (2026-07-07); the spec-review gate confirms them.

1. **Intra-tier band overlap model** — slice-3's `BandSpan` overlap is a *tier-boundary* concept (Ascension semantics: a maxed lower tier reaches into the next tier before ascending). Within a tier, the three bands most naturally **partition** (low/mid/high) rather than overlap.
   → **Planner recommendation: partition within a tier; retain `BandSpan` overlap only at tier boundaries.** Band is purely descriptive (grants no power), so intra-tier overlap would only make `Classify` ambiguous with no semantic payoff, whereas the tier-boundary overlap models a real Ascension mechanic. `Classify` computes tier via the retained `BandAnchor` floors (with `BandSpan` overlap, as shipped), then buckets the within-tier position into band 1/2/3 by thirds; `TargetRange` bounds abut cleanly (band-3 max = next-tier band-1 min). Locked into WP-A's Postconditions.
2. **Calibration target numbers** — the *disposition* is set (oracle-only; hand-derived, real-headroom, deliberately-spaced; documented still-heuristic; sim-validated in prog-4). The concrete `Weights`/anchor/`BandSpan` values are a tuning task **inside** this slice, cross-checked against a few real authored builds via the editor readout.
   → **Planner recommendation: calibrate only the non-mirror knobs; lock numbers before the golden tests.** Move `Weights`, `BandSpan`, and the subdivision math for headroom + spacing; keep the mirror constants (`ReferenceBaseScores` ↔ `CharacterDefaultsOptions`; `TierBaselineStep`/`TrackedScores`/`MaxTier` ↔ `AscensionConstants`) **locked** to their domain sources (moving them silently breaks the "keep in sync" contract and touches gameplay power — prog-4's job). Method: recompute `Estimate(ReferenceBaseScores, tier)` for tier 0–6 to read the current per-tier span, then widen `Weights`/`BandSpan` until each tier span comfortably exceeds `3 × BandSpan` (strict-ordering invariant) and the tier-6 headroom is deliberately large (placeholder target: low thousands, not <1000); cross-check 3–4 real builds via the editor readout; document still-heuristic. **If real headroom proves unreachable within the non-mirror knobs, that surfaces a gameplay-power retune — a prog-4 signal, not a 3b blocker.** Not a user fork.
3. **Headless/admin bulk-audit command in 3b?** — the audit *method* lands regardless; whether the in-game/headless verb ships now or waits for the sim to be its first non-Blazor caller is a planner scoping call.
   → **Planner recommendation: ship the shared `Audit()` method + the Blazor Integrity report now; defer the in-game/headless command.** The Blazor report is the primary designer surface and the slice's functional-validation hook; a telnet audit verb adds a command + output message + tests for marginal value while the sim (prog-4) is its natural first non-Blazor caller. Because `Audit()` is one callable method, adding the verb later is a thin caller (INV-19 satisfied), no rework. Matches the seed's "shape for later."
4. **Shared projection seam scope (new — surfaced by the ground-rule-9 audit).** The `template/component → PowerSnapshot` projection is hand-rolled at three sites today (`PowerCommand`, `ItemEditor`, `MobEditor`) and the audit adds more, crossing the INV-19 threshold.
   → **Planner recommendation: build the shared item/mob projection seam now, in WP-B** (domain-tier, keeps the oracle core-tier). The field split touches every projection site anyway, so consolidating is cheap and prevents three-way drift on the new two-field shape. Surfaced as a **Gap exposed** in Cross-cutting surfaces; default disposition "framework lands with the slice," not absorbed silently. The spec gate should confirm this is in scope.
5. **Band "unbanded" sentinel (planner-resolved, noted for the spec gate).** `Band` is `int` with `0` = unbanded/untagged and `1`/`2`/`3` = low/mid/high (consistent with the codebase's existing `0 = unbanded` convention); `Tier` is `int` `0–6` where `0` is both the base tier and the untagged default. Computed `Classify` returns `Band ∈ {1,2,3}` only; the audit/mismatch flag skips content with authored `Band = 0` (no designer assertion to check). **Intended consequence (spec-gate-noted):** such content gets **no drift assertion**, yet still appears in the *computed* `(Tier, Band)` bucket counts — so the "how much content exists at power level X" view stays complete even for untagged content.

---

## Related

- [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) — the shipped one-axis oracle this slice revises (carries the "Revision pending" banner); links to the new `docs/design/power-model.md` on ship.
- [`../features/progression/ascension-system.md`](../features/progression/ascension-system.md) — the Tier scalar + `AscensionConstants` gameplay power this oracle mirrors and must **not** touch; the mob band tag this split extends.
- [`../features/progression/progression.md`](../features/progression/progression.md) · [progression-system.md](../features/progression/progression-system.md) — the anti-grind proxy (`GetEffectivePower` → `Estimate`), untouched by the two-axis change.
- [`progression-and-balance.md`](progression-and-balance.md) — program brief; slice table (3b → prog-4/5) and Open questions 7–12 (resolved), the frame for this plan.
- [`../roadmap/completed/power-budget-inspector.md`](../roadmap/completed/power-budget-inspector.md) — as-built slice-3 history, incl. the INV-2 deviation the code gate caught and the prog-3b follow-up.
- [`../design/gameplay-model.md`](../design/gameplay-model.md) §6 — the effect-`Power` (potency/stack-rank) the new `docs/design/power-model.md` is explicitly kept distinct from (INV-27/INV-30, one fact one home).
- Flows: [flow-03](../architecture/flows/flow-03-player-command-lifecycle.md) (`power`/`powerband`), [flow-08](../architecture/flows/flow-08-admin-room-creation.md) (`setitem`/`setmob`) — plugged into, not modified.
- Reference catalog diffs (INV-16): [`../reference/systems.md`](../reference/systems.md), [`../reference/commands.md`](../reference/commands.md), [`../reference/components.md`](../reference/components.md).
- [`../architecture/checklist.md`](../architecture/checklist.md) — INV-2, INV-14/23, INV-16/17, INV-18, INV-19, INV-20, INV-24, INV-25/26, INV-27/28/30.

---

**Next:** this seed is now extended into the full plan (Preconditions → Test plan, ground-rule-9 audit run, three Open questions answered with planner recommendations, one new Gap surfaced). Run the **spec-review gate** — [`architecture-reviewer`](../../.claude/agents/architecture-reviewer.md) in **spec mode** against this file — before any code. Blocking findings (especially the Open-question recommendations and the projection-seam Gap) must be resolved in this doc first. Then `implement-plan` executes WP-A → WP-B → WP-C, and the primary agent runs `architecture-reviewer` (code mode) across the combined diff before merge.
