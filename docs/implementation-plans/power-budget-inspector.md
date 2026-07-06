# Power model + balance inspector — slice prog-3

**Status:** planned
**Actors:** Administrator-Designer (inspects power via **admin-gated** `power`/`powerband`; authors item tier-bands; reads the Blazor editor readout — the **primary designer observability surface**) · System (`ProgressionSystem`'s anti-grind proxy consumes the oracle internally) · Player (out of scope this slice — a future player-facing `consider` danger-gauge reuses the same oracle; deferred)
**Module:** `Core/Systems/` (`IPowerBudgetSystem` — **core-tier**, no domain deps) + `PowerBudgetConstants`; new inspector commands in a small module (`Core/Modules/BalanceInspection/`); extends `Core/Modules/Items/` (item tier-band authoring) and `Core/Modules/Progression/` (anti-grind rewire); `Hedron.Web` (`ItemEditor`/`MobEditor` readout). Feature home on ship: [`../features/progression/`](../features/progression/).

---

## Description

Slice prog-3 of the five-slice **Progression & Balance program** (seed: [`progression-and-balance.md`](progression-and-balance.md); prog-1 substrate and prog-2 Ascension have shipped — [`../roadmap/completed/progression-substrate.md`](../roadmap/completed/progression-substrate.md), [`../roadmap/completed/ascension.md`](../roadmap/completed/ascension.md)). It lands the **shared power-budget oracle** the brief names: `IPowerBudgetSystem`, a **core-tier**, generic estimator that takes a **score snapshot** (`ScoreId → int` plus an optional tier) and returns a heuristic power scalar plus a classified tier band (`0–6`). Power is a **weighted sum** over a full `ScoreId → weight` table in a new `PowerBudgetConstants`; the tier bands are **derived** from the Ascension tier baseline (`AscensionConstants.TierBaselineStep`) anchored on a constant reference base build (mirroring the canonical new-character starting stat block), not hand-authored ranges. The snapshot input is the whole point: it keeps the system core-generic (INV-2) and serves **all** consumers with **one** function (INV-19), because callers gather scores first and hand in plain data — no entity id, no internal `IStatSystem` call.

Three live consumers land in this slice (clearing the INV-19 "build the framework now" bar): (A) **admin/designer-gated** in-game **`power <target>` / `powerband [tier]`** inspectors — an admin spot-check and the functional-validation "see it work" gate, with a golden-number test; (B) the **Blazor `ItemEditor`/`MobEditor` readout** — computed power + band, plus an authored-band-vs-computed-band comparison (both mobs and items now carry an authored band) — the **primary designer observability surface**; (C) the **`ProgressionSystem.GetEffectivePower` rewire** — the documented anti-grind tech-debt proxy (commented "replaced wholesale by `IPowerBudgetSystem` in slice 3") is rewired to call the oracle with a **raw-attribute** snapshot, preserving the DI-cycle guard. The slice also adds the **authored item tier-band tag** (`ItemDataComponent.TierBand`, `setitem band`, YAML round-trip, Blazor field), mirroring the mob band prog-2 shipped, so item content can be band-tagged for the readout comparison. A future player-facing `consider` danger-gauge (players roughly comparing their own strength to a mob before engaging) is a **deferred, decoupled** thin consumer of the same oracle — self-power vs target-power → a coarse diegetic label via the same `Estimate`/`Classify` seam; prog-3 keeps the raw balance numbers admin/designer-only (see Design notes and resolved Q3).

---

## Preconditions

- The Progression and Ascension modules are live: `IStatSystem.Get` folds four `IEffectContributor` registrants (equipment, abilities, progression, ascension); `AscensionConstants.TierBaselineStep = 10`, `MaxTier = 6`, `TrackedScores = { Body, HpMax }`.
- `ScoreId` has 14 members (`Mind`, `Body`, `Spirit`, `Attunement`, `HpMax`, `ManaMax`, `StaminaMax`, `AstraMax`, `HpCurrent`, `ManaCurrent`, `StaminaCurrent`, `AstraCurrent`, `AttackPower`, `Defense`).
- The canonical new-character starting stat block exists as `CharacterDefaultsOptions` (`AttributeDefault = 10`, `MaxHp = 100`, `MaxMana = 50`, `MaxStamina = 50`, `MaxAstra = 10`) in `Core/Modules/Account/` — a runtime-configurable **domain** options type. `PowerBudgetConstants.ReferenceBaseScores` **mirrors** these values as co-located core constants; that constant snapshot is the reference base build the tier-band anchors are computed from (resolved Q1 — the core oracle takes no dependency on the domain options; INV-2).
- The item authoring chain exists and is band-less: `ItemDataComponent` (`.Value`, 12a), `IItemBuilderSystem`/`ItemBuilderSystem`, `ItemTemplate`, `ItemTemplateDeserializer`, `ItemContentWriter`, `SetitemCommand`, `ItemEditor.razor`. The mob band chain (`MobDataComponent.TierBand`, `SetMobBand`, `setmob band`, `MobContentWriter`/`MobTemplateDeserializer` `band:` field, `MobEditor`) is the mirror.
- `ProgressionSystem.GetEffectivePower(entityId)` currently returns `Mind + Body + Spirit + Attunement` off raw `AttributesComponent`, guarding the DI cycle (`IStatSystem → IEffectSystem → contributors → ProgressionEffectContributor → IProgressionSystem → IStatSystem`). The anti-grind proxy is the only caller.
- The Blazor content-authoring host boots the full engine via `CompositionRoot.Register`, so `IPowerBudgetSystem` (once registered there via a module extension) is resolvable in `Hedron.Web`; editors operate on YAML **templates** (via `IContentDefinitionCatalog`), which have no live entity id.

## Postconditions

> The coverage contract. Every item asserting player-invisible internal state maps to a named test in the Test plan.

- **P1 — Power is a deterministic weighted sum.** `IPowerBudgetSystem.Estimate(snapshot)` returns `Σ (weight[score] × snapshot[score])` over `PowerBudgetConstants.Weights`, for exactly the scores present in the snapshot; a score with no weight entry contributes 0; an absent score contributes 0. No randomness, no wall-clock — pure function of inputs (INV-26: no `IRandom` seam needed; stated explicitly).
- **P2 — Combat-relevant scores dominate.** `Weights` gives `Body`, `HpMax`, `AttackPower`, `Defense` meaningful positive weights and pools/resistances (`ManaMax`, `StaminaMax`, `AstraMax`, the `*Current` scores) light-or-zero weights; the table values every score uniformly whether the snapshot came from item bonuses or effective scores.
- **P3 — Tier baseline lifts power.** `Estimate(snapshot, tier)` adds the tier baseline contribution: for each score in `AscensionConstants.TrackedScores`, `weight[score] × (AscensionConstants.TierBaselineStep × tier)` on top of the snapshot sum. `tier = 0` (or omitted) adds nothing; the result equals the snapshot-only estimate.
- **P4 — Bands are derived from a constant reference base build, not authored ranges.** `Classify(power)` returns the highest band `b ∈ [0, MaxTier]` whose lower anchor `≤ power`, where anchor(`b`) = `Estimate(referenceBaseSnapshot, tier: b) − BandSpan` and `referenceBaseSnapshot` is `PowerBudgetConstants.ReferenceBaseScores` — a co-located constant `ScoreId → int` snapshot **mirroring** `CharacterDefaultsOptions` (`Body/Mind/Spirit/Attunement = 10`, `HpMax = 100`, `ManaMax/StaminaMax = 50`, `AstraMax = 10`, `AttackPower = Body/2 = 5`, `Defense = Body/4 = 2`, matching `IStatSystem`'s base derivations), documented "keep in sync with `CharacterDefaultsOptions`". The core oracle takes **no** runtime dependency on the domain `Account` options (INV-2-clean; resolved Q1). Bands **overlap** by `BandSpan` (per Ascension semantics — a maxed lower tier can reach into the next band); `BandSpan` is a `PowerBudgetConstants` constant.
- **P5 — `power <target>` prints a power scalar and a band (admin-gated).** The command is **admin/designer-gated** (`CommandCategory.Admin` + `AdminRequirement`, like `defs`). Resolving a target to a snapshot, it outputs the computed power and its classified band via a typed output message; targets resolve **runtime-in-world only** — `power <self>` includes worn gear (its snapshot comes from `IStatSystem.Get` per score, which folds `EquipmentEffectContributor`); `power <item>` (in inventory/room) scores the item's `EquipmentStatBonus` rows in isolation; `power <mob>` (in room) reads the mob's effective scores. Blueprint-id/template resolution is **deferred** (the Blazor editor readout is the designer's template-inspection surface — resolved Q2).
- **P6 — `powerband [tier]` prints the band definitions (admin-gated).** With no argument, lists every band `0–6` with its lower anchor; with a tier argument, prints that band's anchor and the reference base build's power at that tier. `powerband` is likewise **admin/designer-gated**.
- **P7 — Item tier-band round-trips.** `ItemDataComponent.TierBand` (`int 0–6`, `0 = unbanded`) and `ItemTemplate.TierBand` dual-write via `IItemBuilderSystem.SetItemBand`; `setitem band <blueprintId> <tier>` sets both; the value round-trips write→YAML→read through `ItemContentWriter`/`ItemTemplateDeserializer` (`band:` field, absent when 0); an out-of-range/negative band deserializes as `0` with a logged warning (mirrors `MobTemplateDeserializer`).
- **P8 — The Blazor readout shows computed power, computed band, and the authored-vs-computed comparison.** `ItemEditor` and `MobEditor` each display `IPowerBudgetSystem.Estimate(...)` + `Classify(...)` for the template, and flag when the authored `TierBand` differs from the computed band; the readout is derived from the template's authored data (item: `StatBonuses`; mob: authored scores + `TierBand`) via a snapshot — **never** `IStatSystem.Get` (no live entity exists). This is the primary designer observability surface.
- **P9 — The anti-grind rewire is behaviorally equivalent (INV-24 guard preserved).** `ProgressionSystem.GetEffectivePower` calls `IPowerBudgetSystem.Estimate` with a snapshot built from **raw** `AttributesComponent` fields (`Mind/Body/Spirit/Attunement`), never `IStatSystem.Get`; the anti-grind scale (`ComputeAntiGrindScale`) is unchanged, so the combat-award ratio behaves equivalently up to the weighted-sum rescale — **exact only when all four attribute weights are equal**; a `Body`-weighted table is an intended refinement, pinned by the three anti-grind cases (the ratio is scale-invariant under a shared weight table — see Design notes). `IPowerBudgetSystem` takes no domain dependency (INV-2).
- **P10 — Events: none new.** The oracle and inspectors are read/inspect tools; no new event is published. `setitem band` reuses the existing `ItemPropertySetByAdminEvent` (as every `setitem` branch does).

---

## Main flow

`power <target>` — the admin/designer inspection surface (Consumer A):

1. An admin/designer issues `power <target>`. The `PowerCommand` (Initiator, admin-gated) parses the target token.
2. The command resolves the target to a **score snapshot** by kind (runtime-in-world only — blueprint-id/template resolution deferred to the Blazor readout):
   - **self** (no arg, or `self`/`me`): snapshot = `IStatSystem.Get(invoker, score)` for each `ScoreId` — folds worn gear, abilities, progression, tier.
   - **item in inventory / room**: `IItemSystem.TryFindItemInInventory`/`TryFindItemInRoom` → snapshot from the entity's `ItemDataComponent.StatBonuses` rows (score → magnitude); tier from `ItemDataComponent.TierBand`.
   - **mob in room**: `ICombatSystem.TryFindTargetInRoom` → snapshot = `IStatSystem.Get(mob, score)`; tier from `MobDataComponent.TierBand`.
3. If nothing resolves, the command writes "You don't see that here." (or the self-fallback) and returns.
4. The command calls `IPowerBudgetSystem.Estimate(snapshot, tier)` → power scalar, then `IPowerBudgetSystem.Classify(power)` → band.
5. The command writes a typed `PowerReadoutMessage` (target label, power scalar, classified band, and — for a banded target — the authored band for comparison) through the output writer. No event, no persistence.

`ProgressionSystem` anti-grind (Consumer C), on every kill (unchanged trigger — [flow-31](../architecture/flows/flow-31-progression-award.md)):

6. `AwardCombatExperience(killer, victim)` calls `GetEffectivePower` for each combatant; `GetEffectivePower` now builds a raw-attribute snapshot (`Mind/Body/Spirit/Attunement` from `AttributesComponent`) and calls `IPowerBudgetSystem.Estimate(snapshot)` (no tier — raw attributes only, no DI cycle), then `ComputeAntiGrindScale` divides victim power by killer power exactly as before.

---

## Events fired

**None new.** `IPowerBudgetSystem` and the `power`/`powerband` inspectors are pure read tools — INV-5/INV-6 have nothing to add (a read that publishes an event would be wrong). `setitem band` reuses the existing `ItemPropertySetByAdminEvent(invoker, itemEntityId, property, value)` that every `setitem` branch already publishes (the admin-authoring audit fact), so no new event there either. This is confirmed and justified per the brief's "likely none new" note.

---

## Systems / handlers involved

| Piece | Layer | New/Reuse | Role |
|---|---|---|---|
| `IPowerBudgetSystem` / `PowerBudgetSystem` | **Core** (`Core/Systems/`) | **new** | `Estimate(snapshot, tier=0)` → power scalar; `Classify(power)` → band; `BandAnchor(tier)` → the lower anchor. Snapshot in, no entity id, no domain call (INV-2). |
| `PowerBudgetConstants` | Category-3 System Math | **new** | `Weights` (`ScoreId → int`, full table), `BandSpan`, `ReferenceBaseScores` (constant snapshot mirroring `CharacterDefaultsOptions`). Co-located with the system (OD-2 promotion path to YAML noted). |
| `PowerCommand` (`power`) | Initiator (command, **admin-gated**) | **new** | Resolves a runtime-in-world target (self/item/mob) → snapshot → `Estimate`/`Classify` → typed output. Consumer A. |
| `PowerbandCommand` (`powerband`) | Initiator (command, **admin-gated**) | **new** | Lists band definitions / one band. Consumer A. |
| `PowerReadoutMessage` / `PowerbandMessage` | Output (`Core/Output/`) | **new** | Typed output for the inspectors; formatted by `TelnetOutputFormatter`. |
| `IStatSystem.Get` | Domain read seam | reuse | Snapshot source for self/mob targets (folds gear/abilities/progression/tier). Never called *inside* the oracle (INV-24). |
| `IItemSystem.TryFindItemInInventory` / `TryFindItemInRoom` | Domain | reuse | Item target resolution. |
| `ICombatSystem.TryFindTargetInRoom` | Domain | reuse | Mob target resolution. |
| `ItemDataComponent.TierBand` | Component (data) | **new field** | Authored item band; mirrors `MobDataComponent.TierBand`. `[Persistent]` already on the component — band is world-content data (never snapshotted; see persistence audit). |
| `ItemTemplate.TierBand` | Template | **new field** | Durable authored band; YAML round-trip. |
| `IItemBuilderSystem.SetItemBand` / `ItemBuilderSystem.SetItemBand` | Domain | **new** | Dual-write item band onto live component + template. |
| `SetitemCommand` `band` branch | Initiator (command) | **new branch** | `setitem band <id> <tier>`; range-validate `0–6` at the edge; reuse `ItemPropertySetByAdminEvent`. |
| `ItemContentWriter` / `ItemTemplateDeserializer` `band:` field | Content I/O | **new field** | Lossless YAML round-trip; warn-and-default on out-of-range/negative. |
| `ProgressionSystem.GetEffectivePower` | Domain | **rewire** | Consumer C — raw-attribute snapshot → `IPowerBudgetSystem.Estimate`; DI-cycle guard preserved. |
| `ItemEditor.razor` / `MobEditor.razor` | Blazor UI | **new field + readout** | Item band field; computed power/band readout + authored-vs-computed comparison. Consumer B (primary designer surface). |

No new handlers. No heartbeat participation. Blueprint-id/`ITemplateRegistry` target resolution is **deferred** (resolved Q2) — not wired in prog-3.

---

## Implementation plan — work packages

Three independently-executable packages; the primary agent runs `architecture-reviewer` (code mode) across the combined diff once all land.

### WP-1 — The oracle: `IPowerBudgetSystem` + `PowerBudgetConstants` (core spine)

- **Scope:** `Core/Systems/IPowerBudgetSystem.cs` + `PowerBudgetSystem.cs`; `Core/Systems/PowerBudgetConstants.cs`. Define the snapshot input type (a small readonly struct `PowerSnapshot` wrapping `IReadOnlyDictionary<ScoreId,int>` — or the dictionary directly; the plan uses a named struct for clarity and a future-proof optional-tier carry, planner's call at implementation, kept trivially data-only). `Estimate(snapshot, int tier = 0)`, `Classify(int power)` → band, `BandAnchor(int tier)`. `PowerBudgetConstants.Weights` full `ScoreId → int` table (P2 values), `BandSpan`, and `ReferenceBaseScores` — a constant `ScoreId → int` snapshot that **mirrors** `CharacterDefaultsOptions`' defaults as co-located constants (resolved Q1 — the core oracle takes **no** dependency on the domain `Account` options, keeping it INV-2-clean; documented "keep in sync"). Bands are a pure function of the balance table.
- **Registration:** an `IPowerBudgetSystem` singleton. Because the Blazor readout (WP-3) needs it and Blazor boots via `CompositionRoot.Register`, register it there — either a bare `services.AddSingleton<IPowerBudgetSystem, PowerBudgetSystem>()` in `CompositionRoot.Register`, or a `Core/Modules/BalanceInspection/BalanceInspectionModule.cs` `AddBalanceInspectionModule` extension called from `CompositionRoot.Register` (preferred — co-locates the inspector-command registration from WP-2). Not `Program.cs` (that host-specific split would starve Blazor), mirroring why `ProgressionModule`/`AscensionModule` register in `CompositionRoot`.
- **Files:** the three above; `CompositionRoot.cs` (registration).
- **Out of scope:** commands, item band, Blazor, the anti-grind rewire.
- **Exit criterion:** `PowerBudgetSystemTests` green — weighted-sum math (P1), tier lift (P3), band derivation off the reference build with overlap (P4), the golden-number anchor assertion, weight-table sanity (combat scores dominate, P2). DI-smoke resolves `IPowerBudgetSystem`.

### WP-2 — Consumer A + Consumer C: inspector commands + anti-grind rewire

- **Scope:** `PowerCommand` (`power`) and `PowerbandCommand` (`powerband`) — both **admin-gated** (`CommandCategory.Admin` + `AdminRequirement`, resolved Q3) — in `Core/Modules/BalanceInspection/Commands/`; `PowerReadoutMessage`/`PowerbandMessage` in `Core/Output/` + `TelnetOutputFormatter` formatting; register the commands in the module extension (WP-1). Rewire `ProgressionSystem.GetEffectivePower` to build a raw-attribute snapshot and call `IPowerBudgetSystem.Estimate` (inject `IPowerBudgetSystem` into `ProgressionSystem` — a **core** system, no cycle; the guard is that the *snapshot values* stay raw, not that the oracle is un-injected). Delete the now-obsolete `GetEffectivePower` inline sum and its two "replaced by slice 3" comments; update `ProgressionSystem`'s constructor and `ProgressionModule` registration. Update the `edit-progression-system` skill (INV-20 — see Cross-cutting): its anti-grind-proxy description is stale the moment this rewire lands.
- **Target resolution (committed):** runtime-in-world only — self (default), item in inventory/room, mob in room. Reuse `IItemSystem.TryFindItemInInventory`/`TryFindItemInRoom`, `ICombatSystem.TryFindTargetInRoom`, and `IStatSystem.Get`. **No** blueprint-id/`ITemplateRegistry` path in prog-3 (deferred to the Blazor readout / a future `consider`).
- **Files:** the command + message files; `TelnetOutputFormatter.cs`; `ProgressionSystem.cs`, `IProgressionSystem` (unchanged signature — `GetEffectivePower` stays private), `ProgressionModule.cs`; the module extension; `.claude/skills/edit-progression-system/SKILL.md`.
- **Dependencies:** WP-1 (`IPowerBudgetSystem`).
- **Out of scope:** item band authoring, Blazor.
- **Exit criterion:** `PowerCommandTests` (self/item/mob resolution → correct power+band, golden number; **non-admin invocation rejected**), `PowerbandCommandTests` (list/one-band; admin-gated), and updated `ProgressionSystemTests` asserting the anti-grind ratio is unchanged for the three anti-grind cases post-rewire (P9). `dotnet test` green.

### WP-3 — Item tier-band authoring + Blazor readout (Consumer B + INV-18 tooling)

- **Scope:** `ItemDataComponent.TierBand` + `ItemTemplate.TierBand` fields; `Apply` copies band; `IItemBuilderSystem.SetItemBand` + impl (dual-write, mirror `SetMobBand`); `SetitemCommand` `band` branch (range-validate `0–6`, reuse `ItemPropertySetByAdminEvent`; extend `LongDescription`/`Usage`/default-case list); `ItemContentWriter` + `ItemTemplateDeserializer` `band:` field (absent when 0; warn-and-default on out-of-range/negative — mirror `MobTemplateDeserializer` exactly). Blazor: `ItemEditor.razor` band `<input min=0 max=6>` field (mirror the `MobEditor` Ascension fieldset) **and** a computed-power/band readout on both `ItemEditor` and `MobEditor` (inject `IPowerBudgetSystem`; build the snapshot from the template's authored data — item `StatBonuses`, mob authored scores + `TierBand`; show power, computed band, and an authored-vs-computed flag) — the primary designer observability surface.
- **Files:** `ItemDataComponent.cs`, `ItemTemplate.cs`, `IItemBuilderSystem.cs`, `ItemBuilderSystem.cs`, `SetitemCommand.cs`, `ItemContentWriter.cs`, `ItemTemplateDeserializer.cs`, `Hedron.Web/Components/Pages/ItemEditor.razor`, `MobEditor.razor`.
- **Dependencies:** WP-1 (`IPowerBudgetSystem` for the readout).
- **Out of scope:** the inspector commands, the anti-grind rewire.
- **Exit criterion:** `ItemBuilderSystemTests.SetItemBand` (dual-write), `SetitemCommandBandTests` (dual-write, out-of-range/negative rejection at the edge), `ItemTierBandRoundTripTests` (write→real file→read for a representative value; zero/absent round-trip; out-of-range and negative logged-and-defaulted; `Apply` seeding) mirroring `MobTierBandRoundTripTests`. Blazor readout is presentation — smoke-verified, not unit-tested (see Test plan skips).

---

## Content tooling impact (INV-18)

The slice adds one piece of authored gameplay state — the **item tier-band** — and ships its full authoring + inspection surface in the same PR (mirroring the mob band prog-2 shipped):

- **Data-file shape:** `items/*.yaml` gains an optional `band:` integer field (`0–6`, absent = unbanded `0`). Round-trips losslessly through `ItemContentWriter`/`ItemTemplateDeserializer`; out-of-range/negative warns-and-defaults to `0`.
- **Admin command:** `setitem band <blueprintId> <tier>` — dual-writes the live `ItemDataComponent.TierBand` and the template, range-validated at the command edge, audited via the existing `ItemPropertySetByAdminEvent`.
- **Blazor editor (primary designer surface):** `ItemEditor` gains a Tier-band field (mirror the `MobEditor` Ascension fieldset) **and** a computed power/band readout with an authored-vs-computed comparison; `MobEditor` gains the same readout (it already has the band field). This is where the owner expects most balance-observability value to live.
- **Inspection (admin, in-game):** `power <target>` and `powerband [tier]` are the new admin/designer in-game inspection surface for the power model itself — the "see it work" gate. The power oracle is not authored content (it's a computed heuristic), so it has no YAML/`TemplateRegistry` entry; `PowerBudgetConstants` follows the `ProgressionConstants`/`AscensionConstants` Category-3 pattern (co-located, OD-2 promotion path to YAML noted for the sim-driven tuning in prog-4/5).
- No `TemplateRegistry` schema entry beyond the item `band:` field; no new content kind.

---

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Notes |
|---|---|---|
| **Power-budget framework** (the oracle) | **Adequate — this slice *is* the framework** | INV-19's headline: three live consumers (inspector, Blazor readout, anti-grind proxy; the sim is the 4th in prog-4) → the shared oracle lands **once**, as one `Estimate`/`Classify` function, not hand-rolled per caller. The snapshot input is the mechanism that lets one function serve all three (and a future `consider` — see Design notes). |
| **Commands** | **Adequate** | `power`/`powerband` are standard admin `ICommand`s via the existing framework (schema, resolvers, typed output, `AdminRequirement`). `setitem band` is a new branch on an existing admin command — the established dual-write + audit-event pattern. No new command infrastructure. |
| **Output** | **Adequate** | New typed `PowerReadoutMessage`/`PowerbandMessage` + `TelnetOutputFormatter` formatting — the standard typed-output path (`ScoreDisplayMessage`/`ProgressDisplayMessage` precedent). No `session.SendLineAsync` (INV-11). |
| **ECS queries** | **Adequate** | Item/mob resolution reuses existing `IItemSystem`/`ICombatSystem` finders; snapshot assembly is `IStatSystem.Get` per score or `StatBonuses` iteration — no new query primitive. |
| **Content templates / tooling** | **Adequate** | Item `band:` field is a direct mirror of the mob `band:` field prog-2 shipped, through the same writer/deserializer/Blazor pattern. |
| **Contributor port (INV-24)** | **Adequate** | The oracle does **not** register as an `IEffectContributor` and does **not** call `IStatSystem` internally; it consumes computed values that callers gather. The anti-grind proxy's raw-snapshot rule (established prog-1/prog-2) is preserved verbatim — the guard is now *documented as a reusable rule* the oracle's snapshot design enforces structurally. |
| **Configuration** | **Adequate** | `PowerBudgetConstants` follows the Category-3 co-located-constants pattern; OD-2 YAML promotion is noted, not built (restraint — the prog-4 sim is the likely trigger). `ReferenceBaseScores` mirrors `CharacterDefaultsOptions` as constants (resolved Q1) rather than injecting the domain options — keeps the core oracle INV-2-clean. |
| **Persistence** | **Adequate** | See the opt-in audit below — no new persistent state; the item band is world-content data on an already-`[Persistent]` component that never reaches a snapshot (items in the world carry no `PersistentEntity`). |
| **Agent tooling (INV-20)** | **Gap exposed → resolve in-slice** | The `edit-progression-system` skill documents the anti-grind proxy's raw-attribute rule and the DI-cycle guard; WP-2 rewires that proxy to call `IPowerBudgetSystem`. The skill must be updated in the same PR to describe the oracle as the anti-grind backend and to point new balance-tuning at `PowerBudgetConstants` (the power-model surface is new). The advisor/planner/reviewer INV-20 refresh for the *power-model + sim* surfaces is scoped to prog-5 by the brief — but the `edit-progression-system` skill's proxy description is stale the moment WP-2 lands, so it is in-slice. Folded into WP-2. |

**Persistence opt-in audit (INV-22/23).**

- **Level 1 — entity domains:** the slice constructs **no** new entities. `ItemDataComponent.TierBand` is added to the item entity/template — items are **world content** (world-spawn items carry no `PersistentEntity`; their durable form is YAML). A player-owned item that transitions into a persistent context (inventory/persistent container) already has that transition handled by `ItemContextHandler` per persistence reform — this slice does not change it, and the band travels as ordinary `ItemDataComponent` data.
- **Level 2 — component inclusion:** `ItemDataComponent` is already `[Persistent]` (12a decision — so player-owned items snapshot their name/value/bonuses). `TierBand` is authored world-content data; it is *included* in the snapshot for a persistent (player-owned) item, which is harmless and consistent with the existing `Value`/`StatBonuses` fields on the same component — the band is re-sourced from the template on world spawn and simply rides along on a player-owned instance, exactly as `Value` does. No `[Persistent]` change. No new component. `PowerBudgetConstants` and `PowerSnapshot` are not components (static constants / a transient computation struct) — no persistence.
- **Level 3 — save-on-change:** **none.** `setitem band` is a world-content admin command — it writes YAML via `ItemContentWriter` and calls **no** `SaveEntityAsync` (world items have no `PersistentEntity`; matches every existing `setitem` branch). The inspectors and the oracle perform no persistence. The anti-grind rewire is a pure read. No INV-22 boundary save anywhere in this slice.

---

## Flows introduced or modified (INV-17)

- **[flow-31 — Progression journey](../architecture/flows/flow-31-progression-award.md) — MODIFIED (body only).** Step 2's `GetEffectivePower` description ("sums each combatant's raw `Mind + Body + Spirit + Attunement`") changes to "builds a raw-attribute snapshot and calls `IPowerBudgetSystem.Estimate`; the anti-grind ratio is unchanged." The mermaid participant set is unchanged (the oracle is an internal call within the domain system, not a new box). WP-2's PR updates this file.
- **New flow: NOT warranted.** `power`/`powerband` are single-shot admin commands with no recurring fan-out — they plug into the existing [flow-03 command journey](../architecture/flows/flow-03-player-command-lifecycle.md) (parse → resolve → system → typed output) and need no dedicated flow file. The Blazor readout plugs into [flow-29 content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md) (offline editor) with no structural change (a read-only computed field on an existing editor page). Confirmed against `flows/README.md` — no new recurring runtime chain is introduced.

---

## Test plan / Verification (INV-25)

Derived from the Postconditions. Determinism (INV-26): the power math is a **pure weighted sum with no chance or wall-clock** — so **no `IRandom`/clock seam is introduced or needed** for the oracle; this is stated explicitly and is itself a checked property (P1). The one place variance already lives (the combat-award base draw) is untouched and stays behind the existing `IRandom`.

> **Test paths below are indicative locators, not literal folders.** Tests land in the source-mirrored namespace under `Hedron.Tests/Modules/<Feature>/` (and `Hedron.Tests/Persistence/`) per the `add-tests` convention — e.g. `Hedron.Tests/Modules/BalanceInspection/PowerBudgetSystemTests`, item-band tests under `Hedron.Tests/Modules/Items/…`, and the cited round-trip mirror at `Hedron.Tests/Modules/Mobs/MobTierBandRoundTripTests.cs`. Do **not** create a literal top-level `Hedron.Tests/BalanceInspection/` folder — it breaks the mirroring convention.

**Tier 1 — system-unit (`Hedron.Tests/BalanceInspection/PowerBudgetSystemTests`):**
- `Estimate` weighted sum over a mixed snapshot; empty snapshot → 0; unweighted score contributes 0 (P1).
- `Estimate` with tier adds the baseline contribution over `TrackedScores`; tier 0 == snapshot-only (P3).
- Weight-table sanity: `Body`/`HpMax`/`AttackPower`/`Defense` weights are positive and exceed the pool-score weights (P2).
- `Classify` band derivation off the constant reference base build: the **golden-number** assertion — the reference base build classifies to band `0`; a snapshot at the Tier-N anchor classifies to `N`; band overlap by `BandSpan` (a value in the overlap of bands N and N+1 classifies to N+1) (P4). This is the functional-validation gate's automated form.
- `BandAnchor(tier)` equals `Estimate(ReferenceBaseScores, tier) − BandSpan` (P4).

**Tier 2 — handler/command (`Hedron.Tests/BalanceInspection/`):**
- `PowerCommandTests`: self target → snapshot from `IStatSystem.Get` (fake stat system), power+band in the typed message; item target → snapshot from `StatBonuses`, correct power+band incl. authored band echo; mob target → snapshot + `MobDataComponent.TierBand`; unresolved target → the not-found message; the golden number surfaces end-to-end; a non-admin invoker is rejected (admin-gated, matches `defs`) (P5).
- `PowerbandCommandTests`: no-arg lists bands `0–6` with anchors; tier arg prints that band; admin-gated (P6).
- `SetitemCommandBandTests`: `band` branch dual-writes component + template and publishes exactly one `ItemPropertySetByAdminEvent`; out-of-range (`7`) and negative (`-1`) rejected at the edge with no mutation (P7).
- `ItemBuilderSystemTests.SetItemBand`: live-component + template dual-write (P7), mirroring `SetMobBand`.

**Tier 3 — flow:**
- The `power <self>` path is covered by the command test with a real `IPowerBudgetSystem` + fake `IStatSystem` (the golden number through the full command). No separate multi-system flow harness is warranted — there is no cross-system event fan-out (P5, P10).

**Anti-grind equivalence (`ProgressionSystemTests`, on-touch ratchet, P9):**
- Re-assert the three existing anti-grind cases (trivial victim → 0, peer → ~1.0-scaled, over-strong victim → capped) against the **rewired** `GetEffectivePower`; the ratio is scale-invariant, so the assertions hold with the weighted-sum backend. Add a case pinning that `GetEffectivePower`'s snapshot uses raw attributes (a killer with high worn-gear but low raw attributes yields the raw-attribute power, not the gear-folded value) — the DI-cycle guard as a test.

**Tier 4 — persistence round-trip (`Hedron.Tests/Persistence/` + a new `ItemTierBandRoundTripTests`):**
- `ItemTierBandRoundTripTests` (mirror `MobTierBandRoundTripTests`): write→real file→read for a representative band; zero/absent round-trip (band absent from YAML when 0); out-of-range and negative logged-and-defaulted in the deserializer; `Apply` seeds `ItemDataComponent.TierBand` from the template (P7).
- Confirm a player-owned item's `ItemDataComponent` round-trips with `TierBand` intact through the SQLite snapshot (it rides the existing `[Persistent]` component — one assertion in `RoundTripTests`), and a world-spawn item carries no `PersistentEntity` (existing invariant, re-checked).

**Tier 5 — architecture-guard:** covered by the existing reflection suite — no `IEventBus` field on `PowerBudgetSystem` (it publishes nothing, INV-5); `PowerBudgetSystem` imports no `Core/Modules/<Feature>/` domain type (INV-2, the core-tier guard — the load-bearing structural check, and the reason `ReferenceBaseScores` is a constant rather than an injected `CharacterDefaultsOptions`); DI-smoke resolves the new registrations. Add an explicit INV-2 guard assertion if the reflection suite does not already scan `Core/Systems/` for domain imports.

**Skipped, with reason:**
- **Blazor readout rendering** (`ItemEditor`/`MobEditor` computed-power field) — presentation; the *computation* is `PowerBudgetSystem` (Tier 1) and the snapshot-from-template assembly is trivial data mapping. Smoke-verified in the editor, not unit-tested (matches the untested-Blazor-presentation convention prog-2 used for the `MobEditor` band field).
- **Exact output prose** of `PowerReadoutMessage`/`PowerbandMessage` — presentation; the typed message *fields* (power, band, authored band) are asserted in the command tests.
- **`PowerSnapshot` struct / `PowerBudgetConstants`** — pure-data / static constants; exercised transitively by the system tests.
- **`power`/`powerband` command plumbing** (schema wiring, arg parsing) — thin Initiator plumbing over the tested system.

---

## Design notes

> Seam rationale folded from the program brief's Architecture brief (the "one shared power oracle, three consumers" entry) and the four resolved decisions baked into this slice, plus the three slice-level forks resolved with the owner (see Resolved slice questions). Survives disintegration into [`../features/progression/`](../features/progression/) on ship.

### The snapshot input is what keeps the oracle core-generic and singular (brief OQ6 → resolved: stays core)

`IPowerBudgetSystem` is **core-tier** (`Core/Systems/`, INV-2 — no domain dependency). It takes a **score snapshot** (`ScoreId → int` + optional tier) as input, **never** an entity id and **never** an internal `IStatSystem` call. This is the load-bearing decision: callers gather scores *first* and hand in plain data, so the same one function serves all consumers —

- the inspector (`power <self>`/`<mob>`) reads `IStatSystem.Get` per score (domain-tier orchestration, in the command, folding gear/abilities/progression/tier);
- the inspector (`power <item>`) and the Blazor readouts read a template's authored bonuses/scores (no live entity exists in the editor — it operates on YAML templates via `IContentDefinitionCatalog`, which is *why* an entity-id-based API would have been unusable there);
- the anti-grind proxy passes **raw** attributes;
- a future player-facing `consider` (deferred) passes two snapshots and compares.

Because the snapshot is generic data, INV-2 is satisfied structurally (the oracle imports no domain module) and INV-19 is satisfied by construction (one function, many call sites, no drift). The brief's open-Q6 ("confirm it stays core-tier-generic") resolves **yes** — validated by the fact that every consumer can express its input as a plain `ScoreId → int` map without the oracle reaching into any game system.

### Power = weighted sum over a full table; bands = derived from the tier baseline (decisions 1 & 2; slice Q1)

Power is `Σ (weight[score] × snapshot[score])` over `PowerBudgetConstants.Weights`, a **full** `ScoreId → weight` table. Combat-relevant scores (`Body`, `HpMax`, `AttackPower`, `Defense`) carry meaningful weights; pools/resistances carry light-or-zero weights — all tunable later (OD-2 → YAML when the prog-4 sim drives heavy iteration). The table values an item (from its `EquipmentStatBonus` rows) and a mob/PC (from effective scores) **uniformly**, because both reduce to a `ScoreId → int` snapshot.

Tier bands `0–6` are **derived**, not hand-authored: each band is anchored at the power of a **reference "baseline Tier-N" build** = the reference base build snapshot estimated *at tier N* (`Estimate(ReferenceBaseScores, tier: N)`). The **reference base build** is `PowerBudgetConstants.ReferenceBaseScores` — a co-located constant snapshot **mirroring** the canonical new-character starting stat block (`CharacterDefaultsOptions`: attributes 10, HpMax 100, Mana/Stamina 50, Astra 10), projected with the same base derivations `IStatSystem` uses (`AttackPower = Body/2`, `Defense = Body/4`). Holding the reference build as balance constants (resolved Q1) — rather than injecting the domain `Account` options (`CharacterDefaultsOptions` lives in `Core/Modules/Account/`; a `Core/Systems/` type depending on it would violate INV-2 and fail the Tier-5 core-tier guard) — keeps the **core** oracle free of any domain dependency and makes the bands a pure function of the balance table; the constants carry a "keep in sync with `CharacterDefaultsOptions`" note. Band width/overlap comes from a single `PowerBudgetConstants.BandSpan`; bands **overlap** by `BandSpan` so a maxed lower tier can reach into the next band before ascending — the exact Ascension overlap semantics prog-2 established. This ties the balance model's bands directly to the two authored anchors that already exist (the starting block and the tier baseline step), so tuning either automatically re-draws the bands with no separate authored range to drift.

### Consumer C — the anti-grind rewire preserves the DI-cycle guard, and the ratio is scale-invariant (decision matches prog-1/prog-2 guard)

`ProgressionSystem.GetEffectivePower` is rewired from an inline `Mind + Body + Spirit + Attunement` sum to `IPowerBudgetSystem.Estimate(rawAttributeSnapshot)`. The **critical** constraint (established twice — [`progression-system.md`](../features/progression/progression-system.md#anti-grind-proxy-reads-raw-attributes), [`ascension-system.md`](../features/progression/ascension-system.md)): the snapshot values stay **raw `AttributesComponent` fields**, never `IStatSystem.Get`. Injecting `IPowerBudgetSystem` (a **core** system) into `ProgressionSystem` introduces **no** cycle — the cycle only forms if the *input values* come from the stat pipeline. The oracle's snapshot design makes this structurally obvious: you cannot accidentally close the cycle because you hand the oracle plain numbers you already hold.

Equivalence (P9): `ComputeAntiGrindScale` divides victim power by killer power; the weighted-sum backend rescales both by the same weights, so the **ratio is unchanged up to weight-scaling** — a scale-invariant quantity. The anti-grind floor/cap constants operate on the ratio, so behavior is preserved. (One intended nuance: the old proxy summed the four attributes uniformly; the weighted table may weight `Body` above `Mind/Spirit/Attunement`, so a Body-heavy combatant reads as slightly more "powerful" than under the flat sum — a defensible refinement, not a regression. The plan pins the three anti-grind cases to catch any *unintended* drift, and the intended behavior — trivial victims grant nothing, over-strong victims never windfall — is unchanged.)

### Admin/designer-gated inspectors now; a future player `consider` reuses the same oracle (slice Q3, decoupled)

`power`/`powerband` are **admin/designer-gated** (`CommandCategory.Admin` + `AdminRequirement`, like `defs`) — they expose raw balance internals (power scalars, band anchors) that are an explicitly loosely-bounded, expect-iteration heuristic (the prog-4 sim retunes it), so players do not get into the guts of the balance model. The **primary designer observability surface is the Blazor editor readout** (Consumer B), which scores YAML templates directly and is where the owner expects most balance-observability value to live; the in-game commands are an admin spot-check and the functional-validation gate. Admin-gating is also the reversible default (admin→player is a one-line loosen; the reverse strands players).

Players still want a *rough* danger read ("how deadly is this fight?"). That is a **deferred, separate player-facing `consider` command** — and it is a **thin consumer of this exact oracle**, not a parallel system: `consider <mob>` gathers the player's and the target's effective scores into two snapshots, calls `Estimate`/`Classify` on each, and maps the *relationship* (power ratio / band delta) to a coarse **diegetic label** ("trivial / even / dangerous / deadly") — never surfacing the raw numbers or band internals. The public `Estimate`/`Classify`/`BandAnchor` interface already suffices for this with **no interface change**, so the capability is preserved by the core-tier snapshot design without building it now (restraint — no in-slice consumer). Recorded here per the owner's decision to keep the big picture: the two are **decoupled**; `consider` is backlogged. (If a later design finds the danger-label logic wants a shared home, add a small comparison helper on `IPowerBudgetSystem` *then* — when `consider` is the consumer.)

### Item tier-band mirrors the mob band exactly (decision 4)

`ItemDataComponent.TierBand` (`int 0–6`, `0 = unbanded`), `IItemBuilderSystem.SetItemBand` dual-write, `setitem band` branch, YAML `band:` round-trip with warn-and-default on out-of-range/negative, and the Blazor field are a **direct structural mirror** of the mob band chain prog-2 shipped (`MobDataComponent.TierBand`/`SetMobBand`/`setmob band`/`MobContentWriter`/`MobTemplateDeserializer`/`MobEditor`). Following the established pattern verbatim keeps the authoring surface consistent and the round-trip tests are a mirror of `MobTierBandRoundTripTests`. The item band exists **for the readout comparison** (authored-vs-computed) and for future sim/content tagging — mechanical threat stays emergent from the additive baseline, exactly as for mobs (the band is a content tag, not a power multiplier).

### Registration lands in `CompositionRoot` (not `Program.cs`) — the Blazor readout imposes it

`IPowerBudgetSystem` and the inspector commands register through a module extension called from `CompositionRoot.Register`, **not** `Program.cs`, for the same reason `ProgressionModule`/`AscensionModule` do: the Blazor content-authoring host boots the full engine via `CompositionRoot.Register` and its editors' computed-power readout (Consumer B) needs `IPowerBudgetSystem` resolvable. A `Program.cs`-only registration would leave the web host without the oracle and silently break the readout. (The inspector *commands* are only reachable from the telnet host — that's fine; unused command registrations in the web host are inert. What must be in `CompositionRoot` is the **system**.)

---

## Resolved slice questions

> The three load-bearing forks the planner surfaced, resolved with the owner (2026-07-05) before the spec gate. Recorded for the completed-record trail; the plan body above reflects these. Merged plans are TODO-free.

1. **Reference-base-build source → CONSTANTS.** The tier-band anchors read `PowerBudgetConstants.ReferenceBaseScores`, a co-located constant snapshot **mirroring** `CharacterDefaultsOptions` (attributes 10, HpMax 100, Mana/Stamina 50, Astra 10), documented "keep in sync." The core oracle takes **no** dependency on the domain `Account` options — injecting `IOptions<CharacterDefaultsOptions>` (in `Core/Modules/Account/`) into a `Core/Systems/` type would violate **INV-2** and fail the Tier-5 core-tier guard. Bands are a pure function of the balance table; the reference build is a deliberate balance constant, not live ops config.
2. **`power <target>` resolver scope → RUNTIME-IN-WORLD ONLY.** self (default), item in inventory/room, mob in room. Blueprint-id/template resolution is **deferred** — the Blazor editor readout (Consumer B) is the designer's template-inspection surface, and the owner confirmed most balance-observability value is editor-level, not in-game.
3. **`power`/`powerband` visibility → ADMIN/DESIGNER-GATED** (`CommandCategory.Admin` + `AdminRequirement`, like `defs`). Raw balance internals stay out of players' hands. A future **player-facing `consider`** rough-danger gauge is a **deferred, decoupled** consumer of the same oracle (self-vs-target `Estimate`/`Classify` → a coarse diegetic label, no raw numbers) — no interface change needed, so the capability is preserved without building it now. See the Design note above and [`../roadmap/backlog.md`](../roadmap/backlog.md).
