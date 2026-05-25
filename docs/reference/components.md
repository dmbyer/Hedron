# Components Reference

Living catalog of the ECS components **implemented** in Hedron. Update this file whenever a component is added, removed, or materially changed.

Source of truth: `Core/ECS/Components/` (cross-cutting) and `Core/Modules/<Feature>/Components/` (feature-owned).

> The full target component model (Identity, Transform, Attributes, Pools, item/room/area data, …) is design intent for future slices and lives in [`components-planned.md`](components-planned.md) — do not assume those types exist. Shape-level documentation only; for design rules (archetypes, persistence, effects) see [../architecture/02-ecs.md](../architecture/02-ecs.md). Why implemented and planned are separated: [`../documentation-architecture.md`](../documentation-architecture.md).

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
| `AreaComponent` | `AreaId`, `Name`, `Description`, `RespawnRate`, `Pvp` — minimal area metadata seeded by `AreaTemplate` | Area entities (Phase 3 slice 2) | yes |

### Gameplay / session (cross-cutting — `Core/ECS/Components/`)

| Component | Shape | Used by | Persisted? |
|---|---|---|---|
| `PlayerComponent` | `DisplayName`, `Session` (transient ref) | Player entity | no |
| `LocationComponent` | `RoomEntityId` (current room) | any mobile entity | yes |
| `RoomComponent` | `Name`, `Description`, `Exits` (`Dictionary<Direction, uint>`) | Room entity | yes (tagged `[Persistent]` in slice 5a) |
| `ItemDataComponent` | `Name`, `Description`, `Keywords` (`List<string>`), `ItemType`, `WornSlots` (`List<WornSlot>?` — null/empty = not wearable) | Item entity; read by `BroadcastSystem` (room description) and `ItemSystem` / `ItemBuilderSystem` / `EquipmentSystem` | yes |
| `InventoryComponent` | `ItemEntityIds` (`List<uint>`) — item entity ids carried by this entity | Player/mob entities; items in inventory have **no** `LocationComponent` — tracked here exclusively | yes |
| `EquipmentComponent` | `Slots` (`Dictionary<WornSlot, uint>`) — maps each occupied slot to an item entity id | Player/mob entities; cross-cutting so future mob code carries it without a domain dependency | yes |

### Module-owned (`Core/Modules/<Feature>/Components/`)

| Module | Component | Purpose | Persisted? |
|---|---|---|---|
| Account | `AccountComponent` | `Username` (lowercase-normalized), `PasswordHash` (PBKDF2-SHA256), `CharacterEntityIds`, `CreatedAtUtc` | yes |
| Account | `CharacterComponent` | `AccountEntityId`, `CharacterName`, `CreatedAtUtc`, `LastLoginUtc` | yes |

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
