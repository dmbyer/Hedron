# Phase 3 slice 8 — Mobs: basic entity model and spawn (completed)

> Implemented on branch `claude/blissful-hawking-13a992`. Full feature spec: [`../../use-cases/mobs.md`](../../use-cases/mobs.md).

## Outcome

Mobs are now first-class NPC entities in the world. A designer authors a mob in YAML (`kind: mob`) under `data/content/mobs/`, and on the next startup (or `reload`) the mob spawns into its configured room and appears in `look` output as `"<Name> is here."`. An admin can also create and mutate mobs at runtime via `mkmob` and `setmob`, with YAML written atomically to disk on each change. This slice is intentionally narrow: no combat, no loot, no wandering — those land in later slices. The mob lifecycle (authored template → registered blueprint → spawned live entity → placed in room → visible to players) is the foundation they build on.

## Shipped pieces

| Surface | Location |
|---|---|
| `MobDataComponent` — `Name`, `Description`, `Keywords` (`List<string>`), `MobType`; `[Persistent]`; cross-cutting | `Core/ECS/Components/MobDataComponent.cs` |
| `MobType` enum — `None`, `Vendor`, `Guard`, `Creature`; data-only in this slice | `Core/ECS/Components/MobType.cs` |
| `MobTemplate` — `IEntityTemplate`; `Apply` attaches `MobDataComponent`; carries `SpawnRoomBlueprintId` | `Core/Modules/Mobs/Templates/MobTemplate.cs` |
| `MobTemplateDeserializer` — `ITemplateDeserializer` for `kind: mob`; warns on unknown `type`; never throws | `Core/Modules/Mobs/MobTemplateDeserializer.cs` |
| `MobCreatedByAdminEvent`, `MobPropertySetByAdminEvent` — thin past-tense event records | `Core/Modules/Mobs/Events/` |
| `IMobBuilderSystem` / `MobBuilderSystem` — `CreateMob` + four `Set*` methods; blueprint id `mob.adhoc.<8-char-base36>` | `Core/Modules/Mobs/Systems/IMobBuilderSystem.cs`, `MobBuilderSystem.cs` |
| `IMobContentWriter` / `MobContentWriter` — atomic YAML write to `{contentDir}/mobs/{blueprintId}.yaml` | `Core/Modules/Mobs/Systems/IMobContentWriter.cs`, `MobContentWriter.cs` |
| `MkMobCommand` — admin `mkmob [name]`; ordering: CreateMob → WriteAsync → SaveEntityAsync → PublishAsync | `Core/Modules/Mobs/Commands/MkMobCommand.cs` |
| `SetMobCommand` — admin `setmob <blueprintId> <property> <value>`; properties: `name`, `description`, `keywords`, `type` | `Core/Modules/Mobs/Commands/SetMobCommand.cs` |
| `MobsModule` — DI extension registering all five mob services/commands + deserializer | `Core/Modules/Mobs/MobsModule.cs` |
| `WorldContentLoader` — `MobsSubdirectory` constant; `LoadKindAsync("mob", …)` call; `PlaceMobsInRooms` private method; both `LoadAndSpawnAsync` and `ReloadAsync` extended | `Core/Modules/World/Systems/WorldContentLoader.cs` |
| `RoomDescriptionMessage` — `Mobs: IReadOnlyList<string>` field added | `Core/Output/RoomDescriptionMessage.cs` |
| `BroadcastSystem.SendRoomDescriptionAsync` — queries `MobDataComponent` entities in the room; populates `Mobs` list | `Core/Systems/BroadcastSystem.cs` |
| `TelnetOutputFormatter.FormatRoom` — renders each mob name as `"<Name> is here."` below the items line | `Core/Output/TelnetOutputFormatter.cs` |
| `AdminAuditHandler` — `IEventHandler<MobCreatedByAdminEvent>` + `IEventHandler<MobPropertySetByAdminEvent>` added; subscribed in `Program.cs` | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` |
| `Program.cs` — `AddMobsModule()` call; two new audit bus subscriptions | `Server/Program.cs` |
| `docs/reference/components.md` — `MobDataComponent` + `MobType` rows added | — |
| `docs/reference/systems.md` — `MobBuilderSystem`, `MobContentWriter` entries added; `WorldContentLoader` and `BroadcastSystem` extension notes added | — |
| `docs/reference/handlers.md` — `AdminAuditHandler` Events line updated with mob events | — |
| `docs/reference/archetypes.md` — `Mob` row annotated with partial real implementation note | — |

## Spec-review provenance

**Spec-mode gate:** Passed (2026-05-25) before implementation. No blocking findings. The spec was reviewed clean; minor cross-cutting surface gaps (Mobs field on `RoomDescriptionMessage`, `kind: mob` in `WorldContentLoader`, `Mobs` list in `BroadcastSystem`) were classified as minor/resolvable-in-slice and addressed directly.

**Code-mode gate:** Not run (implementation spans this session; to be run before PR merges).

## Notable design points

- **`MobDataComponent` is cross-cutting.** Placed under `Core/ECS/Components/` so future modules (combat, AI, dialogue) can query mob names without a domain dependency on `Core/Modules/Mobs/`. Mirrors `ItemDataComponent`.
- **Event publication ordering.** `MkMobCommand` and `SetMobCommand` write YAML first, save entity second, publish event last — matching the crash-safety ordering precedent in `LoginFlow` (entity save before account save). This ensures the template file exists on disk before any crash window opens.
- **`PlaceMobsInRooms` only places `newlySpawned` entities.** Restored-from-persistence mobs already carry a saved `LocationComponent`; overriding it would clobber their live position (and, once wandering exists, wherever they wandered to). Identical logic to `PlaceItemsInRooms`.
- **`MobType` is data only.** No matching or routing logic uses `MobType` in this slice. Classification field for future use (vendor dialogue, guard aggression, AI routing).
- **`BlueprintComponent` is retained on live mob entities.** Unlike items (which clear `BlueprintComponent` on pickup — INV-21), mobs have no pickup transition. When a mob is killed in a future slice, the respawn system decides whether to reuse or destroy the entity. The INV-21 obligation (clear `BlueprintComponent` from the dead entity before re-spawning) is tracked in `backlog.md`.
- **Mob display.** One line per mob: `"<Name> is here."`. Identical-name mobs are not stacked; stacking is acknowledged debt.
- **`look <mob>` is out of scope.** Inspecting a specific mob by keyword belongs to the same slice that introduces `kill <mob>` argument resolution.
- **No `InventoryComponent`/`EquipmentComponent` on mobs.** Added when combat/death slice requires them.

## Deviations from the use-case doc

None. All postconditions satisfied as written.

## Follow-ups unlocked

- **Slice 8a — Attributes and vitals.** `MobTemplate` is the extension point; slice 8a adds `Level`, `MaxHp`, and base stat overrides to it.
- **Slice 9 — Combat.** `MobDataComponent` provides the identity hook; `LocationComponent` provides the targeting hook; `EquipmentComponent` (already cross-cutting from slice 7) is ready to be attached to mobs.
- **Mob wandering.** Requires `TimeSystem` + AI behavior infrastructure; deferred to a dedicated slice.
- **`look <mob>` by keyword.** Requires prefix-matching argument resolver over `MobDataComponent.Keywords`; belongs with `kill <mob>` in the combat slice.
