# Use Case: Mobs — Basic Entity Model and Spawn

**Status:** planned
**Spec review:** passed (2026-05-25)
**Actors:** Player, Administrator, System
**Module:** `Core/Modules/Mobs/` (new); `Core/Modules/World/` (`WorldContentLoader` extended); `Core/ECS/Components/` (`MobDataComponent`)

---

## Description

Introduces mobs as first-class NPC entities in the world. A mob is a non-player character that occupies a room and is visible to players using `look`. This slice delivers the mob entity model, an admin authoring path (`mkmob` / `setmob`), spawn from YAML (`kind: mob`), and mob room presence in `look` output. Wandering (AI movement) is intentionally deferred — that requires a `TimeSystem` and AI behavior infrastructure that belongs in a dedicated slice. The goal here is the mob lifecycle: authored in YAML → spawned into a room → visible to players.

This slice is **deliberately narrow**: no combat, no loot, no dialogue, no wandering, no factions. Those land in later slices. The mob entity model established here (component shape, template, spawn path) is the foundation they build on. The implementation pattern mirrors the items slice (6) as closely as possible.

**Prerequisite:** Slices 1–7 complete.

---

## Preconditions

- Slices 1–7 complete. Reused: `EntityService`, `IEventBus`, `ITemplateRegistry`, `IPersistenceSystem`, `IBroadcastSystem`, `IOutputWriter`, `ICommandDispatcher`, `LocationComponent`, `RoomComponent`, `PersistentEntity`, `BlueprintComponent`, `IAdminAuthorizer`, `AdminRequirement`, `WorldContentLoader` (extended here). `IItemBuilderSystem` and `IItemContentWriter` are the direct precedents for the builder and content-writer interfaces introduced in this slice.
- Every connected player has a valid `LocationComponent`.

---

## Postconditions

- `MobDataComponent` (`Name: string`, `Description: string`, `Keywords: List<string>`, `MobType: MobType`) exists under `Core/ECS/Components/` (cross-cutting) and is `[Persistent]`.
- `MobType` enum exists (`None`, `Vendor`, `Guard`, `Creature`) alongside `MobDataComponent`. It is data-only in this slice — no routing or behavior logic consumes it.
- A `MobTemplate` YAML shape (`kind: mob`) exists under `Core/Modules/Mobs/Templates/`. `MobTemplateDeserializer` deserializes YAML into `MobTemplate`. `WorldContentLoader` loads `kind: mob` files from a `mobs/` subdirectory on startup and reload.
- Admin `mkmob [name]` creates an ad-hoc mob entity in the invoker's current room. A confirmation shows the blueprint id. Blueprint id format: `mob.adhoc.<8-char-base36>`.
- Admin `setmob <blueprintId> <property> <value>` mutates name, description, keywords, or type on the target mob. Blueprint and live entity are kept in sync; `IMobContentWriter.WriteAsync(template)` writes the YAML file to disk after every mutation.
- Mob entities spawned from templates carry: `MobDataComponent`, `LocationComponent { RoomEntityId }`, `BlueprintComponent`, `PersistentEntity`.
- `RoomDescriptionMessage` gains a `Mobs: IReadOnlyList<string>` field. `IBroadcastSystem.SendRoomDescriptionAsync` populates `Mobs` with `MobDataComponent.Name` for every entity in the room that carries `MobDataComponent`. `look` (no arg) displays mob names below items: one line per mob rendered as `"<Name> is here."`.
- `WorldContentLoader.LoadAndSpawnAsync` calls `PlaceMobsInRooms` (new private method, mirrors `PlaceItemsInRooms`) for newly-spawned mob entities only; restored-from-persistence entities are skipped.

---

## Main Flow

### Flow A-1 — Admin `mkmob [name]`

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement`; non-privileged sessions are rejected before `MkMobCommand.ExecuteAsync` runs.
2. **Creation.** `MkMobCommand` calls `IMobBuilderSystem.CreateMob(name, roomEntityId)`. The system generates blueprint id `mob.adhoc.<8-char-base36>`, allocates an entity via `EntityService`, attaches `MobDataComponent { Name, Description="", Keywords=[], MobType=None }` + `BlueprintComponent { BlueprintId }` + `PersistentEntity` + `LocationComponent { RoomEntityId = invoker's current room }`, and registers a minimal `MobTemplate` in `ITemplateRegistry`. Returns `MobCreationResult(mobEntityId, blueprintId, template)`.
3. **YAML write.** Command calls `IMobContentWriter.WriteAsync(template)`. This step runs **before** the event publish and entity save — YAML write first ensures the template file exists on disk before any crash-window opens, matching the crash-safety ordering precedent in `LoginFlow` (character save before account save). Order: YAML write → `SaveEntityAsync` → publish `MobCreatedByAdminEvent`.
4. **Event + save.** Calls `IPersistenceSystem.SaveEntityAsync(mobEntityId)`. Publishes `MobCreatedByAdminEvent(AdminEntityId, MobEntityId, BlueprintId, RoomEntityId)`. Writes a confirmation `PlainMessage` including the blueprint id.
5. `AdminAuditHandler` (priority 80) logs the event.

### Flow A-2 — Admin `setmob <blueprintId> <property> <value>`

1. **Privilege gate.** As A-1.
2. **Resolve mob.** Command looks up `blueprintId` in `ITemplateRegistry`. Not found → "Unknown blueprint id." Then queries `EntityService` for the live entity with matching `BlueprintComponent.BlueprintId`. Not found → "No live mob entity for that blueprint."
3. **Mutation.** For the named property, calls the corresponding `IMobBuilderSystem.Set*` method. `keywords` splits `value` on whitespace. `type` parses as `MobType` enum; invalid value → "Unknown mob type."
4. **YAML write + save.** Command calls `IMobContentWriter.WriteAsync(template)` first, then `IPersistenceSystem.SaveEntityAsync(mobEntityId)`. Order matches A-1: YAML write → entity save → event publish.
5. **Event + confirmation.** Publishes `MobPropertySetByAdminEvent(AdminEntityId, MobEntityId, PropertyName, NewValue)`. Writes a confirmation `PlainMessage`.
6. `AdminAuditHandler` (priority 80) logs the event.

### Flow A-3 — World-content mob spawn (startup / reload)

1. `WorldContentLoader.LoadTemplatesAsync` reads files from `<ContentDirectory>/mobs/` with the configured format extension. For each, calls `IContentSerializer.Deserialize("mob", body)` → `MobTemplateDeserializer.Deserialize` → `MobTemplate`. Registers in `ITemplateRegistry`.
2. `SpawnMissingEntities` (existing): for mob templates with no live entity in the blueprint map, calls `ITemplateRegistry.Spawn(blueprintId)`. `MobTemplate.Apply` attaches `MobDataComponent` from template values. `SpawnMissingEntities` also attaches `PersistentEntity` and adds to `liveBlueprints`. Returns the `newlySpawned` set.
3. **Immediate save.** `WorldContentLoader` calls `IPersistenceSystem.SaveEntityAsync` for every newly-spawned entity (mob and otherwise) to make IDs durable.
4. **`PlaceMobsInRooms(liveBlueprints, newlySpawned)`.** For each `MobTemplate` whose entity is in `newlySpawned` and whose `SpawnRoomBlueprintId` is non-empty: resolves the room blueprint id → room entity id via `liveBlueprints`, attaches `LocationComponent { RoomEntityId }`. Restored-from-persistence entities are skipped. If the spawn room is unknown, logs a warning and the mob is created without a location.

### Flow B-1 — `look` (no arg) with mobs present

1. Player sends `look`. `LookCommand` calls `IBroadcastSystem.SendRoomDescriptionAsync(playerEntityId, roomEntityId)`.
2. `BroadcastSystem.SendRoomDescriptionAsync` (extended here): queries all entities that have `MobDataComponent` + `LocationComponent.RoomEntityId == roomEntityId`. Builds `Mobs` list from `MobDataComponent.Name` for each.
3. Produces `RoomDescriptionMessage` with `Mobs` populated. `TelnetOutputFormatter` renders mob names below the items section, one line per mob: `"<Name> is here."`.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `MobCreatedByAdminEvent` | `MkMobCommand` | `uint AdminEntityId, uint MobEntityId, string BlueprintId, uint RoomEntityId` | Audit log; future editor hooks |
| `MobPropertySetByAdminEvent` | `SetMobCommand` | `uint AdminEntityId, uint MobEntityId, string PropertyName, string NewValue` | Audit log |

---

## Systems / Handlers Involved

| Artifact | Kind | Responsibility |
|---|---|---|
| `IMobBuilderSystem` / `MobBuilderSystem` | Domain system | Entity allocation, component attachment, template registration for ad-hoc mobs (`mkmob`) and property mutation (`setmob`) |
| `IMobContentWriter` / `MobContentWriter` | Domain system (I/O) | Serializes `MobTemplate` to YAML; mirrors `IItemContentWriter` |
| `MobTemplateDeserializer` | Infrastructure | Deserializes `kind: mob` YAML into `MobTemplate`; registered via `IContentSerializer` |
| `WorldContentLoader` | Core system (extended) | Loads mob templates, spawns missing mob entities, places newly-spawned mobs in rooms |
| `AdminAuditHandler` | Handler (existing, extended) | Subscribes to `MobCreatedByAdminEvent` and `MobPropertySetByAdminEvent`; logs structured entries |
| `BroadcastSystem` | Core system (extended) | `SendRoomDescriptionAsync` populates `RoomDescriptionMessage.Mobs` |
| `TelnetOutputFormatter` | Infrastructure (extended) | Renders `RoomDescriptionMessage.Mobs` lines |

---

## Content Tooling Impact

**Data-file shape (`kind: mob`)**

```yaml
kind: mob
blueprintId: mob.forest.wolf
name: A grey wolf
description: A lean wolf with silver-tipped fur.
keywords:
  - wolf
  - grey
type: Creature
spawnRoomBlueprintId: room.forest.clearing
```

Fields:
- `blueprintId` — unique mob blueprint id (e.g. `mob.<area>.<name>`)
- `name` — display name shown in `look`
- `description` — long description; resolved by a future `look <mob>` command (not wired in this slice)
- `keywords` — prefix-matchable tokens for future `kill <mob>` / `look <mob>` commands
- `type` — `MobType` enum value (data only; no behavior in this slice)
- `spawnRoomBlueprintId` — blueprint id of the room this mob spawns in on first startup

**Content directory.** YAML files are placed under `<World:ContentDirectory>/mobs/` (e.g. `data/content/mobs/wolf.yaml`).

**Admin commands.** `mkmob [name]` and `setmob <blueprintId> <property> <value>` (properties: `name`, `description`, `keywords`, `type`).

**`TemplateRegistry` entries.** Every `kind: mob` YAML file registers one `MobTemplate`.

**Inspect / verify.** A designer confirms a mob is present by connecting a player to the spawn room and running `look`. The mob name should appear in the room description.

---

## Cross-cutting Surfaces Stressed

| Surface | Assessment |
|---|---|
| **Commands** | Adequate — `AdminRequirement`, `ICommandDispatcher`, `ICommand` shape, argument parsing, and `PlainMessage` confirmation are all established. `mkmob` and `setmob` follow the exact shape of `mkitem` and `setitem`. No new command infrastructure needed. |
| **Output** | Gap exposed (minor, resolvable in this slice) — `RoomDescriptionMessage` has no `Mobs` field today. The field and its formatter rendering path must be added. This is a surgical extension of an existing message shape; no new output infrastructure is required. |
| **ECS** | Adequate — `EntityService`, `HasComponent<T>`, `GetAllComponents<T>`, and component attachment are all in place. |
| **Persistence** | Adequate — two-level model (`PersistentEntity` + `[Persistent]`), `SaveEntityAsync`, and `FlushAllPersistentAsync` are established. `MobDataComponent` follows the same model as `ItemDataComponent`. |
| **Event bus** | Adequate — `IEventBus`, handler subscription, priority model, and `AdminAuditHandler` subscribing to new event types all work without changes. |
| **Content templates** | Gap exposed (minor, resolvable in this slice) — `WorldContentLoader` does not yet handle `kind: mob`. Adding `LoadKindAsync("mob", "mobs", ct)`, `PlaceMobsInRooms`, and `MobTemplateDeserializer` extends the existing pattern without touching the loading interface. |
| **Broadcast** | Gap exposed (minor, resolvable in this slice) — `SendRoomDescriptionAsync` does not currently populate a `Mobs` field. `BroadcastSystem` must query `MobDataComponent`-bearing entities in the room. A `GetAllComponents<MobDataComponent>()` call filtered by room id covers this. |
| **Time / heartbeat** | Not stressed. Wandering (the only mob behavior that requires a tick) is deferred. |
| **Configuration** | Adequate — `World:ContentDirectory` drives the content root; adding a `mobs/` subdirectory requires no config change. |

---

## Flows Introduced or Modified

| # | Flow | Change |
|---|---|---|
| 1 | Server startup | Extended: `LoadTemplatesAsync` adds a `kind: mob` loading pass; `SpawnMissingEntities` spawns mob entities; `PlaceMobsInRooms` places newly-spawned mobs. Mermaid diagram updated to show mob loading and placement steps. |
| 5 | Content reload | Extended: `ReloadAsync` now includes mob templates in the additive re-scan, `SpawnMissingEntities`, and `PlaceMobsInRooms` pass. The existing Flow 5 mermaid is missing `PlaceItemsInRooms` (pre-existing gap) and will also need `PlaceMobsInRooms`; both are added to the diagram in this PR. |
| 6 | Output rendering | Extended: `RoomDescriptionMessage` gains a `Mobs` field; `TelnetOutputFormatter` case-match adds mob rendering below items. |
| — | Admin mob creation (`mkmob`) | New flow (Flow 15 in `flows/README.md`): mirrors Flow 12 (`mkitem`). |

---

## Reference Catalog Updates

- `docs/reference/components.md` — add `MobDataComponent` row to the cross-cutting table.
- `docs/reference/archetypes.md` — note that the `Mob` archetype now has a partial real implementation: required `MobDataComponent` + `LocationComponent`; `BlueprintComponent` and `PersistentEntity` present on all template-spawned instances. The archetype registry is not yet built (still target-state); this is a catalog tracking note.
- `docs/reference/systems.md` — add rows for `IMobBuilderSystem` / `MobBuilderSystem`, `IMobContentWriter` / `MobContentWriter`, and note the `WorldContentLoader` and `BroadcastSystem` extensions.
- `docs/reference/handlers.md` — update the `AdminAuditHandler` entry's "Events:" line to add `MobCreatedByAdminEvent` and `MobPropertySetByAdminEvent`.

---

## Design Notes

- **`MobDataComponent` is cross-cutting.** Placed under `Core/ECS/Components/` so future modules (combat, AI, dialogue) can query mob names without taking a dependency on `Core/Modules/Mobs/`. This mirrors `ItemDataComponent`.
- **Mob persistence.** Mobs carry `PersistentEntity` so they survive restarts without a respawn system. When the death/respawn slice lands, the respawn system may choose to destroy and re-seed mob instances rather than persist them — at that point `PersistentEntity` may be removed from mob entities. This is acknowledged debt.
- **`BlueprintComponent` and respawn.** Unlike items (which clear `BlueprintComponent` on pickup to free the blueprint slot — INV-21), mobs have no "pickup" transition. When a mob is killed in a future slice, the respawn system will decide whether to reuse the entity (HP reset) or destroy it and spawn a fresh one (clearing `BlueprintComponent` from the corpse first). This decision belongs to the death/respawn slice; `BlueprintComponent` is retained on live mob entities for now so `setmob` lookups work. The INV-21 obligation (clear `BlueprintComponent` on the dead entity before re-spawning a new one, or reset in place) is tracked in `docs/roadmap/backlog.md` under the mob death/respawn item.
- **`PlaceMobsInRooms` mirrors `PlaceItemsInRooms`.** Only entities in the `newlySpawned` set are placed. Restored-from-persistence mobs already carry a saved `LocationComponent`; overriding it would clobber their live position (and, once wandering exists, wherever they wandered to).
- **`MobType` is data only.** No matching or routing logic uses `MobType` in this slice. It is a classification field for future use (vendor dialogue, guard aggression flags, AI routing).
- **Mob display in `look`.** One line per mob: `"<Name> is here."`. Identical-name mobs are not stacked ("A grey wolf is here.\nA grey wolf is here." not "Two grey wolves are here."). Stacking is acknowledged debt.
- **`look <mob>` is out of scope.** Inspecting a specific mob by keyword belongs to the same slice that introduces `kill <mob>` argument resolution. `LookCommand`'s item-fallback path is not extended to mob entities here.
- **No loot or equipment.** `InventoryComponent` and `EquipmentComponent` are not attached to mob entities in this slice. They are added when the combat/death slice requires them.

---

## Related

- [`items-and-inventory.md`](items-and-inventory.md) — slice 6; `IItemBuilderSystem`, `PlaceItemsInRooms`, `ItemTemplateDeserializer`, `IItemContentWriter` are the direct precedents for this slice's new interfaces.
- [`equipment.md`](equipment.md) — slice 7; `EquipmentComponent` is cross-cutting for players and mobs; not attached in this slice.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; `ITemplateDeserializer` pattern and `WorldContentLoader` extension points.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; `[Persistent]` + `PersistentEntity` model that mobs follow.
- [`output-framework.md`](output-framework.md) — slice 4; `RoomDescriptionMessage` extended here with a `Mobs` field.
- [`attributes.md`](attributes.md) — slice 8a (follows this slice); extends `MobTemplate` with `Level`, `MaxHp`, and base stats.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
