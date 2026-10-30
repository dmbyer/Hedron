# Spawn authoring: rule-keyed slots with count, one spawn truth, editor + admin authoring

- **Status:** planned
- **Actors:** Player (meets respawning mobs and found items), Administrator (authors rules in game), Designer (authors rules in the room / grid / mob / item editors), Operator (converts a pre-slice world by hand), System (boot fill, vacancy, heartbeat respawn, fail-fast shape checks)
- **Module:**
  - `Core/Modules/Spawn` owns the rule-keyed slot model (`SpawnSystem`).
  - `Core/ECS/Components/` holds the changed `SpawnRule` shape (`SpawnConfigComponent.cs`) and, **beside it**, the pure rule helpers: `SpawnRuleLimits`, `SpawnRuleIds` and `SpawnRuleValidation`. Placing them next to the record, rather than in `Modules/Spawn`, means World's deserializer, Admin's builder and Authoring's catalog all depend only on `Core.ECS.Components`. That avoids a World↔Spawn mutual reference.
  - `Core/Modules/World` changes:
    - the loader: rooms and areas only; a staged, fail-fast read phase;
    - the room deserializer and writer;
    - `ContentValidator`.
  - `Core/Systems` gets the shape-error exception types.
  - `Core/Modules/Admin` owns:
    - the room-builder spawn-rule operations;
    - the `spawnrule` verb and its audit event;
    - the `reload` refusal path.
  - `Core/Modules/Mobs` and `Core/Modules/Items` change `mkmob`/`mkitem`/`setmob`/`setitem`, their builder systems, their templates (the spawn-room field is removed) and their `*PropertySetByAdminEvent` payloads. **New module edge:** `MkMobCommand`/`MkitemCommand` now depend on `Core/Modules/Admin`'s `IRoomBuilderSystem`. That is a command → domain-system dependency; Admin does not depend back on Mobs or Items.
  - `Core/Modules/Authoring` gets:
    - `ISpawnRuleAuthoring` on the catalog;
    - the generator change;
    - removal of the spawn-room reference edges and DTO field.
  - `Hedron.Web` gets:
    - the spawn-rules editor (room editor and grid editor);
    - the mob/item "Spawns in" panel;
    - a mob-DTO strict-shape change for the JSON API.
  - Feature homes: [`world/spawn-system.md`](../features/world/spawn-system.md), [`world/world-content.md`](../features/world/world-content.md), [`admin-authoring/`](../features/admin-authoring/admin-authoring.md).
- **Program:** slice 2 of [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md#slice-decomposition). Covers **R9, R12, R30**, plus the "Spawn rule keyed by rule, with count" seam and resolved decision 4. Depends on slice 1 ([`dev-world-foundation.md`](dev-world-foundation.md)).

## Description

Spawn slots stop being keyed by `(room, blueprint)` and become keyed by **rule identity**. Each room `SpawnRule` gets an id and a `count`, and each of its `count` slots respawns on its own timer.

The room's rule becomes the **single spawn truth**:
- The mob/item-side spawn room (`spawnRoomBlueprintId` / `spawnRoomId`) is **removed**. The editors keep a "Spawns in" shortcut that writes a room rule instead.
- The loader now spawns only areas and rooms.
- Every world mob and found item comes from a rule. It is placed at boot, and it respawns after it dies or is picked up (R9, R12).

The change is a **hard break**:
- These shapes are fail-fast load errors, with an actionable message naming the file: a non-blank legacy spawn-room key, the old `minCount`/`maxCount` rule shape, and any other malformed spawn rule.
- They abort boot on both hosts.
- `reload` refuses as a whole and leaves the live world untouched.
- The operator converts a pre-slice world by hand.

Rules are authored and inspected in the room editor and the grid editor, from the mob/item editors, and through an admin `spawnrule` verb. Admin mutations update both the template and the live room (INV-18, INV-21).

The slot model is shaped so tiered-overworld Stage B's weighted pools become "a rule whose blueprint is drawn from a pool" without a slot-model change. Pools themselves stay out (resolved decision 4).

## Preconditions

- **Program decisions settled:** R9, R12, R30, the "One spawn truth" design note, and resolved decision 4 ([program](phase-5-content-baseline-program.md#design-notes)).
- **Slice 1 is merged:**
  - `WorldContentLoader` has the public, manifest-aware `ResolveStartingRoom()`;
  - the dev-world hub skeleton exists, with **no spawn rules** (slice 1 WP3).
- **Current slot model** (verified in code):
  - `SpawnSystem` keys slots `(roomEntityId, blueprintId)` (`SpawnSystem.cs:34-37`).
  - A second rule for the same blueprint in one room is **dropped** with a warning (`:78-84`).
  - Initialization claims **any** live entity in the room with that blueprint (`:88-100`).
  - A rule whose blueprint was not pre-placed waits a **full respawn delay** before its first spawn (`:107-111`).
  - `WorldContentReadyEvent` is handled at `HandlerPriority.Notification` (80) (`:39`).
- **Current rule shape:**
  - `SpawnRule` is `(BlueprintId, MinCount, MaxCount, RespawnDelaySeconds)` (`SpawnConfigComponent.cs:8-12`).
  - `SpawnSystem` never reads Min/Max.
  - The deserializer defaults to 1/1/300 and skips a blank `blueprintId` with a warning (`RoomTemplateDeserializer.cs:79-95`).
  - The writer **always emits** `minCount` and `maxCount` (`RoomContentWriter.cs:47-53`, non-nullable DTO fields), so every rule written before this slice carries the old shape.
- **Legacy keys are always written:**
  - The mob writer always emits `spawnRoomBlueprintId` (`MobContentWriter.cs:84, 121`).
  - The item writer always emits `spawnRoomId` (`ItemContentWriter.cs:57, 78`).
  - Both default to `''`, so an *unplaced* pre-slice mob or item still carries a **blank** key.
- **Loader placement** (`WorldContentLoader.cs`):
  - `SpawnMissingEntities` spawns one live entity for **every** registered template, mobs and items included (`:188-200`).
  - `PlaceItemsInRooms` / `PlaceMobsInRooms` place them only when a spawn room is set (`:235-311`).
  - So every mob/item template without a spawn room becomes an unplaced "limbo" entity.
  - A spawn-room mob with no matching rule is placed once and **never respawns**.
- **Loader error posture:**
  - Per-file load failures are caught, logged and **skipped** (`:157-168`); boot continues.
  - `ReloadAsync` calls `DestroyWorldContent` and `_templateRegistry.Clear()` **before** reading any file (`:100-105`). A failure mid-read therefore leaves a half-built world.
  - `RegistryValidationBootstrap` is the existing fail-fast precedent: it logs a full report and throws to abort boot, and is registered by **both** hosts (`Server/RegistryValidationBootstrap.cs:54-72`; `CompositionRoot.cs:217-223`).
- **Shopkeeper seeding order:** `ShopkeeperSpawnHandler` seeds tills and base stock on `WorldContentReadyEvent` at `Domain` (20) (`ShopkeeperSpawnHandler.cs:46, 60-70`).
- **Builders:**
  - `MobBuilderSystem.CreateMob` and `ItemBuilderSystem.CreateItem` create a live entity in the room and set the template's spawn room (`MobBuilderSystem.cs:29-60`, `ItemBuilderSystem.cs:33-55`).
  - `setmob` / `setitem` update the **first** live entity found by blueprint, and refuse when there is none (`SetMobCommand.cs:84-101`, `SetitemCommand.cs:86-102`).
  - `setitem`'s scan does not exclude `PersistentEntity` copies.
- **Editors:**
  - The room editor and the grid editor's room panel have **no spawn-rule UI** (`RoomEditor.razor:19-24`, `AreaGridEditor.razor:165-166`).
  - The mob/item editors carry a "Spawn location" area→room picker (`MobEditor.razor:58-84`, `ItemEditor.razor:82-105`).
- **Authoring layer:**
  - **Reference edges.** `ContentReferenceIndex` declares `(Item|Mob, SpawnRoomBlueprintId) → Room` and the two-kind `(Room, SpawnRules[]) → {Mob, Item}` edge (`ContentReferenceIndex.cs:105-119, 139-156`).
  - **Catalog cascades.** Delete/rename already remove or rewrite rule `BlueprintId`s (`ContentDefinitionCatalog.cs:850-862`).
  - **Catalog area attribution.** Item/mob area attribution resolves through the spawn room (`:1066-1086`).
  - **`CreateNextFrom(Mob)`** carries the spawn room (`:553-561`).
  - **Catalog file reads.** `List` skips a file that fails to deserialize, with a warning (`:746-757`).
  - **Generator.** `generate` sets the spawn room on every generated mob and item (`ContentGenerationSystem.cs:165-199`).
- **JSON API:**
  - The authoring JSON DTO carries `SpawnRoomBlueprintId` (`MobDefinitionMapper.cs:28, 73`).
  - The host's JSON options **ignore unknown members**: only the enum converter is configured (`Hedron.Web/Program.cs:64-69`).
  - The conformance fitter copies the field (`TemplateConformanceSystem.cs:321, 337`).
- **Web host runs no spawning:**
  - It registers no event subscriptions (`Hedron.Web/Program.cs:48`); subscriptions live only in `Server/Program.cs:167-171`.
  - Its preview world never runs `SpawnSystem`.
  - Nothing in `Hedron.Web` reads live mob/item entities.
  - `Apply.razor` already catches a `ReloadAsync` exception and shows its message (`Apply.razor:54-61`).
- **Committed content needs no rewriting:**
  - `content/dev-world/rooms/room.void.yaml` has an empty `spawnRules`; slice 1 deletes it.
  - `content/profiles/example.yaml` has no spawn fields.
- **Doc drift to correct.** The implementer fixes each of these in the WP that owns the touched text; the work-package Docs bullets cite them.
  - **Pre-existing errors:**
    - `spawn-system.md:40` says `ItemSystem.MoveToInventory` clears `BlueprintComponent`; the code preserves it (`ItemSystem.cs:90`, INV-21).
    - `spawn-system.md`'s "Self-initializing" consideration cites INV-10 for keeping the loader closed to spawn concerns. INV-10 is the no-chain-initiator exception, so the citation is wrong (see Design notes).
    - The `reference/systems.md:527` `SpawnSystem` dependencies omit `IClock`.
  - **Stale after this slice:**
    - `admin-authoring.md:11`: "Apply" does not refresh the Server's live world. It reloads only the web host's own preview world; the live game world applies edits on Server `reload`/restart.
    - `admin-authoring.md:31, 35`: the command list and event list gain `spawnrule` and `SpawnRuleAuthoredByAdminEvent`.
    - `docs/architecture/06-persistence.md:16`: the INV-21 coexistence wording becomes "`SpawnSystem` never claims an existing entity; every slot occupant is one it spawned".
    - `docs/features/mobs/mobs.md:16, 59` and `docs/features/world/area-model.md:13`: spawn-room placement becomes room spawn rules.
    - `docs/reference/handlers.md:180-186` (`ShopkeeperSpawnHandler`): record the fill-then-seed ordering dependency.
    - `ShopkeeperSpawnHandler`'s XML doc: replace "analogous to `SpawnSystem.HandleAsync(WorldContentReadyEvent)` and `WorldContentLoader.PlaceItemsInRooms`" with the State(10)→Domain(20) dependency (INV-7).
    - XML docs: `IContentDefinitionCatalog.cs:43, 90, 178`; `IContentReferenceIndex.cs:17-18`; `ContentReference.cs:12, 24, 42`; `MobTemplate.cs:165`.
    - `ReloadCommand.LongDescription`: add the refusal behavior.
    - The flow-08 `reload` paragraph (~line 49): add the refusal branch.

## Postconditions

*(The coverage contract. Every item marked ◆ asserts internal state and maps to a named test in the Test plan.)*

1. ◆ **Rule shape.** `SpawnRule` is `(Id, BlueprintId, Count, RespawnDelaySeconds)`. Room YAML round-trips all four fields. The writer never emits `minCount`/`maxCount`.
2. ◆ **Id default.** A rule with no `id` reads `id = SpawnRuleIds.DefaultFor(blueprintId)`. That is the blueprint id, **deterministically truncated** to its first `MaxIdLength − 4` (60) characters, which leaves room for a `-N` suffix of up to `-999` within the 64-character cap. For any blueprint of 60 characters or fewer, the default is the blueprint id itself. If the default collides with another rule's id in the same room, it is a **load error** asking for explicit ids. The id is always written.
3. ◆ **Fail-fast shape errors.** Each of the following makes the deserializer throw `ContentShapeException`, whose message names the blueprint, the offending key, and the replacement:
   - a spawn rule carrying `minCount` or `maxCount` (*"replace with `count: N`"*);
   - a mob with a **non-blank** `spawnRoomBlueprintId`, or an item with a non-blank `spawnRoomId` (*"remove it and add a spawn rule to room `X`'s `spawnRules`"*);
   - a spawn rule with a blank blueprint, a malformed or duplicate id, a count outside `1..SpawnRuleLimits.MaxCountPerRule`, or a negative delay.

   A **blank** legacy spawn-room key is ignored, and dropped on the next write. It carries no placement, and every pre-slice writer emitted it.
4. ◆ **Boot aborts.** `LoadAndSpawnAsync` first reads every content file. If any file raises a shape error, it throws `ContentLoadException`, which lists **every** offending file and message (one boot attempt yields the operator's whole conversion worklist). Nothing is registered or spawned, and the host does not start (Server and Web). Other per-file failures, such as malformed YAML, keep today's log-and-skip posture.
5. ◆ **Reload never half-applies.** `ReloadAsync` reads and deserializes the whole corpus into a staging set **before** it tears down the world or clears the registry. On a shape error it throws `ContentLoadException` and leaves the live world entities, the registry, and the `SpawnSystem` tracker unchanged.

   `ReloadCommand` then:
   - catches the exception and writes an error listing each file;
   - publishes **neither** `WorldContentReadyEvent` **nor** `ContentReloadedEvent`.

   `Apply.razor` shows the same message (no code change).
6. ◆ **One rule set, two enforcement points.** The deserializer and `ContentValidator.Validate(RoomTemplate)` enforce the same `SpawnRuleValidation` rules. The validator refuses at catalog save, and the builder refuses at admin mutation. `ValidateRegistry` **warns** (not errors) when a rule's blueprint is unknown or is not a mob/item template. Reference problems keep the warn-but-allow posture.
7. ◆ **The loader spawns only areas and rooms.** After a successful load or reload, no mob or item entity exists that came from the loader: no limbo entities, and no loader-placed mobs or items.
8. ◆ **Boot fill.** On `WorldContentReadyEvent`, now handled at `HandlerPriority.State` (10), `SpawnSystem` rebuilds its tracker and **immediately** fills every slot. Each rule yields `Count` fresh entities in its room, each with `LocationComponent { RoomEntityId, RoomBlueprintId }` (INV-23). An existing entity with the same blueprint, such as a player-owned copy, is never claimed.
9. ◆ **Shopkeeper ordering.** A shopkeeper placed by a rule is seeded (till and base stock) by `ShopkeeperSpawnHandler` (Domain 20) in the same fan-out.
10. ◆ **Duplicate blueprints.** Two rules with the same blueprint and distinct ids in one room are both honored.
11. ◆ **Vacancy and respawn.** `MobDiedEvent` / `ItemPickedUpEvent` on a tracked instance vacates **only that slot**, with `RespawnAt = now + rule delay`. Each slot's timer is independent. A heartbeat tick spawns one entity per due slot. Inside the tracker lock, `TryRespawn` **skips** any slot whose room entity no longer has a `RoomComponent`. This closes most of the reload-teardown vs heartbeat orphan race (see INV-31 posture).
12. ◆ **Rule identity.** Slots key on `(room, rule id, ordinal)`. Reordering a room's rules changes nothing:
    - after `reload`, each id has the same population;
    - after a live `SyncRoom`, nothing is spawned or detached.
13. ◆ **`ISpawnSystem.SyncRoom(room)`** reconciles the tracker with the room's live `SpawnConfigComponent`:
    - a new rule spawns `Count` now, and a count increase spawns the delta now;
    - a count decrease or a removed rule drops surplus slots, vacant slots first;
    - an occupied slot that is dropped **detaches** its entity: it stays alive but untracked, and its death schedules nothing;
    - an unchanged rule keeps its entities and pending timers;
    - a changed delay applies from the next vacancy, and a changed blueprint from the next spawn.

    `DescribeRoom(room)` reports, per rule: id, blueprint, count, live count, delay, and next due time.
14. ◆ **`spawnrule add | set | remove` (admin)** works through `IRoomBuilderSystem`:
    - the builder updates the registered `RoomTemplate`, the live `SpawnConfigComponent`, and the tracker (via `SyncRoom`);
    - the command then writes the room YAML and publishes `SpawnRuleAuthoredByAdminEvent`;
    - there is no `SaveEntityAsync`.

    A refusal leaves the template, component, tracker and file unchanged, and publishes no event. Refusal cases: unknown blueprint, a blueprint that is not a mob or item, unknown rule id, invalid count/delay/id, and a location that is not a room. `spawnrule` with no argument is read-only.
15. ◆ **`mkmob` / `mkitem`** first validate the invoker's location: it must be a room carrying a `BlueprintComponent` with a registered `RoomTemplate`. Only then do they call `CreateMob`/`CreateItem`, so a refusal leaves no orphan template or file. They register a template that has no spawn-room field, and add a count-1 rule (default delay) to the current room's template, live component and YAML. The confirmed entity is that rule's tracked instance: kill it or pick it up, and it respawns.
16. ◆ **`setmob` / `setitem`** apply to the template **and every live world instance**. Entities with `PersistentEntity` are never touched (INV-21). With zero live instances they update the template and YAML, and succeed. Each publishes its `*PropertySetByAdminEvent` with the payload `(AdminEntityId, BlueprintId, PropertyName, NewValue, InstancesUpdated)`. The former single `MobEntityId`/`ItemEntityId` is **dropped**, because a property set no longer targets one entity.
17. ◆ **`ISpawnRuleAuthoring`** (on the catalog, under `_writeGate`):
    - `UpsertSpawnRuleAsync` mints an id when it is blank, and replaces a rule by id;
    - `RemoveSpawnRuleAsync` removes a rule by id;
    - `SpawnSitesFor(kind, id)` lists every `(room, rule)` that references a mob or item;
    - an invalid rule is refused with nothing written.
18. ◆ **Area attribution.** A mob's or item's `ContentSummary.AreaBlueprintId` is the area of the **ordinal-least** room whose rule references it, or `null` when no rule does.
19. ◆ **Rename/delete.** Renaming a mob or item rewrites each referencing rule's `BlueprintId` and **keeps its `Id`**. Deleting a mob or item removes its rules. The `(Item|Mob, SpawnRoomBlueprintId) → Room` edges are removed from the reference index; it now has 4 edges.
20. ◆ **`generate`** emits each generated mob or item as a count-1 rule in its room, with no spawn-room key. Same-seed output stays byte-identical.
21. ◆ **`CreateNextFrom(Mob)`** carries Tier, Band and `XpScale`.
22. ◆ **JSON API:** `MobDefinitionDto` has **no** `spawnRoomBlueprintId`. A POST/PUT whose body carries it is **rejected with 400**, and nothing is written: the DTO disallows unmapped members. GET responses no longer include it.

## Main flow

1. **Read (staged).** `WorldContentLoader.ReadCorpusAsync` deserializes every kind subdirectory into a staging list, collecting each `ContentShapeException` (prefixed with the file path).
   - If any were collected, it throws `ContentLoadException` before anything else happens.
   - At boot this aborts `WorldContentBootstrap`, so the host does not start.
   - On reload, the live world is untouched (step 7).
2. **Load.** On a clean read, the staged templates are registered. `SpawnAndPlaceWorld` spawns **area and room** templates only, links exits and areas, and resolves the starting room (slice 1). Room `Apply` copies the rules into `SpawnConfigComponent`.
3. **Fill.** `WorldContentBootstrap` (or `ReloadCommand`) publishes `WorldContentReadyEvent`.
   - `SpawnSystem` (State 10) clears its tracker, builds one `RuleState` per `(room, rule id)` with `Count` slots, and spawns every slot via `ITemplateRegistry.Spawn` + `LocationComponent`.
   - Then `ShopkeeperSpawnHandler` and `CharacterHydrationHandler` (Domain 20) run against the populated world.
4. **Vacate and respawn.**
   - `CombatMobDeathHandler` publishes `MobDiedEvent` before `DestroyEntity`. `SpawnSystem` (Domain) vacates the slot, with `RespawnAt = now + delay`.
   - `get` publishes `ItemPickedUpEvent`. `SpawnSystem` vacates the item's slot, and `ItemContextHandler` promotes the item to persistent (unchanged).
   - On `HeartbeatTickEvent` (Ai 95), each due slot spawns one fresh entity in its room. A slot whose room entity has lost its `RoomComponent` (torn down mid-reload) is skipped.
5. **Admin authors live.** `SpawnRuleCommand` (`spawnrule add mob.b1.rat 3 120`):
   - calls `IRoomBuilderSystem.AddSpawnRule`, which validates, mutates the template and the live component, and calls `ISpawnSystem.SyncRoom` (3 rats spawn now);
   - writes the room YAML;
   - publishes `SpawnRuleAuthoredByAdminEvent` (`AdminAuditHandler` logs it) and confirms.

   `set` and `remove` follow the same path.
6. **Admin creates and edits content.**
   - `mkmob [name]`, in order:
     1. **Validates the location first:** the invoker must be in a room with a `BlueprintComponent` and a registered `RoomTemplate`. On refusal nothing is created.
     2. `CreateMob(name)` registers the template only, and the command writes the mob YAML.
     3. `IRoomBuilderSystem.AddSpawnRule(room, bp, 1, default)` syncs and spawns the instance, and the command writes the room YAML.
     4. It publishes `MobCreatedByAdminEvent` (with the instance id) and `SpawnRuleAuthoredByAdminEvent`.
   - `mkitem` is symmetric.
   - `setmob <bp> <prop> <value>` calls the blueprint-keyed builder setter, which updates the template and every world instance. The command writes the YAML and publishes `MobPropertySetByAdminEvent(admin, bp, prop, value, instancesUpdated)`.
7. **Reload refused on bad content.** `ReloadCommand`:
   - calls `FlushAllAsync`, then `ReloadAsync`;
   - `ReloadAsync` stages the read and throws `ContentLoadException` on a shape error;
   - the command writes the per-file error list and returns. No teardown, no `WorldContentReadyEvent`, no `ContentReloadedEvent`.
8. **Designer authors in the editor.**
   - The room editor and the grid room panel bind `RoomSpawnRulesEditor` to `RoomTemplate.SpawnRules`. Ids come from `SpawnRuleIds.Mint`, and saving goes through `SaveAsync` / `SaveRoomAsync` (validated).
   - The mob/item **Spawns in** panel lists `SpawnSitesFor(...)` and writes through `UpsertSpawnRuleAsync` / `RemoveSpawnRuleAsync`.
   - Changes reach the live game world on a Server `reload` or restart (flow-05). Apply in the web host refreshes only the web host's own preview world.
9. **Generate.** `ContentGenerationSystem` adds a count-1 rule to the room for each mob or item it places there, and writes as before. `ValidateEmitted` deserializes the output through the same fail-fast deserializers.

## Events fired

| Event | Publisher | Subscribers | Status |
|---|---|---|---|
| `SpawnRuleAuthoredByAdminEvent(AdminEntityId, RoomEntityId, RoomBlueprintId, RuleId, SpawnRuleChange Change, string? BlueprintId, int? Count, int? RespawnDelaySeconds)` | `SpawnRuleCommand`, `MkMobCommand`, `MkitemCommand` (Initiators) | `AdminAuditHandler` (same priority as its other `*ByAdminEvent` subscriptions) | **new** (`Core/Modules/Admin/Events/`); `SpawnRuleChange` enum is `Added, Updated, Removed` |
| `WorldContentReadyEvent` | `WorldContentBootstrap`, `ReloadCommand` (only after a **successful** reload) | `SpawnSystem` moves **80 → 10** (State); `ShopkeeperSpawnHandler` and `CharacterHydrationHandler` unchanged (20) | reused; priority change; not published on a refused reload |
| `ContentReloadedEvent` | `ReloadCommand` | `AdminAuditHandler` | reused; not published on a refused reload |
| `MobDiedEvent` / `ItemPickedUpEvent` / `HeartbeatTickEvent` | unchanged | `SpawnSystem` (Domain / Domain / Ai, unchanged) | reused |
| `MobCreatedByAdminEvent` / `ItemCreatedByAdminEvent` | `mkmob` / `mkitem` | unchanged | reused; the entity id is now the rule's tracked instance |
| `MobPropertySetByAdminEvent(AdminEntityId, BlueprintId, PropertyName, NewValue, int InstancesUpdated)` | `SetMobCommand` | `AdminAuditHandler` (log fields updated) | **payload changed**: `MobEntityId` is **dropped**, `BlueprintId` and `InstancesUpdated` are added, because the set targets a blueprint fan-out, not one entity. Consumers to update: `AdminAuditHandler`, `Server/Program.cs` (subscription unchanged), `SetMobCommandBandTests` |
| `ItemPropertySetByAdminEvent(AdminEntityId, BlueprintId, PropertyName, NewValue, int InstancesUpdated)` | `SetitemCommand` | `AdminAuditHandler` | **payload changed**, as above; consumers: `AdminAuditHandler`, `SetitemCommandBandTests`, `SetitemValueCommandTests` |

The program brief's "no new events required" covers the gameplay path: vacancy and respawn add none. The two property-set payload changes are past-tense, thin facts (INV-6). The blueprint and instance count are the facts a subscriber can't reconstruct afterwards. The one new event is an **audit** event on the established `*ByAdminEvent` fan-in, following slice 1's `StartingRoomSetByAdminEvent` precedent. `SpawnSystem` and the loader publish nothing (INV-5).

## Systems / handlers involved

| Piece | Kind | Status | Change |
|---|---|---|---|
| `SpawnRule` (`Core/ECS/Components/SpawnConfigComponent.cs`) | record (data) | **changed** | `(string Id, string BlueprintId, int Count, int RespawnDelaySeconds)` |
| `SpawnConfigComponent` | component | reused | shape unchanged; **not** `[Persistent]` |
| `SpawnRuleLimits` (`Core/ECS/Components/`, beside `SpawnRule`) | constants | **new** | `DefaultRespawnDelaySeconds = 300`, `MaxCountPerRule = 20`, `MaxIdLength = 64`, `MaxSuffixLength = 4` |
| `SpawnRuleIds` (`Core/ECS/Components/`) | static pure helper | **new** | `string DefaultFor(string blueprintId)`: the first `MaxIdLength − MaxSuffixLength` (60) characters of the blueprint id, which is the blueprint id itself when it is short enough. `string Mint(IEnumerable<string> existingIds, string blueprintId)`: `DefaultFor`, then `-2` … `-999`, case-insensitive; it throws if all of those are taken. `bool IsWellFormed(string id)`: `^[A-Za-z0-9._-]{1,64}$`. Every minted id is well-formed by construction |
| `SpawnRuleValidation` (`Core/ECS/Components/`) | static pure helper | **new** | `IReadOnlyList<string> Errors(string roomBlueprintId, IReadOnlyList<SpawnRule> rules)`, the single rule-shape check used by the deserializer (throw), `ContentValidator` (refuse) and `RoomBuilderSystem` (refuse) |
| `ContentShapeException` / `ContentLoadException` (`Core/Systems/`) | exception types | **new** | `ContentShapeException(string message)` is thrown by a deserializer for a fail-fast shape error. `ContentLoadException(IReadOnlyList<string> Errors)` aggregates them, each prefixed `<kind>/<file>: `, and its `Message` is the full multi-line report (the same format as `RegistryValidationBootstrap`) |
| `RoomTemplateDeserializer` / `RoomContentWriter` | content I/O | **changed** | the `id`/`count` keys; `minCount`/`maxCount` detected and thrown on; `SpawnRuleValidation` errors thrown; the writer emits the new shape only |
| `MobTemplate` / `ItemTemplate` | templates | **changed** | the `SpawnRoomBlueprintId` property is **removed** |
| `MobTemplateDeserializer` / `ItemTemplateDeserializer` | content I/O | **changed** | a DTO detection field only: a non-blank `spawnRoomBlueprintId` / `spawnRoomId` → `ContentShapeException`; blank → ignored |
| `MobContentWriter` / `ItemContentWriter` | content I/O | **changed** | the spawn-room key is no longer emitted |
| `WorldContentLoader` | domain system | **changed** | a private `ReadCorpusAsync` that stages every file and aggregates shape errors into `ContentLoadException`, used by `LoadAndSpawnAsync` **and** by `ReloadAsync` *before* `DestroyWorldContent`/`Clear`; `SpawnMissingEntities` is limited to `RoomTemplate`/`AreaTemplate`; `PlaceItemsInRooms`/`PlaceMobsInRooms` are deleted |
| `ReloadCommand` | admin command | **changed** | catches `ContentLoadException`: writes an error per file; publishes no `WorldContentReadyEvent`/`ContentReloadedEvent` |
| `WorldContentBootstrap` | hosted service | **changed (doc + log only)** | lets `ContentLoadException` propagate (host start fails) after `LogCritical` of the report |
| `ContentValidator` | validation system | **changed** | `Validate(RoomTemplate)` refuses with `SpawnRuleValidation` errors; `ValidateRegistry` warns on a rule naming an unknown or non-mob/item template |
| `ISpawnSystem` / `SpawnSystem` | domain system (+ event subscriber) | **changed** | rule-keyed tracker; immediate boot fill; no claiming; `SpawnSyncResult SyncRoom(uint roomEntityId)`; `IReadOnlyList<SpawnRuleStatus> DescribeRoom(uint roomEntityId)`; `WorldContentReadyEvent` at State; a private lock over the tracker; `TryRespawn` skips a slot whose room entity no longer has a `RoomComponent` (reload-teardown race guard) |
| `SpawnSyncResult` / `SpawnRuleStatus` (`Core/Modules/Spawn/Systems/`) | result records | **new** | `(IReadOnlyList<uint> SpawnedEntityIds, int SlotsAdded, int SlotsDetached)`; `(RuleId, BlueprintId, Count, Live, RespawnDelaySeconds, DateTime? NextRespawnAt)` |
| `IRoomBuilderSystem` / `RoomBuilderSystem` | domain system | **changed** | + ctor `ISpawnSystem` (a lateral dependency; precedent `IAreaSystem`) and three operations; each validates, mutates the template and the live component, then calls `SyncRoom`. The component is added with the first rule and removed with the last, matching `RoomTemplate.Apply`. Operations: `SpawnRuleMutationResult AddSpawnRule(uint roomId, string blueprintId, int count, int respawnDelaySeconds, string? ruleId = null)`; `UpdateSpawnRule(uint roomId, string ruleId, SpawnRuleUpdate update)`; `RemoveSpawnRule(uint roomId, string ruleId)`. Types: `SpawnRuleMutationResult = (bool Success, string? Error, SpawnRule? Rule, string RoomBlueprintId, SpawnSyncResult Sync)`; `SpawnRuleUpdate = (int? Count, int? RespawnDelaySeconds, string? BlueprintId)` |
| `SpawnRuleCommand` (`Core/Modules/Admin/Commands/`) | admin command (Initiator) | **new** | `AdminRequirement`, `CommandMatchingMode.Full`. Schema: `action` (`SpawnRuleAction` enum token, optional: `Add`, `Set`, `Remove`), `arg1`, `arg2`, `arg3`, `arg4` (optional string tokens). The command dispatches per action and validates arity and types: `add <blueprintId> [count:int] [delaySeconds:int] [id]`, `set <ruleId> <count\|delay\|blueprint> <value>`, `remove <ruleId>`. No action means list. Wrong arity, or a non-int where one is required, writes that sub-action's usage line |
| `IMobBuilderSystem` / `IItemBuilderSystem` | domain systems | **changed** | `CreateMob(string name)` / `CreateItem(string name)` register a template only; `Set*` methods become **blueprint-keyed**, updating the template and every live world instance (excluding `PersistentEntity`) and returning the instance count |
| `MkMobCommand` / `MkitemCommand` / `SetMobCommand` / `SetitemCommand` | admin commands | **changed** | per Main flow 6: `mk*` validate the room **before** creating anything and gain an `IRoomBuilderSystem` dependency (a new Mobs/Items → Admin module edge); the "has no live entity" refusal is removed |
| `MobPropertySetByAdminEvent` / `ItemPropertySetByAdminEvent` | events | **changed** | payload `(AdminEntityId, BlueprintId, PropertyName, NewValue, InstancesUpdated)` |
| `AdminAuditHandler` | handler | **changed** | + `IEventHandler<SpawnRuleAuthoredByAdminEvent>`; the property-set branches log `BlueprintId` + `InstancesUpdated`; subscribed in `Server/Program.cs`, or in `CompositionRoot.SubscribeGameplayHandlers` if PT-1 merged first |
| `ShopkeeperSpawnHandler` | handler | **changed (XML doc only)** | records its dependency on `SpawnSystem` filling first (State 10 < Domain 20), and drops the stale "analogous to … `PlaceItemsInRooms`" |
| `ISpawnRuleAuthoring` (`Core/Modules/Authoring/Systems/`) | authoring interface, implemented by `ContentDefinitionCatalog` | **new** | `IReadOnlyList<SpawnSite> SpawnSitesFor(ContentKind kind, string blueprintId)`; `Task<ContentWriteResult> UpsertSpawnRuleAsync(string roomBlueprintId, SpawnRule rule, CancellationToken)`; `Task<ContentWriteResult> RemoveSpawnRuleAsync(string roomBlueprintId, string ruleId, CancellationToken)`. A separate interface leaves the `IContentDefinitionCatalog` test fakes untouched; DI forwards the singleton (slice-1 `IWorldManifestAuthoring` precedent) |
| `ContentDefinitionCatalog` | authoring system | **changed** | implements the above under `_writeGate`; area attribution comes from a rule-site map built in the same room sweep as `roomAreaMap`; the spawn-room cascade branches and the `CreateNextFrom` carry are removed |
| `ContentReferenceIndex` | authoring system | **changed** | the two `SpawnRoomBlueprintId` edges are removed; the spawn-rule field label becomes `SpawnRules[{rule.Id}]` |
| `ContentGenerationSystem` | authoring system | **changed** | emits rules on rooms |
| `TemplateConformanceSystem` | balance tooling | **changed** | stops copying the removed field |
| `MobDefinitionDto` / `MobDefinitionMapper` | JSON contract | **changed** | the `SpawnRoomBlueprintId` field is **removed**; `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` on the DTO, so a stale client sending it (or any unknown member) gets a 400; the OpenAPI document is regenerated |
| `RoomSpawnRulesEditor.razor` (`Hedron.Web/Components/Shared/`) | Blazor component | **new** | rows: id, blueprint picker (mobs + items), count, delay; add/remove; used by `RoomEditor` and the `AreaGridEditor` room panel; binds only (INV-8) |
| `MobEditor.razor` / `ItemEditor.razor` | Blazor pages | **changed** | "Spawn location" becomes a **Spawns in** panel (site list, add-to-room shortcut, remove) |

## Implementation plan — work packages

### WP1 — Rule-keyed slot model, one spawn truth, fail-fast shape (engine)

- **Scope:**
  - the `SpawnRule` shape, `SpawnRuleLimits`, `SpawnRuleIds` and `SpawnRuleValidation`;
  - `ContentShapeException`/`ContentLoadException`;
  - the deserializers (fail-fast) and writers (new shape, no spawn-room key);
  - removal of `SpawnRoomBlueprintId` from both templates, and its **Core compile fallout**: the catalog cascade branches, area attribution, `CreateNextFrom`, the reference-index edges, the fitter, the mapper and DTO (with `Disallow`), and the generator;
  - `ContentValidator`;
  - the `SpawnSystem` rewrite;
  - the loader's staged read and rooms/areas-only spawn;
  - `ReloadCommand` refusal and the `WorldContentBootstrap` log;
  - `MobBuilderSystem.CreateMob`/`ItemBuilderSystem.CreateItem` stop setting a spawn room (WP2 reshapes them).
- **Files:**
  - `Core/ECS/Components/{SpawnConfigComponent.cs, SpawnRuleLimits.cs, SpawnRuleIds.cs, SpawnRuleValidation.cs}`;
  - `Core/Systems/{ContentShapeException.cs, ContentLoadException.cs}`;
  - `Core/Modules/Spawn/Systems/{ISpawnSystem.cs, SpawnSystem.cs, SpawnSyncResult.cs, SpawnRuleStatus.cs}`;
  - `Core/Modules/Shopping/Handlers/ShopkeeperSpawnHandler.cs` (XML doc only);
  - XML docs only: `Core/Modules/Authoring/Systems/{IContentDefinitionCatalog.cs, IContentReferenceIndex.cs}`, `Core/Modules/Authoring/ContentReference.cs`;
  - `Core/Modules/World/{Templates/RoomTemplateDeserializer.cs, Systems/RoomContentWriter.cs, Systems/WorldContentLoader.cs, Systems/ContentValidator.cs}`;
  - `Core/Modules/Admin/Commands/ReloadCommand.cs`;
  - `Server/WorldContentBootstrap.cs`;
  - `Core/Modules/Mobs/{Templates/MobTemplate.cs, MobTemplateDeserializer.cs, Systems/MobContentWriter.cs, Systems/MobBuilderSystem.cs}`;
  - `Core/Modules/Items/{Templates/ItemTemplate.cs, ItemTemplateDeserializer.cs, Systems/ItemContentWriter.cs, Systems/ItemBuilderSystem.cs}`;
  - `Core/Modules/Authoring/{Systems/ContentDefinitionCatalog.cs, Systems/ContentReferenceIndex.cs, Systems/ContentGenerationSystem.cs, Contracts/MobDefinitionDto.cs, Contracts/MobDefinitionMapper.cs, ContentSummary.cs}`;
  - `Core/Modules/BalanceInspection/Systems/TemplateConformanceSystem.cs`;
  - `Hedron.Web/Hedron.Web_authoring.json` (regenerated OpenAPI contract);
  - tests under `Hedron.Tests/{Spawn, World, Authoring, Web/Api}/`. There is no `Hedron.Tests/Admin/`: `RoomBuilderSystemTests` and the admin-command flow tests live in `Hedron.Tests/Authoring/`.
  - **Existing tests to delete or rewrite onto rules:** the catalog cascade/attribution tests (`ContentDefinitionCatalogTests`), `ContentDefinitionCatalogAuthoringApiTests`, `ContentDefinitionCatalogCacheTests`, the reference-index edge tests (`ContentReferenceIndexTests`), `MobDefinitionMapperTests`, `TemplateConformanceSystemTests`, `RoomCoordinateRoundTripTests` (its inline `minCount`/`maxCount` YAML and `SpawnRule(…, 1, 3, …)` constructions), `WorldContentReloadTests`, and the `WorldContentLoaderTests` placement tests.
- **Docs:**
  - **`features/world/spawn-system.md`.** Rewrite:
    - the slot model, the subscription table, boot fill, `SyncRoom`/`DescribeRoom`, and the INV-31 posture (including the reload-teardown race guard);
    - fix the line-40 drift;
    - correct the "Self-initializing" consideration's INV-10 citation to the actual rationale: loader phase separation, a design choice with no INV;
    - replace the "Multi-count slots" item with the Stage-B pool note.
  - **`docs/architecture/06-persistence.md:16`**: the INV-21 coexistence wording becomes "`SpawnSystem` never claims".
  - **`features/mobs/mobs.md:16, 59`** and **`features/world/area-model.md:13`**: placement comes from room rules.
  - **`reference/handlers.md:180-186`**: `ShopkeeperSpawnHandler` records the fill-then-seed dependency on `SpawnSystem` (State 10).
  - **Flow-05**: a note naming the pre-existing reload-teardown vs heartbeat race as acknowledged debt under the backlog's "World-state threading model" entry (`backlog.md:38`), and the in-slice guard.
  - **`.claude/skills/add-core-system/SKILL.md`** (~line 84, the content-I/O notes; or the matching notes in `add-domain-system`): one line on the fail-fast posture. Deserializers throw `ContentShapeException` for shape errors; the loader's staged read collects them into one `ContentLoadException` that aborts boot or refuses `reload`; ordinary exceptions stay log-and-skip (INV-20).
  - **`features/world/world-content.md`.**
    - Content-file-shape rows: room `spawnRules`; mob/item without a spawn room.
    - A new **"Converting a pre-slice-2 world"** subsection: per legacy key, the YAML to write. It also covers the fail-fast posture and startup phase text.
  - **Other feature docs:** `features/world/world.md`; `features/mobs/mob-system.md:40`; `features/items/item-inventory-system.md:70`; `features/items/items.md`.
  - **Reference catalogs:**
    - `reference/components.md`;
    - `reference/systems.md`: the `SpawnSystem` row (add `IClock` and the API), and the `WorldContentLoader`, `ContentValidator`, `ContentReferenceIndex`, `ContentGenerationSystem` and `IContentDefinitionCatalog` rows (cascades, attribution, edges);
    - `reference/commands.md` (`reload` refusal), together with `ReloadCommand.LongDescription`.
  - **`features/admin-authoring/content-tooling.md`:** the edge list becomes 4; delete/rename cascade text; `CreateNextFrom`; the JSON DTO.
  - **XML docs:** `IContentDefinitionCatalog.cs:43, 90, 178`; `IContentReferenceIndex.cs:17-18`; `ContentReference.cs:12, 24, 42`; `MobTemplate.cs:165`.
  - **Flows:**
    - 01: the mermaid diagram **adds** a `SpawnSystem` fill node, which is absent today;
    - 05: the staged-read refusal branch and the race note;
    - 09 and 20;
    - 29: the `generate` and API legs;
    - flow-08's `reload` paragraph (~line 49) gains the refusal branch.
- **Depends on:** slice 1 merged.
- **Out of scope:**
  - authoring surfaces (WP2, WP3);
  - pools;
  - slot self-healing;
  - area respawn rate;
  - changing the log-and-skip posture for malformed YAML in general.
- **Exit criterion:** T1–T9, T13, T14, T19, T20, T21 green, plus `dotnet build`/`dotnet test` green.

### WP2 — Admin authoring (INV-18, INV-21)

- **Scope:**
  - the `RoomBuilderSystem` spawn-rule operations;
  - `SpawnRuleCommand`, `SpawnRuleAuthoredByAdminEvent`, and the audit subscription;
  - `mkmob`/`mkitem` via rules;
  - blueprint-keyed `Set*` on both builders, with the `setmob`/`setitem` fan-out.
- **Files:**
  - `Core/Modules/Admin/{Systems/IRoomBuilderSystem.cs, Systems/RoomBuilderSystem.cs, Commands/SpawnRuleCommand.cs, Events/SpawnRuleAuthoredByAdminEvent.cs, Handlers/AdminAuditHandler.cs, AdminModule.cs}`;
  - `Core/Modules/Mobs/{Systems/IMobBuilderSystem.cs, Systems/MobBuilderSystem.cs, Commands/MkMobCommand.cs, Commands/SetMobCommand.cs, Events/MobPropertySetByAdminEvent.cs}`;
  - `Core/Modules/Items/{Systems/IItemBuilderSystem.cs, Systems/ItemBuilderSystem.cs, Commands/MkitemCommand.cs, Commands/SetitemCommand.cs, Events/ItemPropertySetByAdminEvent.cs}`;
  - `Server/Program.cs` (subscription; PT-1 note in Systems);
  - tests under `Hedron.Tests/Authoring/` (room builder, `spawnrule`, `mk*` flows; the existing `MobBuilderSystemTests`/`ItemBuilderSystemTests` are there) and `Hedron.Tests/Modules/{Mobs, Items}/` (`SetMobCommandBandTests`, `SetitemCommandBandTests`, `SetitemValueCommandTests`, updated for the new event payload).
- **Docs:**
  - `features/admin-authoring/admin-commands.md`;
  - `features/admin-authoring/admin-authoring.md:11, 31, 35`:
    - `:11`: Apply refreshes only the web host's preview world; the live world applies on Server `reload`/restart;
    - `:31, 35`: add `spawnrule` and `SpawnRuleAuthoredByAdminEvent`;
  - `reference/commands.md`;
  - `reference/handlers.md` (`AdminAuditHandler`);
  - `reference/systems.md` (the builder rows);
  - flow-08;
  - **`.claude/skills/add-command/SKILL.md`** (INV-20):
    - **(a)** rewrite the `:162-168` worked example to the new `mkitem` shape: validate the room → `CreateItem(name)` → write item YAML → `IRoomBuilderSystem.AddSpawnRule(room, bp, 1, SpawnRuleLimits.DefaultRespawnDelaySeconds)` → write room YAML → publish `ItemCreatedByAdminEvent` + `SpawnRuleAuthoredByAdminEvent`;
    - **(b)** reword `:105` and `:157` from "the live entity" to "**the template plus every live world instance (never `PersistentEntity`)**";
    - **(c)** add one line to "Admin blueprint-authoring commands (INV-21)" on the spawn-rule mutation path: builder → template + live component + `SyncRoom`; the command writes YAML and publishes.
- **Depends on:** WP1.
- **Out of scope:** despawning detached instances.
- **Exit criterion:**
  - T10–T12, T15, T22 green;
  - the DI smoke resolves `SpawnRuleCommand`;
  - a manual telnet smoke: `spawnrule add` → kill → respawn.

### WP3 — Editor and catalog authoring (INV-18)

- **Scope:**
  - `ISpawnRuleAuthoring` on the catalog, with DI forwarding;
  - `RoomSpawnRulesEditor` in `RoomEditor` and the `AreaGridEditor` room panel;
  - the mob/item **Spawns in** panel.
- **Files:**
  - `Core/Modules/Authoring/{Systems/ISpawnRuleAuthoring.cs, Systems/ContentDefinitionCatalog.cs, SpawnSite.cs, AuthoringModule.cs}`;
  - `Hedron.Web/Components/Shared/RoomSpawnRulesEditor.razor`;
  - `Hedron.Web/Components/Pages/{RoomEditor, AreaGridEditor, MobEditor, ItemEditor}.razor`;
  - tests under `Hedron.Tests/Authoring/`.
- **Docs:** `features/admin-authoring/content-tooling.md` (spawn-rule authoring), `features/admin-authoring/content-authoring.md` (the editor sections), and flow-29 (the editor legs).
- **Depends on:** WP1. It can run in parallel with WP2.
- **Out of scope:**
  - new JSON-API endpoints for rules;
  - a multi-area Browser filter.
- **Exit criterion:**
  - T16–T18 green;
  - the DI smoke resolves `ISpawnRuleAuthoring`;
  - a manual smoke against a temp content dir: save rules in the room editor, in the grid panel, and through a mob's "Add to room"; run `reload` on the Server (telnet) → the mobs appear. Apply in the web host succeeds with no error.

**Primary agent:** once all three packages land, run `architecture-reviewer` (code mode) across the combined diff.

## Content tooling impact

*(INV-18.)*

**Changed data shape: room `spawnRules`** (world-content YAML only, INV-23):

```yaml
spawnRules:
  - id: mob.b1.rat              # rule identity; optional (default: blueprintId); required when two rules share a blueprint; always written
    blueprintId: mob.b1.rat     # a mob or item template
    count: 3                    # population; default 1; 1..20
    respawnDelaySeconds: 120    # per-slot cooldown; default 300; >= 0
```

**Hard break: these shapes are load errors.** They abort boot on both hosts, `reload` is refused, and the catalog editors can't load them. Each message names the file and says what to write instead:

| Old shape | Error says | Hand conversion |
|---|---|---|
| spawn rule with `minCount` / `maxCount` | replace with `count: N` | delete both keys; add `count:` (usually the old `maxCount`) and an `id:` if needed |
| mob `spawnRoomBlueprintId: room.X` (non-blank) | remove it; add a spawn rule to `room.X` | delete the key; add `- { blueprintId: <mob>, count: 1, respawnDelaySeconds: 300 }` to `rooms/room.X.yaml` |
| item `spawnRoomId: room.X` (non-blank) | same | same, in the item's room |
| blank `spawnRoomBlueprintId: ''` / `spawnRoomId: ''` | *(no error; ignored, and dropped on next write)* | none |

The conversion steps are documented in `world-content.md` ("Converting a pre-slice-2 world"). **No committed content needs conversion.**

**Authoring and inspection surfaces:**

| Surface | Author | Inspect | Applies |
|---|---|---|---|
| Room editor + grid-editor room panel (`RoomSpawnRulesEditor`) | add / edit / remove rules | rule list; save-time validation errors; broken-reference warnings | on Server `reload`/restart (Apply refreshes only the web host's preview world) |
| Mob / item editor **Spawns in** panel | "Add to room" (area→room picker, count, delay); remove a site | every room + rule that spawns this template | on Server `reload`/restart (Apply refreshes only the web host's preview world) |
| Admin `spawnrule add <blueprintId> [count] [delaySeconds] [id]` / `set <ruleId> <count\|delay\|blueprint> <value>` / `remove <ruleId>` | yes | confirmation (rule id, spawned/detached counts) | **live** (template + room component + slots) and durable (room YAML) |
| Admin `spawnrule` | — | per rule: id, blueprint, live/count, delay, next respawn | read-only |
| Admin `mkmob` / `mkitem` | template + count-1 rule in the current room | confirmation | live + durable |
| Admin `reload` / editor Apply | — | per-file shape-error list on refusal | whole or nothing |
| `generate` | emits rules on generated rooms | `ValidateEmitted` (same fail-fast deserializers) | on load |
| Hand-edited YAML | yes | boot / `reload` error list; `spawnrule`; editors | on `reload` / restart |

No `TemplateRegistry` kind is added. Rule instances spawn through `ITemplateRegistry.Spawn` (INV-12).

## Cross-cutting surfaces stressed

*(INV-19, INV-31, INV-17.)*

| Surface | Classification | Notes |
|---|---|---|
| **Commands** | Adequate (with a recorded trigger) | **Schema.** `spawnrule` declares `action` (optional `SpawnRuleAction` enum token, using the existing enum-prefix parse at `CommandArgumentParser.cs:136-153`) plus four optional string tokens `arg1..arg4`. The command validates per sub-action: `add <blueprintId> [count] [delaySeconds] [id]`, `set <ruleId> <count\|delay\|blueprint> <value>`, `remove <ruleId>`; no action means list. **This is not the `setmob` posture.** `setmob` has a fixed `<bp> <property> <value>` schema and switches only on the property's *value*; `spawnrule` is the engine's first verb whose argument *arity and types* depend on a sub-action. The parser has no sub-action schema support, so per-action arity checks are hand-rolled inside the command. **Trigger recorded:** promote sub-action schema support into the command framework at the **third** verb of this shape. Slice 1's `startroom [here]` is a candidate second (a single literal sub-action). Add this to the backlog as part of WP2. `reload` gains a refusal branch; its schema is unchanged. |
| **Output** | Adequate | `PlainMessage` with System/Confirmation/Error severity; the refusal lists one error line per file. |
| **Persistence** | Adequate | No new persistent state; see the audit below. |
| **Event bus / ordering (INV-6, INV-7)** | Adequate | **Events:** one new audit event on the established fan-in, plus two property-set payload changes (`BlueprintId` + `InstancesUpdated`, entity id dropped; T22). **Ordering:** `SpawnSystem`'s `WorldContentReadyEvent` handler moves to State (10), ahead of shop seeding (20); T5 pins this. The dependency is recorded on `ShopkeeperSpawnHandler`'s XML doc and on its `reference/handlers.md` row. A refused reload publishes neither post-load event. |
| **ECS queries** | Adequate | The fan-out matches `BlueprintComponent` by id and excludes `PersistentEntity`, the same exclusion as `BuildLiveBlueprintMap` (`WorldContentLoader.cs:418-428`). **No net new blueprint-scan site:** the fan-out *replaces* the existing first-match scans in `SetMobCommand.cs:85` and `SetitemCommand.cs:87` (now one per builder system), and the `SpawnSystem` init scan (`SpawnSystem.cs:90`) is **deleted** because claiming goes away. The count therefore does not move toward the backlog's "Blueprint-scan index" 12th-site trigger (`backlog.md:388`). |
| **Content templates (INV-12)** | Adequate | World mob/item instances now spawn only through `ITemplateRegistry.Spawn` (rules, shop stock, admin `spawn`). The loader's limbo spawn is gone. |
| **Content schema / validation** | **Gap exposed → fixed in-slice** | Rule shape had no validation home: a lenient deserializer, and `Validate(RoomTemplate)` returns `Ok` (`ContentValidator.cs:110-132`). It would otherwise need three hand-rolled checks. `SpawnRuleValidation` is the single home for the deserializer, the catalog and the builder. |
| **Content load failure posture** | **Gap exposed → fixed in-slice** | Boot and reload had no fail-fast path for content *shape*. Per-file failures are log-and-skip (`:157-168`), and reload tears down before reading (`:100-105`), so throwing in a deserializer would silently drop files, or half-apply a reload. The fix: a staged corpus read plus aggregated `ContentLoadException`, used by both boot and reload. The existing log-and-skip for malformed YAML is deliberately unchanged, and the shape-error class is opt-in by exception type. |
| **Admin mutation fan-out (INV-21)** | **Gap exposed → fixed in-slice** (resolved decision 1) | `count > 1` makes `setmob`/`setitem`'s "first live entity" (`SetMobCommand.cs:84-101`) update 1 of N, and ending limbo entities makes it refuse unplaced templates. The builder `Set*` methods become blueprint-keyed: the template plus every world instance, never `PersistentEntity`. |
| **JSON API contract** | Adequate (deliberate contract change) | `spawnRoomBlueprintId` is removed from `MobDefinitionDto`, and unmapped members are disallowed **on that DTO only**, so a stale client gets a 400 instead of a silent drop. Other kinds keep host-default JSON behavior. The OpenAPI document regenerates. |
| **Shopkeeper spawned outside `WorldContentReadyEvent`: till unseeded** | **Acknowledged debt** (resolved decision 3) | `ShopkeeperSpawnHandler` seeds the **till** (and spawns base stock) only on `WorldContentReadyEvent`. A shopkeeper spawned at any other time has an empty till: either a heartbeat respawn of a killed shopkeeper, or a `SyncRoom` spawn from `spawnrule add` of a shop mob or a count increase on its rule. **Base stock self-heals:** `ShopRestockTickHandler` → `IShopSystem.PlanRestock` (`ShopSystem.cs:133-158`) refills base stock on the heartbeat, so only the till stays at zero (the shop can sell, but cannot buy from players until a `reload`). **Reachability:** heartbeat respawn is unreachable while shopkeepers are `untargetable` (Phase 5 hub, R3). The admin path is reachable, and `reload` restores the till. The fix needs a spawn-completion seam, since `SpawnSystem` may not publish. **Backlog entry (added at WP1 close-out by the implementer):** *"🔵 Seed shop tills on non-boot spawn: `ShopkeeperSpawnHandler` seeds the till only on `WorldContentReadyEvent`, so a shopkeeper spawned by `SpawnSystem` outside it starts with an empty till (base stock self-heals via `ShopRestockTickHandler`/`PlanRestock`). That covers a heartbeat respawn of a killed, targetable shopkeeper, and `SyncRoom` via `spawnrule add` or a count increase on a shop mob's rule. Heartbeat path unreachable while shopkeepers are `untargetable` (Phase 5 hub, R3); admin path reachable, and `reload` restores the till. Needs a spawn-completion seam: `SpawnSystem` returns spawned ids but may not publish (INV-5), so either an Initiator-side notification (`spawnrule`/`mk*` can call `IShopSystem.SeedTill` on returned ids) or a heartbeat-side seeding path. Surfaced by spawn-authoring (Phase 5 slice 2)."* |
| **Time / randomness (INV-26)** | Adequate | Respawn timing uses the injected `IClock`; there is no randomness. Stage-B pools will need `IRandom`. |
| **Configuration** | N/A | Delays are per rule (R9). |
| **Sessions** | Adequate | — |
| **Modules / DI** | Adequate | `RoomBuilderSystem` → `ISpawnSystem` (lateral; precedent `IAreaSystem`). No cycle: `SpawnSystem` depends on `EntityService`, `ITemplateRegistry`, `IClock` and a logger. **New module edge:** `MkMobCommand`/`MkitemCommand` (Mobs/Items) → `IRoomBuilderSystem` (Admin). It is one-directional, because Admin references neither Mobs nor Items types. The rule helpers live in `Core/ECS/Components/`, so World, Admin and Authoring don't reference each other for them. `AuthoringModule` forwards `ISpawnRuleAuthoring`. |
| **Web host (INV-8)** | Adequate | Components only bind; id minting is in Core. The web host now **refuses to boot** on shape-error content, exactly like the Server (one posture). |
| **Concurrency (INV-31)** | Adequate (posture declared below) | No new thread, timer or background service. |
| **Flows (INV-17)** | Adequate | Existing flows are extended; no new recurring flow. |

**INV-31 posture:**
- **`SpawnSystem` tracker: guarded.** The tracker is reached from the heartbeat thread (tick; `MobDiedEvent` from combat) and from session threads (`ItemPickedUpEvent`; reload's `WorldContentReadyEvent`; now `SyncRoom` via admin verbs).
  - Every entry point takes one private `lock`: the four handlers, `SyncRoom` and `DescribeRoom`.
  - Nothing inside the lock re-enters `SpawnSystem`, and `ITemplateRegistry.Spawn` publishes nothing.
  - The ECS mutations it performs stay on the existing session-command and heartbeat paths. They fall under the acknowledged `ComponentRepository` debt, which this does not widen.
- **Reload teardown vs heartbeat respawn: pre-existing race, mostly closed in-slice.** `ReloadAsync` runs on a session thread while the heartbeat thread can fire `TryRespawn` against the old tracker. `DestroyWorldContent` runs before `WorldContentReadyEvent` rebuilds the tracker. A respawn in that window spawns a mob into a destroyed room: an orphan that `DestroyWorldContent` already passed over, and that the rebuilt tracker never sees.
  - This race exists **today**. It is acknowledged debt under the backlog's "World-state threading model" entry (`backlog.md:38`), and flow-05 gains a note.
  - **In-slice guard:** inside the tracker lock, `TryRespawn` skips any slot whose room entity no longer has a `RoomComponent`. Rooms are destroyed in the teardown, so a tick that lands after the room is gone spawns nothing.
  - **What remains:** a tick interleaved *between* individual `DestroyEntity` calls, with the room still alive and its mobs already gone. Closing that needs the world-state threading decision, so it stays with the backlog entry. T1 pins the guard.
- **Staged reload: confined.** The staging set is a local of one `ReloadAsync` call. The registry and the world are mutated only after a clean read, on the same caller thread as today (session for `reload`, circuit for Apply).
- **Catalog spawn-rule operations: guarded** by `_writeGate`. The new mutators take the gate once and call only `*Core` bodies.
- **`ISpawnRuleAuthoring.SpawnSitesFor`: lock-free read** over the catalog's existing read path. It derives from the cached room summaries and their rule-site map (built in the same sweep as `roomAreaMap`), under the catalog's snapshot-swap index (generation-checked publish). It takes no `_writeGate`, the same posture as `List`/`Load`.
- **Web host:** it never subscribes `SpawnSystem`. Its preview world holds areas and rooms only, and nothing in it reads mob/item entities.
- **Cross-process:** `spawnrule`/`mkmob` (Server) and the editor (Web) can both write a room file. This is the existing backlogged staleness, and is not widened.

**Persistence opt-in audit (INV-14 / INV-22 / INV-23):**
- **Level 1 — entity domains.**
  - Rule-spawned mobs and found items are **world content**: no `PersistentEntity`, fresh at boot and reload, no SQLite row.
  - Pickup promotion is unchanged: `ItemContextHandler` handles `ItemPickedUpEvent`, not the command, and drop demotes.
  - Shopkeepers stay world content.
- **Level 2 — components.**
  - `SpawnConfigComponent` (room world content) is **not** `[Persistent]`; the YAML is authoritative.
  - `LocationComponent` is set with both `RoomEntityId` and `RoomBlueprintId` on every spawn.
  - The tracker and builder results are runtime state only.
- **Level 3 — save-on-change.**
  - No `SaveEntityAsync` anywhere in this slice.
  - `ReloadCommand`'s existing `FlushAllAsync` still runs before the staged read, so a refused reload has flushed but changed nothing. That is harmless and unchanged.

## Flows introduced or modified

*(INV-17.)* The PR updates each flow file's body, and its mermaid diagram wherever the step appears.

- **[Flow 1 — Server startup](../architecture/flows/flow-01-server-startup.md)** (WP1):
  - The loader runs a staged corpus read that aborts boot with the aggregated shape-error report.
  - It spawns areas and rooms only.
  - `SpawnSystem` fills every slot on `WorldContentReadyEvent` at State, ahead of shop seeding. The mermaid diagram **adds a `SpawnSystem` fill node**: `SpawnSystem` does not appear in the diagram today.
  - The same abort applies to the web host's bootstrap.
- **[Flow 5 — Content reload](../architecture/flows/flow-05-content-reload.md)** (WP1):
  - A new step before teardown: stage and validate the corpus; on shape errors, refuse (error list, no teardown, no events).
  - Step 5 drops mob/item placement.
  - Step 6: `SpawnSystem` (first) rebuilds and refills.
  - The mermaid diagram gains the refusal branch.
  - The concurrency note names the reload-teardown vs heartbeat respawn race as pre-existing acknowledged debt (the "World-state threading model" entry), and records the in-slice `RoomComponent` guard.
- **[Flow 9 — Items journey](../architecture/flows/flow-09-item-pickup.md)** (WP1): pickup vacates the item's **rule slot**; the found item respawns after the rule delay (R12).
- **[Flow 20 — Death & respawn](../architecture/flows/flow-20-mob-death-respawn.md)** (WP1): the rule slot (room, rule id) is vacated, and respawn is per slot.
- **[Flow 8 — Admin authoring journey](../architecture/flows/flow-08-admin-room-creation.md)** (WP2):
  - Add the `spawnrule` leg.
  - `mkmob`/`mkitem` now author a count-1 rule.
  - The index-row title gains `spawnrule`.
- **[Flow 29 — Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md)** (WP1 + WP3):
  - the spawn-rules editor legs;
  - the "Spawns in" leg;
  - `generate` emits rules;
  - the edge-list note (~line 354) becomes 4 edges with the rule-id label;
  - the JSON-API mob shape.

No new canonical flow is needed.

## Test plan / Verification

*(INV-25; rubric in [07-testing.md](../architecture/07-testing.md).)*

| # | Tier | Target | Asserts | Postcondition |
|---|---|---|---|---|
| T1 | system-unit (FakeClock) | `SpawnSystemTests` (rewrite + extend) | **Boot fill:** a count-3 rule spawns 3 entities immediately, each with `RoomEntityId` + `RoomBlueprintId`; a pre-placed same-blueprint entity is not claimed. **Duplicates:** two rules with the same blueprint and distinct ids are both honored. **Vacancy:** killing 1 of 3 vacates only that slot; a tick before the delay does nothing; exactly one spawns at the delay; independent timers. **Items:** `ItemPickedUpEvent` behaves the same for item rules. **Robustness:** an unknown template disables the slot; a duplicate id in the component skips the second rule with a warning. **Rebuild:** a second `WorldContentReadyEvent` clears and refills. **Priority:** `Priority == HandlerPriority.State`. **Teardown guard:** a slot is due, then `RoomComponent` is removed from its room entity (simulating mid-reload teardown) → the tick spawns nothing, and no entity is created | 8, 10, 11 |
| T2 | system-unit | `SpawnSystemSyncTests` | **New rule:** spawns `Count`. **Count changes:** +2 spawns 2; −2 drops vacant slots first, and a detached entity stays alive while its `MobDiedEvent` schedules nothing. **Removed rule:** detaches. **Reorder:** no spawn, no detach, same ids. **Unchanged rule keeps its pending timers:** with one slot vacated at t0 (due t0+delay), a `SyncRoom` at t0+1 for an unrelated change leaves that slot's `NextRespawnAt` exactly t0+delay; no respawn before it, exactly one at it. **Delay/blueprint change:** applies at the next vacancy/spawn. **`DescribeRoom`:** values per rule id | 12, 13 |
| T3 | system-unit | `SpawnRuleIdsTests`, `SpawnRuleValidationTests` | **`DefaultFor`:** a 40-character blueprint is returned unchanged; a 70-character blueprint is truncated to exactly its first 60 characters, deterministically (same input, same output). **`Mint`:** default, then `-2`, `-3`, case-insensitive; a 70-character blueprint's `-2` is 63 characters and well-formed; it throws when `-2`…`-999` are all taken. **`IsWellFormed`:** bounds and charset. **Validation errors:** blank blueprint, malformed id, duplicate id (case-insensitive), count 0 or 21, negative delay. A valid list returns none | 2, 3, 6 |
| T4 | system-unit (temp dir) | `RoomSpawnRuleRoundTripTests` | **Round-trip:** write → read preserves id/blueprint/count/delay; the written file has no `minCount`/`maxCount`. **Defaults:** no `id` → `id == blueprintId`; no `count` → 1; no delay → 300 | 1, 2 |
| T5 | flow (temp content dir, real loader + registry + `SpawnSystem` + `ShopkeeperSpawnHandler` via `RecordingEventBus` dispatching in priority order) | `SpawnBootFlowTests` | **Boot:** a mob rule (count 2), an item rule (count 1) and a shopkeeper rule → after load + `WorldContentReadyEvent` the room holds 2 mobs, 1 item and the shopkeeper; the shopkeeper's till and stock are seeded; no mob/item lacks a `LocationComponent` (no limbo). **Reload:** same population with fresh ids; YAML rules reordered before reload → per-id populations unchanged | 7, 8, 9, 12 |
| T6 | system-unit (**throws**) | `RoomTemplateDeserializerShapeTests` | `Deserialize` throws `ContentShapeException`, with a message containing the room id, the rule's blueprint and the offending key, for: `minCount` present; `maxCount` present; blank blueprint; malformed id; duplicate explicit id; two id-less rules sharing a blueprint (derived-id collision); count 0; count 21; negative delay. **Does not throw** for an id-less rule whose blueprint id is 70 characters: its id is the deterministic 60-character `DefaultFor` value | 2, 3 |
| T7 | system-unit (**throws**) | `MobTemplateDeserializerShapeTests`, `ItemTemplateDeserializerShapeTests` | a non-blank `spawnRoomBlueprintId` (mob) / `spawnRoomId` (item) throws `ContentShapeException`; the message names the blueprint, the key and the room, and says to add a spawn rule there. A blank key deserializes cleanly; re-writing that template omits the key | 3 |
| T8 | flow (**throws**, temp dir) | `ContentLoadFailFastTests` | **Boot:** a corpus with one legacy-rule room, one legacy mob and valid content → `LoadAndSpawnAsync` throws `ContentLoadException` whose `Errors` lists **both** files with path prefixes; the registry is empty and no entity exists. **Generic failures:** a malformed-YAML file alone still logs and skips (posture unchanged). **Reload:** a valid world loaded, then a legacy file added → `ReloadAsync` throws; every pre-reload entity id still exists with identical components, `AllBlueprintIds()` is unchanged, and the `SpawnSystem` `DescribeRoom` output is unchanged | 4, 5 |
| T9 | flow | `ReloadCommandRefusalTests` | `reload` over a shape-error corpus → error output (one line per file); `RecordingEventBus` captured **no** `WorldContentReadyEvent` and **no** `ContentReloadedEvent`; the world is unchanged. A clean `reload` still publishes both (regression) | 5 |
| T10 | flow | `SpawnRuleCommandFlowTests` | **Add:** `spawnrule add mob.rat 3 60` → rule present in the template, the live component, the tracker (3 rats) and the room YAML; `SpawnRuleAuthoredByAdminEvent(Added)`; no persistence save. **Set:** `set <id> count 1` → 2 detached, YAML updated. **Remove:** the rule is gone (the component is removed with the last rule); the live rat dies → no respawn. **Refusals:** unknown blueprint, a room id as target, unknown rule id, count 0, not in a room → error, everything byte-identical, no event. **Read-only:** `spawnrule` with no argument | 14 |
| T11 | flow (+ update `MobBuilderSystemTests` / `ItemBuilderSystemTests`) | `MkMobMkItemRuleFlowTests` | **`mkmob`:** the template has no spawn-room field; room R's template, component and YAML gain a count-1 rule; the confirmed entity is tracked (kill → respawn after 300 s); both events captured. **`mkitem`:** symmetric (pickup → respawn). **Refusal first:** run `mkmob` in a location that is not a room, or a room with no registered template → error; **no** new template is registered, no mob or room YAML is written, no event. **Builders:** `CreateMob`/`CreateItem` create no entity | 15 |
| T12 | system-unit + flow | builder setter tests, `SetMobSetItemFanOutTests` | three world rats plus a persistent copy → `SetAttribute("mob.rat","hp",30)` updates the template and all three, and leaves the persistent copy untouched. With zero instances, the template and YAML are updated and the call succeeds. `setitem` is symmetric, including the player-owned-copy exclusion | 16 |
| T13 | system-unit | `ContentValidatorTests` (extend) | **Save path:** `Validate(RoomTemplate)` returns the `SpawnRuleValidation` errors, the same set as T6 minus the legacy keys, which cannot exist on a template. **Registry warnings:** `ValidateRegistry` warns on a rule naming an unknown template, and on a rule naming a room/area template | 6 |
| T14 | system-unit (existing, re-pinned) | `ContentGenerationSystemTests`, `GenerationRunModeTests` | every generated mob/item is referenced by exactly one count-1 rule in its room; no generated file has a spawn-room key; same-seed output is byte-identical (goldens deliberately re-pinned); `ValidateEmitted` is clean | 20 |
| T15 | architecture-guard (existing) | DI-smoke `CompositionRoot_resolves_all_commands_handlers_and_systems` | resolves `SpawnRuleCommand`, `RoomBuilderSystem` (new dependency) and `ISpawnRuleAuthoring` | — |
| T16 | system-unit (temp dir) | `SpawnRuleAuthoringTests` (against `ContentDefinitionCatalog`) | **Upsert:** blank id → minted; same id → replaced; count 0 → `Failed`, file byte-identical; unknown room → `Failed`. **Remove:** removes by id. **Sites:** `SpawnSitesFor(Mob, id)` lists rules across two rooms | 17 |
| T17 | system-unit (temp dir, existing extended) | `ContentDefinitionCatalogTests` | **Area attribution:** one area; two areas → ordinal-least room's area; none → `null`. **Rename:** rewrites the rule `BlueprintId` and keeps the `Id`. **Delete:** removes the mob's rules. **Reads:** `Load` of a legacy-key file surfaces the `ContentShapeException` message; `List` skips it with a warning (existing posture) | 18, 19, 3 |
| T18 | system-unit (extend existing) | `ContentDefinitionCatalogWriteSerializationTests.Every_public_mutator_completes_in_sequence_without_deadlock` | `UpsertSpawnRuleAsync` and `RemoveSpawnRuleAsync` added to the sequence; no deadlock under `_writeGate` | 17 |
| T19 | HTTP-integration (Tier 6, per-test temp content dir) | `AuthoringApiMobShapeTests` (extend the existing `Web/Api` suite) | POST and PUT `/mobs` with a body carrying `spawnRoomBlueprintId` → **400**, and no file is written or changed; GET `/mobs/{id}` JSON has no `spawnRoomBlueprintId` property; `/power/mob` with the field → 400. **Contract re-pin:** the checked-in OpenAPI document `Hedron.Web/Hedron.Web_authoring.json` is regenerated and committed in the same PR, so the CI OpenAPI drift gate (`.github/workflows/ci.yml`) stays green with the field removed | 22 |
| T20 | system-unit (existing, extended) | `ContentReferenceIndexTests` | exactly **4** declared edges: `Room.AreaId`, `Room.Exits[dir]`, `Area.Rooms[]`, `Room.SpawnRules[]`. The spawn-rule edge's `FieldLabel` is `SpawnRules[<rule id>]`, not keyed by blueprint. **No** edge has a `SpawnRoomBlueprintId` label (the old item/mob → room edge tests are deleted). The two-kind resolve/broken behavior is unchanged | 19 |
| T21 | system-unit (existing, rewritten) | `Hedron.Tests/Authoring/ContentDefinitionCatalogAuthoringApiTests.cs` (~:239+, `CreateNextFrom` region) | `CreateNextFrom_Mob_CarriesTierBandAndSpawnRoom_AndResetsTheRest` becomes `CreateNextFrom_Mob_CarriesTierBandXpScale_AndResetsTheRest`: Tier, Band and `XpScale` carry over, and the next definition has no spawn field (the property no longer exists, so the assertion is over the carried set) | 21 |
| T22 | flow (existing, extended) | `SetMobCommandBandTests`, `SetitemCommandBandTests`, `SetitemValueCommandTests` (`Hedron.Tests/Modules/{Mobs,Items}/`) + `SetMobSetItemFanOutTests` | the captured `MobPropertySetByAdminEvent` / `ItemPropertySetByAdminEvent` carries `BlueprintId` = the target blueprint and `InstancesUpdated` = the number of world instances updated: 3 with three world rats and a persistent copy; 0 with no live instances | 16 |

**Skipped (with reason):**
- **Blazor markup** (`RoomSpawnRulesEditor`, the Spawns-in panel). This is the presentation tier; the logic behind it is covered by T3 and T16–T17.
- **`Apply.razor`'s error display.** It is unchanged code that already catches the exception, and T8 covers the exception.
- **Exact message prose.** T6–T8 assert only the key, blueprint and file, not the full text.
- **Thin records:** `SpawnRuleAuthoredByAdminEvent`, `SpawnSyncResult`, `SpawnRuleStatus`, `ContentLoadException`.
- **`AdminAuditHandler`'s new and changed branches.** These are logging-only: a structured log line per event, with no decision, state change or output, and the rubric skips pure logging. Event *publication* and payloads, which are the observable contract, are covered by T10, T11 and T22.
- **The `SpawnSystem` lock.** No meaningful deterministic test; T1–T2 cover the behavior.
- **Persistence round-trip.** There is no new `[Persistent]` shape, and pickup promotion is unchanged and already covered.

**Testability gaps:** none. `SpawnSystem` takes `IClock` (FakeClock), the helpers are pure, the loader, catalog and writers take temp directories, and nothing draws randomness.

## Design notes

*(Slice-relevant rationale. The seam reasoning lives in the program's [Design notes](phase-5-content-baseline-program.md#design-notes) ("One spawn truth").)*

- **Mob/item spawn room is removed; the editors keep a shortcut that writes a room rule.** A validation warning would have left two placement mechanisms, plus the loader's limbo entities. Removing the field makes the rule the only runtime truth, and lets the loader stop spawning mob/item templates. The **Spawns in** panel keeps the "put this mob in that room" gesture. Items are included because `spawnRoomId` is the identical mechanism, and R12 needs it.
- **Hard break, not compatibility (user's call, 2026-10-29).**
  - The old shapes are **fail-fast load errors**: a non-blank `spawnRoomBlueprintId`/`spawnRoomId`, and `minCount`/`maxCount` on a rule. There is no translation, no silent `maxCount → count`, and no converter. The operator converts their world by hand, guided by errors that name each file and the YAML to write.
  - **Rationale.** The repo ships no world. A compatibility path would be permanent-until-removed debt across the deserializers, the loader, the reference index and the editors. A hard break keeps one schema and one truth from the first commit.
  - **Consistent posture.** The same deserializers run at boot (both hosts), on `reload`/Apply, in `generate`'s `ValidateEmitted`, and on catalog reads. Each therefore reports the same errors. The full error list comes back in one pass, so one boot attempt is the operator's complete worklist.
- **Blank legacy keys are tolerated.** Every pre-slice writer emitted `spawnRoomBlueprintId: ''` / `spawnRoomId: ''`, including for unplaced templates (`MobContentWriter.cs:121`, `ItemContentWriter.cs:78`). A blank key carries no placement and nothing to convert, so erroring on it would force busywork on every mob and item file without making anything more correct. It is ignored, and dropped on the next write.
- **All rule-shape problems are errors; reference problems stay warnings.** This is a planner extension of the hard-break posture.
  - Once the old rule shape is an error, a rule with count 0, a bad id or a duplicate id must not be silently skipped: that would change world population without telling anyone. Shape is local and unambiguous, so it fails fast.
  - A rule pointing at an unknown blueprint is a *reference*, and keeps the established warn-but-allow posture (`ContentReferenceIndex`, `ValidateRegistry`; `SpawnSystem` disables the slot).
  - The general log-and-skip for malformed YAML is unchanged: shape errors opt in through `ContentShapeException`, and changing the whole loader's posture is out of scope.
- **Reload is whole-or-nothing.** `ReloadAsync` used to tear down before reading (`:100-105`), so any fail-fast read would have left a half-built world. Staging the corpus read first makes refusal free: the world, registry and tracker are untouched.
- **JSON API: removed and rejected.** Removing the field alone would silently drop a stale client's value, because the host ignores unknown members (`Program.cs:64-69`). `[JsonUnmappedMemberHandling(Disallow)]` on `MobDefinitionDto` turns it into a 400. That matches the hard break, and makes the mob DTO a strict full-shape contract. It is scoped to that DTO, so other kinds' behavior doesn't change.
- **Rule identity is an id, unique within the room.** Position fails editor reordering. Keying by blueprint fails the duplicate-blueprint case, and fails a pool rule (Stage B) that has no single blueprint.
  - The id defaults to the blueprint id, so the common case reads naturally. Two rules on one blueprint must carry explicit ids (a load error otherwise), so identity never depends on file order.
  - **Id length (planner choice).** Blueprint ids have no length cap but rule ids do (64), so the default is **deterministically truncated** to 60 characters, leaving room for a `-N` (≤ `-999`) mint suffix. The alternative was to exempt derived defaults from the cap. Truncation was chosen because every id, derived or minted, then satisfies one `IsWellFormed` rule. A 70-character blueprint therefore never makes a room unloadable, and its id is stable across loads.
  - **`mkmob`/`mkitem` validate the room first**, so a refusal never leaves an orphan template file. That gives the `mk*` commands an `IRoomBuilderSystem` dependency (a one-way Mobs/Items → Admin edge).
  - The editor and admin paths always mint and write ids.
  - After a rename the id stays as it was: identity matters, readability is cosmetic.
  - Durable identity is `(roomBlueprintId, ruleId)`.
- **Count replaces Min/Max.** `SpawnSystem` never read `MinCount`/`MaxCount`, so there are no range semantics to keep. A random population would need `IRandom`, which belongs with the Stage-B pools.
- **Shaped for Stage B pools.** A pool rule is a `SpawnRule` whose blueprint *source* becomes a weighted pool, drawn per spawn through `IRandom`. Slots, vacancy, timers, ids, `SyncRoom` and authoring all key on the rule id, so pools touch only the "which blueprint" step.
- **Boot fill is immediate, inside `SpawnSystem`, at State priority.** Nothing else places world mobs or items now, so waiting a full delay would leave a fresh boot empty. Filling in its own handler keeps the loader's phases closed to spawn concerns. That is a design choice for phase separation, not an invariant. (`spawn-system.md` currently cites INV-10 for it, but INV-10 is the no-chain-initiator exception; the WP1 rewrite of that doc corrects the citation.) Running first (10) lets `ShopkeeperSpawnHandler` (20) find rule-placed shopkeepers.
- **No claiming.** Every world entity is destroyed before reload's `WorldContentReadyEvent`, or never existed at boot. That leaves only entities a slot must *not* own as claim candidates, such as persistent copies.
- **Shrinking detaches instead of despawning.** Despawning in front of players needs narration the engine lacks. A detached instance lives until it dies, or until `reload` resets the room.
- **`mkmob`/`mkitem` author a respawning count-1 rule** (300 s), because that is what a world mob or item now is. `spawnrule remove` undoes it.
- **Admin mutations stay in the builder.** `RoomBuilderSystem` owns the template, the live component and `SyncRoom` for all three callers (`spawnrule`, `mkmob`, `mkitem`), so the command layer has no hand-rolled triple (INV-19). Writers stay on the command side.
- **Area attribution uses the ordinal-least room.** It keeps a single `AreaBlueprintId`, so the Browser and API shape is unchanged. The **Spawns in** panel shows every site.
- **Interactions with other slices:**
  - *Slice 1:* the hub has no rules. Both slices edit `WorldContentLoader`; this one builds on `ResolveStartingRoom`. Slice 1's manifest re-read **runs after the staged read** (only on a clean read) and keeps its warn posture.
  - *Slice 3:* the out-of-designation audit iterates room rules, the only spawn truth.
  - *PT-1:* the subscription site moves to `CompositionRoot.SubscribeGameplayHandlers` if it merges first. PT-1's per-world composition gets fail-fast loading for free.

## Resolved decisions

*(User-confirmed 2026-10-29. Do not relitigate.)*

1. **INV-21 fan-out lands in this slice (WP2).** `setmob`/`setitem` become blueprint-keyed: they update the template and every live world instance, never player-owned copies. Rejected: backlogging the fan-out.
2. **Legacy content is a hard break.** A non-blank `spawnRoomBlueprintId` / `spawnRoomId`, and a rule's `minCount`/`maxCount`, are fail-fast load errors with an actionable per-file message. Boot aborts on both hosts and `reload` refuses as a whole. The operator converts by hand. The JSON field is **removed and rejected** (400). Rejected: load-time translation, lossless round-trip of the legacy key, the converter, a deprecated JSON passthrough. *Planner extensions under this posture:* all rule-shape errors are load errors; reference errors stay warnings; blank legacy keys are tolerated.
3. **Respawned-shopkeeper re-seed is acknowledged debt.** The backlog entry text is in the cross-cutting audit; the implementer adds it at WP1 close-out. The spec review corrected the scope: only the **till** stays unseeded, since base stock self-heals via restock, and the gap is also admin-reachable through `SyncRoom`.
4. **The spec-mode architecture review's findings are folded in, every one of them, with the reviewer's recommended option wherever it offered a choice (user-approved, 2026-10-29).** That covers:
   - B1–B3: the `add-command` skill edits; the T20/T21 test rows; the Server-`reload` smoke and Applies wording;
   - INV-6: property-set payloads drop the entity id;
   - INV-31: the reload-teardown race guard and the `SpawnSitesFor` read posture;
   - INV-7: the ordering is recorded on `ShopkeeperSpawnHandler`;
   - INV-19: the sub-action trigger and the blueprint-scan count;
   - the rule-id deterministic truncation;
   - `mk*` validate the room first;
   - the doc-drift list, test-path fixes, the helpers moved beside `SpawnRule`, and the nits.
5. **Adopted defaults stand:**
   - shrinking a rule detaches instances rather than despawning them;
   - `mkitem` creates a respawning item with a 300 s delay;
   - at most 20 per rule;
   - ordinal-least area attribution.

**Open questions:** none.

## Related

- [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md): the umbrella (R9, R12, R30; the "Spawn rule keyed by rule, with count" seam; resolved decision 4).
- [`dev-world-foundation.md`](dev-world-foundation.md): slice 1. A loader neighbor; its hub has no rules; precedents for the catalog interface and the audit event.
- [`tiered-overworld-program.md`](tiered-overworld-program.md): Stage B weighted area pools.
- [`playtest-world-substrate.md`](playtest-world-substrate.md): PT-1 subscription site; per-world composition.
- Features: [`spawn-system.md`](../features/world/spawn-system.md) · [`world-content.md`](../features/world/world-content.md) · [`world.md`](../features/world/world.md) · [`mob-system.md`](../features/mobs/mob-system.md) · [`item-inventory-system.md`](../features/items/item-inventory-system.md) · [`admin-commands.md`](../features/admin-authoring/admin-commands.md) · [`content-tooling.md`](../features/admin-authoring/content-tooling.md) · [`content-authoring.md`](../features/admin-authoring/content-authoring.md) · [`shop-system.md`](../features/economy/shop-system.md)
- Architecture: [`07-testing.md`](../architecture/07-testing.md) · [`checklist.md`](../architecture/checklist.md) (INV-5, 7, 10, 12, 17, 18, 19, 21, 22, 23, 25, 26, 31)
- Completed records: [`persistence-reform.md`](../roadmap/completed/persistence-reform.md) · [`blueprint-id-editing.md`](../roadmap/completed/blueprint-id-editing.md) · [`world-editor-grid.md`](../roadmap/completed/world-editor-grid.md)
- Backlog:
  - **New entries:** "Seed shop tills on non-boot spawn" (WP1; text in the cross-cutting audit); "Command sub-action schema support: promote at the 3rd sub-action verb" (WP2; trigger in the Commands row).
  - **Referenced:** "World-state threading model" (`backlog.md:38`, the reload-teardown race); "Blueprint-scan index" (`backlog.md:388`, no net new site).
