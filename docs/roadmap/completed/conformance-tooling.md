# Template conformance tooling (slice sim-5, completed)

> Implemented on branch `claude/conformance-tooling-plan-c99c96`, 2026-07-17. Living docs:
> [`features/progression/power-budget-system.md`](../../features/progression/power-budget-system.md).

## Outcome

Closes the balance workbench's observation→correction loop. `IBalanceAuditSystem` already
*observes*: it flags every item/mob template whose authored (Tier, Band) drifts past tolerance from
its oracle-computed classification. `sim-5` adds the *correction*: a new `ITemplateConformanceSystem`
that **scales a template's existing stat vector** (ratio-preserving, uniform) until its projected
power lands in `IPowerBudgetSystem.TargetRange(authoredTier, authoredBand)` — preview → apply, single
template and bulk over the flagged set, writing YAML through the existing
`IContentDefinitionCatalog.SaveAsync` validate-then-write path. It is scaling toward a target, never
stat-block *synthesis*. YAML-side only: no live-entity mutation, no bus publish, no SQLite; live
worlds pick up conformed templates on the established `reload` step. The final sub-slice of the
`prog-4` balance-simulator program — `prog-4` is now fully shipped.

## Behavior digest

- **Preconditions** — `sim-1` (`IPowerBudgetSystem` + standards registry + `IBalanceAuditSystem` +
  Integrity page) and `sim-2` (sim-validated target ranges) both shipped; item/mob YAML exists on
  disk under the content directory; the fitted template carries an authored `Band` 1–3 (`Band` 0 has
  no target cell, matching the audit's own drift exclusion).
- **Postcondition 1** — for a fittable template, the applied YAML — re-loaded from disk and
  re-projected through the same seam — classifies inside `TargetRange(authoredTier, authoredBand)`.
- **Postcondition 2** — the fitter mutates only the fields the projection seam reads (items:
  `StatBonuses` magnitudes; mobs: `Mind`/`Body`/`Spirit`/`Attunement`/`MaxHp`/`MaxMana`/
  `MaxStamina`/`MaxAstra`); every other field — name, description, keywords, spawn room, value,
  slots, shop/loot/protection fields, and authored `Tier`/`Band` itself — is byte-identical.
- **Postcondition 3** — stat ratios are preserved up to integer rounding (single uniform scale
  factor + a bounded ±1 correction when rounding alone misses the target cell).
- **Postcondition 4** — an already-on-target template yields `AlreadyInRange` and apply performs
  zero `SaveAsync` calls.
- **Postcondition 5** — an unfittable template (zero weighted power vector, `Band` 0, or a
  non-convergent rounding correction) yields `NotFittable` with a reason and no write.
- **Postcondition 6** — a catalog validation refusal surfaces on the apply result (`Success = false`,
  `Status = WriteRefused`); no file written.
- **Postcondition 7** — bulk apply is a loop of the single-apply path: per-template results
  aggregated, `NotFittable`/refused entries skipped and recorded, remaining templates written — one
  code path (INV-19).
- **Postcondition 8** — no live entity, `EntityService`, `PersistentEntity`, `SaveEntityAsync`, or
  bus publish is touched anywhere in the slice (existing no-bus guard + a new ctor-shape guard
  pinning the fitter's exact dependency set).
- **Postcondition 9** — apply re-derives the fit from disk at apply time (never trusts a stale
  preview object); the audit's boot-registry staleness can trigger a no-op, never a stale overwrite.

## Shipped pieces

| Surface | Location |
|---|---|
| `ITemplateConformanceSystem`/`TemplateConformanceSystem` — `Preview`, `PreviewFlagged`, `ApplyAsync`, `ApplyFlaggedAsync` | `Core/Modules/BalanceInspection/Systems/ITemplateConformanceSystem.cs` · `TemplateConformanceSystem.cs` |
| `ConformancePreview`/`ConformanceFieldChange`/`ConformanceApplyResult`/`ConformanceBulkResult`/`ConformanceStatus`/`ConformanceNotFittableReason` | `Core/Modules/BalanceInspection/ConformanceReport.cs` |
| `BalanceInspectionModule` — fitter singleton registration | `Core/Modules/BalanceInspection/BalanceInspectionModule.cs` |
| `Integrity.razor` — per-row Preview fit/Apply, bulk Preview all flagged/Apply all flagged, "applied — pending reload" row state, amended read-only header | `Hedron.Web/Components/Pages/Integrity.razor` |
| `.pending-badge` style | `Hedron.Web/wwwroot/app.css` |
| `flow-29-bulk-content-generation.md` — new "B3 — Conformance fitter" leg (body + mermaid) | `docs/architecture/flows/flow-29-bulk-content-generation.md` |
| Reference row (`TemplateConformanceSystem`/`ITemplateConformanceSystem`) | `docs/reference/systems.md` |
| `Dockerfile.dev` — reproducible `dotnet build`/`dotnet test` container for environments with no preinstalled SDK | repo root |

## Tests shipped

`dotnet test` green at 1212 total (up from 1198 pre-slice).

- **Tier 1** — `TemplateConformanceSystemTests` (new, `Hedron.Tests/Modules/BalanceInspection/`):
  item fit (ratio preserved exactly, non-`StatBonuses` fields byte-identical); mob fit (attributes +
  pools scaled, derived `AttackPower`/`Defense` follow scaled `Body` through the real projection,
  shop/loot/protection/spawn fields untouched); determinism (`Preview` called twice over the same
  disk state returns identical results); rounding-correction convergence (a real Default-tunables
  case whose naive scale lands exactly on `PowerBudgetSystem`'s documented tier-boundary-overlap
  anchor, needing exactly one bounded correction step) and its non-convergent counterpart (a
  constructed single-weight-125-vs-band-width-41 tunables setup that can only ever land on multiples
  of 125, never inside the target band — exhausts the iteration cap deterministically); `AlreadyInRange`
  (no field changes, zero `SaveAsync` calls); `NotFittable` guards (zero weighted power vector;
  `Band` 0); apply re-derives from disk (a stale `Preview` is never trusted — the saved fit reflects
  a template mutated on disk *after* the preview); validation-refusal propagation (`Status =
  WriteRefused`, exactly one attempted write); bulk = loop of singles (3-entry flagged set, one
  `NotFittable`, exactly 2 `SaveAsync` calls, `PreviewFlagged` matches per-entry `Preview` calls).
- **Tier 3** — `ConformanceRoundTripTests` (new): a real `ContentDefinitionCatalog` over a temp
  content directory (mirrors `ContentDefinitionCatalogTests`'s harness) — author an item and a mob
  YAML out of band, `Preview` → `ApplyAsync` → re-`Load` from disk → re-project/classify inside the
  target cell, through the real serializer.
- **Tier 5** — new `TemplateConformanceSystem_has_exactly_the_five_named_seam_dependencies` guard
  (precedent: `PowerBudgetSystem_has_no_domain_module_dependency`) pins the constructor to exactly
  `IContentDefinitionCatalog`/`IPowerBudgetSystem`/`IItemPowerProjectionSystem`/
  `IMobPowerProjectionSystem`/`IBalanceAuditSystem` — no existing guard would have caught the fitter
  quietly gaining an `EntityService`/`IPersistenceSystem` dependency. Existing no-bus-in-systems,
  no-ambient-nondeterminism, and DI-smoke guards cover the new system automatically (namespace/
  interface-convention based).
- **Manual walkthrough** (WP-2 exit criterion) — ran the real `Hedron.Web` host end-to-end via
  Playwright/Chromium: created a drifted item through the live `ItemEditor`, reloaded so the
  boot-time `ITemplateRegistry` picked it up, swept the Integrity page (flagged, drift 7), clicked
  **Preview fit** (power 400→688, cell 0/1→2/2, AttackPower 15→42, Defense 5→14 — matching the
  Tier-1 test's hand-verified numbers exactly), clicked **Apply** (row → "applied — pending reload"),
  ran **Apply to live**, and re-swept: the applied item cleared the flagged set while an untouched
  sibling item remained flagged. Confirms the full preview→apply→reload→re-observe loop end-to-end,
  not just through fakes.

## Decisions

- **`OnTarget` uses `Classify` equality, not raw `PowerRange` containment.** The plan's Main Flow
  step 3 says "if it now classifies in range, return `AlreadyInRange`" — read literally as
  `Classify(power) == (tier, band)`, not `TargetRange(tier, band).Contains(power)`. These two
  diverge exactly at `PowerBudgetSystem`'s documented tier-boundary-overlap fallback (a power
  landing at or above a tier's `BandAnchor` but below its own reference power floors to
  `(tier, 1)`, and a power below every anchor floors to `(0, 1)` regardless of literal range
  containment). Using `Classify` equality throughout — both the initial short-circuit and the
  post-scale convergence check — matches `IBalanceAuditSystem`'s own zero-drift definition exactly
  and is what actually caught the real tier-boundary-overlap correction case exercised in the mob
  rounding-correction test. `TargetRange` is still the only input to the closed-form scale's
  midpoint target.
- **The bounded-correction "largest per-unit power contribution" field is chosen by probing the
  real projection, not by reading raw `PowerBudgetTunables.Weights` directly.** The fitter's
  constructor deliberately excludes `PowerBudgetTunables` (the ctor-shape guard pins exactly five
  seams) — probing each candidate field's actual `+1` marginal `Estimate` delta through the real
  `IItemPowerProjectionSystem`/`IMobPowerProjectionSystem` captures derived-field nonlinearity (a
  mob's `Body`-driven `AttackPower`/`Defense` floor division) that a raw weight lookup would miss,
  and is more literally faithful to the plan's "verifies with real `Estimate`+`Classify` calls" design
  note than a shortcut through the tunables table would have been.
  Candidate fields are probed in a fixed declared order (item: `StatBonuses` list order; mob: a
  static `MobKnobs` array) with strict `>` comparison, so ties resolve deterministically — no
  `Dictionary` enumeration-order dependency (INV-26 golden-number test backs this).
- **`ConformanceStatus` gained a `WriteRefused` member post-review.** The `architecture-reviewer`
  code-mode gate flagged that `ConformanceApplyResult.Failed(...)` originally reused
  `ConformanceStatus.Fitted` for a catalog validation refusal — harmless today (every call site
  gates on `Success` first) but a latent landmine for a future `Status`-only consumer. Fixed by
  adding `ConformanceStatus.WriteRefused` before merge; Test 8 now asserts the status explicitly.
- **No .NET SDK preinstalled in this execution environment** (same shape as `sim-4`'s record); this
  time a `Dockerfile.dev` (`mcr.microsoft.com/dotnet/sdk:8.0`) was committed to the repo root as a
  reusable dev/CI container rather than a throwaway per-session workaround, per the task's explicit
  request. `docker run --network host` (reaching the session's host-local proxy) plus mounting the
  proxy's CA bundle as `SSL_CERT_FILE` lets restore/build/test complete through the proxy. All Tier-1
  numeric fixtures (the item/mob fit scenarios, the tier-boundary-overlap correction case, the
  non-convergent construction) were hand-derived from `PowerBudgetTunables.Default`'s published
  weights/anchors *before* any test run, then verified — every hand-derived test passed on the first
  real `dotnet test` execution; a throwaway diagnostic `[Fact]` (deleted before the final test file)
  was used once, to search a magnitude range for a case exercising the bounded-correction path
  empirically rather than by further hand arithmetic.

## Deviations / Follow-ups

- **No deviations from the plan's shape.** Both work packages (WP-1 fitter + records + registration
  + tests; WP-2 Integrity page surface + flow doc) shipped as scoped; every Postcondition and every
  numbered Test-plan item (Tier 1 items 1–9, Tier 3, Tier 5) has a corresponding, present, green
  test. The one code change beyond the plan's literal text (`ConformanceStatus.WriteRefused`) was a
  spec-compatible enum addition surfaced by the code-review gate, not a behavior or postcondition
  change.
- **`prog-4` (the balance-simulator program) is now fully shipped** — `sim-1` through `sim-5` all
  landed. The only remaining slice in the Progression & Balance program is `prog-5` (agentic +
  balance-doc layer), which stays seed-only until framed with `/new-plan`.
- **Deferred, as planned (named INV-21 exception, not built toward here):** the player-owned
  instance reconform sweep — see
  [`../backlog.md`](../backlog.md#-player-owned-instance-reconform-sweep-deferred-from-the-balance-simulator-program-prog-4).
  Also unchanged/still tracked: the mob projection-vs-spawn attribute-defaulting divergence (see
  [`../backlog.md`](../backlog.md#-mob-projection-vs-spawn-attribute-defaulting-divergence-surfaced-at-sim-5-planning)).
