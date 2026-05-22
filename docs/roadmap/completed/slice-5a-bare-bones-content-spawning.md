# Phase 3 slice 5a — Bare-bones content spawning (completed)

> Implemented on branch `claude/hardcore-hopper-4eac0d`. Full feature spec: [`../../use-cases/bare-bones-content-spawning.md`](../../use-cases/bare-bones-content-spawning.md).

## Outcome

The slice-2 `dig <direction> <targetRoomBlueprintId>` command is replaced by `dig <direction> [name]`, which creates a new room entity at runtime, wires bidirectional exits, and auto-moves the administrator into the new room — no pre-authored YAML required. A companion `set <property> <value>` command lets an administrator rename or re-describe any room they are standing in. All room-creation and mutation logic is extracted into `IRoomBuilderSystem` so a future in-game editor can reuse the same operations without a live player session. `RoomComponent` is now `[Persistent]`, closing a gap from slice 2 where `Name`, `Description`, and `Exits` were lost on server restart.

## Shipped pieces

| Surface | Location |
|---|---|
| `RoomCreatedByAdminEvent` (AdminEntityId, NewRoomEntityId, BlueprintId, SourceRoomEntityId, Direction, BidirectionalLinkCreated) | `Core/Modules/Admin/Events/RoomCreatedByAdminEvent.cs` |
| `RoomPropertySetByAdminEvent` (AdminEntityId, RoomEntityId, PropertyName, NewValue) | `Core/Modules/Admin/Events/RoomPropertySetByAdminEvent.cs` |
| `IRoomBuilderSystem` + `RoomCreationResult` record struct | `Core/Modules/Admin/Systems/IRoomBuilderSystem.cs` |
| `RoomBuilderSystem` (CreateRoom with collision-guarded Base36 short-id, LinkExits with template mirror, SetRoomName, SetRoomDescription) | `Core/Modules/Admin/Systems/RoomBuilderSystem.cs` |
| `DigCommand` — full replacement (schema: `dig <direction> [name]`, delegates to `IRoomBuilderSystem`, publishes `RoomCreatedByAdminEvent` + `PlayerMovedEvent`, updates `LocationComponent`) | `Core/Modules/Admin/Commands/DigCommand.cs` |
| `SetCommand` (new; `set <name\|description> <value>`, delegates to `IRoomBuilderSystem`, publishes `RoomPropertySetByAdminEvent`) | `Core/Modules/Admin/Commands/SetCommand.cs` |
| `RoomComponent` — tagged `[Persistent]` | `Core/ECS/Components/RoomComponent.cs` |
| `AdminAuditHandler` — added `IEventHandler<RoomCreatedByAdminEvent>` and `IEventHandler<RoomPropertySetByAdminEvent>` | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` |
| `PersistenceHandler` — added `IEventHandler<RoomCreatedByAdminEvent>` and `IEventHandler<RoomPropertySetByAdminEvent>`; both use `MarkIfPersistent` | `Core/Handlers/PersistenceHandler.cs` |
| `AdminModule` — registers `IRoomBuilderSystem` (singleton) and `SetCommand` | `Core/Modules/Admin/AdminModule.cs` |
| `Program.cs` — four new `bus.Subscribe` calls for `RoomCreatedByAdminEvent` and `RoomPropertySetByAdminEvent` on both `audit` and `persistenceHandler` | `Server/Program.cs` |
| `docs/architecture/06-flows.md` — Flow 8 (Admin room creation) added with full mermaid diagram and index row | — |
| `docs/reference/systems.md` — `IRoomBuilderSystem` domain system entry added | — |
| `docs/reference/components.md` — `RoomComponent` Persisted? updated to `yes` | — |
| `docs/reference/commands.md` — `dig` entry replaced; `set` entry added | — |
| `docs/reference/handlers.md` — `AdminAuditHandler` and `PersistenceHandler` subscription lists updated | — |
| `docs/use-cases/README.md` — slice 5 and 5a index rows added | — |
| `.claude/skills/add-command/SKILL.md` — "What NOT to do" bullet updated to reflect INV-8 nuance on unconditional multi-event publication | — |

## Spec-review provenance

**Spec-mode gate:** Passed as part of PR #74 (the spec commit). Architecture-reviewer ran in spec mode; the spec doc was corrected in-PR before any code was written. Key pre-implementation findings resolved in the spec:
- `RoomComponent` persistence gap explicitly called out and assigned to this slice.
- INV-8 clarified in `checklist.md` (unconditional sequential multi-event publication is permitted in commands/initiators).
- `mkitem`/`mkmob` explicitly deferred to slices 6 and 8.

**Code-mode gate:** Two blocking findings resolved before merge:
- **B-1 (blocking):** `RoomCreatedByAdminEvent` and `RoomPropertySetByAdminEvent` were not subscribed in `Server/Program.cs` — both handlers were implemented but dead. Fixed by adding four `bus.Subscribe` calls.
- **B-2 (blocking):** `DigCommand` published `PlayerMovedEvent` without updating `LocationComponent.RoomEntityId`, so the auto-move was visible (broadcast + look) but the admin's location state remained on the source room. Fixed by adding `location.RoomEntityId = result.RoomEntityId` before the event publish.

Advisory findings addressed:
- **N-2:** `PersistenceHandler` new handlers switched from direct `_persistence.MarkDirty` to `MarkIfPersistent` for consistency with all other handler methods.
- **INV-20:** `add-command` skill "What NOT to do" bullet rewritten to distinguish game-rule orchestration (forbidden) from unconditional sequential multi-event publication (permitted per INV-8).

## Notable design points

- **`IRoomBuilderSystem` scope is room-only.** `mkitem` and `mkmob` follow the same pattern but are deferred to slices 6 and 8 when those entity types exist. A `IWorldBuilderFacade` coordinator is not introduced until there is evidence of multi-type operations needing shared context.
- **Short-id collision guard.** `RoomBuilderSystem.CreateRoom` calls `ITemplateRegistry.TryGet` before `Register` to confirm uniqueness. On collision (negligible in practice) it regenerates; after 10 attempts it falls back to a 16-char Guid-derived suffix. `TemplateRegistry.Register` itself performs a bare upsert and does not collision-guard.
- **Template mirror in `LinkExits`.** The in-memory `RoomTemplate` exit map is updated alongside `RoomComponent.Exits` so a same-session `reload` does not orphan the exits. Exit keys are blueprint ids in the template; entity ids in `RoomComponent`.
- **`RoomExitAuthoredByAdminEvent` retained.** The event remains defined for a future `link` command that would connect two existing rooms. `DigCommand` no longer fires it; `AdminAuditHandler` and `PersistenceHandler` retain their slice-2 subscriptions for that event but no new code subscribes to it for `dig`-sourced mutations.
- **`set` scope is intentionally narrow.** Only `name` and `description` are settable. Expanding to other entity types or properties is additive (new accepted `property` token values) and deferred to the slices that introduce those types.

## Deviations from the use-case doc

None substantive. Two implementation details differed from the spec's surface description:

1. The spec's "Events Fired" table listed only three fields for `PlayerMovedEvent` (`EntityId`, `FromRoomEntityId`, `ToRoomEntityId`) but the live record has four (`PlayerEntityId`, `FromRoomEntityId`, `ToRoomEntityId`, `Direction`). The implementation correctly supplies all four; the spec payload description was stale (noted as N-3 in the code review — minor doc inaccuracy only).
2. `LocationComponent.RoomEntityId` is updated directly in `DigCommand` (not via a dedicated `IRoomBuilderSystem.MoveInto` method). The spec left movement as an implicit consequence of `dig`; no movement system method exists for direct location mutation. The code-review gate (B-2) identified the missing update and it was fixed in-command.

## Follow-ups unlocked

- **Slice 6 — Items + inventory.** Admins can now `dig` a test room network at runtime and immediately `spawn` items into it without pre-authored YAML, making slice 6 content exercisable end-to-end.
- **`mkitem` command** — follows the same `IRoomBuilderSystem` pattern; lands with slice 6.
- **`mkmob` command** — same pattern; lands with slice 8.
- **`link <direction> <targetBlueprintId>`** — would publish `RoomExitAuthoredByAdminEvent` for connecting two existing rooms; deferred, tracked in backlog.
- **"Save room to YAML"** — ad-hoc rooms exist only in persistence (`data/entities/`), not in `data/content/`. A future write-back command is tracked in the backlog.
