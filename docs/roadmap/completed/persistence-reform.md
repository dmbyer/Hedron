# Persistence reform — Stages A–C (completed)

> Implemented before 2026-06-05 (it predates the start of the retained, squashed git history at `#108`, so exact PRs are not recoverable). Living docs: [`06-persistence.md`](../../architecture/06-persistence.md) · [`spawn-system`](../../features/world/spawn-system.md) · [`item-inventory-system`](../../features/items/item-inventory-system.md). Completed record written retroactively (2026-10) — the plan stayed in `implementation-plans/` marked `planned` after ship and was disintegrated in the housekeeping pass.

## Outcome

Entity persistence moved from file-per-entity JSON snapshots to SQLite, with the persistence lifecycle folded into `EntityService` so no handler or command carries delete responsibility. World content (rooms, areas, mobs, world-spawn items) stopped being persisted at all — YAML is its only source of truth and it is fresh-spawned on every boot — which removed the two-sources-of-truth drift and mob-duplication bugs. Item persistence became context-driven, and room/area-owned spawn slots replaced entity-owned slot bookkeeping.

## Behavior digest

*As-specified snapshot (present truth lives in [`06-persistence.md`](../../architecture/06-persistence.md), the [persistence flush cycle](../../architecture/flows/flow-04-persistence-flush-cycle.md), and the spawn-system doc).*

- **Stage A — SQLite + `EntityService` lifecycle.** One `entity_components(entity_id, type_name, data)` table; `IPersistenceSystem` reduced to `SaveEntityAsync` / `FlushAllAsync` / `FlushDirtyAsync` / `LoadAllAsync`; attaching `PersistentEntity` registers the entity for flush, and `DestroyEntity` deletes its rows; the flush timer does a full sweep of every persistent entity each cycle (no dirty tracking, no session-footprint calculation); `Persistence:DatabasePath` replaced `Persistence:DataDirectory`, with `HEDRON_`-prefixed env overrides for container volumes.
- **Stage B — world content is YAML-only.** `LocationComponent` gained `[Persistent] RoomBlueprintId` as the cross-restart room reference, with `RoomEntityId` runtime-only and re-resolved by `CharacterHydrationHandler` after world content loads (unresolvable → default room for players). Rooms, areas, mobs, and world-spawn items carry no `PersistentEntity`; admin builder verbs stopped calling `SaveEntityAsync`.
- **Stage C — context-driven items + spawn slots.** `ItemContextHandler` adds `PersistentEntity` when an item enters a persistent context (pickup, purchase) and removes it when it leaves (drop, sale). `SpawnConfigComponent` declares a room/area's spawn rules; `SpawnSystem` observes removal events to mark slots vacant and respawns on the heartbeat.

## Shipped pieces

| Surface | Location |
|---|---|
| SQLite-backed `PersistenceSystem` (`Microsoft.Data.Sqlite` in `Core`) | `Core/Systems/PersistenceSystem.cs` |
| `EntityService` persistence set + auto-delete on destroy | `Core/ECS/EntityService.cs` |
| `LocationComponent.RoomBlueprintId` | `Core/ECS/Components/LocationComponent.cs` |
| `SpawnConfigComponent` (`List<SpawnRule>`) | `Core/ECS/Components/SpawnConfigComponent.cs` |
| `ISpawnSystem` / `SpawnSystem`, `ItemContextHandler`, `SpawnModule` | `Core/Modules/Spawn/` |

## Tests shipped

Backfilled by the Phase 4 testing-harness program rather than at ship time (the slice predates the live suite): persistence round-trips (`Hedron.Tests/Persistence/`, `PersistenceTestHarness` over in-memory SQLite) and spawn-slot behavior (`Hedron.Tests/Spawn/SpawnSystemTests.cs`).

## Decisions

- **SQLite over JSON files:** deleting a row is centralized; the JSON model needed every entity-removing mechanic to remember a file delete, a failure mode that scaled with mechanic count.
- **Periodic full flush over property-level dirty tracking:** mutable-class components make mutations invisible to `EntityService`; manual `MarkDirty` had already proven error-prone. A full sweep at MUD entity counts is cheap, and the flush-interval loss window is acceptable for runtime state.
- **Save-on-change restricted to three boundaries:** entity construction, admin boundary saves (paired with an audit event), and session-end force-saves.
- **Rooms are not persistent; the blueprint id is the stable reference.** Persisting room entity ids would bloat the flush pool and break for instanced rooms with no YAML backing.
- **Spawn slots are room/area-owned and event-driven** so no individual mechanic has to "clear a slot". This superseded the earlier rule that clears `BlueprintComponent` on pickup; `BlueprintComponent` is kept as an origin record and INV-21 was updated to match.
- **Dropped items are non-persistent** — standard MUD convention; durable storage means a persistent container, not the floor.
- **Open questions resolved as built:** spawn rules are one component holding a `List<SpawnRule>`; spawn rules are authored as YAML on room templates (`spawnRules`); `Microsoft.Data.Sqlite` lives in `Core` directly.

## Deviations / Follow-ups

- The companion [Docker deployment plan](../../deployment/docker.md) (accepted, to ship "alongside or after Stage A") never shipped beyond the dev/CI `Dockerfile.dev`; parked in [`backlog.md`](../backlog.md).
- Room-deletion cascade for persistent entities located in a deleted room was explicitly deferred to the room-deletion admin slice.
