# World Content System

> The YAML-driven pipeline that scans content files, registers templates, seeds the live world on startup, and supports additive hot-reload. **Authoring checkpoint:** slice 2 (foundation); persistence-reform Stage B (migration). Living document.

## What it is / does

`WorldContentLoader` is the **domain system** that owns the content pipeline: it reads authored YAML files under `World:ContentDirectory`, deserializes each into a typed `IEntityTemplate` via `IContentSerializer` (a kind-dispatcher), registers templates in the cross-cutting `ITemplateRegistry`, and fresh-spawns entities for any template without a live counterpart. At startup it runs inside `WorldContentBootstrap` (an `IHostedService`) to enforce ordering after `PersistenceBootstrap`. On `reload` it runs the same phases as a **full rebuild** — tearing down and re-spawning all world content while preserving persistent (player-owned) entities.

The world model is blueprint-seeds-world: persisted entities (those carrying `PersistentEntity`) are hydrated by `PersistenceBootstrap` first; the spawn pass sees them and skips their blueprint slots.

## How it works

### Startup phases

`LoadAndSpawnAsync` runs these phases in order:

1. **`LoadTemplates`** — scans `*.yaml` files, dispatches each by `kind:` key to the registered `ITemplateDeserializer`, registers the result in `ITemplateRegistry`.
2. **`SpawnMissingEntities`** — for each registered template with no live entity carrying that `BlueprintComponent.BlueprintId`, calls `ITemplateRegistry.Spawn(blueprintId)`. Persisted entities already hydrated are skipped. No per-entity events are published — matches the persistence hydration contract.
3. **`LinkRoomExits`** — a second pass resolves each room's `Direction → blueprintId` exit map into live entity IDs, populating `RoomComponent.Exits`. Done after spawn because exits reference blueprints that may not exist yet at spawn time.
4. **`PlaceItemsInRooms`** — sets `LocationComponent` on each world-content item, placing it in its authored spawn room. Sets both `RoomEntityId` (runtime) and `RoomBlueprintId` (durable string ref).
5. **`PlaceMobsInRooms`** — same for mob entities.
6. **`LinkRoomAreas`** — sweeps all room entities, resolves each room's `RoomTemplate.AreaId` to a live area entity ID via the live blueprint map, and sets `RoomComponent.AreaEntityId`. Unresolvable area refs log a warning and leave `AreaEntityId = 0`.
7. **`ResolveStartingRoom`** — resolves `World:StartingRoomBlueprintId` to a live entity ID. Unresolvable → fail fast with a logged error.

`ReloadAsync` is a **full rebuild**, not additive: it first tears down every world-content entity (`DestroyWorldContent` — anything with a `BlueprintComponent` and no `PersistentEntity`), then refreshes the template registry and runs the same spawn/place/link phases to re-spawn the world from scratch. Persistent entities (players, player-owned items/containers) are preserved; the `reload` command re-publishes `WorldContentReadyEvent` afterward so shops re-seed, spawn slots rebuild, and players' rooms are re-resolved. This resets runtime instance state (picked-up items respawn, shops refill) and applies edits to existing content. See [Flow 5](../../architecture/flows/flow-05-content-reload.md).

### Content file shape

All files are `*.yaml` under `data/content/` (the path is `World:ContentDirectory`):

| Kind | Path | Key fields |
|---|---|---|
| `room` | `rooms/<id>.yaml` | `id`, `name`, `description`, `exits` (`east: room.east_end`), `areaId`, `isSafe`, `lightLevel`, `x`/`y`/`z` (optional `int`, authoring-side grid coordinates — see below) |
| `area` | `areas/<id>.yaml` | `id`, `name`, `description`, `respawnRate`, `pvp`, room-id list, `aspectAffinities` |
| `item` | `items/<id>.yaml` | `id`, `name`, `description`, `keywords`, `type`, `spawnRoomId` |
| `mob` | `mobs/<id>.yaml` | `id`, `name`, `description`, `keywords`, `type`, `spawnRoomBlueprintId`, attributes, pools |

A reserved `schemaVersion: 1` key is logged on mismatch but not enforced.

`x`/`y`/`z` (nullable `int`, omitted when unset) are optional authoring-side grid coordinates — the authored half of the backlogged runtime coordinate system (see [`../../roadmap/backlog.md`](../../roadmap/backlog.md)). East = X+1, North = Y+1, Up = Z+1. They are advisory: `RoomTemplate.Apply` attaches no coordinate-bearing runtime component this slice, and an exit may target a non-adjacent cell or another area without being an error. Authored and visualized by the visual grid area editor (`/area/{id}/grid`) — see [`../admin-authoring/content-tooling.md`](../admin-authoring/content-tooling.md).

### Template registry

`ITemplateRegistry` is a **cross-cutting core system** (`Core/Systems/`) that all content-bearing modules (world, mobs, items) register into. `Spawn(blueprintId)` allocates an entity, attaches `BlueprintComponent`, then invokes `IEntityTemplate.Apply` to add archetype-specific components. No events are published — callers (admin commands, content loader) publish their own past-tense events.

`YamlContentSerializer` is the kind-dispatcher that routes YAML body text to the right `ITemplateDeserializer`. Each module registers its own deserializer via DI; the serializer has no module knowledge.

### Component migration

`WorldContentLoader.MigrateEntityComponentsAsync` uses `IArchetypeRegistry.MissingRequired(entityId, archetype)` to find components an entity is missing (relative to its expected archetype) and adds them without ever removing extras — the data-safety guarantee. This runs at startup and `reload` to keep hydrated entities current with template changes.

### Empty content directory

Missing/empty `World:ContentDirectory` → warn and seed a single hardcoded `room.void` (no exits). The host stays up so an admin can `dig` outward.

## Interface

- [`IWorldContentLoader.cs`](../../../Core/Modules/World/Systems/IWorldContentLoader.cs) — `LoadAndSpawnAsync` / `ReloadAsync(→ ContentReloadResult)`. Pure: returns results; never touches the event bus (INV-5); callers (`ReloadCommand`) publish `ContentReloadedEvent`.
- [`ITemplateRegistry.cs`](../../../Core/Systems/TemplateRegistry.cs) — `Register` / `TryGet` / `Spawn` / `AllBlueprintIds` / `Clear`.
- [`IContentSerializer.cs`](../../../Core/Systems/YamlContentSerializer.cs) — `Deserialize(kind, fileBody)` / `FormatExtension`.

## Considerations

- **Two serializers coexist by design.** `System.Text.Json` for component persistence snapshots; `YamlDotNet` for content authoring. Different audiences, different change cadence, no shared code path.
- **`reload` is a full rebuild.** Tears down all world content and re-spawns from YAML (preserving players), so edits to existing rooms/mobs/items take effect and runtime instance state resets. Documented in `reload`'s help text.
- **Silent startup spawn.** No per-entity events during `LoadAndSpawnAsync` — consistent with the persistence hydration contract.
- **Config keys are Category 1 (operational).** `World:ContentDirectory` and `World:StartingRoomBlueprintId` are operator-controlled. See [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md).

## Extensibility

- **New content kinds** register an `ITemplateDeserializer` via DI with no `YamlContentSerializer` change (open/closed property).
- **Content-tooling track** (`IContentDefinitionCatalog`, `IContentGenerationSystem`, offline Blazor editor) builds on the same content writers and `IContentValidator` without touching the runtime pipeline. See [`../../reference/systems.md`](../../reference/systems.md) for those rows.

## Related

- [`world.md`](world.md) — holistic feature view.
- [Server startup (flow-01)](../../architecture/flows/flow-01-server-startup.md) — startup ordering and the spawn phase sequence.
- [Content reload (flow-05)](../../architecture/flows/flow-05-content-reload.md) — the `reload` command path.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `WorldContentLoader`, `BlueprintComponent`, `AreaComponent` catalog rows.
- [`../../roadmap/completed/slice-2-world-content-and-admin-substrate.md`](../../roadmap/completed/slice-2-world-content-and-admin-substrate.md) — as-built history and design decisions.
