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
7. **`ResolveStartingRoom`** — reads the optional [`world.yaml` manifest](#world-manifest--worldyaml) and resolves the starting room over the live **room** set (manifest → `World:StartingRoomBlueprintId` → `room.void` → ordinal-least room id), publishing one immutable `ResolvedStartingRoom` snapshot to `WorldConfiguration.StartingRoom`. Never fails startup: each fallback logs a warning; no rooms at all logs an error and publishes `Source = Unresolved` (entity id 0).

`ReloadAsync` is a **full rebuild**, not additive: it first tears down every world-content entity (`DestroyWorldContent` — anything with a `BlueprintComponent` and no `PersistentEntity`), then refreshes the template registry and runs the same spawn/place/link phases to re-spawn the world from scratch, then re-reads `world.yaml` and re-resolves the starting room. Persistent entities (players, player-owned items/containers) are preserved; the `reload` command re-publishes `WorldContentReadyEvent` afterward so shops re-seed, spawn slots rebuild, and players' rooms are re-resolved. This resets runtime instance state (picked-up items respawn, shops refill) and applies edits to existing content. See [Flow 5](../../architecture/flows/flow-05-content-reload.md).

### Content ownership — the engine ships no game world

Hedron is an open-source **engine**. The repository never ships a playable game world: whoever runs a Hedron server — the maintainer included — authors and owns their own world content, and keeps it outside this repository.

| Location | What it is | Committed? |
|---|---|---|
| `<Paths:DataDirectory>/content` (default `data/content/`) | **The operator's world.** The live game's rooms, areas, mobs, and items, authored with the in-game builder verbs, the Blazor editor, or the `generate` CLI. Version it in your own (private) repository or backup, or point `Paths:DataDirectory` at a folder outside the clone. | **No** — `/data/*` is gitignored |
| `content/dev-world/` | **The generic development world — the primary playable baseline.** Generic, unbranded, lore-free content with functional names, grown as development proceeds. Acceptance tests, CI goldens, playtest automation and smoke-testing a fresh clone all run on it, and a slice demonstrating a mechanic adds to it. Boot it with the committed `DevWorld` environment profile. | Yes |
| `content/profiles/` | Example `generate` profiles (tooling examples, not a world). | Yes |
| `data/sim/scenarios/example-*.yaml` | The two shipped example simulation scenarios (an INV-18 tooling example). | Yes (the only un-ignored `data/` files) |

Rules for contributors and agents:

- **Never commit operator world content.** Don't move, copy, or un-ignore anything under `data/content/`, and don't widen the `/data/*` ignore rule.
- **Keep `content/dev-world/` generic.** Use functional names and descriptions (`Training Yard`, `General Store`, `Band-2 Wolf`) and no lore, setting, or narrative. It is the primary playable baseline and grows as development proceeds; there is no minimum-volume rule. Content a slice needs to demonstrate or test a mechanic goes here.
- **Run against it explicitly.** `DOTNET_ENVIRONMENT=DevWorld` selects the committed profile, which points the host at `content/dev-world/` and gives it its own database (`data/dev-world.db`) so its persisted players don't mix with your own world's. Data locations follow one rule ([`05-configuration.md`](../../architecture/05-configuration.md#data-locations--one-rule)). The in-game builder verbs and the editor write back into whichever directory is configured, so edits made while pointed at the dev world land in the committed fixture. That's intended when you are maintaining the fixture, and a mistake otherwise.

### Content file shape

All files are `*.yaml` under the content directory (`<Paths:DataDirectory>/content`, default `data/content/`; `World:ContentDirectory` overrides it):

| Kind | Path | Key fields |
|---|---|---|
| `room` | `rooms/<id>.yaml` | `id`, `name`, `description`, `exits` (`east: room.east_end`), `areaId`, `spawnRules`, `x`/`y`/`z` (optional `int`, authoring-side grid coordinates — see below) |
| `area` | `areas/<id>.yaml` | `id`, `name`, `description`, `respawnRate`, `pvp`, room-id list, `aspectAffinities` |
| `item` | `items/<id>.yaml` | `id`, `name`, `description`, `keywords`, `type`, `spawnRoomId` |
| `mob` | `mobs/<id>.yaml` | `id`, `name`, `description`, `keywords`, `type`, `spawnRoomBlueprintId`, attributes, pools |
| world manifest | `world.yaml` (content root, optional) | `schemaVersion`, `startingRoomId` — see [World manifest](#world-manifest--worldyaml) |

A reserved `schemaVersion: 1` key is logged on mismatch but not enforced.

`x`/`y`/`z` (nullable `int`, omitted when unset) are optional authoring-side grid coordinates — the authored half of the backlogged runtime coordinate system (see [`../../roadmap/backlog.md`](../../roadmap/backlog.md)). East = X+1, North = Y+1, Up = Z+1. They are advisory: `RoomTemplate.Apply` attaches no coordinate-bearing runtime component this slice, and an exit may target a non-adjacent cell or another area without being an error. Authored and visualized by the visual grid area editor (`/area/{id}/grid`) — see [`../admin-authoring/content-tooling.md`](../admin-authoring/content-tooling.md).

### World manifest — `world.yaml`

A world declares world-level facts in an optional `world.yaml` at the content root (`<World:ContentDirectory>/world.yaml`). It is **not** a template kind: the per-kind loaders scan only their subdirectories, so a root file is never registered or spawned (INV-12), and it is world-content YAML, never SQLite (INV-23).

```yaml
schemaVersion: 1                 # reserved; logged on mismatch, not enforced
startingRoomId: room.hub.square  # optional; where new characters land and fall back to on respawn
```

- **Resolution.** `WorldContentLoader.ResolveStartingRoom()` runs `StartingRoomResolver` (a pure, case-insensitive function) over the live room ids: `startingRoomId` → `World:StartingRoomBlueprintId` → `room.void` → ordinal-least room id. Only `RoomComponent` entities are candidates, so a declaration naming a mob or item never resolves. With no `world.yaml`, resolution is the config → void → any-room chain, unchanged.
- **Malformed file** → logged as an error and treated as an empty manifest (warn-not-error; the host stays up). Unknown keys are ignored on read and not preserved on write; YAML comments are not preserved either.
- **One snapshot.** The result is published as one immutable `ResolvedStartingRoom(EntityId, BlueprintId, Source, DeclaredId)` reference; character creation, the respawn fallback and the hydration reset each read it once, so an entity id is never paired with a blueprint id from another resolution (INV-31).
- **Store.** `IWorldManifestStore` / `WorldManifestStore` (`Core/Modules/World/Systems/`) reads and atomically writes the file, and its `UpdateAsync` is the one read-modify-write path every writer uses (refuse while malformed, default `schemaVersion`); it is stateless and builds its YamlDotNet (de)serializer per call. It is a hand-rolled single-document store, scheduled to migrate onto the shared YAML definition store ([backlog](../../roadmap/backlog.md)).

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
- **Config keys are Category 1 (operational).** `World:ContentDirectory` and `World:StartingRoomBlueprintId` are operator-controlled. The starting room is primarily a Category 2 world fact (`world.yaml`); `World:StartingRoomBlueprintId` is the operator **fallback** behind the manifest. See [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md).

## Extensibility

- **New content kinds** register an `ITemplateDeserializer` via DI with no `YamlContentSerializer` change (open/closed property).
- **Content-tooling track** (`IContentDefinitionCatalog`, `IContentGenerationSystem`, offline Blazor editor) builds on the same content writers and `IContentValidator` without touching the runtime pipeline. See [`../../reference/systems.md`](../../reference/systems.md) for those rows.

## Related

- [`world.md`](world.md) — holistic feature view.
- [Server startup (flow-01)](../../architecture/flows/flow-01-server-startup.md) — startup ordering and the spawn phase sequence.
- [Content reload (flow-05)](../../architecture/flows/flow-05-content-reload.md) — the `reload` command path.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `WorldContentLoader`, `BlueprintComponent`, `AreaComponent` catalog rows.
- [`../../roadmap/completed/slice-2-world-content-and-admin-substrate.md`](../../roadmap/completed/slice-2-world-content-and-admin-substrate.md) — as-built history and design decisions.
