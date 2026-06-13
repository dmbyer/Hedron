# Use Case: Mobs — Basic Entity Model and Spawn

**Status:** implemented
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
- `RoomDescriptionMessage` has a `Mobs: IReadOnlyList<string>` field. `IBroadcastSystem.SendRoomDescriptionAsync` populates `Mobs` with `MobDataComponent.Name` for every entity in the room that carries `MobDataComponent`. `look` (no arg) displays mob names below items: one line per mob rendered as `"<Name> is here."`.
- `WorldContentLoader.LoadAndSpawnAsync` calls `PlaceMobsInRooms` (private method, mirrors `PlaceItemsInRooms`) for newly-spawned mob entities only; restored-from-persistence entities are skipped.

---

## Main Flow

### Flow A-1 — Admin `mkmob [name]`

1. **Privilege gate.** `CommandDispatcher` evaluates `AdminRequirement`; non-privileged sessions are rejected before `MkMobCommand.ExecuteAsync` runs.
2. **Creation.** `MkMobCommand` calls `IMobBuilderSystem.CreateMob(name, roomEntityId)`. The system generates blueprint id `mob.adhoc.<8-char-base36>`, allocates an entity via `EntityService`, attaches `MobDataComponent { Name, Description="", Keywords=[], MobType=None }` + `BlueprintComponent { BlueprintId }` + `PersistentEntity` + `LocationComponent { RoomEntityId = invoker's current room }`, and registers a minimal `MobTemplate` in `ITemplateRegistry`. Returns `MobCreationResult(mobEntityId, blueprintId, template)`.
3. **YAML write.** Command calls `IMobContentWriter.WriteAsync(template)`. Order: YAML write → `SaveEntityAsync` → publish `MobCreatedByAdminEvent`.
4. **Event + save.** Calls `IPersistenceSystem.SaveEntityAsync(mobEntityId)`. Publishes `MobCreatedByAdminEvent(AdminEntityId, MobEntityId, BlueprintId, RoomEntityId)`. Writes a confirmation `PlainMessage` including the blueprint id.
5. `AdminAuditHandler` (priority 80) logs the event.

### Flow A-2 — Admin `setmob <blueprintId> <property> <value>`

1. **Privilege gate.** As A-1.
2. **Resolve mob.** Command looks up `blueprintId` in `ITemplateRegistry`. Not found → "No mob template found with blueprint id." Then queries `EntityService` for the live entity with matching `BlueprintComponent.BlueprintId` and `MobDataComponent`. Not found → "Mob has no live entity in the world."
3. **Mutation.** For the named property, calls the corresponding `IMobBuilderSystem.Set*` method. `keywords` splits `value` on whitespace. `type` parses as `MobType` enum; invalid value → "Unknown mob type."
4. **YAML write + save.** Order: YAML write → entity save → event publish.
5. **Event + confirmation.** Publishes `MobPropertySetByAdminEvent(AdminEntityId, MobEntityId, PropertyName, NewValue)`. Writes a confirmation `PlainMessage`.
6. `AdminAuditHandler` (priority 80) logs the event.

### Flow A-3 — World-content mob spawn (startup / reload)

1. `WorldContentLoader.LoadTemplatesAsync` reads files from `<ContentDirectory>/mobs/`. For each, calls `IContentSerializer.Deserialize("mob", body)` → `MobTemplateDeserializer.Deserialize` → `MobTemplate`. Registers in `ITemplateRegistry`.
2. `SpawnMissingEntities` (existing): for mob templates with no live entity in the blueprint map, calls `ITemplateRegistry.Spawn(blueprintId)`. `MobTemplate.Apply` attaches `MobDataComponent`. `SpawnMissingEntities` also attaches `PersistentEntity` and adds to `liveBlueprints`. Returns the `newlySpawned` set.
3. **Immediate save.** `WorldContentLoader` calls `IPersistenceSystem.SaveEntityAsync` for every newly-spawned entity.
4. **`PlaceMobsInRooms(liveBlueprints, newlySpawned)`.** For each `MobTemplate` whose entity is in `newlySpawned` and whose `SpawnRoomBlueprintId` is non-empty: resolves the room blueprint id → room entity id, attaches `LocationComponent { RoomEntityId }`. Restored-from-persistence entities are skipped. Unknown spawn room logs a warning; mob is created without a location.

### Flow B-1 — `look` (no arg) with mobs present

1. Player sends `look`. `LookCommand` calls `IBroadcastSystem.SendRoomDescriptionAsync(playerEntityId, roomEntityId)`.
2. `BroadcastSystem.SendRoomDescriptionAsync` queries all entities that have `MobDataComponent` + `LocationComponent.RoomEntityId == roomEntityId`. Builds `Mobs` list from `MobDataComponent.Name` for each.
3. Produces `RoomDescriptionMessage` with `Mobs` populated. `TelnetOutputFormatter` renders mob names below the items section, one line per mob: `"<Name> is here."`.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `MobCreatedByAdminEvent` | `MkMobCommand` | `uint AdminEntityId, uint MobEntityId, string BlueprintId, uint RoomEntityId` | Audit log; future editor hooks |
| `MobPropertySetByAdminEvent` | `SetMobCommand` | `uint AdminEntityId, uint MobEntityId, string PropertyName, string NewValue` | Audit log |

---

## Design Notes

- **`MobDataComponent` is cross-cutting.** Placed under `Core/ECS/Components/` so future modules (combat, AI, dialogue) can query mob names without taking a dependency on `Core/Modules/Mobs/`. This mirrors `ItemDataComponent`.
- **Mob persistence.** Mobs carry `PersistentEntity` so they survive restarts without a respawn system. When the death/respawn slice lands, the respawn system may choose to destroy and re-seed mob instances rather than persist them — at that point `PersistentEntity` may be removed from mob entities. This is acknowledged debt.
- **`BlueprintComponent` and respawn.** Unlike items (which clear `BlueprintComponent` on pickup — INV-21), mobs have no "pickup" transition. When a mob is killed in a future slice, the respawn system will decide whether to reuse the entity (HP reset) or destroy it and spawn a fresh one. The INV-21 obligation is tracked in `docs/roadmap/backlog.md`.
- **`PlaceMobsInRooms` mirrors `PlaceItemsInRooms`.** Only entities in the `newlySpawned` set are placed. Restored-from-persistence mobs already carry a saved `LocationComponent`.
- **`MobType` is data only.** No matching or routing logic uses `MobType` in this slice. Classification field for future use (vendor dialogue, guard aggression flags, AI routing).
- **Mob display in `look`.** One line per mob: `"<Name> is here."`. Identical-name mobs are not stacked. Stacking is acknowledged debt.
- **`look <mob>` is out of scope.** Inspecting a specific mob by keyword belongs to the same slice that introduces `kill <mob>` argument resolution.
- **No loot or equipment.** `InventoryComponent` and `EquipmentComponent` are not attached to mob entities in this slice. Added when combat/death slice requires them.

---

## Related

- [`items-and-inventory.md`](../features/items/items.md) — slice 6; `IItemBuilderSystem`, `PlaceItemsInRooms`, `ItemTemplateDeserializer`, `IItemContentWriter` are the direct precedents.
- [`equipment.md`](../features/items/items.md) — slice 7; `EquipmentComponent` is cross-cutting for players and mobs; not attached in this slice.
- [`world-content-loading-and-admin-substrate.md`](../features/world/world.md) — slice 2; `ITemplateDeserializer` pattern and `WorldContentLoader` extension points.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; `[Persistent]` + `PersistentEntity` model that mobs follow.
- [`output-framework.md`](output-framework.md) — slice 4; `RoomDescriptionMessage` extended here with a `Mobs` field.
- [`attributes.md`](../features/character-stats/character-stats.md) — slice 8a (follows this slice); extends `MobTemplate` with `Level`, `MaxHp`, and base stats.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
