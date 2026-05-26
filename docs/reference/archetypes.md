# Archetypes Reference

> **Target catalog — the archetype system is not yet built.** `EntityArchetype`, `ArchetypeRegistry`, and `ArchetypeDefinition` are marked *(target)* in [`../architecture/00-overview.md`](../architecture/00-overview.md); no archetype validation/detection ships today, and most components referenced below live in [`components-planned.md`](components-planned.md). This file is the **intended** composition catalog — design intent, not a description of running code. Re-audit against reality as gameplay slices land (tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md)). Why implemented and planned are separated: [`../documentation-architecture.md`](../documentation-architecture.md).

Catalog of every intended entity archetype. **Update this file whenever an archetype is added, removed, or its required/optional components change.**

Source of truth (target): `Core/ECS/EntityArchetype.cs` (enum), `Core/ECS/ArchetypeRegistry.cs` (definitions), `Core/ECS/ArchetypeDefinition.cs` (shape).

> Archetypes are a **validation and detection** tool — not a construction tool. Entities are built via `EntityService` / `TemplateRegistry`, not by passing an archetype to a factory. See [../architecture/02-ecs.md](../architecture/02-ecs.md).

---

## Archetype list

| Archetype | Purpose | Required components | Optional components |
|---|---|---|---|
| `Player` | PC-controlled living entity | Identity, Transform, PersistentEffects, TransientEffects, Attributes, Pools, Currency, Skills, Qualities, Inventory, Equipment, PlayerData, PlayerConfiguration | — |
| `Mob` | NPC living entity | Identity, Transform, PersistentEffects, TransientEffects, Attributes, Pools, Currency, Skills, Qualities, Inventory, Equipment, MobData | Faction*, Dialogue*, BehaviorTree* |
| `Weapon` | Equippable damage-dealing item | Identity, Transform, ItemData, WeaponData | PersistentEffects* |
| `Armor` | Equippable defensive item | Identity, Transform, ItemData | PersistentEffects* |
| `Potion` | Consumable with pool restoration / effects | Identity, Transform, ItemData, PotionData | — |
| `StaticItem` | Furniture, decoration, non-interactable | Identity, Transform, ItemData | — |
| `Consumable` | Generic consumable (non-potion) | Identity, Transform, ItemData | — |
| `Room` | Traversable location inside an area | Identity, Transform, ContainerData, RoomData, Inventory | PersistentEffects* |
| `Area` | Collection of rooms | Identity, Transform, ContainerData, AreaData | — |
| `World` | Top-level world container | Identity, Transform, ContainerData | — |
| `Storage` | Chest, corpse, persistent container | Identity, Transform, ContainerData | — |
| `Inventory` | Internal carried-container marker | Identity, Transform, ContainerData | — |
| `Portal` | Non-standard exit between rooms | Identity, Transform | — |
| `Trigger` | World-interaction hook entity | Identity, Transform | — |
| `Custom` | Escape hatch — no archetype contract | — | — |

> **Slices 8/8a partial implementation — Mob:** Required components delivered so far: `MobDataComponent` + `LocationComponent` + `AttributesComponent` + `PoolsComponent`; `BlueprintComponent` and `PersistentEntity` present on all template-spawned instances. Player entities also carry `AttributesComponent` and `PoolsComponent` from slice 8a. Archetype registry not yet built (target-state); `InventoryComponent`/`EquipmentComponent` on mobs and the combat-specific components will be added in the combat slice.

\* Optional components, asterisked. Note: most **required** components listed above are also not yet built — see [`components-planned.md`](components-planned.md) and the target-catalog banner at the top of this file.

---

## Validation

`archetypeRegistry.Validate(entityId, EntityArchetype.Weapon)` asserts that the entity has every required component for the named archetype. Use in editor tooling, in debug asserts at system boundaries, and in tests.

```csharp
Debug.Assert(archetypes.Validate(entityId, EntityArchetype.Weapon),
    $"Expected {entityId} to be a Weapon archetype");
```

Validation does not care about optional components — they may or may not be present.

---

## Detection

`archetypeRegistry.Detect(entityId)` inspects components and returns the best-matching archetype. Prefer `HasComponent<T>` queries in normal handler/system code; reach for `Detect` only when a handler is given an entity of genuinely unknown archetype (editor tools, generic inspection, debug commands).

```csharp
EntityArchetype detected = archetypes.Detect(entityId);
```

Detection order in `ArchetypeRegistry` must match the specificity of required components (Weapon before Armor before StaticItem, Mob before any general living-entity check, etc.). When adding an archetype with overlapping required sets, verify detection order explicitly.

---

## How to add a new archetype

Use the `add-archetype` skill — see `.claude/skills/add-archetype/SKILL.md`. Short version:

1. Add a value to the `EntityArchetype` enum.
2. Register a new `ArchetypeDefinition` in `ArchetypeRegistry` with explicit required/optional component types.
3. Update detection order in `ArchetypeRegistry` if the required set overlaps with an existing archetype.
4. Update this catalog (add a row above, including any `*`-marked new components).
5. Write a validation test: construct an entity → `Validate(entityId, EntityArchetype.X)` → true; remove a required component → false.

Invariants to preserve:
- The archetype definition is declarative. Construction code lives in the feature that needs it (`TemplateRegistry`, `ItemGeneratorSystem`, `PlayerCreationSystem`), not in the registry.
- Required components must be present for `Validate` to return true.
- Optional components may come and go at runtime.
- The `Custom` archetype is the escape hatch for entities that deliberately don't match any standard composition.
