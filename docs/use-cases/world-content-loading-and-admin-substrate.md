# Use Case: World Content Loading + Admin Substrate

**Status:** implemented
**Actors:** System, Administrator
**Module:** `Core/Modules/World/`, `Core/Modules/Admin/`

---

## Description

Replaces the hardcoded three-room MVP world with a YAML-driven content pipeline and lands the in-game admin command framework that designers will use to author and iterate on that content. On startup the world is built from authored area/room files via `TemplateRegistry` (with `PersistenceSystem` hydration winning over blueprint defaults per the blueprint-seeds-world model). At runtime, privileged sessions can spawn templated entities (`spawn`), move themselves between rooms (`teleport`), author connections between rooms (`dig`), and reload the content directory without restart (`reload`). This slice resolves Ticket B (admin tooling — telnet first, web/desktop UI deferred) and is the substrate every future content-bearing slice (items, mobs, shops) will build on.

This is an **infrastructure-plus-tooling slice**, not a gameplay slice. It introduces no new player-facing verbs and no combat/inventory/character-progression behaviour. Its value is making every subsequent slice authorable.

---

## Preconditions

- Phase 3 slice 1 has merged: `IPersistenceSystem`, `IComponentTypeRegistry`, `IComponentSerializer`, `PersistenceBootstrap`, `WorldLoadedEvent`, `EntityHydratedEvent` are available.
- Persistence already exposes `Persistence:DataDirectory` (default `data/entities/`) and `Persistence:FlushIntervalSeconds` via `IConfiguration`. This slice introduces a sibling key for the content directory and reaffirms both as configurable.
- `RoomComponent` and `LocationComponent` continue to exist from MVP. Their schema may be extended (e.g. a `BlueprintId` field) but no rename or removal in this slice.
- `Direction` enum and `ICommand` / `CommandDispatcher` continue to exist from MVP.

---

## Postconditions

- The hardcoded `WorldBootstrap.Initialize` body is removed (or reduced to a fallback for empty content directories — see Design Notes).
- On host startup, every authored area/room defined under the configured content directory is spawned via `TemplateRegistry` into the live `EntityService`, and `WorldConfiguration.StartingRoomEntityId` resolves to a designer-named blueprint id rather than a hardcoded entity id.
- An entity persisted in `data/entities/` whose blueprint id matches an authored template wins on conflict — its `[Persistent]` components hydrate first, then the blueprint reseed step skips that entity. Blueprint-only entities (never persisted) are spawned fresh each boot.
- Authored templates registered with `TemplateRegistry` are addressable by stable string ids (e.g. `room.crossroads`, `area.starter_road`) and can be respawned at any time after startup.
- `spawn <templateId> [hereOrTarget]`, `teleport <playerOrRoomBlueprintId>`, `dig <direction> <targetRoomBlueprintId>`, and `reload` are usable from a privileged session and produce visible output to the invoker.
- `reload` re-reads the content directory and re-registers templates without restart; existing live entities are not destroyed unless the admin asks for it (out of scope this slice — see Design Notes).
- Every admin command is rejected with a clear error if invoked from a non-privileged session.
- All admin actions emit a past-tense event so handlers (notification, audit logging, persistence dirty-tracking) can react.
- No regressions to existing player-facing commands (`look`, movement, `say`).

---

## Main Flow

1. **Startup — content discovery.** `WorldContentLoader` (an `IHostedService` registered to start *after* `PersistenceBootstrap`) scans the configured content directory (`World:ContentDirectory`, default `data/content/`). It enumerates every area file and room file (`*.yaml`), deserializes each into a `RoomTemplate` / `AreaTemplate` POCO via `IContentSerializer` (YAML-backed), and registers them with `ITemplateRegistry`. No live entities are created yet — only templates are loaded into the registry.

2. **Startup — blueprint-seeded spawn.** `WorldContentLoader` then walks every registered room/area template. For each, it asks `IPersistenceSystem` (via a query helper) whether an entity with that template's blueprint id already exists in the live world (i.e. was hydrated from disk). If yes, it's skipped — the persisted state wins. If no, it calls `templateRegistry.Spawn(blueprintId)` which calls into `EntityService.CreateEntity()` and attaches the components described in the template (`IdentityComponent`, `RoomComponent`, etc.). The new entity is also tagged with a `BlueprintComponent` recording its source blueprint id so future reloads can resolve it.

3. **Startup — exit linking.** Once every room entity exists (either hydrated or freshly spawned), `WorldContentLoader` makes a second pass over the room templates and resolves each exit (`Direction → blueprintId`) into the corresponding live entity id, populating `RoomComponent.Exits`. Linking happens after spawn because exits reference blueprints that may not have been processed yet at spawn time.

4. **Startup — starting room resolution.** `WorldConfiguration.StartingRoomEntityId` is set by resolving a configured starting blueprint id (`World:StartingRoomBlueprintId`, e.g. `"room.crossroads"`) through the registry. If unresolvable, startup fails fast with a logged error.

5. **Runtime — privileged session check.** Each admin `ICommand` calls `IAdminAuthorizer.IsPrivileged(session)` as the first line of its `Execute` body. Non-privileged sessions receive a single rejection line and the command body does not run. Privileged sessions proceed. `CommandDispatcher` itself stays policy-free — it does not inspect command names or route through the authorizer. (See Design Notes for the rationale.)

6. **Runtime — `spawn <blueprintId>`.** `SpawnCommand` resolves the blueprint via `templateRegistry`, calls `templateRegistry.Spawn(blueprintId)` to create a live entity, then if the spawned archetype warrants placement (item, mob), places it in the invoker's current room via `TransformComponent` / `RoomComponent` containment. Publishes `EntitySpawnedByAdminEvent`. Output: a confirmation line to the invoker.

7. **Runtime — `teleport <target>`.** `TeleportCommand` parses the target as either a room blueprint id (`room.east_end`) or a player display name. If a room, it resolves to that room's entity id and updates the invoker's `LocationComponent.RoomEntityId`. If a player, it resolves to the player's current room and does the same. Publishes `PlayerTeleportedByAdminEvent` (consumed by `PlayerMovedHandler` for the same downstream behaviour as a normal move — broadcast, look-on-arrival).

8. **Runtime — `dig <direction> <targetBlueprintId>`.** `DigCommand` mutates the invoker's current `RoomComponent.Exits` to add an exit in the named direction pointing at the target room's entity id, and (by default) wires the reverse exit on the target room. The corresponding `RoomTemplate` for the invoker's room is also updated *in memory* in the registry so a `reload` round-trip won't lose the change. **The source content YAML on disk is not rewritten in this slice** — durability of the change comes from `PersistenceSystem` saving the live room's `[Persistent]` components on its next timed flush. Publishes `RoomExitAuthoredByAdminEvent`.

9. **Runtime — `reload`.** `ReloadCommand` calls `WorldContentLoader.ReloadAsync()` which re-scans the content directory, replaces the registry's template set, and (per the conflict model) re-attaches blueprint-side defaults to any entity that hasn't drifted from its blueprint via persistence. **Existing live entities are not mutated.** Only missing entities (templates that have no live counterpart) are seeded. Player sessions are not disrupted. Publishes `ContentReloadedEvent`.

10. **Audit and notification.** `AdminAuditHandler` subscribes to all admin events at low priority and writes a single structured log line per action via `ILogger<AdminAuditHandler>` (no separate audit file in this slice). `NotificationHandler` (existing) subscribes to `PlayerTeleportedByAdminEvent` so witnesses see the teleport's arrival/departure flavour text.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `EntitySpawnedByAdminEvent(uint AdminEntityId, uint SpawnedEntityId, string BlueprintId, uint RoomEntityId)` | `SpawnCommand` | Per `spawn` invocation | Notifies witnesses; triggers persistence dirty-mark on the new entity if it carries `[Persistent]`; audit log. |
| `PlayerTeleportedByAdminEvent(uint AdminEntityId, uint TargetEntityId, uint FromRoomEntityId, uint ToRoomEntityId)` | `TeleportCommand` | Per `teleport` invocation | Drives departure/arrival broadcast and `look`-on-arrival via the existing movement handler; audit log. |
| `RoomExitAuthoredByAdminEvent(uint AdminEntityId, uint RoomEntityId, Direction Direction, uint TargetRoomEntityId, bool BidirectionalLinkCreated)` | `DigCommand` | Per `dig` invocation | Persistence dirty-marks the affected room(s); audit log; future hot-reload-aware tooling can pick this up. |
| `ContentReloadedEvent(int TemplatesLoaded, int TemplatesUnchanged, int TemplatesRemoved)` | `ReloadCommand` (via `WorldContentLoader.ReloadAsync`) | Per `reload` invocation | Logging; future indexes that cache template ids invalidate. |
| `WorldLoadedEvent` (existing — published by `PersistenceBootstrap`) | unchanged | Once at startup | Reused: `WorldContentLoader`'s startup spawn pass runs **after** `WorldLoadedEvent` so persistence-vs-blueprint conflict resolution sees the fully hydrated world. |

### Hydration vs spawn ordering

- `PersistenceBootstrap.StartAsync` runs to completion (all persisted entities hydrated, `WorldLoadedEvent` published) **before** `WorldContentLoader.StartAsync` begins its spawn pass.
- `WorldContentLoader.StartAsync` must complete before `TelnetServer` accepts connections. The `IHostedService` registration order in `Server/Program.cs` enforces this: `PersistenceBootstrap` → `WorldContentLoader` → `TelnetServer`.
- `WorldContentLoader` does **not** publish per-entity events on spawn; the spawn path uses `EntityService.AddComponent` directly (silent attachment), matching the persistence hydration contract.

---

## Systems / Handlers Involved

### ITemplateRegistry (new — core system)

```
ITemplateRegistry
  void Register(string blueprintId, IEntityTemplate template)
  bool TryGet(string blueprintId, out IEntityTemplate template)
  Entity Spawn(string blueprintId)
  Entity Spawn(string blueprintId, IDictionary<string, object>? overrides)
  IReadOnlyCollection<string> AllBlueprintIds()
  void Clear()
```

Lives at `Core/Systems/TemplateRegistry.cs` (cross-cutting). Depends on `EntityService`. `IEntityTemplate` is an internal contract implemented by `RoomTemplate`, `AreaTemplate`, and (in later slices) `MobTemplate`, `ItemTemplate` — each owns its own `Apply(Entity)` translation to components.

### IContentSerializer (new — core utility, YAML-backed)

```
IContentSerializer
  IEntityTemplate Deserialize(string blueprintId, string fileBody)
  string FormatExtension { get; }   // ".yaml"
```

Single implementation in this slice using `YamlDotNet`. Lives at `Core/Systems/ContentSerializer.cs`. Schema validation happens here — malformed files log a clear error and skip that template rather than crashing startup. The YAML deserializer is configured with camelCase property mapping and a `EnumNamingConvention` matching the JSON serializer used by persistence (so `Direction` enum spellings match across both formats).

> **Format-coexistence note.** The persistence layer (slice 1) uses `System.Text.Json` for component snapshots — that does not change. Persistence is a runtime-write format optimized for fidelity round-trips; content authoring is a designer-write format optimized for readability. Both serializers are present in the build.

### IAdminAuthorizer (new — domain system, `Core/Modules/Admin/`)

```
IAdminAuthorizer
  bool IsPrivileged(ISession session)
  bool IsPrivileged(uint playerEntityId)
```

**Layered authorization model — settings is the floor, component is the additive layer.**

1. **Bootstrap layer (this slice):** `AdminAuthorizer` reads `Admin:PrivilegedNames` from `IConfiguration` — a string array of player display names that are *always* admin. This allows first-run bootstrap on a fresh save with no persisted state. Anyone in this list is admin even if they have no `AdminPrivilegeComponent`.
2. **Persisted layer (future slice — see [`admin-privilege-elevation.md`](admin-privilege-elevation.md)):** an `AdminPrivilegeComponent` (`[Persistent]`) attached to a player entity also grants admin rights. Initial admins (from settings) can elevate other players via `grant` / `promote`, and the elevation persists across restarts. **This component does not exist in this slice** — `AdminAuthorizer.IsPrivileged` only consults the settings allowlist for now, but the implementation is structured so the component check can be added later without changing the interface.

The settings list is the floor: removing the component never demotes someone whose name is in `Admin:PrivilegedNames`. This guarantees an operator can always recover admin access by editing config.

Lives at `Core/Modules/Admin/Systems/AdminAuthorizer.cs`.

### IWorldContentLoader (new — domain system + hosted service)

```
IWorldContentLoader
  Task LoadAndSpawnAsync(CancellationToken ct = default)
  Task ReloadAsync(CancellationToken ct = default)
```

Implementation lives at `Core/Modules/World/Systems/WorldContentLoader.cs`. The `IHostedService` wrapper that runs `LoadAndSpawnAsync` at startup lives at `Server/WorldContentBootstrap.cs` (parallel to `PersistenceBootstrap`).

If the content directory is missing or empty at startup, `WorldContentLoader` logs a warning via `ILogger` and seeds a single hardcoded "void" room with no exits so the host stays up and an admin can `dig` their way out. The host does *not* fail-fast on empty content — that would defeat first-run bootstrap.

### AdminAuditHandler (new — handler, cross-cutting)

**Events subscribed:** `EntitySpawnedByAdminEvent`, `PlayerTeleportedByAdminEvent`, `RoomExitAuthoredByAdminEvent`, `ContentReloadedEvent`.
**Priority:** 80 (after gameplay handlers, before persistence at 90).
**Responsibilities:** writes a single structured-log entry per admin action — admin name, command, target, timestamp — via `ILogger<AdminAuditHandler>`. **No dedicated audit file in this slice.** Promotion to a separate audit sink is a future ops concern.

Lives at `Core/Modules/Admin/Handlers/AdminAuditHandler.cs`.

### PlayerMovedHandler (existing — extended)

**New event subscribed:** `PlayerTeleportedByAdminEvent`.
**Responsibility added:** treat an admin teleport as a normal arrival/departure: broadcast departure flavour text to the source room, broadcast arrival flavour text to the destination room, send a `look` to the teleported player. Implementation should funnel both `PlayerMovedEvent` and `PlayerTeleportedByAdminEvent` through the same private helper to avoid drift.

### PersistenceHandler (existing — extended)

**New events subscribed:** `EntitySpawnedByAdminEvent` (mark the new entity dirty if it has any `[Persistent]` component), `RoomExitAuthoredByAdminEvent` (mark both affected rooms dirty when their components carry `[Persistent]`).
**Priority:** 90 (unchanged).

### CommandDispatcher (existing — unchanged)

`CommandDispatcher` is **not** modified by this slice. The privilege check is enforced at the command level: each admin `ICommand.Execute` calls `IAdminAuthorizer.IsPrivileged(session)` as its first line and short-circuits with a rejection line for non-privileged sessions. This keeps the dispatcher policy-free and matches the existing thin-dispatcher style.

---

## Content Tooling Impact

This slice exists primarily to introduce content tooling. Every gameplay slice after this one consumes what lands here.

### Data file shape — YAML

Two file kinds under `data/content/`, all `.yaml`:

- **Area files** (`data/content/areas/<area_id>.yaml`) — author-facing: `id`, `name`, `description`, `respawnRate`, `pvp`, list of room ids in the area.
- **Room files** (`data/content/rooms/<room_id>.yaml`) — author-facing: `id` (e.g. `room.crossroads`), `name`, `description`, `exits` (`east: room.east_end`), `areaId`, optional flags (`isSafe`, `lightLevel`).

A `data/content/README.md` is added describing the schema and where to put new files. Schema versioning is intentionally minimal in this slice — a top-level `schemaVersion: 1` key is reserved but not enforced beyond a log warning on mismatch. The `YamlDotNet` package is added as a Core dependency.

### Admin commands introduced

| Verb | Purpose | Aliases |
|---|---|---|
| `spawn <blueprintId>` | Spawn an authored template into the invoker's current room | none |
| `teleport <roomBlueprintId\|playerName>` | Move the invoker to a target room or player | `tp` |
| `dig <direction> <targetRoomBlueprintId>` | Add an exit from the invoker's current room | none |
| `reload` | Re-scan the content directory and re-register templates (does NOT mutate live entities) | none |

Every admin command returns at minimum a one-line confirmation or error to the invoker. Output verbosity past that is deferred.

**`reload` in-game help text — required wording.** The `help`/usage line for `reload` must explicitly state the limitation so admins do not expect description hot-reload on existing rooms. Required text (or equivalent):

> `reload` — re-scans the content directory and refreshes the template registry. Newly authored templates with no live counterpart are seeded. **Existing live entities are not modified** — descriptions, exits, and components on rooms that already exist will not change. To pick up edits to a live room, restart, or use `dig` for exit changes.

### TemplateRegistry entries — first-run shape

The hardcoded MVP rooms (`room.west_end`, `room.crossroads`, `room.east_end`, `area.starter_road`) are no longer in code. The old `WorldBootstrap.Initialize` is removed entirely; world assembly is now driven by `WorldContentLoader` against the configured content directory.

**No seed content ships in this PR** (per the gitignore decision below — `data/` is local-only, and a committed `seed-content/` directory is deferred to a future slice). On a fresh clone the loader finds an empty content directory, logs a warning, and falls back to a single hardcoded `room.void` so the host comes up successfully. From there a privileged session can `dig` outward — or a developer can hand-author YAML files under `data/content/` to seed authored rooms. The MVP three-room flow can be reproduced by authoring those four YAML files locally; this slice deliberately does not commit them.

---

## Configuration

### Configurable paths — both content and persistence directories

Both the content load location and the persistence save location are operator-configurable Category 1 settings. Both default to a sensible local path under `data/`.

| Config key | Default | Source |
|---|---|---|
| `World:ContentDirectory` | `data/content/` | New in this slice |
| `World:StartingRoomBlueprintId` | `room.crossroads` | New in this slice |
| `Admin:PrivilegedNames` | `[]` (empty array) | New in this slice |
| `Persistence:DataDirectory` | `data/entities/` | **Existing — confirmed configurable in slice 1** (`PersistenceSystem` reads it from `IConfiguration`). Reaffirmed here as an operator-controlled path. |
| `Persistence:FlushIntervalSeconds` | `60` | Existing — slice 1 |

The persistence config key was verified against the slice 1 implementation (`Core/Systems/PersistenceSystem.cs`); no changes needed to the persistence layer to make the path configurable. This slice should not regress that contract.

### Local-only data directories — gitignore

The `data/` tree (both `data/entities/` and `data/content/`) holds local runtime state and per-developer authored content. **It must not be committed.** Add to `.gitignore`:

```
# Hedron runtime + local content
/data/
/Server/data/
```

(Both rooted-at-repo and rooted-at-`Server/` paths are ignored because `dotnet run --project Server` resolves relative paths from the `Server/` working directory by default.)

A separate seed directory (e.g. `seed-content/`) **may** be committed in a future slice to bootstrap a fresh clone, but is out of scope here. For this slice, a fresh clone with no `data/` directory triggers the empty-content fallback (single void room + warning) — that is the intended onboarding path.

### Configuration category mapping

- `World:ContentDirectory`, `World:StartingRoomBlueprintId`, `Persistence:DataDirectory`, `Persistence:FlushIntervalSeconds` — Category 1 (operational).
- `Admin:PrivilegedNames` — Category 1 (operational; bootstrap allowlist is environment-specific).
- Authored content inside `data/content/` — Category 2 (designer data).

See [`../architecture/05-configuration.md`](../architecture/05-configuration.md).

---

## Design Notes

- **Blueprint-seeds-world conflict model.** Hydration runs first; persisted entities win. The blueprint pass spawns only what wasn't restored. This matches the model deferred from slice 1 (`docs/use-cases/persistence-substrate.md` §"Conflict model").
- **`BlueprintComponent`** (new, cross-cutting, `[Persistent]`). Stores `BlueprintId : string` on every entity that originated from a template. Lets `WorldContentLoader` skip-on-conflict during reseed and lets `spawn` distinguish authored vs ad-hoc entities. Adding `[Persistent]` here means every templated entity becomes savable; consumers that don't want persistence on a particular template can omit the component (template author's choice).
- **`dig` write-back deferred.** This slice mutates the in-memory template so a same-session `reload` won't undo the change, but it does **not** rewrite the source `.yaml` file on disk. Durability of admin-authored room shape comes from `PersistenceSystem` saving the live room's `[Persistent]` components on its next timed flush. A follow-up slice (`save-room` or similar source-file round-trip) is tracked in the backlog.
- **`reload` does not destroy or mutate live entities.** The reload pass only refreshes the template registry and reseeds entities that were never spawned. Mutating already-live entities to match new blueprint values is a richer story (does it touch persistence? does it kick players?) and is deferred. Document this limitation in command help.
- **Empty content directory fallback.** On startup, if `World:ContentDirectory` is missing or empty, `WorldContentLoader` warns via `ILogger`, seeds a single hardcoded "void" room (no exits) with blueprint id `room.void`, and continues. The host does not fail-fast — first-run bootstrap on a fresh clone must succeed without manual intervention. From there, a privileged session can `dig` outward.
- **Admin command privilege gate.** `IAdminAuthorizer` is the policy seam. The bootstrap implementation reads `Admin:PrivilegedNames` from `IConfiguration`. The future persisted-component layer (`AdminPrivilegeComponent`) is captured in [`admin-privilege-elevation.md`](admin-privilege-elevation.md) as a deferred slice. The authorizer's interface is stable across that addition.
- **Audit logging.** `AdminAuditHandler` writes through `ILogger<AdminAuditHandler>` with a stable structured-event name (`AdminCommandExecuted`). No separate audit file in this slice; can be promoted later if ops needs.
- **Silent spawn vs notification.** Startup spawn is silent (no events) to match the persistence hydration contract. Runtime `spawn` does publish events because the world is live and other systems need to react.
- **No item or mob templates yet.** This slice's `IEntityTemplate` implementations are `RoomTemplate` and `AreaTemplate` only. Mob and item templates land with their respective slices (4, 5, 6) and reuse the same `ITemplateRegistry`.
- **Module entry-points.** Two new modules: `Core/Modules/World/WorldModule.cs` exposes `AddWorldModule(IServiceCollection, IConfiguration)` (registers `IContentSerializer`, `IWorldContentLoader`, `RoomTemplate`/`AreaTemplate` deserializers, and the existing `LookCommand`); `Core/Modules/Admin/AdminModule.cs` exposes `AddAdminModule(IServiceCollection, IConfiguration)` (registers `IAdminAuthorizer` reading `Admin:PrivilegedNames`, the four admin commands, and `AdminAuditHandler`). `WorldContentBootstrap` is registered as an `IHostedService` from `Server/Program.cs` *between* `PersistenceBootstrap` and `TelnetServer`.
- **`TemplateRegistry` is cross-cutting, not module-scoped.** It lives at `Core/Systems/TemplateRegistry.cs` because every future content module (mobs, items, shops) will register into it. The `World` module owns the loader and the room/area templates; the registry itself is shared infrastructure.
- **Two serializers coexist.** `System.Text.Json` (slice 1) for component snapshots; `YamlDotNet` (this slice) for content authoring. Both are in the build. Persistence and content do not share serializer code paths.

---

## Resolved Decisions

For traceability — these were open questions at the start of slice 2 planning, resolved before implementation.

| # | Question | Decision |
|---|---|---|
| 1 | Content data file format — JSON vs YAML? | **YAML**, via `YamlDotNet`. Persistence stays JSON; both serializers coexist. |
| 2 | Admin privilege gate mechanism? | **Layered.** Bootstrap layer (this slice): `Admin:PrivilegedNames` allowlist in `appsettings.json`. Persisted layer (future): `AdminPrivilegeComponent` — see [`admin-privilege-elevation.md`](admin-privilege-elevation.md). Settings is the floor. |
| 3 | `reload` semantics — destructive or additive? | **Additive only.** Refresh registry, seed missing entities. Live entities are not mutated. |
| 4 | `dig` write-back to source files? | **No file round-trip in this slice.** In-memory template is updated; durability comes from `PersistenceSystem` on the next flush. |
| 5 | Empty / missing content dir at startup? | **Spawn a single void room and warn via `ILogger`.** Host stays up. Audit sink is `ILogger<AdminAuditHandler>` only — no dedicated audit file. |
| 6 | Should `data/` be committed? Are paths configurable? | **No on commit; yes on configurable.** `data/` is `.gitignore`d. Both `World:ContentDirectory` and `Persistence:DataDirectory` are configurable; the persistence key already exists from slice 1. |

---

## Related

- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; provides hydration-before-spawn ordering, the `WorldLoadedEvent` startup signal, the `[Persistent]` mechanism this slice's `BlueprintComponent` plugs into, and the existing `Persistence:DataDirectory` config key.
- [`admin-privilege-elevation.md`](admin-privilege-elevation.md) — deferred / placeholder; future slice that adds the persisted `AdminPrivilegeComponent` layer on top of the bootstrap allowlist.
- `account-character-creation.md` — slice 3 (next); produces the first user-authored persisted entities that will conflict-resolve against blueprints.
- `inventory-get-drop.md` — slice 4; reuses `ITemplateRegistry` for item templates and `spawn` to drop test items into rooms during development.
- `mob-wandering.md` — slice 6; reuses `ITemplateRegistry` for mob templates and consumes `WorldLoadedEvent` to start wander timers after spawn.

For the slice queue and ordering rationale, see [`../roadmap/plan.md`](../roadmap/plan.md).
