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

Two tools resolve ordering; a third avoids the stale-read failure that causes most "ordering" bugs. See [03-events.md#handler-priorities--ordering](03-events.md#handler-priorities--ordering) for the full guidance.

### Capture point-in-time data in the event

Many "ordering" bugs are really stale-read bugs: a later handler recomputes something that an earlier handler already mutated. Fix it by putting the captured fact on the event.

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

### Sequential event phases — for phase sequencing

If the work has distinct phases, split the event. Each phase fires its own event; the next phase only runs after the prior phase's handlers finish. This is the canonical answer when "handler B needs to run after handler A" means "after phase A completes."

```
PlayerDyingEvent     → NotificationHandler, InterventionHandler
PlayerDeathEvent     → PenaltyHandler, LootHandler
PlayerRespawnedEvent → SpawnHandler, UIHandler
```

### Explicit handler priorities — for intra-event tie-breaking

Within a single event, when multiple handlers legitimately subscribe and one concern must resolve before another, use `Priority`. Not a substitute for phased events — use phases when the ordering represents phases.

```csharp
public class CombatHandler : IEventHandler<PlayerDeathEvent>       { public int Priority => 10; }
public class NotificationHandler : IEventHandler<PlayerDeathEvent> { public int Priority => 80; }
```

### Anti-pattern: single orchestration handler

Collapsing the whole flow into one handler that calls every service in sequence reintroduces the god-handler problem this architecture exists to avoid. See [#god-handlers](#god-handlers).

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

## Threading Model & Cross-Thread State

**The runtime is multi-threaded at its edges, single-logical-world in the middle.** The threads that execute engine code:

- **Session read-loop threads** — one per connected telnet client; commands run on the caller's session thread (there is no command queue marshaling onto a game loop).
- **`BackgroundService` initiators** — the heartbeat (`HeartbeatBackgroundService`), the persistence flush timer; each runs on its own background thread and drives combat/effects/regen/restock through handlers.
- **Blazor circuit threads** (`Hedron.Web`) — the offline authoring editor and tooling pages; the `SimulationRunService` drain loop runs sim jobs on a background task.
- **Sim worker threads** — `SimulationRunner` parallelizes runs, but each run owns an **isolated sandbox `EntityService`** — confinement by construction, never the live world.

**The rule (INV-31, [checklist.md](checklist.md)):** state reachable from more than one of those threads is either **guarded** by its owning infrastructure type or **confined** to one thread/world, and new cross-thread surfaces declare which, in the plan, before implementation.

**Guarded sites (precedents):** `EventBus` (subscription/dispatch), `SessionOutputBuffer` + `SessionBufferRegistry` (three writer threads: own commands, other players' broadcasts, heartbeat output — drain-then-prompt is atomic under the buffer lock), `SessionManager`, `TelnetSession` (write lock), `TemplateRegistry`, `EcsManager` (world-instance assignment), `SimScenarioStore`.

**The acknowledged gap — ECS component storage.** `ComponentRepository`'s nested `Dictionary` storage is **unguarded**, while session threads and the heartbeat thread both read and mutate live world components concurrently. It has not bitten because structural mutations (add/remove component, create/destroy entity) cluster at startup, login, and admin actions while steady-state traffic is mostly value mutation — but it is a real latent race, not a proven-safe design. The bounded decision — guard the repository vs. marshal world mutation onto a single game-loop/queue — is tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md). Until it is made, **new work must not widen the exposure**: no new thread or timer that mutates live world components outside the existing session-command and heartbeat-handler paths (a new background initiator that only *publishes events* consumed on existing paths is fine; one that reaches into `EntityService` directly is a finding).

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
- [ ] Persistence shape is correct — entities that must survive restart carry `PersistentEntity`; `[Persistent]` on component types controls snapshot inclusion, not entity-level opt-in (see [06-persistence.md](06-persistence.md))
- [ ] Any new cross-thread surface declares its concurrency posture — guarded or confined (INV-31)
