# World Content System

> The YAML-driven pipeline that scans content files, registers templates, seeds the live world on startup, and supports whole-or-nothing reload. **Authoring checkpoints:** slice 2 (foundation); persistence-reform Stage B (migration); spawn-authoring (Phase 5 slice 2 — staged fail-fast read, rooms/areas-only spawn). Living document.

## What it is / does

`WorldContentLoader` is the **domain system** that owns the content pipeline: it reads authored YAML files under `World:ContentDirectory`, deserializes each into a typed `IEntityTemplate` via `IContentSerializer` (a kind-dispatcher), registers templates in the cross-cutting `ITemplateRegistry`, and fresh-spawns the **areas and rooms** that have no live counterpart. Mobs and found items are placed only by room spawn rules, which [`SpawnSystem`](spawn-system.md) fills on `WorldContentReadyEvent`. At startup it runs inside `WorldContentBootstrap` (an `IHostedService`) to enforce ordering after `PersistenceBootstrap`. On `reload` it runs the same phases as a **full rebuild** — tearing down and re-spawning all world content while preserving persistent (player-owned) entities.

The world model is blueprint-seeds-world: persisted entities (those carrying `PersistentEntity`) are hydrated by `PersistenceBootstrap` first and never count as world content (the live-blueprint map excludes them).

## How it works

### Startup phases

`LoadAndSpawnAsync` runs these phases in order:

1. **`ReadCorpusAsync` (staged, fail-fast)** — scans every kind subdirectory and deserializes each `*.yaml` file by kind into a **staging list** — nothing is registered yet. A `ContentShapeException` (a legacy or malformed spawn shape — see [Converting a pre-slice-2 world](#converting-a-pre-slice-2-world)) is collected, prefixed `<kind>/<file>: `. If any were collected, `ContentLoadException` is thrown listing **every** offending file, so one boot attempt is the operator's whole conversion worklist. Other per-file failures (e.g. malformed YAML) keep the log-and-skip posture.
2. **Register** — the staged templates are registered in `ITemplateRegistry`.
3. **`SpawnMissingEntities`** — for each registered **area and room** template with no live entity carrying that blueprint, calls `ITemplateRegistry.Spawn(blueprintId)`. Mob and item templates are never spawned here (no "limbo" entities). No per-entity events are published.
4. **`LinkRoomExits`** — a second pass resolves each room's `Direction → blueprintId` exit map into live entity IDs, populating `RoomComponent.Exits`. Done after spawn because exits reference blueprints that may not exist yet at spawn time.
5. **`LinkRoomAreas`** — sweeps all room entities, resolves each room's `RoomTemplate.AreaId` to a live area entity ID via the live blueprint map, and sets `RoomComponent.AreaEntityId`. Unresolvable area refs log a warning and leave `AreaEntityId = 0`.
6. **`ResolveStartingRoom`** — reads the optional [`world.yaml` manifest](#world-manifest--worldyaml) and resolves the starting room over the live **room** set (manifest → `World:StartingRoomBlueprintId` → `room.void` → ordinal-least room id), publishing one immutable `ResolvedStartingRoom` snapshot to `WorldConfiguration.StartingRoom`. Never fails startup: each fallback logs a warning; no rooms at all logs an error and publishes `Source = Unresolved` (entity id 0).

**Fail-fast boot.** `WorldContentBootstrap` logs the `ContentLoadException` report critical and lets it propagate, so the host does not start — on **both** hosts (Server and the Blazor web host), one posture. `WorldContentReadyEvent` is published only after a clean load; `SpawnSystem` (State) then fills every rule slot ahead of shop seeding (Domain).

`ReloadAsync` is a **full rebuild**, not additive, and **whole-or-nothing**: it first stages and validates the whole corpus (phase 1) — on a shape error it throws before touching anything, so the live world, the registry and the spawn tracker stay unchanged, and `reload` writes the per-file error list and publishes neither `WorldContentReadyEvent` nor `ContentReloadedEvent` (the web host's Apply shows the same message). On a clean read it tears down every world-content entity (`DestroyWorldContent` — anything with a `BlueprintComponent` and no `PersistentEntity`), then swaps in the staged templates and runs the same spawn/link phases to re-spawn the world from scratch, then re-reads `world.yaml` and re-resolves the starting room. Persistent entities (players, player-owned items/containers) are preserved; the `reload` command re-publishes `WorldContentReadyEvent` afterward so shops re-seed, spawn slots rebuild, and players' rooms are re-resolved. This resets runtime instance state (picked-up items respawn, shops refill) and applies edits to existing content. See [Flow 5](../../architecture/flows/flow-05-content-reload.md).

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
| `room` | `rooms/<id>.yaml` | `id`, `name`, `description`, `exits` (`east: room.east_end`), `areaId`, `spawnRules` (see [Spawn rules](#spawn-rules)), `x`/`y`/`z` (optional `int`, authoring-side grid coordinates — see below) |
| `area` | `areas/<id>.yaml` | `id`, `name`, `description`, `respawnRate`, `pvp`, room-id list, `aspectAffinities` |
| `item` | `items/<id>.yaml` | `id`, `name`, `description`, `keywords`, `type`, … — **no spawn location** (placed by room rules) |
| `mob` | `mobs/<id>.yaml` | `id`, `name`, `description`, `keywords`, `type`, attributes, pools, … — **no spawn location** (placed by room rules) |
| world manifest | `world.yaml` (content root, optional) | `schemaVersion`, `startingRoomId` — see [World manifest](#world-manifest--worldyaml) |

A reserved `schemaVersion: 1` key is logged on mismatch but not enforced.

### Spawn rules

A room's `spawnRules` is the **only** place a world mob or found item is placed:

```yaml
spawnRules:
  - id: mob.b1.rat              # rule identity; optional (default: blueprintId, truncated to 60 chars); required when two rules share a blueprint; always written
    blueprintId: mob.b1.rat     # a mob or item template
    count: 3                    # population; default 1; 1..20
    respawnDelaySeconds: 120    # per-slot cooldown; default 300; >= 0
```

A blank blueprint, malformed or duplicate id (including two id-less rules on one blueprint), out-of-range count or negative delay is a load error (`SpawnRuleValidation`); a rule naming an unknown or non-mob/item template is a warning and its slot stays empty. Runtime behavior: [`spawn-system.md`](spawn-system.md).

### Converting a pre-slice-2 world

Spawn-authoring was a **hard break**: there is no translation layer. These shapes are fail-fast load errors — boot aborts on both hosts, `reload`/Apply refuse as a whole, and the catalog editors can't load the file. Every message names the file, the blueprint and what to write instead; one boot attempt lists every file to convert.

| Old shape | Hand conversion |
|---|---|
| spawn rule with `minCount` / `maxCount` | delete both keys; add `count:` (usually the old `maxCount`), and an explicit `id:` if two rules in the room share a blueprint |
| mob `spawnRoomBlueprintId: room.X` (non-blank) | delete the key; add `- { blueprintId: <mob>, count: 1, respawnDelaySeconds: 300 }` to `rooms/room.X.yaml`'s `spawnRules` |
| item `spawnRoomId: room.X` (non-blank) | same, in the item's room |
| blank `spawnRoomBlueprintId: ''` / `spawnRoomId: ''` | nothing — ignored, and dropped on the next write |

Before slice 2, a mob/item **without** a spawn room was still spawned once as an unplaced "limbo" entity, and a spawn-room mob without a matching rule never respawned; after conversion every world mob/item is placed by a rule and respawns. No committed content needed conversion.

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
- **`reload` is a full rebuild.** Tears down all world content and re-spawns from YAML (preserving players), so edits to existing rooms/mobs/items take effect and runtime instance state resets — or is refused as a whole on a shape error, with nothing touched. Documented in `reload`'s help text.
- **Shape errors fail fast; everything else is log-and-skip.** The fail-fast class is opt-in by exception type (`ContentShapeException`), so a stray malformed file doesn't take a server down while a legacy spawn shape — which would otherwise silently change world population — does. Staging the read before teardown is what makes reload refusal free (no half-built world).
- **Reload concurrency (INV-31).** The staging set is a local of one `ReloadAsync` call (confined); the registry and world are mutated only after a clean read, on the caller's thread (session for `reload`, circuit for Apply).
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
