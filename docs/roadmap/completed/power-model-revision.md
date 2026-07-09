# Power model revision — Tier × Band + calibration + audit tooling (slice prog-3b, completed)

> Implemented on branch `claude/power-model-revision-4c6d78`, 2026-07-08. Living docs:
> [`features/progression/power-budget-system.md`](../../features/progression/power-budget-system.md) ·
> [`features/progression/ascension-system.md`](../../features/progression/ascension-system.md) ·
> [`design/power-model.md`](../../design/power-model.md).

## Outcome

`prog-3`'s one-axis power-budget oracle (`Classify(power) → int`, a 0–6 tier band) is revised into a
D&D-Challenge-Rating-style two-axis model: **Tier** (0–6, the mechanical Ascension-gated scalar,
unchanged) × **Band** (1–3, a purely descriptive low/mid/high subdivision *within* each tier). The
oracle gains `Classify(power) → PowerBand(Tier, Band)` and its inverse, `TargetRange(tier, band) →
PowerRange`; the authored `TierBand` content tag on both items and mobs splits into a `Tier`+`Band`
pair across every authoring surface; a shared item/mob power-projection seam replaces three
hand-rolled inline snapshot builds; a new `IBalanceAuditSystem` sweeps all content for
authored-vs-computed band drift (soft, advisory, feeding a Blazor Integrity report); the
placeholder `PowerBudgetConstants` are recalibrated for real headroom; and the oracle's
snapshot-only extensibility rule is written down in a new `docs/design/power-model.md`. `Estimate`'s
algorithm, the three-consumer framework, and the anti-grind rewire from prog-3 all hold unchanged —
only the classification/inverse/calibration/authoring surface moved. Fourth slice of the
Progression & Balance program (prog-4 simulation harness is next).

## Behavior digest

- **Two-axis classify.** `Classify(power)` returns `PowerBand(int Tier, int Band)`; `Tier ∈ [0,
  MaxTier]` derived exactly as before (highest tier whose `BandAnchor` is at or below power, the
  `BandSpan` tier-boundary overlap retained); `Band ∈ {1,2,3}`, never 0 (0 is exclusively the
  *authored* unbanded sentinel) — the position within the tier's power span, bucketed into thirds.
  A power still in the tier-boundary overlap zone (below the tier's own reference power) floors to
  band 1.
- **Partition within tier, overlap only at tier boundaries.** The three bands partition a tier's
  span with no intra-tier overlap; the retained `BandSpan` overlap is exclusively the
  tier-boundary hysteresis.
- **Inverse query.** `TargetRange(tier, band) → PowerRange(MinPower, MaxPower)`; band 3's
  `MaxPower` abuts the next tier's band-1 `MinPower` (clean partition, no gap); an out-of-range
  cell throws `ArgumentOutOfRangeException`.
- **Strictly-increasing anchors.** The ~21 `(Tier, Band)` cell floors are strictly increasing
  across the whole table; the calibration invariant `BandSpan < tierSpan / BandsPerTier` holds.
- **`Estimate` unchanged.** Algorithm and signature untouched; numeric output shifted only because
  `Weights`/`BandSpan` were recalibrated. The anti-grind proxy's ratio semantics (floor/peer/cap)
  hold because the fixtures use proportional/identical stat profiles — the three
  `ProgressionSystemTests` cases pass unmodified.
- **INV-2 preserved & extended guard.** The architecture-guard test's hardcoded oracle-file array
  grew from four files to six (`PowerBand.cs`/`PowerRange.cs` added), so the new types are
  actually scanned for domain imports, not merely assumed clean.
- **Field split (clean break, lossy not a wipe).** `ItemDataComponent`/`MobDataComponent` and
  `ItemTemplate`/`MobTemplate` carry `Tier` (0–6) + `Band` (0–3) replacing `TierBand`. Because the
  new `band:` YAML key reuses the old key name, a legacy `band:` value in `[1,3]` is silently
  reinterpreted as new-axis `(tier 0, band N)`; `[4,6]` warns-and-untags. The distinct legacy
  persisted `TierBand` JSON key no longer maps → untagged.
- **Authoring parity.** `setitem`/`setmob` gained `tier` (0–6) and `band` (0–3) property branches,
  each dual-writing template + live component and reusing the existing admin-audit event (no
  schema change — `NewValue` composes either value as a string).
- **Round-trips.** YAML `tier:`+`band:` round-trip losslessly (warn-and-default out-of-range); a
  player-owned item's `ItemDataComponent.Tier`+`Band` survive a SQLite save→load round-trip.
- **Readouts.** `power <target>` shows computed `(Tier, Band)` + authored `(Tier, Band)`;
  `powerband [tier]` lists all ~21 cells (or one tier's three) with each cell's `TargetRange`.
- **Shared projection seam.** One item projection (`ItemTemplate` + live `ItemDataComponent`, both
  keyed on `StatBonuses`) feeds `power`'s item path, `ItemEditor`, and the audit; one mob
  *template* projection feeds `MobEditor` and the audit. `power`'s self/mob paths keep their
  existing live `IStatSystem` snapshot — a distinct, correct projection, not folded in.
- **Drift audit (soft).** `IBalanceAuditSystem.Audit()` returns every item/mob whose global
  band-index drift (`|index(authored) − index(computed)|`, `index(T,B) = T·BandsPerTier + (B−1)`)
  exceeds `BalanceAuditConstants.BandDriftTolerance` (authored `Band = 0` excluded from drift, but
  still bucketed), plus counts bucketed by `(Tier, Band)`; recomputed on demand, never a build/CI
  gate.
- **Presentation.** Both editors show tier + band inputs, computed `(Tier, Band)`, and a
  band-count-tolerance mismatch flag; the Blazor Integrity page renders the audit report.
- **Discipline docs.** `docs/design/power-model.md` states the snapshot-only extensibility
  principle; `add-domain-system`/`add-core-system`/`architecture-advisor` now ask "does this
  affect power, and how does its contribution enter the snapshot?"

## Shipped pieces

| Surface | Location |
|---|---|
| `PowerBand` / `PowerRange` result types | `Core/Systems/PowerBand.cs` · `PowerRange.cs` |
| `IPowerBudgetSystem`/`PowerBudgetSystem` — two-axis `Classify`, new `TargetRange`, `BandsPerTier` subdivision | `Core/Systems/IPowerBudgetSystem.cs` · `PowerBudgetSystem.cs` |
| Recalibrated `PowerBudgetConstants` (`Weights`, `BandSpan`, `BandsPerTier`) | `Core/Systems/PowerBudgetConstants.cs` |
| `docs/design/power-model.md` — snapshot-only extensibility principle | `docs/design/power-model.md` |
| `ItemDataComponent`/`MobDataComponent` `Tier`+`Band` (replacing `TierBand`) | `Core/ECS/Components/ItemDataComponent.cs` · `MobDataComponent.cs` |
| `ItemTemplate`/`MobTemplate` `Tier`+`Band` + `Apply` | `Core/Modules/Items/Templates/ItemTemplate.cs` · `Core/Modules/Mobs/Templates/MobTemplate.cs` |
| `IItemBuilderSystem`/`IMobBuilderSystem` `SetItemTier`+`SetItemBand` / `SetMobTier`+`SetMobBand` | `Core/Modules/Items/Systems/ItemBuilderSystem.cs` · `Core/Modules/Mobs/Systems/MobBuilderSystem.cs` |
| `SetitemCommand`/`SetMobCommand` `tier`+`band` branches | `Core/Modules/Items/Commands/SetitemCommand.cs` · `Core/Modules/Mobs/Commands/SetMobCommand.cs` |
| `ItemContentWriter`/`ItemTemplateDeserializer`, `MobContentWriter`/`MobTemplateDeserializer` — `tier:`+`band:` YAML | `Core/Modules/Items/Systems/ItemContentWriter.cs` · `Core/Modules/Items/ItemTemplateDeserializer.cs` · `Core/Modules/Mobs/Systems/MobContentWriter.cs` · `Core/Modules/Mobs/MobTemplateDeserializer.cs` |
| `IItemPowerProjectionSystem`/`ItemPowerProjectionSystem` (new, shared item projection seam) | `Core/Modules/Items/Systems/IItemPowerProjectionSystem.cs` · `ItemPowerProjectionSystem.cs` |
| `IMobPowerProjectionSystem`/`MobPowerProjectionSystem` (new, shared mob template projection seam) | `Core/Modules/Mobs/Systems/IMobPowerProjectionSystem.cs` · `MobPowerProjectionSystem.cs` |
| `IBalanceAuditSystem`/`BalanceAuditSystem` (new, bulk drift sweep) + `BalanceAuditReport` + `BalanceAuditConstants` | `Core/Modules/BalanceInspection/Systems/IBalanceAuditSystem.cs` · `BalanceAuditSystem.cs` · `Core/Modules/BalanceInspection/BalanceAuditReport.cs` · `BalanceAuditConstants.cs` |
| `PowerCommand`/`PowerbandCommand` — two-axis readout / ~21-cell listing | `Core/Modules/BalanceInspection/Commands/PowerCommand.cs` · `PowerbandCommand.cs` |
| `PowerReadoutMessage`/`PowerBandRow`+`PowerbandMessage`/`TelnetOutputFormatter` — `(Tier, Band)` + ranges | `Core/Output/PowerReadoutMessage.cs` · `PowerbandMessage.cs` · `TelnetOutputFormatter.cs` |
| `BalanceInspectionModule` — registers `IBalanceAuditSystem` | `Core/Modules/BalanceInspection/BalanceInspectionModule.cs` |
| Blazor `ItemEditor`/`MobEditor` — tier+band inputs, two-axis readout, tolerance flag | `Hedron.Web/Components/Pages/ItemEditor.razor` · `MobEditor.razor` |
| Blazor `Integrity` page — audit report section | `Hedron.Web/Components/Pages/Integrity.razor` |
| `add-domain-system`/`add-core-system`/`architecture-advisor` — power-contribution question | `.claude/skills/add-domain-system/SKILL.md` · `.claude/skills/add-core-system/SKILL.md` · `.claude/skills/architecture-advisor/SKILL.md` |

## Tests shipped

- **Tier 1** — `PowerBudgetSystemTests` re-golded for the two-axis anchors (tier-boundary overlap,
  within-tier partition, below-floor → band 1 never 0) plus new `TargetRange` tests (cell
  correctness, band-3/next-tier-band-1 abutment, strictly-increasing whole-table floors,
  out-of-range fail-fast) and a `BandSpan < tierSpan/BandsPerTier` calibration-invariant test.
  `BalanceAuditSystemTests` (new): band-index drift delta, within/past-tolerance, authored `Band =
  0` excluded from drift but still bucketed, empty-registry → empty report.
- **Tier 2** — `PowerCommandTests`/`PowerbandCommandTests` re-golded for the recalibrated constants
  and two-axis output. `SetitemCommandBandTests`/`SetMobCommandBandTests` extended with `tier`
  branch cases alongside the existing `band` cases (dual-write + one audit event, range
  validation, no mutation on invalid input). `ItemBuilderSystemTests`/`MobBuilderSystemTests`
  extended with `SetItemTier`/`SetMobTier` cases.
- **Tier 4** — `ItemTierBandRoundTripTests`/`MobTierBandRoundTripTests` re-golded for `tier:`+`band:`
  YAML, including two new clean-break cases per file: a legacy `band: 2`-only file reinterpreted as
  `(tier 0, band 2)`, and a legacy `band: 5`-only file warning-and-untagging.
- **On-touch ratchet** — the three `ProgressionSystemTests` anti-grind cases re-verified unmodified
  (ratio invariant to weight rescaling).
- **Tier 5** — `ArchitectureGuardTests.PowerBudgetSystem_has_no_domain_module_dependency` extended
  to scan `PowerBand.cs`/`PowerRange.cs` alongside the original four files; the DI-smoke test
  resolves `IBalanceAuditSystem` in the shared composition root (both hosts).
- `dotnet build` and `dotnet test` green — 1045 tests total (up from 1039 pre-slice; net +6 new
  `BalanceAuditSystemTests`, with the re-golded suites holding their prior counts).

## Decisions

- **Tier is mechanical, Band is descriptive — the whole revision's load-bearing distinction.**
  Tier grants power (the Ascension additive baseline); Band only describes where in the tier a
  build sits. This is why Band could become a soft, auditable content tag with no gameplay
  consequence to protect, and why the two-axis change stayed confined to the oracle's
  *classification output*, never its input.
- **Output-only revision → INV-2 preserved by construction.** `Estimate`'s input side is
  untouched; only `Classify`/`TargetRange`/the anchor subdivision moved. The anti-grind proxy
  needed zero code changes.
- **Intra-tier partition, not overlap (Open question 1, resolved).** Within a tier, bands
  partition cleanly; the shipped `BandSpan` overlap is retained exclusively as tier-boundary
  hysteresis (a real Ascension mechanic), since intra-tier overlap would only make classification
  ambiguous with no descriptive payoff.
- **Calibration is oracle-estimation only (Open question 2, resolved).** Only the non-mirror knobs
  (`Weights`, `BandSpan`, `BandsPerTier`) moved; the mirror constants
  (`ReferenceBaseScores`/`MaxTier`/`TierBaselineStep`/`TrackedScores`) stayed locked to their
  domain sources. Real gameplay-power tuning is deferred to `prog-4`'s simulation harness — the
  near-blank-character-reads-Tier-4 symptom that motivated this slice is an admin-set
  `AscensionComponent.Tier` reading, not a formula bug.
- **Clean-break field split, no migration (resolved fork, 2026-07-07).** `TierBand` → `Tier`+`Band`
  discards/reinterprets today's authored tags; content is thin and pre-release. The break is
  lossy, not a clean wipe, because the new `band:` key reuses the old name.
- **Headless bulk-audit command deferred (Open question 3, resolved).** The shared `Audit()`
  method + Blazor Integrity report ship now; a telnet/admin verb is a thin later caller (INV-19
  satisfied) — `prog-4`'s sim is its natural first non-Blazor caller.
- **Shared projection seam built now, in the same PR as the field split (Open question 4,
  resolved).** The field split touches every projection site anyway, so consolidating the
  three-way hand-rolled snapshot builds into one item seam + one mob seam was near-free and
  prevents drift on the new two-field shape.
- **Band 0 is exclusively the authored "unbanded" sentinel (Open question 5, resolved).** Computed
  `Classify` never returns Band 0; the audit/mismatch flag skips authored-unbanded content for
  drift but still counts it in the bucket totals, so "how much content exists at power level X"
  stays complete even for untagged content.
- **Extensibility principle gets its own doc home (Open question 11, resolved).** `docs/design/
  power-model.md` states the snapshot-only rule once, distinct from `gameplay-model.md` §6's
  effect-`Power` — the same word, unrelated concepts.

## Deviations / Follow-ups

- **No deviations from the plan.** All three work packages (core oracle + calibration + design
  doc; field split + authoring parity + projection seam; band-drift audit + INV-20 tooling)
  shipped as scoped; every Test-plan item is present and green.
- **Follow-up (tracked, unchanged from `prog-3`'s record):** `prog-4` (simulation harness) depends
  on this slice landing — it validates against the revised two-axis oracle, not the superseded
  one-axis model. `prog-5` (agentic + balance-doc layer) follows, minus the INV-20 tooling piece
  this slice already pulled forward. Promoting `PowerBudgetConstants` to tunable YAML (OD-2) stays
  deferred until the sim drives heavy iteration. A headless/admin bulk-audit command and the
  player-facing `consider` danger-gauge remain deferred, decoupled thin consumers. Tracked in
  [`../backlog.md`](../backlog.md).
