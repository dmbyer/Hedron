---
name: add-event
description: Use when adding a new event type to the event bus. Covers naming (past tense, specific), payload shape (thin vs enriched), where to publish, and who subscribes. Invoke when the user asks to add an event, fire a signal, or wire up a cross-cutting notification.
---

# Add an Event

Events are past-tense facts published to the event bus. Handlers subscribe by priority and react. Systems *never* publish events — only Initiators (commands, scheduled ticks) and Handlers do.

Authoritative rules: [docs/architecture/03-events.md](../../../docs/architecture/03-events.md) · pitfalls: [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md).

## Naming rules

- **Past tense, specific**: `PlayerDiedEvent`, `ItemEquippedEvent`, `MobMovedEvent`.
- **Avoid gerunds and futures**: no `DamageApplyingEvent`, no `WillDieEvent`.
- **Domain-typed actor in name when ambiguous**: `PlayerDeathEvent` + `MobDeathEvent` instead of a shared `DeathEvent` when handlers care which.

## Payload shape — thin vs enriched

**Thin** (default): just IDs and the minimal fact.
```csharp
public record ItemEquippedEvent(uint PlayerId, uint ItemId, ItemSlot Slot) : IGameEvent;
```

**Enriched**: only when multiple handlers would otherwise re-fetch the same data.
```csharp
public record PlayerDeathEvent(uint PlayerId, uint DeathRoomId, DamageKind FinalBlow) : IGameEvent;
```
Rule: "enrich" means capture **state at publish time that subscribers can't reconstruct later** (like the death location before respawn moves the player). Don't enrich with convenience lookups.

## Steps

1. Pick the module that owns the event: `Core/Modules/<Feature>/Events/<X>Event.cs`.
2. Name past-tense, specific.
3. Thin payload by default; enrich only for state that changes before handlers run.
4. The **publishing Initiator or Handler** (not a service/system) calls `eventBus.Publish(new XEvent(...))`. Initiators (commands, scheduled ticks) publish when the event is a direct consequence of their action; Handlers publish when the event is a downstream reaction to a prior event.
5. Register subscribers with priorities in each subscribing feature's `AddXModule(IServiceCollection)` extension (e.g. `Core/Modules/<Feature>/<Feature>Module.cs`).
6. Add the event to [docs/architecture/03-events.md](../../../docs/architecture/03-events.md) under its category (combat, movement, inventory, etc.).
7. If a use case now produces this event, update its "Events fired" section in `docs/use-cases/<relevant>.md`.

## Common mistakes

- **Service/system publishing an event.** Systems return results; the Initiator (command or scheduled tick) or Handler that called the system publishes.
- **Logic in an event handler that belongs in a system.** Handlers call systems; systems compute. Keep handlers thin.
- **Over-enriched payloads.** If three handlers each read `player.Name` from the event payload, that's fine. If the payload carries the entire player snapshot, redesign.
- **Missing order intent.** If handler B needs B's effect to happen after handler A's, set priorities — don't rely on registration order.

See [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md) for worked anti-patterns.
