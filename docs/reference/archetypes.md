# Archetypes Reference

Living catalog of every entity archetype. **Update this file whenever an archetype is added, removed, or its required/optional components change.**

Source of truth: `Core/ECS/EntityArchetype.cs` (enum), `Core/ECS/ArchetypeRegistry.cs` (definitions), `Core/ECS/ArchetypeDefinition.cs` (shape).

> For design rules and the prototype/instance model, see [../architecture/02-ecs.md](../architecture/02-ecs.md).

---

## Archetype list

| Archetype | Purpose | Required components | Optional components |
|---|---|---|---|
| `Player` | PC-controlled living entity | Identity, Transform, Prototype, Effects, Attributes, Pools, Currency, Skills, Qualities, Inventory, Equipment, PlayerData, PlayerConfiguration | — |
| `Mob` | NPC living entity | Identity, Transform, Prototype, Effects, Attributes, Pools, Currency, Skills, Qualities, Inventory, Equipment, MobData | Faction*, Dialogue*, BehaviorTree* |
| `Weapon` | Equippable damage-dealing item | Identity, Transform, Prototype, Effects, ItemData, WeaponData | — |
| `Armor` | Equippable defensive item | Identity, Transform, Prototype, Effects, ItemData | — |
| `Potion` | Consumable with pool restoration / effects | Identity, Transform, Prototype, Effects, ItemData, PotionData | — |
| `StaticItem` | Furniture, decoration, non-interactable | Identity, Transform, Prototype, Effects, ItemData | — |
| `Consumable` | Generic consumable (non-potion) | Identity, Transform, Prototype, Effects, ItemData | — |
| `Room` | Traversable location inside an area | Identity, Transform, Prototype, Effects, ContainerData, RoomData, Inventory | — |
| `Area` | Collection of rooms | Identity, Transform, Prototype, Effects, ContainerData, AreaData | — |
| `World` | Top-level world container | Identity, Transform, Prototype, Effects, ContainerData | — |
| `Storage` | Chest, corpse, persistent container | Identity, Transform, Prototype, Effects, ContainerData | — |
| `Inventory` | Internal carried-container marker | Identity, Transform, Prototype, Effects, ContainerData | — |
| `Portal` | Non-standard exit between rooms | Identity, Transform, Prototype, Effects | — |
| `Trigger` | World-interaction hook entity | Identity, Transform, Prototype, Effects | — |
| `Custom` | Escape hatch — no archetype contract | — | — |

\* Components marked with an asterisk are planned but not yet implemented — see [components.md](components.md).

---

## Detection

`EntityFactory.GetEntityArchetype(entityId)` inspects components and returns the best match. Use this in preference to tracking archetype in a field:

```csharp
EntityArchetype archetype = EntityFactory.GetEntityArchetype(entityId);
bool isValid = EntityFactory.ValidateEntityArchetype(entityId, EntityArchetype.Weapon);
```

Detection order in `ArchetypeRegistry` should match the specificity of required components (Weapon before Armor before StaticItem, Mob before Player-data check, etc.). When adding an archetype with overlapping required sets, verify detection order explicitly.

---

## How to add a new archetype

Use the `add-archetype` skill — see `.claude/skills/add-archetype/SKILL.md`. Short version:

1. Add a value to `EntityArchetype` enum.
2. Register a new `ArchetypeDefinition` in `ArchetypeRegistry` with explicit required/optional component types.
3. Update `EntityFactory.AddArchetypeComponents` with the construction logic.
4. Update detection order in `ArchetypeRegistry.DetectArchetype` if required components overlap with existing archetypes.
5. Update this catalog (add a row above, including any `*`-marked new components).
6. Write a factory-level test: create → `ValidateEntityArchetype` → true.

Invariants to preserve:
- Required components must be present at construction and never removed.
- Optional components may come and go at runtime.
- Every archetype has Identity + Transform + Prototype + Effects except `Custom`.
