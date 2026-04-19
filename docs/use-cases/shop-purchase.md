# Shop Purchase

**Status:** planned
**Actors:** Player, Shop (room or shopkeeper entity)
**Module:** `Core/Modules/Shop/`

## Description

A player buys an item from a shop. Currency is deducted; a fresh item entity is spawned from the shop's catalog (template reference), not moved from the shop's inventory, so stock can be infinite or finite per config.

## Preconditions

- Player is in a room with `ShopComponent` (or adjacent to a shopkeeper carrying it)
- Player has a `CurrencyComponent` with funds ≥ item price
- The requested item exists in `ShopComponent.Catalog`
- Item is in stock (if stock is finite)

## Postconditions

- Player's `CurrencyComponent` is debited by the item price
- Shop's `CurrencyComponent` is credited (if tracked)
- A new item entity is spawned and added to the player's inventory
- Shop stock is decremented (if finite)

## Main flow

1. `buy <item>` command → `ShopHandler`
2. `ShopSystem.ResolveShop(player)` — locate shop in current location
3. `ShopSystem.FindCatalogEntry(shop, itemName)` — match item
4. `CurrencySystem.CanAfford(player, price)` — gate
5. `CurrencySystem.Transfer(player, shop, price)`
6. `TemplateRegistry.Spawn(catalogEntry.TemplateId)` → item entity
7. `InventorySystem.AddItem(player, item)`
8. `ShopHandler` publishes `ItemPurchasedEvent`
9. `NotificationHandler` messages the player

## Events fired

- `ItemPurchasedEvent` _(planned)_
- `CurrencyChangedEvent` _(planned)_

## Systems / handlers

- `ShopSystem`, `CurrencySystem`, `InventorySystem`
- `ShopHandler` — orchestrator
- `NotificationHandler`, `PersistenceHandler`

## Design notes

- **Spawn, don't move.** Shop inventory is a catalog (template references), not live stock. This keeps restocking and pricing logic simple.
- Dynamic pricing (haggling, reputation) is a later enhancement via `PricingSystem`.

## Related

- [../reference/components.md](../reference/components.md) — ShopComponent, CurrencyComponent
- [container-looting.md](container-looting.md) — instance-moving counterpart
