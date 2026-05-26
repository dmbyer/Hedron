# Use Case: Attributes and Vitals

**Status:** planned
**Actors:** Player, Administrator, System
**Module:** `Core/Modules/Attributes/` (new); `Core/ECS/Components/` (cross-cutting components); `Core/Modules/Mobs/` (`MobTemplate` and builder extended); `Core/Modules/Account/` (`CreateCharacterAsync` and `CharacterHydrationHandler` extended)

---

## Description

Introduces the data layer for entity health and base combat statistics. Every living entity — player or mob — gains an `AttributesComponent` (Level, Strength, Dexterity, Constitution) and a `PoolsComponent` (MaxHp, CurrentHp). These two components are the minimum foundation the combat slice requires: without HP, damage has nowhere to land; without base stats, attack and defense calculations have no ground truth. This slice deliberately stops at the data layer — it seeds and exposes values, but performs no damage application or death detection. Those belong to the combat slice.

The `score` command gives players visibility into their own stats. Admin tooling extends `setmob` with attribute properties and introduces a `setplayer` admin command for direct stat manipulation during testing.

**Prerequisite:** Slice 8 (mobs) complete. `MobDataComponent`, `MobTemplate`, and `IMobBuilderSystem` must exist.

---

## Preconditions

- Slices 1–8 complete. Reused: `EntityService`, `IEventBus`, `IPersistenceSystem`, `IOutputWriter`, `ICommandDispatcher`, `IAdminAuthorizer`, `AdminRequirement`, `ISessionManager`, `MobDataComponent`, `MobTemplate`, `IMobBuilderSystem`, `IMobContentWriter`, `CharacterComponent`, `AccountSystem.CreateCharacterAsync`, `CharacterHydrationHandler`, `WorldContentLoader`.
- Mobs have `MobDataComponent`, `LocationComponent`, `BlueprintComponent`, and `PersistentEntity` from slice 8.
- Players have `CharacterComponent` and `LocationComponent` from slice 5.

---

## Postconditions

- `AttributesComponent` (`Level: int`, `Strength: int`, `Dexterity: int`, `Constitution: int`) exists under `Core/ECS/Components/` and is `[Persistent]`.
- `PoolsComponent` (`MaxHp: int`, `CurrentHp: int`) exists under `Core/ECS/Components/` and is `[Persistent]`.
- `AccountSystem.CreateCharacterAsync` attaches default `AttributesComponent { Level=1, Strength=10, Dexterity=10, Constitution=10 }` and `PoolsComponent { MaxHp=100, CurrentHp=100 }` to every new character, saved as part of the existing `LoginFlow` save call.
- `CharacterHydrationHandler` attaches empty-default `AttributesComponent` and `PoolsComponent` to existing characters that lack them, without immediately saving (migration guard — same pattern as `InventoryComponent`).
- `MobTemplate` gains optional `Level: int`, `MaxHp: int`, `Strength: int`, `Dexterity: int`, `Constitution: int` fields. `MobTemplate.Apply` attaches `AttributesComponent` and `PoolsComponent` from template values; if `Level` is absent or 0, defaults (Level 1, Str/Dex/Con 10, MaxHp/CurrentHp 100) are used.
- `setmob <blueprintId> level <n>`, `setmob <blueprintId> hp <n>`, `setmob <blueprintId> str <n>`, `setmob <blueprintId> dex <n>`, `setmob <blueprintId> con <n>` mutate `AttributesComponent` / `PoolsComponent` on the live mob entity and update the template YAML via `IMobContentWriter`.
- Admin `setplayer <characterName> level <n>` / `setplayer <characterName> hp <n>` sets attributes/pools on a currently-connected player's entity by character name.
- `score` displays the invoking player's Level, HP (`CurrentHp/MaxHp`), Strength, Dexterity, and Constitution in a formatted `ScoreDisplayMessage`. No events fired.
- `IAttributeSystem` (domain system, `Core/Modules/Attributes/Systems/`) exposes `GetLevel(entityId)`, `GetMaxHp(entityId)`, `GetStrength(entityId)`, `GetDexterity(entityId)`, `GetConstitution(entityId)` as the read seams the combat slice will call; mutation methods (`SetLevel`, `SetMaxHp`, `SetAttribute`) serve the admin path.

---

## Main Flow

### Flow A-1 — New character creation (attribute initialization)

1. `AccountSystem.CreateCharacterAsync` (existing): allocates entity, attaches `CharacterComponent`, `LocationComponent`, `PersistentEntity`.
2. **Extended here:** also attaches `AttributesComponent { Level=1, Strength=10, Dexterity=10, Constitution=10 }` and `PoolsComponent { MaxHp=100, CurrentHp=100 }`.
3. Saved via the existing `LoginFlow` `SaveEntityAsync(characterEntityId)` call — no additional save needed.

### Flow A-2 — Existing character hydration (migration guard)

1. `CharacterHydrationHandler.HandleAsync(WorldContentReadyEvent)` (existing): iterates all hydrated character entities.
2. **Extended here:** for each character entity, if `AttributesComponent` is absent, attaches default. If `PoolsComponent` is absent, attaches default. Does not call `SaveEntityAsync` immediately — the components are attached to the live entity and persisted on the character's next save-on-change event.

### Flow A-3 — Mob entity spawn with attributes

1. `WorldContentLoader` calls `MobTemplate.Apply(entity, entityService)` for each newly-spawned mob.
2. **Extended here:** `MobTemplate.Apply` attaches `AttributesComponent` from template values (defaults if `Level == 0`) and `PoolsComponent { MaxHp, CurrentHp = MaxHp }`.
3. Saved immediately as part of the existing `SpawnMissingEntities` save pass.

### Flow B-1 — `score`

1. Player sends `score`. `ScoreCommand.ExecuteAsync` has no privilege requirement.
2. Reads `CharacterComponent`, `AttributesComponent`, `PoolsComponent` from invoker entity. If either component is absent (pre-hydration edge case), uses default values rather than writing an error.
3. Builds `ScoreDisplayMessage(CharacterName, Level, CurrentHp, MaxHp, Strength, Dexterity, Constitution)`. Writes to invoker. No events fired.

### Flow C-1 — Admin `setmob <blueprintId> level/hp/str/dex/con <value>`

1. **Privilege gate.** `AdminRequirement` checked.
2. **Resolve mob.** Looks up blueprint in `ITemplateRegistry` and live entity in `EntityService` via `BlueprintComponent.BlueprintId`. Not found → error.
3. **Mutation.** Calls `IMobBuilderSystem.SetAttribute(mobEntityId, template, property, value)` (new method added to the builder). For `hp`: sets `PoolsComponent.MaxHp`; if `CurrentHp > new MaxHp`, clamps `CurrentHp = MaxHp`. For `level`, `str`, `dex`, `con`: sets the corresponding `AttributesComponent` field. Updates the corresponding field on `MobTemplate`.
4. **YAML write.** Calls `IMobContentWriter.WriteAsync(template)`.
5. **Event + save.** Publishes `MobPropertySetByAdminEvent` (reused from slice 8; `PropertyName` carries the attribute key). Calls `IPersistenceSystem.SaveEntityAsync(mobEntityId)`. Writes a confirmation `PlainMessage`.
6. `AdminAuditHandler` logs the event.

### Flow C-2 — Admin `setplayer <characterName> level/hp <value>`

1. **Privilege gate.** `AdminRequirement` checked.
2. **Resolve player.** `SetPlayerCommand` iterates `ISessionManager.GetAll()` and finds the session whose `CharacterComponent.CharacterName` matches (case-insensitive). Not found → "No connected player named '<name>'."
3. **Mutation.** Calls `IAttributeSystem.SetLevel(entityId, value)` or `IAttributeSystem.SetMaxHp(entityId, value)`; clamps `CurrentHp` to new `MaxHp` if needed.
4. **Event + save.** Publishes `PlayerAttributeSetByAdminEvent(AdminEntityId, PlayerEntityId, PropertyName, NewValue)`. Calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)`. Writes a confirmation `PlainMessage`.
5. `AdminAuditHandler` logs the event.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `MobPropertySetByAdminEvent` | `SetMobCommand` (extended from slice 8) | `uint AdminEntityId, uint MobEntityId, string PropertyName, string NewValue` | Reused from slice 8; attribute changes on mobs share the existing audit event |
| `PlayerAttributeSetByAdminEvent` | `SetPlayerCommand` | `uint AdminEntityId, uint PlayerEntityId, string PropertyName, string NewValue` | Audit log; future stat-recalculation hooks |

---

## Systems / Handlers Involved

| Artifact | Kind | Responsibility |
|---|---|---|
| `IAttributeSystem` / `AttributeSystem` | Domain system | Read-only getters for `AttributesComponent` / `PoolsComponent`; mutation methods for admin and initialization paths (event publication is callers' responsibility) |
| `IMobBuilderSystem` (extended) | Domain system | Gains `SetAttribute(mobEntityId, template, property, value)` for `level`, `hp`, `str`, `dex`, `con` |
| `AccountSystem` (extended) | Domain system | `CreateCharacterAsync` attaches default `AttributesComponent` + `PoolsComponent` |
| `CharacterHydrationHandler` (extended) | Handler | Attaches default `AttributesComponent` + `PoolsComponent` to existing characters that lack them |
| `MobTemplate` (extended) | Infrastructure | Gains `Level`, `MaxHp`, `Strength`, `Dexterity`, `Constitution` fields; `Apply` attaches both components |
| `SetMobCommand` (extended) | Command | Handles five new properties: `level`, `hp`, `str`, `dex`, `con` |
| `SetPlayerCommand` | Command (new) | Admin command to set `level` or `hp` on a currently-connected player by character name |
| `ScoreCommand` | Command (new) | Player command; reads and displays `AttributesComponent` + `PoolsComponent` |
| `AdminAuditHandler` (extended) | Handler | Subscribes to `PlayerAttributeSetByAdminEvent` |

---

## Content Tooling Impact

**`MobTemplate` YAML extension.** `kind: mob` files gain optional attribute fields:

```yaml
kind: mob
blueprintId: mob.forest.wolf
name: A grey wolf
description: A lean wolf with silver-tipped fur.
keywords:
  - wolf
type: Creature
spawnRoomBlueprintId: room.forest.clearing
level: 3
maxHp: 45
strength: 12
dexterity: 14
constitution: 11
```

Omitting any attribute field causes `MobTemplate.Apply` to use defaults (Level 1, Str/Dex/Con 10, MaxHp 100, CurrentHp 100). This is a purely additive YAML extension — existing `kind: mob` files without attribute fields remain valid.

**Admin commands.** Five new `setmob` sub-properties: `level`, `hp`, `str`, `dex`, `con`. One new admin command: `setplayer <characterName> level <n>` / `setplayer <characterName> hp <n>`.

**Player command.** `score` — no args, no privilege requirement.

**Inspect / verify.** Players use `score` to confirm their own stats. Admins use `setmob <blueprintId> level <n>` and read the confirmation echo to verify mob attributes.

---

## Cross-cutting Surfaces Stressed

| Surface | Assessment |
|---|---|
| **Commands** | Adequate — `ICommand`, `AdminRequirement`, argument parsing, `IOutputWriter`, and `CommandExecutedEvent` are all established. `score` is a zero-arg player command; `setplayer` is an admin command with two positional tokens. No new command infrastructure needed. |
| **Output** | Gap exposed (minor, resolvable in this slice) — `ScoreDisplayMessage` does not exist. A new message shape and a corresponding formatter case are added. No output infrastructure changes required — purely additive. |
| **ECS** | Adequate — component attach/query patterns are established. Two new `[Persistent]` cross-cutting components follow existing conventions. |
| **Persistence** | Adequate — two-level model is in place. Both components are `[Persistent]`; entities that carry them already have `PersistentEntity`. No persistence infrastructure changes. |
| **Event bus** | Adequate — one new event type (`PlayerAttributeSetByAdminEvent`) follows the established thin-payload pattern. `AdminAuditHandler` subscribes. |
| **Content templates** | Gap exposed (minor, resolvable in this slice) — `MobTemplate` and `MobTemplateDeserializer` gain new optional fields. No template infrastructure changes; additive-only to the YAML schema. |
| **Broadcast** | Not stressed. `score` is a single-player write; `setplayer` confirmation goes to the admin only. |
| **Time / heartbeat** | Not stressed. No timed or periodic behavior introduced. |
| **Configuration** | Not stressed. No new config keys. |

---

## Flows Introduced or Modified

Flow numbering dependency: the mobs slice (slice 8, prerequisite) must have added Flow 15 (`mkmob — admin mob creation`) to `flows/README.md` before this slice closes. If that is not yet done at implementation time, the flows doc must be reconciled — Flow 16 (`score`) is stable only once Flow 15 is written.

| # | Flow | Change |
|---|---|---|
| 1 | Server startup | Extended: `MobTemplate.Apply` now attaches `AttributesComponent` + `PoolsComponent` for newly-spawned mobs. Mermaid: add a `Note over MobTemplate,ES: also attaches AttributesComponent + PoolsComponent` annotation inside the `SpawnMissingEntities` loop, after the `Spawn(blueprintId)` call. |
| 7 | Login / character flow | Extended: `AccountSystem.CreateCharacterAsync` attaches `AttributesComponent` + `PoolsComponent` to new characters. Mermaid: update the `Note over AccSys` in step 6 to read `creates entity, attaches CharacterComponent + LocationComponent + AttributesComponent + PoolsComponent + PersistentEntity`. |
| 16 | `score` (new) | New flow in `flows/README.md`: player sends `score`; `CommandDispatcher` routes to `ScoreCommand`; `ScoreCommand` calls `EntityService.TryGet<AttributesComponent>` and `TryGet<PoolsComponent>`; writes `ScoreDisplayMessage` via `IOutputWriter`. No events. |

---

## Reference Catalog Updates

- `docs/reference/components.md` — add `AttributesComponent` and `PoolsComponent` rows to the cross-cutting table.
- `docs/reference/components-planned.md` — promote `AttributesComponent` and `PoolsComponent` to `components.md` (with the adopted `Strength`/`Dexterity`/`Constitution` naming); remove or update the planned-catalog entries (`Might`/`Finesse`/`Will`) so they no longer imply the superseded design is in force.
- `docs/reference/systems.md` — add `IAttributeSystem` / `AttributeSystem` row. **Pre-condition:** the mobs slice (slice 8) must have added the `IMobBuilderSystem` row first; update that row to add `SetAttribute` once it exists.
- `docs/reference/commands.md` — add rows for `score` and `setplayer`. **Pre-condition:** the mobs slice (slice 8) must have added `mkmob` and `setmob` rows first; update the `setmob` row to record the five new sub-properties (`level`, `hp`, `str`, `dex`, `con`) once it exists.

---

## Design Notes

- **`AttributesComponent` and `PoolsComponent` are cross-cutting.** Both live under `Core/ECS/Components/` so `Core/Modules/Combat/` can read them without depending on a domain module. This mirrors `EquipmentComponent` (cross-cutting since slice 7).
- **Direct-set, no formula.** `MaxHp` is set directly by the template or admin command — no derived formula in this slice. The combat slice will introduce a formula once stat relationships are validated through play. Starting with direct-set avoids locking in numbers that need to change. When a formula is introduced, `CreateCharacterAsync` and `MobTemplate.Apply` will need updating — acknowledged debt.
- **`setplayer` is test tooling.** In production, character stats would be driven by level-up events. For the current phase (no progression system), `setplayer` provides a manual override path for test scenarios and admin intervention. Protected by `AdminRequirement`.
- **`hp` sets `MaxHp`, clamps `CurrentHp`.** When an admin sets `hp <n>` on a mob, `MaxHp` is updated and `CurrentHp` is clamped to `min(CurrentHp, n)`. Setting `hp` does not heal the mob to full — healing belongs to the respawn or heal-command slice.
- **Migration guard does not save.** `CharacterHydrationHandler` attaches missing components without calling `SaveEntityAsync`. This matches the established pattern from `InventoryComponent` (slice 6) and avoids persistence I/O during the startup sequence.
- **`score` shows own stats only.** There is no `look <player>` or `inspect <mob>` stat display in this slice. A dedicated `inspect` admin command is acknowledged debt.
- **`IAttributeSystem` mutation methods publish no events (INV-5).** Event publication is the caller's (command's) responsibility. `SetLevel` and `SetMaxHp` mutate components only; the calling command publishes the appropriate audit event.
- **`IMobBuilderSystem.SetAttribute` follows the same INV-5 discipline (INV-5).** `SetAttribute` mutates the live entity's `AttributesComponent` / `PoolsComponent` fields and updates the corresponding field on the in-memory `MobTemplate` record. It does not call `IEventBus`, `IPersistenceSystem`, or `IMobContentWriter`. YAML writing, entity saving, and event publishing are all the calling command's responsibility.
- **`CurrentHp` clamping lives in the domain system (INV-8).** The rule "if `CurrentHp > new MaxHp`, clamp `CurrentHp = MaxHp`" is enforced inside `IMobBuilderSystem.SetAttribute` (and `IAttributeSystem.SetMaxHp`), not in the command body. Commands are thin; game rules belong in domain systems.
- **Stat naming deviates from `components-planned.md`.** The planned catalog (`docs/reference/components-planned.md`) listed `Might`, `Finesse`, `Will` as the stat names. This slice uses `Strength`, `Dexterity`, `Constitution` — the conventional MUD stat set — because they are directly meaningful to players and align with the combat model the design targets. The planned catalog was written before the combat model was designed; the deviation is intentional and supersedes the planned names.
- **Default values.** Level 1, Str/Dex/Con 10, MaxHp 100, CurrentHp 100. These are placeholders for the combat slice to calibrate against.

---

## Related

- [`mobs.md`](mobs.md) — slice 8; provides `MobDataComponent`, `MobTemplate`, `IMobBuilderSystem`, and `IMobContentWriter` extended here.
- [`equipment.md`](equipment.md) — slice 7; `EquipmentComponent` established the cross-cutting component precedent.
- [`account-character-creation.md`](account-character-creation.md) — slice 5; `CreateCharacterAsync` and `CharacterHydrationHandler` extended here.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; both new components follow the two-level persistence model.
- [`output-framework.md`](output-framework.md) — slice 4; `ScoreDisplayMessage` plugs into the same formatter pipeline.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
