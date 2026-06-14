# Wearable Equipment Expansion (completed)

> Implemented on branch `claude/busy-lichterman-5540f5`, 2026-06-14. Living docs: [`items`](../../features/items/items.md) ([equipment-system → worn-gear stat contributions](../../features/items/equipment-system.md#worn-gear-stat-contributions)) · [`character-stats`](../../features/character-stats/stat-system.md). Commits: `1d6fefb` (plan), `946380f` (implementation).

## Outcome

Worn equipment now measurably changes a character's effective stats, data-driven and authorable. Each item carries authored `EquipmentStatBonus(ScoreId, int)` rows on `ItemDataComponent`; a new `EquipmentEffectContributor` (the INV-24 effect contributor seam) derives them on read as `WhileEquipped` `StatModifier`s, which `IStatSystem.Get(AttackPower|Defense)` folds. The flat `ItemDataComponent.DamageBonus` was removed and migrated onto this one path, and the combat round was repointed to read through `Get(...)` so weapon/armor bonuses land. `WornSlot` gained 9 values for a full armor suit (including doubled rings/wrists), and `setitem bonus`/`clearbonus` + YAML `statBonuses:` + the Blazor row editor author the bonuses. "A battleaxe and a dagger differ by their authored rows; a plate suit sums armor across slots" is now real.

## Behavior digest

*(As-specified snapshot; present truth lives in the feature/system docs.)*

**Preconditions:** target has `EquipmentComponent` (auto-attached on first wear; absent = no contribution); the effect substrate already sums `IEffectContributor`s inside `GetModifiers`, and `Get(AttackPower|Defense)` already folds `GetModifiers` — the contributor registration is the only new wiring.

**Postconditions:** `Get(AttackPower)` = `Body/2 + Σ(AttackPower rows)`; `Get(Defense)` = `Body/4 + Σ(Defense rows)` across all worn pieces; `EquipmentEffectContributor.GetModifiers` sums matching rows over worn items (0 if no `EquipmentComponent`); contributions are derived on every read, never written to `EffectsComponent` or recomputed on equip; combat damage reflects the worn weapon (consumes `Get`, not the bare getter); `DamageBonus` is gone from component/template/DTOs/generator/Blazor; a geared persistent entity round-trips its `EquipmentComponent.Slots` + each item's `StatBonuses` with no `WhileEquipped` effect persisted; the 9 new `WornSlot` values parse everywhere (enum-driven); `setitem bonus`/`clearbonus` dual-writes component + template, persists via `IItemContentWriter`, and publishes `ItemPropertySetByAdminEvent`.

**Main flow (representative):** admin `setitem axe.1 bonus attackpower 6` → `SetitemCommand` → `IItemBuilderSystem.SetItemStatBonus` (dual-writes component + template) → publish + YAML write. Player `wear axe` (existing path, no recompute/event). Combat round → `CombatSystem.ExecuteRound` → `IStatSystem.Get(AttackPower)` → folds `EffectSystem.GetModifiers` → sums `EquipmentEffectContributor.GetModifiers` (reads `EquipmentComponent.Slots`, sums each worn item's matching `StatBonuses`) → gear-inclusive attack power feeds damage.

## Shipped pieces

| Surface | Location |
|---|---|
| `EquipmentStatBonus(ScoreId TargetScore, int Magnitude)` — pure-data bonus row | `Core/ECS/Components/EquipmentStatBonus.cs` (new) |
| `ItemDataComponent.StatBonuses: List<EquipmentStatBonus>` (replaced `DamageBonus`) | `Core/ECS/Components/ItemDataComponent.cs` |
| `EquipmentEffectContributor : IEffectContributor` — derives worn-gear bonuses (two-slot dedupe) | `Core/Modules/Items/EquipmentEffectContributor.cs` (new) |
| Contributor DI registration (`IEffectContributor`) | `Core/Modules/Items/ItemsModule.cs` |
| `StatSystem` — `GetEffectiveAttackPower` now `Body/2` base-only; dropped `EntityService` dep | `Core/Modules/Stats/Systems/StatSystem.cs` · `IStatSystem.cs` |
| `CombatSystem` — 3 damage-path reads repointed to `Get(AttackPower)` / `Get(Defense)` | `Core/Modules/Combat/Systems/CombatSystem.cs` |
| `WornSlot` +9: `Legs, Hands, Arms, Waist, Neck, Finger, Finger2, Wrist, Wrist2` | `Core/WornSlot.cs` |
| `IItemBuilderSystem.SetItemStatBonus` / `ClearItemStatBonuses` (replaced `SetItemDamageBonus`) | `Core/Modules/Items/Systems/{IItemBuilderSystem,ItemBuilderSystem}.cs` |
| `setitem bonus <score> <amount>` / `clearbonus`; `value` now optional | `Core/Modules/Items/Commands/SetitemCommand.cs` |
| `StatBonuses` on `ItemTemplate` + `Apply`; YAML `statBonuses:` read/write | `Core/Modules/Items/Templates/ItemTemplate.cs` · `ItemTemplateDeserializer.cs` · `Systems/ItemContentWriter.cs` |
| Generator emits `(AttackPower, rng)` row | `Core/Modules/Authoring/Systems/ContentGenerationSystem.cs` |
| Blazor repeating `(ScoreId, magnitude)` row editor | `Hedron.Web/Components/Pages/ItemEditor.razor` |

## Tests shipped

`dotnet test` = **627 passed, 0 failed** (INV-25). New/changed:

- `Hedron.Tests/Items/EquipmentEffectContributorTests.cs` (new) — T-U1 `GetModifiers` (weapon bonus / 0 for other scores / 0 with no component); T-U2 multi-slot `Defense` summation (`Chest`/`Feet`/`Head`); two-slot dedupe; T-U3 `Get(AttackPower)` folds the contributor while the bare getter stays base-only; T-U4 `Get(Defense)`; T-U5 `GetActive` yields one `WhileEquipped` `StatModifier` per row (`Power == Magnitude`, item as source).
- `Hedron.Tests/Combat/CombatFlowTests.cs` — T-F1 `Combat_round_uses_equipment_attack_power_via_Get` (the read-path regression guard: prescribes a damage roll in range *only* if the +6 weapon bonus landed).
- `Hedron.Tests/Persistence/RoundTripTests.cs` — T-P1 geared player round-trips slots + each item's `StatBonuses`, no `EffectsComponent` written, and the contributor re-derives post-reload.
- `Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs` — T-P2 item YAML round-trip extended to two `StatBonuses` rows.
- `Hedron.Tests/Authoring/ItemBuilderSystemTests.cs` — T-U6 `SetItemStatBonus`/`ClearItemStatBonuses` group (add/replace/distinct-scores/zero-removes/clear/no-op), replacing the `SetItemDamageBonus` block.
- `Hedron.Tests/Authoring/ContentGenerationSystemTests.cs` — determinism comparison repointed off `DamageBonus` to a `StatBonuses` key.
- `Hedron.Tests/Stats/StatSystemTests.cs` — the `GetEffectiveAttackPower_adds_MainHand_DamageBonus` group **removed** (behavior deleted; on-touch ratchet); base-only getter tests retained.

Skipped (per the [07-testing.md](../../architecture/07-testing.md) rubric): `setitem bonus`/`clearbonus` parse-validation — thin command-tier orchestration with no command-test harness in the suite; substantive logic covered by T-U6. `T-G1` (no-`DamageBonus`-symbol) is covered by the build deleting the symbol.

## Decisions

- **The equipment-stats seam is the `WhileEquipped` effect contributor, not new flat fields (INV-15, INV-24).** The architecture was pre-shaped for this — the `WhileEquipped` lifetime (slice 9-e) and the `IEffectContributor` port (precedent `AbilityEffectContributor`) existed and were documented as the target ("equipment / aura / area — worn items — future, same port"). A parallel flat-field path (`ArmorBonus`, `DefenseBonus`, …) would have re-created the per-feature stat sprawl the effect substrate exists to prevent. The contributor lives in Items (domain) and depends only on the core port + `EntityService`; `EffectSystem`/`StatSystem` stay closed for modification (open/closed).
- **Pull-on-read, never materialized.** Worn-gear modifiers are derived from `EquipmentComponent` + `ItemDataComponent` on every read — never stored, never recomputed on an equip event. So **no new event** and no on-equip recompute: the next stat read reflects the new worn set. This kills the "did I recompute when gear changed?" bug class.
- **Bonuses keyed by `ScoreId` (shaped for later).** Only `AttackPower` and `Defense` flow through today, but any `ScoreId`-addressable score (`+HpMax`, future speed/crit) is a free data addition — no contributor change.
- **`DamageBonus` unified onto the one path; the combat read-path repoint was the load-bearing change.** `GetEffectiveAttackPower` does not itself fold modifiers — only `Get(AttackPower)` does. After dropping the field, the typed getter is base-only (`Body/2`), so the three `CombatSystem` damage-path reads were repointed to `Get(AttackPower)`/`Get(Defense)` — the single place weapon damage could silently vanish, pinned by the spec gate and guarded by T-F1. `StatSystem` shed its now-unused `EntityService` dependency.
- **Armor feeds the existing `Defense` score (minimal).** A distinct damage-reduction/armor-class score, and per-aspect resistance gear, were deferred (resistance is `IAspectSystem.Resist`, not a `ScoreId` — routing gear into it would pull in the deferred `EffectParams.Aspect → AspectComposition?` migration).
- **Two-of-a-kind slots are distinct enum values** (`Finger`/`Finger2`, `Wrist`/`Wrist2`) — `EquipmentComponent.Slots` holds one item per value; precedent is the two-hand weapon declaring both `MainHand` + `OffHand`. The contributor dedupes a multi-slot item so its bonuses count once.
- **Migration approach = re-author in YAML, drop the field immediately** (no load-time shim). No on-disk item YAML existed; the only `DamageBonus` carrier was `ContentGenerationSystem`, re-authored to emit an `(AttackPower, rng)` row.
- **Inspection parity.** There is no `defs item` family (items are `TemplateRegistry` templates, never a `defs` registry family), so `DamageBonus` never had a telnet read-back. Parity is the Blazor editor row list (updated to `StatBonuses`) + the `setitem` echo; a dedicated in-game item inspector is logged to the backlog.

**Deferred (named):** per-aspect resistance gear · item rarity & affixes (Spine D — affixes *are* `WhileEquipped` `StatModifier`s rolled at spawn, made "free later" by this seam) · distinct armor/DR score (combat-depth) · set bonuses (composite-effect content slice) · subtype argument matching · in-game item-definition inspector (`defs item`/`iteminfo`).

## Deviations / Follow-ups

- **Flows: plan over-promised, no edit needed (INV-17 satisfied).** The plan committed to editing flows 17/21/13/08/29, but the code-review gate verified the as-built flows were already accurate — flow-21 describes `GetModifiers` summing contributors *contributor-agnostically* (it does not enumerate individual contributors), and no flow carried a stale `GetEffective*`/`DamageBonus`/`dmg` reference. So no flow file changed and none needed to. Recorded here so a future reader isn't confused by the unfulfilled plan commitment.
- **Backlog updated:** "Equipment slot expansion" marked ✅ shipped; "In-game item-definition inspector (`defs item` / `iteminfo`)" added as a 🔵 follow-up.
- **Otherwise no deviations** — built as specified across WP-1…WP-3; spec gate (APPROVE WITH NITS, applied) and code gate (APPROVE, no blocking findings) both clean.
