# Admin Commands

> The builder verbs (`dig`, `mkitem`, `mkmob`, `mkarea`, `set*`, `list`, `reload`) and the privilege gate that secures them. **Status:** live (slices 2, 5a, 6, 8, admin-area-authoring).

## What it is / does

Admin commands are the in-game authoring surface: thin Initiators that call a builder system, write the result to YAML, publish an audit event, and optionally persist the new entity. Privilege is **structural** — each command calls `IAdminAuthorizer.IsPrivileged(session)` as its first line. Non-privileged sessions get a rejection line and the command body short-circuits. There is **no `@` prefix** or special sigil; plainly named verbs are secured by the privilege gate.

## Privilege gate

[`IAdminAuthorizer.cs`](../../../Core/Modules/Admin/Systems/IAdminAuthorizer.cs) — two call modes: `IsPrivileged(ISession)` and `IsPrivileged(uint playerEntityId)`.

Authorization is layered:
- **Bootstrap layer (live):** reads `Admin:PrivilegedNames` from `IConfiguration` and matches against `PlayerComponent.DisplayName`. Anyone in the list is always privileged.
- **Persisted layer (deferred):** `AdminPrivilegeComponent` (`[Persistent]`) on a player entity also grants rights; the settings floor remains. Implementation tracked in [`../../implementation-plans/admin-privilege-elevation.md`](../../implementation-plans/admin-privilege-elevation.md).

`AdminAuditHandler` (priority 80) subscribes to every `*ByAdminEvent` and writes one structured log entry per event. It runs after gameplay handlers and before `PersistenceHandler` at 90.

## Builder verb catalogue

| Verb | System called | YAML written | Event |
|---|---|---|---|
| `dig <direction> [name]` | `IRoomBuilderSystem.CreateRoom` + `LinkExits` | both rooms via `IRoomContentWriter` | `RoomCreatedByAdminEvent` |
| `mkitem [name]` | `IItemBuilderSystem.CreateItem` | none (item is `PersistentEntity`; saved by `IPersistenceSystem`) | `ItemCreatedByAdminEvent` |
| `mkmob [name]` | `IMobBuilderSystem.CreateMob` | mob template via `IMobContentWriter` | `MobCreatedByAdminEvent` |
| `mkarea [name]` | `IAreaBuilderSystem.CreateArea` | area template via `IAreaContentWriter` | `AreaCreatedByAdminEvent` |
| `set <property> <value>` | `IRoomBuilderSystem.SetRoom*` | room template via `IRoomContentWriter` | `RoomPropertySetByAdminEvent` |
| `setitem <blueprintId> <property> <value>` | `IItemBuilderSystem.Set*` | — (item is persisted) | `ItemPropertySetByAdminEvent` |
| `setmob <blueprintId> <property> <value>` | `IMobBuilderSystem.Set*` | mob template via `IMobContentWriter` | `MobPropertySetByAdminEvent` |
| `setarea <roomBp> <areaBp>` | `IAreaSystem.AssignRoomToArea` | room template via `IRoomContentWriter` | `RoomAreaAssignedByAdminEvent` |
| `list <area\|room>` | `EntityService.GetAllComponents<T>()` (direct scan) | — | none |
| `reload` | `IWorldContentLoader.ReloadAsync` | — | `ContentReloadedEvent` |
| `spawn <blueprintId>` | `ITemplateRegistry.Spawn` | — | `EntitySpawnedByAdminEvent` |
| `teleport <target>` | `IMovementSystem` (teleport path) | — | `PlayerTeleportedByAdminEvent` |

Full argument schemas and per-command behavior details are in [`../../reference/commands.md`](../../reference/commands.md).

## Builder systems

All builder systems follow the same shape: pure ECS mutations, return results, never publish events or call persistence (INV-5). The calling command is the Initiator.

- [`IRoomBuilderSystem.cs`](../../../Core/Modules/Admin/Systems/IRoomBuilderSystem.cs) — `CreateRoom` / `LinkExits` / `SetRoomName` / `SetRoomDescription`. Located in `Core/Modules/Admin/Systems/`.
- [`IAreaBuilderSystem.cs`](../../../Core/Modules/Admin/Systems/IAreaBuilderSystem.cs) — `CreateArea`. Located in `Core/Modules/Admin/Systems/`.
- [`IItemBuilderSystem.cs`](../../../Core/Modules/Items/Systems/IItemBuilderSystem.cs) — `CreateItem` / `SetItem*`. Located in `Core/Modules/Items/Systems/`.
- [`IMobBuilderSystem.cs`](../../../Core/Modules/Mobs/Systems/IMobBuilderSystem.cs) — `CreateMob` / `SetMob*` / `SetAttribute`. Located in `Core/Modules/Mobs/Systems/`.

## Content writers

Content writers serialize a template to an atomic YAML file (tmp → rename). They are called by commands after the builder system returns, not by the builder system itself (INV-5).

- [`IAreaContentWriter.cs`](../../../Core/Modules/World/Systems/IAreaContentWriter.cs) — `WriteAsync(AreaTemplate, ct)`.
- [`IRoomContentWriter.cs`](../../../Core/Modules/World/Systems/IRoomContentWriter.cs) — `WriteAsync(RoomTemplate, ct)`.
- [`IItemContentWriter.cs`](../../../Core/Modules/Items/Systems/IItemContentWriter.cs) — `WriteAsync(ItemTemplate, ct)`.
- [`IMobContentWriter.cs`](../../../Core/Modules/Mobs/Systems/IMobContentWriter.cs) — `WriteAsync(MobTemplate, ct)`.

## Design notes

- **`dig` auto-moves the admin into the new room** by publishing `PlayerMovedEvent` after YAML is written. `PlayerMovedHandler` fires the departure/arrival/look sequence.
- **World-content entities carry no `PersistentEntity`** (INV-23). Rooms and areas are YAML-sourced; their durability is the YAML file. Items and mobs created via `mkitem`/`mkmob` are player-adjacent entities and carry `PersistentEntity` — they are saved immediately via `IPersistenceSystem.SaveEntityAsync` (the admin boundary-save pattern, INV-22).
- **`reload` is additive only.** It seeds missing blueprints; it never mutates existing live entities. To pick up edits to a live room, restart the server.
- **`list` is read-only** — it scans components directly, publishes no events, calls no systems. This is consistent with INV-10 (no-chain read-only path).

## Related

- [`admin-authoring.md`](admin-authoring.md) — the holistic feature view.
- [`../../architecture/flows/flow-08-admin-room-creation.md`](../../architecture/flows/flow-08-admin-room-creation.md) — the admin authoring journey: builder-verb sequences, privilege gate, YAML write, audit event.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5, INV-8, INV-10, INV-22, INV-23.
- [`../../reference/commands.md`](../../reference/commands.md) — full command catalog rows for all admin verbs.
- [`../../reference/systems.md`](../../reference/systems.md) — `AdminAuthorizer`, `RoomBuilderSystem`, `AreaBuilderSystem`, `ItemBuilderSystem`, `MobBuilderSystem`, the `I*ContentWriter` family.
