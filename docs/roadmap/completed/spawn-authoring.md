# Phase 5 slice 2 — Spawn authoring (completed)

> Implemented on branch `claude/spawn-authoring-plan-01de0c`, 2026-10-30. Program: [`phase-5-content-baseline-program.md`](../../implementation-plans/phase-5-content-baseline-program.md) (R9, R12, R30; the "Spawn rule keyed by rule, with count" seam; resolved decision 4). Living docs: [`spawn-system.md`](../../features/world/spawn-system.md) · [`world-content.md`](../../features/world/world-content.md#spawn-rules) · [`admin-commands.md`](../../features/admin-authoring/admin-commands.md) · [`content-tooling.md`](../../features/admin-authoring/content-tooling.md#ispawnruleauthoring--room-spawn-rules-from-the-editors).

## Outcome

Room spawn rules became the **single spawn truth**. A `SpawnRule` is now `(Id, BlueprintId, Count, RespawnDelaySeconds)`; `SpawnSystem` keys slots by `(room, rule id, ordinal)`, fills every slot immediately at boot (State priority, ahead of shop seeding), and respawns each slot on its own timer after death or pickup — so every world mob and found item respawns (R9, R12). The mob/item spawn-room field is gone: the loader spawns only areas and rooms. The change is a **hard break** — legacy spawn keys and the old `minCount`/`maxCount` rule shape are fail-fast load errors with a per-file conversion message, aborting boot on both hosts and refusing `reload` as a whole. Rules are authored and inspected through the admin `spawnrule` verb, `mkmob`/`mkitem` (now a count-1 rule), the room and grid editors' spawn-rules editor, and the mob/item editors' "Spawns in" panel; `setmob`/`setitem` fan out to every live world instance.

## Behavior digest

**Preconditions (as specified).** Slice 1 merged (manifest-aware `ResolveStartingRoom`, a rule-less dev-world hub). Before this slice: slots keyed `(room, blueprint)` with duplicate-blueprint rules dropped; initialization *claimed* any same-blueprint entity in the room; a rule without a pre-placed entity waited a full delay; `MinCount`/`MaxCount` were never read; the loader spawned every mob/item template (unplaced ones in "limbo") and placed them by spawn room; per-file load failures were log-and-skip and `reload` tore down before reading.

**Postconditions (as specified).**
1. Rule shape `(Id, BlueprintId, Count, RespawnDelaySeconds)`; YAML round-trips all four; the writer never emits `minCount`/`maxCount`.
2. A rule with no `id` reads `SpawnRuleIds.DefaultFor(blueprint)` (blueprint id truncated to 60 chars); a default-id collision is a load error; the id is always written.
3. `ContentShapeException` (message names blueprint, key, replacement) for: `minCount`/`maxCount`; a non-blank mob `spawnRoomBlueprintId` / item `spawnRoomId`; a blank blueprint, malformed/duplicate id, count outside 1..20, negative delay. A blank legacy key is ignored and dropped on next write.
4. Boot reads every file first; any shape error → `ContentLoadException` listing every file; nothing registered or spawned; host does not start (Server and Web). Malformed YAML stays log-and-skip.
5. `ReloadAsync` stages the whole corpus before teardown; on a shape error the world, registry and tracker are unchanged; `ReloadCommand` writes per-file errors and publishes neither `WorldContentReadyEvent` nor `ContentReloadedEvent`.
6. Deserializer and `ContentValidator.Validate(RoomTemplate)` enforce the same `SpawnRuleValidation`; `ValidateRegistry` warns on a rule naming an unknown / non-mob-item template.
7. The loader spawns only areas and rooms.
8. Boot fill on `WorldContentReadyEvent` at State (10): `Count` fresh entities per rule with `LocationComponent { RoomEntityId, RoomBlueprintId }`; never claims an existing entity.
9. A rule-placed shopkeeper is seeded by `ShopkeeperSpawnHandler` (Domain 20) in the same fan-out.
10. Two rules with the same blueprint and distinct ids are both honored.
11. Death/pickup vacates only that slot (`RespawnAt = now + delay`); independent timers; a due slot whose room lost its `RoomComponent` is skipped.
12. Slots key on `(room, rule id, ordinal)`; reordering rules changes nothing (reload or `SyncRoom`).
13. `SyncRoom` reconciles (new → spawn `Count`; increase → spawn delta; decrease/remove → drop vacant first, detach occupants; unchanged keeps timers; delay applies next vacancy, blueprint next spawn); `DescribeRoom` reports per rule.
14. `spawnrule add|set|remove` via `IRoomBuilderSystem` (template + live component + tracker), then room YAML + `SpawnRuleAuthoredByAdminEvent`; refusals change nothing; bare verb read-only.
15. `mkmob`/`mkitem` validate the room first, register a template with no spawn-room field, add a count-1 rule (template, component, YAML); the confirmed entity is the tracked instance.
16. `setmob`/`setitem` update the template and every live world instance (never `PersistentEntity`); succeed with zero instances; event payload `(AdminEntityId, BlueprintId, PropertyName, NewValue, InstancesUpdated)`.
17. `ISpawnRuleAuthoring` on the catalog: upsert (mint blank id, replace by id), remove, `SpawnSitesFor`; invalid → refused, nothing written.
18. Mob/item area attribution = area of the ordinal-least room whose rule names it, else `null`.
19. Rename rewrites a rule's `BlueprintId` keeping its `Id`; delete removes the rules; the reference index has 4 edges.
20. `generate` emits each mob/item as a count-1 rule; same-seed output byte-identical.
21. `CreateNextFrom(Mob)` carries Tier, Band, `XpScale`.
22. `MobDefinitionDto` has no `spawnRoomBlueprintId`; a body carrying it is a 400 (unmapped members disallowed); GET omits it.

**Main flow (summary).** Staged read → register → spawn areas/rooms → link → `WorldContentReadyEvent` → `SpawnSystem` fills (State) → shop seeding / hydration (Domain). Death/pickup vacates a slot; the heartbeat respawns due slots. Admin `spawnrule` / `mk*` go builder → template + component + `SyncRoom`, then the command writes YAML and publishes. `reload` refuses on bad content with nothing touched. Editors write rules through the catalog; changes reach the Server's live world on its `reload`/restart.

## Shipped pieces

| Surface | Location |
|---|---|
| `SpawnRule(Id, BlueprintId, Count, RespawnDelaySeconds)`; `SpawnRuleLimits`, `SpawnRuleIds`, `SpawnRuleValidation` | `Core/ECS/Components/` |
| `ContentShapeException`, `ContentLoadException` | `Core/Systems/` |
| `SpawnSystem` rewrite (rule-keyed tracker, State boot fill, no claiming, lock, teardown guard); `ISpawnSystem.SyncRoom`/`DescribeRoom`; `SpawnSyncResult`, `SpawnRuleStatus` | `Core/Modules/Spawn/Systems/` |
| Room deserializer/writer (`id`/`count`, legacy detection, validation); mob/item deserializers (non-blank legacy key → shape error) and writers (key dropped); `SpawnRoomBlueprintId` removed from `MobTemplate`/`ItemTemplate` | `Core/Modules/World/Templates/`, `Core/Modules/{Mobs,Items}/` |
| `WorldContentLoader` staged `ReadCorpusAsync`, rooms/areas-only spawn, placement helpers deleted | `Core/Modules/World/Systems/WorldContentLoader.cs` |
| `ContentValidator` room rule check + registry warnings | `Core/Modules/World/Systems/ContentValidator.cs` |
| `ReloadCommand` refusal; `WorldContentBootstrap` critical log + rethrow | `Core/Modules/Admin/Commands/`, `Server/` |
| `IRoomBuilderSystem` `TryGetAuthorableRoom` / `AddSpawnRule` / `UpdateSpawnRule` / `RemoveSpawnRule` (+ `SpawnRuleUpdate`, `SpawnRuleMutationResult`) | `Core/Modules/Admin/Systems/` |
| `SpawnRuleCommand` (+ `SpawnRuleAction`), `SpawnRuleAuthoringOutcome` (shared write-room + publish step for `spawnrule`/`mk*`, extracted after code review), `SpawnRuleAuthoredByAdminEvent` (+ `SpawnRuleChange`), audit subscription | `Core/Modules/Admin/`, `Server/Program.cs` |
| Blueprint-keyed `IMobBuilderSystem`/`IItemBuilderSystem` (`CreateX(name)` template-only; `Set*` → instance count); `MkMobCommand`/`MkitemCommand` via rules; `SetMobCommand`/`SetitemCommand` fan-out; property-set event payloads | `Core/Modules/{Mobs,Items}/` |
| `ISpawnRuleAuthoring` + `SpawnSite` on `ContentDefinitionCatalog`; rule-site area attribution; cascade/clone/`CreateNextFrom` cleanup | `Core/Modules/Authoring/` |
| Reference index: spawn-room edges removed, rule-id label | `Core/Modules/Authoring/Systems/ContentReferenceIndex.cs` |
| Generator emits count-1 rules; fitter stops copying the field | `ContentGenerationSystem.cs`, `TemplateConformanceSystem.cs` |
| `MobDefinitionDto` strict shape (`JsonUnmappedMemberHandling.Disallow`), mapper; regenerated OpenAPI | `Core/Modules/Authoring/Contracts/`, `Hedron.Web/Hedron.Web_authoring.json` |
| `RoomSpawnRulesEditor` (room editor + grid panel), `SpawnSitesPanel` (mob/item "Spawns in") | `Hedron.Web/Components/Shared/`, `Pages/{RoomEditor,AreaGridEditor,MobEditor,ItemEditor}.razor` |

## Tests shipped

All on the plan's Test plan; `dotnet test` green (1605 passing).

| # | Tier | Tests |
|---|---|---|
| T1 | system-unit (FakeClock) | `Spawn/SpawnSystemTests` (rewritten): boot fill + location, no claiming, duplicate blueprints, per-slot vacancy/timers, item pickup, unknown template, duplicate id, rebuild, State priority, teardown guard |
| T2 | system-unit | `Spawn/SpawnSystemSyncTests` (new rule, ±count, removed rule detaches, reorder, unchanged timers, delay/blueprint change, `DescribeRoom`) |
| T3 | system-unit | `Spawn/SpawnRuleIdsTests`, `SpawnRuleValidationTests` |
| T4, T6, T7 | system-unit (temp dir / throws) | `World/RoomSpawnRuleShapeTests.cs` — `RoomSpawnRuleRoundTripTests`, `RoomTemplateDeserializerShapeTests`, `SpawnRoomKeyShapeTests` |
| T5 | flow | `Spawn/SpawnBootFlowTests` (real loader + `SpawnSystem` + `ShopkeeperSpawnHandler`, priority dispatch; reload repopulates by id) |
| T8, T9 | flow (throws) | `Spawn/ContentLoadFailFastTests` — `ContentLoadFailFastTests`, `ReloadCommandRefusalTests` |
| T10–T12, T22 | flow | `Spawn/SpawnRuleCommandFlowTests.cs` — `SpawnRuleCommandFlowTests`, `MkMobMkItemRuleFlowTests`, `SetMobSetItemFanOutTests`; payload asserts in `SetMobCommandBandTests`, `SetitemCommandBandTests`, `SetitemValueCommandTests` |
| T11/T12 builders | system-unit | `Authoring/MobBuilderSystemTests`, `ItemBuilderSystemTests` reworked blueprint-keyed (+ fan-out / zero-instance / unknown-blueprint) |
| T13 | system-unit | `World/ContentValidatorTests` (room rule errors, registry warnings) |
| T14 | system-unit | `ContentGenerationSystemTests` (one count-1 rule per generated mob/item), `GenerationRunModeTests` |
| T15 | architecture-guard | existing DI smoke resolves `SpawnRuleCommand`, `RoomBuilderSystem`, `ISpawnRuleAuthoring` (all `.Systems` interfaces + `ICommand`s) |
| T16, T17 | system-unit (temp dir) | `ContentDefinitionCatalogTests` spawn-rule authoring + legacy-key read; attribution tests rewritten onto rules |
| T18 | system-unit | `ContentDefinitionCatalogWriteSerializationTests` sequence includes upsert/remove |
| T19 | HTTP-integration | `Web/Api/AuthoringApiMobShapeTests` (POST/PUT/power 400, GET has no field); OpenAPI re-pinned |
| T20, T21 | system-unit | `ContentReferenceIndexTests` (exactly 4 edges via a one-broken-per-edge fixture; rule-id label), `CreateNextFrom_Mob_CarriesTierBandXpScale_AndResetsTheRest` |

On-touch: `WorldContentReloadTests`, `WorldContentLoaderTests` (now "spawns only areas and rooms"), `RoomCoordinateRoundTripTests`, `RoomBuilderSystemTests` harness, `MobDefinitionMapperTests`, `TemplateConformanceSystemTests`, `ContentDefinitionCatalogCacheTests` moved onto rules.

## Decisions

- **Remove the mob/item spawn room; keep the gesture as a shortcut.** A warning would have left two placement mechanisms plus limbo entities. The rule is the only runtime truth; the "Spawns in" panel keeps "put this mob in that room". Items are included (identical mechanism; R12).
- **Hard break, not compatibility (user's call, 2026-10-29).** No translation, no silent `maxCount → count`, no converter. The repo ships no world; a compat path is permanent debt across deserializers, loader, index and editors. Errors name each file and the YAML to write; one boot attempt yields the complete worklist. **Blank legacy keys are tolerated** — every pre-slice writer emitted them, they carry no placement.
- **All rule-shape problems are errors; reference problems stay warnings.** Silently skipping a bad rule would change population unannounced. Unknown blueprints keep warn-but-allow (`ContentReferenceIndex`, `ValidateRegistry`; the slot is disabled). General malformed-YAML log-and-skip is unchanged — shape errors opt in by exception type.
- **Reload is whole-or-nothing** — staging the read before teardown makes refusal free.
- **JSON: removed and rejected.** The host ignores unknown members, so removal alone would silently drop a stale client's value; `Disallow` on `MobDefinitionDto` only makes it a 400.
- **Rule identity is an id unique in the room** — position fails reordering; blueprint keying fails duplicates and Stage-B pools. Default = blueprint id **deterministically truncated to 60** (room for a `-N` ≤ `-999` suffix under the 64 cap), chosen over exempting derived ids so every id satisfies one `IsWellFormed`. Two rules on one blueprint must carry explicit ids. A rename keeps the id. Durable identity `(roomBlueprintId, ruleId)`.
- **Count replaces Min/Max** (never read; random populations need `IRandom` → Stage B).
- **Shaped for Stage-B pools** — only the "which blueprint" step changes.
- **Boot fill is immediate, inside `SpawnSystem`, at State** — nothing else places mobs now; filling in its own handler keeps the loader closed to spawn concerns (a phase-separation choice, not INV-10 as the old doc claimed). State (10) < Domain (20) lets shop seeding find rule-placed shopkeepers.
- **No claiming** — everything world-side is destroyed before reload's ready event, so the only claim candidates were ones a slot must not own.
- **Shrinking detaches** rather than despawning (no narration for vanishing in front of players).
- **`mk*` author a respawning count-1 rule (300 s)** and validate the room first so a refusal leaves no orphan; this creates a one-way Mobs/Items → Admin (`IRoomBuilderSystem`) edge.
- **Admin mutations stay in the builder** (`RoomBuilderSystem` owns template + component + `SyncRoom`) so commands carry no hand-rolled triple (INV-19); writers stay command-side. The builder refuses room/area blueprints by type (World types) rather than requiring Mob/Item types, so Admin depends on neither module.
- **INV-21 fan-out lands here (resolved decision 1)** — with `count > 1`, "first live entity" would update 1 of N.
- **Ordinal-least area attribution** keeps one `AreaBlueprintId` (Browser/API shape unchanged).
- **INV-31:** tracker guarded by one lock; staged reload confined; catalog rule writes under `_writeGate`, `SpawnSitesFor` lock-free over the cached room sweep. The reload-teardown race is pre-existing; guarded in-slice by the `RoomComponent` check.
- **Spec-review findings (resolved decision 4)** were folded in wholesale (skill edits, test rows, race guard, read posture, ordering record, sub-action trigger, id truncation, room-first `mk*`).

## Deviations / Follow-ups

- **T3 arithmetic.** The plan said a 70-character blueprint's minted `-2` id is 63 characters; it is 62 (60 + `-2`). The test asserts 62.
- **`IRoomBuilderSystem.TryGetAuthorableRoom`** was added (not named in the plan) so `mk*` validate the room through the builder instead of re-implementing the room/template check.
- **`SpawnSitesPanel`** is a shared component used by both mob and item editors (the plan described the panel per page).
- **Code-review follow-ups (applied in-PR).** The architecture-reviewer code gate found the code clean and flagged doc drift (mob/item system docs, the systems catalog), all fixed; the third copy of "write room YAML + publish the rule event" was extracted into `SpawnRuleAuthoringOutcome` (INV-19); and the checklist's **INV-21** wording was amended to "template plus every live world instance (never `PersistentEntity`)" to match resolved decision 1.
- **Builders return `-1`** for a blueprint that is not a template of their kind (the plan specified only the instance count).
- **Empty `spawnRules:`** — removing the last rule leaves a null `spawnRules:` key in the room YAML (the writer emits nulls, like `exits`); harmless, reads as no rules.
- **Operator worlds need conversion.** Any pre-slice world with non-blank spawn-room keys or `minCount`/`maxCount` rules will not boot until converted ([guide](../../features/world/world-content.md#converting-a-pre-slice-2-world)).
- **Backlog:** "Seed shop tills on non-boot spawn" and "Command sub-action schema support: promote at the 3rd sub-action verb" added; the reload-teardown race recorded under "World-state threading model". No net new blueprint-scan site (the fan-outs replace the old first-match scans; `SpawnSystem`'s claim scan was deleted).
- **Manual smokes** (telnet `spawnrule add` → kill → respawn; editor save → Server `reload`) were not run in this session; the flow tests cover the same paths in-process.
