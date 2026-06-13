# Mob System

> Domain system for mob entity creation, YAML authoring, and template deserialization. **Authoring checkpoint:** slice 8 (attributes extended: slice 8-a). Living document.

## What it is / does

`MobBuilderSystem` is a **domain-tier pure system** that creates ad-hoc mob entities and mutates their properties. It mutates ECS state only — event publication and persistence calls remain in the commands that call it (INV-5). It never touches the event bus and never calls persistence directly.

`MobContentWriter` is the atomic YAML writer for `MobTemplate`: it serializes the template to `{contentDirectory}/mobs/{blueprintId}.yaml` using a tmp-then-rename pattern. `MobTemplateDeserializer` is the symmetric reader, registered as `ITemplateDeserializer` for `kind: mob` with `YamlContentSerializer`.

## How it works

### Mob entity lifecycle

`CreateMob(name, roomEntityId)` generates a blueprint id (`mob.adhoc.<8-char-base36>`), allocates an entity via `EntityService`, and attaches:

- `MobDataComponent { Name, Description="", Keywords=[], MobType=None }`
- `BlueprintComponent { BlueprintId }`
- `PersistentEntity` — mobs survive restarts without a dedicated respawn system today (acknowledged debt; the respawn system will decide whether to reuse or destroy the entity when the death slice lands)
- `LocationComponent { RoomEntityId }` — placed in the invoker's room

A minimal `MobTemplate` is registered in `ITemplateRegistry`. Returns `MobCreationResult(MobEntityId, BlueprintId, Template)`.

### Mutation methods

`Set*` methods (`SetMobName`, `SetMobDescription`, `SetMobKeywords`, `SetMobType`) mutate both the live entity's `MobDataComponent` and the in-memory `MobTemplate` so the assignment survives `reload`. Callers (commands) write YAML after each mutation — the system does not call `IMobContentWriter` (INV-5).

Slice 8-a added `SetAttribute(mobEntityId, template, property, value)` for mutating `AttributesComponent` and `PoolsComponent` properties (`level`, `hp`, `mind`, `body`, `spirit`, `attunement`, `maxmana`, `maxstamina`, `maxastra`). It enforces `CurrentX ≤ MaxX` clamp on pool max changes (INV-8).

### YAML template shape

`MobTemplate` fields: `blueprintId`, `name`, `description`, `keywords`, `type` (string enum — `none`, `vendor`, `guard`, `creature`), `spawnRoomBlueprintId`. Slice 8-a extended the DTO with `level`, `maxHp`, attribute fields (`mind`, `body`, `spirit`, `attunement`), and pool fields (`maxMana`, `maxStamina`, `maxAstra`).

`MobTemplateDeserializer` warns on unknown `type` values and never throws — resilient deserialization is the design contract (unknown fields are ignored; bad fields log and default).

### Spawn from content

`WorldContentLoader` reads files from `<ContentDirectory>/mobs/` on startup and reload. `PlaceMobsInRooms` runs only for entities in the `newlySpawned` set (mirrors `PlaceItemsInRooms`): resolves each template's `SpawnRoomBlueprintId` to a live room entity id and attaches `LocationComponent { RoomEntityId }`. Restored-from-persistence mob entities already carry a saved `LocationComponent` and are skipped. Unknown spawn rooms log a warning; the mob is created without a location.

### `MobType` classification

`MobType` (`None`, `Vendor`, `Guard`, `Creature`) is **data only** — no routing or behavior logic consumes it in the current implementation. It is a classification field for future use (vendor dialogue, guard aggression, AI routing).

## Interface

The seam self-documents in code — describe behaviour here, not signatures:

- [`IMobBuilderSystem.cs`](../../../Core/Modules/Mobs/Systems/IMobBuilderSystem.cs) — `CreateMob` / `SetMobName` / `SetMobDescription` / `SetMobKeywords` / `SetMobType` / `SetAttribute`. Pure: returns results; never touches the bus or persistence (INV-5).
- [`IMobContentWriter.cs`](../../../Core/Modules/Mobs/Systems/IMobContentWriter.cs) — `WriteAsync(MobTemplate, CancellationToken)`. Atomic YAML write (tmp → rename).

## Considerations

- **`MobDataComponent` is cross-cutting.** Placed under `Core/ECS/Components/` so future modules (combat, AI, dialogue) can query mob names without a domain dependency on `Core/Modules/Mobs/`. Mirrors `ItemDataComponent`.
- **`BlueprintComponent` is retained on live mob entities.** Unlike items (which clear `BlueprintComponent` on pickup — INV-21), mobs have no pickup transition. The INV-21 obligation (clear `BlueprintComponent` from the dead entity before re-spawning) is tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).
- **Event publication ordering.** `MkMobCommand` and `SetMobCommand` write YAML first, save entity second, publish event last — matching the crash-safety ordering precedent in `LoginFlow` (entity save before account save). This ensures the template file exists on disk before any crash window opens.
- **Mob display.** `BroadcastSystem.SendRoomDescriptionAsync` queries `MobDataComponent` entities in the room and populates `RoomDescriptionMessage.Mobs`. `TelnetOutputFormatter` renders one line per mob: `"<Name> is here."`. Identical-name mobs are not stacked — stacking is acknowledged debt.
- **`look <mob>` is out of scope.** Inspecting a specific mob by keyword belongs with `kill <mob>` argument resolution in the combat slice.
- **No `InventoryComponent`/`EquipmentComponent` on mobs.** Added when the combat/death slice requires them.

## Extensibility

- **Wandering (AI movement)** — requires `TimeSystem` + AI behavior infrastructure; deferred to a dedicated slice.
- **Faction / aggro** — `MobType` is the seam; guard aggression and creature behavior routing layer on without model change.
- **Loot** — `InventoryComponent` is cross-cutting and ready to attach; the death slice drives when it lands.

## Related

- [`mobs.md`](mobs.md) — holistic feature view, player-facing surfaces, and the combat-target surface.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `MobBuilderSystem` / `MobContentWriter` and `MobDataComponent` catalog rows.
- [`../../roadmap/completed/slice-8-mobs.md`](../../roadmap/completed/slice-8-mobs.md) — as-built record and notable design decisions.
- **Combat** — [`../combat/combat.md`](../combat/combat.md) and [`../combat/combat-system.md`](../combat/combat-system.md) — `TryFindTargetInRoom` uses `MobDataComponent.Name`/`Keywords` for target resolution.
