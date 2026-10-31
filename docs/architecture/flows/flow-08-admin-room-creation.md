# Admin Authoring Journey (dig · mkitem · mkmob · mkarea · spawnrule · list · startroom)

> [Back to flows index](README.md)

**Source:** [`../../features/admin-authoring/admin-authoring.md`](../../features/admin-authoring/admin-authoring.md)

**Summary.** A privileged session issues a builder verb. `CommandDispatcher` routes through the privilege gate (`IAdminAuthorizer.IsPrivileged`); the command calls the appropriate builder system (pure result), writes YAML via an `I*ContentWriter`, and publishes a past-tense `*ByAdminEvent`. Admin mutations update both the template and the live world (INV-21); authored world content is never saved to SQLite. `AdminAuditHandler` (priority 80) logs every `*ByAdminEvent`. `dig` additionally publishes `PlayerMovedEvent` to auto-move the admin into the new room. `setmob`/`setitem` are **blueprint-keyed**: the builder updates the template and every live world instance (never a `PersistentEntity`), and the event carries the blueprint and the instance count.

**Trigger.** Privileged session sends a builder verb: `dig <direction> [name]`, `mkitem [name]`, `mkmob [name]`, `mkarea [name]`, `spawnrule [add|set|remove …]`, `set <property> <value>`, `setitem`/`setmob`/`setarea`, `listents <area|room>`, `reload`, or `startroom [here]`.

```mermaid
sequenceDiagram
    participant Sess as TelnetSession
    participant Auth as IAdminAuthorizer
    participant Cmd as Builder command
    participant Sys as Builder system
    participant W as I*ContentWriter
    participant Bus as IEventBus
    participant Audit as AdminAuditHandler

    Sess->>Auth: IsPrivileged(session)
    alt unauthorized
        Auth-->>Sess: rejection PlainMessage
    else authorized
        Sess->>Cmd: ExecuteAsync(CommandContext)
        Cmd->>Sys: Create*/Set* (pure result)
        Sys-->>Cmd: CreationResult / void
        Cmd->>W: WriteAsync(template)
        opt spawnrule / mkmob / mkitem
            Cmd->>Sys: IRoomBuilderSystem.AddSpawnRule / UpdateSpawnRule / RemoveSpawnRule
            Sys->>Sys: template + live SpawnConfigComponent + ISpawnSystem.SyncRoom (spawns / detaches)
            Cmd->>W: IRoomContentWriter.WriteAsync(room)
            Cmd->>Bus: Publish(SpawnRuleAuthoredByAdminEvent)
        end
        Cmd->>Bus: Publish(*ByAdminEvent)
        Bus->>Audit: HandleAsync (priority 80) → structured log
        Cmd->>Sess: confirmation PlainMessage
    end
    Note over Cmd: mkmob/mkitem/spawnrule, listents, reload and startroom extend or deviate from this shape; see the text below
```

**Steps.**

1. `CommandDispatcher` routes the verb; each admin command calls `IAdminAuthorizer.IsPrivileged(session)` as its first line. Non-privileged sessions receive a rejection and return immediately.
2. The command calls the appropriate builder system (`IRoomBuilderSystem`, `IAreaBuilderSystem`, `IItemBuilderSystem`, or `IMobBuilderSystem`). Builder systems return results and never publish events or call persistence (INV-5).
3. The command writes YAML via the matching `I*ContentWriter` (atomic tmp → rename). Rooms, areas, mobs and items authored here are world content: YAML is the sole durable state — no `PersistentEntity`, no `SaveEntityAsync` (INV-23).
4. The command publishes a past-tense `*ByAdminEvent`. `AdminAuditHandler` (priority 80) logs one structured entry.
5. `dig` additionally publishes `PlayerMovedEvent(adminId, sourceId, newRoomId, direction)`. `PlayerMovedHandler` fires departure broadcast + arrival broadcast + look.
6. The command writes a confirmation `PlainMessage` showing the blueprint id.

**`spawnrule` authors room spawn rules live.** `SpawnRuleCommand` (`spawnrule add mob.b1.rat 3 120`, `set <ruleId> <count|delay|blueprint> <value>`, `remove <ruleId>`) calls `IRoomBuilderSystem.AddSpawnRule` / `UpdateSpawnRule` / `RemoveSpawnRule`, which validates (`SpawnRuleValidation`; the blueprint must be a mob/item template; the invoker must stand in a registered room) and then updates the registered `RoomTemplate`, the live `SpawnConfigComponent` and the tracker via `ISpawnSystem.SyncRoom` — e.g. 3 rats spawn now; shrinking a count detaches surplus instances. The command writes the room YAML and publishes `SpawnRuleAuthoredByAdminEvent` (`Added`/`Updated`/`Removed`), logged by `AdminAuditHandler`. A refusal changes and publishes nothing. Bare `spawnrule` is read-only (`ISpawnSystem.DescribeRoom`: id, blueprint, live/count, delay, next respawn).

**`mkmob` / `mkitem` author a count-1 rule.** In order: (1) validate the invoker's room first (`IRoomBuilderSystem.TryGetAuthorableRoom`) so a refusal leaves no orphan template or file; (2) `CreateMob`/`CreateItem` registers the template only and the command writes its YAML; (3) `IRoomBuilderSystem.AddSpawnRule(room, bp, 1, 300)` syncs and spawns the instance, and the command writes the room YAML; (4) publish `MobCreatedByAdminEvent`/`ItemCreatedByAdminEvent` (carrying the rule's instance id) and `SpawnRuleAuthoredByAdminEvent`. The confirmed entity respawns after it dies or is picked up.

**`listents` is read-only.** `ListEntitiesCommand` scans `EntityService.GetAllComponents<T>()` directly, publishes no events, and calls no builder system (INV-10).

**`reload` is a full rebuild.** `ReloadCommand` force-saves persistent state, then `IWorldContentLoader.ReloadAsync` stages and validates the corpus, then tears down all world content and re-spawns it fresh from YAML (or refuses as a whole on a content shape error — error list, nothing changed, nothing published); the command re-publishes `WorldContentReadyEvent` (shop re-seed, spawn slots, player re-placement) and `ContentReloadedEvent`. Persistent (player) entities survive. See [Flow 5](flow-05-content-reload.md).

**`startroom here` writes a world definition, not a template.** `StartRoomCommand` validates that the invoker stands in a world room (`RoomComponent` + `BlueprintComponent`), updates `world.yaml` via `IWorldManifestStore.UpdateAsync` (which refuses, with no write, while the file is malformed) to set `startingRoomId` to that room, then calls `IWorldContentLoader.ResolveStartingRoom()` to swap the live `WorldConfiguration.StartingRoom` snapshot — durable definition first, then live state (the INV-21 analogue). It publishes `StartingRoomSetByAdminEvent` (previous **resolved** id), which `AdminAuditHandler` logs, and writes a confirmation. No builder system, no `SaveEntityAsync`; existing characters' respawn points are untouched. Bare `startroom` is read-only (resolved room, source, any declared-but-unresolved id).

**Cross-references.**
- [`Core/Modules/Admin/`](../../../Core/Modules/Admin/) — `DigCommand`, `SetCommand`, `SetAreaCommand`, `MkareaCommand`, `ListEntitiesCommand`, `ReloadCommand`, `StartRoomCommand`, `SpawnRuleCommand`, `RoomBuilderSystem`, `AreaBuilderSystem`, `AdminAuthorizer`, `AdminAuditHandler`.
- [`Core/Modules/Items/Commands/MkitemCommand.cs`](../../../Core/Modules/Items/Commands/MkitemCommand.cs) · [`Core/Modules/Mobs/Commands/MkMobCommand.cs`](../../../Core/Modules/Mobs/Commands/MkMobCommand.cs)
- [`../../features/admin-authoring/admin-commands.md`](../../features/admin-authoring/admin-commands.md) — full builder verb table and privilege gate design.
