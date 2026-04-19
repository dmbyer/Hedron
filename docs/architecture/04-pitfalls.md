# Pitfalls and Anti-Patterns

Common architectural mistakes and how to avoid them. Review before merging any change that spans layers.

---

## Circular Dependencies

**The problem:** Service A depends on Service B, and Service B depends on Service A. Compilation issues (or runtime resolution failures), unclear ownership, testing nightmares.

**Detection:** Before adding a dependency, ask *"Could the service I'm depending on ever need to call me?"* If yes, you're opening a cycle. Draw the system DAG — if you can't arrange it top-to-bottom, you have cycles.

### Solution 1: Extract shared logic downward

```csharp
// New core service both can depend on
public class CombatStateService
{
    public bool IsInCombat(uint entity);
    public IEnumerable<uint> GetCombatants(uint entity);
}

public class CombatSystem       { private readonly ICombatStateService _state; }
public class VisibilitySystem   { private readonly ICombatStateService _state; }
```

### Solution 2: Pass data instead of services

```csharp
public class VisibilitySystem
{
    public bool CanSee(Entity observer, Entity target, bool observerInCombat = false) { /* ... */ }
}

// Caller provides state
var inCombat = _combatState.IsInCombat(observer);
var canSee = _visibility.CanSee(observer, target, inCombat);
```

### Solution 3: Invert with events

```csharp
// CombatSystem doesn't call VisibilitySystem. Handler composes both.
public class AttackHandler : IEventHandler<AttackEvent>
{
    public async Task HandleAsync(AttackEvent e)
    {
        var isSneak = !_visibility.CanSee(e.Defender, e.Attacker);
        var result = _combat.ResolveAttack(e.Attacker, e.Defender, e.Attack);
        if (isSneak) result.DamageMultiplier *= 1.5;
    }
}
```

---

## Handler Ordering Issues

**The problem:** Multiple handlers respond to the same event. If handler B relies on handler A's side effects, undefined execution order causes races.

**Example:** `NotificationHandler` reads the player's location; `RespawnHandler` moves the player. If respawn runs first, the notification goes to the wrong room.

### Solution 1: Capture point-in-time data in the event

```csharp
public record PlayerDeathEvent(Player Victim, Entity? Killer, Location DeathLocation) : IEvent;

public class NotificationHandler : IEventHandler<PlayerDeathEvent>
{
    public async Task HandleAsync(PlayerDeathEvent e)
    {
        var witnesses = _locations.GetEntitiesAt(e.DeathLocation);  // always correct
        _notifications.SendToMany(witnesses, $"{e.Victim.Name} died.");
    }
}
```

### Solution 2: Explicit handler priorities

```csharp
public class NotificationHandler : IEventHandler<PlayerDeathEvent> { public int Priority => 10; }
public class RespawnHandler     : IEventHandler<PlayerDeathEvent> { public int Priority => 100; }
```

### Solution 3: Sequential event phases (preferred)

```
PlayerDyingEvent  → NotificationHandler, InterventionHandler
PlayerDeathEvent  → PenaltyHandler, LootHandler
PlayerRespawnedEvent → SpawnHandler, UIHandler
```

### Solution 4: Orchestration handler

See [03-events.md](03-events.md#option-3-orchestration-handler).

---

## God Handlers

**The problem:** One handler does too many things (combat cleanup + notification + penalties + loot + respawn + AI updates).

**Fix:** Split into focused handlers — one concern each — or delegate to an orchestration handler that calls services in explicit order.

```
CombatCleanupHandler      // remove from combat
DeathNotificationHandler  // notify witnesses
DeathPenaltyHandler       // apply penalties
LootDropHandler           // drop items
RespawnHandler            // move to spawn
AIThreatHandler           // update NPC threat tables
```

---

## Logic in Events

**The problem:** Events contain computed properties or methods. Events become versioned carriers of game rules.

```csharp
// ❌ Event contains logic
public record PlayerDeathEvent(Player Victim, Entity? Killer)
{
    public int ExperiencePenalty => Victim.Level * 100;        // business logic!
    public bool WasMurdered => Killer is Player;
}
```

**Fix:** Events are pure data. Put logic in systems.

```csharp
public record PlayerDeathEvent(Player Victim, Entity? Killer, Location Location) : IEvent;

public class DeathSystem
{
    public int CalculateExperiencePenalty(Player p) => p.Level * 100;
}
```

---

## Services Raising Events

**The problem:** Services publish events directly, creating hidden side effects. Callers don't know what's fired. Tests must mock the bus.

**Fix:** Services return results. Handlers publish events. See [03-events.md](03-events.md#services-return-results-handlers-publish-events).

---

## Over-Enriched Events

**The problem:** Events include every possible field a handler might want. Event creation becomes complex; data goes stale; events become coupled to handler internals.

```csharp
// ❌ knows too much about what handlers need
public record PlayerDeathEvent(
    Player Victim, Entity? Killer, Location Location,
    IReadOnlyList<Player> Witnesses,
    IReadOnlyList<Item> DroppedItems,
    int ExperienceLost,
    Location RespawnPoint,
    IReadOnlyList<Entity> NearbyEnemies,
    bool WasInCombat,
    TimeSpan CombatDuration
    // ... 10 more properties
);
```

**Fix:** Start thin. Enrich only when multiple handlers need the same expensive computation and the value won't change during processing.

---

## Domain Logic Leaking into Core Systems

**The problem:** A core system starts making decisions based on game-specific concepts.

```csharp
// ❌ SkillSystem knows about stealth game rules
public class SkillSystem
{
    public SkillCheckResult Check(Entity actor, SkillType skill, int difficulty)
    {
        var modifiers = GetModifiers(actor, skill);
        if (skill == SkillType.Stealth && actor.HasEffect(EffectType.Invisibility))
            modifiers.Add(new Modifier(20, "Invisibility"));   // domain logic!
        // ...
    }
}
```

**Fix:** Core accepts modifiers; domain decides what modifiers apply.

```csharp
public class SkillSystem
{
    public SkillCheckResult Check(
        Entity actor, SkillType skill, int difficulty,
        IEnumerable<Modifier>? situationalModifiers = null) { /* sums, doesn't interpret */ }
}

public class VisibilitySystem
{
    public bool CanDetect(Entity observer, Entity target)
    {
        var modifiers = new List<Modifier>();
        if (_effects.HasEffect(target, EffectType.Invisibility))
            modifiers.Add(new Modifier(20, "Invisibility"));

        return _skills.OpposedCheck(
            target, SkillType.Stealth,
            observer, SkillType.Perception, modifiers).Winner == observer;
    }
}
```

---

## Type Checks Using `is` / `as`

**The problem:** Inheritance checks bypass the ECS model and break when the class hierarchy is flattened.

```csharp
// ❌
if (entity is ItemWeapon weapon) { weapon.Damage(); }
```

**Fix:** Use component queries.

```csharp
// ✅
if (_world.TryGet<WeaponDataComponent>(entityId, out var weapon)) { /* use weapon */ }
```

---

## Treating Templates Like Entities (or Vice Versa)

**The problem:** A `TemplateRegistry` entry is a declarative blueprint, not an entity. It does not live in `EntityService`; it has no id that `HasComponent<T>` can query; it cannot be damaged, moved, or saved as a live entity. Code that forgets this usually manifests as "why does this template id return nothing from `GetComponent`?" or "why did my spawn loop destroy the template?"

**Fix:** Keep the boundary in the types:
- `TemplateRegistry.Spawn(templateId) → Entity` is the only way to turn a template into a live entity.
- Domain systems only operate on `Entity` / `uint` — they never take a template id.
- When something designer-facing edits a template, it goes through `TemplateRegistry`, not `EntityService`.

If a system takes "either a template or an entity," it has two jobs and should be split.

## Building Entities Outside Their Owning Feature

**The problem:** A handler or unrelated system knows too much about how to construct a specific archetype (attaching 8 components in the right order, setting defaults the caller shouldn't know about). Construction logic scattered across the codebase drifts out of sync.

**Fix:** Bespoke construction belongs to the feature that owns the entity's semantics:
- `ItemGeneratorSystem` builds crafted items.
- `PlayerCreationSystem` builds new player characters.
- `LootSystem` builds generated loot.
- `TemplateRegistry.Spawn` handles authored content.

Handlers orchestrate; they call one of the above. They do not themselves call `CreateEntity` + six `AddComponent`s.

---

## Summary Checklist

Before merging a change that adds or modifies a system/handler/event, verify:

- [ ] Dependencies flow downward only (no cycles)
- [ ] Events are thin unless enrichment is justified
- [ ] Handlers orchestrate; services compute
- [ ] Services return results; handlers publish events
- [ ] Core systems don't reference game-specific concepts
- [ ] Handler ordering is explicit if it matters
- [ ] Each handler has a single clear responsibility
- [ ] Component queries (not `is`/`as`) identify entity type
- [ ] Entities and templates are not confused at system boundaries
- [ ] Persistence shape is correct — components that must survive restart carry `[Persistent]`, session-only components don't
