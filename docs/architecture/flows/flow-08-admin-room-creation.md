# Flow 8 — Admin room creation (`dig`)

> [Back to flows index](README.md)

**Summary.** A privileged session sends `dig <direction> [name]`. `DigCommand` checks for an existing exit, delegates entity creation and exit wiring to `IRoomBuilderSystem`, publishes `RoomCreatedByAdminEvent` (caught by `AdminAuditHandler`), writes YAML files for both rooms (the YAML is the room's sole durable state — no `SaveEntityAsync`), then publishes `PlayerMovedEvent` to auto-move the admin into the new room via the existing `PlayerMovedHandler`. Room entities carry no `PersistentEntity`; they are fresh-spawned from YAML on each restart.

**Trigger.** Privileged session sends `dig <direction> [name]`.

```mermaid
sequenceDiagram
    participant Sess as TelnetSession
    participant CD as CommandDispatcher
    participant Auth as IAuthorizationChecker
    participant Cmd as DigCommand
    participant RBS as IRoomBuilderSystem
    participant PSys as IPersistenceSystem
    participant Bus as IEventBus
    participant Audit as AdminAuditHandler
    participant PMH as PlayerMovedHandler

    Sess->>CD: "dig north Garden"
    CD->>Auth: IsSatisfied(AdminRequirement, session)
    alt unauthorized
        CD->>Sess: rejection PlainMessage
    else authorized
        CD->>Cmd: ExecuteAsync(CommandContext)
        Cmd->>Cmd: check Exits[North] on current room
        alt exit exists
            Cmd->>Sess: error PlainMessage
        else no exit
            Cmd->>RBS: CreateRoom("Garden")
            RBS-->>Cmd: RoomCreationResult(newRoomId, "room.adhoc.a1b2c3")
            Cmd->>RBS: LinkExits(sourceId, North, newRoomId, true)
            Cmd->>Bus: Publish(RoomCreatedByAdminEvent)
            Bus->>Audit: HandleAsync (priority 80) → structured log
            Note over Cmd: Write YAML for new room + source room (YAML is sole durable state)
            Cmd->>Bus: Publish(PlayerMovedEvent)
            Bus->>PMH: HandleAsync → departure broadcast + arrival broadcast + look
            Cmd->>Sess: confirmation PlainMessage
        end
    end
```

**Steps.**

1. `CommandDispatcher` routes `dig` to `DigCommand` after the privilege gate (`AdminRequirement` via `IAuthorizationChecker`).
2. `DigCommand.ExecuteAsync` reads `LocationComponent.RoomEntityId` and checks `RoomComponent.Exits` for the requested direction. If an exit already exists, writes a `PlainMessage` error and returns.
3. Calls `IRoomBuilderSystem.CreateRoom(name)` — allocates an entity, attaches `RoomComponent` + `BlueprintComponent` (no `PersistentEntity`), registers a minimal `RoomTemplate`, returns `RoomCreationResult(newRoomId, blueprintId)`.
4. Calls `IRoomBuilderSystem.LinkExits(sourceId, direction, newRoomId, bidirectional: true)` — sets `Exits` on both room entities and mirrors to both in-memory `RoomTemplate` exit maps.
5. Sets admin's `LocationComponent.RoomEntityId = newRoomId` and `RoomBlueprintId = result.BlueprintId`. Publishes `RoomCreatedByAdminEvent`. `AdminAuditHandler` (priority 80) logs one structured entry. `DigCommand` writes YAML for both rooms — the YAML file is the room's sole durable state. No `SaveEntityAsync` is called; rooms have no `PersistentEntity`.
6. Publishes `PlayerMovedEvent(adminId, sourceId, newRoomId, direction)`. `PlayerMovedHandler` fires: departure broadcast to the source room (excluding the admin), arrival broadcast to the new room, `look` sent to the admin.
7. Writes a confirmation `PlainMessage` (e.g. `"Room 'Garden' (room.adhoc.a1b2c3) created to the north."`).

**Cross-references.**
- [`Core/Modules/Admin/Commands/DigCommand.cs`](../../../Core/Modules/Admin/Commands/DigCommand.cs), [`Core/Modules/Admin/Systems/RoomBuilderSystem.cs`](../../../Core/Modules/Admin/Systems/RoomBuilderSystem.cs)
- [`Core/Modules/Admin/Events/RoomCreatedByAdminEvent.cs`](../../../Core/Modules/Admin/Events/RoomCreatedByAdminEvent.cs)
- [`Core/Modules/Admin/Handlers/AdminAuditHandler.cs`](../../../Core/Modules/Admin/Handlers/AdminAuditHandler.cs)
- [`docs/use-cases/bare-bones-content-spawning.md`](../../use-cases/bare-bones-content-spawning.md)
