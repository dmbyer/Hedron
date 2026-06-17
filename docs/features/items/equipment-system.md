# Equipment System

> The domain system for equipment slot lifecycle: querying worn items, equipping from inventory (with implicit slot displacement), and removing back to inventory. **Authoring checkpoint:** slice 7. Living document.

## What it is / does

`EquipmentSystem` is a **domain-tier pure system** that owns the equipment slot lifecycle. It finds worn items by name/keyword, equips an item from inventory into its declared `WornSlot` entries (displacing existing occupants silently), and removes items back to inventory. All methods are pure ECS mutations; no event publication, no persistence calls (INV-5, INV-8). Commands publish events; `WearCommand` calls `SaveEntityAsync` after `EquipItem` returns.

## How it works

### Slot model

`ItemDataComponent.WornSlots: List<WornSlot>?` (null or empty = not wearable) declares which slots an item occupies. `WornSlot` enum: `MainHand`, `OffHand`, `Head`, `Chest`, `Feet`. Additional slots (Legs, Hands, Neck, Ring, etc.) are acknowledged debt — pure enum + YAML extension with no architecture change; tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

`EquipmentComponent.Slots: Dictionary<WornSlot, uint>` on the character (or mob) maps each occupied slot to an item entity id. It is `[Persistent]` and cross-cutting (`Core/ECS/Components/`) so mob entities can carry gear without a domain dependency on the Items module.

### EquipItem — the implicit-remove loop

`EquipItem(characterEntityId, itemEntityId)` is the single call `WearCommand` makes. Internally:

1. For each `WornSlot` declared on the item, if the slot is already occupied, calls `RemoveFromSlot` to silently return the displaced item to `InventoryComponent` (no event fired for the displaced item).
2. Removes the new item's id from `InventoryComponent.ItemEntityIds`.
3. Places the item id into each declared `EquipmentComponent.Slots` entry.

The command never iterates slots. This keeps `WearCommand` thin and keeps the "clear occupied slots before equipping" rule unit-testable against mock ECS state (INV-8).

**Implicit swap is silent.** When wearing displaces an existing item, no `ItemUnequippedEvent` fires for the displaced item — only `ItemEquippedEvent` fires for the new one. A future `autoswap no` player preference can add confirmation prompts; that requires the state-machine prompt infrastructure tracked in backlog.

### Two-hand weapons

An item with both `MainHand` and `OffHand` in `WornSlots` works without special-casing — `EquipItem` iterates the slot list and displaces occupants of each slot independently. Declaring both slots is sufficient.

### RemoveItem

`RemoveItem(characterEntityId, itemEntityId)` clears all `EquipmentComponent.Slots` entries mapping to this item, appends the item id to `InventoryComponent.ItemEntityIds`. `RemoveCommand` calls `GetWornSlots` first to capture the slot list for the `ItemUnequippedEvent` payload, then calls `RemoveItem`.

### Equipment display

`EquipmentCommand.ExecuteAsync` reads `EquipmentComponent.Slots` from the invoker. If empty → "You are not wearing anything." Otherwise builds an `EquipmentDisplayMessage` with one row per occupied slot (slot label + item name), ordered by `WornSlot` enum ordinal: `MainHand`, `OffHand`, `Head`, `Chest`, `Feet`. Unoccupied slots are omitted. No events fired.

### Worn-gear stat contributions

Equipment changes effective stats through the **effect contributor seam** (INV-24), not through `EquipmentSystem` or `StatSystem` directly. Each item carries authored `ItemDataComponent.StatBonuses` — a list of `EquipmentStatBonus(ScoreId TargetScore, int Magnitude)` rows. [`EquipmentEffectContributor`](../../reference/systems.md) (an `IEffectContributor` registered by `AddItemsModule`) reads the wearer's `EquipmentComponent.Slots`, and for each worn item yields its bonus rows as `WhileEquipped` `StatModifier` effects. `IStatSystem.Get` folds `IEffectSystem.GetModifiers`, which sums every contributor — so a weapon's `AttackPower` row and a breastplate's `Defense` row land in `Get(AttackPower)` / `Get(Defense)` with no change to `StatSystem` or `EffectSystem` (open/closed).

The contribution is **derived on read, never stored** (the [contributor seam](../effects/effect-system.md#the-contributor-seam) rule): no `EffectsComponent` entry is written on equip, no recompute event fires, and the next stat read simply reflects the current worn set. An item occupying two slots (a two-hand weapon in `MainHand` + `OffHand`) is deduped so its bonuses count once. Keying by `ScoreId` makes new bonus dimensions (`+HpMax`, future speed/crit) pure data. Authoring is via `setitem <id> bonus <score> <amount>` / `clearbonus` (`IItemBuilderSystem.SetItemStatBonus` / `ClearItemStatBonuses`).

### Migration guard

`CharacterHydrationHandler` attaches an empty `EquipmentComponent` to characters persisted before slice 7. The component is persisted on the character's next save-on-change event.

## Interface

The seam self-documents in code — describe behaviour here, not signatures:

- [`IEquipmentSystem.cs`](../../../Core/Modules/Items/Systems/IEquipmentSystem.cs) — `GetWornSlots` / `GetEquippedItems` / `TryFindEquippedItem` / `EquipItem` / `RemoveItem` / `RemoveFromSlot`. Pure: returns results, never touches the bus or persistence.
- [`EquipmentComponent.cs`](../../../Core/ECS/Components/EquipmentComponent.cs) — the `[Persistent]`, cross-cutting slot dictionary.

## Considerations

- **Stat effects ship via the effect contributor, not this system.** Worn gear contributes stats through `ItemDataComponent.StatBonuses` + [`EquipmentEffectContributor`](#worn-gear-stat-contributions) (the INV-24 seam), which `IStatSystem.Get(AttackPower|Defense)` folds on read. `EquipmentSystem` does **no** stat computation — it only owns slot lifecycle. `EquipmentComponent.Slots` is the source the contributor reads.
- **`OffHand` is deferred.** The enum value exists so YAML authors can declare it, but no player command uses it independently in slice 7. A future dual-wield or shield use-case leverages it without a schema change.
- **`BlueprintComponent` is already cleared.** `ItemSystem.MoveToInventory` clears `BlueprintComponent` at pickup (INV-21). `WearCommand` and `EquipmentSystem` need not interact with `BlueprintComponent`.
- **`setitem slot` writes YAML.** `SetitemCommand` calls `IItemContentWriter.WriteAsync` after `IItemBuilderSystem.SetItemSlots` so the slot assignment survives `@reload`. This is the command's responsibility, not the system's (INV-5).

## Extensibility

- **Additional worn slots.** Add enum values and YAML entries — no architecture change.
- **Stat bonuses.** Authored as `ItemDataComponent.StatBonuses` (`(ScoreId, magnitude)` rows) and folded by `EquipmentEffectContributor` into `IStatSystem.Get` — keyed by `ScoreId`, so any addressable score (`+HpMax`, future speed/crit) is a data addition, not a code change.
- **Mob gear.** `EquipmentComponent` is cross-cutting; no domain dependency added.

## Related

- [`items.md`](items.md) — the holistic feature view and player surfaces.
- [Equipment journey](../../architecture/flows/flow-13-wear-item.md) — the runtime path for wear and remove.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `EquipmentSystem`/`IEquipmentSystem`, `EquipmentComponent` catalog rows.
- [`../../roadmap/completed/slice-7-equipment.md`](../../roadmap/completed/slice-7-equipment.md) — as-built record and design decisions.
- [`item-inventory-system.md`](item-inventory-system.md) — the item entity model this system builds on.
