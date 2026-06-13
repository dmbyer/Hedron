# World

> Rooms, areas, movement, mob/item spawning, and the game clock. The structural substrate everything else runs on. **Status:** live (slices 2, 5a, 9-b, persistence-reform, area-model).

## What it is

The world is the physical and temporal fabric of the game. Rooms are the traversable nodes; areas group rooms into themed regions. Players walk from room to room by typing a direction. Mobs and items that belong to the world re-appear over time after they are removed. The heartbeat is the shared clock that drives all periodic logic — combat rounds, effect ticks, mob respawns.

From a player's seat: the world is the description you see when you `look`, the exits you walk through, and the mobs and items that populate each room. All world content is authored as YAML files; a `reload` refreshes it without restart.

## How it works

The feature composes four cooperating subsystems:

- **`WorldContentLoader`** — reads YAML content files, registers templates with `ITemplateRegistry`, and fresh-spawns room/area/mob/item entities on every startup and `reload`. It enforces startup ordering so persistence hydrates before blueprints are seeded. The full pipeline is the [world-content design doc](world-content.md).
- **`IMovementSystem`** — pure domain system that validates a direction, resolves the exit to a target room entity, and updates `LocationComponent`. It returns `MoveResult` and never touches the event bus. `MoveCommand` is the Initiator that calls it and publishes `PlayerMovedEvent`. The full model is the [movement-system design doc](movement-system.md).
- **`SpawnSystem`** — tracks spawn-slot occupancy for world-content mobs and items, listens for death/pickup events to mark slots vacant, and re-spawns from templates on the heartbeat. It never publishes events. The full model is the [spawn-system design doc](spawn-system.md).
- **`HeartbeatBackgroundService`** — the shared game clock. Fires a `PeriodicTimer` and publishes `HeartbeatTickEvent` on each tick. No game logic lives in the service; all consumers subscribe independently. The full model is the [time-system design doc](time-system.md).

The room-to-area relationship uses a **bidirectional via component field + scan** model: `RoomComponent.AreaEntityId` is the single-direction link set at startup by `WorldContentLoader.LinkRoomAreas`; the reverse (area → rooms) is an on-demand scan inside `IAreaSystem`. The full area model is the [area-model design doc](area-model.md).

## Systems

| System | Role |
|---|---|
| [`world-content.md`](world-content.md) | YAML content pipeline, startup spawn phases, template registry, reload mechanics |
| [`area-model.md`](area-model.md) | Bidirectional room-area membership, area queries, aspect affinities on areas |
| [`movement-system.md`](movement-system.md) | Direction validation, exit resolution, `LocationComponent` mutation |
| [`spawn-system.md`](spawn-system.md) | Spawn-slot tracking, death/pickup vacancy, heartbeat-driven respawn |
| [`time-system.md`](time-system.md) | Heartbeat service: tick loop, startup ordering, `HeartbeatTickEvent` |

## Surfaces

- **Commands** — `north`/`south`/`east`/`west`/`up`/`down` (movement), `look`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `PlayerMovedEvent` (thin, past-tense). See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Components** — `RoomComponent`, `AreaComponent`, `LocationComponent`, `SpawnConfigComponent`, `BlueprintComponent`. See [`../../reference/components.md`](../../reference/components.md).
- **Admin commands** — `spawn <blueprintId>`, `teleport <target>`, `reload`, `dig <direction> [name]`, `set <property> <value>`, `area [blueprintId]`, `setarea <roomBlueprintId> <areaBlueprintId>`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Config keys** — `World:ContentDirectory` (default `data/content/`), `World:StartingRoomBlueprintId` (default `room.crossroads`), `Admin:PrivilegedNames`, `Heartbeat:IntervalMs` (default 2000). See [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md).

## Flows

- [Server startup (flow-01)](../../architecture/flows/flow-01-server-startup.md) — startup ordering: persistence hydrate → world content spawn → telnet → heartbeat. Cross-cutting infra; world links it.
- [Content reload (flow-05)](../../architecture/flows/flow-05-content-reload.md) — `reload` re-scans content and seeds missing entities. Cross-cutting infra; world links it.
- [Heartbeat tick (flow-16)](../../architecture/flows/flow-16-heartbeat-tick.md) — the tick loop that drives combat, effects, spawn, and regen. Cross-cutting infra; world links it.
- [Admin room creation (flow-08)](../../architecture/flows/flow-08-admin-room-creation.md) — `dig` + `set` runtime room authoring.
- [Admin area creation (flow-27)](../../architecture/flows/flow-27-admin-area-creation.md) — `mkarea` runtime area authoring.

## Related

- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-12 (one world model), INV-21 (blueprint/instance separation), INV-23 (world content never `PersistentEntity`).
- [`../../roadmap/completed/slice-2-world-content-and-admin-substrate.md`](../../roadmap/completed/slice-2-world-content-and-admin-substrate.md) · [`../../roadmap/completed/slice-5a-bare-bones-content-spawning.md`](../../roadmap/completed/slice-5a-bare-bones-content-spawning.md) · [`../../roadmap/completed/slice-9b-time-system.md`](../../roadmap/completed/slice-9b-time-system.md) · [`../../roadmap/completed/area-model.md`](../../roadmap/completed/area-model.md) — as-built history and design decisions.
- **Mobs** (not yet migrated) — mob entities are spawned by world content templates; `SpawnSystem` tracks their slots. See [`../../reference/systems.md`](../../reference/systems.md) for the `MobBuilderSystem` row.
- **Items** — [`../items/items.md`](../items/items.md) — item entities are placed in rooms by `WorldContentLoader.PlaceItemsInRooms`; `SpawnSystem` tracks pickup-triggered vacancy.
- **Effects / Combat** — [`../effects/effects.md`](../effects/effects.md) · [`../combat/combat.md`](../combat/combat.md) — both subscribe to `HeartbeatTickEvent` as downstream consumers of the time system.
