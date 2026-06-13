# Components Reference

Living catalog of the ECS components **implemented** in Hedron. Update this file whenever a component is added, removed, or materially changed.

Source of truth: `Core/ECS/Components/` (cross-cutting) and `Core/Modules/<Feature>/Components/` (feature-owned).

> The full target component model (Identity, Transform, Attributes, Pools, item/room/area data, …) is design intent for future slices and lives in [`components-planned.md`](components-planned.md) — do not assume those types exist. Shape-level documentation only; for design rules (archetypes, persistence, effects) see [../architecture/02-ecs.md](../architecture/02-ecs.md). Why implemented and planned are separated: [`../architecture/09-documentation.md`](../architecture/09-documentation.md).

---

## Persistence — two-level model

Persistence uses two independent opt-ins. See [../architecture/06-persistence.md](../architecture/06-persistence.md) for the full model.

**Level 1 — entity opt-in:** an entity is saved only if it carries the `PersistentEntity` marker component. Entities without it are never written to disk, regardless of which other components they have.

**Level 2 — component inclusion:** `[Persistent]` on a component *type* tells `PersistenceSystem` to include that component in the snapshot for entities that are already opted in. The **Persisted?** column below marks which components carry the attribute.

---

## Implemented components

### Infrastructure (cross-cutting — `Core/ECS/Components/`)

| Component | Shape | Used by | Persisted? |
|---|---|---|---|
| `PersistentEntity` | *(zero-data marker)* — opts the entity into persistence | any entity that must survive restart | yes (self-referential: the marker is `[Persistent]` so it round-trips) |
| `BlueprintComponent` | `BlueprintId : string` — records the authored template id this entity was spawned from | every templated entity (Phase 3 slice 2+) | yes |
| `AreaComponent` | `AreaId`, `Name`, `Description`, `RespawnRate`, `Pvp` — minimal area metadata seeded by `AreaTemplate` | Area entities (Phase 3 slice 2) | no — world content; always fresh-spawned from YAML |
| `EntityStateComponent` | `ActiveStates: EntityStateFlags` — tracks active state flags for any entity; absent when `ActiveStates == None` | `IEntityStateService`; commands and handlers that guard on entity state | no — transient; cleared on restart by design |
| `EntityStateFlags` | `[Flags]` enum `{ None=0, InCombat=1, Resting=2, Incapacitated=4 }` — co-located with `EntityStateComponent` in `Core/ECS/Components/`; named with `Flags` suffix to avoid a C# namespace/type collision with the `EntityState` module | n/a (enum, not a component) | n/a |
| `CombatStateComponent` | `OpponentEntityId: uint` — holds the entity id of this entity's current combat opponent. Companion to `EntityState.InCombat`: the flag records that combat is active; this component records who the entity is fighting. Absent when not in combat. | `ICombatSystem`, `FleeCommand`, `CombatTickHandler` | no — transient; stale combat state on restart would reference entities that may not exist |
| `SpawnConfigComponent` | `Rules: List<SpawnRule>` — each `SpawnRule` holds `BlueprintId`, `MinCount`, `MaxCount`, `RespawnDelaySeconds`; one entry per independent respawn slot. Absent if the room has no spawn rules. | Room/area entities; `SpawnSystem` reads on `WorldContentReadyEvent` to populate its tracker | no — YAML is the authoritative source; always attached via `RoomTemplate.Apply` |

### Gameplay / session (cross-cutting — `Core/ECS/Components/`)

| Component | Shape | Used by | Persisted? |
|---|---|---|---|
| `PlayerComponent` | `DisplayName`, `Session` (transient ref) | Player entity | no |
| `LocationComponent` | `RoomEntityId` (`uint`, `[JsonIgnore]` — runtime entity ID resolved at startup from blueprint) + `RoomBlueprintId` (`string?` — stable cross-restart room reference stored in SQLite) | any mobile entity | yes (`[Persistent]` on class; `RoomEntityId` excluded via `[JsonIgnore]`, `RoomBlueprintId` included) |
| `RoomComponent` | `Name`, `Description`, `Exits` (`Dictionary<Direction, uint>`), `AreaEntityId` (`uint`, runtime-only, default 0 — entity id of the area this room belongs to; 0 = unassigned; set by `WorldContentLoader.LinkRoomAreas` on startup and by `IAreaSystem.AssignRoomToArea` at runtime; NOT `[Persistent]`) | Room entity | no — world content; always fresh-spawned from YAML |
| `ItemDataComponent` | `Name`, `Description`, `Keywords` (`List<string>`), `ItemType`, `WornSlots` (`List<WornSlot>?` — null/empty = not wearable), `DamageBonus: int` (default 0 — flat attack bonus when equipped in MainHand) | Item entity; read by `BroadcastSystem` (room description) and `ItemSystem` / `ItemBuilderSystem` / `EquipmentSystem` / `StatSystem` | yes |
| `InventoryComponent` | `ItemEntityIds` (`List<uint>`) — item entity ids carried by this entity | Player/mob entities; items in inventory have **no** `LocationComponent` — tracked here exclusively | yes |
| `EquipmentComponent` | `Slots` (`Dictionary<WornSlot, uint>`) — maps each occupied slot to an item entity id | Player/mob entities; cross-cutting so future mob code carries it without a domain dependency | yes |
| `MobDataComponent` | `Name`, `Description`, `Keywords` (`List<string>`), `MobType` — cross-cutting so combat, AI, and dialogue modules can read mob names without a domain dependency | Mob entities; read by `BroadcastSystem` (room description) | yes |
| `MobType` | Enum `{ None, Vendor, Guard, Creature }` — classification only; no behavior routing in this slice | Co-located with `MobDataComponent` in `Core/ECS/Components/` | n/a (enum, not a component) |
| `AttributesComponent` | `Level: int`, `Mind: int`, `Body: int`, `Spirit: int`, `Attunement: int` — base attributes for any living entity; defaults Level 1, all attributes 10. `Level` is vestigial (superseded by Ascension tier in slice S8). Read/written via `IAttributeSystem`; see [`../features/character-stats/attribute-system.md`](../features/character-stats/attribute-system.md). | Player and mob entities | yes |
| `PoolsComponent` | `MaxHp: int`, `CurrentHp: int`, `MaxMana: int`, `CurrentMana: int`, `MaxStamina: int`, `CurrentStamina: int`, `MaxAstra: int`, `CurrentAstra: int` — four resource pools (HP/Mana/Stamina/Astra). `SetMaxX` clamps `CurrentX` to new `MaxX` (INV-8). Governance (Mana↔Mind, Stamina↔Body, Astra↔Attunement) recorded in `IStatRegistry`; derivation is a later progression concern. Read/written via `IAttributeSystem`; see [`../features/character-stats/attribute-system.md`](../features/character-stats/attribute-system.md). | Player and mob entities | yes |
| `ResourceType` | Enum `{ Hp, Mana, Stamina, Astra }` — identifies a resource pool by type; the expandable seam for future pools. Co-located with `PoolsComponent` in `Core/ECS/Components/`. | `IStatRegistry` governance metadata; future effect/ability consumers | n/a (enum, not a component) |

### Module-owned (`Core/Modules/<Feature>/Components/`)

| Module | Component | Purpose | Persisted? |
|---|---|---|---|
| Account | `AccountComponent` | `Username` (lowercase-normalized), `PasswordHash` (PBKDF2-SHA256), `CharacterEntityIds`, `CreatedAtUtc` | yes |
| Account | `CharacterComponent` | `AccountEntityId`, `CharacterName`, `CreatedAtUtc`, `LastLoginUtc` | yes |
| Effects | `EffectsComponent` | `List<Effect> Effects` — active timed/permanent effects on an entity. `[Persistent]`, lifetime-filtered `JsonConverter` (`EffectsComponentJsonConverter`) serializes only `UntilRemoved` effects — timed effects are transient by design. Located in `Core/ECS/Components/` (cross-cutting). | yes |
| Death | `RespawnComponent` | `RoomBlueprintId: string?` — stable blueprint id of the room where this entity respawns after death. `null` means "use the world starting room" (fallback in `IDeathSystem.Respawn`). Stores the blueprint id rather than the runtime entity id because room entity ids are not stable across restarts. Attached to every new character by `AccountSystem.CreateCharacterAsync`. Set by `SetRespawnCommand` (admin boundary save, INV-22). Located in `Core/ECS/Components/` (cross-cutting). | yes |
| Abilities | `AbilitiesComponent` | `Known: List<string>` — ability ids the entity has learned. `CooldownRemaining: Dictionary<string, float>` — per-ability cooldown in seconds remaining. `[Persistent]` with `AbilitiesComponentJsonConverter`: `Known` is durable and persists across restarts; `CooldownRemaining` is transient (resets to ready on load). Located in `Core/ECS/Components/` (cross-cutting). | yes (`Known` only — `CooldownRemaining` excluded by converter) |
| Aspects | `AspectAffinitiesComponent` | `AffinityWeights: Dictionary<AspectId, int>` — normalized aspect composition for outgoing damage typing (empty, or positive weights summing to 100). `BaseResistances: Dictionary<AspectId, int>` — independent per-aspect base resistance [0, 100] (100 = full immunity). Both are compute-on-read via `IAspectSystem` — not cached. Serialized by `AspectId` name (never ordinal, INV-23) via the global `JsonStringEnumConverter`. Attached empty to every new character by `AccountSystem.CreateCharacterAsync`. Located in `Core/ECS/Components/` (cross-cutting). Co-located `AspectId` (enum) and `AspectCategory` (enum) are in `Core/Modules/Aspects/AspectId.cs`. | yes |

---

## How to add a new component

Use the `add-component` skill — see `.claude/skills/add-component/SKILL.md`. Short version:

1. Decide if it's cross-cutting or module-owned.
2. Add the `.cs` file under the correct folder (name `*Component.cs`).
3. Implement `IComponent` with **pure data only** — no logic.
4. Decide persistence — two questions (see [../architecture/06-persistence.md](../architecture/06-persistence.md)):
   - **Should entities of this type survive restart?** → controlled by `PersistentEntity` on the *entity*, not by this component. Decide when defining the construction path for this archetype.
   - **If the entity IS saved, should this component's data be included?** → tag the class `[Persistent]` if yes; leave untagged if it's transient (session ref, cached lookup, derived state). Transient is the right default for session-only state.
5. Update this catalog (add a row to the appropriate table, including the **Persisted?** column).
6. If required by an archetype, update `Core/ECS/ArchetypeRegistry.cs` and [archetypes.md](archetypes.md).

Invariants to preserve:
- Components are pure data. No methods that perform work.
- Do not call back into `EntityService` / `EcsManager.World` from a component.
- Components may be struct or class. Prefer class (current codebase convention) unless profiling calls for struct.
- If a stat can be modified by gear or effects, the component stores the **base** value only; systems compute effective values on read by summing base + effects.
