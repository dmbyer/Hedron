# Equipment Swap

**Status:** partial
**Actors:** Player
**Module:** `Core/Modules/Inventory/` (item) / `Core/Modules/Equipment/`

## Description

A player equips a weapon while one is already equipped in the same slot. The new weapon replaces the old; stat bonuses swap accordingly.

## Preconditions

- Player has the target weapon in `InventoryComponent`
- Player has an item currently occupying the slot in `EquipmentComponent`
- Player state is Active or Combat

## Postconditions

- New weapon lives in `EquipmentComponent.Slots[slot]`
- Old weapon returns to `InventoryComponent`
- Stat-modifier delta applied to `AttributesComponent.EquipmentBonuses`
- Player is notified (swap message + stat changes if significant)
- Witnesses in the room are notified if the change is visible

## Main flow

1. `wear <item>` command → `EquipmentHandler`
2. `InventorySystem.HasItem` — locate the new item
3. `EquipmentSystem.GetEquipped(slot)` — capture the old item
4. `EquipmentSystem.Equip` → returns an `EquipResult` describing the change
5. Handler computes stat delta
6. Handler publishes `ItemEquippedEvent` (new) and `ItemUnequippedEvent` (old)
7. Subscribers: stats system updates modifiers; effects system swaps item effects; notification system messages player + witnesses

## Events fired

- `ItemUnequippedEvent` — old item leaving the slot
- `ItemEquippedEvent` — new item entering the slot

## Systems / handlers

- `EquipmentSystem`, `InventorySystem`, `AttributeCalculator` (core) via a domain attribute system
- `EquipmentHandler` — orchestrator
- `NotificationHandler` — player + witnesses
- `PersistenceHandler` — if equipment changes persist

## Related

- [../architecture/02-ecs.md](../architecture/02-ecs.md) — Equipment vs Inventory vs Container
- [potion-consumption.md](potion-consumption.md) — similar inventory mutation shape
