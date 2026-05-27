# Use Case: Stat Computation System

**Status:** implemented
**Actors:** Player, Mob, System, Administrator
**Module:** `Core/Modules/Stats/` (new); `Core/ECS/Components/` (`ItemDataComponent` extended); `Core/Modules/Attributes/` (`IAttributeSystem` extended); `Core/Modules/Items/` (`ItemTemplate`, `ItemTemplateDeserializer`, `IItemContentWriter`, `SetItemCommand` extended)

---

## Description

Introduces a dedicated `IStatSystem` that aggregates effective stat values from multiple sources (base attributes and equipment bonuses) into a single read seam for the combat slice and future consumers. Currently `IAttributeSystem` returns raw component values; combat needs *effective* values that sum base + equipment modifiers + future buff/effect modifiers. Without an aggregation seam, every consumer (combat, skills, UI) would repeat the same inline summation and would each need to change whenever a new modifier source is added. `IStatSystem` owns that seam permanently.

The slice also adds the first equipment-sourced modifier: a flat `DamageBonus` field on `ItemDataComponent` that contributes to `GetEffectiveAttackPower` when the item is equipped in `MainHand`. Future modifier sources (active effects, buffs, auras) will plug into `StatSystem` without changing the interface. Additionally, `IAttributeSystem` gains `SetCurrentHp`, the write-seam the combat slice will use to apply damage — decoupling write-path clamping from callers (INV-8).

**Prerequisite:** Slices 1–8a complete. `IAttributeSystem`, `IEquipmentSystem`, `ItemDataComponent`, `WornSlot.MainHand`, `AttributesComponent`, `PoolsComponent` must exist.

---

## Preconditions

- Slices 1–8a complete. Reused: `EntityService`, `IAttributeSystem`, `IEquipmentSystem`, `ItemDataComponent`, `InventoryComponent`, `EquipmentComponent`, `AttributesComponent`, `PoolsComponent`, `WornSlot`, `ITemplateRegistry`, `IContentSerializer`, `IItemContentWriter`, `SetItemCommand`, `AdminRequirement`, `IPersistenceSystem`.
- `IAttributeSystem` exists with `GetStrength`, `GetDexterity`, `GetConstitution`, `GetCurrentHp`, `GetMaxHp`, `SetLevel`, `SetStrength`, `SetDexterity`, `SetConstitution`, `SetMaxHp` (slice 8a).
- `IEquipmentSystem.GetEquippedItems(entityId)` exists and returns item entity ids (slice 7).
- `ItemDataComponent` has `Name`, `Description`, `Keywords`, `ItemType`, `WornSlots` (`[Persistent]`).
- `WornSlot.MainHand` enum value exists (slice 7).

---

## Postconditions

- `ItemDataComponent` gains `DamageBonus: int` (default 0). Tagged `[Persistent]` (already is as part of `ItemDataComponent`). Non-weapon items carry the default 0 — no discriminator needed; the field is harmless on non-weapons.
- `ItemTemplate`, `ItemTemplateDeserializer`, and `IItemContentWriter` gain optional `damageBonus` YAML field (absent or 0 = no bonus).
- `IAttributeSystem` gains `void SetCurrentHp(uint entityId, int value)`. The setter clamps `value` to `[0, MaxHp]` — the clamp game rule lives in the system, not in callers (INV-8). No events, no persistence (INV-5).
- `IStatSystem` (domain, `Core/Modules/Stats/Systems/`) exists with `StatSystem` implementation.
- `StatsModule` DI entry point exists (`Core/Modules/Stats/StatsModule.cs`), exposes `AddStatsModule(IServiceCollection)`, registers `IStatSystem` / `StatSystem` as singleton, called from `Server/Program.cs`.
- Admin `setitem <blueprintId> dmg <n>` extends `SetItemCommand` — sets `ItemDataComponent.DamageBonus` on the live item entity and updates the `ItemTemplate` record; calls `IItemContentWriter.WriteAsync` to write YAML; publishes `ItemPropertySetByAdminEvent` (extended `PropertyName = "damageBonus"`); calls `IPersistenceSystem.SaveEntityAsync`.

---

## Main Flow

### Flow A-1 — Effective stat read (combat slice consumer)

1. Combat system (or any consumer) calls `IStatSystem.GetEffectiveAttackPower(attackerEntityId)`.
2. `StatSystem.GetEffectiveAttackPower` calls `IAttributeSystem.GetStrength(entityId)` → `strength`. Computes base: `strength / 2`.
3. Reads `EquipmentComponent.Slots[WornSlot.MainHand]` via `EntityService.TryGet<EquipmentComponent>`. If the slot is occupied, reads `ItemDataComponent.DamageBonus` on the equipped item via `EntityService.TryGet<ItemDataComponent>`.
4. Returns `strength / 2 + mainHandDamageBonus` (0 if no weapon in `MainHand` or weapon has no bonus).

### Flow A-2 — Effective defense read

1. Combat system calls `IStatSystem.GetEffectiveDefense(defenderEntityId)`.
2. `StatSystem.GetEffectiveDefense` calls `IAttributeSystem.GetDexterity(entityId)`. Returns `dexterity / 4`. (Armor-slot bonus is future; no equipment loop in this slice for defense.)

### Flow A-3 — HP mutation (combat write path)

1. Combat system calls `IAttributeSystem.SetCurrentHp(entityId, newValue)`.
2. `AttributeSystem.SetCurrentHp` clamps `newValue` to `[0, GetMaxHp(entityId)]` and writes `PoolsComponent.CurrentHp`. No events, no persistence (INV-5). The caller (`CombatTickHandler`, slice 9) is responsible for event publication.

### Flow B-1 — Admin `setitem <blueprintId> dmg <n>`

1. **Privilege gate.** `AdminRequirement` checked by `CommandDispatcher` via `IAuthorizationChecker`.
2. `SetItemCommand.ExecuteAsync` parses `dmg <n>` as property=`dmg`, value=int.
3. Resolves the blueprint in `ITemplateRegistry` and live item entity via `BlueprintComponent`. Not found → error.
4. Validates `n >= 0`. Negative → "Damage bonus must be non-negative."
5. Calls `IItemBuilderSystem.SetItemDamageBonus(itemEntityId, n)` — sets `ItemDataComponent.DamageBonus` on the live entity and `ItemTemplate.DamageBonus` in the registry.
6. Calls `IItemContentWriter.WriteAsync(template)` — writes YAML atomically.
7. Publishes `ItemPropertySetByAdminEvent(adminEntityId, itemEntityId, "damageBonus", n.ToString())`. `AdminAuditHandler` logs.
8. Calls `IPersistenceSystem.SaveEntityAsync(itemEntityId)`.
9. Writes confirmation `PlainMessage`.

### Flow C-1 — Content loading (YAML → DamageBonus)

1. `WorldContentLoader.LoadAndSpawnAsync` calls `ItemTemplateDeserializer.Deserialize(fileBody)`.
2. Deserializer reads optional `damageBonus: int` (default 0). Populates `ItemTemplate.DamageBonus`.
3. `ItemTemplate.Apply(entity, entityService)` attaches `ItemDataComponent` with `DamageBonus` from template. Existing path unchanged; new field just added.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `ItemPropertySetByAdminEvent` (extended) | `SetItemCommand` | `uint AdminEntityId, uint ItemEntityId, string PropertyName, string NewValue` | Existing event, new `PropertyName="damageBonus"` case. Audit log. |

No new events. `IStatSystem` and `IAttributeSystem.SetCurrentHp` never publish events (INV-5).

---

## Design Notes

- **`DamageBonus` on `ItemDataComponent`, not a separate `CombatItemDataComponent`.** The early `combat.md` draft proposed `CombatItemDataComponent.WeaponDamageBonus`. This slice takes the opposite approach: `DamageBonus` belongs on `ItemDataComponent` because (a) it is authored alongside the item's other stats, (b) the YAML file is already the item authoring surface, (c) separating it would require two components to describe one authored weapon and two YAML write paths for one `setitem dmg` command, and (d) `ItemDataComponent` already carries `WornSlots`, which is equally "gear-mechanic" data.

- **`IStatSystem` is the read seam; `IAttributeSystem.SetCurrentHp` is the write seam.** The combat slice reads HP via `IStatSystem.GetCurrentHp` and writes via `IAttributeSystem.SetCurrentHp`. These are distinct concerns at distinct layers: `IStatSystem` aggregates (may apply modifiers in future); `IAttributeSystem` mutates the raw component. Callers never write `PoolsComponent` directly.

- **`GetEffectiveAttackPower` reads `EquipmentComponent` directly, not via `IEquipmentSystem.GetEquippedItems`.** `StatSystem` reads `EquipmentComponent.Slots[WornSlot.MainHand]` via a direct dictionary lookup rather than allocating a list from `GetEquippedItems` and filtering it. Both are INV-4-compliant; direct access is cheaper for the single-slot case.

- **`SetCurrentHp` clamp is the system's responsibility (INV-8).** The rule "`CurrentHp` must stay in `[0, MaxHp]`" is enforced inside `AttributeSystem.SetCurrentHp`. The combat system passes the raw computed value; the attribute system enforces the invariant. This prevents the rule from being duplicated across every future caller.

- **`GetEffectiveDefense` is dexterity-only for now.** Armor slot bonuses are acknowledged debt — the slot enum exists but no armor contributes defense yet. `GetEffectiveDefense` returns `dexterity / 4`; when armor lands, `StatSystem` sums armor bonuses here without changing the interface.

- **`IStatSystem` has no setter methods.** It is a pure read layer. All writes go through `IAttributeSystem` (raw component write) or `IEquipmentSystem` (slot mutation).

- **No events in `StatSystem` (INV-5).** `StatSystem` never publishes events. It is a computation layer, not an Initiator or handler.

- **`StatsModule` is a thin DI registration.** No hosted service, no event handler, no configuration binding. Its only job is `services.AddSingleton<IStatSystem, StatSystem>()`.

---

## Related

- [`attributes.md`](attributes.md) — slice 8a; `IAttributeSystem`, `AttributesComponent`, `PoolsComponent` are extended here with `SetCurrentHp`.
- [`equipment.md`](equipment.md) — slice 7; `IEquipmentSystem`, `EquipmentComponent`, `WornSlot.MainHand` are read by `StatSystem`.
- [`items-and-inventory.md`](items-and-inventory.md) — slice 6; `ItemDataComponent` is extended with `DamageBonus`; `IItemBuilderSystem`, `IItemContentWriter`, `SetItemCommand` are extended.
- [`combat.md`](combat.md) — slice 9; primary consumer of `IStatSystem.GetEffectiveAttackPower`, `GetEffectiveDefense`, `GetCurrentHp`, and `IAttributeSystem.SetCurrentHp`.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; `[Persistent]` on `ItemDataComponent` ensures `DamageBonus` survives restart automatically.
- [`output-framework.md`](output-framework.md) — slice 4; `SetItemCommand` writes `PlainMessage` confirmation via `IOutputWriter`.
- [`command-framework.md`](command-framework.md) — slice 3; `SetItemCommand` extends an existing `ICommand` registration.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
