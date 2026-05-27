# Archetypes Reference

> **Implementation status.** `EntityArchetype`, `IArchetypeRegistry`, `ArchetypeRegistry`, and `ArchetypeDefinition` are **implemented** (Phase 3 slice 9). The registry currently covers five archetypes (Mob, Player, Room, Area, StaticItem); the remaining rows in the table below are the intended target composition. Re-audit and add registry definitions as slices land (tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md)). Why implemented and planned are separated: [`../documentation-architecture.md`](../documentation-architecture.md).

Catalog of every intended entity archetype. **Update this file whenever an archetype is added, removed, or its required/optional components change.**

Source of truth (target): `Core/ECS/EntityArchetype.cs` (enum), `Core/ECS/ArchetypeRegistry.cs` (definitions), `Core/ECS/ArchetypeDefinition.cs` (shape).

> Archetypes are a **validation and detection** tool — not a construction tool. Entities are built via `EntityService` / `TemplateRegistry`, not by passing an archetype to a factory. See [../architecture/02-ecs.md](../architecture/02-ecs.md).

---

## Archetype list

| Archetype | Purpose | Required components | Optional components |
|---|---|---|---|
| `Player` ✓ | PC-controlled living entity | `CharacterComponent`, `AttributesComponent`, `PoolsComponent`, `InventoryComponent`, `EquipmentComponent` | — |
| `Mob` ✓ | NPC living entity | `MobDataComponent`, `AttributesComponent`, `PoolsComponent` | `InventoryComponent`*, `EquipmentComponent`* |
| `Weapon` | Equippable damage-dealing item | `ItemDataComponent`, `WeaponDataComponent`† | `PersistentEffectsComponent`†* |
| `Armor` | Equippable defensive item | `ItemDataComponent`, `ArmorDataComponent`† | `PersistentEffectsComponent`†* |
| `Potion` | Consumable with pool restoration / effects | `ItemDataComponent`, `PotionDataComponent`† | — |
| `StaticItem` ✓ | Furniture, decoration, non-interactable | `ItemDataComponent` | — |
| `Consumable` | Generic consumable (non-potion) | `ItemDataComponent` | — |
| `Room` ✓ | Traversable location inside an area | `RoomComponent` | — |
| `Area` ✓ | Collection of rooms | `AreaComponent` | — |
| `World` | Top-level world container | *(not yet in registry)* | — |
| `Storage` | Chest, corpse, persistent container | *(not yet in registry)* | — |
| `Inventory` | Internal carried-container marker | *(not yet in registry)* | — |
| `Portal` | Non-standard exit between rooms | *(not yet in registry)* | — |
| `Trigger` | World-interaction hook entity | *(not yet in registry)* | — |
| `Custom` | Escape hatch — no archetype contract | — | — |

✓ Wired in `ArchetypeRegistry.BuildDefinitions()` — required components reflect the as-built implementation.
Rows without ✓ are target compositions; add a `BuildDefinitions` entry and update this table as slices land.
† Component not yet implemented; see [`components-planned.md`](components-planned.md).
\* Optional — may be added after construction.

> **Note on structural components.** `LocationComponent`, `BlueprintComponent`, and `PersistentEntity` are construction/persistence markers present on most template-spawned entities; they are not listed as archetype required components because they are cross-cutting infrastructure, not archetype identity. Mob `InventoryComponent`/`EquipmentComponent` are optional in the registry and planned for mob construction in a future slice (tracked in backlog).

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
5. Validate by constructing an entity and calling `Validate(entityId, EntityArchetype.X)` → true; remove a required component → false. (No automated test framework yet — see [`../roadmap/backlog.md`](../roadmap/backlog.md); manual verification in a running server is the current substitute.)

Invariants to preserve:
- The archetype definition is declarative. Construction code lives in the feature that needs it (`TemplateRegistry`, `ItemGeneratorSystem`, `PlayerCreationSystem`), not in the registry.
- Required components must be present for `Validate` to return true.
- Optional components may come and go at runtime.
- The `Custom` archetype is the escape hatch for entities that deliberately don't match any standard composition.
