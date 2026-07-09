# Power-budget system

> The core-tier, generic power-budget oracle: weighted-sum power estimation, a two-axis Tier×Band
> classification, the inverse target-range query, and the consumers that read it (admin
> inspectors, the Blazor editor readout, the bulk drift audit, the anti-grind proxy).
> **Authoring checkpoint:** slice prog-3 (one-axis), revised prog-3b (Tier×Band). Living document.

## What it is / does

**Core-tier** (INV-2). `PowerBudgetSystem` is a pure, dependency-free function: given a
`PowerSnapshot` (`ScoreId → int`, gathered by the caller — never an entity id, never an internal
`IStatSystem` call), it computes a weighted-sum power scalar and classifies it into a `PowerBand`
(Tier×Band) cell. It never touches the event bus or persistence (INV-5), and it imports no
`Core/Modules/<Feature>/` domain type — the load-bearing property that lets one function serve
every consumer without becoming a hub of domain knowledge. See
[`../../design/power-model.md`](../../design/power-model.md) for the named snapshot-only
extensibility principle this oracle is built to never violate.

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
`Σ (weight[score] × snapshot[score])` over `PowerBudgetConstants.Weights` — a full
`ScoreId → weight` table where combat-relevant scores (`Body`, `HpMax`, `AttackPower`, `Defense`)
carry meaningful positive weights and pools/current-value scores carry light-or-zero weights.
When `tier` is positive, it adds `weight[score] × (TierBaselineStep × tier)` for each of
`PowerBudgetConstants.TrackedScores` — the same additive baseline `AscensionEffectContributor`
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

### The oracle mirrors domain constants as core-tier constants — it does not import the domain modules that own them

`PowerBudgetConstants.ReferenceBaseScores` mirrors `CharacterDefaultsOptions`
(`Core/Modules/Account/`); `PowerBudgetConstants.MaxTier`/`TierBaselineStep`/`TrackedScores` mirror
`AscensionConstants` (`Core/Modules/Ascension/`). Both are held as co-located constants — never an
injected `IOptions<CharacterDefaultsOptions>`, never a `using Hedron.Core.Modules.Ascension`
import — because a `Core/Systems/` type depending on either would violate INV-2 and fail the
architecture-guard reflection check (`ArchitectureGuardTests.PowerBudgetSystem_has_no_domain_module_dependency`,
which flags any `Core/Modules/<Feature>/` import in the oracle's six files — `PowerBudgetSystem`,
`IPowerBudgetSystem`, `PowerBudgetConstants`, `PowerSnapshot`, `PowerBand`, `PowerRange` — other
than `Hedron.Core.Modules.Stats`, the `ScoreId` vocabulary). Each mirrored constant is documented
"keep in sync with `<Source>`." The prog-3b recalibration only moved the **non-mirror** knobs
(`Weights`, `BandSpan`, `BandsPerTier`) — the mirrored constants stayed locked, and real
gameplay-power tuning (`AscensionConstants` itself) is deferred to `prog-4`'s simulation harness.

### Consumers, one function each (INV-19)

1. **`power [target]` / `powerband [tier]`** (`Core/Modules/BalanceInspection/Commands/`,
   admin/designer-gated like `defs`) — an in-game spot-check. `power` resolves a runtime-in-world
   target (self, item in inventory/room, or mob in room) to a snapshot and prints the computed
   power + `(Tier, Band)`, echoing the authored `(Tier, Band)` for a tagged target. `powerband`
   lists every `(Tier, Band)` cell's target range (~21 rows), or just one tier's three.
2. **The Blazor `ItemEditor`/`MobEditor` readout** — the **primary designer observability
   surface**. Both editors build a snapshot through the shared projection seam, show computed
   power and `(Tier, Band)`, and flag a mismatch when the authored/computed band-index drift
   exceeds `BalanceAuditConstants.BandDriftTolerance` (upgraded from exact-match).
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
  zero constructor dependencies.
- [`PowerBudgetConstants.cs`](../../../Core/Systems/PowerBudgetConstants.cs) — `Weights`,
  `BandSpan`, `BandsPerTier`, `ReferenceBaseScores`, and the mirrored
  `MaxTier`/`TierBaselineStep`/`TrackedScores`.
- [`PowerSnapshot.cs`](../../../Core/Systems/PowerSnapshot.cs) — the `ScoreId → int` input wrapper.
- [`PowerBand.cs`](../../../Core/Systems/PowerBand.cs) /
  [`PowerRange.cs`](../../../Core/Systems/PowerRange.cs) — the two-axis result types.
- [`IItemPowerProjectionSystem.cs`](../../../Core/Modules/Items/Systems/IItemPowerProjectionSystem.cs) /
  [`IMobPowerProjectionSystem.cs`](../../../Core/Modules/Mobs/Systems/IMobPowerProjectionSystem.cs) —
  the shared template/component → `PowerSnapshot` seams.
- [`IBalanceAuditSystem.cs`](../../../Core/Modules/BalanceInspection/Systems/IBalanceAuditSystem.cs) /
  [`BalanceAuditReport.cs`](../../../Core/Modules/BalanceInspection/BalanceAuditReport.cs) /
  [`BalanceAuditConstants.cs`](../../../Core/Modules/BalanceInspection/BalanceAuditConstants.cs) —
  the bulk drift sweep, its report shape, and the shared tolerance/index math.
- [`BalanceInspectionModule.cs`](../../../Core/Modules/BalanceInspection/BalanceInspectionModule.cs) —
  registers the oracle, the audit system, and the two inspector commands; called from
  `Server/CompositionRoot.Register` (not `Program.cs`) so `Hedron.Web` can resolve them for the
  editor readout and the Integrity page.

## Considerations

- **Determinism (INV-26):** the power math is a pure weighted sum with no chance or wall-clock —
  no `IRandom`/`IClock` seam needed, stated explicitly and checked by a golden-number test.
- **Persistence:** the oracle, inspectors, and audit perform no persistence; `setitem`/`setmob`
  `tier`/`band` are world-content admin commands (YAML write only, no `SaveEntityAsync`), matching
  every existing `setitem`/`setmob` branch.
- **Registration:** `BalanceInspectionModule.AddBalanceInspectionModule` — see Interface above.
- **Acknowledged debt:** the player-facing `consider` danger-gauge is deferred (backlog); the
  `prog-4` simulation harness is the oracle's next consumer (expected-vs-actual outcome
  comparisons) and the likely trigger for promoting `PowerBudgetConstants` to tunable YAML (OD-2);
  a headless/admin bulk-audit command is deferred (the Blazor Integrity report is the primary
  surface; `Audit()` being one shared method makes a later command a thin caller).

## Related

- [`../../design/power-model.md`](../../design/power-model.md) — the named snapshot-only
  extensibility principle this oracle follows, distinct from `gameplay-model.md` §6's effect-`Power`.
- Flow: [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md) —
  the anti-grind consumer's trigger path. No dedicated flow file for `power`/`powerband`/the audit —
  they plug into the existing [flow-03 command journey](../../architecture/flows/flow-03-player-command-lifecycle.md)
  and the content-tooling surface with no structural change.
- Reference rows: [`systems.md`](../../reference/systems.md), [`commands.md`](../../reference/commands.md),
  [`components.md`](../../reference/components.md) (`ItemDataComponent`/`MobDataComponent` `Tier`/`Band`).
- [`progression-system.md`](progression-system.md) — the anti-grind proxy this oracle now backs,
  and the DI-cycle guard precedent.
- [`ascension-system.md`](ascension-system.md) — the tier baseline and mob `Tier`/`Band` tags this
  oracle's tiers are derived from and item tags mirror.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-2 (core-tier, no
  domain import), INV-19 (framework at ≥3 consumers), INV-24 (contribute-on-read, unaffected by
  this oracle since it never registers as an `IEffectContributor`).
- [`../../roadmap/completed/power-budget-inspector.md`](../../roadmap/completed/power-budget-inspector.md) —
  the one-axis slice-3 history, including the INV-2 deviation the code-review gate caught.
- [`../../roadmap/completed/power-model-revision.md`](../../roadmap/completed/power-model-revision.md) —
  the prog-3b Tier×Band revision history.
