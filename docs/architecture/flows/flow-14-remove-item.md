# Flow 14 — `remove <item>`

> [Back to flows index](README.md)

**Summary.** Player removes a worn item from their equipment slots. The item moves from `EquipmentComponent.Slots` back to `InventoryComponent`. The player entity is saved; the room is notified.

**Trigger.** Player sends `remove <item>`.

```mermaid
sequenceDiagram
    participant Player
    participant Dispatcher as CommandDispatcher
    participant Cmd as RemoveCommand
    participant EqSys as IEquipmentSystem
    participant Bus as IEventBus
    participant Handler as EquipmentInteractionHandler

    Player->>Dispatcher: "remove sword"
    Dispatcher->>Cmd: ExecuteAsync(context)
    Cmd->>EqSys: TryFindEquippedItem(playerEntityId, "sword")
    EqSys-->>Cmd: itemEntityId
    Cmd->>EqSys: GetWornSlots(itemEntityId)
    EqSys-->>Cmd: [MainHand]
    Cmd->>EqSys: RemoveItem(playerEntityId, itemEntityId)
    Note over EqSys: Clears slot(s) in EquipmentComponent;<br/>appends itemEntityId to InventoryComponent
    Cmd->>Bus: PublishAsync(ItemUnequippedEvent)
    Bus->>Handler: HandleAsync(ItemUnequippedEvent) [priority 80]
    Handler-->>Player: "You remove a short sword."
    Handler-->>Player: (others) "Korin removes a short sword."
    Cmd->>Persistence: SaveEntityAsync(playerEntityId)
```

**Steps.**

1. `CommandDispatcher` routes `remove` to `RemoveCommand`.
2. `ItemInEquipmentResolver` builds `ResolvedCandidate` list from the invoker's `EquipmentComponent.Slots.Values`; prefix-match selects the canonical item name.
3. `RemoveCommand.ExecuteAsync` calls `IEquipmentSystem.TryFindEquippedItem(playerEntityId, canonicalName)`. On miss: "You aren't wearing that."
4. Calls `IEquipmentSystem.GetWornSlots(itemEntityId)` to capture the slot list for the event payload.
5. Calls `IEquipmentSystem.RemoveItem(playerEntityId, itemEntityId)`: clears all `EquipmentComponent.Slots` entries that map to this item, appends the item id to `InventoryComponent.ItemEntityIds`.
6. Publishes `ItemUnequippedEvent(playerEntityId, itemEntityId, slots)`.
7. `EquipmentInteractionHandler` (priority 80): reads `LocationComponent` from the player; broadcasts `"<PlayerName> removes <ItemName>."` to others; writes `"You remove <ItemName>."` to the player.
8. `RemoveCommand` calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)`.

**Cross-references.**
- [`Core/Modules/Items/Commands/RemoveCommand.cs`](../../../Core/Modules/Items/Commands/RemoveCommand.cs)
- [`Core/Modules/Items/Systems/EquipmentSystem.cs`](../../../Core/Modules/Items/Systems/EquipmentSystem.cs)
- [`Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs)
- [`docs/use-cases/equipment.md`](../../use-cases/equipment.md) — slice 7 spec
