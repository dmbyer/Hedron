# Equipment journey (wear · remove)

> Source: [../../features/items/items.md](../../features/items/items.md)

[Back to flows index](README.md)

## Wear (`wear <item>`)

**Trigger.** Player sends `wear <item>`.

```mermaid
sequenceDiagram
    participant Client
    participant CD as CommandDispatcher
    participant Cmd as WearCommand
    participant IS as IItemSystem
    participant EqSys as IEquipmentSystem
    participant Bus as IEventBus
    participant Handler as EquipmentInteractionHandler (Notification 80)

    Client->>CD: "wear sword"
    CD->>Cmd: ExecuteAsync — ItemInInventoryResolver prefix-matches
    Cmd->>IS: TryFindItemInInventory(playerEntityId, canonical)
    IS-->>Cmd: itemEntityId
    Cmd->>EqSys: GetWornSlots(itemEntityId)
    EqSys-->>Cmd: [MainHand] (empty → "You can't wear that.")
    Cmd->>EqSys: EquipItem(playerEntityId, itemEntityId)
    Note over EqSys: For each occupied slot: RemoveFromSlot → inv (silent);<br/>remove item from inv; place in EquipmentComponent.Slots
    Cmd->>Bus: Publish(ItemEquippedEvent)
    Bus->>Handler: broadcast wear messages
    Cmd->>Persistence: SaveEntityAsync(playerEntityId)
```

**Steps.**

1. `ItemInInventoryResolver` builds candidates from the holder's `InventoryComponent`; parser prefix-matches.
2. `WearCommand.ExecuteAsync` calls `IItemSystem.TryFindItemInInventory`. Not found → "You aren't carrying that."
3. `IEquipmentSystem.GetWornSlots(itemEntityId)` reads `ItemDataComponent.WornSlots`. Empty → "You can't wear that."
4. `IEquipmentSystem.EquipItem(playerEntityId, itemEntityId)`: for each declared slot that is occupied, calls `RemoveFromSlot` (silently returns displaced item to inventory, no event); removes new item from `InventoryComponent`; places item id in each `EquipmentComponent.Slots` entry. Command never iterates slots (INV-8).
5. Publishes `ItemEquippedEvent(playerEntityId, itemEntityId, slots)`.
6. `EquipmentInteractionHandler` (priority 80): broadcasts `"<PlayerName> wears <ItemName>."` to the room; writes `"You wear <ItemName>."` to the player.
7. `WearCommand` calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)`.

---

## Remove (`remove <item>`)

**Trigger.** Player sends `remove <item>`.

```mermaid
sequenceDiagram
    participant Client
    participant CD as CommandDispatcher
    participant Cmd as RemoveCommand
    participant EqSys as IEquipmentSystem
    participant Bus as IEventBus
    participant Handler as EquipmentInteractionHandler (Notification 80)

    Client->>CD: "remove sword"
    CD->>Cmd: ExecuteAsync — ItemInEquipmentResolver prefix-matches
    Cmd->>EqSys: TryFindEquippedItem(playerEntityId, canonical)
    EqSys-->>Cmd: itemEntityId
    Cmd->>EqSys: GetWornSlots(itemEntityId)
    EqSys-->>Cmd: [MainHand]
    Cmd->>EqSys: RemoveItem(playerEntityId, itemEntityId)
    Note over EqSys: Clears EquipmentComponent.Slots entries;<br/>appends itemEntityId to InventoryComponent
    Cmd->>Bus: Publish(ItemUnequippedEvent)
    Bus->>Handler: broadcast remove messages
    Cmd->>Persistence: SaveEntityAsync(playerEntityId)
```

**Steps.**

1. `ItemInEquipmentResolver` builds candidates from `EquipmentComponent.Slots.Values`; parser prefix-matches.
2. `RemoveCommand.ExecuteAsync` calls `IEquipmentSystem.TryFindEquippedItem`. Not found → "You aren't wearing that."
3. `IEquipmentSystem.GetWornSlots(itemEntityId)` captures the slot list for the event payload.
4. `IEquipmentSystem.RemoveItem(playerEntityId, itemEntityId)`: clears all `EquipmentComponent.Slots` entries mapping to this item; appends item id to `InventoryComponent.ItemEntityIds`.
5. Publishes `ItemUnequippedEvent(playerEntityId, itemEntityId, slots)`.
6. `EquipmentInteractionHandler` (priority 80): broadcasts `"<PlayerName> removes <ItemName>."` to the room; writes `"You remove <ItemName>."` to the player.
7. `RemoveCommand` calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)`.

---

**Cross-references.**
- [`Core/Modules/Items/Commands/WearCommand.cs`](../../../Core/Modules/Items/Commands/WearCommand.cs) · [`Core/Modules/Items/Commands/RemoveCommand.cs`](../../../Core/Modules/Items/Commands/RemoveCommand.cs)
- [`Core/Modules/Items/Systems/EquipmentSystem.cs`](../../../Core/Modules/Items/Systems/EquipmentSystem.cs)
- [`Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs)
- [`../../features/items/equipment-system.md`](../../features/items/equipment-system.md) — system design, implicit-swap rule, and slot model.
