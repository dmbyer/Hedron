# Admin Commands

> The builder verbs (`dig`, `mkitem`, `mkmob`, `mkarea`, `spawnrule`, `set*`, `list`, `reload`) and the privilege gate that secures them. **Status:** live (slices 2, 5a, 6, 8, admin-area-authoring, spawn-authoring).

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
| `mkitem [name]` | room check → `IItemBuilderSystem.CreateItem` → `IRoomBuilderSystem.AddSpawnRule` (count 1, 300 s) | item template + the current room (new rule) | `ItemCreatedByAdminEvent` + `SpawnRuleAuthoredByAdminEvent` |
| `mkmob [name]` | room check → `IMobBuilderSystem.CreateMob` → `IRoomBuilderSystem.AddSpawnRule` (count 1, 300 s) | mob template + the current room (new rule) | `MobCreatedByAdminEvent` + `SpawnRuleAuthoredByAdminEvent` |
| `spawnrule [add\|set\|remove …]` | `IRoomBuilderSystem.AddSpawnRule` / `UpdateSpawnRule` / `RemoveSpawnRule` (bare verb: `ISpawnSystem.DescribeRoom`, read-only) | current room via `IRoomContentWriter` | `SpawnRuleAuthoredByAdminEvent` (`Added`/`Updated`/`Removed`) |
| `mkarea [name]` | `IAreaBuilderSystem.CreateArea` | area template via `IAreaContentWriter` | `AreaCreatedByAdminEvent` |
| `set <property> <value>` | `IRoomBuilderSystem.SetRoom*` | room template via `IRoomContentWriter` | `RoomPropertySetByAdminEvent` |
| `setitem <blueprintId> <property> <value>` | `IItemBuilderSystem.Set*` (blueprint-keyed fan-out) | item template via `IItemContentWriter` | `ItemPropertySetByAdminEvent` (blueprint, instances updated) |
| `setmob <blueprintId> <property> <value>` | `IMobBuilderSystem.Set*` (blueprint-keyed fan-out) | mob template via `IMobContentWriter` | `MobPropertySetByAdminEvent` (blueprint, instances updated) |
| `setarea <roomBp> <areaBp>` | `IAreaSystem.AssignRoomToArea` | room template via `IRoomContentWriter` | `RoomAreaAssignedByAdminEvent` |
| `areaset <areaBp> <name\|description\|designation> <value>` | `IAreaBuilderSystem.SetAreaName` / `SetAreaDescription` / `SetAreaDesignation` | area template via `IAreaContentWriter` | `AreaPropertySetByAdminEvent` |
| `list <area\|room>` | `EntityService.GetAllComponents<T>()` (direct scan) | — | none |
| `reload` | `IWorldContentLoader.ReloadAsync` (refuses as a whole on a shape error) | — | `ContentReloadedEvent` (not on refusal) |
| `startroom [here]` | `IWorldManifestStore` + `IWorldContentLoader.ResolveStartingRoom` | `world.yaml` (`here` only; refused while malformed) | `StartingRoomSetByAdminEvent` (`here` only) |
| `spawn <blueprintId>` | `ITemplateRegistry.Spawn` | — | `EntitySpawnedByAdminEvent` |
| `teleport <target>` | `IMovementSystem` (teleport path) | — | `PlayerTeleportedByAdminEvent` |

Full argument schemas and per-command behavior details are in [`../../reference/commands.md`](../../reference/commands.md).

## Builder systems

All builder systems follow the same shape: pure ECS mutations, return results, never publish events or call persistence (INV-5). The calling command is the Initiator.

- [`IRoomBuilderSystem.cs`](../../../Core/Modules/Admin/Systems/IRoomBuilderSystem.cs) — `CreateRoom` / `LinkExits` / `SetRoomName` / `SetRoomDescription`, and the spawn-rule operations `TryGetAuthorableRoom` / `AddSpawnRule` / `UpdateSpawnRule` / `RemoveSpawnRule`. Each spawn-rule operation validates (`SpawnRuleValidation` + the blueprint must be a registered non-room/area template), then updates the registered `RoomTemplate`, the live `SpawnConfigComponent` (added with the first rule, removed with the last) and the tracker via `ISpawnSystem.SyncRoom` (a lateral dependency, precedent `IAreaSystem`); a refusal changes nothing. Located in `Core/Modules/Admin/Systems/`.
- [`IAreaBuilderSystem.cs`](../../../Core/Modules/Admin/Systems/IAreaBuilderSystem.cs) — `CreateArea`, plus the `areaset` setters `SetAreaName` / `SetAreaDescription` / `SetAreaDesignation` → `AreaPropertyResult`. Each resolves the registered `AreaTemplate` (refusing an unknown one) and updates the live area entity when spawned (INV-21); `SetAreaDesignation` refuses a malformed designation (`AreaDesignationValidation`) and adds, replaces or removes `AreaDesignationComponent`. Located in `Core/Modules/Admin/Systems/`.
- [`IItemBuilderSystem.cs`](../../../Core/Modules/Items/Systems/IItemBuilderSystem.cs) — `CreateItem` (registers a template only) / `SetItem*`. Located in `Core/Modules/Items/Systems/`.
- [`IMobBuilderSystem.cs`](../../../Core/Modules/Mobs/Systems/IMobBuilderSystem.cs) — `CreateMob` (registers a template only) / `SetMob*` / `SetAttribute`. Located in `Core/Modules/Mobs/Systems/`.

**Blueprint-keyed setters (INV-21).** Every `Set*` takes a blueprint id and updates the template **and every live world instance** (a matching `BlueprintComponent` entity of that kind, never `PersistentEntity` — the same exclusion as the loader's live-blueprint map), returning the instance count (0 is fine; `-1` = no such template). A rule with `count > 1` would otherwise leave N−1 instances stale.

## Content writers

Content writers serialize a template to an atomic YAML file (tmp → rename). They are called by commands after the builder system returns, not by the builder system itself (INV-5).

- [`IAreaContentWriter.cs`](../../../Core/Modules/World/Systems/IAreaContentWriter.cs) — `WriteAsync(AreaTemplate, ct)`.
- [`IRoomContentWriter.cs`](../../../Core/Modules/World/Systems/IRoomContentWriter.cs) — `WriteAsync(RoomTemplate, ct)`.
- [`IItemContentWriter.cs`](../../../Core/Modules/Items/Systems/IItemContentWriter.cs) — `WriteAsync(ItemTemplate, ct)`.
- [`IMobContentWriter.cs`](../../../Core/Modules/Mobs/Systems/IMobContentWriter.cs) — `WriteAsync(MobTemplate, ct)`.

## Design notes

- **`dig` auto-moves the admin into the new room** by publishing `PlayerMovedEvent` after YAML is written. `PlayerMovedHandler` fires the departure/arrival/look sequence.
- **World-content entities carry no `PersistentEntity`** (INV-23). Rooms, areas, and the items/mobs created via `mkitem`/`mkmob` are all YAML-sourced world content — they carry no `PersistentEntity`; their durability is the YAML file the builder writes. They are never written to SQLite and re-spawn fresh from YAML on startup or `reload`.
- **`reload` is a full rebuild — or nothing.** It force-saves persistent (player) state, stages and validates the whole corpus, then tears down all world content and re-spawns it from YAML — so edits to existing rooms/mobs/items take effect, picked-up items respawn, and depleted shops refill. Players are preserved (and moved to the starting room if their room was removed). On a content shape error it writes one error line per file and changes and publishes nothing. See [Flow 5](../../architecture/flows/flow-05-content-reload.md).
- **`spawnrule` is the first sub-action verb.** Its schema is an optional `SpawnRuleAction` enum token plus four optional string tokens; per-action arity and types (`add <blueprintId> [count] [delaySeconds] [id]`, `set <ruleId> <count|delay|blueprint> <value>`, `remove <ruleId>`) are hand-rolled in the command because the parser has no sub-action schema support. Promote that support into the command framework at the third verb of this shape (backlog).
- **`mkmob`/`mkitem` validate the room first** (`IRoomBuilderSystem.TryGetAuthorableRoom`), so a refusal never leaves an orphan template or file. The confirmed entity is the new rule's tracked instance — it respawns after death/pickup. `spawnrule remove <id>` undoes the placement. This gives Mobs/Items commands a one-way dependency on Admin's `IRoomBuilderSystem`.
- **Spawn-rule mutation path:** builder → template + live component + `SyncRoom`; then the shared Initiator-tier helper `SpawnRuleAuthoringOutcome` (used by `spawnrule`, `mkmob`, `mkitem` — INV-19 at the third copy) writes the room YAML and publishes `SpawnRuleAuthoredByAdminEvent`. After its confirmation, each command calls `SpawnRuleAuthoringOutcome.WriteFindingsAsync`, which prints any out-of-designation finding the builder attached as a `Warning:` line. The finding is advisory output, not part of the audit event, and no command judges a placement itself. No `SaveEntityAsync` (world content). Shrinking a count **detaches** surplus instances (they live until they die or the world reloads).
- **`list` is read-only** — it scans components directly, publishes no events, calls no systems. This is consistent with INV-10 (no-chain read-only path).

## Related

- [`admin-authoring.md`](admin-authoring.md) — the holistic feature view.
- [`../../architecture/flows/flow-08-admin-room-creation.md`](../../architecture/flows/flow-08-admin-room-creation.md) — the admin authoring journey: builder-verb sequences, privilege gate, YAML write, audit event.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5, INV-8, INV-10, INV-22, INV-23.
- [`../../reference/commands.md`](../../reference/commands.md) — full command catalog rows for all admin verbs.
- [`../../reference/systems.md`](../../reference/systems.md) — `AdminAuthorizer`, `RoomBuilderSystem`, `AreaBuilderSystem`, `ItemBuilderSystem`, `MobBuilderSystem`, the `I*ContentWriter` family.
