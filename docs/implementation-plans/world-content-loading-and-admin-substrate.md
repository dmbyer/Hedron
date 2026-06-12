# Use Case: World Content Loading + Admin Substrate

**Status:** implemented
**Actors:** System, Administrator
**Module:** `Core/Modules/World/`, `Core/Modules/Admin/`

> **Note — partially superseded.** The slice-2 `dig <direction> <targetRoomBlueprintId>` (connect-to-existing) was **replaced** by the create-a-room `dig` in [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) (5a). The per-command `IsPrivileged` convention was **replaced** by the structural privilege gate in [`command-framework.md`](command-framework.md) (3). `PersistenceHandler` dirty-marking was **removed** by [`persistence-two-level-model.md`](persistence-two-level-model.md) (5b). The YAML pipeline, `TemplateRegistry`, `IAdminAuthorizer` allowlist, and `reload` remain current.

---

## Description

Replaces the hardcoded three-room MVP world with a YAML-driven content pipeline and lands the in-game admin command framework designers use to author content. On startup the world is built from authored area/room files via `TemplateRegistry`, with `PersistenceSystem` hydration winning over blueprint defaults (blueprint-seeds-world model). At runtime, privileged sessions can `spawn` templated entities, `teleport`, author exits (`dig`), and `reload` the content directory without restart. Resolves Ticket B (admin tooling — telnet first, web/desktop UI deferred). This is an **infrastructure-plus-tooling slice**: its value is making every subsequent content-bearing slice authorable.

---

## Preconditions

- Phase 3 slice 1 merged: `IPersistenceSystem`, `IComponentTypeRegistry`, `IComponentSerializer`, `PersistenceBootstrap`, `WorldLoadedEvent`, `EntityHydratedEvent`.
- Persistence exposes `Persistence:DataDirectory` and `Persistence:FlushIntervalSeconds` via `IConfiguration`.
- `RoomComponent`, `LocationComponent`, `Direction`, `ICommand`/`CommandDispatcher` continue to exist from MVP.

---

## Postconditions

- The hardcoded `WorldBootstrap.Initialize` body is removed (replaced by the empty-directory void fallback).
- On startup, every authored area/room under the content directory is spawned via `TemplateRegistry` into the live `EntityService`; `WorldConfiguration.StartingRoomEntityId` resolves to a designer-named blueprint id.
- A persisted entity whose blueprint id matches an authored template wins on conflict — its `[Persistent]` components hydrate first, then the reseed step skips it. Blueprint-only entities spawn fresh each boot.
- Authored templates are addressable by stable string ids (`room.crossroads`, `area.starter_road`) and respawnable after startup.
- `spawn`, `teleport`/`tp`, `dig`, `reload` are usable from a privileged session and produce visible output. Non-privileged sessions are rejected with a clear error.
- `reload` re-reads the content directory and re-registers templates without restart; existing live entities are not destroyed or mutated — only missing templates are seeded.
- Every admin action emits a past-tense event (notification, audit, persistence can react).
- No regressions to `look`, movement, `say`.

---

## Main Flow

1. **Startup — content discovery.** `WorldContentLoader` (an `IHostedService` starting *after* `PersistenceBootstrap`) scans `World:ContentDirectory` (default `data/content/`), deserializes each `*.yaml` into a `RoomTemplate`/`AreaTemplate` via `IContentSerializer` (YAML), and registers them with `ITemplateRegistry`. No live entities yet.
2. **Startup — blueprint-seeded spawn.** For each registered template with no hydrated counterpart, `templateRegistry.Spawn(blueprintId)` creates the entity and attaches its components plus a `BlueprintComponent` recording the source id. Persisted entities win and are skipped.
3. **Startup — exit linking.** A second pass resolves each room's `Direction → blueprintId` exits into live entity ids, populating `RoomComponent.Exits` (after spawn, since exits reference blueprints that may not exist yet at spawn time).
4. **Startup — starting room.** `WorldConfiguration.StartingRoomEntityId` resolves `World:StartingRoomBlueprintId` through the registry; unresolvable → fail fast with a logged error.
5. **Runtime — privilege check.** (As shipped, the structural gate from slice 3 enforces `AdminRequirement` at the dispatcher before the command body runs.)
6. **Runtime — `spawn <blueprintId>`.** Resolves + spawns a live entity, places it in the invoker's room if the archetype warrants, publishes `EntitySpawnedByAdminEvent`, confirms to the invoker.
7. **Runtime — `teleport <target>`.** Resolves a room blueprint id or player name to a room entity id and updates the invoker's `LocationComponent.RoomEntityId`. Publishes `PlayerTeleportedByAdminEvent` (consumed by `PlayerMovedHandler` for broadcast + look).
8. **Runtime — `reload`.** `WorldContentLoader.ReloadAsync()` re-scans, replaces the registry's template set, and seeds only templates with no live counterpart. Existing live entities are not mutated. Publishes `ContentReloadedEvent`.
9. **Audit + notification.** `AdminAuditHandler` logs one structured line per admin action; `PlayerMovedHandler` gives teleports normal arrival/departure flavour.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `EntitySpawnedByAdminEvent(uint AdminEntityId, uint SpawnedEntityId, string BlueprintId, uint RoomEntityId)` | `SpawnCommand` | Per `spawn` | Notify witnesses; audit. |
| `PlayerTeleportedByAdminEvent(uint AdminEntityId, uint TargetEntityId, uint FromRoomEntityId, uint ToRoomEntityId)` | `TeleportCommand` | Per `teleport` | Departure/arrival broadcast + look via `PlayerMovedHandler`; audit. |
| `RoomExitAuthoredByAdminEvent(uint AdminEntityId, uint RoomEntityId, Direction Direction, uint TargetRoomEntityId, bool BidirectionalLinkCreated)` | slice-2 `DigCommand` | Per `dig` | *(slice-2 dig only; the 5a `dig` publishes `RoomCreatedByAdminEvent` instead.)* |
| `ContentReloadedEvent(int TemplatesLoaded, int TemplatesUnchanged, int TemplatesRemoved)` | `ReloadCommand` | Per `reload` | Logging; cache invalidation. |
| `WorldLoadedEvent` (existing) | `PersistenceBootstrap` | Once at startup | Reused — the content spawn pass runs after it so conflict resolution sees the hydrated world. |

**Ordering:** `PersistenceBootstrap` → `WorldContentBootstrap` → `TelnetServer` (registration order in `Server/Program.cs`). The spawn pass uses silent `AddComponent` (no per-entity events), matching the hydration contract.

---

## Design Notes

- **Blueprint-seeds-world conflict model.** Hydration runs first; persisted entities win. The blueprint pass spawns only what wasn't restored. (Deferred from slice 1.)
- **`BlueprintComponent`** (cross-cutting, `[Persistent]`) stores `BlueprintId` on every templated entity, enabling skip-on-conflict reseed and distinguishing authored vs ad-hoc entities.
- **`reload` is additive only** — refreshes the registry and seeds entities never spawned; never mutates or destroys live entities. Documented in `reload`'s help text so admins don't expect description hot-reload.
- **Empty content directory fallback.** Missing/empty `World:ContentDirectory` → warn and seed a single hardcoded `room.void` (no exits); host stays up so first-run bootstrap succeeds and an admin can `dig` outward.
- **Layered admin authorization — settings is the floor.** `AdminAuthorizer` reads `Admin:PrivilegedNames` (string array) from `IConfiguration`; anyone listed is always admin (recoverable by editing config). A future persisted `AdminPrivilegeComponent` layer adds elevation — see [`admin-privilege-elevation.md`](admin-privilege-elevation.md). The interface is stable across that addition.
- **Two serializers coexist.** `System.Text.Json` (slice 1) for component snapshots; `YamlDotNet` (this slice) for content authoring. They do not share code paths.
- **YAML content file shape** (under `data/content/`, all `*.yaml`; the durable authoring reference):
  - **Area** (`areas/<area_id>.yaml`): `id`, `name`, `description`, `respawnRate`, `pvp`, room-id list.
  - **Room** (`rooms/<room_id>.yaml`): `id` (e.g. `room.crossroads`), `name`, `description`, `exits` (`east: room.east_end`), `areaId`, optional `isSafe`/`lightLevel`. A reserved `schemaVersion: 1` key is logged-on-mismatch but not enforced.
- **Config keys** (operational, Category 1; see [`../architecture/05-configuration.md`](../architecture/05-configuration.md)): `World:ContentDirectory` (`data/content/`), `World:StartingRoomBlueprintId` (`room.crossroads`), `Admin:PrivilegedNames` (`[]`). The `data/` tree is `.gitignore`d (local runtime + per-developer content); a committed seed directory is deferred.
- **`dig` write-back deferred** *(slice-2 dig)* — the in-memory template is updated so a same-session `reload` won't undo it, but the source `.yaml` is not rewritten; durability comes from persistence. (The 5a `dig` supersedes this command.)
- **No item/mob templates yet** — `IEntityTemplate` implementations are `RoomTemplate`/`AreaTemplate` only; mob/item templates reuse the same `ITemplateRegistry` in their slices.

---

## Related

- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; hydration-before-spawn ordering, `WorldLoadedEvent`, the `[Persistent]` mechanism `BlueprintComponent` plugs into.
- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — slice 5a; replaces `dig` with runtime room creation.
- [`command-framework.md`](command-framework.md) — slice 3; replaces this slice's `IsPrivileged` convention with a structural gate.
- [`admin-privilege-elevation.md`](admin-privilege-elevation.md) — deferred; the persisted `AdminPrivilegeComponent` layer.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
