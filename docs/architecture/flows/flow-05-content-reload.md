# Flow 5 — Content reload (`reload`)

> [Back to flows index](README.md)

**Summary.** A privileged session re-scans the content directory and refreshes the template registry. Templates with no live counterpart are seeded; **existing live entities are not mutated**. The pass is additive only.

**Trigger.** Privileged session sends `reload`.

```mermaid
sequenceDiagram
    participant Session
    participant CD as CommandDispatcher
    participant Auth as IAuthorizationChecker
    participant RC as ReloadCommand
    participant WCL as WorldContentLoader
    participant Reg as ITemplateRegistry
    participant Bus as IEventBus
    participant Audit as AdminAuditHandler

    Session->>CD: "reload"
    CD->>Auth: IsSatisfied(AdminRequirement, session)
    alt unauthorized
        CD->>Session: rejection (via IOutputWriter)
    else authorized
        CD->>RC: ExecuteAsync(CommandContext)
        RC->>WCL: ReloadAsync
        WCL->>Reg: snapshot previous ids
        WCL->>Reg: Clear
        WCL->>WCL: re-scan + re-deserialize → register
        WCL->>WCL: BuildLiveBlueprintMap
        WCL->>WCL: SpawnMissingEntities (skip-on-conflict)
        WCL->>WCL: LinkRoomExits (new entities only)
        WCL->>WCL: PlaceItemsInRooms (newlySpawned only)
        WCL->>WCL: PlaceMobsInRooms (newlySpawned only)
        WCL->>WCL: MigrateEntityComponentsAsync (add missing required components; persist modified entities)
        WCL-->>RC: ContentReloadResult{ loaded, unchanged, removed }
        RC->>Session: confirmation (via IOutputWriter)
        RC->>Bus: Publish(ContentReloadedEvent)
        Bus->>Audit: HandleAsync (structured log)
    end
```

**Steps.**

1. `CommandDispatcher` routes `reload` to `ReloadCommand`.
2. **Authorization gate.** The dispatcher calls `IAuthorizationChecker.IsSatisfied(AdminRequirement, session)` **before** invoking `ReloadCommand`. Non-privileged sessions receive a rejection `PlainMessage` via `IOutputWriter` and `CommandExecutedEvent(Unauthorized)`; `ReloadCommand.ExecuteAsync` never runs. This is the slice-3 structural replacement for slice-2's per-command `IsPrivileged` convention.
3. The command calls `IWorldContentLoader.ReloadAsync(ct)`.
4. The loader snapshots the previous template ids, clears the registry, and re-scans `World:ContentDirectory`. Each YAML file is re-deserialized via the cross-cutting `IContentSerializer` → kind-specific `ITemplateDeserializer` and re-registered.
5. Loaded / unchanged / removed counts are computed by set difference against the previous snapshot.
6. `BuildLiveBlueprintMap` enumerates every entity that has a `BlueprintComponent`. For each registered template that has no entry in the map, `SpawnMissingEntities` calls `TemplateRegistry.Spawn(blueprintId)` (which allocates an entity, attaches `BlueprintComponent`, and runs `IEntityTemplate.Apply`).
7. `LinkRoomExits` populates `RoomComponent.Exits` for the newly spawned entities only — existing live rooms are not touched.
8. `PlaceItemsInRooms` attaches `LocationComponent { RoomEntityId }` to newly-spawned item entities only. If a YAML `spawnRoomBlueprintId` changed for an existing live entity, a warning is logged — live entities are never mutated by reload.
9. `PlaceMobsInRooms` applies the same pass for newly-spawned mob entities. Same constraint and warning behavior as items.
10. `MigrateEntityComponentsAsync` checks every loaded entity against `IArchetypeRegistry.MissingRequired` for its archetype and adds any absent required components via `Activator.CreateInstance`; calls `IPersistenceSystem.SaveEntityAsync` for each modified entity; never removes extra components. Logs each added component and a summary count.
11. `ReloadAsync` returns `ContentReloadResult { loaded, unchanged, removed }`.
12. The command writes a confirmation `PlainMessage` via `CommandContext.Output` (`IOutputWriter`) and publishes `ContentReloadedEvent` (thin payload — the three counts).
13. `AdminAuditHandler` (priority `HandlerPriority.Notification` = 80) writes one structured-log entry with stable event name `AdminCommandExecuted`.

**Constraint.** Live entities are never mutated by reload. To pick up edits to a live room's description or components, restart the host; or use `dig` for exit changes that should apply immediately.

**Cross-references.**
- [`Core/Modules/Admin/Commands/ReloadCommand.cs`](../../../Core/Modules/Admin/Commands/ReloadCommand.cs), [`Core/Modules/World/Systems/WorldContentLoader.cs`](../../../Core/Modules/World/Systems/WorldContentLoader.cs)
- [`docs/use-cases/world-content-loading-and-admin-substrate.md`](../../use-cases/world-content-loading-and-admin-substrate.md)
