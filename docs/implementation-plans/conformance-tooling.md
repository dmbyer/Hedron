# Conformance Tooling — Template Auto-Fit (sim-5)

**Status:** planned
**Actors:** Administrator/Designer (previews and applies conformance, single and bulk, from the Blazor Integrity page) · System (deterministic fitter math). No player-facing surface.
**Module:** `Core/Modules/BalanceInspection/` (fitter system + result records); `Hedron.Web` (Integrity page affordances). Feature home on ship: [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) (workbench consumer #5).

> **Framing.** Fifth and final sub-slice of the [balance-simulator program seed](balance-simulator.md) (prog-4). The seed's advisor intake (2026-07-13) already resolved the load-bearing decisions — home (`BalanceInspection` domain system + `IContentDefinitionCatalog` write-through), no bus events, templates-only with the player-owned reconform sweep deferred as a named INV-21 exception ([backlog entry](../roadmap/backlog.md#-player-owned-instance-reconform-sweep-deferred-from-the-balance-simulator-program-prog-4)). This plan does not relitigate them. The seed file stays untouched; `sync-roadmap` reconciles it on ship.

---

## Description

Closes the balance workbench's observation→correction loop. The Integrity page already *observes*: `IBalanceAuditSystem.Audit()` flags every item/mob template whose authored (Tier, Band) drifts past tolerance from its oracle-computed classification. Sim-5 adds the *correction*: a new `ITemplateConformanceSystem` that **scales a template's existing stat vector** (ratio-preserving, uniform) until its projected `Estimate` lands in `IPowerBudgetSystem.TargetRange(authoredTier, authoredBand)` — **preview → apply**, single template and bulk over the flagged set, writing YAML through the existing `IContentDefinitionCatalog.SaveAsync` validate-then-write path. It is scaling toward a target, never stat-block *synthesis* (that remains procedural-generation scope, per the prog-3b resolution). YAML-side only: no live-entity mutation, no bus publish, no SQLite; live worlds pick up conformed templates on the established `reload` step (server `reload` command / web-host "Apply to live"). The sim-2/3/4 engine that validated the target ranges needs no changes.

---

## Preconditions

- sim-1 shipped: `IPowerBudgetSystem` composed from the balance-standards registry; `IBalanceAuditSystem.Audit()` + Integrity page live; `BalanceInspectionModule` registered via `Server/CompositionRoot.Register` (both hosts resolve it).
- sim-2 shipped (order rationale from the seed): the target ranges are sim-validated before bulk writes against them.
- Item/mob YAML definitions exist on disk under the content directory; `IContentDefinitionCatalog` (Authoring module) resolvable in the host.
- The template being fitted carries an authored `Band` 1–3 (Band 0 / unbanded has no target cell — excluded, matching the audit's drift exclusion).
- Designer is on the loopback-only `Hedron.Web` host (existing v1 auth posture — unchanged).

## Postconditions

- For a **fittable** template, the applied YAML — re-loaded from disk and re-projected through the same projection seam — classifies inside `TargetRange(authoredTier, authoredBand)`. *(internal state — flow test)*
- The fitter mutated **only** the fields the projection seam reads (items: `StatBonuses` magnitudes; mobs: `Mind`/`Body`/`Spirit`/`Attunement`/`MaxHp`/`MaxMana`/`MaxStamina`/`MaxAstra`); authored `Tier`/`Band`, name, description, keywords, spawn room, value, slots, shop/loot/protection fields are byte-identical. *(internal state — system-unit test)*
- Stat *ratios* are preserved up to integer rounding (single uniform scale factor + bounded ±1 correction). *(internal state — system-unit test)*
- An **already-in-range** template yields `AlreadyInRange` and apply performs **no write** (zero `SaveAsync` calls). *(internal state — system-unit test)*
- An **unfittable** template (zero weighted power vector, Band 0, or non-convergent rounding correction) yields `NotFittable` with a reason and no write. *(internal state — system-unit test)*
- A catalog validation refusal (`ContentWriteResult.Failed`) surfaces on the apply result; no file written. *(internal state — system-unit test)*
- Bulk apply is a loop of the single-apply path: per-template results aggregated, `NotFittable`/refused entries skipped and recorded, remaining templates written — one code path (INV-19). *(internal state — system-unit test)*
- No live entity, `EntityService`, `PersistentEntity`, `SaveEntityAsync`, or bus publish is touched anywhere in the slice. *(architecture-guard — existing no-bus guard + a new ctor-shape guard pinning the fitter's dependency set; see Test plan Tier 5)*
- Apply **re-derives the fit from disk at apply time** (never trusts a stale preview object); the audit's boot-registry staleness cannot cause a stale-data overwrite. *(internal state — system-unit test)*

---

## Main flow

*Single-template conformance from the Integrity page; bulk is steps 2–7 looped over the flagged set via `PreviewFlagged()`/`ApplyFlaggedAsync()`.*

1. Designer opens the Integrity page; the existing audit sweep lists drifted templates (authored vs. computed cell, drift).
2. Designer clicks **Preview fit** on a flagged row → page calls `ITemplateConformanceSystem.Preview(kind, blueprintId)`.
3. The system loads the template **from disk** via `IContentDefinitionCatalog.Load` (disk truth — a designer may have edited since the audit registry was loaded; if it now classifies in range, return `AlreadyInRange`).
4. The system projects the template through `IItemPowerProjectionSystem`/`IMobPowerProjectionSystem`, computes current power/cell via `Estimate`/`Classify`, reads `TargetRange(authoredTier, authoredBand)`, computes one uniform scale factor toward the range **midpoint**, rounds each field, re-projects with the real seam, and runs the bounded ±1 correction if rounding left the result outside the range (see Design notes for the exact policy).
5. The page renders the `ConformancePreview`: per-field before→after, power before→after, computed cell before→after, status (`Fitted`/`AlreadyInRange`/`NotFittable` + reason).
6. Designer clicks **Apply** → page calls `ApplyAsync(kind, blueprintId)`; the system re-derives the same fit from disk (idempotent) and calls `IContentDefinitionCatalog.SaveAsync` — validate-then-write; refusal returns a failed result, warnings pass through (warn-but-allow, existing catalog posture).
7. The page renders the apply result and marks the row **applied — pending reload** (the audit table reads the boot-time `ITemplateRegistry` and refreshes only after step 8).
8. Designer runs the existing **Apply to live (reload)** page (web host) and/or `reload` (telnet server) — `IWorldContentLoader.ReloadAsync` re-reads YAML, re-registers templates, respawns world content (existing flow-05 / flow-29 legs; nothing new built here).

## Events fired

**None.** Resolved by the program seed's Architecture brief: conformance writes are YAML-side, mirroring `SaveAsync` — the fitter and the catalog are domain systems that return results (INV-5); there is no live-world fact for an observer to react to, so nothing belongs on the bus. The Blazor page is the initiating surface and consumes returned records directly. If an in-game bulk-conform admin command lands later, *that* command follows the admin audit-event pattern (INV-22 boundary) — out of scope here.

## Systems / handlers involved

| Piece | Role | Status |
|---|---|---|
| `ITemplateConformanceSystem` / `TemplateConformanceSystem` (`Core/Modules/BalanceInspection/Systems/`) | The fitter: `Preview`, `PreviewFlagged`, `ApplyAsync`, `ApplyFlaggedAsync`. Returns records; publishes nothing. | **New** |
| `ConformancePreview` / `ConformanceFieldChange` / `ConformanceApplyResult` / `ConformanceBulkResult` / `ConformanceStatus` (`Core/Modules/BalanceInspection/ConformanceReport.cs`) | Result records, mirroring `BalanceAuditReport.cs` placement. | **New** |
| `IPowerBudgetSystem` (`Core/Systems/`) | `Estimate`/`Classify`/`TargetRange` — the *only* source of target math; the fitter never re-derives weights/anchors (INV-19). | Reused |
| `IItemPowerProjectionSystem` / `IMobPowerProjectionSystem` | The template→snapshot seams; define exactly which fields the fitter may touch. | Reused |
| `IBalanceAuditSystem` | Supplies the flagged set for the bulk path. | Reused |
| `IContentDefinitionCatalog` (`Core/Modules/Authoring/Systems/`) | `Load` (disk truth) + `SaveAsync` (validate-then-write, warn-but-allow) — the single YAML write path. | Reused |
| `BalanceInspectionModule` | Registers the fitter (singleton) — via `CompositionRoot.Register`, so both hosts resolve it. | Modified |
| `Integrity.razor` (`Hedron.Web/Components/Pages/`) | Per-row Preview/Apply + bulk Preview-all/Apply-all affordances; thin caller, no fit logic. | Modified |
| `IWorldContentLoader.ReloadAsync` / Apply page / `reload` command | The existing pickup step for live worlds — untouched, referenced. | Reused |

**Handlers:** none (no events). **Commands:** none (no telnet verb this slice — the seed scopes the surface to the Integrity page).

---

## Implementation plan — work packages

### WP-1 — Fitter system + records + registration + tests (`Core` + `Hedron.Tests`)

- **Scope:** `ConformanceReport.cs` records; `ITemplateConformanceSystem`/`TemplateConformanceSystem` implementing the scaling policy (Design notes); registration in `BalanceInspectionModule` (deps: `IContentDefinitionCatalog`, `IPowerBudgetSystem`, `IItemPowerProjectionSystem`, `IMobPowerProjectionSystem`, `IBalanceAuditSystem`); all Test-plan tiers T1 + T3 + the reference-catalog row diffs staged in the PR.
- **Dependencies:** none (all seams exist).
- **Out of scope:** any UI; any new command; any live-entity or persistence code; any change to the oracle, audit, projections, catalog, or simulation engine.
- **Exit criterion:** `dotnet test` green with the named tests; DI-smoke resolves the fitter in both host compositions.

### WP-2 — Integrity page surface + flow doc (`Hedron.Web` + `docs`)

- **Scope:** per-drifted-row **Preview fit** → expandable preview (field before/after, power/cell before/after, status) → **Apply**; bulk **Preview all flagged** → preview table → **Apply all** (skips `NotFittable`, reports per-row results); "applied — pending reload" row state + hint pointing at Apply-to-live; amend the page's "read-only; never writes files" header to name the conformance exception. Update [flow-29](../architecture/flows/flow-29-bulk-content-generation.md) with the conformance leg.
- **Dependencies:** WP-1.
- **Out of scope:** background-job execution (fits are fast synchronous math + one file write per template — no `SimulationRunService`-style job registry needed; bulk over the flagged set is dozens of templates, not sweeps); auto-reload after apply.
- **Exit criterion:** build green; manual walkthrough — flag a template, preview, apply, Apply-to-live, re-sweep shows it in range.

The primary agent runs `architecture-reviewer` (code mode) across the combined WP-1+WP-2 diff.

---

## Content tooling impact (INV-18)

**This slice *is* content tooling** — it adds no new gameplay state, no new data-file shape, no new `TemplateRegistry` entry, and no admin command. It edits **existing** item/mob YAML definitions through the **existing** catalog write path (same validator, same warn-but-allow cross-reference posture, same atomic per-file write the editors use). Inspection of what it did is intrinsic: the preview *is* the inspection surface (per-field diff before any write), the applied YAML is diffable in the content directory, and the post-reload audit re-sweep confirms the flagged row cleared. INV-18's "author and inspect in the same PR" is satisfied by construction.

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Notes |
|---|---|---|
| Commands | **Adequate** (not exercised) | No new verb; the seed scopes the surface to the Integrity page. A later telnet `conform` command would be a thin caller of the same system. |
| Output | **Adequate** (not exercised) | Blazor rendering only; no `IOutputMessage`. |
| Persistence | **Adequate** | YAML-side only; see the opt-in audit below. |
| Event bus | **Adequate** | Nothing published (INV-5; seed-resolved). Existing no-bus-in-systems guard covers the new `*.Systems` type automatically. |
| ECS queries | **Adequate** (not exercised) | The fitter never touches `EntityService` or live entities. |
| Broadcast / sessions | **Adequate** (not exercised) | No player/session surface. |
| Time / randomness | **Adequate** | Pure deterministic math — no `IRandom`/`IClock` seam needed (stated explicitly, like the oracle; golden-number test). |
| Content templates | **Adequate** | Reuses `IContentDefinitionCatalog` `Load`/`SaveAsync` — no schema change, no new writer, no hand-rolled YAML. The single-fitter/two-kinds dispatch mirrors `BalanceAuditSystem`'s item/mob switch (2nd instance of the pattern; below the ≥3 bar, noted for the next kind). |
| Configuration | **Adequate** | No new keys. Fit parameters (`BandDriftTolerance`, tunables) already live in the standards registry. |
| Modules / DI | **Adequate** | One registration line in `BalanceInspectionModule`; `CompositionRoot` pattern already serves both hosts. |
| Audit-vs-disk staleness | **Adequate** (by design) | The flagged set comes from the boot-time registry while the fitter reads disk; resolved by disk-recompute at preview *and* apply (`AlreadyInRange` short-circuit), never by trusting the audit row. Post-apply audit staleness until reload is the established restart/reload-to-apply model (sim-1 precedent), stated in the UI. |

**Persistence opt-in audit (INV-22/23).**
- *Level 1 — entity domains:* the slice introduces or modifies **no entity construction path**. Item/mob templates are world-content definitions; their spawned entities never carry `PersistentEntity` (existing rule, unchanged). No domain transitions.
- *Level 2 — components:* **no component is introduced or touched.** The fitter mutates template POCOs (`ItemTemplate`/`MobTemplate`), which are not components; `ItemDataComponent`/`MobDataComponent` re-apply from templates on spawn/reload exactly as today.
- *Level 3 — save-on-change:* **zero `SaveEntityAsync` call sites.** The only write is `IContentDefinitionCatalog.SaveAsync` (YAML), which is documented as never creating entities or touching SQLite. No INV-22 exposure.

## Flows introduced or modified (INV-17)

- **[flow-29 — Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md)** — **extended**: a conformance leg on the offline-edit path (Integrity page → `ITemplateConformanceSystem` preview/apply → `IContentDefinitionCatalog.SaveAsync` → existing reload step). Updated in WP-2; no new flow file — this is the same designer journey, not a new recurring runtime chain.
- **[flow-05 — Content reload](../architecture/flows/flow-05-content-reload.md)** — referenced unchanged (the pickup step).
- No other flow is touched; there is no telnet leg (flow-03) and no simulation-engine change (flow-33).

## Test plan / Verification (INV-25)

**Tier 1 — system unit (`Hedron.Tests/BalanceInspection/TemplateConformanceSystemTests.cs`)** — fake `IContentDefinitionCatalog` (records `SaveAsync` calls), real oracle/projections under `PowerBudgetTunables.Default`:

1. **Item fit** — drifted `ItemTemplate` → `Fitted`; re-projected power ∈ `TargetRange(tier, band)`; `StatBonuses` ratios preserved within rounding; every non-`StatBonuses` field (incl. `Tier`/`Band`) unchanged.
2. **Mob fit** — attributes + max pools scaled uniformly; derived `AttackPower`/`Defense` follow scaled `Body` through the real projection; lands in range; shop/loot/protection/spawn fields unchanged.
3. **Midpoint targeting + determinism golden-number** — same input → identical fitted vector and power (pure math; no `IRandom`/`IClock` seam needed — asserted by the guard suite's no-ambient-nondeterminism check plus this repeatability test).
4. **Rounding-correction convergence** — a scale factor whose rounding lands just outside a narrow range converges inside via the bounded ±1 correction; a constructed non-convergent case returns `NotFittable(RoundingDidNotConverge)` within the iteration cap, no write.
5. **`AlreadyInRange`** — in-range template → no field changes; `ApplyAsync` performs zero `SaveAsync` calls.
6. **`NotFittable` guards** — zero weighted power vector (throws nothing, returns reason); authored `Band == 0` (no target cell); each with no write.
7. **Apply re-derives from disk** — catalog fake returns a template that changed after a preview; the saved definition reflects the disk-derived fit, not the stale preview.
8. **Validation refusal propagation** — fake `SaveAsync` returns `Failed` → apply result failed, errors surfaced, exactly one attempted write.
9. **Bulk = loop of singles (INV-19)** — flagged set of 3 (one `NotFittable`) → exactly 2 `SaveAsync` calls, 1 recorded skip, aggregate result matches the per-template results; `PreviewFlagged` over the same set returns per-template previews identical to calling `Preview` on each (no bulk-path fork on the read side either).

**Tier 3 — flow round-trip (`Hedron.Tests/BalanceInspection/ConformanceRoundTripTests.cs`)** — real `ContentDefinitionCatalog` over a temp content directory (existing `ContentDefinitionCatalogTests` harness): author an out-of-band item + mob YAML → `Preview` → `ApplyAsync` → re-`Load` from disk → re-project/classify in range; asserts the Postconditions end-to-end through the real serializer (catches YAML field-fidelity regressions the fake cannot).

**Tier 5 — architecture-guard:** existing suite covers most of the new system automatically (no-bus-in-systems over `*.Systems`, no-ambient-nondeterminism, DI-smoke resolving the new registration in both host compositions). **One new guard** (spec-gate finding): a ctor-shape assertion (precedent: `PowerBudgetSystem_has_no_domain_module_dependency`) that `TemplateConformanceSystem`'s constructor parameters are exactly the five named seams (`IContentDefinitionCatalog`, `IPowerBudgetSystem`, `IItemPowerProjectionSystem`, `IMobPowerProjectionSystem`, `IBalanceAuditSystem`) — pinning that it never gains an `EntityService`/`IPersistenceSystem` dependency, which no existing guard asserts.

**Skipped, with reasons:** Tier 2 (no handlers/events exist); Tier 4 (no `[Persistent]` shape — YAML-side only); `Integrity.razor` markup and button wiring (presentation-skip tier per 07-testing — every decision lives in the fitter, which WP-1 tests directly); catalog internals and YamlDotNet (existing suite / third-party); exact preview prose.

**Testability gaps:** none — the fitter is deterministic math over injected seams; no un-injected randomness, wall-clock, or I/O (file I/O is behind the injected catalog).

---

## Design notes

*Folded from the seed's Design notes and Architecture brief; durable — survives into `power-budget-system.md` / `roadmap/completed/` on ship.*

- **Scaling toward a target, not synthesis.** The fitter multiplies an *existing* authored vector by one factor; it never invents a stat block from a range (procedural-generation scope, prog-3b resolution). This is why ratio preservation is the natural policy: the designer's *shape* intent (a glass-cannon mob, a defense-heavy item) survives; only its *magnitude* conforms.
- **The oracle is the only target math (INV-19).** The fitter calls `Estimate`/`Classify`/`TargetRange` and exploits the documented linearity of the weighted sum to compute the closed-form factor — `k = (targetMid − tierTerm) / variablePower`, where `tierTerm = Estimate(emptySnapshot, tier)` and `variablePower = Estimate(currentSnapshot, 0)` — then **verifies with real `Estimate`+`Classify` calls** and corrects, so a future oracle change degrades to more correction steps, never to silent drift. Weights, anchors, and band math are never re-derived.
- **The projection seams define the knob set.** The fitter may touch exactly the fields `IItemPowerProjectionSystem`/`IMobPowerProjectionSystem` read — anything else is invisible to power and mutating it would be scope creep into general editing. Mob HP is included because the mob snapshot includes `MaxHp`. Negative item bonuses (cursed-style) scale with their sign preserved.
- **Scaling policy (resolves seed Open question 6 — user-confirmed at the spec gate, see Open questions):** uniform ratio-preserving scale of the projected fields toward the **cell midpoint**; round half-away-from-zero per field; verify via real projection; if outside the range, bounded correction — adjust the field with the largest per-unit power contribution by ±1, re-verify, cap at 8 iterations; on cap, `NotFittable(RoundingDidNotConverge)`. Midpoint (vs. nearest edge) maximizes headroom against later tunables tweaks and keeps repeated bulk passes idempotent (`AlreadyInRange` on the second pass).
- **Disk truth, both directions.** The audit's flagged set comes from the boot-time `ITemplateRegistry`, but preview and apply both re-load from disk via the catalog and re-derive the fit — a stale audit row can trigger a no-op (`AlreadyInRange`), never a stale overwrite. Symmetrically, an applied fit is invisible to the audit until the existing reload step; the page says so ("applied — pending reload"), matching sim-1's restart/reload-to-apply posture.
- **INV-21 posture.** Conformance is an admin mutation of the **template definition only**. Unlike `setitem`/`setmob` (which mutate a *live* entity through a domain system and mirror to YAML), the fitter starts and ends at YAML — live entities re-apply from the template on `reload`/respawn, so "updates both template and entity" is satisfied through the established reload path, not by touching entities. Player-owned instances are never read or written; the owned-instance reconform sweep stays the deferred, named, audited INV-21 exception in the [backlog](../roadmap/backlog.md#-player-owned-instance-reconform-sweep-deferred-from-the-balance-simulator-program-prog-4) — nothing here builds toward it, and its later slice reuses this fitter's math.
- **Known pre-existing nuance, unchanged:** `MobTemplate.Apply` defaults zero attributes to 10 at spawn while the projection reads raw template values — the fitter scales exactly what the projection (and therefore the audit) sees, so it is internally consistent with the flagging that triggered it; reconciling projection-vs-spawn defaulting is out of scope and now tracked in the [backlog](../roadmap/backlog.md#-mob-projection-vs-spawn-attribute-defaulting-divergence-surfaced-at-sim-5-planning) (spec-gate finding — this slice gives the divergence its first writer).
- **No engine changes.** The simulation engine (sim-2/3/4) validated the target ranges the fitter now writes toward; it is a consumer-sibling, not a dependency at runtime.

## Open questions

1. **Scaling policy (seed Open question 6)** — ✅ **RESOLVED (2026-07-17, user-confirmed at spec gate).** Knobs: items — `StatBonuses` magnitudes only; mobs — `Mind`/`Body`/`Spirit`/`Attunement` + `MaxHp`/`MaxMana`/`MaxStamina`/`MaxAstra` (exactly the projection-read set; derived `AttackPower`/`Defense` follow `Body`). Policy: ratio-preserving uniform scale to the cell **midpoint**, round half-away-from-zero, bounded ±1 largest-contribution correction (cap 8), `NotFittable` on zero vector / Band 0 / non-convergence. Rationale in Design notes. The alternative (targeting specific per-stat scores from the reference build) was set aside as synthesis-adjacent — it discards the designer's authored shape.
2. **No auto-reload after bulk apply** — ✅ **RESOLVED (2026-07-17, user-confirmed at spec gate).** Applying conformance leaves the registry/live world to the explicit Apply-to-live / `reload` step (established model; keeps the fitter YAML-pure and the reload decision in the designer's hands). The alternative — the page chaining `ReloadAsync` after bulk apply — is a one-line UI addition later if the two-step friction proves real.

## Related

- [`balance-simulator.md`](balance-simulator.md) — the program seed this plan extends (sim-5 row, "Conformance is scaling toward a target" design note, Architecture brief, resolved decision 3).
- [`progression-and-balance.md`](progression-and-balance.md) — parent program brief (prog-4 context).
- [`../features/progression/power-budget-system.md`](../features/progression/power-budget-system.md) — the oracle, standards registry, projection seams, and audit this slice composes; gains the conformance consumer on ship.
- [`../features/simulation/simulation.md`](../features/simulation/simulation.md) / [`simulation-engine.md`](../features/simulation/simulation-engine.md) — sim-2/3/4 as-built; validated the target ranges, unchanged here.
- [`../features/admin-authoring/content-authoring.md`](../features/admin-authoring/content-authoring.md) — the catalog write path and editor posture (validate-then-write, warn-but-allow) the fitter reuses.
- [`../roadmap/backlog.md`](../roadmap/backlog.md#-player-owned-instance-reconform-sweep-deferred-from-the-balance-simulator-program-prog-4) — the deferred owned-instance reconform sweep (named INV-21 exception).
- [`../architecture/checklist.md`](../architecture/checklist.md) — INV-5, INV-17, INV-18, INV-19, INV-21, INV-22/23, INV-25/26.
- Flows: [flow-29](../architecture/flows/flow-29-bulk-content-generation.md) (extended), [flow-05](../architecture/flows/flow-05-content-reload.md) (referenced).
