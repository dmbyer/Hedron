# Power-budget system

> The core-tier, generic power-budget oracle: weighted-sum power estimation, derived tier bands, and the three consumers that read it (admin inspectors, the Blazor editor readout, the anti-grind proxy). **Authoring checkpoint:** slice prog-3. Living document.

> **⚠️ Revision pending (slice `prog-3b`, not yet planned).** `Classify` below returns a single 0–6 number; a post-merge design conversation established that Tier (0–6, character-wide, Ascension-gated) and Band (a finer 1–3 low/mid/high subdivision *within* each tier — a D&D-Challenge-Rating-style model) are two distinct axes this slice conflated into one. `prog-3b` will change `Classify`/`BandAnchor` to return `(Tier, Band)`, add an inverse target-range query, split the authored `TierBand` content field into a `Tier`+`Band` pair, add band-count-tolerance drift auditing, and recalibrate the weights/anchors below (documented as placeholder from the start — see the golden numbers in `PowerBudgetSystemTests`). See [`../../implementation-plans/progression-and-balance.md`](../../implementation-plans/progression-and-balance.md) Open questions 7–12 and [`../../roadmap/completed/power-budget-inspector.md`](../../roadmap/completed/power-budget-inspector.md)'s Deviations/Follow-ups. Nothing below is wrong, but the "band" language throughout should be read as **v1, single-axis** until `3b` lands.

## What it is / does

**Core-tier** (INV-2). `PowerBudgetSystem` is a pure, dependency-free function: given a `PowerSnapshot` (`ScoreId → int`, gathered by the caller — never an entity id, never an internal `IStatSystem` call), it computes a weighted-sum power scalar and classifies it into a tier band. It never touches the event bus or persistence (INV-5), and it imports no `Core/Modules/<Feature>/` domain type — the load-bearing property that lets one function serve every consumer without becoming a hub of domain knowledge.

## How it works

**The snapshot input is what keeps the oracle core-generic and singular.** `IPowerBudgetSystem` takes a `PowerSnapshot`, not an entity id, so callers gather scores *first* and hand in plain data:

- the `power <self>`/`<mob>` inspector reads `IStatSystem.Get` per score (domain-tier orchestration, in the command — folds gear/abilities/progression/tier);
- the `power <item>` inspector and the Blazor `ItemEditor`/`MobEditor` readouts read a template's authored bonuses/scores directly (no live entity exists in the editor);
- the `ProgressionSystem` anti-grind proxy passes **raw** attributes.

Because the snapshot is generic data, INV-2 is satisfied structurally (the oracle imports no domain module) and INV-19 is satisfied by construction (one function, many call sites, no drift).

**Power = weighted sum over a full table; bands = derived from a reference build.** `Estimate(snapshot, tier)` is `Σ (weight[score] × snapshot[score])` over `PowerBudgetConstants.Weights` — a full `ScoreId → weight` table where combat-relevant scores (`Body`, `HpMax`, `AttackPower`, `Defense`) carry meaningful positive weights and pools/current-value scores carry light-or-zero weights. When `tier` is positive, it adds `weight[score] × (TierBaselineStep × tier)` for each of `PowerBudgetConstants.TrackedScores` — the same additive baseline `AscensionEffectContributor` folds into `IStatSystem.Get`, so a self/mob snapshot (which already includes the baseline via the contributor) omits the tier argument, while an item/mob snapshot from *authored* data (no live baseline) supplies its `TierBand` as the tier argument to project "if this were built for tier N."

Tier bands (`0`–`6`) are **derived, not hand-authored**: each band is anchored at `Estimate(ReferenceBaseScores, tier: N) − BandSpan`, where `ReferenceBaseScores` mirrors the canonical new-character starting stat block and `BandSpan` is the deliberate overlap width (a maxed lower-tier build can reach into the next band before formally ascending — the same Ascension overlap semantics prog-2 established). `Classify(power)` returns the highest band whose anchor is at or below `power`, floored to band 0.

### The oracle mirrors domain constants as core-tier constants — it does not import the domain modules that own them

`PowerBudgetConstants.ReferenceBaseScores` mirrors `CharacterDefaultsOptions` (`Core/Modules/Account/`); `PowerBudgetConstants.MaxTier`/`TierBaselineStep`/`TrackedScores` mirror `AscensionConstants` (`Core/Modules/Ascension/`). Both are held as co-located constants — never an injected `IOptions<CharacterDefaultsOptions>`, never a `using Hedron.Core.Modules.Ascension` import — because a `Core/Systems/` type depending on either would violate INV-2 and fail the architecture-guard reflection check (`ArchitectureGuardTests.PowerBudgetSystem_has_no_domain_module_dependency`, which flags any `Core/Modules/<Feature>/` import in the oracle's four files other than `Hedron.Core.Modules.Stats`, the `ScoreId` vocabulary). Each mirrored constant is documented "keep in sync with `<Source>`." **This was the one deviation the code-review gate caught**: the plan's own Postconditions specified reading `AscensionConstants` directly, which the reviewer correctly flagged as violating the very rule the plan applied to `CharacterDefaultsOptions` — fixed by applying the identical mirroring pattern uniformly. See [`../../roadmap/completed/power-budget-inspector.md`](../../roadmap/completed/power-budget-inspector.md) for the full account.

### Three consumers, one function (INV-19)

1. **`power [target]` / `powerband [tier]`** (`Core/Modules/BalanceInspection/Commands/`, admin/designer-gated like `defs`) — an in-game spot-check. `power` resolves a runtime-in-world target (self, item in inventory/room, or mob in room) to a snapshot and prints the computed power + band, echoing the authored band for a banded target. `powerband` lists the band anchors or inspects one tier.
2. **The Blazor `ItemEditor`/`MobEditor` readout** — the **primary designer observability surface**. Both editors build a snapshot from the template's authored data (item `StatBonuses`; mob authored attributes/pools + derived `AttackPower`/`Defense`) and show computed power, computed band, and an authored-vs-computed mismatch flag.
3. **`ProgressionSystem.GetEffectivePower`** (the anti-grind proxy) — builds a raw-attribute snapshot (`Mind`/`Body`/`Spirit`/`Attunement`, never `IStatSystem.Get`) and calls `Estimate` with no tier. See [`progression-system.md`](progression-system.md#anti-grind-proxy-reads-raw-attributes) for the DI-cycle guard this preserves.

A future player-facing `consider` danger-gauge (self-vs-target `Estimate`/`Classify` → a coarse diegetic label, no raw numbers) is a **deferred, decoupled** 4th consumer — the public interface already suffices with no interface change, so the capability exists without building it now.

## Interface

- [`IPowerBudgetSystem.cs`](../../../Core/Systems/IPowerBudgetSystem.cs) — `Estimate(PowerSnapshot, int tier = 0)`, `Classify(int power)`, `BandAnchor(int tier)`. Publishes nothing; takes zero constructor dependencies.
- [`PowerBudgetConstants.cs`](../../../Core/Systems/PowerBudgetConstants.cs) — `Weights`, `BandSpan`, `ReferenceBaseScores`, and the mirrored `MaxTier`/`TierBaselineStep`/`TrackedScores`.
- [`PowerSnapshot.cs`](../../../Core/Systems/PowerSnapshot.cs) — the `ScoreId → int` input wrapper.
- [`BalanceInspectionModule.cs`](../../../Core/Modules/BalanceInspection/BalanceInspectionModule.cs) — registers the oracle and the two inspector commands; called from `Server/CompositionRoot.Register` (not `Program.cs`) so `Hedron.Web` can resolve the oracle for the editor readout.

## Considerations

- **Determinism (INV-26):** the power math is a pure weighted sum with no chance or wall-clock — no `IRandom`/`IClock` seam needed, stated explicitly and checked by a golden-number test.
- **Persistence:** the oracle and inspectors perform no persistence; `setitem band` is a world-content admin command (YAML write only, no `SaveEntityAsync`), matching every existing `setitem` branch.
- **Registration:** `BalanceInspectionModule.AddBalanceInspectionModule` — see Interface above.
- **Acknowledged debt:** the player-facing `consider` danger-gauge is deferred (backlog); the `prog-4` simulation harness is the oracle's 4th consumer (expected-vs-actual outcome comparisons) and the likely trigger for promoting `PowerBudgetConstants` to tunable YAML (OD-2).

## Related

- Flow: [flow-31 — Progression journey](../../architecture/flows/flow-31-progression-award.md) — the anti-grind consumer's trigger path. No dedicated flow file for `power`/`powerband` — they plug into the existing [flow-03 command journey](../../architecture/flows/flow-03-player-command-lifecycle.md) with no structural change.
- Reference rows: [`systems.md`](../../reference/systems.md), [`commands.md`](../../reference/commands.md), [`components.md`](../../reference/components.md) (`ItemDataComponent.TierBand`).
- [`progression-system.md`](progression-system.md) — the anti-grind proxy this oracle now backs, and the DI-cycle guard precedent.
- [`ascension-system.md`](ascension-system.md) — the tier baseline and mob `TierBand` tag this oracle's bands are derived from and item bands mirror.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-2 (core-tier, no domain import), INV-19 (framework at 3 consumers), INV-24 (contribute-on-read, unaffected by this oracle since it never registers as an `IEffectContributor`).
- [`../../roadmap/completed/power-budget-inspector.md`](../../roadmap/completed/power-budget-inspector.md) — as-built history, including the INV-2 deviation the code-review gate caught and fixed.
