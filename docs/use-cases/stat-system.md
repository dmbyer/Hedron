# Use Case: Stat Computation System

**Status:** planned
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
- `IStatSystem` (domain, `Core/Modules/Stats/Systems/`) exists with `StatSystem` implementation (see interface shape in Systems / Handlers section).
- `StatsModule` DI entry point exists (`Core/Modules/Stats/StatsModule.cs`), exposes `AddStatsModule(IServiceCollection)`, registers `IStatSystem` / `StatSystem` as singleton, called from `Server/Program.cs`.
- Admin `setitem <blueprintId> dmg <n>` extends `SetItemCommand` — sets `ItemDataComponent.DamageBonus` on the live item entity and updates the `ItemTemplate` record; calls `IItemContentWriter.WriteAsync` to write YAML; publishes `ItemPropertySetByAdminEvent` (extended `PropertyName = "damageBonus"`); calls `IPersistenceSystem.SaveEntityAsync`.

---

## Main Flow

### Flow A-1 — Effective stat read (combat slice consumer)

1. Combat system (or any consumer) calls `IStatSystem.GetEffectiveAttackPower(attackerEntityId)`.
2. `StatSystem.GetEffectiveAttackPower` calls `IAttributeSystem.GetStrength(entityId)` → `strength`. Computes base: `strength / 2`.
3. Calls `IEquipmentSystem.GetEquippedItems(entityId)` to obtain all equipped item entity ids. For each, checks `EntityService.TryGetComponent<ItemDataComponent>` and whether the item occupies `WornSlot.MainHand` (by checking `ItemDataComponent.WornSlots` contains `MainHand`). If the `EquipmentComponent.Slots` key `MainHand` resolves to an item entity, reads `ItemDataComponent.DamageBonus` on that item.
4. Returns `strength / 2 + mainHandDamageBonus` (0 if no weapon in `MainHand` or weapon has no bonus).

### Flow A-2 — Effective defense read

1. Combat system calls `IStatSystem.GetEffectiveDefense(defenderEntityId)`.
2. `StatSystem.GetEffectiveDefense` calls `IAttributeSystem.GetDexterity(entityId)`. Returns `dexterity / 4`. (Armor-slot bonus is future; no equipment loop in this slice for defense.)

### Flow A-3 — HP mutation (combat write path)

1. Combat system calls `IAttributeSystem.SetCurrentHp(entityId, newValue)`.
2. `AttributeSystem.SetCurrentHp` clamps `newValue` to `[0, GetMaxHp(entityId)]` and writes `PoolsComponent.CurrentHp`. No events, no persistence (INV-5). The caller (`CombatPulseService`, a future Initiator) is responsible for event publication.

### Flow B-1 — Admin `setitem <blueprintId> dmg <n>`

1. **Privilege gate.** `AdminRequirement` checked by `CommandDispatcher` via `IAuthorizationChecker`.
2. `SetItemCommand.ExecuteAsync` parses `dmg <n>` as property=`dmg`, value=int. Not a recognized property → error.
3. Resolves the blueprint in `ITemplateRegistry` and live item entity via `BlueprintComponent`. Not found → error.
4. Validates `n >= 0`. Negative → "Damage bonus must be non-negative."
5. Sets `ItemDataComponent.DamageBonus = n` on the live entity. Updates `ItemTemplate.DamageBonus = n` in the registry.
6. Calls `IItemContentWriter.WriteAsync(template)` — writes YAML atomically.
7. Publishes `ItemPropertySetByAdminEvent(adminEntityId, itemEntityId, blueprintId, "damageBonus", n.ToString())`. `AdminAuditHandler` logs.
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
| `ItemPropertySetByAdminEvent` (extended) | `SetItemCommand` | `uint AdminEntityId, uint ItemEntityId, string BlueprintId, string PropertyName, string NewValue` | Existing event, new `PropertyName="damageBonus"` case. Audit log. |

No new events. `IStatSystem` and `IAttributeSystem.SetCurrentHp` never publish events (INV-5).

---

## Systems / Handlers Involved

### New: `IStatSystem` (domain, `Core/Modules/Stats/Systems/`)

```csharp
public interface IStatSystem
{
    int GetEffectiveStrength(uint entityId);
    int GetEffectiveDexterity(uint entityId);
    int GetEffectiveConstitution(uint entityId);
    /// Strength / 2 + MainHand item DamageBonus (0 if none).
    int GetEffectiveAttackPower(uint entityId);
    /// Dexterity / 4. Armor-slot bonus deferred (future).
    int GetEffectiveDefense(uint entityId);
    int GetCurrentHp(uint entityId);
    int GetMaxHp(uint entityId);
}
```

`StatSystem` implementation rules:
- `GetEffectiveStrength` → `IAttributeSystem.GetStrength(entityId)`.
- `GetEffectiveDexterity` → `IAttributeSystem.GetDexterity(entityId)`.
- `GetEffectiveConstitution` → `IAttributeSystem.GetConstitution(entityId)`.
- `GetEffectiveAttackPower` → `IAttributeSystem.GetStrength(entityId) / 2` + MainHand item `ItemDataComponent.DamageBonus`. Reads `EquipmentComponent.Slots[WornSlot.MainHand]` via `EntityService.TryGetComponent<EquipmentComponent>`, then reads `ItemDataComponent.DamageBonus` via `EntityService.TryGetComponent<ItemDataComponent>` on the equipped item — no `is`/`as` (INV-4).
- `GetEffectiveDefense` → `IAttributeSystem.GetDexterity(entityId) / 4`.
- `GetCurrentHp` → `IAttributeSystem.GetCurrentHp(entityId)`.
- `GetMaxHp` → `IAttributeSystem.GetMaxHp(entityId)`.
- Never publishes events, never calls persistence (INV-5). Pure aggregation.
- **Future extension point:** when `ActiveEffectsComponent` lands, `StatSystem` sums modifiers from that component into the same getter implementations without changing the interface.

### Extended: `IAttributeSystem` (`Core/Modules/Attributes/Systems/`)

New method added:
```csharp
/// Sets CurrentHp, clamped to [0, MaxHp]. Game rule enforced here (INV-8). No events, no persistence (INV-5).
void SetCurrentHp(uint entityId, int value);
```

`AttributeSystem.SetCurrentHp` implementation:
- Reads `PoolsComponent.MaxHp` (via `GetMaxHp(entityId)`).
- Clamps `value` to `[0, maxHp]`.
- Writes `PoolsComponent.CurrentHp = clampedValue`.
- No event bus call, no persistence call.

### Extended: `IItemBuilderSystem` / `SetItemCommand` (`Core/Modules/Items/`)

`SetItemCommand` extended with `dmg` property keyword. Delegates mutation to a new `IItemBuilderSystem.SetItemDamageBonus(uint itemEntityId, int value, ItemTemplate template)` method (or equivalent inline mutation following the `SetAttribute` pattern from `IMobBuilderSystem`). Updates both live `ItemDataComponent.DamageBonus` and `ItemTemplate.DamageBonus`. Does not call persistence or events (INV-5).

### Extended: `ItemTemplate` / `ItemTemplateDeserializer` / `IItemContentWriter`

- `ItemTemplate` gains `int DamageBonus { get; init; }` (default 0).
- `ItemTemplateDeserializer` reads optional `damageBonus: int` YAML key (absent = 0).
- `IItemContentWriter.WriteAsync` writes `damageBonus: <n>` to YAML (omit when 0 is acceptable but explicit write is also fine for readability).
- `ItemTemplate.Apply` attaches `ItemDataComponent { ..., DamageBonus = template.DamageBonus }`.

### Extended: `ItemDataComponent` (`Core/ECS/Components/`)

New field: `int DamageBonus { get; set; }` = 0. `[Persistent]` (inherited from `ItemDataComponent` class attribute).

### Reused (no change): `IEquipmentSystem`, `EquipmentComponent`, `WornSlot`, `AdminAuditHandler`, `IPersistenceSystem`, `IAuthorizationChecker`

---

## Content Tooling Impact

**New gameplay state:** `ItemDataComponent.DamageBonus` (authored per-item attack power contribution). INV-18 requires tooling in the same slice.

**Admin command extended:** `setitem <blueprintId> dmg <n>` — lets designers set weapon damage bonus on any item entity. `n` is a non-negative integer. The value is persisted to the item YAML file immediately (save-on-change pattern).

**YAML field extended (items):** Optional `damageBonus: int` in item YAML files. Zero (or absent) means no contribution to attack power. Example:

```yaml
kind: item
blueprintId: item.sword.short
name: a short sword
keywords: [sword, short sword]
type: Weapon
wornSlots: [MainHand]
damageBonus: 3
```

**Designer workflow:** author a weapon via `mkitem`, set `dmg` via `setitem <blueprintId> dmg 3`, equip it in-game — `score` (slice 8a) will still show base stats; effective attack power is visible in combat narrative (slice 9).

**`IItemContentWriter` extended** to write the `damageBonus` field; `ItemTemplateDeserializer` extended to read it. No new YAML `kind`; no new file extension.

---

## Cross-Cutting Surfaces Stressed

### Commands — Adequate

`setitem dmg` is a new property on the existing `SetItemCommand`. The command follows the established `ICommand` + `ICommandDispatcher` + `AdminRequirement` + `IAuthorizationChecker` pattern. No framework change needed.

### Output — Adequate

`SetItemCommand` writes a `PlainMessage` confirmation via `IOutputWriter`. No new message shape required. `IStatSystem` is a pure read layer with no output responsibility.

### Persistence — see sub-check below.

### Event bus — Adequate

No new events. `ItemPropertySetByAdminEvent` with `PropertyName="damageBonus"` is an existing event shape reused. `IStatSystem` and `AttributeSystem.SetCurrentHp` are never publishers (INV-5).

### ECS queries — Adequate

`StatSystem.GetEffectiveAttackPower` reads `EquipmentComponent` then `ItemDataComponent` — two `TryGetComponent` calls per invocation. Called per combat round (future), not per tick globally. At Phase 3 scale, linear lookups are acceptable. No new query pattern introduced.

### Broadcast — Not stressed

`IStatSystem` is a read layer; no broadcast.

### Time / heartbeat — Not stressed

No timed behavior in this slice.

### Content templates — Adequate (extended)

`ItemTemplate` + `ItemTemplateDeserializer` + `IItemContentWriter` gain one optional `damageBonus` int field. This follows the established extension pattern from slice 8a (`MobTemplate` gained attribute fields). No new serializer, no new `kind`.

### Configuration — Not stressed

No new config keys.

### Sessions — Not stressed

`IStatSystem` is entity-keyed, not session-keyed.

### Modules — Adequate

`StatsModule` is new, following the `Add*Module(IServiceCollection)` extension pattern. Registered in `Server/Program.cs` after `AttributesModule`. No new DI infrastructure.

---

### Persistence opt-in audit

**Level 1 — entity opt-in.**

No new entity construction paths. Item entities already carry `PersistentEntity` from `IItemBuilderSystem.CreateItem` and `WorldContentLoader.SpawnMissingEntities`. This slice only adds a field to `ItemDataComponent`; the opt-in decision was made in slice 6.

**Level 2 — component `[Persistent]` status.**

| Component | `[Persistent]`? | Rationale |
|---|---|---|
| `ItemDataComponent` (existing, extended) | Yes (already tagged) | `DamageBonus` is authored weapon state. It must survive restart — a weapon that loses its damage bonus on restart is broken. No change to the tag; the field is included automatically. |
| `EquipmentComponent` (existing, read-only) | Yes (already tagged, slice 7) | Not modified by this slice. `StatSystem` reads it; no change. |
| `AttributesComponent` (existing, read-only) | Yes (already tagged, slice 8a) | `SetCurrentHp` writes `PoolsComponent`, not `AttributesComponent`. No change. |
| `PoolsComponent` (existing, written by `SetCurrentHp`) | Yes (already tagged, slice 8a) | `CurrentHp` mutations are covered by the area-scoped periodic flush. `SetCurrentHp` does not call persistence itself — the combat pulse Initiator (slice 9) owns the save cadence. No change to persistence rules. |

**Level 3 — entity ID stability.**

`ItemDataComponent.DamageBonus` is a new field on existing item entities. Items spawned by `WorldContentLoader.SpawnMissingEntities` already have their IDs saved immediately at startup (Level 3 guard from slice 6). No new entities of this type are introduced; no change needed.

**Level 4 — restore vs. spawn placement guard.**

No new `PlaceXInRooms`-style logic. Not applicable.

---

## Flows Introduced or Modified

### Modified: Flow 5 — Content reload (`reload`)

`ItemTemplateDeserializer` now reads `damageBonus`. Newly spawned items from templates with `damageBonus > 0` will have `ItemDataComponent.DamageBonus` set correctly. Existing live item entities are not mutated (additive-only reload constraint is unchanged). The flow description in `flows/README.md` does not need a structural change — the YAML field is an extension of the existing item deserialization step in step 4.

### Modified: Flow 12 — Admin item creation (`mkitem`)

`IItemBuilderSystem.CreateItem` now attaches `ItemDataComponent` with `DamageBonus = 0` (the default). No flow step changes; the component shape note in the flow references catalog should mention the new field. `flows/README.md` step 3 updated to add `DamageBonus` to the `ItemDataComponent` field list.

The implementation PR must update `flows/README.md` Flow 12 step 3 to add `DamageBonus: 0` to the `ItemDataComponent` shape note.

No new canonical flow is introduced by this slice. `IStatSystem` is a read layer called by the combat system (slice 9, Flow 17 — Combat round pulse) — that flow is owned by slice 9 and will reference `IStatSystem`.

### Reference catalog updates (INV-16)

The implementation PR must update the following reference catalogs in the same PR:

- **`docs/reference/systems.md`** — Add `IStatSystem` / `StatSystem` entry (domain, `Core/Modules/Stats/Systems/`, interface shape, dependencies). Update the `IAttributeSystem` entry to add `SetCurrentHp(uint entityId, int value)` with its clamp behavior note.
- **`docs/reference/components.md`** — Update the `ItemDataComponent` row to include `DamageBonus: int` (default 0, `[Persistent]` via class-level attribute).
- **`docs/use-cases/README.md`** — Already updated (index row added by the planner).

---

## Design Notes

- **`DamageBonus` on `ItemDataComponent`, not a separate `CombatItemDataComponent`.** `combat.md` (the planned combat spec, not yet implemented) proposed a `CombatItemDataComponent` for weapon damage to avoid adding a combat-specific field to the general-purpose `ItemDataComponent`. This slice takes the opposite approach: `DamageBonus` belongs on `ItemDataComponent` because (a) it is authored alongside the item's other stats, (b) the YAML file is already the item authoring surface, (c) separating it would require two components to describe one authored weapon and two YAML write paths for one `setitem dmg` command, and (d) `ItemDataComponent` already carries `WornSlots`, which is equally "gear-mechanic" data. `combat.md` must be updated before implementation: replace all references to `CombatItemDataComponent` and `WeaponDamageBonus` with `ItemDataComponent.DamageBonus`. The open-question section calls this out explicitly.

- **`IStatSystem` is the read seam; `IAttributeSystem.SetCurrentHp` is the write seam.** The combat slice reads HP via `IStatSystem.GetCurrentHp` and writes via `IAttributeSystem.SetCurrentHp`. These are distinct concerns at distinct layers: `IStatSystem` aggregates (may apply modifiers in future); `IAttributeSystem` mutates the raw component. Callers never write `PoolsComponent` directly.

- **`GetEffectiveAttackPower` implementation detail.** Rather than calling `IEquipmentSystem.GetEquippedItems` and iterating the list, `StatSystem` reads `EquipmentComponent.Slots[WornSlot.MainHand]` directly via `EntityService.TryGetComponent<EquipmentComponent>`. This is a direct dictionary lookup rather than a list scan. Both approaches are INV-4-compliant; direct component access is cheaper for the single-slot case.

- **`SetCurrentHp` clamp is the system's responsibility (INV-8).** The rule "`CurrentHp` must stay in `[0, MaxHp]`" is enforced inside `AttributeSystem.SetCurrentHp`, not inside `ICombatSystem.ExecuteRound`. The combat system passes the raw computed value; the attribute system enforces the invariant. This prevents the rule from being duplicated across every future caller.

- **`GetEffectiveDefense` is dexterity-only for now.** Armor slot bonuses are acknowledged debt — the slot enum exists but no armor contributes defense yet. `GetEffectiveDefense` returns `dexterity / 4`; when armor lands, `StatSystem` sums armor bonuses here without changing the interface.

- **`IStatSystem` has no setter methods.** It is a pure read layer. All writes go through `IAttributeSystem` (raw component write) or `IEquipmentSystem` (slot mutation). This separates the aggregation concern from the mutation concern cleanly.

- **No events in `StatSystem` (INV-5).** `StatSystem` never publishes events. It is a computation layer, not an Initiator or handler.

- **`StatsModule` is a thin DI registration.** No hosted service, no event handler, no configuration binding. Its only job is `services.AddSingleton<IStatSystem, StatSystem>()` and any dependencies that `StatSystem` needs that aren't already registered.

- **Combat.md divergence.** The `combat.md` use case (already planned, not yet implemented) references `CombatItemDataComponent` and `WeaponDamageBonus`. That spec was written before this aggregation seam was designed. The implementation PR for slice 9-c must update `combat.md` Design Notes to reflect `ItemDataComponent.DamageBonus` as the canonical field and remove the `CombatItemDataComponent` design. The `combat.md` Systems / Handlers section references to `CombatItemDataComponent` in `ExecuteRound` must be replaced with `IStatSystem.GetEffectiveAttackPower`.

---

## Open Questions

1. **`combat.md` alignment.** `combat.md` (planned, not implemented) specifies `CombatItemDataComponent.WeaponDamageBonus` and proposes that `ICombatSystem.ExecuteRound` reads it directly. This slice chooses `ItemDataComponent.DamageBonus` aggregated by `IStatSystem`. The architecture reviewer must confirm that `combat.md` is updated before slice 9 implementation begins — the two specs must not coexist with conflicting field names. **Resolution needed before any slice-9 code is written.**

2. **`IStatSystem.GetEffectiveAttackPower` MainHand lookup strategy.** The spec says `StatSystem` reads `EquipmentComponent.Slots[WornSlot.MainHand]` directly rather than using `IEquipmentSystem.GetEquippedItems`. This couples `StatSystem` to `EquipmentComponent` directly. Alternative: call `IEquipmentSystem.GetEquippedItems` and filter by `WornSlot.MainHand`. Either is INV-4-compliant. The architecture reviewer should confirm which dependency relationship is preferred (`IEquipmentSystem` vs. direct `EntityService` query). Recommendation: direct `EntityService.TryGetComponent<EquipmentComponent>` + slot dictionary lookup is simpler and avoids the list allocation from `GetEquippedItems`.

3. **`IItemBuilderSystem` extension shape.** The spec calls for `IItemBuilderSystem.SetItemDamageBonus(uint itemEntityId, int value, ItemTemplate template)`. This follows the `IMobBuilderSystem.SetAttribute` pattern. Alternative: the `SetItemCommand` mutates `ItemDataComponent.DamageBonus` inline (no new builder method). The architecture reviewer should confirm which pattern to follow — the builder method is cleaner (INV-2 discipline), the inline mutation is simpler given that there's only one new property. If a `SetItemDamageBonus` method is added, `IItemBuilderSystem` needs a catalog update.

---

## Related

- [`attributes.md`](attributes.md) — slice 8a; `IAttributeSystem`, `AttributesComponent`, `PoolsComponent` are extended here with `SetCurrentHp`.
- [`equipment.md`](equipment.md) — slice 7; `IEquipmentSystem`, `EquipmentComponent`, `WornSlot.MainHand` are read by `StatSystem`.
- [`items-and-inventory.md`](items-and-inventory.md) — slice 6; `ItemDataComponent` is extended with `DamageBonus`; `IItemBuilderSystem`, `IItemContentWriter`, `SetItemCommand` are extended.
- [`combat.md`](combat.md) — slice 9; primary consumer of `IStatSystem.GetEffectiveAttackPower`, `GetEffectiveDefense`, `GetCurrentHp`, and `IAttributeSystem.SetCurrentHp`. **`combat.md` must be updated before slice 9 implementation to remove `CombatItemDataComponent` references.**
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; `[Persistent]` on `ItemDataComponent` ensures `DamageBonus` survives restart automatically.
- [`output-framework.md`](output-framework.md) — slice 4; `SetItemCommand` writes `PlainMessage` confirmation via `IOutputWriter`.
- [`command-framework.md`](command-framework.md) — slice 3; `SetItemCommand` extends an existing `ICommand` registration.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
