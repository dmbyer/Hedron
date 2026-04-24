# ECS, Components, Archetypes, Templates & Persistence

This is the **canonical** ECS reference.

> Code examples use the **idealized API** (`EntityService`, `TemplateRegistry`, `Entity` wrapper). The implementation sits behind `EntityService`/`ComponentRepository`/`EcsManager`; see [../roadmap/plan.md](../roadmap/plan.md) for the current rebuild phase.

---

## ECS Concepts

- **Entity** — an identity. Represented at runtime as a `readonly record struct Entity(uint Id)`. No data, no behaviour.
- **Component** — a pure data container attached to an entity. No logic.
- **System** — contains logic; operates on entities via component queries.

```csharp
// Entity is an ID, wrapped for readability
public readonly record struct Entity(uint Id);

Entity player = entityService.CreateEntity();

// Component is data
public class HealthComponent : IComponent
{
    public int Current;
    public int Max;
}

// System contains logic
public class CombatSystem
{
    public DamageResult ApplyDamage(Entity target, int damage) { /* ... */ }
}
```

`Entity` is a wrapper around `uint` for flavour and call-site readability. All persistent references between entities still use the `uint` id (stored on components). The wrapper is a convenience, not an object reference — lifetime is tracked by `EntityService`, not by holding an `Entity` value.

See [../reference/components.md](../reference/components.md) for the living component catalog.

---

## One World Model

Hedron has **one world**. Every entity that exists at runtime lives in the same `EntityService`, and every entity of a given archetype has the same shape.

Authored content (rooms, mobs, items — anything a designer writes) is expressed as **templates**. A template is a declarative spec, not an entity. `TemplateRegistry.Spawn(templateId, ...)` builds a live entity from a template when the world starts or when something needs to be respawned.

```csharp
// Template lives in the registry; world holds only live entities
Entity sword = templateRegistry.Spawn("iron_sword");

// For bespoke construction (player characters, shop-purchased items),
// domain systems build entities directly
Entity newPlayer = entityService.CreateEntity();
entityService.AddComponent(newPlayer.Id, new IdentityComponent { Name = "Gelthor" });
entityService.AddComponent(newPlayer.Id, new PoolsComponent { /* ... */ });
// ...
```

Templates aren't entities — they live in `TemplateRegistry` and are consumed by `Spawn` to build runtime entities. The question "is this entity a canonical template or a running instance?" doesn't arise: if you hold an `Entity`, it's live.

---

## Entity Construction

Two paths, chosen by intent:

**1. Template spawn** — for authored content with known shape.
```csharp
Entity mob = templateRegistry.Spawn("goblin_scout");
```
`TemplateRegistry` owns the template → component translation. Templates can carry optional overrides (tier, custom name, location).

**2. Bespoke construction** — for entities whose shape is computed at runtime.
```csharp
// Inside ItemGeneratorSystem
Entity item = entityService.CreateEntity();
entityService.AddComponent(item.Id, new IdentityComponent { Name = $"{quality} {recipe.Name}" });
entityService.AddComponent(item.Id, new ItemDataComponent { /* derived from recipe + skill */ });
entityService.AddComponent(item.Id, new WeaponDataComponent { /* ... */ });
```
Domain systems build directly against `EntityService`. Each feature owns its own construction code (`ItemGeneratorSystem`, `PlayerCreationSystem`, `LootSystem`) — construction is never centralised in a single factory.

The `Custom` archetype exists as an escape hatch: an entity that doesn't match any archetype composition but still needs to participate in queries.

---

## Archetypes: Validation and Detection, Not Creation

An **archetype** defines a standard component composition for a common entity type (Player, Mob, Weapon, Room…). Archetypes describe *required* vs *optional* components. They are a **validation and introspection tool** — they are not used to construct entities.

```csharp
public class ArchetypeDefinition
{
    public EntityArchetype Archetype { get; init; }
    public Type[] Required { get; init; }
    public Type[] Optional { get; init; }
}

public interface IArchetypeRegistry
{
    IReadOnlyList<Type> RequiredComponents(EntityArchetype archetype);
    bool Validate(uint entityId, EntityArchetype expected);
    EntityArchetype Detect(uint entityId);
}
```

The registry lives in `Core/ECS/ArchetypeRegistry.cs`. See [../reference/archetypes.md](../reference/archetypes.md) for every archetype and its composition.

**Current archetypes** (enum `EntityArchetype`): Player, Mob, Weapon, Armor, Potion, StaticItem, Consumable, Room, Area, World, Storage, Inventory, Portal, Trigger, Custom.

### Archetype validation + detection

```csharp
// "This entity was built as a Weapon — are all the required components present?"
bool valid = archetypes.Validate(entityId, EntityArchetype.Weapon);

// "Given an unknown entity, which archetype does it look like?"
EntityArchetype detected = archetypes.Detect(entityId);
```

Use `Validate` in asserts and editor tooling. Use `Detect` when a handler receives an entity of unknown archetype and needs to branch (most handlers won't — prefer `HasComponent<T>` queries).

---

## Components Describe What an Entity IS

The sharpest archetype rule: **components describe what an entity is, not what it interacts with**.

**Worked example — a healing potion:**

```csharp
// Potion template
entityService.AddComponent(potion.Id, new IdentityComponent { Name = "Lesser Healing Potion" });
entityService.AddComponent(potion.Id, new ItemDataComponent { /* ... */ });
entityService.AddComponent(potion.Id, new PotionDataComponent
{
    EffectsOnUse = new[] { new HealEffect(amount: 25) }
});

// The potion does NOT have a PoolsComponent or HealthComponent.
// Those belong to Players/Mobs. The potion describes what happens when someone drinks it.
```

When a player drinks the potion, `PotionSystem` reads the potion's `PotionDataComponent.EffectsOnUse` and applies those effects to the *drinker's* `PoolsComponent` / `EffectsComponent`. The potion is consumed (destroyed).

The reverse also holds: if a mob has an inventory, the mob has an `InventoryComponent`. The items inside the inventory are their own entities with their own components — they don't get bolted onto the mob.

See [../reference/archetypes.md](../reference/archetypes.md) for every archetype's required/optional composition.

---

## Common Composition Patterns

**Living entities (Player, Mob)**
```
Identity + Transform + Effects
+ Attributes + Pools + Currency + Skills + Qualities
+ Inventory + Equipment
+ PlayerData / MobData
+ PlayerConfiguration (player only)
```

**Items**
```
Identity + Transform + Effects + ItemData
+ WeaponData (for weapons)
+ PotionData (for potions)
```

**World containers**
```
Identity + Transform + Effects + ContainerData
+ RoomData (rooms) + Inventory (for ground items)
+ AreaData (areas)
```

See [../reference/components.md](../reference/components.md) for every component and [../reference/archetypes.md](../reference/archetypes.md) for exact required/optional sets.

---

## Type Identification: Components, Not `is`

Replace inheritance checks with component queries everywhere:

```csharp
// ❌ Do not
if (entity is Player) { ... }
if (entity is ItemWeapon weapon) { weapon.Something(); }

// ✅ Do
if (entityService.HasComponent<PlayerDataComponent>(entityId)) { ... }
if (entityService.TryGet<WeaponDataComponent>(entityId, out var weapon)) { ... }
```

This is a hard rule — the legacy entity class hierarchy was stripped in Phase 1 of the rebuild (see [../roadmap/plan.md](../roadmap/plan.md)).

---

## Effects: Computed Stats, Split Persistence

Stat modifications — from equipment, temporary buffs, auras, skill passives — are modelled as **effects**, not as cached mutations on the base stat. Systems compute the effective value on read:

```csharp
public int GetEffectiveMaxHealth(uint entityId)
{
    var pools = entityService.Get<PoolsComponent>(entityId);
    int total = pools.BaseMaxHp;
    foreach (var effect in effectTracker.EffectsOn(entityId))
        total += effect.MaxHpModifier;
    return total;
}
```

This sidesteps the classic "equipment changed / effect expired / did I remember to recalc?" family of bugs. The base value is the only persistent truth; everything else is recomputed.

To keep persistence clean, active effects live in **two separate components**:

| Component | Content | Persisted? |
|---|---|---|
| `PersistentEffectsComponent` | Long-term effects a player expects to survive restart: curses, disease, quest-tied debuffs. | Yes |
| `TransientEffectsComponent` | Short-term effects tied to the session: buffs from potions, combat buffs, spell durations. | No |

When an effect wears off, `EffectTracker` removes it from whichever component it lives in — no further side effects needed, because stat recomputation on read is automatic.

---

## Persistence: `[Persistent]` on Component Types

Persistence operates at the **component-type** level, not the entity level. Tag a component class with `[Persistent]` and `PersistenceSystem` includes it when serializing an entity; omit the attribute and the component is transient.

```csharp
[Persistent]
public class IdentityComponent : IComponent { /* saved */ }

[Persistent]
public class PoolsComponent : IComponent { /* saved */ }

public class TransientEffectsComponent : IComponent { /* NOT saved */ }
public class CombatStateComponent : IComponent       { /* NOT saved */ }
```

An entity is persisted if it has **any** `[Persistent]` component. On save, only the `[Persistent]` components are written. On load, `PersistenceSystem` rebuilds the entity from the stored components; any transient components are re-attached by the systems that own them (the combat system re-attaches `CombatStateComponent` when combat starts, etc.).

### Blueprint-seeds-world for authored content

For rooms/areas a designer authors, the data flow is:

1. Authored blueprint (JSON / YAML / code) → `TemplateRegistry.Spawn` on world boot creates the entity.
2. The entity lives in the world. Players interact with it. If they modify it (e.g. change a room description via an admin command, or a door's locked state flips permanently), the modified `[Persistent]` components are saved.
3. On next boot: `PersistenceSystem` loads the persisted components first; only entities that weren't persisted are reseeded from blueprints. Persisted changes win over blueprint defaults.

The blueprint is the seed. The persisted components are the authority. This handles the "player modified a pre-authored room permanently" case in one model: save what changed, reseed what wasn't touched.

### Silent load path

During `PersistenceSystem` hydration, components are restored **without firing events**. Systems subscribe to change events (`PlayerMovedEvent`, `PoolsChangedEvent`, etc.) to react to runtime changes — hydration is not a runtime change, so it must not trigger those. Hydration uses `entityService.AddComponent` directly and never touches the event bus.

---

## Component Organization

```
Core/ECS/Components/              # cross-cutting components
  IdentityComponent.cs            # Name, descriptions, tier
  TransformComponent.cs           # Parent/child, room/area
  AttributesComponent.cs          # Might, Finesse, Will, …
  PoolsComponent.cs               # HP / Stamina / Energy
  PersistentEffectsComponent.cs   # survives save/load
  TransientEffectsComponent.cs    # session-only
  ...

Core/Modules/<Feature>/Components/   # feature-owned components
  Combat/Components/CombatStateComponent.cs
  AI/Components/BehaviorTreeComponent.cs
```

A component belongs **under a module** only if it's exclusively used by that feature. Anything queried by multiple modules is cross-cutting.

---

## Summary

| Concept | Purpose |
|---|---|
| `Entity` | `readonly record struct` wrapper around `uint`; identity with flavour |
| `EntityService` | Creates, destroys, and composes entities; adds/reads/removes components |
| `TemplateRegistry` | Authored content → live entity on spawn |
| Archetype | Required/optional component composition for validation + detection |
| `[Persistent]` attribute | Marks component types as save-worthy; entity persistence is derived |
| `PersistentEffectsComponent` / `TransientEffectsComponent` | Splits effects by lifetime so persistence is automatic |
| Computed stats | Effective values recomputed on read; base + effects |
| Blueprint-seeds-world | Authored templates seed; persisted components win on reload |
