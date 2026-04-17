# ECS Migration Status

> Snapshot of where the ECS refactor stands. For the **forward plan** (how the code gets to the idealized API), see [api-alignment-plan.md](api-alignment-plan.md). For the **target design**, see [../architecture/02-ecs.md](../architecture/02-ecs.md).

## Summary

The ECS infrastructure exists as classes but is not yet live — the legacy inheritance-based entity system still runs the game. Phase 1 component extraction and Phase 1.5 archetype scaffolding are in the codebase; factory/locale migration (Phase 2) hasn't happened; systems and handlers don't exist.

| Phase | Scope | Status |
|---|---|---|
| 1 | Component extraction: `IComponent`, 13 core components, `ComponentRepository`, `EntityService` | **In repo, unused in live paths** |
| 1.5 | Entity archetype system: `EntityArchetype`, `ArchetypeRegistry`, `ArchetypeDefinition`, `EntityFactory` | **In repo, broken reference to `EcsManager.World`** |
| 2 | Factory + Locale migration; delete `DEPRECATED - Entities/`; kill `is`/`as` checks | **Not started** |
| 3 | Domain systems (Movement, Combat, etc.) as injectable classes | **Not started** |
| 4 | Commands routed via handlers through the event bus | **Not started** |
| 5 | Persistence moves to dirty-tracked flush | **Not started** |

## What exists in `Core/ECS/`

**Working surface** (implementations + tests compile):
- `IComponent.cs`
- `ComponentRepository.cs`
- `EntityService.cs` — the real owner of components (methods: `GetComponent`, `AddComponent`, `HasComponent`, `RemoveComponent`, `GetEntitiesWith`, `DestroyEntity`, `RegisterModule`, `GetModule`)
- `IModule.cs` — interface; no concrete implementations yet
- 22 component classes under `Core/ECS/Components/`
- `EntityArchetype.cs`, `ArchetypeRegistry.cs`, `ArchetypeDefinition.cs`

**Broken surface:**
- `EntityFactory.cs` references `EcsManager.World`, which does not exist. Factory is effectively unusable from live code paths.
- No `EntityService` instance is constructed or wired into DI anywhere.

**Legacy surface still active** (folder named `Core/ECS/DEPRECATED - Entities/`):
- `Entity`, `EntityAnimate`, `EntityInanimate`, `EntityContainer`
- `Player`, `Mob`, `ItemWeapon`, `ItemPotion`, `Storage`
- Used by `Core/Factory/*`, `Core/Locale/*`, `Core/Commands/*`, `Core/System/DataAccess.cs`

## Recently completed

**Phase 1 — Component extraction** (Nov 2025)
- Thirteen components extracted: Identity, Transform, Prototype, Effects, Attributes, Pools, Currency, Skills, PlayerData, PlayerConfiguration, Inventory, Qualities, MobData.

**Phase 1.5 — Entity hierarchy flattening + archetype system** (Dec 2024 per legacy doc)
- Additional item/container components added: ItemData, WeaponData, PotionData, Equipment, ContainerData, RoomData, AreaData.
- `EntityArchetype` enum + `ArchetypeRegistry` + `EntityFactory` scaffolded.

**Cache simplification** (Oct 2025)
- Unified dual cache dictionary.

## Known divergences from the idealized API

1. **`EcsManager.World` vs `EntityService`** — idealized docs use `EntityService` directly (DI-injected); current code calls a nonexistent global.
2. **No event bus** — `IEventBus` is the idealized cross-feature mechanism; current code uses direct calls.
3. **No domain systems as classes** — logic lives in entity methods, static helpers, and commands.
4. **Legacy hierarchy still active** — `DEPRECATED - Entities/` is a folder name, not a reality; those classes are the entities that run.
5. **Inheritance type checks everywhere** — `entity is Player`, `as Mob`, etc. Target is component queries (`HasComponent<PlayerDataComponent>`).

The full gap table and the sequenced plan to close it are in [api-alignment-plan.md](api-alignment-plan.md).

## Why we're not moving faster

- The codebase has no test suite, so every wave has to be hand-verified.
- Factory and locale migration (Wave 1 of the alignment plan) is the big unlock but touches every command.
- Modules (`IModule`) are the natural containment, but adopting them requires the event bus (Wave 2) to be present first.
