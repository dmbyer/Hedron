# Phase 3 — Area Model + Room–Area Membership

**PR:** (this branch) · **Spec:** [`../../use-cases/area-model.md`](../../use-cases/area-model.md)

## Outcome

Established a proper bidirectional area model. `RoomComponent` now carries a runtime-resolved `AreaEntityId` (uint, 0 = unassigned), set by a new `LinkRoomAreas` phase in `WorldContentLoader.LoadAndSpawnAsync` and `ReloadAsync` that sweeps all room entities and resolves their YAML-authored `areaId` field to a live entity ID. `IAreaSystem`/`AreaSystem` provides the three query/mutation operations (`GetRoomsInArea`, `GetAreaForRoom`, `AssignRoomToArea`). `AreaTemplate` and `AreaTemplateDeserializer` are extended with an optional `aspectAffinities` block that attaches `AspectAffinitiesComponent` to area entities, and `RegistryValidationBootstrap` validates those compositions at boot. Two new admin commands (`area`, `setarea`) expose area inspection and runtime reassignment; `@dig` inherits the source room's area automatically.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `RoomComponent.AreaEntityId` | `Core/ECS/Components/RoomComponent.cs` | `uint`, default 0, no `[Persistent]` — runtime-resolved; durable form is `RoomTemplate.AreaId` in YAML |
| `IAreaSystem` / `AreaSystem` | `Core/Modules/World/Systems/IAreaSystem.cs`, `AreaSystem.cs` | Domain system; `GetRoomsInArea` (scan), `GetAreaForRoom` (read), `AssignRoomToArea` (set + mirror to template); no event bus (INV-5) |
| `WorldContentLoader.LinkRoomAreas` | `Core/Modules/World/Systems/WorldContentLoader.cs` | Private phase added after `PlaceMobsInRooms` in both `LoadAndSpawnAsync` and `ReloadAsync`; unresolvable area refs log a warning and leave `AreaEntityId = 0` |
| `IRoomBuilderSystem.CreateRoom` extended | `Core/Modules/Admin/Systems/IRoomBuilderSystem.cs` | Optional `string areaId = ""` parameter; backward-compatible |
| `RoomBuilderSystem.CreateRoom` extended | `Core/Modules/Admin/Systems/RoomBuilderSystem.cs` | Sets `template.AreaId`; calls `IAreaSystem.AssignRoomToArea` when area entity found; `IAreaSystem` injected |
| `DigCommand` area inheritance | `Core/Modules/Admin/Commands/DigCommand.cs` | Resolves source room's area via `IAreaSystem.GetAreaForRoom`; passes blueprint ID to `CreateRoom`; `IAreaSystem` injected |
| `WorldModule` DI registration | `Core/Modules/World/WorldModule.cs` | `services.AddSingleton<IAreaSystem, AreaSystem>()` added |
| `AreaTemplate.AspectAffinities` | `Core/Modules/World/Templates/AreaTemplate.cs` | `Dictionary<AspectId, int>?`; `Apply` conditionally attaches `AspectAffinitiesComponent` |
| `AreaTemplateDeserializer` extended | `Core/Modules/World/Templates/AreaTemplateDeserializer.cs` | `AreaDto.AspectAffinities` (`Dictionary<string, int>?`); unknown aspect keys logged + skipped |
| `RegistryValidationBootstrap` extended | `Server/RegistryValidationBootstrap.cs` | `EntityService` injected; section 3 sweeps area entities with `AspectAffinitiesComponent` and validates compositions with `AspectComposition.IsValid` |
| `RoomAreaAssignedByAdminEvent` | `Core/Modules/Admin/Events/RoomAreaAssignedByAdminEvent.cs` | New past-tense audit event; payload: `AdminEntityId`, `RoomEntityId`, `RoomBlueprintId`, `AreaEntityId`, `AreaBlueprintId` |
| `AreaCommand` | `Core/Modules/Admin/Commands/AreaCommand.cs` | Admin `area [blueprintId]` — inspect area name/description/affinities/rooms; no args = current room's area; no events |
| `SetAreaCommand` | `Core/Modules/Admin/Commands/SetAreaCommand.cs` | Admin `setarea <roomBlueprintId> <areaBlueprintId>` — calls `AssignRoomToArea`, persists room YAML, publishes `RoomAreaAssignedByAdminEvent` |
| `AdminModule` updated | `Core/Modules/Admin/AdminModule.cs` | `AreaCommand` and `SetAreaCommand` registered |
| `AdminAuditHandler` extended | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` | `IEventHandler<RoomAreaAssignedByAdminEvent>` added; structured log entry |
| `Program.cs` updated | `Server/Program.cs` | `bus.Subscribe<RoomAreaAssignedByAdminEvent>(audit)` wired |
| Flow 01 updated | `docs/architecture/flows/flow-01-server-startup.md` | `LinkRoomAreas` step added after `PlaceMobsInRooms` in Mermaid diagram and prose |
| Flow 08 updated | `docs/architecture/flows/flow-08-admin-room-creation.md` | Area inheritance documented for `@dig` |
| `docs/reference/systems.md` | | `IAreaSystem`/`AreaSystem` entry added; `RoomBuilderSystem` updated |
| `docs/reference/components.md` | | `RoomComponent` row updated with `AreaEntityId` field |
| `docs/reference/commands.md` | | `area` and `setarea` rows added |
| `docs/architecture/03-events.md` | | `RoomAreaAssignedByAdminEvent` entry added |
| `docs/roadmap/backlog.md` | | "room-to-area membership" bullet retired from Locale enhancements |

## Tests shipped

All tests in `Hedron.Tests/`; 34 new tests (587 total from 566 before this slice + 21 WP-1/WP-2).

| Test | Tier | Asserts |
|---|---|---|
| `AreaSystemTests` (7 tests) | system-unit | `GetRoomsInArea` matching/empty; `GetAreaForRoom` assigned/unassigned; `AssignRoomToArea` sets `AreaEntityId` and mirrors to template; INV-5 guard (no `IEventBus` field) |
| `WorldContentLoaderTests` (4 tests) | flow | `LinkRoomAreas` sets `AreaEntityId` after `LoadAndSpawnAsync`; tolerates missing area blueprint; unassigned room stays 0; `ReloadAsync` re-resolves area links |
| `AreaTemplateDeserializerTests` (5 tests) | system-unit | Parses valid `aspectAffinities`; absent block yields no component; unknown key skipped; mixed known/unknown; `Apply` attaches `AspectAffinitiesComponent` correctly |
| `RegistryValidationTests` additions (3 tests) | system-unit | Area entity with invalid composition (sum ≠ 100) throws; valid (sum = 100) passes; area without `AspectAffinitiesComponent` passes |

On-touch ratchet: `RoomBuilderSystemTests` updated to pass a real `AreaSystem` instance to `RoomBuilderSystem`; all 14 existing `RoomBuilderSystem` tests continue to pass.

`dotnet test` green (587 tests).

## Spec-review provenance

**Spec gate (spec-mode):** Ran before implementation. No blocking findings.

**Code gate (code-mode):** Ran after both work packages landed. (See separate review pass.)

## Notable design points

- **Bidirectional via component field + scan, not a stored list.** `RoomComponent.AreaEntityId` is the one-direction link; the reverse (area → rooms) is an O(n) scan of `GetAllComponents<RoomComponent>()`. Storing a `List<uint>` on `AreaComponent` would require concurrent maintenance across all mutation sites (`@dig`, `@setarea`, startup). Cache layer deferred — not a hot path at MUD-scale room counts.
- **`AreaEntityId` is runtime-only; durable form is `RoomTemplate.AreaId` in YAML.** This parallels the way `LocationComponent.RoomBlueprintId` is the durable string ref for player location. Room entities are world content (never `PersistentEntity`), so there is no SQLite row to carry `AreaEntityId`; it is resolved fresh on every startup and reload.
- **`RegistryValidationBootstrap` extended with `EntityService` injection.** The bootstrap lives in `Server/` (requires `Microsoft.Extensions.Hosting`, not available in `Core`). Adding entity-scan capability requires `EntityService` injection — the constructor grew one parameter. Existing tests in `RegistryValidationTests` pass an empty `new EntityService()`, making the new sweep a no-op for the existing test scenarios.
- **Three area modes via existing ECS primitives — no new machinery.** Authored static (`BlueprintComponent` + YAML-backed), spawned area instance (template-spawned, session-only), and in-memory generated (future `CreateArea` call) all work identically from `IAreaSystem`'s perspective — the scan is blind to how the entity was created.

## Deviations from the use-case doc

None — shipped per spec. All 14 postconditions are met. The `area` command uses `PlainMessage` output (prose strings) per the spec's implementation-choice allowance.

## Follow-ups unlocked

- **Area aura mechanics:** `AspectAffinitiesComponent` is now authored and stored on area entities; a future slice can add ambient damage/resistance modifiers for entities in an area by reading the area entity's `AffinityWeights`.
- **Shopping (slice 12):** No area-model dependency; can proceed independently.
- **Coordinate system / room-to-area enforcement:** Remaining "Locale enhancements" backlog items (coordinate grid, area-level mob-respawn enforcement) are now unblocked by this slice's bidirectional area model.
