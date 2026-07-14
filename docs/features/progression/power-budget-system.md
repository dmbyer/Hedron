# Power-budget system

> The core-tier, generic power-budget oracle: weighted-sum power estimation, a two-axis Tier×Band
> classification, the inverse target-range query, the designer-editable standards registry that
> backs its tunables, and the consumers that read it (admin inspectors, the Blazor editor readout,
> the bulk drift audit, the anti-grind proxy).
> **Authoring checkpoint:** slice prog-3 (one-axis), revised prog-3b (Tier×Band), tunables promoted
> to injected data + the balance-standards registry sim-1. Living document.

## What it is / does

**Core-tier** (INV-2). `PowerBudgetSystem` is a pure function taking exactly one constructor
dependency — the plain-data `PowerBudgetTunables` record, never a service or domain type (see
[`../../design/power-model.md`](../../design/power-model.md)): given a `PowerSnapshot`
(`ScoreId → int`, gathered by the caller — never an entity id, never an internal `IStatSystem`
call), it computes a weighted-sum power scalar and classifies it into a `PowerBand` (Tier×Band)
cell. It never touches the event bus or persistence (INV-5), and it imports no
`Core/Modules/<Feature>/` domain type — the load-bearing property that lets one function serve
every consumer without becoming a hub of domain knowledge. `PowerBudgetTunables` is composed by
the host from the **balance-standards registry** (sim-1) — a designer-editable YAML document with
compiled defaults as fallback — not hand-rolled per caller.

## How it works

**The snapshot input is what keeps the oracle core-generic and singular.** `IPowerBudgetSystem`
takes a `PowerSnapshot`, not an entity id, so callers gather scores *first* and hand in plain data:

- the `power <self>`/`<mob>` inspector reads `IStatSystem.Get` per score (domain-tier
  orchestration, in the command — folds gear/abilities/progression/tier);
- the `power <item>` inspector, `ItemEditor`, and `IBalanceAuditSystem` all read through the
  shared `IItemPowerProjectionSystem` seam (template or live `ItemDataComponent`, both keyed on
  `StatBonuses`); `MobEditor` and the audit read mob templates through `IMobPowerProjectionSystem`;
- the `ProgressionSystem` anti-grind proxy passes **raw** attributes.

Because the snapshot is generic data, INV-2 is satisfied structurally (the oracle imports no
domain module) and INV-19 is satisfied by construction (one function, many call sites, no drift).

**Power = weighted sum over a full table.** `Estimate(snapshot, tier)` is
`Σ (weight[score] × snapshot[score])` over `PowerBudgetTunables.Weights` — a full
`ScoreId → weight` table where combat-relevant scores (`Body`, `HpMax`, `AttackPower`, `Defense`)
carry meaningful positive weights and pools/current-value scores carry light-or-zero weights.
When `tier` is positive, it adds `weight[score] × (TierBaselineStep × tier)` for each of
`PowerBudgetTunables.TrackedScores` — the same additive baseline `AscensionEffectContributor`
folds into `IStatSystem.Get`, so a self/mob snapshot (which already includes the baseline via the
contributor) omits the tier argument, while an item/mob snapshot from *authored* data (no live
baseline) supplies its authored `Tier` as the tier argument to project "if this were built for
tier N." **This algorithm and signature are untouched by the prog-3b revision** — only the
classification/inverse surface below changed.

### Tier×Band classification (prog-3b) — Tier is mechanical, Band is descriptive

Tier grants power; Band only describes it. **Tier** (0–`MaxTier`) is the Ascension scalar that
confers the additive baseline through `AscensionEffectContributor` — a real mechanical input to
`IStatSystem.Get`. **Band** (1–3, low/mid/high) is a pure classification device, a
Challenge-Rating-style label answering "where in this tier does this build sit?" It grants no
power, feeds no contributor, and gates nothing mechanically.

`Classify(power)` returns a `PowerBand(Tier, Band)`:

- **Tier** is derived exactly as before: the highest tier whose anchor
  (`Estimate(ReferenceBaseScores, tier: N) − BandSpan`) is at or below `power`, floored to tier 0.
  The `BandSpan` overlap is retained **only** as this tier-boundary hysteresis — a maxed
  lower-tier build reaching into the next tier before formally ascending.
- **Band** then buckets the position *within* that tier's power span into thirds (low/mid/high,
  `BandsPerTier = 3`), partitioning — not overlapping — since Band is purely descriptive and
  intra-tier overlap would only make classification ambiguous with no semantic payoff. A power
  still inside the tier-boundary overlap (below the tier's own reference power) floors to band 1.
  Computed `Band` is never `0` — that value is exclusively the authored "unbanded" sentinel.

`TargetRange(tier, band)` is the inverse: given a cell, it returns the `PowerRange(MinPower,
MaxPower)` a designer should build toward. Bands partition cleanly — band 3's `MaxPower` abuts the
next tier's band-1 `MinPower` — so the ~21-cell table (7 tiers × 3 bands) has strictly increasing
floors end to end. The oracle never inverts the weighted sum back to a stat block; a designer
converges an actual build by iterating in the editor, whose readout already shows computed
power/band live. Stat-block synthesis from a target range is procedural-generation scope, not
built here.

### The oracle's tunables are injected plain data — mirror-checking moved to the domain-tier store (sim-1)

Before sim-1, `PowerBudgetConstants.ReferenceBaseScores` mirrored `CharacterDefaultsOptions`
(`Core/Modules/Account/`) and `PowerBudgetConstants.MaxTier`/`TierBaselineStep`/`TrackedScores`
mirrored `AscensionConstants` (`Core/Modules/Ascension/`) as co-located constants, with the sync
discipline enforced only by a doc comment. **Sim-1 promotes the whole constant class to
`PowerBudgetTunables`** — a plain-data record composed by `BalanceInspectionModule` from the
**balance-standards registry** (`IBalanceStandardsRegistry`, backed by a YAML document with
compiled defaults as fallback) and passed into `PowerBudgetSystem`'s single constructor parameter.
This is the one permitted constructor input under the snapshot-only principle — data, not a
service, so the oracle still imports no `Core/Modules/<Feature>/` domain type beyond the
allowlisted `Stats` (`ScoreId`), and the architecture-guard reflection check
(`ArchitectureGuardTests.PowerBudgetSystem_has_no_domain_module_dependency`) now asserts exactly
that shape: one ctor param of type `PowerBudgetTunables`, and no `Core/Modules/<Feature>/` import
across the oracle's six files (`PowerBudgetSystem`, `IPowerBudgetSystem`, `PowerBudgetTunables`,
`PowerSnapshot`, `PowerBand`, `PowerRange`) other than `Hedron.Core.Modules.Stats`.

**Mirror-sync becomes load-time validation, not a comment discipline.** The domain-tier
`BalanceStandardsStore` (which may legally import `Ascension`/`Account`, unlike the core-tier
oracle) compares the loaded document's `MaxTier`/`TierBaselineStep`/`TrackedScores` against
`AscensionConstants` and `ReferenceBaseScores` against `CharacterDefaultsOptions` (plus the base
`AttackPower = Body/2`/`Defense = Body/4` derivations) on every `Load()`/`SaveAsync()`, returning
one warning per drifted field — logged by the DI factory, never silently absorbed. The prog-3b
recalibration moved the **non-mirror** knobs (`Weights`, `BandSpan`, `BandsPerTier`); sim-1 leaves
the mirrored fields' *values* untouched (still locked to the real gameplay constants) but changes
*where* they live and *how* drift is caught. See [`../../design/power-model.md`](../../design/power-model.md)
for the amended extensibility rule and the [balance-standards-registry](../../roadmap/completed/balance-standards-registry.md)
history for the promotion itself.

### Consumers, one function each (INV-19)

1. **`power [target]` / `powerband [tier]`** (`Core/Modules/BalanceInspection/Commands/`,
   admin/designer-gated like `defs`) — an in-game spot-check. `power` resolves a runtime-in-world
   target (self, item in inventory/room, or mob in room) to a snapshot and prints the computed
   power + `(Tier, Band)`, echoing the authored `(Tier, Band)` for a tagged target. `powerband`
   lists every `(Tier, Band)` cell's target range (~21 rows), or just one tier's three.
2. **The Blazor `ItemEditor`/`MobEditor` readout** — the **primary designer observability
   surface**. Both editors build a snapshot through the shared projection seam, show computed
   power and `(Tier, Band)`, and flag a mismatch when the authored/computed band-index drift
   exceeds `IBalanceStandardsRegistry.BandDriftTolerance` (injected `@inject`; the tolerance and
   `GlobalBandIndex` math moved off the retired `BalanceAuditConstants` onto the registry/
   `PowerBudgetTunables` in sim-1).
3. **`IBalanceAuditSystem.Audit()`** (`Core/Modules/BalanceInspection/Systems/`) — the bulk sweep
   consumed by the Blazor Integrity page: enumerates every item/mob template, projects and
   classifies each, and returns every content past the drift tolerance plus a bucket count by
   computed `(Tier, Band)` — a free "how much content exists at power level X" report. Soft and
   advisory only; never a build/reload/CI gate. Authored `Band = 0` (unbanded) is excluded from the
   drift list but still bucketed.
4. **`ProgressionSystem.GetEffectivePower`** (the anti-grind proxy) — builds a raw-attribute
   snapshot (`Mind`/`Body`/`Spirit`/`Attunement`, never `IStatSystem.Get`) and calls `Estimate`
   with no tier. See [`progression-system.md`](progression-system.md#anti-grind-proxy-reads-raw-attributes)
   for the DI-cycle guard this preserves. Unaffected by the prog-3b classify/inverse revision —
   only `Estimate`'s *raw output* shifted with the recalibrated `Weights`, and the anti-grind
   *ratio* is invariant to that rescaling.

A future player-facing `consider` danger-gauge (self-vs-target `Estimate`/`Classify` → a coarse
diegetic label, no raw numbers) remains a **deferred, decoupled** consumer — the public interface
already suffices with no interface change.

## Interface

- [`IPowerBudgetSystem.cs`](../../../Core/Systems/IPowerBudgetSystem.cs) —
  `Estimate(PowerSnapshot, int tier = 0)`, `Classify(int power) → PowerBand`,
  `TargetRange(int tier, int band) → PowerRange`, `BandAnchor(int tier)`. Publishes nothing; takes
  exactly one constructor dependency, `PowerBudgetTunables`.
- [`PowerBudgetTunables.cs`](../../../Core/Systems/PowerBudgetTunables.cs) — the plain-data record:
  `Weights`, `BandSpan`, `BandsPerTier`, `ReferenceBaseScores`, `MaxTier`, `TierBaselineStep`,
  `TrackedScores`, plus `GlobalBandIndex(tier, band)`; `Default` is the compiled fallback. Replaces
  the former `PowerBudgetConstants`.
- [`PowerSnapshot.cs`](../../../Core/Systems/PowerSnapshot.cs) — the `ScoreId → int` input wrapper.
- [`PowerBand.cs`](../../../Core/Systems/PowerBand.cs) /
  [`PowerRange.cs`](../../../Core/Systems/PowerRange.cs) — the two-axis result types.
- [`IItemPowerProjectionSystem.cs`](../../../Core/Modules/Items/Systems/IItemPowerProjectionSystem.cs) /
  [`IMobPowerProjectionSystem.cs`](../../../Core/Modules/Mobs/Systems/IMobPowerProjectionSystem.cs) —
  the shared template/component → `PowerSnapshot` seams.
- [`IBalanceAuditSystem.cs`](../../../Core/Modules/BalanceInspection/Systems/IBalanceAuditSystem.cs) /
  [`BalanceAuditReport.cs`](../../../Core/Modules/BalanceInspection/BalanceAuditReport.cs) —
  the bulk drift sweep and its report shape; tolerance/index math now sourced from
  `PowerBudgetTunables`/`IBalanceStandardsRegistry` (constructor-injected), not a standalone
  constants class.
- [`IBalanceStandardsStore.cs`](../../../Core/Modules/BalanceInspection/Standards/IBalanceStandardsStore.cs) /
  [`BalanceStandardsStore.cs`](../../../Core/Modules/BalanceInspection/Standards/BalanceStandardsStore.cs) /
  [`IBalanceStandardsRegistry.cs`](../../../Core/Modules/BalanceInspection/Standards/IBalanceStandardsRegistry.cs) /
  [`BalanceStandardsRegistry.cs`](../../../Core/Modules/BalanceInspection/Standards/BalanceStandardsRegistry.cs) /
  [`BalanceStandardsDocument.cs`](../../../Core/Modules/BalanceInspection/Standards/BalanceStandardsDocument.cs) /
  [`BalanceStandardsDefaults.cs`](../../../Core/Modules/BalanceInspection/Standards/BalanceStandardsDefaults.cs) —
  the sim-1 standards registry: YAML load/validate/save (store), the dense-filled (Tier, Band)
  table + `Tunables`/`BandDriftTolerance`/`OutcomesFor`/`ReferenceSnapshot` (registry), and the
  document/defaults data shapes.
- [`BalanceOptions.cs`](../../../Core/Modules/BalanceInspection/BalanceOptions.cs) —
  `Balance:StandardsPath` (Category 1 config key, default `data/balance/standards.yaml`).
- [`BalanceInspectionModule.cs`](../../../Core/Modules/BalanceInspection/BalanceInspectionModule.cs) —
  registers the standards store/registry (load-once factory, warning logging), projects
  `PowerBudgetTunables` from the registry, then the oracle, the audit system, and the two
  inspector commands; called from `Server/CompositionRoot.Register` (not `Program.cs`) so
  `Hedron.Web` can resolve them for the editor readout, the Standards page, and the Integrity page.

## Considerations

- **Determinism (INV-26):** the power math is a pure weighted sum with no chance or wall-clock —
  no `IRandom`/`IClock` seam needed, stated explicitly and checked by a golden-number test.
- **Persistence:** the oracle, inspectors, and audit perform no persistence; `setitem`/`setmob`
  `tier`/`band` are world-content admin commands (YAML write only, no `SaveEntityAsync`), matching
  every existing `setitem`/`setmob` branch. The standards document is likewise YAML-side only —
  no SQLite, no entities.
- **Registration:** `BalanceInspectionModule.AddBalanceInspectionModule` — see Interface above.
- **Restart-to-apply (sim-1):** the oracle's ctor-injection means a saved standards edit takes
  effect on the **next host start**, not immediately — both hosts' `RegistryValidationBootstrap`
  eagerly resolves `IBalanceStandardsRegistry` at boot (fail-fast on structural violations); the
  Standards page states the restart requirement. Live-reload is a backlog entry, not built here.
- **Acknowledged debt:** the player-facing `consider` danger-gauge is deferred (backlog); the
  sim-2 simulation engine (`balance-simulator.md` program) is the standards registry's next
  consumer (expected-vs-actual outcome comparisons against the now-data-backed outcome
  tolerances); a headless/admin bulk-audit command is deferred (the Blazor Integrity report is the
  primary surface; `Audit()` being one shared method makes a later command a thin caller); the
  standards store's hand-rolled YAML load/validate/save path is acknowledged debt against a future
  "YAML-authored definition pipeline for registry families" generalization (≥3-instance trigger,
  backlogged).

## Related

- [`../../design/power-model.md`](../../design/power-model.md) — the named snapshot-only
  extensibility principle this oracle follows (amended sim-1 to permit the single injected
  `PowerBudgetTunables` record), distinct from `gameplay-model.md` §6's effect-`Power`.
- Flow: [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md) —
  the anti-grind consumer's trigger path. [flow-01 — Server startup](../../architecture/flows/flow-01-server-startup.md) —
  the standards-load boot step (sim-1). [flow-29 — Content-tooling journey](../../architecture/flows/flow-29-bulk-content-generation.md) —
  the Standards page's offline-edit leg (sim-1). No dedicated flow file for `power`/`powerband`/the
  audit themselves — they plug into the existing
  [flow-03 command journey](../../architecture/flows/flow-03-player-command-lifecycle.md).
- Reference rows: [`systems.md`](../../reference/systems.md), [`commands.md`](../../reference/commands.md),
  [`components.md`](../../reference/components.md) (`ItemDataComponent`/`MobDataComponent` `Tier`/`Band`).
- [`progression-system.md`](progression-system.md) — the anti-grind proxy this oracle now backs,
  and the DI-cycle guard precedent.
- [`ascension-system.md`](ascension-system.md) — the tier baseline and mob `Tier`/`Band` tags this
  oracle's tiers are derived from and item tags mirror.
- [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md) — OD-2,
  resolved for the power-budget/balance-standards family by sim-1.
- [`../admin-authoring/content-authoring.md`](../admin-authoring/content-authoring.md) — the
  Standards page alongside the catalog-backed editors it mirrors in posture (validate-then-write,
  refuse-vs-warn, atomic write) without sharing its machinery.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-2 (core-tier, single
  plain-data ctor input permitted), INV-18 (sim-1's standards YAML ships with the Standards page in
  the same slice), INV-19 (framework at ≥3 consumers), INV-24 (contribute-on-read, unaffected by
  this oracle since it never registers as an `IEffectContributor`).
- [`../../roadmap/completed/power-budget-inspector.md`](../../roadmap/completed/power-budget-inspector.md) —
  the one-axis slice-3 history, including the INV-2 deviation the code-review gate caught.
- [`../../roadmap/completed/power-model-revision.md`](../../roadmap/completed/power-model-revision.md) —
  the prog-3b Tier×Band revision history.
- [`../../roadmap/completed/balance-standards-registry.md`](../../roadmap/completed/balance-standards-registry.md) —
  the sim-1 tunables-as-injected-data promotion + standards registry history.
