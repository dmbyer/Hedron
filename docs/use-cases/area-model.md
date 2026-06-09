# Area Model + Room–Area Membership

- **Status:** planned
- **Actors:** System (world content loader links rooms to areas at startup; startup validation sweeps area aspect compositions); Administrator (`area`, `setarea` commands)
- **Module:** `Core/Modules/World/` (new `IAreaSystem`/`AreaSystem`; `WorldContentLoader` extension; `AreaTemplate`/`AreaTemplateDeserializer` extension); `Core/Modules/Admin/` (two new admin commands; `@dig` area inheritance)
- **Description:** Establish a proper bidirectional area model: `RoomComponent` carries a runtime-resolved `AreaEntityId` (uint, 0 = none); game logic enumerates all rooms in an area through a new `IAreaSystem`; area entities optionally carry `AspectAffinitiesComponent` to declare an elemental theme authored in YAML. Three area modes are supported by existing ECS primitives with no new machinery — authored static (YAML-backed, always present), spawned area instance (template-spawned for a session, ephemeral), and in-memory only (generated, no template, ephemeral). All area entities are world content and are never SQLite-persisted (06-persistence.md). Cross-area room exits are already transparent through the entity-ID exit map and require no change. This slice closes the "Locale enhancements / room-to-area membership" backlog item (`backlog.md`).

---

## Design notes

> Durable seam rationale — the non-obvious "why here" that survives trim-on-ship (INV-D2).

- **Bidirectional via component field + query, not a stored list.** `RoomComponent` carries `uint AreaEntityId` (one direction); the reverse — area → rooms — is a scan over `EntityService.GetAllComponents<RoomComponent>()` inside `IAreaSystem.GetRoomsInArea`. Storing a `List<uint> RoomIds` on `AreaComponent` would require concurrent maintenance (a room is added via `@dig`, assigned via `@setarea`, or spawned from a template — all mutation sites would have to keep the list in sync). The scan is O(n) over room entities; at MUD-scale room counts this is not a hot path. A cache layer inside `AreaSystem` is a future optimization if profiling shows real cost (backlog).

- **`AreaEntityId` is a runtime field; durability lives in the template's `AreaId`.** `RoomTemplate.AreaId`, `AreaTemplateDeserializer`, and `RoomContentWriter.RoomDto.AreaId` already exist and round-trip through YAML. `@dig`-created rooms are YAML-persisted (not SQLite-enrolled — `DigCommand` calls `_contentWriter.WriteAsync`, not `SaveEntityAsync`), so the template is the durable store for every room's area reference. `RoomComponent.AreaEntityId` is purely runtime: set at startup by the new `LinkRoomAreas` phase and updated in-process by `IAreaSystem.AssignRoomToArea`. No blueprint-ID field is needed on `RoomComponent` — the YAML/template already carries it. This contrasts with `LocationComponent`, which stores `RoomBlueprintId` because player entities are SQLite-persisted across restarts; room entities are not.

- **Three area modes via existing primitives — no new ECS machinery.** Areas are **world content** (06-persistence.md): they are never enrolled in `PersistentEntity` and are never written to SQLite. All three modes are ephemeral across server restarts unless backed by a YAML file.
  1. *Authored static* — YAML file present → `BlueprintComponent` + `AreaComponent` + optional `AspectAffinitiesComponent`; spawned fresh from YAML each startup. Always present as long as the file exists. The standard world content path.
  2. *Spawned area instance* — area entity spawned from a YAML template for the current session only (e.g., a dungeon floor with modifiers applied at spawn time). Ephemeral: exists in-memory only for the session; vanishes on restart. No `PersistentEntity`. The template retains its `BlueprintComponent` as an origin record (INV-21); spawn-slot vacancy for a multi-instance mode is tracked via domain events, not by clearing `BlueprintComponent`.
  3. *In-memory only (generated)* — area and room entities created directly in `EntityService` by a future `IAreaSystem.CreateArea` call, carrying no `BlueprintComponent` and no `PersistentEntity`. Ephemeral. Not implemented in this slice (no concrete consumer yet), but the design accommodates it without change — `RoomComponent.AreaEntityId` is set directly, the query scan works identically.

- **Area aspect affinities reuse `AspectAffinitiesComponent`.** Areas are entities; the component is already designed to be carried by any entity. The `AffinityWeights` map on the component (e.g., Fire → 60, Lightning → 40) declares the area's elemental character. No system consumes this in the current slice — the data is present for future "area aura" mechanics (buff/debuff resistances for entities inside the area). `RegistryValidationBootstrap` gains a sweep over area entities to validate that any authored `AspectAffinities` composition is either empty or sums to 100, consistent with the same check already performed on ability definitions.

- **`AssignRoomToArea` is sync; the content writer is the caller's responsibility.** `IAreaSystem.AssignRoomToArea(uint roomEntityId, uint areaEntityId, string areaBlueprintId)` mutates `RoomComponent.AreaEntityId`, and if the room has a `BlueprintComponent`, mirrors `areaBlueprintId` into the in-memory `RoomTemplate.AreaId`. The `@setarea` command then calls `_contentWriter.WriteAsync(roomTemplate)` to flush the change to YAML — the same pattern used by `@dig` and `@set`. Domain systems return results; they do not perform async I/O (INV-5).

- **`@dig` inherits the source room's area.** When an admin digs a new room, the new room is a continuation of the same area — making the admin manually `@setarea` every `@dig` would be error-prone. `DigCommand` reads `sourceRoom.AreaEntityId`, looks up the corresponding area entity's `BlueprintComponent.BlueprintId`, and passes it to `RoomBuilderSystem.CreateRoom`. No new template field is needed; this is passed through to `RoomTemplate.AreaId` and written to YAML via the existing `_contentWriter.WriteAsync` call. If the source room has no area (`AreaEntityId == 0`), the new room inherits no area either.

---

## Preconditions

- `AreaComponent`, `RoomComponent`, `AreaTemplate`, `RoomTemplate` exist (slices 2, 5a).
- `RoomTemplate.AreaId`, `RoomTemplateDeserializer` (reads `areaId` from YAML), and `RoomContentWriter.RoomDto.AreaId` already exist and round-trip through YAML.
- `AreaTemplate.Rooms` list (list of room blueprint IDs) exists in the template but is not used for entity-level linking today.
- `AspectAffinitiesComponent` exists (slice 11-d); `RegistryValidationBootstrap` exists and runs at startup (slice 11-d).
- `WorldContentLoader.LoadAndSpawnAsync` runs the phases: `LoadTemplates` → `SpawnMissingEntities` → `LinkRoomExits` → `PlaceItemsInRooms` → `PlaceMobsInRooms` → `ResolveStartingRoom`.
- `IRoomBuilderSystem.CreateRoom(string name, string description)` exists; `@dig` calls it.
- `@dig`-created rooms are YAML-persisted via `_contentWriter.WriteAsync` (no `PersistentEntity` enrollment, no `SaveEntityAsync`).

## Postconditions

- `RoomComponent` carries `uint AreaEntityId` (0 = not assigned). The new field carries no `[Persistent]` attribute — `AreaEntityId` is a runtime-resolved value; its durable form is `RoomTemplate.AreaId` in YAML.
- `WorldContentLoader.LoadAndSpawnAsync` includes a `LinkRoomAreas` phase (inserted after `PlaceMobsInRooms`) that sweeps all room entities: for YAML-spawned rooms it reads the corresponding `RoomTemplate.AreaId`; for both YAML and live rooms, it resolves the area blueprint to an entity ID via the live blueprint map and sets `RoomComponent.AreaEntityId`. Unresolvable area references log a warning and leave `AreaEntityId = 0` — no crash, no cascade. `ReloadAsync` runs the same phase.
- A new `IAreaSystem` (domain system, `Core/Modules/World/Systems/`) provides:
  - `IReadOnlyList<uint> GetRoomsInArea(uint areaEntityId)` — scans `EntityService.GetAllComponents<RoomComponent>()`.
  - `uint? GetAreaForRoom(uint roomEntityId)` — reads `RoomComponent.AreaEntityId`; returns null if 0.
  - `void AssignRoomToArea(uint roomEntityId, uint areaEntityId, string areaBlueprintId)` — sets `RoomComponent.AreaEntityId` and mirrors `areaBlueprintId` to the in-memory `RoomTemplate.AreaId` if the room has a `BlueprintComponent`.
- `AreaTemplate` carries `Dictionary<AspectId, int>? AspectAffinities` (null = no affinities authored). `AreaTemplate.Apply` optionally attaches `AspectAffinitiesComponent { AffinityWeights = AspectAffinities }` to the area entity when non-null.
- `AreaTemplateDeserializer` reads an optional `aspectAffinities` block from YAML (camelCase key → `AspectId` enum, value → weight int). Unknown aspect names log a warning and are skipped. The parsed map is validated at startup by `RegistryValidationBootstrap`.
- `RegistryValidationBootstrap` sweeps all entities with `AreaComponent` that also carry `AspectAffinitiesComponent` and validates that `AffinityWeights` is empty or sums to 100 (using `AspectComposition.IsValid()`). Failure is fail-fast: full report + throw (same behavior as existing registry validation).
- `IRoomBuilderSystem.CreateRoom` gains an optional `string areaId = ""` parameter; if non-empty, `AssignRoomToArea` is called after creation and `template.AreaId` is set.
- Admin command `area [blueprintId]`: no args = area of the admin's current room; with a blueprint ID arg = that area. Outputs area name, description, aspect affinities if present, and a list of rooms (name + blueprint ID). Admin-gated.
- Admin command `setarea <roomBlueprintId> <areaBlueprintId>`: assigns a room to an area, mirrors to template, writes room YAML, publishes `RoomAreaAssignedByAdminEvent`. Admin-gated.
- `@dig` inherits the current room's area: the new room is automatically assigned to the source room's area (if any) at creation time; the YAML for the new room includes `areaId`.
- The backlog item "🔵 Locale enhancements / room-to-area membership" is retired in `backlog.md`.

## Main flow

1. **Startup — `LinkRoomAreas`.** After `PlaceMobsInRooms`, `WorldContentLoader` sweeps all room entities via `EntityService.GetAllComponents<RoomComponent>()`. For each room entity, it looks up the corresponding template in `TemplateRegistry` via the room's `BlueprintComponent.BlueprintId` (if present), reads `RoomTemplate.AreaId`, resolves it to an area entity ID via the live blueprint map, and sets `RoomComponent.AreaEntityId`. Rooms with no blueprint, an empty `AreaId`, or an unresolvable area blueprint get `AreaEntityId = 0`. Unresolvable references are logged as warnings. The phase publishes nothing (closed sweep, INV-10). `ReloadAsync` runs the same phase.

2. **Query: room → area.** `IAreaSystem.GetAreaForRoom(roomEntityId)` reads `RoomComponent.AreaEntityId`. Returns null if 0.

3. **Query: area → rooms.** `IAreaSystem.GetRoomsInArea(areaEntityId)` scans `EntityService.GetAllComponents<RoomComponent>()` and collects entity IDs where `AreaEntityId == areaEntityId`. The result is a snapshot list; callers do not retain it across mutations.

4. **Admin `area [blueprintId]`.** Admin executes `area` (no args = current room's area) or `area core.area.midlands`. Command resolves the area entity via `IAreaSystem.GetAreaForRoom` (no args) or by looking up the blueprint ID in the live blueprint map. Reads `AreaComponent` for name and description; calls `IAreaSystem.GetRoomsInArea`; reads each room's `RoomComponent.Name` and `BlueprintComponent.BlueprintId`. Writes a structured inspection message. No events published.

5. **Admin `setarea <roomBlueprintId> <areaBlueprintId>`.** Admin executes `setarea room.adhoc.abc12345 core.area.midlands`. Command resolves both blueprint IDs to entity IDs. Calls `IAreaSystem.AssignRoomToArea(roomEntityId, areaEntityId, areaBlueprintId)`. Calls `_contentWriter.WriteAsync(roomTemplate)` to persist the updated `AreaId` to YAML. Publishes `RoomAreaAssignedByAdminEvent`. `AdminAuditHandler` logs.

6. **`@dig` area inheritance.** Admin executes `dig north "Cave Entrance"` from a room with `AreaEntityId != 0`. `DigCommand` reads `sourceRoom.AreaEntityId`, finds the area entity's `BlueprintComponent.BlueprintId`, and passes it as `areaId` to `RoomBuilderSystem.CreateRoom`. `CreateRoom` sets `template.AreaId = areaId` and calls `AssignRoomToArea`. The new room's YAML (written by the existing `_contentWriter.WriteAsync`) includes `areaId`. If the source room has no area, the new room inherits no area.

7. **Area YAML with aspect affinities.** YAML in `content/areas/` includes an optional `aspectAffinities` map. `AreaTemplateDeserializer` reads it and populates `AreaTemplate.AspectAffinities`. `AreaTemplate.Apply` attaches `AspectAffinitiesComponent` to the area entity. At startup, `RegistryValidationBootstrap` validates the composition — if weights don't sum to 100, boot fails with a full report. No consumer reads these affinities in this slice; they are authored and stored for future area aura mechanics.

8. **Cross-area exits.** Movement uses `RoomComponent.Exits[direction]` → room entity ID. The target room's `AreaEntityId` is not consulted. No change is needed; exits are already transparent across areas.

## Events fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `RoomAreaAssignedByAdminEvent` | `SetAreaCommand` | `uint AdminEntityId, uint RoomEntityId, string RoomBlueprintId, uint AreaEntityId, string AreaBlueprintId` | Audit log. |

No events from `IAreaSystem` (domain system returns results, INV-5). `LinkRoomAreas` is a closed startup sweep, publishes nothing (INV-10).

---

## Content tooling impact

**YAML schema extension — area file.** `AreaTemplate` gains an optional `aspectAffinities` block. Weights must sum to 100 or be absent entirely.

```yaml
schemaVersion: 1
kind: area
id: core.area.midlands
name: The Midlands
description: Rolling plains scarred by ancient battles.
aspectAffinities:
  Fire: 30
  Lightning: 70
rooms:
  - core.room.town-square
  - core.room.north-gate
```

The `rooms` list in the area file is already supported in the template and is the preferred way to declare area membership for authored content. It is used only for validation in this slice (checking that each listed room blueprint exists); `LinkRoomAreas` reads the area membership from the **room** file's `areaId` field as the authoritative source, since that's what the `@dig`/`@setarea` content-writer path writes.

**Admin commands:**
- `area [blueprintId]` — inspect an area: name, description, aspect affinities, member rooms. No args = current room's area.
- `setarea <roomBlueprintId> <areaBlueprintId>` — reassign a room to an area at runtime and persist the change to YAML.

**`@dig` extension:** new rooms automatically inherit the source room's area; the written YAML includes `areaId`.

---

## Cross-cutting surfaces stressed

| Surface | Classification | Reason |
|---|---|---|
| ECS component query (`GetAllComponents<T>`) | **Adequate** | `GetRoomsInArea` and `LinkRoomAreas` scan room entities — same pattern used by `LocationSystem`, `SpawnSystem`, and the persistence flush. No new shape. |
| Event bus | **Adequate** | One new past-tense audit event (`RoomAreaAssignedByAdminEvent`). Follows existing admin event conventions. |
| Persistence (YAML, two-level opt-in) | **Adequate** | `AreaEntityId` is runtime-only (resolved at startup). The durable area reference lives in `RoomTemplate.AreaId`, written by `RoomContentWriter` — the same path already used for room name/description/exits. No new persistence surface. |
| Content template system | **Adequate** | `AreaTemplateDeserializer` gains an `aspectAffinities` field — additive extension to existing DTO. Pattern mirrors how `RoomTemplate` carries `SpawnRules`. `AreaTemplate.Apply` gains a conditional `AddComponent` call. |
| Admin command framework | **Adequate** | `area` and `setarea` are standard admin commands using the existing `AdminRequirement` gate, `CommandContext`, and `IOutputWriter`. |
| `RegistryValidationBootstrap` | **Adequate — extended.** The bootstrap gains an entity-scan sweep (over area entities with `AspectAffinitiesComponent`) in addition to its existing registry sweeps. This is new code but fits within the existing single-responsibility of "fail-fast validation at startup." No new framework needed; the sweep is added as a private method in the existing class. |
| Output framework | **Adequate** | `area` command output uses existing message types (`PlainMessage`, or a new `AreaInspectionMessage` if a structured format is desired — this is a presentation choice for the implementer). |
| `IRoomContentWriter` / `RoomContentWriter` | **Adequate** | `RoomContentWriter.RoomDto.AreaId` already exists and is written. The `@setarea` command calls the existing `WriteAsync` after `AssignRoomToArea`. |

---

## Flows introduced or modified

| Flow | Change |
|---|---|
| **flow-01 — Server startup** | Step 7 (`WorldContentLoader.LoadAndSpawnAsync`) extended: a new `LinkRoomAreas` phase runs after `PlaceMobsInRooms`. Both the Mermaid sequence diagram (new `WCL->>WCL: LinkRoomAreas` step after `PlaceMobsInRooms`) and the prose step 7 description must be updated in `flow-01-server-startup.md`. |
| **flow-08 — Admin room creation (`@dig`)** | `@dig` now passes `sourceRoom.AreaEntityId → areaBlueprintId` to `CreateRoom`; the written YAML includes `areaId`. Flow-08 description must be updated. |

No new canonical flow is promoted. `area` and `setarea` follow the standard admin-command shape (flow-03 variant) and do not need a dedicated flow file.

---

## Test plan / Verification

**All tests in `Hedron.Tests/`; tiers per `docs/architecture/07-testing.md`.**

| Test | Tier | Asserts |
|---|---|---|
| `AreaSystem_GetRoomsInArea_ReturnsOnlyMatchingRooms` | system-unit | Two rooms with matching `AreaEntityId`, one with a different ID; query returns exactly the two matching ones. |
| `AreaSystem_GetRoomsInArea_ReturnsEmpty_WhenNoRoomsInArea` | system-unit | Area entity with no rooms assigned; query returns empty list. |
| `AreaSystem_GetAreaForRoom_ReturnsMembership` | system-unit | Room with `AreaEntityId != 0`; returns that ID. |
| `AreaSystem_GetAreaForRoom_ReturnsNull_WhenUnassigned` | system-unit | Room with `AreaEntityId == 0`; returns null. |
| `AreaSystem_AssignRoomToArea_SetsAreaEntityIdAndMirrorsTemplate` | system-unit | After `AssignRoomToArea`, room component has correct `AreaEntityId`; in-memory template has correct `AreaId`. |
| `WorldContentLoader_LinkRoomAreas_SetsAreaEntityId` | flow | Area + room templates with matching `AreaId`; after `LoadAndSpawnAsync`, `RoomComponent.AreaEntityId` equals the spawned area entity ID. |
| `WorldContentLoader_LinkRoomAreas_ToleratesMissingArea` | flow | Room template references non-existent area blueprint; `AreaEntityId` stays 0; no exception thrown. |
| `WorldContentLoader_ReloadAsync_RelinkRoomAreas` | flow | After `ReloadAsync`, room-area membership is re-resolved correctly. |
| `AreaTemplateDeserializer_ParsesAspectAffinities` | system-unit | YAML with `aspectAffinities: { Fire: 60, Lightning: 40 }` → `AspectAffinitiesComponent.AffinityWeights` matches expected dictionary. |
| `AreaTemplateDeserializer_AbsentAspectAffinities_NoComponent` | system-unit | YAML without `aspectAffinities` → area entity has no `AspectAffinitiesComponent`. |
| `AreaTemplateDeserializer_UnknownAspectKey_Skipped` | system-unit | YAML with an unrecognized aspect name → that key is absent from the parsed weights; no exception. |
| `RegistryValidationBootstrap_RejectsInvalidAreaAspectComposition` | system-unit | Area entity with `AspectAffinitiesComponent` whose weights sum to 90 → bootstrap throws with a descriptive message. |
| `RegistryValidationBootstrap_AcceptsValidAreaAspectComposition` | system-unit | Area entity with weights summing to 100 → bootstrap does not throw. |
| `RegistryValidationBootstrap_AcceptsAreaWithNoAffinities` | system-unit | Area entity without `AspectAffinitiesComponent` → bootstrap does not throw. |

**On-touch ratchet:** `WorldContentLoader.LoadAndSpawnAsync` and `RoomBuilderSystem.CreateRoom` are modified; any existing tests for those methods must continue to pass.

**Not tested (per rubric):**
- `area` and `setarea` command output prose (presentation layer).
- `@dig` area-inheritance plumbing — thin command logic; covered by the `AssignRoomToArea` system-unit test above.
- `RoomAreaAssignedByAdminEvent` payload — thin event data, no player-invisible invariant.
- Cross-area exit behavior — no change to exit resolution; covered by existing movement tests if present.

---

## Implementation plan — work packages

### WP-1: `RoomComponent.AreaEntityId` + `IAreaSystem`/`AreaSystem` + `WorldContentLoader.LinkRoomAreas`

**Scope:**
- Add `uint AreaEntityId` (0 = none) to `RoomComponent`.
- Create `Core/Modules/World/Systems/IAreaSystem.cs` and `AreaSystem.cs` with `GetRoomsInArea`, `GetAreaForRoom`, `AssignRoomToArea`.
- Add `LinkRoomAreas` phase to `WorldContentLoader.LoadAndSpawnAsync` (after `PlaceMobsInRooms`) and to `ReloadAsync`.
- Extend `IRoomBuilderSystem.CreateRoom` with optional `string areaId = ""` parameter; `RoomBuilderSystem.CreateRoom` sets `template.AreaId` and calls `AssignRoomToArea` when non-empty.
- Update `DigCommand` to pass source room's area blueprint ID to `CreateRoom` (read from area entity's `BlueprintComponent.BlueprintId` via `IAreaSystem.GetAreaForRoom`).
- Register `IAreaSystem`/`AreaSystem` in the World module DI registration.
- Update `flow-01-server-startup.md` (add `LinkRoomAreas` step) and `flow-08` (document area inheritance).

**Files:**
- `Core/ECS/Components/RoomComponent.cs`
- `Core/Modules/World/Systems/IAreaSystem.cs` (new)
- `Core/Modules/World/Systems/AreaSystem.cs` (new)
- `Core/Modules/World/Systems/WorldContentLoader.cs`
- `Core/Modules/Admin/Systems/IRoomBuilderSystem.cs`
- `Core/Modules/Admin/Systems/RoomBuilderSystem.cs`
- `Core/Modules/Admin/Commands/DigCommand.cs`
- World module DI registration
- `docs/architecture/flows/flow-01-server-startup.md`
- `docs/architecture/flows/flow-08-admin-room-creation.md`
- `docs/reference/systems.md` — add `IAreaSystem`/`AreaSystem` entry
- `docs/reference/components.md` — update `RoomComponent` row to document the new `AreaEntityId` field
- `Hedron.Tests/` — tests for rows 1–8 in the test plan above

**Dependencies:** none — self-contained.

**Exit criterion:** tests for rows 1–8 pass; `dotnet test` green; `@dig` writes the correct `areaId` to the room's YAML file.

---

### WP-2: Area aspect affinities + `area`/`setarea` admin commands

**Depends on:** WP-1

**Scope:**
- Extend `AreaTemplate` with `Dictionary<AspectId, int>? AspectAffinities`; update `Apply` to conditionally attach `AspectAffinitiesComponent`.
- Extend `AreaTemplateDeserializer.AreaDto` with `Dictionary<string, int>? AspectAffinities`; parse and validate during deserialization (warn on unknown aspect keys; skip them).
- Extend `RegistryValidationBootstrap` to sweep area entities with `AspectAffinitiesComponent` and validate `AffinityWeights` composition.
- Add `Core/Modules/Admin/Events/RoomAreaAssignedByAdminEvent.cs` (new).
- Add `Core/Modules/Admin/Commands/AreaCommand.cs` (new) — `area [blueprintId]`.
- Add `Core/Modules/Admin/Commands/SetAreaCommand.cs` (new) — `setarea <roomBlueprintId> <areaBlueprintId>`.
- Register both commands in the Admin module.
- Update `AdminAuditHandler` to handle `RoomAreaAssignedByAdminEvent`.
- Update `backlog.md`: retire the "room-to-area membership" bullet from the "🔵 Locale enhancements" item.

**Files:**
- `Core/Modules/World/Templates/AreaTemplate.cs`
- `Core/Modules/World/Templates/AreaTemplateDeserializer.cs`
- `Core/Modules/Admin/Events/RoomAreaAssignedByAdminEvent.cs` (new)
- `Core/Modules/Admin/Commands/AreaCommand.cs` (new)
- `Core/Modules/Admin/Commands/SetAreaCommand.cs` (new)
- Admin module DI registration
- `Core/Modules/Admin/Handlers/AdminAuditHandler.cs`
- `Core/Systems/RegistryValidationBootstrap.cs` (or wherever the bootstrap lives)
- `docs/roadmap/backlog.md`
- `docs/architecture/03-events.md` — add `RoomAreaAssignedByAdminEvent` entry
- `docs/reference/commands.md` — add rows for `area` and `setarea`
- `Hedron.Tests/` — tests for rows 9–14 in the test plan above

**Exit criterion:** tests for rows 9–14 pass; `dotnet test` green; a YAML area file with `aspectAffinities` causes `RegistryValidationBootstrap` to fail-fast if the weights don't sum to 100; the `area` command shows rooms and affinities in a running server; `setarea` persists across `@reload`.

---

## Related

- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — slice 5a; introduced `IRoomBuilderSystem`, YAML room persistence, `@dig`; deferred area membership as "Locale enhancements."
- [`aspect-foundation.md`](aspect-foundation.md) — slice 11-d; `AspectAffinitiesComponent`, `AspectComposition`, `RegistryValidationBootstrap`.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; `WorldContentLoader`, `AreaTemplate`, `RoomTemplate`, and their deserializers.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — "🔵 Locale enhancements" backlog item this slice partially closes (coordinate system remains deferred).
- [`../architecture/checklist.md`](../architecture/checklist.md) — invariants cited: INV-1, INV-2, INV-5, INV-10, INV-14, INV-18, INV-19, INV-21, INV-25.
