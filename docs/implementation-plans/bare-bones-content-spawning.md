# Use Case: Bare-Bones Content Spawning

**Status:** implemented
**Actors:** Administrator
**Module:** `Core/Modules/Admin/` (commands, events, handler extensions); new domain system `Core/Modules/Admin/Systems/RoomBuilderSystem.cs`

> **Note — persistence model superseded.** References to `PersistenceHandler`, `MarkDirty`, or the dirty-set flush pattern reflect the slice-5a design and are now historical. The as-built model is the two-level opt-in in [`persistence-two-level-model.md`](persistence-two-level-model.md): `DigCommand` and `SetCommand` call `SaveEntityAsync` directly rather than relying on `PersistenceHandler` subscriptions. The Main Flow below reflects the as-built save-on-change behaviour.

---

## Description

Provides in-game admin commands (`dig`, `set`) to create and edit room content at runtime without pre-authored YAML. `dig <direction> [name]` creates a new room in the named direction, wires bidirectional exits, and auto-moves the administrator into it. `set <property> <value>` mutates the name or description of the room the administrator is standing in. Both delegate domain logic to a new `IRoomBuilderSystem` so the operations are reusable by a future in-game editor without a live session. This slice unblocks functional testing of slices 6+ by letting administrators build testable room networks at runtime.

The slice-2 `dig <direction> <targetRoomBlueprintId>` (connect-to-existing) is **replaced** by the create-a-room behaviour here.

---

## Preconditions

- Slices 1–5 complete. Reused, not re-introduced: `IAdminAuthorizer` + `AdminRequirement` gate; `spawn`/`teleport`/`reload` (slice 2); `AdminAuditHandler`; `PlayerMovedHandler`; `ITemplateRegistry`, `RoomTemplate`, `EntityService`, `IEventBus`; `RoomComponent` (`Name`, `Description`, `Exits`); `LocationComponent`, `BlueprintComponent`; the full command framework (slice 3) and output framework (slice 4); account/character components (slice 5).
- The administrator is connected with a bound session (`PlayerEntityId != 0`), has admin rights, and is standing in a room with `RoomComponent` + `LocationComponent`.
- Entity IDs are `uint`; blueprint IDs are `string` (e.g. `room.adhoc.<shortid>`).

---

## Postconditions

### After `dig <direction> [name]`

- A new room entity exists with `RoomComponent`, `BlueprintComponent` (`room.adhoc.<shortid>`), and `PersistentEntity`, registered in `ITemplateRegistry` via a minimal `RoomTemplate`.
- The source room's `Exits[direction]` points to the new room; the new room's `Exits[Opposite(direction)]` points back.
- Both rooms are saved via `SaveEntityAsync` (save-on-change).
- `RoomCreatedByAdminEvent` is published; the administrator is moved into the new room (existing `PlayerMovedHandler` fires broadcast + look).
- If an exit already existed in that direction, the command fails with a clear error; no room is created.

### After `set <property> <value>`

- `RoomComponent.Name` or `.Description` on the administrator's current room is updated.
- The room is saved via `SaveEntityAsync`; `RoomPropertySetByAdminEvent` is published; a one-line confirmation is returned.

---

## Main Flow

### Flow A — `dig <direction> [name]`

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement` via `IAuthorizationChecker`; non-privileged sessions are rejected before the body runs.
2. **Conflict check.** `DigCommand` reads the invoker's `LocationComponent.RoomEntityId`, retrieves its `RoomComponent`, and checks `Exits` for the requested `Direction`. If present → error `PlainMessage`, return.
3. **Room creation.** `IRoomBuilderSystem.CreateRoom(name)` (default `"New Room"`) generates a unique blueprint id, creates the entity, attaches `RoomComponent` + `BlueprintComponent` + `PersistentEntity`, registers a minimal `RoomTemplate`, returns `RoomCreationResult(RoomEntityId, BlueprintId)`.
4. **Exit wiring.** `IRoomBuilderSystem.LinkExits(sourceRoomId, direction, newRoomId, bidirectional: true)` mutates `Exits` on both rooms and both in-memory `RoomTemplate` exit maps.
5. **Event + save.** The command publishes `RoomCreatedByAdminEvent`, then calls `SaveEntityAsync(newRoomId)` and `SaveEntityAsync(sourceRoomId)` directly (save-on-change, INV-10), then publishes `PlayerMovedEvent` (auto-move). Writes a confirmation `PlainMessage` (e.g. `"Room 'New Room' (room.adhoc.a1b2c3) created to the north."`).
6. **`AdminAuditHandler` (priority 80)** logs a structured entry for `RoomCreatedByAdminEvent`.
7. **`PlayerMovedHandler`** handles `PlayerMovedEvent`: departure broadcast on the source room, arrival broadcast on the new room, `look` to the administrator.

### Flow B — `set <property> <value>`

1. **Privilege gate.** As Flow A step 1.
2. **Argument parse.** `property` (`Token`: `name`|`description`) + `value` (`RestOfLine`). Unrecognized property → usage hint + `CommandExecutedEvent(ParseFailed)`.
3. **Mutation.** `IRoomBuilderSystem.SetRoomName` / `SetRoomDescription` against the invoker's current room.
4. **Event + save.** Publishes `RoomPropertySetByAdminEvent`; calls `SaveEntityAsync(roomId)`.
5. **Handlers.** `AdminAuditHandler` (80) logs.
6. **Confirmation** `PlainMessage` via `context.Output`.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `RoomCreatedByAdminEvent` | `DigCommand` | `uint AdminEntityId, uint NewRoomEntityId, string BlueprintId, uint SourceRoomEntityId, Direction Direction, bool BidirectionalLinkCreated` | Audit log; future map/editor hooks. (Persistence is save-on-change in the command, not handler-driven.) |
| `RoomPropertySetByAdminEvent` | `SetCommand` | `uint AdminEntityId, uint RoomEntityId, string PropertyName, string NewValue` | Audit log. |
| `PlayerMovedEvent` | `DigCommand` (auto-move leg) | `uint EntityId, uint FromRoomEntityId, uint ToRoomEntityId` | Consumed by existing `PlayerMovedHandler`; published directly because auto-move is an unconditional consequence of `dig`. |

**`RoomExitAuthoredByAdminEvent`** (slice 2) remains defined but is no longer fired by `dig` — the new `dig` publishes `RoomCreatedByAdminEvent`. A future connect-existing-rooms command could revive it.

---

## Design Notes

- **`RoomComponent` is tagged `[Persistent]` in this slice** — closes a slice-2 gap where `Name`/`Description`/`Exits` weren't saved. Ad-hoc rooms now survive restart; the blueprint-seeds-world skip-on-conflict pass leaves hydrated room entities alone. `SetRoomName`/`SetRoomDescription` mutate the live `RoomComponent` only; `LinkExits` still mirrors to the in-memory `RoomTemplate` for same-session `reload` consistency.
- **`set` scope is intentionally narrow** — only `Name` and `Description`. The `property`-as-`Token` schema makes expansion to items/mobs (slices 6+) additive.
- **Short-id generation.** `CreateRoom` generates the `<shortid>` suffix from 8 Base36 chars of a `Guid`-derived value; it calls `ITemplateRegistry.TryGet` to confirm uniqueness and regenerates on collision (uniqueness is the caller's responsibility — `Register` is a bare upsert). The id is shown in the confirmation so the admin can `teleport` to it.
- **`dig` drops the slice-2 connect-to-existing path** — a future `link <direction> <targetBlueprintId>` would handle connecting two existing rooms (and could publish `RoomExitAuthoredByAdminEvent`).
- **No `mkroom`/`mkitem`/`mkmob` in this slice.** A standalone orphan-room command was dropped (`dig` covers creation + placement). `mkitem` lands with slice 6, `mkmob` with slice 8 — same pattern (logic in a domain system, command as thin orchestrator).
- **Locale enhancements deferred** — room-to-area membership, coordinate system, area-level properties; see `backlog.md` (`🔵 Locale enhancements`).

---

## Related

- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; introduced `dig`/`spawn`/`teleport`/`reload` and the admin event/handler substrate; this slice replaces `dig` and adds `set`.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; the as-built save-on-change model this doc's banner points to.
- [`command-framework.md`](command-framework.md) — slice 3; `ICommand`, `CommandContext`, `AdminRequirement` used by both commands.
- [`output-framework.md`](output-framework.md) — slice 4; `PlainMessage` / `IOutputWriter` for all command output.
- [`account-character-creation.md`](account-character-creation.md) — slice 5; the `PlayerEntityId`-bound session these commands run under.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
