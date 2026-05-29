# Flow 10 — Item drop (`drop`)

> [Back to flows index](README.md)

**Summary.** Player sends `drop <item>`. `DropCommand` uses `ItemInInventoryResolver` to prefix-match against carried items, calls `IItemSystem.DropToRoom` to move the item from inventory to the ground, publishes `ItemDroppedEvent`. `ItemContextHandler` removes `PersistentEntity` from the item (it will vanish on restart); `ItemInteractionHandler` broadcasts the drop messages. No immediate save — the player entity is persisted in the next flush cycle.

**Trigger.** Player sends `drop <item>`.

```mermaid
sequenceDiagram
    participant Client
    participant CD as CommandDispatcher
    participant Parser as ICommandArgumentParser
    participant Resolver as ItemInInventoryResolver
    participant Cmd as DropCommand
    participant IS as IItemSystem
    participant Bus as IEventBus
    participant ICH as ItemContextHandler (Domain 20)
    participant IIH as ItemInteractionHandler (Notification 80)
    participant Broadcast as IBroadcastSystem

    Client->>CD: "drop sword"
    CD->>Parser: Parse(schema, "sword", resolverContext)
    Parser->>Resolver: GetCandidates(resolverContext)
    Resolver->>IS: GetItemsInInventory(playerEntityId)
    Resolver-->>Parser: [ResolvedCandidate("a short sword","a short sword"), ...]
    Parser-->>CD: ParsedArguments{item="a short sword"}
    CD->>Cmd: ExecuteAsync(CommandContext)
    Cmd->>IS: TryFindItemInInventory(playerEntityId, "a short sword", out itemEntityId)
    IS-->>Cmd: true, itemEntityId
    Cmd->>IS: DropToRoom(itemEntityId, playerEntityId, roomEntityId)
    Cmd->>Bus: Publish(ItemDroppedEvent)
    Bus->>ICH: HandleAsync — RemoveComponent<PersistentEntity>(itemEntityId)
    Bus->>IIH: HandleAsync — broadcast drop messages
    IIH->>Broadcast: SendToRoomAsync(roomId, "Bob drops a short sword.", id≠player)
    IIH->>Broadcast: SendToRoomAsync(roomId, "You drop a short sword.", id==player)
    Note over Cmd: item entity removed from flush pool — vanishes on restart
```

**Steps.**

1. `CommandDispatcher` routes `drop` to `DropCommand`. No privilege requirement.
2. **Argument resolution.** `ItemInInventoryResolver.GetCandidates` reads the invoker's `InventoryComponent.ItemEntityIds` and builds `ResolvedCandidate` pairs for each carried item's name and keywords. Deduplication and substitution are the same as in [Flow 9](flow-09-item-pickup.md).
3. **Entity resolve.** `IItemSystem.TryFindItemInInventory(playerEntityId, canonicalName, out itemEntityId)`. Not found → "You aren't carrying that."
4. **Drop mutation.** `IItemSystem.DropToRoom(itemEntityId, playerEntityId, roomEntityId)` — removes item id from `InventoryComponent.ItemEntityIds`, attaches `LocationComponent { RoomEntityId, RoomBlueprintId }` to the item.
5. **Event.** Publishes `ItemDroppedEvent(PlayerEntityId, ItemEntityId, RoomEntityId)`.
6. **Context demotion.** `ItemContextHandler` (priority 20) calls `EntityService.RemoveComponent<PersistentEntity>(itemEntityId)`. The item entity leaves the flush pool and will not be written on the next flush or on shutdown. If the server restarts before the player picks the item back up, the entity is gone — the template's spawn system will eventually respawn a fresh instance at the original spawn location.
7. **Broadcast.** `ItemInteractionHandler` (priority 80) broadcasts drop messages (same filter pattern as pickup, reversed flavour text).

**Cross-references.**
- [`Core/Modules/Items/Commands/DropCommand.cs`](../../../Core/Modules/Items/Commands/DropCommand.cs), [`Core/Modules/Items/Systems/ItemSystem.cs`](../../../Core/Modules/Items/Systems/ItemSystem.cs)
- [`Core/Modules/Items/Resolvers/ItemInInventoryResolver.cs`](../../../Core/Modules/Items/Resolvers/ItemInInventoryResolver.cs)
- [`Core/Modules/Spawn/Handlers/ItemContextHandler.cs`](../../../Core/Modules/Spawn/Handlers/ItemContextHandler.cs)
- [`Core/Modules/Items/Handlers/ItemInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/ItemInteractionHandler.cs)
- [`docs/use-cases/persistence-reform.md`](../../use-cases/persistence-reform.md) — Stage C, item drop flow
