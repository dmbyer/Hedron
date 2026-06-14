# Area Model

> Bidirectional room-to-area membership: how rooms know their area, how areas enumerate their rooms, and how elemental affinities are authored on areas. **Authoring checkpoint:** area-model slice. Living document.

## What it is / does

`AreaSystem` is the **domain system** for area–room membership. It provides three operations: query an area's rooms, query a room's area, and assign a room to an area. No event publication (INV-5). The relationship is bidirectional — but not stored bidirectionally. `RoomComponent.AreaEntityId` is the single forward link; the reverse (area → rooms) is an O(n) scan of all room entities inside `IAreaSystem.GetRoomsInArea`. Storing a list on `AreaComponent` would require concurrent maintenance across all mutation sites (`dig`, `setarea`, startup); the scan is not a hot path at MUD-scale room counts.

## How it works

### Runtime link setup

`WorldContentLoader.LinkRoomAreas` (the sixth startup phase, after `PlaceMobsInRooms`) sweeps all room entities. For each room with a `BlueprintComponent`, it reads the corresponding `RoomTemplate.AreaId`, resolves that blueprint id to a live area entity ID via the live blueprint map, and sets `RoomComponent.AreaEntityId`. Rooms with no blueprint, empty `AreaId`, or unresolvable area blueprint get `AreaEntityId = 0` (a warning is logged). `ReloadAsync` runs the same phase.

**`AreaEntityId` is a runtime-only field.** Its durable form is `RoomTemplate.AreaId` in YAML. Room entities are world content (never `PersistentEntity` — [INV-23](../../architecture/checklist.md)); there is no SQLite row to carry it. This parallels how `LocationComponent.RoomBlueprintId` is the durable string ref for player location.

### Three area modes

All three use the same ECS primitives — no new machinery:

| Mode | How created | `BlueprintComponent`? | Ephemeral? |
|---|---|---|---|
| Authored static | YAML file → `WorldContentLoader` | yes | no (YAML is the source) |
| Spawned area instance | template-spawned for a session | yes (as origin record — INV-21) | yes (vanishes on restart) |
| In-memory generated | future `IAreaSystem.CreateArea` call | no | yes |

### Area aspect affinities

`AreaTemplate` carries an optional `aspectAffinities` map (`Dictionary<AspectId, int>`). `AreaTemplate.Apply` attaches `AspectAffinitiesComponent` to the area entity when the map is non-null. `RegistryValidationBootstrap` validates at boot: weights must be empty or sum to 100 (fail-fast, full report). No system consumes area affinities in the current slice — they are authored and stored for future area-aura mechanics (ambient buffs/debuffs for entities inside the area).

### `@dig` area inheritance

When an admin digs a new room, `DigCommand` reads the source room's `AreaEntityId`, looks up the area entity's `BlueprintComponent.BlueprintId`, and passes it as `areaId` to `RoomBuilderSystem.CreateRoom`. The new room is automatically assigned to the source room's area; its YAML includes `areaId`. If the source room has no area, the new room inherits none.

### Admin inspection and mutation

- `area [blueprintId]` — no args = area of the admin's current room; with an id = that area. Reads `AreaComponent` for name/description, `AspectAffinitiesComponent` if present, and calls `GetRoomsInArea` for the room list.
- `setarea <roomBlueprintId> <areaBlueprintId>` — calls `IAreaSystem.AssignRoomToArea`, writes updated room YAML via `IRoomContentWriter`, and publishes `RoomAreaAssignedByAdminEvent`.

## Interface

- [`IAreaSystem.cs`](../../../Core/Modules/World/Systems/IAreaSystem.cs) — `GetRoomsInArea(areaEntityId)` / `GetAreaForRoom(roomEntityId)` / `AssignRoomToArea(roomEntityId, areaEntityId, areaBlueprintId)`. Pure ECS mutations; no event bus (INV-5).

## Considerations

- **Persistence rules are INV-23.** `RoomComponent` and `AreaComponent` are world content — never `[Persistent]` on the entity level (never enrolled in `PersistentEntity`). `RoomComponent.AreaEntityId` carries no `[Persistent]` attribute additionally because it is a runtime-resolved value. Link [INV-23](../../architecture/checklist.md) rather than restating.
- **Cross-area exits are transparent.** Movement uses `RoomComponent.Exits[direction]` → room entity ID. The target room's `AreaEntityId` is not consulted. No `IAreaSystem` involvement in movement.
- **O(n) scan is acceptable.** At MUD-scale room counts this is not a hot path. A cache layer inside `AreaSystem` is a future optimization tracked in backlog.

## Extensibility

- **Area aura mechanics** — `AspectAffinitiesComponent` is authored and stored; a future slice can add ambient resistance modifiers for entities inside the area by reading the area entity's `AffinityWeights`.
- **Coordinate system** — room-to-area enforcement via a coordinate grid is a remaining "Locale enhancements" backlog item.

## Related

- [`world.md`](world.md) — holistic feature view.
- [`world-content.md`](world-content.md) — the `LinkRoomAreas` startup phase and YAML area file shape.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5 (systems pure), INV-21 (blueprint/instance separation), INV-23 (world content persistence).
- [`../../reference/systems.md`](../../reference/systems.md) — `AreaSystem` / `IAreaSystem` catalog row.
- [`../../reference/components.md`](../../reference/components.md) — `RoomComponent` (`AreaEntityId` field), `AreaComponent` rows.
- [`../../roadmap/completed/area-model.md`](../../roadmap/completed/area-model.md) — as-built history and design decisions.
