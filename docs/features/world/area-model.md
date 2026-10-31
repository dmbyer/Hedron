# Area Model

> Bidirectional room-to-area membership: how rooms know their area, how areas enumerate their rooms, how elemental affinities are authored on areas, and an area's (Tier, Band) designation with its out-of-designation check. **Authoring checkpoints:** area-model slice; area-designation (Phase 5 slice 3). Living document.

## What it is / does

`AreaSystem` is the **domain system** for area–room membership. It provides three operations: query an area's rooms, query a room's area, and assign a room to an area. No event publication (INV-5). The relationship is bidirectional — but not stored bidirectionally. `RoomComponent.AreaEntityId` is the single forward link; the reverse (area → rooms) is an O(n) scan of all room entities inside `IAreaSystem.GetRoomsInArea`. Storing a list on `AreaComponent` would require concurrent maintenance across all mutation sites (`dig`, `setarea`, startup); the scan is not a hot path at MUD-scale room counts.

## How it works

### Runtime link setup

`WorldContentLoader.LinkRoomAreas` (the startup phase after `LinkRoomExits`) sweeps all room entities. For each room with a `BlueprintComponent`, it reads the corresponding `RoomTemplate.AreaId`, resolves that blueprint id to a live area entity ID via the live blueprint map, and sets `RoomComponent.AreaEntityId`. Rooms with no blueprint, empty `AreaId`, or unresolvable area blueprint get `AreaEntityId = 0` (a warning is logged). `ReloadAsync` runs the same phase.

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

### Area designation

An area may carry an optional **designation**: the range of (Tier, Band) cells it is meant to hold. Phase 5 authors one tier with a band range (`Tier 0, Bands 1-2`). Undesignated means unchecked.

- **Shape.** [`AreaDesignation`](../../../Core/Modules/World/AreaDesignation.cs) stores `Min`/`Max` as `PowerBand` cells. `AreaDesignationValidation.Errors` is the single shape check:
  - tier in `0..AscensionConstants.MaxTier`;
  - bands in `1..3`, the same bounds as the mob/item `Band` tag;
  - `bandMin ≤ bandMax`;
  - both cells in one tier.

  It has three callers: the area deserializer (throws `ContentShapeException`, so boot fails and `reload` refuses), `IContentValidator.Validate` (refuses an editor save) and `IAreaBuilderSystem.SetAreaDesignation` (refuses an admin mutation). Cross-tier ranges are shaped for later (tiered-overworld borders): allowing them means deleting the same-tier rule.
- **YAML.** `designation: { tier, bandMin, bandMax }`. All three keys are required when the block is present, and an absent block means undesignated. The writer omits the key for an undesignated area.
- **Live form.** `AreaTemplate.Apply` attaches `AreaDesignationComponent` only when designated, mirroring affinities. It is world content and not `[Persistent]` (INV-23).
- **The out-of-designation check.** [`AreaPlacementCheck`](../../../Core/Modules/World/Systems/AreaPlacementCheck.cs) is one pure judgement over *authored* tags; it never calls the power oracle.
  - It flags a room spawn rule only when: the room has an area, the area is designated, the placed mob/item is banded (`Band ≠ 0`), and its authored (Tier, Band) is outside the designation.
  - `Sweep` yields one `PlacementFinding` per offending (room, rule), ordered by room then rule id, optionally filtered to one area (case-insensitive).
  - `PlacementFinding.Describe()` is the one text every surface shows.
  - Findings are **advisory**: never an error, never a gate.
- **Two sources, one judgement.** The check reads content through [`IAreaPlacementSource`](../../../Core/Modules/World/Systems/IAreaPlacementSource.cs), and the source follows the surface:
  - `RegistryPlacementSource` reads the template registry, which in-game verbs mutate. It is wrapped by `IAreaPlacementSystem` (`Findings`, `JudgeRule`).
  - The Authoring `CatalogPlacementSource` reads disk, which is what the editors write ([content-tooling](../admin-authoring/content-tooling.md#iareadesignationaudit--out-of-designation-findings-on-disk)).
  - Both key area membership on `RoomTemplate.AreaId`, and a parity test pins identical findings.
- **Where findings surface:**
  - the boot `ValidateRegistry` report as warnings (boot-only, like the other registry warnings);
  - the `area` verb;
  - `Warning:` lines from `spawnrule`/`mkmob`/`mkitem`;
  - the area editor's findings list;
  - the Integrity page;
  - web spawn-rule save warnings, for new or changed rules only.
- **Independent of band drift.** Band drift (authored vs. *computed* power, `IBalanceAuditSystem`) and designation fit (authored vs. the area) are separate checks. A mis-tagged and misplaced mob appears in both.

### `@dig` area inheritance

When an admin digs a new room, `DigCommand` reads the source room's `AreaEntityId`, looks up the area entity's `BlueprintComponent.BlueprintId`, and passes it as `areaId` to `RoomBuilderSystem.CreateRoom`. The new room is automatically assigned to the source room's area; its YAML includes `areaId`. If the source room has no area, the new room inherits none.

### Admin inspection and mutation

- `area [blueprintId]` — no args = area of the admin's current room; with an id = that area. Reads `AreaComponent` for name/description, `AspectAffinitiesComponent` if present, `AreaDesignationComponent` (or "undesignated") with the area's out-of-designation findings (`IAreaPlacementSystem.Findings`), and calls `GetRoomsInArea` for the room list.
- `areaset <areaBlueprintId> <name|description|designation> <value>` — the area member of the `setitem`/`setmob` setter family. `designation` takes `<tier> <band>`, `<tier> <band>-<band>`, or `clear`/`none` (`AreaDesignation.TryParseOrClear`). `IAreaBuilderSystem` updates the registered template and the live area entity (INV-21); the command writes YAML via `IAreaContentWriter` and publishes `AreaPropertySetByAdminEvent`.
- `setarea <roomBlueprintId> <areaBlueprintId>` — calls `IAreaSystem.AssignRoomToArea`, writes updated room YAML via `IRoomContentWriter`, and publishes `RoomAreaAssignedByAdminEvent`.

## Interface

- [`IAreaPlacementSystem.cs`](../../../Core/Modules/World/Systems/IAreaPlacementSystem.cs) — `Findings(areaBlueprintId?)` / `JudgeRule(roomBlueprintId, rule)`, registry-backed out-of-designation queries. Stateless; publishes nothing.
- [`IAreaSystem.cs`](../../../Core/Modules/World/Systems/IAreaSystem.cs) — `GetRoomsInArea(areaEntityId)` / `GetAreaForRoom(roomEntityId)` / `AssignRoomToArea(roomEntityId, areaEntityId, areaBlueprintId)`. Pure ECS mutations; no event bus (INV-5).

## Considerations

- **Persistence rules are INV-23.** `RoomComponent` and `AreaComponent` are world content — never `[Persistent]` on the entity level (never enrolled in `PersistentEntity`). `RoomComponent.AreaEntityId` carries no `[Persistent]` attribute additionally because it is a runtime-resolved value. Link [INV-23](../../architecture/checklist.md) rather than restating.
- **Cross-area exits are transparent.** Movement uses `RoomComponent.Exits[direction]` → room entity ID. The target room's `AreaEntityId` is not consulted. No `IAreaSystem` involvement in movement.
- **O(n) scan is acceptable.** At MUD-scale room counts this is not a hot path. A cache layer inside `AreaSystem` is a future optimization tracked in backlog.

## Extensibility

- **Designation consumers (tiered-overworld).** Stage A's boundary banner and adjacent-tier gradient warning, Stage B's generator designations and weighted pools, and Stage E's `map` region view all read the live `AreaDesignationComponent`. A designation's cells also map to a power window through `IPowerBudgetSystem.TargetRange`, the "cell anchor" input of the planned relative-power read.

- **Area aura mechanics** — `AspectAffinitiesComponent` is authored and stored; a future slice can add ambient resistance modifiers for entities inside the area by reading the area entity's `AffinityWeights`.
- **Coordinate system** — room-to-area enforcement via a coordinate grid is a remaining "Locale enhancements" backlog item.

## Related

- [`world.md`](world.md) — holistic feature view.
- [`world-content.md`](world-content.md) — the `LinkRoomAreas` startup phase and YAML area file shape.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5 (systems pure), INV-21 (blueprint/instance separation), INV-23 (world content persistence).
- [`../../reference/systems.md`](../../reference/systems.md) — `AreaSystem` / `IAreaSystem` catalog row.
- [`../../reference/components.md`](../../reference/components.md) — `RoomComponent` (`AreaEntityId` field), `AreaComponent` rows.
- [`../../roadmap/completed/area-model.md`](../../roadmap/completed/area-model.md) — as-built history and design decisions.
- [`../../roadmap/completed/area-designation.md`](../../roadmap/completed/area-designation.md) — designation slice history and decisions.
