# Area Model + Room–Area Membership

- **Status:** implemented
- **Actors:** System (world content loader links rooms to areas at startup; startup validation sweeps area aspect compositions); Administrator (`area`, `setarea` commands)
- **Module:** `Core/Modules/World/` (new `IAreaSystem`/`AreaSystem`; `WorldContentLoader` extension; `AreaTemplate`/`AreaTemplateDeserializer` extension); `Core/Modules/Admin/` (two new admin commands; `@dig` area inheritance)
- **Description:** Establish a proper bidirectional area model: `RoomComponent` carries a runtime-resolved `AreaEntityId` (uint, 0 = none); game logic enumerates all rooms in an area through a new `IAreaSystem`; area entities optionally carry `AspectAffinitiesComponent` to declare an elemental theme authored in YAML. Three area modes are supported by existing ECS primitives with no new machinery — authored static (YAML-backed, always present), spawned area instance (template-spawned for a session, ephemeral), and in-memory only (generated, no template, ephemeral). All area entities are world content and are never SQLite-persisted (06-persistence.md). Cross-area room exits are already transparent through the entity-ID exit map and require no change. This slice closes the "Locale enhancements / room-to-area membership" backlog item (`backlog.md`).

---

## Design notes

> Durable seam rationale — the non-obvious "why here" that survives trim-on-ship (INV-28).

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

## Related

- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — slice 5a; introduced `IRoomBuilderSystem`, YAML room persistence, `@dig`; deferred area membership as "Locale enhancements."
- [`aspect-foundation.md`](aspect-foundation.md) — slice 11-d; `AspectAffinitiesComponent`, `AspectComposition`, `RegistryValidationBootstrap`.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; `WorldContentLoader`, `AreaTemplate`, `RoomTemplate`, and their deserializers.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — "🔵 Locale enhancements" backlog item this slice partially closes (coordinate system remains deferred).
- [`../architecture/checklist.md`](../architecture/checklist.md) — invariants cited: INV-1, INV-2, INV-5, INV-10, INV-14, INV-18, INV-19, INV-21, INV-25.
