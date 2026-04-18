# Architectural Layers

This document details each layer, its responsibilities, and how layers interact.

> Code in this doc uses the **idealized API** — the target the codebase is being rebuilt against. See [../roadmap/plan.md](../roadmap/plan.md) for phase sequencing.

---

## Layer 1: Event Handlers

**Purpose:** Respond to game events by orchestrating calls to domain systems.

**Characteristics:**
- Thin — typically 10–30 lines per handler method
- No game logic; purely orchestration
- Receive events via the event bus
- Call domain systems to perform actual work
- May raise subsequent events

**Example:**

```csharp
public class PlayerConditionHandler :
    IEventHandler<PlayerDeathEvent>,
    IEventHandler<PlayerReviveEvent>
{
    private readonly IDeathSystem _death;
    private readonly IVisibilitySystem _visibility;
    private readonly INotificationSystem _notifications;
    private readonly ILocationSystem _locations;

    public async Task HandleAsync(PlayerDeathEvent e)
    {
        await _death.ApplyDeathPenalties(e.Victim);

        var nearby = _locations.GetEntitiesAt(e.Location);
        var witnesses = _visibility.GetWitnesses(e.Victim, nearby);

        _notifications.Send(e.Victim, "You have died.");
        _notifications.SendToMany(witnesses, $"{e.Victim.Name} has fallen.");

        await _death.Respawn(e.Victim);
    }
}
```

**Anti-patterns (see [04-pitfalls.md](04-pitfalls.md)):**
- Conditional game logic (`if player has TrueSight…`)
- Direct entity state modification outside a system
- Complex branching — split into multiple handlers instead

---

## Layer 2: Domain Systems

**Purpose:** Encode game-specific rules and semantics.

**Characteristics:**
- Know the game's specific concepts (stealth, magic, combat mechanics)
- Compose core systems to implement game rules
- Stateless, or manage domain-specific state
- May depend on other domain systems (same or lower level)
- May depend on core systems

**Example:**

```csharp
public class VisibilitySystem : IVisibilitySystem
{
    private readonly ISkillSystem _skills;
    private readonly IEffectTracker _effects;

    public bool CanSee(Entity observer, Entity target)
    {
        // Rule: True Invisibility blocks all sight except True Sight
        if (_effects.HasEffect(target, EffectType.TrueInvisibility))
            return _effects.HasEffect(observer, EffectType.TrueSight);

        // Rule: Hiding requires contested stealth vs perception check
        if (_effects.HasEffect(target, EffectType.Hiding))
        {
            var result = _skills.OpposedCheck(
                target, SkillType.Stealth,
                observer, SkillType.Perception);
            return result.Winner == observer;
        }

        // Rule: Darkness blocks sight without darkvision
        if (_effects.HasEffect(observer.Location, EffectType.Darkness))
            return _effects.HasEffect(observer, EffectType.Darkvision);

        return true;
    }

    public IEnumerable<Entity> GetWitnesses(Entity target, IEnumerable<Entity> candidates)
        => candidates.Where(c => c != target && CanSee(c, target));
}
```

**Litmus test:** If a game design rule changes, domain code changes. If core mechanics change, domain systems stay put.

---

## Layer 3: Core Systems

**Purpose:** Mechanically generic systems without game-specific knowledge.

**Characteristics:**
- Could be reused in a different game
- Don't reference specific game concepts in logic (may process labeled data)
- Focus on resolution mechanics
- Typically stateless
- Only depend on other core systems or data layer

**Example:**

```csharp
public class SkillSystem : ISkillSystem
{
    private readonly IDiceSystem _dice;
    private readonly IAttributeCalculator _attributes;

    public SkillCheckResult Check(
        Entity actor,
        SkillType skill,
        int difficulty,
        IEnumerable<Modifier>? modifiers = null)
    {
        var baseValue = actor.GetSkillValue(skill);
        var totalModifiers = _attributes.SumModifiers(actor, skill, modifiers);
        var effectiveSkill = baseValue + totalModifiers;

        var roll = _dice.Roll("1d100");
        var target = effectiveSkill - difficulty;

        return new SkillCheckResult
        {
            Success = roll <= target,
            CriticalSuccess = roll <= target - 20,
            CriticalFailure = roll > 95,
            Margin = target - roll,
            Roll = roll,
            EffectiveSkill = effectiveSkill
        };
    }

    public OpposedCheckResult OpposedCheck(
        Entity actor, SkillType actorSkill,
        Entity target, SkillType targetSkill,
        IEnumerable<Modifier>? modifiers = null)
    {
        var actorResult = Check(actor, actorSkill, 0, modifiers);
        var targetResult = Check(target, targetSkill, 0);
        return new OpposedCheckResult
        {
            Winner = actorResult.Margin >= targetResult.Margin ? actor : target,
            Margin = actorResult.Margin - targetResult.Margin,
            ActorResult = actorResult,
            TargetResult = targetResult
        };
    }
}
```

**Litmus test:** Core systems answer *how do checks work?*, not *when should we check stealth?*

---

## Layer 4: Components / World

**Purpose:** Store entity data via ECS components; manage world state.

**Characteristics:**
- Components are pure data containers
- No business logic in components
- World (`EntityWorld` / `EntityService`) manages entity-component relationships
- Prototype and instance caches store entities

See [02-ecs.md](02-ecs.md) for ECS patterns, component design, and archetype rules.

---

## Cross-Layer Dependency Rules

```
Handlers    →  Domain Systems  →  Core Systems  →  Components/World
    ↓              ↓                    ↓
    └──────────────┴────────────────────┴──────────────→ Components/World
```

**Allowed:**
- Handlers → Domain Systems
- Handlers → Core Systems (rarely, for trivial lookups)
- Domain Systems → Domain Systems (same or lower level in the graph)
- Domain Systems → Core Systems
- Core Systems → Core Systems
- Any layer → Components/World (read/write components)

**Forbidden:**
- Components → Any system (components are pure data)
- Core Systems → Domain Systems
- Domain Systems → Handlers
- Any system → Event Bus directly (services return results; handlers publish events)

---

## Modules: Feature Cohesion

Each gameplay feature lives under `Core/Modules/<Feature>/` and contains the systems, handlers, events, and feature-specific components for that feature. This keeps slices discoverable as the project grows.

```
Core/Modules/Combat/
├── Events/
│   ├── AttackEvent.cs
│   ├── DamageEvent.cs
│   └── PlayerDeathEvent.cs
├── Handlers/
│   └── CombatHandler.cs
├── Core/                   # core systems specific to this module
│   └── DamageCore.cs
├── Domain/
│   ├── CombatSystem.cs
│   └── DeathSystem.cs
└── Components/             # components only used by this module
    └── CombatStateComponent.cs
```

Cross-cutting components (Identity, Transform, Pools, Attributes) stay under `Core/ECS/Components/`.

---

## Dependency Injection Setup

```csharp
// Core Systems (stateless singletons)
services.AddSingleton<IDiceSystem, DiceSystem>();
services.AddSingleton<ISkillSystem, SkillSystem>();
services.AddSingleton<IAttributeCalculator, AttributeCalculator>();
services.AddSingleton<IEffectTracker, EffectTracker>();
services.AddSingleton<ITimeSystem, TimeSystem>();

// Domain Systems
services.AddScoped<IVisibilitySystem, VisibilitySystem>();
services.AddScoped<ICombatSystem, CombatSystem>();
services.AddScoped<IDeathSystem, DeathSystem>();
services.AddScoped<ILootSystem, LootSystem>();
services.AddScoped<ICraftingSystem, CraftingSystem>();

// Event Bus
services.AddSingleton<IEventBus, EventBus>();

// Handlers (auto-subscribed via reflection or manual registration)
services.AddScoped<IEventHandler<PlayerDeathEvent>, PlayerConditionHandler>();
services.AddScoped<IEventHandler<PlayerDeathEvent>, CombatHandler>();
services.AddScoped<IEventHandler<PlayerDeathEvent>, NotificationHandler>();
```

**Note:** Multiple handlers may subscribe to the same event. Each owns a distinct concern — see [../reference/handlers.md](../reference/handlers.md).
