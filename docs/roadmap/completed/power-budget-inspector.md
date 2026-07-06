# Power model + balance inspector — slice prog-3 (completed)

> Implemented on branch `claude/happy-almeida-002c5d`, 2026-07-06. Living docs: [`features/progression/progression.md`](../../features/progression/progression.md) · [`features/progression/progression-system.md`](../../features/progression/progression-system.md).

## Outcome

A core-tier, generic power-budget oracle (`IPowerBudgetSystem`) landed at `Core/Systems/`: given a caller-supplied score snapshot (never an entity id, never an internal `IStatSystem` call), it computes a weighted-sum power scalar and classifies it into a derived tier band (`0`–`6`). Three live consumers land in this slice: admin-gated in-game `power`/`powerband` inspectors, a computed power/band readout on the Blazor `ItemEditor`/`MobEditor` (the primary designer observability surface), and the `ProgressionSystem` anti-grind proxy, rewired off its inline attribute sum onto the oracle. The slice also ships an authored item tier-band tag (`ItemDataComponent.TierBand`, `setitem band`, YAML round-trip), mirroring the mob band tag prog-2 shipped. This is slice 3 of the five-slice Progression & Balance program; slices 4–5 (simulation harness, agentic/balance-doc layer) remain unbuilt.

## Behavior digest

- **Weighted-sum power (P1).** `Estimate(snapshot, tier)` returns `Σ (weight[score] × snapshot[score])` over `PowerBudgetConstants.Weights`, for exactly the scores present in the snapshot; an unweighted or absent score contributes 0. Pure function of inputs — no `IRandom`/`IClock` seam needed (INV-26).
- **Combat scores dominate (P2).** `Body`, `HpMax`, `AttackPower`, `Defense` carry positive weights that exceed every pool/current-value score's weight (which is 0).
- **Tier baseline (P3).** `Estimate(snapshot, tier)` adds `weight[score] × (TierBaselineStep × tier)` for each of `PowerBudgetConstants.TrackedScores`; `tier = 0` (or omitted) adds nothing.
- **Derived bands, not authored ranges (P4).** `Classify(power)` returns the highest band `b ∈ [0, MaxTier]` whose anchor (`Estimate(ReferenceBaseScores, tier: b) − BandSpan`) is at or below `power`; bands overlap by `BandSpan` (Ascension semantics — a maxed lower tier can reach into the next band before ascending) and floor to band 0 below the lowest anchor.
- **`power <target>` (admin-gated, P5).** Resolves a runtime-in-world target — self (default), item in inventory/room, or mob in room — to a snapshot, then prints the computed power + band, echoing the authored band for a banded target. Blueprint/template resolution is deferred to the Blazor readout.
- **`powerband [tier]` (admin-gated, P6).** No argument lists every band 0–6 with its anchor; a tier argument prints that band's anchor and the reference build's power at that tier.
- **Item tier-band round-trips (P7).** `ItemDataComponent.TierBand`/`ItemTemplate.TierBand` (`0`–`6`, `0` = unbanded) dual-write via `IItemBuilderSystem.SetItemBand`; `setitem band` range-validates at the edge; YAML `band:` round-trips losslessly, warn-and-default on out-of-range/negative — mirrors the mob band chain exactly.
- **Blazor readout (P8).** `ItemEditor`/`MobEditor` each show `Estimate`/`Classify` computed from the template's authored data (never `IStatSystem.Get` — no live entity exists in the editor) plus an authored-vs-computed band mismatch flag.
- **Anti-grind rewire (P9).** `ProgressionSystem.GetEffectivePower` builds a raw-attribute `PowerSnapshot` (`Mind`/`Body`/`Spirit`/`Attunement` off `AttributesComponent`) and calls `IPowerBudgetSystem.Estimate(snapshot)` (no tier — no DI cycle); the anti-grind ratio is unchanged (scale-invariant under a shared weight table) — all three pre-existing equivalence cases hold unmodified.
- **No new events (P10).** The oracle and inspectors are pure read tools; `setitem band` reuses the existing `ItemPropertySetByAdminEvent`.

## Shipped pieces

| Surface | Location |
|---|---|
| `IPowerBudgetSystem` / `PowerBudgetSystem` (core-tier, zero dependencies) | `Core/Systems/IPowerBudgetSystem.cs` · `PowerBudgetSystem.cs` |
| `PowerBudgetConstants` (Weights, BandSpan, ReferenceBaseScores, and mirrored `MaxTier`/`TierBaselineStep`/`TrackedScores`) | `Core/Systems/PowerBudgetConstants.cs` |
| `PowerSnapshot` | `Core/Systems/PowerSnapshot.cs` |
| `BalanceInspectionModule.AddBalanceInspectionModule` | `Core/Modules/BalanceInspection/BalanceInspectionModule.cs` — registered in `Server/CompositionRoot.cs` (not `Program.cs`, so `Hedron.Web` can resolve the oracle) |
| `PowerCommand` (`power`, admin-gated) | `Core/Modules/BalanceInspection/Commands/PowerCommand.cs` |
| `PowerbandCommand` (`powerband`, admin-gated) | `Core/Modules/BalanceInspection/Commands/PowerbandCommand.cs` |
| `PowerReadoutMessage` / `PowerbandMessage` + `TelnetOutputFormatter` formatting | `Core/Output/PowerReadoutMessage.cs` · `PowerbandMessage.cs` · `TelnetOutputFormatter.cs` |
| `ItemDataComponent.TierBand` / `ItemTemplate.TierBand` | `Core/ECS/Components/ItemDataComponent.cs` · `Core/Modules/Items/Templates/ItemTemplate.cs` |
| `IItemBuilderSystem.SetItemBand` / `ItemBuilderSystem.SetItemBand` | `Core/Modules/Items/Systems/IItemBuilderSystem.cs` · `ItemBuilderSystem.cs` |
| `SetitemCommand` `band` branch | `Core/Modules/Items/Commands/SetitemCommand.cs` |
| `ItemContentWriter` / `ItemTemplateDeserializer` `band:` YAML field | `Core/Modules/Items/Systems/ItemContentWriter.cs` · `Core/Modules/Items/ItemTemplateDeserializer.cs` |
| `ProgressionSystem.GetEffectivePower` rewire | `Core/Modules/Progression/Systems/ProgressionSystem.cs` |
| Blazor `ItemEditor` band field + computed power/band readout | `Hedron.Web/Components/Pages/ItemEditor.razor` |
| Blazor `MobEditor` computed power/band readout | `Hedron.Web/Components/Pages/MobEditor.razor` |
| `.warning-inline` style | `Hedron.Web/wwwroot/app.css` |

## Tests shipped

- **Tier 1** — `PowerBudgetSystemTests` (`Hedron.Tests/Modules/BalanceInspection/`): weighted-sum math (empty/unweighted/mixed snapshots), tier-baseline addition, weight-table sanity (combat scores exceed pool weights), golden-number band derivation (reference build → band 0, tier-N anchor → band N, overlap → higher band, floor below lowest anchor), `BandAnchor` formula equivalence, strictly-increasing anchors.
- **Tier 2** — `PowerCommandTests` (self/item/mob resolution with a stub `IStatSystem`, golden numbers end-to-end, unresolved-target message, admin-gate declaration); `PowerbandCommandTests` (list-all vs. single-tier, invalid-tier rejection, admin-gate declaration); `SetitemCommandBandTests` (dual-write + one audit event, out-of-range/negative rejection with no mutation, mirrors `SetMobCommandBandTests`); `ItemBuilderSystemTests.SetItemBand` additions.
- **Anti-grind equivalence (on-touch ratchet)** — the three pre-existing `ProgressionSystemTests` cases (floor, peer, cap) re-verified unmodified against the rewired backend, plus a new regression pinning that worn gear does not inflate the raw-attribute snapshot (the DI-cycle guard, as a test).
- **Tier 4** — `ItemTierBandRoundTripTests` (`Hedron.Tests/Items/`, mirroring `MobTierBandRoundTripTests`): write→YAML→read, zero/absent-key round-trip, out-of-range/negative logged-and-defaulted, `Apply` seeding, plus a SQLite persistence round-trip for a player-owned item's `TierBand`.
- **Tier 5** — `ArchitectureGuardTests.PowerBudgetSystem_has_no_domain_module_dependency` (new): asserts `PowerBudgetSystem` takes zero constructor parameters and that none of the four `Core/Systems/PowerBudget*`/`PowerSnapshot.cs` files import any `Core/Modules/<Feature>/` namespace other than `Hedron.Core.Modules.Stats` (the `ScoreId` vocabulary) — a general guard, not just an `Account`-specific check. DI-smoke resolves the new registrations.
- `dotnet build` and `dotnet test` green — 1008 tests total (up from 982 pre-slice).

## Decisions

- **Snapshot input, never an entity id (brief OQ6 → resolved: stays core).** `IPowerBudgetSystem` takes a `PowerSnapshot` so the same one function serves the inspector (`IStatSystem.Get` per score), the item/mob Blazor readout (template's authored data), and the anti-grind proxy (raw attributes) — no domain dependency, structurally satisfying INV-2 and INV-19 by construction.
- **Bands derived from a constant reference build, not hand-authored (resolved Q1).** `PowerBudgetConstants.ReferenceBaseScores` mirrors `CharacterDefaultsOptions` as co-located constants rather than an injected `IOptions<CharacterDefaultsOptions>`, keeping the core oracle free of the domain `Account` module.
- **`power <target>` resolver scope → runtime-in-world only (resolved Q2).** self/item-in-inventory-or-room/mob-in-room; blueprint-id/template resolution deferred to the Blazor readout, which the owner confirmed is where most balance-observability value lives.
- **`power`/`powerband` admin/designer-gated, not player-facing (resolved Q3).** Raw balance internals (power scalars, band anchors) stay out of players' hands; a future player-facing `consider` danger-gauge is a deferred, decoupled thin consumer of the same `Estimate`/`Classify` surface — no interface change needed to add it later.
- **Item tier-band mirrors the mob band chain exactly.** Same dual-write/command/YAML/Blazor pattern as prog-2's mob band, for authoring-surface consistency and round-trip test parity.
- **Anti-grind rewire preserves the DI-cycle guard structurally.** Injecting `IPowerBudgetSystem` (a core system) into `ProgressionSystem` introduces no cycle — the guard is that the *snapshot values* passed to `Estimate` stay raw (`AttributesComponent` fields), never `IStatSystem.Get`. The ratio is scale-invariant under a shared weight table, so the three pre-existing anti-grind cases needed no changes.
- **Registration lands in `CompositionRoot`, not `Program.cs`** — mirrors `ProgressionModule`/`AscensionModule`; the Blazor content-authoring host needs `IPowerBudgetSystem` resolvable for the editor readout.

## Deviations / Follow-ups

- **Deviation from the plan, found and fixed during the code-review gate:** the plan's own Postconditions (P3/P4) directed `PowerBudgetSystem` to read `AscensionConstants.TrackedScores`/`.TierBaselineStep`/`.MaxTier` directly. The architecture-reviewer's code-mode pass flagged this as a literal INV-2 violation — a core-tier system (`Core/Systems/`) importing a `Core/Modules/<Feature>/` domain type (`Ascension`), the same class of dependency the plan itself was careful to avoid for `CharacterDefaultsOptions`/`Account`. Fixed by mirroring those three values as co-located constants on `PowerBudgetConstants` (`MaxTier`, `TierBaselineStep`, `TrackedScores`, each documented "keep in sync with `AscensionConstants.X`") — the identical pattern the plan already used for `ReferenceBaseScores`. `PowerBudgetSystem.cs` now imports no `Core/Modules/` namespace at all. The architecture-guard test was broadened from an `Account`-specific check to a general `Core/Modules/<Feature>/` import scanner (`PowerBudgetSystem_has_no_domain_module_dependency`) so this class of gap is caught mechanically going forward, not just for the one case that shipped broken. `PowerbandCommand` (domain-tier, not itself INV-2-bound) was also switched from `AscensionConstants.MaxTier` to `PowerBudgetConstants.MaxTier` for a single source of truth with what `Classify`/`BandAnchor` actually enumerate.
- **No other deviations.** All three work packages (the oracle, the inspector commands + anti-grind rewire, item band authoring + Blazor readout) shipped as scoped; the Test plan's five tiers are all present.
- **Follow-up (backlog):** `prog-4` (simulation harness) is the next slice in the program and the oracle's 4th consumer (expected-vs-actual outcome comparisons); `prog-5` (agentic + balance-doc layer). A future player-facing `consider` danger-gauge remains a deferred, decoupled thin consumer of `Estimate`/`Classify`. Tracked in [`../backlog.md`](../backlog.md).
