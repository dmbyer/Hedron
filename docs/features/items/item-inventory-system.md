# Item & Inventory System

> The domain system for item entity queries, ground/inventory mutation, argument resolution, and the item persistence lifecycle. **Authoring checkpoint:** slices 6, persistence-reform Stage C. Living document.

## What it is / does

`ItemSystem` is a **domain-tier pure system** that owns item entity query and mutation: it finds items in a room or a holder's inventory by name/keyword, moves items between ground and inventory, and exposes the raw entity-id lists that resolvers and commands use. It returns results and never publishes events or calls persistence (INV-5). Commands are the Initiators that publish events and save entities.

## How it works

### Item entity model

An item entity carries:

| Component | Purpose |
|---|---|
| `ItemDataComponent` | `Name`, `Description`, `Keywords`, `ItemType`, `WornSlots?`, `StatBonuses` (`List<EquipmentStatBonus>`) — all `[Persistent]` |
| `LocationComponent` | Set when on the ground; absent when in inventory |
| `InventoryComponent` (on holder) | `ItemEntityIds` — the item id list on the character or mob |
| `BlueprintComponent` | Cleared at pickup (`MoveToInventory`) per INV-21 |
| `PersistentEntity` | Added at pickup (`ItemContextHandler`), removed at drop |

`ItemType` enum (`None`, `Weapon`, `Armor`, `Consumable`, `Container`, `Misc`) is data only; slot validation reads `WornSlots`, not `ItemType`. `StatBonuses` is a list of `EquipmentStatBonus(ScoreId, int)` rows — the worn-gear stat contributions folded into `IStatSystem.Get` by `EquipmentEffectContributor` while the item is equipped (see [`equipment-system.md`](equipment-system.md#worn-gear-stat-contributions)).

### Room vs inventory

- **Room items**: entities with both `ItemDataComponent` and `LocationComponent.RoomEntityId == roomId`.
- **Inventory items**: entity ids in `InventoryComponent.ItemEntityIds` on the holder (no `LocationComponent`).

`GetItemsInRoom` and `GetItemsInInventory` are the list accessors. `TryFindItemInRoom` / `TryFindItemInInventory` perform linear prefix-match against `Name` and each keyword; first match wins.

### MoveToInventory and DropToRoom

`MoveToInventory(itemEntityId, holderEntityId)` — removes `LocationComponent` from the item (no-op if absent — handles race condition), appends item id to holder's `InventoryComponent.ItemEntityIds`, and unconditionally clears `BlueprintComponent` (INV-21: item is now independent of its template spawn slot).

`DropToRoom(itemEntityId, holderEntityId, roomEntityId)` — removes item id from `InventoryComponent`, attaches `LocationComponent { RoomEntityId, RoomBlueprintId }`. `DropCommand` saves only the player entity; the item is intentionally **not** saved (drop-and-vanish policy, see [items.md](items.md)).

### Argument resolvers

`ItemInRoomResolver` and `ItemInInventoryResolver` implement `IArgumentResolver`, each returning `ResolvedCandidate(MatchString, CanonicalValue)` — one for the item name and one per keyword. The parser deduplicates by `CanonicalValue` after prefix-match so aliases collapse to one result.

### Persistence lifecycle (pickup and drop)

`ItemContextHandler` (priority 20, `ItemPickedUpEvent` / `ItemDroppedEvent`) manages the flush-pool membership:
- On pickup: `EntityService.AddComponent(itemEntityId, new PersistentEntity())` — item enters the flush pool and survives restarts in inventory.
- On drop: `EntityService.RemoveComponent<PersistentEntity>(itemEntityId)` — item leaves the flush pool and vanishes on restart.

`SpawnSystem` (priority 20, `ItemPickedUpEvent`) marks the spawn slot vacant and schedules a respawn when a world-content item is picked up.

### Admin item authoring

`IItemBuilderSystem.CreateItem(name, roomEntityId)` mints an ad-hoc item (`item.adhoc.<shortid>` blueprint id), attaches all required components including `PersistentEntity` and `LocationComponent`, and registers an `ItemTemplate`. `MkitemCommand` calls `SaveEntityAsync` after this returns (INV-5: the system never calls persistence).

`SetItemSlots` updates both `ItemDataComponent.WornSlots` and the in-memory `ItemTemplate.WornSlots` so the slot assignment survives `@reload`. `SetItemStatBonus` (add-or-replace one `(ScoreId, magnitude)` row; magnitude 0 removes it) and `ClearItemStatBonuses` mirror the same dual-write pattern for `StatBonuses`.

`SetitemCommand` with `slot` property writes the updated template to YAML via `IItemContentWriter.WriteAsync` (system returns result; command writes disk — INV-5).

## Interface

The seam self-documents in code — describe behaviour here, not signatures:

- [`IItemSystem.cs`](../../../Core/Modules/Items/Systems/IItemSystem.cs) — `GetItemsInRoom` / `GetItemsInInventory` / `TryFindItemInRoom` / `TryFindItemInInventory` / `MoveToInventory` / `DropToRoom`. Pure: returns results, never touches the bus or persistence.
- [`IItemBuilderSystem.cs`](../../../Core/Modules/Items/Systems/IItemBuilderSystem.cs) — `CreateItem`, `SetItemName`, `SetItemDescription`, `SetItemKeywords`, `SetItemType`, `SetItemSlots`, `SetItemStatBonus`, `ClearItemStatBonuses`. Returns `ItemCreationResult`; never touches the bus or persistence.
- [`ItemDataComponent.cs`](../../../Core/ECS/Components/ItemDataComponent.cs) · [`InventoryComponent.cs`](../../../Core/ECS/Components/InventoryComponent.cs) — the `[Persistent]` data stores.

## Considerations

- **`InventoryComponent` migration guard.** `CharacterHydrationHandler` attaches an empty `InventoryComponent` to characters persisted before slice 6. The component is persisted on the character's next save-on-change event, not immediately.
- **`IArgumentResolver` deduplication.** Two different keyword matches on the same item are not ambiguous — they share `CanonicalValue`. Two different items sharing a keyword *are* ambiguous by design.
- **YAML `spawnRoomId`.** If the target room does not exist at spawn time, the item is created with no `LocationComponent` (in the void); admin can relocate via `setitem`.
- **`setitem slot` writes YAML (INV-5).** After `IItemBuilderSystem.SetItemSlots` mutates ECS and template state, `SetitemCommand` writes the YAML file via `IItemContentWriter` to keep disk and in-memory state in sync. Without this, a reload drops the slot assignment.

## Extensibility

- **Items persist where dropped.** Requires: save item entity on drop; remove `PlaceItemsInRooms` re-placement for items with a saved location. Additive change, no model change.
- **`PlaceItemsInRooms` Phase B (deferred).** A second pass to re-place restored template items with no `LocationComponent` and not in any inventory is noted in backlog.
- **Containers, stacking, weight.** `ItemType.Container` and `ItemType.Consumable` are data; behavior is deferred to later slices.

## Related

- [`items.md`](items.md) — the holistic feature view and player surfaces.
- [Items journey](../../architecture/flows/flow-09-item-pickup.md) — the runtime path for pickup, drop, and inventory display.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `ItemSystem`/`IItemSystem`, `ItemDataComponent`/`InventoryComponent` catalog rows.
- [`../../roadmap/completed/slice-6-items-and-inventory.md`](../../roadmap/completed/slice-6-items-and-inventory.md) — as-built record and design decisions.
- [`equipment-system.md`](equipment-system.md) — the worn-slot lifecycle that builds on the item model here.
