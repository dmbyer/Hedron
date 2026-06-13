# Use Case: Admin Area Authoring (`mkarea` + `list`)

**Status:** planned
**Actors:** Administrator
**Module:** `Core/Modules/Admin/` (new `IAreaBuilderSystem`, `MkareaCommand`, `ListCommand`, `AreaCreatedByAdminEvent`); `Core/Modules/World/` (new `IAreaContentWriter`)

---

## Description

Two new admin-only commands extend the area-authoring toolchain. `mkarea [name]` creates an ad-hoc area entity, registers a minimal `AreaTemplate` in `TemplateRegistry`, writes the template to `content/areas/<blueprintId>.yaml`, and returns the generated blueprint ID — the exact pattern that `mkitem` and `mkmob` established for items and mobs. `list <type>` is a read-only inspection command that prints a tabular view of all entities of a given type (`area` or `room`), showing Name, ShortDesc (≤15 chars, truncated with `…`), and BlueprintId; it publishes no events and calls no systems. Both commands are admin-gated. Together they close the area-authoring gap: an admin can now create an area from the telnet console, confirm it in `content/`, and then use the existing `setarea` command to assign rooms to it.

---

## Preconditions

- `area-model` slice is implemented: `AreaComponent`, `AreaTemplate`, `AreaTemplateDeserializer`, `IAreaSystem`, `BlueprintComponent`, `RoomComponent` all exist.
- `IRoomBuilderSystem` / `RoomBuilderSystem` exist at `Core/Modules/Admin/Systems/` — the builder pattern to mirror.
- `IRoomContentWriter` / `RoomContentWriter` exist at `Core/Modules/World/Systems/` — the content-writer pattern to mirror.
- `IItemBuilderSystem` / `ItemBuilderSystem` and `IItemContentWriter` / `ItemContentWriter` exist — additional reference patterns.
- `MkitemCommand` at `Core/Modules/Items/Commands/MkitemCommand.cs` is the command model.
- `AdminAuditHandler` is live; `IAdminAuthorizer` / `AdminRequirement` are functional.
- `EntityService.GetAllComponents<T>()` supports component-type scans (used by `AreaCommand`).
- `TemplateRegistry.Register(blueprintId, template)` supports `AreaTemplate`.
- `AdminModule.cs` DI composition point exists.

---

## Postconditions

- `IAreaBuilderSystem` / `AreaBuilderSystem` exist at `Core/Modules/Admin/Systems/`. `CreateArea(string name)` returns `AreaCreationResult { uint AreaEntityId, string BlueprintId, AreaTemplate Template }`. The area entity carries `AreaComponent { Name, Description="" }` + `BlueprintComponent { BlueprintId }`. No `PersistentEntity` — area entities are world content (INV-23). Blueprint ID format: `area.adhoc.<8-char-base36>` (same generation pattern as `room.adhoc.*` in `RoomBuilderSystem`).
- `IAreaContentWriter` / `AreaContentWriter` exist at `Core/Modules/World/Systems/`. `WriteAsync(AreaTemplate template)` serializes to `content/areas/<blueprintId>.yaml` using an atomic tmp → rename write. YAML DTO fields match the shape `AreaTemplateDeserializer` reads: `id`, `name`, `description`, `respawnRate`, `pvp`, `rooms`, `aspectAffinities` (camelCase). Mirrors `IRoomContentWriter` pattern.
- `AreaCreatedByAdminEvent` exists at `Core/Modules/Admin/Events/` with payload `{ uint AdminEntityId, uint AreaEntityId, string BlueprintId }`. `AdminAuditHandler` handles this event.
- `MkareaCommand` exists at `Core/Modules/Admin/Commands/`. Admin-gated. Argument: rest-of-line name (optional, defaults to `"New Area"`). Flow: call `IAreaBuilderSystem.CreateArea(name)` → call `IAreaContentWriter.WriteAsync(result.Template)` → publish `AreaCreatedByAdminEvent` → write confirmation with blueprint ID.
- `ListCommand` exists at `Core/Modules/Admin/Commands/`. Admin-gated. Argument: type token (required, exact match case-insensitive: `area` or `room`). Queries `EntityService.GetAllComponents<T>()` directly. Output: header row + one row per entity showing Name | ShortDesc (first 15 chars + `…` if longer) | BlueprintId (from `BlueprintComponent` if present, else entity ID). Unknown type token → error message. No events published.
- `docs/architecture/flows/README.md` gains two rows: Flow 27 (admin area creation, `mkarea`) and Flow 28 (admin entity list, `list`).
- `docs/reference/systems.md` gains `IAreaBuilderSystem` under Domain Systems (Admin) and `IAreaContentWriter` under Domain Systems (World).
- `docs/reference/handlers.md` notes `AdminAuditHandler` now handles `AreaCreatedByAdminEvent`.
- `docs/reference/commands.md` gains `mkarea` and `list` rows under Admin commands.

---

## Main Flow

### Flow 27 — Admin area creation (`mkarea`)

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement`; non-privileged sessions are rejected before the command body runs.
2. **Argument parse.** `MkareaCommand` reads the rest-of-line name argument; if absent or empty, defaults to `"New Area"`.
3. **Creation.** Calls `IAreaBuilderSystem.CreateArea(name)`. System generates `area.adhoc.<base36>`, creates an entity via `EntityService`, attaches `AreaComponent { Name=name, Description="" }` + `BlueprintComponent { BlueprintId }`, registers a minimal `AreaTemplate` in `TemplateRegistry`, and returns `AreaCreationResult { AreaEntityId, BlueprintId, Template }`.
4. **Persist to YAML.** Command calls `IAreaContentWriter.WriteAsync(result.Template)`. Writer serializes the DTO and atomically writes to `content/areas/<blueprintId>.yaml`. YAML is written before the audit event fires so that "area created" in the audit log is always true (file exists on disk). Matches the `MkitemCommand` and `MkmobCommand` ordering.
5. **Event.** Command publishes `AreaCreatedByAdminEvent { AdminEntityId, AreaEntityId, BlueprintId }`. `AdminAuditHandler` (priority 80) logs the event.
6. **Confirmation.** Command writes a `PlainMessage` to the admin: area name and blueprint ID.

### Flow 28 — Admin entity list (`list`)

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement`; non-privileged sessions rejected.
2. **Argument parse.** `ListCommand` reads the required type token; unrecognised type → write error message and return.
3. **Query.** For `area`: calls `EntityService.GetAllComponents<AreaComponent>()`. For `room`: calls `EntityService.GetAllComponents<RoomComponent>()`. No system call needed — direct component scan (same pattern as `AreaCommand`).
4. **Format.** Builds output via `StringBuilder`. Writes header row, then one row per entity: Name | ShortDesc (component `Description` truncated to 15 chars + `…`) | BlueprintId (from `BlueprintComponent` if present, else entity ID as string).
5. **Output.** Writes the assembled `PlainMessage` to the admin's session. No events published.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `AreaCreatedByAdminEvent` | `MkareaCommand` | `uint AdminEntityId, uint AreaEntityId, string BlueprintId` | Audit log. |

No events from `IAreaBuilderSystem` (domain system returns results, INV-5). `ListCommand` is read-only; it publishes no events (INV-10).

---

## Systems / Handlers Involved

| Artifact | Reuse / New | Location |
|---|---|---|
| `IAreaBuilderSystem` / `AreaBuilderSystem` | New | `Core/Modules/Admin/Systems/` |
| `IAreaContentWriter` / `AreaContentWriter` | New | `Core/Modules/World/Systems/` |
| `MkareaCommand` | New | `Core/Modules/Admin/Commands/` |
| `ListCommand` | New | `Core/Modules/Admin/Commands/` |
| `AreaCreatedByAdminEvent` | New | `Core/Modules/Admin/Events/` |
| `AdminAuditHandler` | Extended (new event case) | `Core/Modules/Admin/Handlers/` |
| `IAdminAuthorizer` | Reused | `Core/Modules/Admin/Systems/` |
| `EntityService` | Reused | `Core/ECS/` |
| `ITemplateRegistry` | Reused | `Core/Systems/` |
| `AreaComponent`, `BlueprintComponent` | Reused | `Core/ECS/Components/` |
| `AreaTemplate`, `AreaTemplateDeserializer` | Reused | `Core/Modules/World/` |

---

## Implementation Plan — Work Packages

### WP-1 — `IAreaContentWriter` infrastructure (lands first)

**Scope:** `IAreaContentWriter` interface + `AreaContentWriter` implementation. No commands or events depend on this until WP-2.

**Files:**
- `Core/Modules/World/Systems/IAreaContentWriter.cs`
- `Core/Modules/World/Systems/AreaContentWriter.cs`
- Wire into `WorldModule` / `AdminModule` DI (TBD per module ownership; `AreaContentWriter` consumes `IConfiguration`).

**Exit criterion:** `AreaContentWriter.WriteAsync(template)` produces a valid YAML file under `content/areas/` that `AreaTemplateDeserializer` can round-trip.

**Out of scope:** No commands, events, or builder system in this package.

---

### WP-2 — Builder, commands, event, tests, flows, reference updates (depends on WP-1)

**Scope:** `IAreaBuilderSystem` + `AreaBuilderSystem`; `MkareaCommand`; `ListCommand`; `AreaCreatedByAdminEvent`; `AdminAuditHandler` extension; wire all into `AdminModule`; system-unit tests; `flows/README.md` rows for Flow 27 and Flow 28; `reference/systems.md` and `reference/handlers.md` updates.

**Files:**
- `Core/Modules/Admin/Systems/IAreaBuilderSystem.cs`
- `Core/Modules/Admin/Systems/AreaBuilderSystem.cs`
- `Core/Modules/Admin/Commands/MkareaCommand.cs`
- `Core/Modules/Admin/Commands/ListCommand.cs`
- `Core/Modules/Admin/Events/AreaCreatedByAdminEvent.cs`
- `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` (add `AreaCreatedByAdminEvent` case)
- `Core/Modules/Admin/AdminModule.cs` (wire new services and commands)
- `Hedron.Tests/Modules/Admin/Systems/AreaBuilderSystemTests.cs`
- `docs/architecture/flows/README.md`
- `docs/architecture/flows/flow-27-admin-area-creation.md`
- `docs/architecture/flows/flow-28-admin-entity-list.md`
- `docs/reference/systems.md`
- `docs/reference/handlers.md`
- `docs/reference/commands.md`

**Exit criterion:** `dotnet build` and `dotnet test` green. `mkarea` creates a YAML file, `list area` shows the new entity in the table.

**Out of scope:** `setarea`, `area` inspect command (both exist), area-to-room YAML round-trip test (covered by WP-1 exit criterion).

---

## Content Tooling Impact

- **`mkarea [name]`** — creates an area entity and writes `content/areas/<blueprintId>.yaml`. The blueprint ID is printed to the admin console for immediate use in `setarea` assignments.
- **`list area`** / **`list room`** — read-only inspection covering the two entity types introduced in this and prior slices. Enables quick verification that authored content was loaded.
- **No new `TemplateRegistry` shape** — `AreaTemplate` is already registered by `AreaTemplateDeserializer` at startup. `mkarea` writes a new YAML file that will be loaded by `WorldContentLoader` on the next `reload` or restart.
- **No new YAML field shape** — the DTO written by `IAreaContentWriter` uses the same camelCase fields (`id`, `name`, `description`, `respawnRate`, `pvp`, `rooms`, `aspectAffinities`) already read by `AreaTemplateDeserializer`.

---

## Cross-Cutting Surfaces Stressed

**Commands framework:** Adequate — existing `ICommand` shape, `AdminRequirement`, and `CommandArgumentSchema` cover both `mkarea` (optional rest-of-line arg) and `list` (required token arg) without modification.

**Output framework:** Adequate — `PlainMessage` / `IOutputWriter` handles the tabular `list` output and the `mkarea` confirmation. No new message type is needed.

**Content writing:** Gap exposed (resolved in this slice). `IRoomContentWriter` exists for rooms; `IAreaContentWriter` does not yet exist for areas. `mkarea` needs it. `AreaContentWriter` ships in WP-1 — same PR.

**Event bus:** Adequate — `IEventBus.PublishAsync` covers `AreaCreatedByAdminEvent`; `AdminAuditHandler` already handles the handler-extension pattern.

**ECS queries:** Adequate — `EntityService.GetAllComponents<T>()` supports component-type scans for both `AreaComponent` and `RoomComponent`; `AreaCommand` already uses this pattern.

**Persistence — entity domain classification:**
- Area entities created by `mkarea` are **world content** (INV-23). No `PersistentEntity` is added. YAML is the sole durable form; the file is written by `IAreaContentWriter`. No `SaveEntityAsync` call anywhere in this slice.
- `ListCommand` is read-only; it does not create or modify entities.

**Persistence — component inclusion:**
- `AreaComponent`: world content component; no `[Persistent]` attribute. Rationale: area entities are never SQLite-enrolled.
- `BlueprintComponent`: world content component; no `[Persistent]`. Same rationale.
- No new `[Persistent]` components in this slice.

**Persistence — save-on-change scope:** No `SaveEntityAsync` calls in this slice. `IAreaContentWriter.WriteAsync` writes YAML (not SQLite). This satisfies INV-22: world content is never SQLite-persisted.

**TemplateRegistry:** Adequate — `Register(blueprintId, template)` already supports `AreaTemplate`. `AreaBuilderSystem.CreateArea` registers the new template; `IAreaContentWriter` writes the corresponding YAML so the template survives restart.

**Broadcast:** Not stressed — both commands write only to the invoking admin's session; no room broadcast.

**Time:** Not stressed — neither command interacts with the heartbeat or wall clock.

**Configuration:** Adequate — `AreaContentWriter` reads `WorldConfiguration.ContentDirectory` (or `IConfiguration["World:ContentDirectory"]`) using the same pattern as `RoomContentWriter`. No new configuration keys.

**Sessions:** Adequate — admin session access follows the existing command-framework pattern.

**Modules:** Adequate — `AdminModule.cs` is the DI composition point; no new module needed.

---

## Flows Introduced or Modified

| # | Flow | Change |
|---|---|---|
| 27 | Admin area creation (`mkarea`) | New — append row to `flows/README.md`; create `flow-27-admin-area-creation.md` |
| 28 | Admin entity list (`list`) | New — append row to `flows/README.md`; create `flow-28-admin-entity-list.md` |

No existing flows are modified. Flow 27 follows the same structural shape as Flow 12 (admin item creation) and Flow 15 (admin mob creation). Flow 28 is the first read-only inspection flow with tabular output; it does not touch any existing flow.

---

## Test Plan / Verification

**System-unit tests (tier: system-unit, `Hedron.Tests/Modules/Admin/Systems/AreaBuilderSystemTests.cs`):**

1. `AreaBuilderSystem_CreateArea_ReturnsEntityWithComponents` — call `CreateArea("Test Area")`, assert: returned `AreaEntityId != 0`; entity has `AreaComponent` with `Name == "Test Area"`; entity has `BlueprintComponent` with `BlueprintId` starting with `"area.adhoc."`; `TemplateRegistry.TryGet(blueprintId, out _)` is true.

2. `AreaBuilderSystem_CreateArea_BlueprintIdIsUnique` — call `CreateArea` twice in the same session; assert that the two returned `BlueprintId` values differ.

**Legitimately skipped (per rubric in `docs/architecture/07-testing.md`):**

- `MkareaCommand` — thin command wiring: reads one argument, calls one system method, publishes one event, calls one writer, writes one confirmation line. No game-rule logic resides in the command body; the decision logic is in `AreaBuilderSystem` (covered above).
- `ListCommand` — thin read-only inspection with no internal state; the only logic is truncating a description string to 15 chars. Presentation output is legitimately skip-tier per the rubric.
- `IAreaContentWriter` / `AreaContentWriter` — thin I/O adapter. The rubric permits skipping thin I/O adapters; the WP-1 exit criterion (YAML round-trip with `AreaTemplateDeserializer`) satisfies INV-25 for this case because: (a) `AreaContentWriter` is an exact structural copy of `RoomContentWriter` (same atomic-write pattern, same DTO serialization approach, same YamlDotNet configuration) with only the DTO field names differing; (b) the DTO field names are validated at startup by `AreaTemplateDeserializer` itself — any mismatch produces a deserialization failure or empty fields that the WP-1 round-trip check catches; (c) `RoomContentWriter` has no unit test and is accepted by the existing test suite. A formal integration test in `Hedron.Tests` would add coverage for the I/O adapter tier but is not required by the rubric for a thin-copy adapter.
- `AreaCreatedByAdminEvent` — pure-data record; no logic to test.
- `AdminAuditHandler` extension — the new `AreaCreatedByAdminEvent` case adds one `ILogger.LogInformation` call with no branching; skipped as thin wiring per the rubric.

**Coverage contract:** the two postconditions that assert player-invisible internal state are (a) `AreaEntityId != 0` and (b) blueprint ID uniqueness. Both are covered by the named system-unit tests above.

---

## Design Notes

- **`IAreaBuilderSystem` mirrors `IRoomBuilderSystem`, not `IItemBuilderSystem`.** Areas are world content; they are never SQLite-enrolled and never carry `PersistentEntity`. The builder creates the entity, attaches world-content components, and registers the template — no `SaveEntityAsync` call is needed or correct. Items carry `PersistentEntity` because they enter player inventories; areas never do.

- **`IAreaContentWriter` lives in `Core/Modules/World/` by analogy with `IRoomContentWriter`.** The content writer is a World-layer concern (it knows the `AreaTemplate` DTO shape and the `content/areas/` path); the builder that calls it lives in `Core/Modules/Admin/` (it knows the admin authoring workflow). The command calls both in order — builder first, then writer — keeping each layer at its correct scope (INV-1).

- **`ListCommand` queries `EntityService` directly rather than through a system.** The existing `AreaCommand` already sets this precedent for admin inspection commands. A dedicated `IListSystem` would provide no reuse benefit; the query is a one-liner component scan with no game-rule logic. If a third admin command needs the same tabular output formatting, extract a shared helper at that point (INV-19: ≥3-consumer threshold). This acknowledged debt is tracked in `docs/roadmap/backlog.md` as "tabular output helper — defer until third consumer."

- **Blueprint ID `area.adhoc.<base36>`.** Same 8-character base-36 generation pattern used by `RoomBuilderSystem` (`room.adhoc.*`) and `ItemBuilderSystem` (`item.adhoc.*`). Collision probability at MUD-scale area counts is negligible; the uniqueness test in the test plan asserts the property for two sequential calls.

- **`list` does not prefix-match the type token.** The command accepts exactly `area` or `room` (case-insensitive). Prefix-matching on a two-value enum adds complexity with no real benefit; unknown tokens produce an error message listing the accepted values.

- **No `mkroom` command.** Rooms are created via `@dig` (which inherits area context). `mkarea` fills the gap on the area side. A standalone `mkroom` (no exit context) is deferred pending a concrete use case.

---

## Related

- [`area-model.md`](../features/world/world.md) — implemented slice that introduced `AreaComponent`, `AreaTemplate`, `IAreaSystem`, `AreaCommand`, and `SetAreaCommand`. This slice is a direct continuation of that work.
- [`items-and-inventory.md`](../features/items/items.md) — `MkitemCommand` and `IItemBuilderSystem` are the command/builder patterns mirrored here.
- [`mobs.md`](mobs.md) — `MkmobCommand` and `IMobBuilderSystem` are additional reference patterns.
- [`bare-bones-content-spawning.md`](../features/world/world.md) — `IRoomBuilderSystem` and `IRoomContentWriter` are the direct structural models for `IAreaBuilderSystem` and `IAreaContentWriter`.
- [`world-content-loading-and-admin-substrate.md`](../features/world/world.md) — `WorldContentLoader`, `TemplateRegistry`, and the `content/` directory layout established here.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — INV-22/23 rules that govern why area entities carry no `PersistentEntity`.
- [`../reference/systems.md`](../reference/systems.md) — `IRoomBuilderSystem`, `IItemBuilderSystem`, `IMobBuilderSystem` entries are the catalog models for the new `IAreaBuilderSystem` entry.
- [`../reference/handlers.md`](../reference/handlers.md) — `AdminAuditHandler` entry is extended.
- [`../architecture/checklist.md`](../architecture/checklist.md) — invariants cited: INV-1, INV-2, INV-5, INV-10, INV-18, INV-19, INV-21, INV-22, INV-23, INV-25.
