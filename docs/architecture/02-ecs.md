# ECS, Components, Archetypes, Prototypes & Instances

This is the **canonical** ECS reference. It supersedes the legacy root `ENTITIES.md` and `DESIGN_DOCS/ENTITIES.md`.

> Code examples use the **idealized API** (`EcsManager.World`, `EntityWorld`, `ref T` component access). The implementation sits behind `EntityService`/`ComponentRepository`; see [../roadmap/plan.md](../roadmap/plan.md) for the current rebuild phase.

---

## ECS Concepts

- **Entity** — a unique `uint` ID. Has no data or behavior of its own.
- **Component** — a pure data container attached to an entity. No logic.
- **System** — contains logic; operates on entities via component queries.

```csharp
// Entity is an ID
uint entityId = world.CreateEntity();

// Component is data
public class HealthComponent : IComponent
{
    public int Current;
    public int Max;
}

// System contains logic
public class HealthSystem
{
    public void ApplyDamage(uint entity, int damage) { /* ... */ }
}
```

See [../reference/components.md](../reference/components.md) for the living component catalog.

---

## Prototype vs Instance

The game maintains two categories of entity:

| Prototype | Instance |
|---|---|
| Design-time template | Runtime entity in the game world |
| Used by editors and persistence | Spawned from a prototype |
| Edits mark the prototype dirty for save | Edits publish gameplay events |
| No gameplay events fired | Transient; not directly persisted |

Every entity carries a `PrototypeComponent` (current name) / `CacheInfo` (idealized name) that records this plus the link back to its prototype:

```csharp
public class PrototypeComponent : IComponent
{
    public CacheType CacheType;        // Prototype | Instance
    public uint? PrototypeSource;       // Instance: which prototype spawned this
    public string PersistenceKey;       // Prototype: storage identifier
}

public enum CacheType { Prototype, Instance }
```

**Query examples:**

```csharp
// All prototype mobs
foreach (var id in world.Query<PrototypeComponent, MobDataComponent>())
{
    ref var cache = ref world.Get<PrototypeComponent>(id);
    if (cache.CacheType == CacheType.Prototype) { /* ... */ }
}

// All active mob instances in a room
foreach (var id in world.Query<PrototypeComponent, TransformComponent, MobDataComponent>())
{
    ref var cache = ref world.Get<PrototypeComponent>(id);
    ref var xform = ref world.Get<TransformComponent>(id);
    if (cache.CacheType == CacheType.Instance && xform.RoomId == roomId) { /* ... */ }
}
```

---

## Archetypes

An **archetype** defines a standard component composition for a common entity type (Player, Mob, Weapon, Room…). Archetypes describe *required* vs *optional* components and power validation + spawning.

```csharp
public class ArchetypeDefinition
{
    public EntityArchetype Archetype { get; init; }
    public Type[] Required { get; init; }
    public Type[] Optional { get; init; }
}
```

The full registry lives in `Core/ECS/ArchetypeRegistry.cs`. See [../reference/archetypes.md](../reference/archetypes.md) for every archetype and its composition.

**Current archetypes** (enum `EntityArchetype`): Player, Mob, Weapon, Armor, Potion, StaticItem, Consumable, Room, Area, World, Storage, Inventory, Portal, Trigger, Custom.

### Creating an entity from an archetype

```csharp
// Prototype (editor, persistence)
uint swordPrototypeId = EntityFactory.CreateEntity(
    EntityArchetype.Weapon, CacheType.Prototype, name: "Iron Sword");

var weaponData = world.Get<WeaponDataComponent>(swordPrototypeId);
weaponData.WeaponType = WeaponType.Sword;

// Instance (runtime, spawned from prototype)
uint swordInstanceId = EntityFactory.CreateInstanceFromPrototype(swordPrototypeId);
```

### Archetype validation

```csharp
bool valid = EntityFactory.ValidateEntityArchetype(entityId, EntityArchetype.Weapon);
EntityArchetype detected = EntityFactory.GetEntityArchetype(entityId);
```

---

## Common Composition Patterns

**Living entities (Player, Mob)**
```
Identity + Transform + Prototype + Effects
+ Attributes + Pools + Currency + Skills + Qualities
+ Inventory + Equipment
+ PlayerData / MobData
+ PlayerConfiguration (player only)
```

**Items**
```
Identity + Transform + Prototype + Effects + ItemData
+ WeaponData (for weapons)
+ PotionData (for potions)
```

**World containers**
```
Identity + Transform + Prototype + Effects + ContainerData
+ RoomData (rooms) + InventoryComponent (for ground items)
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
if (world.HasComponent<PlayerDataComponent>(entityId)) { ... }
if (world.TryGet<WeaponDataComponent>(entityId, out var weapon)) { ... }
```

This is a hard rule — the legacy entity class hierarchy was stripped in Phase 1 of the rebuild (see [../roadmap/plan.md](../roadmap/plan.md)).

---

## Spawning: Prototype → Instance

```csharp
public class SpawnSystem
{
    public uint SpawnFromPrototype(uint prototypeId, uint locationRoomId)
    {
        ref var protoCache = ref _world.Get<PrototypeComponent>(prototypeId);
        Debug.Assert(protoCache.CacheType == CacheType.Prototype);

        var instanceId = EntityFactory.CreateInstanceFromPrototype(prototypeId);

        ref var xform = ref _world.Get<TransformComponent>(instanceId);
        xform.RoomId = locationRoomId;

        return instanceId;
    }
}
```

`EntityFactory.CreateInstanceFromPrototype` handles component copy + cache-info swap.

---

## Prototype vs Instance Operations: Share Logic, Not Side Effects

Systems that operate on both prototypes and instances use the **shared core** pattern: pure, side-effect-free static helpers encapsulate the actual data transform; two system classes (or two methods) wrap it with their respective side effects.

### Pattern: Static Core + Two Systems

```csharp
// Pure logic — no DI, no side effects, no I/O
public static class HealthCore
{
    public static void SetHealth(ref HealthComponent health, int value)
        => health.Current = Math.Clamp(value, 0, health.Max);

    public static bool IsDead(in HealthComponent health) => health.Current <= 0;

    public static float GetPercent(in HealthComponent health)
        => health.Max > 0 ? (float)health.Current / health.Max : 0;
}

// Prototype operations: editor-facing, persist changes, no events
public class PrototypeHealthSystem
{
    public void SetHealth(uint prototypeId, int value)
    {
        AssertPrototype(prototypeId);
        ref var h = ref _world.Get<HealthComponent>(prototypeId);
        HealthCore.SetHealth(ref h, value);
        _persistence.MarkDirty(prototypeId);
    }
}

// Instance operations: gameplay-facing, publish events via handler layer
public class CombatSystem : ICombatSystem
{
    public DamageResult ApplyDamage(uint instanceId, int damage, DamageType type)
    {
        AssertInstance(instanceId);
        ref var h = ref _world.Get<HealthComponent>(instanceId);
        var oldHp = h.Current;
        HealthCore.SetHealth(ref h, h.Current - damage);

        return new DamageResult
        {
            Target = instanceId,
            OldHp = oldHp,
            NewHp = h.Current,
            Killed = HealthCore.IsDead(h)
        };
        // Handler receives this result and publishes DamageEvent / PlayerDeathEvent.
    }
}
```

The rule: **services return results; handlers publish events.** See [03-events.md](03-events.md#services-return-results-handlers-publish-events).

---

## Component Organization

```
Core/ECS/Components/              # cross-cutting components
  IdentityComponent.cs            # Name, descriptions, tier
  TransformComponent.cs           # Parent/child, room/area
  PrototypeComponent.cs           # CacheType + PrototypeSource (the "CacheInfo")
  AttributesComponent.cs          # Might, Finesse, Will, …
  PoolsComponent.cs               # HP / Stamina / Energy
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
| `PrototypeComponent` / `CacheInfo` | Distinguishes prototype vs instance |
| Archetype | Required/optional component composition for a standard entity type |
| `EntityFactory` | Creation; enforces archetype composition |
| `*Core` static class | Shared, side-effect-free logic between prototype & instance systems |
| Prototype systems | Editor operations, persistence |
| Runtime systems | Gameplay operations; return results that handlers turn into events |
