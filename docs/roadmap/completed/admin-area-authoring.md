# Admin area authoring — `mkarea` + `listents` (completed)

> Implemented in PR #119 (`f966da6`), 2026-06-10. Living docs: [`admin-authoring`](../../features/admin-authoring/admin-authoring.md) · [`admin-commands`](../../features/admin-authoring/admin-commands.md). Completed record written retroactively (2026-10) — the plan stayed in `implementation-plans/` marked `planned` after ship and was disintegrated in the housekeeping pass.

## Outcome

Two admin-only verbs closed the area-authoring gap in the telnet toolchain. `mkarea [name]` creates an ad-hoc area entity, registers a minimal `AreaTemplate`, writes it to the content directory as YAML, and prints the generated blueprint id for immediate use with `setarea` — the same builder → writer → audit-event pattern `mkitem`/`mkmob` established. `listents <area|room>` is a read-only tabular listing (Name · ShortDesc · BlueprintId) for confirming what authored content loaded.

## Behavior digest

*As-specified snapshot (present truth lives in the admin-authoring feature docs and the [admin authoring journey](../../architecture/flows/flow-08-admin-room-creation.md)).*

**Preconditions:** the area-model slice (`AreaComponent`, `AreaTemplate`, `AreaTemplateDeserializer`, `IAreaSystem`), the room builder/writer pattern (`IRoomBuilderSystem`, `IRoomContentWriter`), and the admin privilege gate (`AdminRequirement`, `AdminAuditHandler`) exist.

**Postconditions:**
- `IAreaBuilderSystem.CreateArea(name)` returns `AreaCreationResult { AreaEntityId, BlueprintId, Template }`; the entity carries `AreaComponent` + `BlueprintComponent`, **no** `PersistentEntity` (world content, INV-23); ids are `area.adhoc.<8-char base36>`.
- `IAreaContentWriter.WriteAsync(template)` atomically writes `areas/<blueprintId>.yaml` under `World:ContentDirectory` in the shape `AreaTemplateDeserializer` reads.
- `AreaCreatedByAdminEvent { AdminEntityId, AreaEntityId, BlueprintId }` is published by `MkareaCommand` and logged by `AdminAuditHandler`.
- `listents` accepts exactly `area` or `room` (case-insensitive), scans components directly, publishes nothing.

**Main-flow summary:** privilege gate → parse (default name `"New Area"`) → `CreateArea` → write YAML (before the audit event, so "area created" in the log always means the file exists) → publish event → confirmation echo.

## Shipped pieces

| Surface | Location |
|---|---|
| `IAreaBuilderSystem` / `AreaBuilderSystem` | `Core/Modules/Admin/Systems/` |
| `IAreaContentWriter` / `AreaContentWriter` | `Core/Modules/World/Systems/` |
| `MkareaCommand` | `Core/Modules/Admin/Commands/MkareaCommand.cs` |
| `ListEntitiesCommand` (`listents`) | `Core/Modules/Admin/Commands/ListEntitiesCommand.cs` |
| `AreaCreatedByAdminEvent` | `Core/Modules/Admin/Events/` |
| `AdminAuditHandler` — new event case | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` |

## Tests shipped

System tier: `Hedron.Tests/Authoring/AreaBuilderSystemTests.cs` — entity/components/template registration, blueprint-id uniqueness, and an INV-5 no-`IEventBus` guard. The command bodies, the writer (a structural copy of `RoomContentWriter`), the event record, and the one-line audit case were skipped as thin wiring per the [testing rubric](../../architecture/07-testing.md). Suite green at ship (590 tests).

## Decisions

- **Mirror `IRoomBuilderSystem`, not `IItemBuilderSystem`.** Areas are world content and never enter the SQLite flush pool, so no `SaveEntityAsync` anywhere; the YAML file is the area's only durable form.
- **Writer in World, builder in Admin.** The writer knows the template DTO and path; the builder knows the admin workflow; the command calls both in order (INV-1).
- **`listents` queries `EntityService` directly** — the `AreaCommand` inspection precedent; a list system would add no reuse. The hand-rolled table is acknowledged debt until a third consumer ([backlog: tabular output helper](../backlog.md)).
- **No prefix matching on the type token**, and **no standalone `mkroom`** — rooms come from `dig`, which carries area context.

## Deviations / Follow-ups

- Flows were planned as new Flow 27 / Flow 28 detail files; the later docs-refinement program folded both into the consolidated [admin authoring journey](../../architecture/flows/flow-08-admin-room-creation.md).
- The plan named the verb `list <type>` in places; it shipped as `listents` (the shopping slice later took `list` for shop wares).
