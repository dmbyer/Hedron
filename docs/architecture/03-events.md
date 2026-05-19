# Events & the Event Bus

This document covers event philosophy, naming, payload design, and the event bus. It supersedes the root `EVENT_EXPLANATION.md` (tutorial style) and `DESIGN_DOCS/EVENTS.md`.

---

## Why an event bus?

A MUD's systems are deeply interconnected. A single player death needs to: remove them from combat, apply penalties, notify visible witnesses, move them to respawn, update persistence, and update NPC threat tables. Naïve implementations fail in one of two ways:

1. **Monolithic handlers** that know about every other system — god classes.
2. **Tightly coupled systems** where CombatSystem calls DeathSystem calls NotificationSystem calls VisibilitySystem — fragile chains, cycles.

An event bus breaks the coupling: CombatSystem reports *what happened* via an event, and any system that cares subscribes.

---

## Events are thin facts

Events represent **what happened**. They are past-tense, immutable, and contain enough context for handlers to do their work without round-tripping (where practical).

| ARE | ARE NOT |
|---|---|
| Notifications ("this happened") | Commands ("do this") |
| Facts ("this is now true") | Requests ("please do this") |
| History ("this occurred at T") | Queries ("what is this?") |

### Naming

Past tense, suffix `Event`.

| ✅ | ❌ |
|---|---|
| `PlayerDeathEvent` | `PlayerDieEvent`, `KillPlayerEvent` |
| `ItemEquippedEvent` | `EquipItemEvent` |
| `SpellCastEvent` | `CastSpellEvent` |
| `TradeAcceptedEvent` | `AcceptTradeEvent` |

---

## Payload: thin by default, enrich only when justified

### Thin events (default)

```csharp
public record PlayerDeathEvent(
    Player Victim,
    Entity? Killer,
    Location Location,
    DamageType? FinalBlowType
) : IEvent;
```

**Pros:** small events, handlers fetch fresh data, no stale context.
**Cons:** multiple handlers may redundantly query the same data.

### Enriched events (only when justified)

Include pre-computed context that **multiple** handlers need, where **recomputing is expensive** and the value **won't change** during processing.

```csharp
public record PlayerDeathEvent(
    Player Victim,
    Entity? Killer,
    Location Location,
    DamageType? FinalBlowType,
    IReadOnlyList<Player> Witnesses    // pre-computed because 3+ handlers need it
) : IEvent;
```

Include location in the event **even if it might change** — it captures the death *location* at the moment of death. That's a fact, not a lookup.

Anti-pattern: don't over-enrich. See [04-pitfalls.md](04-pitfalls.md#over-enriched-events).

---

## Event Bus Interface

```csharp
public interface IEvent
{
    DateTime OccurredAt { get; }
    Guid EventId { get; }
}

public interface IEventHandler<TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event);
    int Priority { get; }   // lower = earlier execution
}

public interface IEventBus
{
    void Publish<TEvent>(TEvent @event) where TEvent : IEvent;
    Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
    void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
}
```

### Minimal implementation sketch

```csharp
public class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<object>> _handlers = new();
    private readonly object _lock = new();

    public void Subscribe<T>(IEventHandler<T> handler) where T : IEvent
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
                _handlers[typeof(T)] = list = new();
            list.Add(handler);
            list.Sort((a, b) =>
                ((IEventHandler<T>)a).Priority.CompareTo(((IEventHandler<T>)b).Priority));
        }
    }

    public async Task PublishAsync<T>(T evt) where T : IEvent
    {
        List<object>? snapshot;
        lock (_lock) snapshot = _handlers.TryGetValue(typeof(T), out var list) ? list.ToList() : null;
        if (snapshot == null) return;
        foreach (var h in snapshot.Cast<IEventHandler<T>>())
            await h.HandleAsync(evt);
    }
}
```

---

## Services return results; handlers publish events

**Rule ([checklist.md](checklist.md) INV-5):** *Systems* compute and return — they never touch the event bus. The tiers permitted to publish are **Initiators** (commands, scheduled ticks) and **Handlers**. This rule constrains domain & core systems specifically; it is *not* a prohibition on the orchestration boundary. A command or the heartbeat publishing its outcome event is correct and expected — see [01-layers.md](01-layers.md#initiators--entry-points). The example below uses a "Service" to show the *system* anti-pattern.

```csharp
// ❌ Service raises events directly — hidden side effects
public class CombatService
{
    public void ApplyDamage(Entity target, int damage)
    {
        target.Health -= damage;
        _eventBus.Publish(new DamageEvent(target, damage));          // hidden
        if (target.Health <= 0)
            _eventBus.Publish(new PlayerDeathEvent(target));         // hidden
    }
}

// ✅ Service returns a result
public class CombatService
{
    public DamageResult ApplyDamage(Entity target, int damage)
    {
        target.Health -= damage;
        return new DamageResult { Target = target, Damage = damage, Killed = target.Health <= 0 };
    }
}

// ✅ Handler owns event publication
public class CombatHandler : IEventHandler<AttackEvent>
{
    public async Task HandleAsync(AttackEvent e)
    {
        var result = _combat.ApplyDamage(e.Defender, e.DamageAmount);

        await _bus.PublishAsync(new DamageEvent(result.Target, e.Attacker, result.Damage, e.DamageType));
        if (result.Killed)
            await _bus.PublishAsync(new PlayerDeathEvent(result.Target, e.Attacker, e.Location, e.DamageType));
    }
}
```

**Why?** Services raising events creates hidden side effects, couples services to the bus, makes testing harder, and lets event chains become unpredictable.

---

## Handler Priorities & Ordering

Within a single event, the bus dispatches handlers in **priority order** (lower first). Two tools solve the two distinct ordering problems:

### Phase sequencing — sequential phased events

**When distinct phases of work must run in order** (die → notify → loot → respawn), split the work across multiple past-tense events. Each handler subscribes to the phase it cares about; the next phase fires its event when the prior phase finishes.

```
AttackEvent → DamageEvent → PlayerDyingEvent → PlayerDeathEvent → PlayerRespawnedEvent
```

This is the default tool. It keeps each handler narrow, makes ordering explicit in the event graph, and lets a later phase assume the earlier phase completed.

### Intra-event tie-breaking — handler priority

**When several handlers legitimately subscribe to the same event** and one concern must resolve before another (e.g. combat-state cleanup before notification), set explicit `Priority` values. Pick numbers with gaps so future handlers can slot in.

```csharp
public class CombatHandler : IEventHandler<PlayerDeathEvent>        { public int Priority => 10; }
public class PlayerConditionHandler : IEventHandler<PlayerDeathEvent> { public int Priority => 20; }
public class NotificationHandler : IEventHandler<PlayerDeathEvent>  { public int Priority => 80; }
```

Priority is not a replacement for phased events — if ordering implies phases, split the event instead.

### What to avoid

**Single orchestration handler that calls every service in sequence.** Collapses the graph back into a procedural god-handler, reintroduces coupling the bus is meant to break, and contradicts [04-pitfalls.md#god-handlers](04-pitfalls.md#god-handlers). Don't use this pattern.

See [04-pitfalls.md#handler-ordering-issues](04-pitfalls.md#handler-ordering-issues) for the matching failure modes and the "capture point-in-time data" technique for stale-read avoidance.

---

## Event Catalog (canonical categories)

The living catalog lives in the module events folders (`Core/Modules/<Feature>/Events/`). Canonical categories:

**Player session:** `PlayerLoginEvent`, `PlayerLogoutEvent`, `CharacterCreatedEvent`, `CharacterDeletedEvent`
**Player condition:** `PlayerDeathEvent`, `PlayerReviveEvent`, `PlayerRestStartedEvent`, `PlayerRestCompletedEvent`
**Movement:** `PlayerMoveEvent`, `PlayerTeleportEvent`, `PlayerEnterRoomEvent`, `PlayerExitRoomEvent`
**Combat:** `CombatStartedEvent`, `CombatEndedEvent`, `AttackEvent`, `DamageEvent`, `FleeEvent`
**Items:** `ItemPickedUpEvent`, `ItemDroppedEvent`, `ItemEquippedEvent`, `ItemUnequippedEvent`, `ItemDestroyedEvent`, `LootDroppedEvent`, `LootCollectedEvent`, `ContainerOpenedEvent`
**Economy:** `TradeProposedEvent`, `TradeAcceptedEvent`, `TradeCompletedEvent`, `ShopPurchaseEvent`, `ShopSaleEvent`
**Crafting:** `CraftingStartedEvent`, `CraftingCompletedEvent`, `CraftingFailedEvent`
**Magic:** `SpellCastEvent`, `SpellEffectAppliedEvent`, `SpellEffectExpiredEvent`, `SpellInterruptedEvent`
**Progression:** `ExperienceGainedEvent`, `LevelUpEvent`, `SkillIncreasedEvent`, `AttributeIncreasedEvent`
**World:** `TimeTickEvent`, `DayNightChangedEvent`, `WeatherChangedEvent`
**Input:** `CommandReceivedEvent`

---

## Worked example: Player death

**Publisher (CombatHandler):**

```csharp
public class CombatHandler : IEventHandler<AttackEvent>
{
    public async Task HandleAsync(AttackEvent e)
    {
        var result = _combat.ApplyDamage(e.Defender, e.DamageAmount);
        await _bus.PublishAsync(new DamageEvent(result.Target, e.Attacker, result.Damage, e.DamageType));
        if (result.Killed && _world.HasComponent<PlayerDataComponent>(result.Target))
        {
            await _bus.PublishAsync(new PlayerDeathEvent(
                Victim: result.Target,
                Killer: e.Attacker,
                Location: _world.Get<TransformComponent>(result.Target).RoomId,
                FinalBlowType: e.DamageType));
        }
    }
}
```

**Subscribers** — multiple handlers, each with one concern:

| Handler | Concern |
|---|---|
| `CombatHandler` (self, priority 10) | Remove from combat |
| `PlayerConditionHandler` (priority 20) | Apply death penalty, respawn |
| `NotificationHandler` (priority 80) | Notify witnesses |
| `PersistenceHandler` (priority 90) | Save state |
| `AIHandler` (priority 95) | Update NPC threat tables |
