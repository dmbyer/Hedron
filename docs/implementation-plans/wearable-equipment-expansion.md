# Wearable Equipment Expansion — weapon/armor catalog + the equipment stat seam

> **Status: implemented** (advisor seed → planner → spec gate cleared → built, `dotnet test` green at 627). Pending: code-review gate, then disintegrate-on-ship via `sync-roadmap`.

## Actors

- **Player** — wears a variety of weapon and armor types across an expanded set of worn slots; the gear they wear now measurably changes their effective stats (attack from weapons, defense from armor, summed across a full suit).
- **Admin/builder** — authors the weapon/armor catalog: item types, worn slots, and per-item stat contributions, via `setitem` + YAML + the Blazor content editor.
- **The stat read path** — combat round and the `score` command read effective `AttackPower`/`Defense` through `IStatSystem`, now inclusive of worn-gear contributions.

## Module

`Core/Modules/Items/` (owner). Pulls on two seams it does not own: the **core effect contributor port** (`Core/Modules/Effects/`, INV-24) and the **stat read fold** (`Core/Modules/Stats/`). No new module.

## Description

Today an item contributes stats through a single flat field — `ItemDataComponent.DamageBonus` (int), read only for `MainHand` by `IStatSystem.GetEffectiveAttackPower`; armor contributes nothing (`Defense = Body/4`, flagged interim). This slice expands wearable equipment in three coordinated moves: (1) **slots** — extend `WornSlot` to cover a full armor suit; (2) **catalog** — author a variety of weapon and armor types as content; (3) **the stat seam** — wire the long-anticipated `WhileEquipped` effect contributor so each item carries authored, `ScoreId`-keyed stat contributions, and migrate the flat `DamageBonus` onto that one path. After this slice, "different weapons can be tested and configured" is real and data-driven: a battleaxe and a dagger differ by their authored bonus rows, and a full plate suit sums armor across slots — all through the effect pipeline the architecture was already shaped for.

## Design notes

*(Durable seam rationale — survives the disintegrate-on-ship trim.)*

- **The equipment-stats seam is the `WhileEquipped` effect contributor, not new flat fields (INV-15, INV-24).** The architecture was deliberately shaped for this: the `WhileEquipped` lifetime (slice 9-e) and the `IEffectContributor` port (INV-24, precedent `AbilityEffectContributor`) already exist and are unwired for equipment; both [`effect-system.md`](../features/effects/effect-system.md#the-contributor-seam) ("equipment / aura / area — worn items — future, same port") and the [gameplay-model overlap map](../design/gameplay-model.md) ("Item grants +HP while worn → `Effect(StatModifier, WhileEquipped)`") name it as the target. A parallel flat-field path (`ArmorBonus`, `DefenseBonus`, …) would re-create the per-feature stat-contribution sprawl the effect substrate exists to prevent. A new `EquipmentEffectContributor : IEffectContributor` (Items module, domain-tier) reads `EquipmentComponent.Slots` + each worn item's authored bonuses and yields `StatModifier` contributions; `IStatSystem.Get` already folds `IEffectSystem.GetModifiers`, which sums all registered contributors. The dependency arrow points Items(domain) → core port, satisfying INV-2.
- **Pull-on-read, never materialized (INV-24).** Worn-gear modifiers are derived on read from `EquipmentComponent` + `ItemDataComponent` — never written into a stored component, never recomputed on an equip event. This is why **no new event and no on-equip stat recompute is needed**: `ItemEquippedEvent`/`ItemUnequippedEvent` already exist for broadcast fan-out, and the next stat read simply reflects the new worn set. Removing the "did I recompute when gear changed?" bug class is the whole point of the contributor seam.
- **Bonuses are keyed by `ScoreId` (shape-for-later).** The authored bonus carries a `ScoreId` target + magnitude, so only `AttackPower` and `Defense` flow through it today, but any `ScoreId`-addressable score (`+HpMax`, `+ManaMax`, future speed/crit) becomes a free data addition with no contributor change. The seam's breadth is keyed for the general case while only two values flow through it now.
- **`DamageBonus` is unified onto the one path.** The flat `MainHand`-only `DamageBonus` is deprecated; weapon damage becomes a `ScoreId.AttackPower` equipment bonus. One stat-contribution path for all gear. **Migration risk:** `GetEffectiveAttackPower` reads `DamageBonus` directly and does *not* itself fold modifiers — only `Get(ScoreId.AttackPower)` adds them. The combat read path must therefore consume `Get(AttackPower)` (which folds the contributor) rather than `GetEffectiveAttackPower` directly, or weapon damage silently vanishes post-migration. Verify and pin this in the plan.
- **Armor feeds the existing `Defense` score (minimal).** `Get(ScoreId.Defense)` already folds `GetModifiers`, so an armor item's `StatModifier(Defense)` lands automatically with zero `Get` change. A *distinct* damage-reduction / armor-class score separate from `Defense`, and per-aspect resistance gear, are **deferred** (see brief) — this slice keeps `Defense` as the single armor target.
- **Two-of-a-kind slots are distinct enum values.** `EquipmentComponent.Slots` is `Dictionary<WornSlot, uint>` (one item per enum value); a second ring/wrist is a distinct `WornSlot` value (precedent: a two-hand weapon already declares both `MainHand` + `OffHand`). No model change.
- **The combat read path is the migration's load-bearing change (planner-resolved, open question 2).** `CombatSystem.ExecuteRound`/`ResolveAbilityStrike` today call `IStatSystem.GetEffectiveAttackPower` and `GetEffectiveDefense` **directly** (`Core/Modules/Combat/Systems/CombatSystem.cs` lines 79, 93, 110) — neither folds `GetModifiers`, so neither sees the contributor. After the migration `GetEffectiveAttackPower` returns `Body/2` only (weapon bonus moves to the contributor), so a direct call would lose all weapon damage. **Fix:** repoint the three combat reads to `_statSystem.Get(id, ScoreId.AttackPower)` / `Get(id, ScoreId.Defense)`, the seams that fold the contributor. The typed `GetEffective*` getters survive as `Body/2` and `Body/4` base computations (still folded by `Get`), and the `score` command already reads through `Get`. This is the single place weapon damage can silently vanish; it is pinned in WP-1.
- **Bonus-row shape (planner-resolved, open question 1).** `ItemDataComponent.DamageBonus: int` is replaced by `List<EquipmentStatBonus> StatBonuses` where `EquipmentStatBonus` is a `sealed record(ScoreId TargetScore, int Magnitude)` (pure data, INV-3; the list is `[Persistent]` via the class-level tag). Weapon damage is authored as one `(AttackPower, n)` row; armor as `(Defense, n)`; a future `+HpMax` ring is just another row, no contributor change. `ItemTemplate` carries the mirror `List<EquipmentStatBonus> StatBonuses`; `Apply` copies it onto the component. `setitem` grammar generalizes `dmg` → `bonus <score> <amount>` (add-or-replace the row for that score; `<amount> 0` removes it) plus `clearbonus` (clear all). YAML key `statBonuses:` is a list of `{ targetScore, magnitude }` maps.

## Architecture brief

*(In-flight; trimmed on ship.)*

### Seams + recommended homes

| New verb / state / signal | Home / layer | Notes |
|---|---|---|
| Worn-gear → effective-stat contribution | `EquipmentEffectContributor : IEffectContributor`, **Items module, domain-tier** | Mirrors `AbilityEffectContributor` (`Core/Modules/Abilities/AbilityEffectContributor.cs`). Implements `GetModifiers(entityId, scoreId)` (sum matching worn-item bonuses) + `GetActive` (yield synthetic `WhileEquipped` `Effect`s). DI-registered as `IEffectContributor`. |
| Authored per-item stat bonus | `ItemDataComponent` (cross-cutting, `Core/ECS/Components/`) | New field — a list of `(ScoreId TargetScore, int Magnitude)` bonus rows. `[Persistent]`. Replaces the single `DamageBonus int`. Pure data (INV-3). |
| Expanded worn slots | `WornSlot` enum (`Core/WornSlot.cs`) | Add the full-suit set (e.g. `Legs`, `Hands`, `Arms`, `Waist`, `Wrist`, `Neck`, `Finger`, plus a second of any doubled slot). Pure enum + YAML extension — no architecture change (resolves the backlog "Equipment slot expansion" item). |
| Stat read inclusive of gear | `IStatSystem.Get` (**no change**) | `Get(Defense)` and `Get(AttackPower)` already fold `GetModifiers`; the contributor registration is the only wiring. `GetEffectiveAttackPower` drops its direct `DamageBonus` read (becomes `Body/2`). |
| Authoring of catalog + bonuses | `IItemBuilderSystem` (Items) + `setitem` + YAML + Blazor editor | `SetItemDamageBonus` generalizes to bonus-row authoring (add/clear `(ScoreId, magnitude)`); slot authoring already exists (`SetItemSlots`). INV-18 obligation — see Cross-cutting. |

### Family disposition

- **Build now:** the `WhileEquipped` equipment contributor; the `ScoreId`-keyed bonus rows; the slot expansion; the `DamageBonus` migration; armor→`Defense`; the authoring surface; tests.
- **Shape for later (keyed seam, zero extra code):** any `ScoreId`-addressable gear bonus (`+HpMax`, `+ManaMax`, speed, crit) — pure data once those scores carry derive/fold support.
- **Defer (backlog / horizon):**
  - **Per-aspect resistance gear** — resistance is *not* a `ScoreId`; it is `IAspectSystem.Resist` (independent per-aspect, base+effects). Routing gear into it pulls in the deferred `EffectParams.Aspect → AspectComposition?` migration and an aspect-resist contributor path. Out of scope (user chose AttackPower+Defense only).
  - **Item rarity & affixes (Spine D)** — affixes *are* `WhileEquipped` `StatModifier` effects rolled at spawn; this slice's seam is precisely what makes them "free later." Already on [feature-horizon](../design/feature-horizon.md) (§7 "Item rarity & affixes [D, C]"). Not opened here.
  - **Distinct armor/damage-reduction score** separate from `Defense` (combat-depth slice).
  - **Set bonuses** (composite effects keyed on equipped count) — lands with the curses/blessings composite-effect content slice.
  - **Subtype argument matching** ("wear armor" / "get sword") — independent QoL, already a backlog item; the broader catalog makes it more attractive but it is not required.

### Observers & contributors

- **Contributors:** this slice *adds* a contributor (equipment) to the existing `IEffectContributor` set that `EffectSystem.GetModifiers` aggregates — the canonical INV-24 use. `EquipmentEffectContributor` joins `AbilityEffectContributor` (passive abilities) under the same port; `StatSystem` and `EffectSystem` stay closed for modification.
- **Observers:** none new. Stat application is pull-on-read, so no `EffectAppliedEvent`-on-equip and no recompute signal. Existing `ItemEquippedEvent`/`ItemUnequippedEvent` (broadcast fan-out) are unchanged.

### Ordering & timing

None. `WhileEquipped` contributions are derived on read; they participate in no heartbeat tick and need no `EffectPhase` ordering. (Phase ordering matters only for `Timed`/`Periodic` effects the tick advances.)

### Invariants in tension

- **INV-24 / INV-2** — the contributor must live in the Items (domain) module and depend on the *core-owned* `IEffectContributor` port; `EffectSystem` (core) must not reference Items. Derived modifiers must be pulled on read, not stored.
- **INV-15** — write against the documented target (the contributor seam), not a new flat-field path; the effect-system and gameplay-model docs are that target.
- **INV-18** — adds gameplay state (per-item bonus rows + new slots); the authoring surface (`setitem` bonus rows, YAML schema, Blazor editor over the new field, `defs`/inspector visibility) ships in the same slice.
- **INV-25** — new contributor behavior + the `DamageBonus` migration + multi-slot armor summation + the `WhileEquipped`-not-stored persistence round-trip all need tests.
- **INV-16 / INV-29** — `reference/components.md` (`ItemDataComponent` field change), `reference/systems.md` (new contributor), and `reference/commands.md` (`setitem` change) update in-PR; the equipment-system + item-inventory-system + stat-system feature docs reconcile the `DamageBonus`→bonus-rows migration.

### Resolved decisions (from the advisor intake — do not relitigate)

1. **Scope** = slots + catalog + **the stat seam** (not slots/catalog alone). The slice wires `WhileEquipped` equipment contributions.
2. **Stat breadth** = `AttackPower` + `Defense` only this slice; other `ScoreId`s are free-as-data later; resistances/vitals explicitly deferred.
3. **`DamageBonus`** = **migrate** onto the unified effect path (deprecate the flat field), accepting the combat-read-path change.
4. **Slot set** = full suit **with doubled slots** — add 9: `Legs`, `Hands`, `Arms`, `Waist`, `Neck`, `Finger`, `Finger2`, `Wrist`, `Wrist2` (two rings, two wrist items). Each doubled slot is a distinct `WornSlot` enum value (no model change).
5. **`DamageBonus` migration** = **re-author in YAML, drop the field immediately** — no load-time shim. `DamageBonus` is removed from `ItemDataComponent`/`ItemTemplate`; the existing authored weapons' damage is re-authored by hand as `(AttackPower, n)` bonus rows in the same slice. The plan's content step must enumerate and re-author every existing weapon so none silently loses damage.

## Open questions

*(Both advisor-seed open questions are now planner-resolved — recorded here as settled decisions, no `TODO` remains.)*

1. **Bonus-row shape & authoring grammar — RESOLVED.** `ItemDataComponent.DamageBonus: int` → `List<EquipmentStatBonus> StatBonuses`, where `EquipmentStatBonus` is `public sealed record EquipmentStatBonus(ScoreId TargetScore, int Magnitude)` (`Core/ECS/Components/`). `[Persistent]` rides the existing class-level tag. `setitem` grammar: `setitem <id> bonus <score> <amount>` adds-or-replaces the row for `<score>` (amount `0` removes it); `setitem <id> clearbonus` clears all rows. YAML key `statBonuses:` — a list of `{ targetScore, magnitude }`. Blazor editor renders a repeating `(score, magnitude)` row editor. See Design notes.
2. **Combat read-path audit — RESOLVED.** Callers of `GetEffectiveAttackPower`/`GetEffectiveDefense` on the damage path are all in `CombatSystem` (`ExecuteRound` lines 79, 93; `ResolveAbilityStrike` line 110). They are repointed to `IStatSystem.Get(id, ScoreId.AttackPower)` / `Get(id, ScoreId.Defense)` — the only seams that fold the contributor. `GetEffectiveAttackPower` becomes `Body/2`; `GetEffectiveDefense` stays `Body/4`; both remain the base inside `Get`. This is pinned in WP-1 and guarded by a flow test (Test plan T-F1). See Design notes.

## Preconditions

- Target entity has a `CharacterComponent` (player) or `MobDataComponent` (mob) and an `EquipmentComponent` (auto-attached on first wear; absent = no worn-gear contributions).
- An item with `StatBonuses` is equipped only in a slot it declares in `WornSlots` (existing `EquipmentSystem` validation; unchanged).
- The effect substrate is wired: `IEffectSystem.GetModifiers` already sums all registered `IEffectContributor`s; `IStatSystem.Get(AttackPower|Defense)` already folds `GetModifiers`. No precondition work — the contributor registration is the only new wiring.
- Admin authoring a bonus has `AdminRequirement` (existing `setitem` gate; unchanged).

## Postconditions

- **P1.** `IStatSystem.Get(entityId, ScoreId.AttackPower)` for an entity wearing a weapon with a `(AttackPower, n)` row returns `Body/2 + n` (+ any other contributor/effect). *(invisible internal state — asserted)*
- **P2.** `IStatSystem.Get(entityId, ScoreId.Defense)` for an entity wearing armor with `(Defense, m)` rows across multiple slots returns `Body/4 + Σ m` summed over all worn armor pieces. *(invisible internal state — asserted)*
- **P3.** `EquipmentEffectContributor.GetModifiers(entityId, scoreId)` returns the sum of `Magnitude` over every worn item's `StatBonuses` rows matching `scoreId`; `0` when no `EquipmentComponent` or no matching rows. *(invisible internal state — asserted)*
- **P4.** No stored component is mutated by reading stats: worn-gear modifiers are derived on every read, never written into `EffectsComponent` or recomputed on an equip/unequip event. *(invisible internal state — asserted)*
- **P5.** `CombatSystem.ExecuteRound` damage reflects the worn weapon's `(AttackPower, n)` row (i.e. consumes `Get(AttackPower)`, not the bare `GetEffectiveAttackPower`); equivalently, removing the field did not drop weapon damage. *(behavioral regression guard — asserted)*
- **P6.** `ItemDataComponent`/`ItemTemplate` no longer carry `DamageBonus`; the field is gone from the component, template, YAML DTO (read + write), content-definition layer, content generator, and Blazor editor. Every previously `DamageBonus`-bearing authored/generated item is re-authored as a `(AttackPower, n)` row.
- **P7.** A persistent entity wearing equipment round-trips through save→load with its `EquipmentComponent.Slots` and each worn item's `StatBonuses` intact, and **no** `WhileEquipped` synthetic effect is persisted into `EffectsComponent`. *(persistence shape — asserted)*
- **P8.** `WornSlot` has the 9 new values (`Legs`, `Hands`, `Arms`, `Waist`, `Neck`, `Finger`, `Finger2`, `Wrist`, `Wrist2`); `setitem slot`, YAML `wornSlots`, and the Blazor slot checklist all accept them (enum-driven, automatic).
- **P9.** `setitem <id> bonus <score> <amount>` and `clearbonus` mutate both the live `ItemDataComponent` and the `ItemTemplate`, persist via `IItemContentWriter`, publish `ItemPropertySetByAdminEvent`, and survive `reload`.

## Main flow

*(The representative runtime path — a worn weapon influencing a combat round. Authoring and persistence are covered in their own flows below.)*

1. Admin authors a weapon: `setitem axe.1 bonus attackpower 6` → `SetitemCommand` parses `bonus`, resolves `(AttackPower, 6)`, calls `IItemBuilderSystem.SetItemStatBonus(itemEntityId, ScoreId.AttackPower, 6)`.
2. `ItemBuilderSystem.SetItemStatBonus` adds-or-replaces the `(AttackPower, 6)` row on both the live `ItemDataComponent.StatBonuses` and the `ItemTemplate.StatBonuses`; the command then publishes `ItemPropertySetByAdminEvent` and writes YAML via `IItemContentWriter`.
3. A player `wear axe` → existing `WearCommand`/`EquipmentSystem` path places the item id in `EquipmentComponent.Slots[MainHand]`. **No stat recompute, no new event** — the contributor reads live state.
4. Combat starts; on a heartbeat round `CombatSystem.ExecuteRound` calls `IStatSystem.Get(attackerId, ScoreId.AttackPower)`.
5. `StatSystem.Get(AttackPower)` returns `GetEffectiveAttackPower (Body/2)` + `IEffectSystem.GetModifiers(id, AttackPower)`.
6. `EffectSystem.GetModifiers` sums stored `StatModifier` effects **plus** every registered `IEffectContributor.GetModifiers`, including `EquipmentEffectContributor`.
7. `EquipmentEffectContributor.GetModifiers` reads `EquipmentComponent.Slots`, looks up each worn item's `ItemDataComponent.StatBonuses`, and sums `Magnitude` over rows where `TargetScore == AttackPower` → `6`.
8. `CombatSystem` computes raw damage from the gear-inclusive attack power; the round resolves through the existing aspect/HP path unchanged.

## Events fired

- **None new.** Worn-gear stat application is pull-on-read (P4), so there is no `EffectAppliedEvent`-on-equip and no recompute signal. Existing `ItemEquippedEvent` / `ItemUnequippedEvent` (broadcast fan-out) and `ItemPropertySetByAdminEvent` (authoring audit, reused by the `bonus`/`clearbonus` cases) are unchanged.

## Systems / handlers involved

| Element | Role | New / changed |
|---|---|---|
| `EquipmentEffectContributor : IEffectContributor` (Items, domain) | `GetModifiers(id, score)` sums worn-item `StatBonuses`; `GetActive(id)` yields one synthetic `WhileEquipped` `StatModifier` `Effect` per bonus row (for `affects`/inspection parity) | **new** |
| `IEffectSystem.GetModifiers` / `EffectSystem` (Effects, core) | already sums all `IEffectContributor`s — registration is the only wiring | reused, no change |
| `IStatSystem.Get` / `StatSystem` (Stats, core) | `Get(AttackPower|Defense)` already folds `GetModifiers`; `GetEffectiveAttackPower` drops its `DamageBonus` read → `Body/2` | changed (drop `DamageBonus` read) |
| `CombatSystem` (Combat, domain) | repoint 3 reads from `GetEffective*` to `Get(score)` | changed |
| `ItemDataComponent` (`Core/ECS/Components`) | `DamageBonus:int` → `List<EquipmentStatBonus> StatBonuses` | changed |
| `EquipmentStatBonus` record (`Core/ECS/Components`) | `(ScoreId TargetScore, int Magnitude)` pure-data row | **new** |
| `WornSlot` enum | +9 values | changed |
| `IItemBuilderSystem` / `ItemBuilderSystem` | `SetItemDamageBonus` → `SetItemStatBonus(id, score, magnitude)` + `ClearItemStatBonuses(id)` | changed |
| `SetitemCommand` | `dmg` case → `bonus <score> <amount>` + `clearbonus` cases | changed |
| `ItemTemplate` / `ItemTemplateDeserializer` / `ItemContentWriter` | `DamageBonus` → `StatBonuses` list (read + write DTO) | changed |
| `ContentGenerationSystem` / content-definition layer | generate `(AttackPower, rng)` row instead of `DamageBonus` | changed |
| `ItemEditor.razor` (Blazor) | replace `DamageBonus` number field with a `StatBonuses` row editor | changed |

## Implementation plan — work packages

### WP-1 — Stat seam: contributor + DamageBonus migration + combat read-path

**Scope.** Add `EquipmentStatBonus` record + `ItemDataComponent.StatBonuses` (drop `DamageBonus`); add `EquipmentEffectContributor` and register it `AddSingleton<IEffectContributor, EquipmentEffectContributor>()` in `ItemsModule`; drop the `DamageBonus` read in `StatSystem.GetEffectiveAttackPower` (→ `Body/2`); repoint `CombatSystem` reads to `Get(AttackPower)` / `Get(Defense)`.
**Files.** `Core/ECS/Components/{ItemDataComponent,EquipmentStatBonus}.cs`, `Core/Modules/Items/EquipmentEffectContributor.cs`, `Core/Modules/Items/ItemsModule.cs`, `Core/Modules/Stats/Systems/StatSystem.cs`, `Core/Modules/Combat/Systems/CombatSystem.cs`. Tests: `Hedron.Tests/Items/EquipmentEffectContributorTests.cs`; in `Hedron.Tests/Stats/StatSystemTests.cs` **remove the `GetEffectiveAttackPower_adds_MainHand_DamageBonus` group (the 6 tests asserting the typed getter adds the flat bonus — the behavior is deleted) and replace it with T-U3** (the bonus now rides `Get`); `Hedron.Tests/Combat/CombatFlowTests.cs` updates. The red from the deleted group is expected, not a regression — the on-touch ratchet replaces it with `Get`-folded coverage.
**Depends on.** Nothing — the effect/stat folds already exist.
**Out of scope.** Slots, authoring grammar, YAML/Blazor, content re-author (WP-2/WP-3).
**Exit criterion.** `Get(AttackPower)` returns `Body/2 + Σ(AttackPower rows)`; `Get(Defense)` sums armor rows across slots; combat damage reflects the weapon row; no `DamageBonus` symbol remains in Core. `dotnet test` green.

### WP-2 — Slots + authoring grammar (system + command + builder)

**Scope.** Add the 9 `WornSlot` values; `IItemBuilderSystem.SetItemStatBonus`/`ClearItemStatBonuses` (replace `SetItemDamageBonus`); `SetitemCommand` `bonus`/`clearbonus` cases with `ScoreId` + int parse/validation; update `ItemTemplate.StatBonuses` + `Apply`.
**Files.** `Core/WornSlot.cs`, `Core/Modules/Items/Systems/{IItemBuilderSystem,ItemBuilderSystem}.cs`, `Core/Modules/Items/Commands/SetitemCommand.cs`, `Core/Modules/Items/Templates/ItemTemplate.cs`. Tests: `Hedron.Tests/Authoring/ItemBuilderSystemTests.cs` (replace the `SetItemDamageBonus` block).
**Depends on.** WP-1 (the `StatBonuses` shape).
**Out of scope.** YAML round-trip + Blazor + content (WP-3).
**Exit criterion.** `setitem <id> bonus defense 3` then a re-read shows the row on component + template; `clearbonus` empties it; 9 new slots parse via `setitem slot`. `dotnet test` green.

### WP-3 — Content surface: YAML schema, generator, Blazor, content re-author (INV-18)

**Scope.** `statBonuses:` list in `ItemTemplateDeserializer` (read) + `ItemContentWriter` (write); `ContentGenerationSystem` emits an `(AttackPower, rng)` row; `ItemEditor.razor` row editor (read-back + edit); re-author the only `DamageBonus` carrier (the generator — no on-disk item YAML exists). Inspection rides the Blazor row list + `setitem` echo (no `defs item` family exists — see Content tooling).
**Files.** `Core/Modules/Items/{ItemTemplateDeserializer.cs,Systems/ItemContentWriter.cs}`, `Core/Modules/Authoring/Systems/ContentGenerationSystem.cs`, `Hedron.Web/Components/Pages/ItemEditor.razor`. Tests: `Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs` (item YAML round-trip — T-P2) + `ContentGenerationSystemTests.cs` updates.
**Depends on.** WP-1, WP-2.
**Out of scope.** Per-aspect resistance gear, affixes, set bonuses (deferred — see brief).
**Exit criterion.** YAML `statBonuses` round-trips losslessly; generator emits rows; Blazor saves them; no `DamageBonus` symbol remains anywhere. `dotnet test` green.

> The primary agent runs `architecture-reviewer` (code mode) across the combined WP-1…WP-3 diff once all land.

## Content tooling impact

*(INV-18 — adds gameplay state, so the authoring + inspection surface ships in-slice.)*

- **Data-file shape.** Item YAML gains `statBonuses:` — a list of `{ targetScore: <ScoreId>, magnitude: <int> }`. `damageBonus:` is removed from the DTO (read + write). `wornSlots:` gains 9 new accepted values (enum-driven, no DTO change).
- **Admin command.** `setitem <id> bonus <score> <amount>` (add-or-replace one row; `0` removes it) and `setitem <id> clearbonus` (clear all). Replaces `setitem <id> dmg <n>`. `LongDescription`/`Usage` updated to list the new slot names and the `bonus`/`clearbonus` grammar.
- **Inspection (as-built).** There is **no `defs item` branch** — `DefsCommand` covers only the registry families (aspect/ability/effect/score); items are `TemplateRegistry` templates, never a `defs` family, so `DamageBonus` never had a telnet read-back. Parity is preserved and improved: the authored bonuses are inspectable via the **Blazor editor row list** (read-back, updated this slice to the `StatBonuses` rows) and echoed by the `setitem bonus`/`clearbonus` confirmation. A dedicated in-game item-definition inspector (`defs item` / `iteminfo`) is logged to [`../roadmap/backlog.md`](../roadmap/backlog.md) as a follow-up — it is a new inspector surface, not a regression of this slice.
- **Blazor editor.** `ItemEditor.razor` replaces the single `DamageBonus` number field with a repeating `(ScoreId select, magnitude number)` row editor over `StatBonuses` (add/remove row). The 9 new slots appear automatically (the checklist is `Enum.GetValues<WornSlot>()`-driven).
- **TemplateRegistry.** No new template kind; `ItemTemplate` shape change only.

## Cross-cutting surfaces stressed

*(INV-19 — classify each surface.)*

- **ECS queries — Adequate.** Contributor reads `EquipmentComponent` + `ItemDataComponent` via `EntityService.TryGet`/`GetAllComponents`; the INV-24 contributor port is the canonical extension. No new query primitive.
- **Event bus — Adequate.** No new event; pull-on-read removes the need for an equip-recompute signal (P4). `ItemPropertySetByAdminEvent` is reused for `bonus`/`clearbonus`.
- **Persistence — Adequate (with audit below).** `StatBonuses` rides the existing class-level `[Persistent]` on `ItemDataComponent`; derived `WhileEquipped` modifiers are never stored (P7). See the opt-in audit.
- **Commands / authoring — Adequate.** `setitem` already has the multi-property switch + `IItemBuilderSystem` setter pattern; `bonus`/`clearbonus` follow it. `ScoreId` parse mirrors the existing `ItemType`/`WornSlot` `Enum.TryParse` cases.
- **Content templates — Adequate.** YAML DTO + `IItemContentWriter` round-trip pattern is established; `StatBonuses` is one more field on it.
- **Effect substrate — Adequate.** `IEffectContributor` + `GetModifiers` fold are the exact INV-24 seam; `EquipmentEffectContributor` mirrors `AbilityEffectContributor` 1:1. No `EffectSystem`/`StatSystem` modification (open-closed).
- **Combat read path — Gap closed in-slice (not a gap left open).** The direct `GetEffective*` calls were a latent bug surface the moment `DamageBonus` moved off the typed getter; WP-1 repoints them to `Get(score)`. Flagged here for the reviewer because it is the one place the migration can silently regress.
- **Time / ordering — Adequate.** `WhileEquipped` is derived-on-read; participates in no tick, needs no `EffectPhase`.

### Persistence opt-in audit (mandatory)

- **Level 1 — entity domain.** No new entity construction path. Items already transition world↔persistent via `ItemContextHandler` on pickup/drop (unchanged); equipment lives on the **player** (already `PersistentEntity`). World-spawn weapons/armor in rooms remain world content (no `PersistentEntity`) and re-spawn from templates. No change.
- **Level 2 — component inclusion.** `ItemDataComponent` is already `[Persistent]`; the new `StatBonuses` list (replacing the `[Persistent]`-covered `DamageBonus`) rides the same class tag — **correct**, as a player-owned item's authored bonuses must survive restart. `EquipmentStatBonus` is a pure-data record inside that list, no separate tag needed. `EquipmentComponent` already `[Persistent]` — unchanged. The synthetic `WhileEquipped` `Effect`s the contributor yields are **never written to `EffectsComponent`** (derived-on-read), so they are correctly absent from any snapshot — this is what P7's round-trip asserts.
- **Level 3 — save-on-change scope.** No new `SaveEntityAsync` call site. `setitem bonus`/`clearbonus` is an **admin boundary save** — the existing `setitem` path already persists the *template* via `IItemContentWriter` (YAML) and saves the entity through the established admin command; the `bonus` case reuses that path verbatim. No handler or non-admin command gains a `SaveEntityAsync` for a runtime state change. INV-22 satisfied.

## Flows introduced or modified

*(INV-17 — reference flows; never reproduce diagrams. The slice's PR updates these files.)*

- **Flow 17 — Combat journey** (`flow-17-kill-mob-combat-initiation.md`): the round's attack/defense reads now go through `IStatSystem.Get(AttackPower|Defense)` (folding the equipment contributor) instead of the bare `GetEffective*` getters. Update the round-pulse step's stat-read description.
- **Flow 21 — Effects journey** (`flow-21-effect-tick.md`): `EquipmentEffectContributor` joins `AbilityEffectContributor` as a registered `IEffectContributor` folded by `GetModifiers`. Note the second contributor in the GetModifiers description (no tick participation — `WhileEquipped` is derived-on-read).
- **Flow 13 — Equipment journey** (`flow-13-wear-item.md`): wear/remove now has a *real* stat consequence (previously a deferred hook). No new step — call out that the stat change is reflected on the next read, with no recompute event, in the cross-reference note.
- **Flow 08 / Flow 29 — Admin authoring / Content tooling**: `setitem bonus`/`clearbonus` replaces `dmg`; the YAML `statBonuses` schema replaces `damageBonus`. Update the `setitem` property list and the item YAML schema reference.

No new canonical flow file is introduced — all four are extensions of existing journeys.

## Test plan / Verification

*(INV-25 — derived from Postconditions + Main flow per `docs/architecture/07-testing.md`. Each new/changed system method and each invisible-state postcondition maps to a named test.)*

- **T-U1 — system-unit — `EquipmentEffectContributor.GetModifiers`** — wearing a weapon with `(AttackPower,6)` returns `6` for `AttackPower`, `0` for `Defense`; no `EquipmentComponent` → `0`. (P3)
- **T-U2 — system-unit — multi-slot armor summation** — three armor pieces `(Defense,2)`,`(Defense,3)`,`(Defense,1)` across `Chest`/`Feet`/`Head` (all pre-existing slots, so the test is self-contained within WP-1) → `GetModifiers(Defense)` = `6`. (P2)
- **T-U3 — system-unit — `StatSystem.Get(AttackPower)` with equipment contributor** — `Body=10`, weapon `(AttackPower,6)` → `Get(AttackPower)` = `5 + 6 = 11`; `GetEffectiveAttackPower` alone = `5` (proves the bonus rides `Get`, not the bare getter). (P1, P5)
- **T-U4 — system-unit — `Get(Defense)`** — `Body=20`, armor `(Defense,4)` → `Get(Defense)` = `5 + 4 = 9`. (P2)
- **T-U5 — system-unit — `EquipmentEffectContributor.GetActive`** — yields one `WhileEquipped` `StatModifier` `Effect` per bonus row, `Power == Magnitude`, correct `TargetScore`. (affects/inspection parity)
- **T-U6 — system-unit — `ItemBuilderSystem.SetItemStatBonus` / `ClearItemStatBonuses`** — add-or-replace updates both live `ItemDataComponent` and `ItemTemplate`; second call for same score replaces (no dup row); `0` magnitude removes; `clearbonus` empties; no-op for unknown entity. (P9) (replaces the `SetItemDamageBonus` tests)
- **T-F1 — flow — combat round consumes equipment attack power** — equip a weapon `(AttackPower,n)`, run `CombatSystem.ExecuteRound`, assert damage range reflects the gear-inclusive attack power (guards the read-path repoint; this is the regression test for the migration). (P5)
- **T-P1 — persistence round-trip — equipped persistent entity** — save→load a player wearing armor+weapon; assert `EquipmentComponent.Slots` and each item's `StatBonuses` survive, and `EffectsComponent` contains **no** `WhileEquipped` effect (derived-not-stored). (P4, P7)
- **T-P2 — content round-trip — item YAML `statBonuses`** — serialize an `ItemTemplate` with two bonus rows via `ItemContentWriter`, deserialize, assert rows equal; assert `damageBonus` absent. (P6)
- **T-G1 — architecture-guard — no `DamageBonus` symbol** — covered by the build: the symbol is deleted from `ItemDataComponent`/`ItemTemplate`/both DTOs/`IItemBuilderSystem`, so any lingering reference fails compilation (a dedicated reflection guard would be redundant). (P6)
- **Skipped:** `setitem bonus`/`clearbonus` parse-validation (`notascore`/`notanint`/arg-count) — thin command-tier orchestration; the suite has **no command-test harness** (no command is unit-tested) and the substantive logic (`SetItemStatBonus` add/replace/remove) is covered by T-U6. Consistent with the [07-testing.md](../architecture/07-testing.md) skip rubric for presentation/plumbing. Also skipped: exact `setitem`/`score` output prose (presentation); the `WornSlot` enum additions (pure data, exercised transitively by T-U2 and the enum-driven slot parse); the Blazor editor rendering (UI) — its save path is the `ItemContentWriter` round-trip (T-P2).

**As-built:** all named tests implemented and green — `dotnet test` = **627 passed, 0 failed**. New: `EquipmentEffectContributorTests` (T-U1…T-U5 + two-slot dedupe), `CombatFlowTests.Combat_round_uses_equipment_attack_power_via_Get` (T-F1), `RoundTripTests.Equipped_player_gear_bonuses_round_trip_and_are_not_stored_as_effects` (T-P1), `ContentDefinitionCatalogTests` item round-trip extended to `StatBonuses` (T-P2), `ItemBuilderSystemTests` `SetItemStatBonus`/`ClearItemStatBonuses` group (T-U6). `StatSystemTests` DamageBonus-getter group removed (behavior deleted).

**Testability:** no un-injected seam introduced — `EquipmentEffectContributor` is pure over ECS state; combat already injects `IRandom`. No INV-26 gap.

---

**Next:** spec-review gate **cleared** (`architecture-reviewer`, spec mode — *APPROVE WITH NITS*, no blocking findings; the three non-blocking nits — T-U2 self-contained slots, the explicit `StatSystemTests` DamageBonus-group removal, and the committed `defs` inspector deliverable — are applied above). Ready for `implement-plan`: build WP-1 → WP-2 → WP-3, then the **code-review gate** (`architecture-reviewer`, code mode) across the combined diff before merge.
