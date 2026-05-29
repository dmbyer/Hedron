# Flow 9 — Item pickup (`get`)

> [Back to flows index](README.md)

**Summary.** Player sends `get <item>`. `GetCommand` uses `ItemInRoomResolver` to prefix-match the token against items in the player's room, calls `IItemSystem.MoveToInventory` to transfer the item from ground to inventory, publishes `ItemPickedUpEvent`. `ItemContextHandler` promotes the item to persistent; `SpawnSystem` marks the spawn slot vacant and schedules a respawn; `ItemInteractionHandler` broadcasts the pickup messages. No immediate save — both item and player are persisted in the next flush cycle.

**Trigger.** Player sends `get <item>`.

```mermaid
sequenceDiagram
    participant Client
    participant CD as CommandDispatcher
    participant Parser as ICommandArgumentParser
    participant Resolver as ItemInRoomResolver
    participant Cmd as GetCommand
    participant IS as IItemSystem
    participant Bus as IEventBus
    participant ICH as ItemContextHandler (Domain 20)
    participant SS as SpawnSystem (Domain 20)
    participant IIH as ItemInteractionHandler (Notification 80)
    participant Broadcast as IBroadcastSystem

    Client->>CD: "get sword"
    CD->>Parser: Parse(schema, "sword", resolverContext)
    Parser->>Resolver: GetCandidates(resolverContext)
    Resolver->>IS: GetItemsInRoom(playerRoomId)
    Resolver-->>Parser: [ResolvedCandidate("a short sword","a short sword"), ...]
    Parser-->>CD: ParsedArguments{item="a short sword"}
    CD->>Cmd: ExecuteAsync(CommandContext)
    Cmd->>IS: TryFindItemInRoom(roomId, "a short sword", out itemEntityId)
    IS-->>Cmd: true, itemEntityId
    Cmd->>IS: MoveToInventory(itemEntityId, playerEntityId)
    Cmd->>Bus: Publish(ItemPickedUpEvent)
    Bus->>ICH: HandleAsync — AddComponent<PersistentEntity>(itemEntityId)
    Bus->>SS: HandleAsync — mark slot vacant; schedule respawn timer
    Bus->>IIH: HandleAsync — broadcast pickup messages
    IIH->>Broadcast: SendToRoomAsync(roomId, "Bob picks up a short sword.", id≠player)
    IIH->>Broadcast: SendToRoomAsync(roomId, "You pick up a short sword.", id==player)
```

**Steps.**

1. `CommandDispatcher` routes `get` to `GetCommand`. No privilege requirement.
2. **Argument resolution.** `ICommandArgumentParser` calls `ItemInRoomResolver.GetCandidates(resolverContext)`, which reads the invoker's `LocationComponent.RoomEntityId`, calls `IItemSystem.GetItemsInRoom`, and emits `ResolvedCandidate(MatchString, CanonicalValue)` pairs — one for each item name and each keyword. The parser prefix-matches the token against all `MatchString` values, deduplicates by `CanonicalValue`, and substitutes the canonical item name into `ParsedArguments.item` (unique match) or fails with an ambiguity error (two+ distinct canonical values).
3. **Entity resolve.** `IItemSystem.TryFindItemInRoom(roomId, canonicalName, out itemEntityId)` performs a final entity lookup. If not found (race condition: item taken between resolve and pickup), writes "You don't see that here." and returns.
4. **Pickup mutation.** `IItemSystem.MoveToInventory(itemEntityId, playerEntityId)` — removes `LocationComponent` from the item (no-op if already absent), appends item id to `InventoryComponent.ItemEntityIds`.
5. **Event.** Publishes `ItemPickedUpEvent(PlayerEntityId, ItemEntityId, RoomEntityId)`.
6. **Context promotion.** `ItemContextHandler` (priority 20) calls `EntityService.AddComponent(itemEntityId, new PersistentEntity())`. The item entity enters the flush pool and will be saved on the next periodic flush cycle.
7. **Spawn slot vacancy.** `SpawnSystem` (priority 20) checks its reverse map for `itemEntityId`. If the item occupied a spawn slot (world-spawn item), marks the slot vacant and schedules a respawn after `RespawnDelaySeconds`.
8. **Broadcast.** `ItemInteractionHandler` (priority 80) broadcasts `"<name> picks up <item>."` to the room excluding the picker, then `"You pick up <item>."` to the picker via `SendToRoomAsync` with opposite filters.

**Cross-references.**
- [`Core/Modules/Items/Commands/GetCommand.cs`](../../../Core/Modules/Items/Commands/GetCommand.cs), [`Core/Modules/Items/Systems/ItemSystem.cs`](../../../Core/Modules/Items/Systems/ItemSystem.cs)
- [`Core/Modules/Items/Resolvers/ItemInRoomResolver.cs`](../../../Core/Modules/Items/Resolvers/ItemInRoomResolver.cs)
- [`Core/Modules/Spawn/Handlers/ItemContextHandler.cs`](../../../Core/Modules/Spawn/Handlers/ItemContextHandler.cs)
- [`Core/Modules/Spawn/Systems/SpawnSystem.cs`](../../../Core/Modules/Spawn/Systems/SpawnSystem.cs)
- [`Core/Modules/Items/Handlers/ItemInteractionHandler.cs`](../../../Core/Modules/Items/Handlers/ItemInteractionHandler.cs)
- [`docs/use-cases/persistence-reform.md`](../../use-cases/persistence-reform.md) — Stage C, item pickup flow
