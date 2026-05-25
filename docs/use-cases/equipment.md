# Use Case: Equipment — Wear and Remove

**Status:** implemented
**Actors:** Player, Administrator
**Module:** `Core/Modules/Items/` (extended); `Core/ECS/Components/` (cross-cutting equipment)

---

## Description

Extends items with wearable and wieldable equipment. Players can `wear` an item from their inventory, placing it into a named equipment slot on their character. They can `remove` a worn item to return it to inventory. The `equipment` command (aliases `eq`) displays all currently worn items by slot. Wearing an item to a slot that is already occupied automatically removes the existing item to inventory first (implicit swap). This slice is intentionally scoped to the infrastructure: no stat bonuses, no armor class calculations, no combat integration. Those land in the combat and skills slices. The goal here is the slot lifecycle (item in inventory → worn → back to inventory) and the supporting ECS and command machinery.

**Prerequisite:** Slice 6 (`items-and-inventory.md`) must be merged. This slice depends on `ItemDataComponent`, `InventoryComponent`, `IItemSystem`, `ItemInInventoryResolver`, and `ItemPickedUpEvent` / `ItemDroppedEvent` infrastructure.

---

## Preconditions

- Slice 6 complete: `ItemDataComponent` (with `ItemType`), `InventoryComponent`, `IItemSystem`, `IItemBuilderSystem`, `ItemInInventoryResolver`, `ResolvedCandidate` resolver contract.
- A player has a live character with `InventoryComponent` and at least one item carried.
- Items have `ItemDataComponent.WornSlots` populated (by admin `setitem <blueprintId> slot <value>` introduced here, or from YAML authoring).

---

## Postconditions

- `ItemDataComponent` gains `WornSlots: IReadOnlyList<WornSlot>?` (null or empty = not wearable/wieldable). This field is `[Persistent]` (already is, as part of `ItemDataComponent`).
- `WornSlot` enum exists: `MainHand`, `OffHand`, `Head`, `Chest`, `Feet`. Additional slots (Legs, Hands, Neck, Ring) are acknowledged debt — see Design Notes.
- A cross-cutting `EquipmentComponent` exists (for player and mob entities), containing `Dictionary<WornSlot, uint> Slots`. It is `[Persistent]`. `AccountSystem.CreateCharacterAsync` attaches an empty `EquipmentComponent` to every new character.
- `wear <item>` takes an item from the invoker's inventory by name/keyword (using `ItemInInventoryResolver`); validates that the item has at least one `WornSlot`; for each target slot, if the slot is occupied the existing item is silently removed to inventory first; places the item in the slot(s); saves player entity; publishes `ItemEquippedEvent`; broadcasts to room.
- `remove <item>` takes a worn item by name/keyword (using `ItemInEquipmentResolver`); moves it to inventory; saves player entity; publishes `ItemUnequippedEvent`; broadcasts to room.
- `equipment` (aliases `eq`) renders all occupied slots with slot label + item name, or "You are not wearing anything." when all slots are empty. No events.
- Admin `setitem <blueprintId> slot <value>` sets `WornSlots` on an item (extends the Phase A admin command). `value` is a space-separated list of `WornSlot` names (e.g. `mainhand`, `chest`).

---

## Main Flow

### Flow — `wear <item>`

1. **Argument resolve.** `WearCommand` declares `item` as a `Token` with `ItemInInventoryResolver`. Resolver builds `ResolvedCandidate` list from invoker's `InventoryComponent.ItemEntityIds`. Unique match → canonical item name.
2. **Entity resolve.** `IItemSystem.TryFindItemInInventory(playerEntityId, canonicalName, out itemEntityId)`. Not found → "You aren't carrying that."
3. **Slot check.** `IEquipmentSystem.GetWornSlots(itemEntityId)` reads `ItemDataComponent.WornSlots`. Empty or null → "You can't wear that." (not a wearable item).
4. **Equip (with internal implicit remove).** `IEquipmentSystem.EquipItem(playerEntityId, itemEntityId)` is the single call the command makes. Internally, `EquipItem` iterates `ItemDataComponent.WornSlots`; for each slot that is already occupied it calls `RemoveFromSlot(playerEntityId, slot)` to silently move the existing item back to `InventoryComponent` (no event for the implicit remove — it is a side-effect of `wear`, not a distinct player action). After clearing all affected slots, it removes the new item from `InventoryComponent.ItemEntityIds` and places it in each `EquipmentComponent.Slots` entry. The command never iterates slots itself.
5. **Event + save.** Command publishes `ItemEquippedEvent(PlayerEntityId, ItemEntityId, IReadOnlyList<WornSlot> Slots)`. Calls `SaveEntityAsync(playerEntityId)`.
6. **Handler.** `EquipmentInteractionHandler` (priority 80): broadcasts `"<PlayerName> wears <ItemName>."` to room (excluding wearer); writes `"You wear <ItemName>."` to player.

### Flow — `remove <item>`

1. **Argument resolve.** `RemoveCommand` declares `item` as `Token` with `ItemInEquipmentResolver`. Resolver builds candidates from all items in `EquipmentComponent.Slots.Values`.
2. **Entity resolve.** `IEquipmentSystem.TryFindEquippedItem(playerEntityId, canonicalName, out itemEntityId)`. Not found → "You aren't wearing that."
3. **Remove.** `IEquipmentSystem.RemoveItem(playerEntityId, itemEntityId)` clears the slot(s), appends item id to `InventoryComponent.ItemEntityIds`.
4. **Event + save.** Publishes `ItemUnequippedEvent(PlayerEntityId, ItemEntityId, IReadOnlyList<WornSlot> Slots)`. Calls `SaveEntityAsync(playerEntityId)`.
5. **Handler.** `EquipmentInteractionHandler` (subscribed to `ItemUnequippedEvent`): broadcasts `"<PlayerName> removes <ItemName>."` to room; writes `"You remove <ItemName>."` to player.

### Flow — `equipment`

1. `EquipmentCommand.ExecuteAsync` reads `EquipmentComponent.Slots` from invoker. If empty → "You are not wearing anything."
2. Builds an `EquipmentDisplayMessage` with one row per occupied slot: slot label (e.g. `[Main Hand]`) + item name, ordered by `WornSlot` enum ordinal. Unoccupied slots are omitted.
3. No events fired.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `ItemEquippedEvent` | `WearCommand` | `uint PlayerEntityId, uint ItemEntityId, IReadOnlyList<WornSlot> Slots` | Room broadcast; future stat recalc hook |
| `ItemUnequippedEvent` | `RemoveCommand` | `uint PlayerEntityId, uint ItemEntityId, IReadOnlyList<WornSlot> Slots` | Room broadcast; future stat recalc hook |

---

## Design Notes

- **`EquipmentComponent` is cross-cutting.** Mobs and players both wear gear; placing it in `Core/ECS/Components/` means neither `Core/Modules/Items/` nor `Core/Modules/Mobs/` has to take a cross-module dependency for what is conceptually shared state.
- **Implicit swap is silent.** When wearing an item displaces another, there is no "You remove X to make room for Y" message — only "You wear Y." is sent. This simplifies the flow and avoids multi-step confirmation logic. A future player-config option (`autoswap no`) can add confirmation prompts; that requires the state-machine prompt infrastructure referenced in the user notes as acknowledged debt (see backlog).
- **Two-hand weapons.** An item that fills both `MainHand` and `OffHand` declares both in `WornSlots`. `EquipItem` iterates the slot list and implicitly removes items from each occupied slot. This means a two-hander correctly displaces a main-hand weapon and an off-hand shield.
- **`OffHand` is deferred.** The `WornSlot` enum includes `OffHand` so YAML authors can declare it, but no player-facing command uses it in this slice unless an item explicitly declares both `MainHand` and `OffHand`. A future "dual-wield" or "shield" use case can leverage this without a schema change.
- **Slot display order.** `EquipmentDisplayMessage` renders slots in a canonical order defined by the enum ordinal: `MainHand`, `OffHand`, `Head`, `Chest`, `Feet`. This is deterministic and consistent across clients.
- **Additional slots (Legs, Hands, Neck, Ring, etc.) are acknowledged debt.** Adding them is a pure enum + YAML extension with no architecture change. Tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md).
- **Stat effects are explicitly out of scope.** When combat lands (slice 9), `EquipmentSystem` (or a dedicated `StatSystem`) will query `EquipmentComponent.Slots` to compute effective attack/defense. The slot data is ready; the computation is deferred.
- **`setitem slot` writes YAML.** After mutating `WornSlots` on `ItemDataComponent` and `ItemTemplate`, the command writes the updated template back to disk via `IItemContentWriter.WriteAsync(template)` to keep the YAML file and in-memory state in sync. Without this, a content reload drops the slot assignment.
- **`BlueprintComponent` is cleared before `wear` is ever called (INV-21).** Slice 6 `ItemSystem.MoveToInventory` unconditionally clears `BlueprintComponent` at pickup. By the time a player can invoke `wear`, the item entity is already decoupled from its template. `WearCommand` and `EquipmentSystem` need not take any action with respect to `BlueprintComponent`.
- **`EquipItem` owns the implicit-remove loop (INV-8).** `WearCommand` calls only `EquipItem(playerEntityId, itemEntityId)` — one call. The per-slot iteration and displacement logic live entirely inside `EquipmentSystem`, keeping the command thin and the game rule unit-testable.
- **`EquipmentDisplayMessage` scope.** Used exclusively by the `equipment` command to render the slot table to the requesting player. Room descriptions do not display worn items in this slice.

---

## Related

- [`items-and-inventory.md`](items-and-inventory.md) — slice 6; prerequisite; provides `ItemDataComponent`, `InventoryComponent`, `IItemSystem`, resolver infrastructure.
- [`command-prefix-matching.md`](command-prefix-matching.md) — slice 3a; `IArgumentResolver` contract; `ItemInEquipmentResolver` follows the pattern established in slice 6.
- [`output-framework.md`](output-framework.md) — slice 4; `EquipmentDisplayMessage` plugs into the same formatter pipeline.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; `EquipmentComponent` persistence follows the two-level model.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
