# Flow 13 — `wear <item>`

> [Back to flows index](README.md)

**Summary.** Player wears a named item from their inventory. The item moves from `InventoryComponent` into `EquipmentComponent.Slots`. If any target slot is already occupied, the existing item is silently displaced back to inventory first. The player entity is saved; the room is notified.

**Trigger.** Player sends `wear <item>`.

```mermaid
sequenceDiagram
    participant Player
    participant Dispatcher as CommandDispatcher
    participant Cmd as WearCommand
    participant ItemSys as IItemSystem
    participant EqSys as IEquipmentSystem
    participant Bus as IEventBus
    participant Handler as EquipmentInteractionHandler

    Player->>Dispatcher: "wear sword"
    Dispatcher->>Cmd: ExecuteAsync(context)
    Cmd->>ItemSys: TryFindItemInInventory(playerEntityId, "sword")
    ItemSys-->>Cmd: itemEntityId
    Cmd->>EqSys: GetWornSlots(itemEntityId)
    EqSys-->>Cmd: [MainHand]
    Cmd->>EqSys: EquipItem(playerEntityId, itemEntityId)
    Note over EqSys: RemoveFromSlot for each occupied slot → inv;<br/>remove item from inv; place in Slots
    Cmd->>Bus: PublishAsync(ItemEquippedEvent)
    Bus->>Handler: HandleAsync(ItemEquippedEvent) [priority 80]
    Handler-->>Player: "You wear a short sword."
    Handler-->>Player: (others) "Korin wears a short sword."
    Cmd->>Persistence: SaveEntityAsync(playerEntityId)
```

**Steps.**

1. `CommandDispatcher` routes `wear` to `WearCommand`.
2. `ItemInInventoryResolver` builds `ResolvedCandidate` list from the invoker's `InventoryComponent.ItemEntityIds`; prefix-match selects the canonical item name.
3. `WearCommand.ExecuteAsync` calls `IItemSystem.TryFindItemInInventory(playerEntityId, canonicalName)`. On miss: "You aren't carrying that."
4. Calls `IEquipmentSystem.GetWornSlots(itemEntityId)` — reads `ItemDataComponent.WornSlots`. Empty → "You can't wear that."
5. Calls `IEquipmentSystem.EquipItem(playerEntityId, itemEntityId)`. Internally: for each declared slot, `RemoveFromSlot` displaces any existing item (silently, no event); then removes item id from `InventoryComponent`; places item id in each `EquipmentComponent.Slots` entry. Command never iterates slots.
6. Publishes `ItemEquippedEvent(playerEntityId, itemEntityId, slots)`.
7. `EquipmentInteractionHandler` (priority 80): reads `LocationComponent` from the player to find the room; broadcasts `"<PlayerName> wears <ItemName>."` to others; writes `"You wear <ItemName>."` to the player.
8. `WearCommand` calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)`.

**Cross-references.**
- [`Core/Modules/Items/Commands/WearCommand.cs`](../../../Core/Modules/Items/Commands/WearCommand.cs)
- [`Core/Modules/Items/Systems/EquipmentSystem.cs`](../../../Core/Modules/Items/Systems/EquipmentSystem.cs)
- [`Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs)
- [`docs/use-cases/equipment.md`](../../use-cases/equipment.md) — slice 7 spec
