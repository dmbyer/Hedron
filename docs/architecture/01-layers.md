# Architectural Layers

This document details each layer, its responsibilities, and how layers interact.

> Code in this doc uses the **idealized API** — the target the codebase is being rebuilt against. See [../roadmap/plan.md](../roadmap/plan.md) for phase sequencing.

> The consolidated, authoritative list of every architectural invariant is [checklist.md](checklist.md). This document is the *explanation*; the checklist is the *enforcement list* the `architecture-reviewer` agent and the per-slice gates run against.

---

## The shape: initiators feed a processing stack

The four numbered layers below (Handlers → Domain Systems → Core Systems → Components) are the **processing stack**: dependencies flow strictly downward through them. But something has to *start* a chain of processing. Those entry points are **Initiators**, and they are a distinct tier that sits above the stack and feeds its top.

```
┌──────────────────────────────────────────────────────────┐
│  Initiators / Entry Points                                │
│  Commands (external input) · Scheduled ticks (heartbeat)  │
│  Thin. Gather context → call a domain system → publish    │
│  the resulting event(s). No game rules.                   │
└───────────────────────────┬──────────────────────────────┘
                            │ publish events
                            ▼
┌──────────────────────────────────────────────────────────┐
│  Layer 1  Event Handlers      (orchestrate)               │
│  Layer 2  Domain Systems      (decide game rules)         │
│  Layer 3  Core Systems        (compute mechanics)         │
│  Layer 4  Components / World   (hold data)                │
└──────────────────────────────────────────────────────────┘
```

**Initiators and Handlers are the only two tiers permitted to publish events.** Domain and Core systems compute and return; they never touch the event bus. This is the precise statement of the "services return results; handlers publish events" rule — it constrains *systems*, not the orchestration boundary. See [03-events.md](03-events.md).

---

## Initiators / Entry Points

**Purpose:** Begin an event chain. An initiator gathers the context for one unit of work, asks a domain system to do it, and publishes the past-tense event(s) describing what happened. It contains **no game rules** — it is the thinnest possible glue between a trigger and the processing stack.

**Two kinds (more may be added; the tier is open):**

| Kind | Trigger | Cardinality | Examples |
|---|---|---|---|
| **Command** | External — a player or admin sends input | one input → one chain | `look`, `say`, `north`, `@spawn`, `@reload` |
| **Scheduled tick / heartbeat** | Internal — a timer fires | one fire → many chains (iterates entities, publishes per-entity events) | combat round, regen pulse, mob wander, effect expiry, mob respawn |

**Characteristics:**
- Thin. A command is **≤ 30 lines**; if it grows, the logic belongs in a domain system.
- No game-rule logic, no conditional branching on game state. "Publish A, then call system X, then **conditionally** publish B based on a game rule" is a *handler*, not an initiator — move it. Publishing multiple events is fine when every event is an unconditional, direct consequence of the command's action (e.g. `dig` always creates the room *and* always moves the player — both events belong in the command). The test: would extracting this into a handler reveal any game logic, or just re-publish mechanically? If the latter, keep it in the command.
- Parses/gathers input, resolves targets via domain-system lookups (`InventorySystem.FindByName`, `LocationSystem.FindInRoom`), calls the relevant domain system, publishes the resulting event(s).
- May publish events. This is the tier's defining permission, shared only with Handlers.

**The no-chain variant.** An initiator whose work is a closed mechanical sweep with **no game-rule fan-out** may call a system directly and publish nothing. The existing precedent is `PersistenceFlushTimer` (a scheduled-tick initiator) → `PersistenceSystem.FlushAsync()`, no event — persistence has no downstream chain to notify. Use this only when there is genuinely no chain; the moment another concern needs to react, it becomes an event.

**Where they live:**
- Commands — `Core/Commands/<X>Command.cs` (cross-cutting like `look`/`who`) or `Core/Modules/<Feature>/Commands/<X>Command.cs` (feature-owned).
- The command **dispatcher/runtime** (`CommandDispatcher`) is the runtime of the command-initiator tier — *not* a domain or core system despite living under `Core/`. It is permitted to publish the command-lifecycle event (e.g. `CommandExecutedEvent`) because it is the only component that observes every dispatch outcome (success / parse-fail / unauthorized / threw).
- The scheduler/heartbeat runtime lands with the first slice that needs scheduled work (slice 8, mobs + wandering). It is initiator-tier infrastructure, same publishing permission as the dispatcher.

**Anti-patterns:**
- Game rules inside a command (`if armor > threshold…`) — belongs in a domain system.
- Conditional event routing inside a command (`if condition → publish A, else → publish B`) — that's a handler.
- Unconditional sequential publishing is not the same thing: a command that always publishes A then B as direct consequences of its action is fine.
- A domain/core system reaching for the event bus to "save a hop" — forbidden; return a result and let the initiator or a handler publish.

### Heartbeat / scheduled-tick — forward design constraints

The heartbeat is not built until slice 8, but the Initiator tier is shaped now so it plugs in rather than forcing another revision. When it lands it **must** observe:

1. **It is a scheduler, not one global pulse.** Different work ticks at different cadences (combat round ≈ 2s, regen ≈ 10s, wander ≈ 30s, respawn ≈ minutes). Built on `TimeSystem.RegisterTimer(duration, callback)` (see [../reference/systems-planned.md](../reference/systems-planned.md)) — many timers, not a single `Tick()`.
2. **Intra-tick ordering uses the multi-phase-event pattern.** "All mobs move, then combat resolves, then DoT applies" is expressed as ordered past-tense events, each phase firing the next — see [03-events.md](03-events.md#handler-priorities--ordering). The heartbeat does not contain an ordering `if`-ladder.
3. **Single-threaded, queue-drained.** The intended concurrency shape is a single-threaded tick that drains an event queue, keeping the bus and handlers lock-free and consistent with the synchronous `EventBus`. Do not introduce locks ad hoc; revisit only if profiling forces it (tracked in [../roadmap/backlog.md](../roadmap/backlog.md)).

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

        // Output is always a typed message, never a raw transport string (INV-11).
        _notifications.Send(e.Victim, new PlainMessage("You have died."));
        _notifications.SendToMany(witnesses, new PlainMessage($"{e.Victim.Name} has fallen."));

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
- `EntityService` manages entity-component relationships for the single live world
- Entities are identified by `uint` ids, wrapped as `Entity(uint Id)` at call sites for readability

See [02-ecs.md](02-ecs.md) for ECS patterns, component design, template spawning, and archetype rules.

---

## Cross-Layer Dependency Rules

```
Initiators  →  (publish events)  →  Handlers  →  Domain Systems  →  Core Systems  →  Components/World
                                        ↓              ↓                  ↓
                                        └──────────────┴──────────────────┴────────→ Components/World
```

**Allowed:**
- Initiators → Domain Systems (resolve targets, do the work)
- Initiators → Event Bus (publish the resulting event)
- Handlers → Domain Systems
- Handlers → Core Systems (rarely, for trivial lookups)
- Handlers → Event Bus (publish follow-on events)
- Domain Systems → Domain Systems (same or lower level in the graph)
- Domain Systems → Core Systems
- Core Systems → Core Systems
- Any tier → Components/World (read/write components)

**Forbidden:**
- Components → Any system (components are pure data)
- Core Systems → Domain Systems
- Domain Systems → Handlers
- **Domain & Core Systems → Event Bus directly** (systems compute and return; only Initiators and Handlers publish). This is the exact scope of the rule — it constrains systems, *not* the orchestration boundary. An initiator or handler publishing is correct and expected.
- Initiators → Handlers directly (an initiator publishes an event; it never calls a handler)
- Game rules inside an Initiator (parse/resolve/call/publish only)

---

## Modules: Feature Cohesion

A **module** and a **feature** are the same thing in Hedron — a module is a feature slice. Each one lives under `Core/Modules/<Feature>/` and groups the systems, handlers, events, and feature-specific components that belong to it. This keeps slices discoverable as the project grows.

```
Core/Modules/Combat/
├── Events/
│   ├── AttackEvent.cs
│   ├── DamageEvent.cs
│   └── PlayerDeathEvent.cs
├── Handlers/
│   └── CombatHandler.cs
├── Systems/                # feature-owned (domain) systems
│   ├── CombatSystem.cs
│   └── DeathSystem.cs
└── Components/             # components only used by this module
    └── CombatStateComponent.cs
```

**Where systems live:**
- **Domain (feature) systems** — inside the module at `Core/Modules/<Feature>/Systems/`.
- **Core (cross-cutting) systems** — outside any module at `Core/Systems/` (e.g. `DiceSystem`, `TimeSystem`, `SkillSystem`). Usable by multiple features.

Cross-cutting components (Identity, Transform, Pools, Attributes) stay under `Core/ECS/Components/`.

**Registration.** There is no `IModule` interface. Each module exposes a single `AddXModule(IServiceCollection)` extension method (e.g. `Core/Modules/Combat/CombatModule.cs`) that registers that feature's systems, handlers, and event subscriptions. `Server/Program.cs` composes the host by calling each feature's extension. Handlers are registered via DI and subscribed to the event bus through the same extension.

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
