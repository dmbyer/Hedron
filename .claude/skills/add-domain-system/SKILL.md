---
name: add-domain-system
description: Use when adding a new domain (feature) system under Core/Modules/<Feature>/Systems/. Covers interface-first shape, dependency rules (domain-on-core is fine; domain-on-domain via events only), pure-result pattern, and registration. Invoke when the user asks to add a system, extract gameplay logic, or stand up a new feature module.
---

# Add a Domain System

A domain system is the "rules" layer for a feature — combat math, loot decisions, crafting validation, etc. It is a plain class injected via DI. It **returns results**; handlers publish events based on those results.

Authoritative rules: [docs/architecture/01-layers.md](../../../docs/architecture/01-layers.md) · catalog: [docs/reference/systems.md](../../../docs/reference/systems.md).

## Shape

```csharp
public interface ICombatSystem
{
    AttackResult ResolveAutoAttack(uint attackerId, uint defenderId);
    DamageResult ApplyDamage(uint targetId, int amount);
    void InitiateCombat(uint attackerId, uint targetId);
}

public class CombatSystem : ICombatSystem
{
    private readonly EntityService _ecs;
    private readonly IDiceSystem _dice;
    // core systems only; no other domain systems injected
    ...
}
```

## Dependency rules

- **Domain systems can depend on core systems** (DiceSystem, SkillSystem, AttributeCalculator, EffectTracker, TimeSystem, RandomGeneratorSystem).
- **Domain systems should NOT depend on other domain systems directly.** Coordinate cross-domain effects through handlers that publish events.
- **No `IEventBus` inside a domain system.** Services don't publish — handlers do.

Why: this keeps systems composable, unit-testable, and free of side effects.

## Pure where possible

Prefer pure resolvers:
- `ResolveAutoAttack(a, d) → AttackResult` — no state mutation
- `ValidateRecipe(player, id) → ValidationResult` — no side effects

Then the handler calls the pure resolver, decides what to do with the result, and mutates state via a second call.

## Steps

1. Create the module folder if new: `Core/Modules/<Feature>/Systems/<X>System.cs` + interface `I<X>System.cs`.
2. Register in the module's `IModule.Register` via DI.
3. Keep the constructor's dependencies tight — only core systems and `EntityService`.
4. Add the system's signature to [docs/reference/systems.md](../../../docs/reference/systems.md).
5. If a use case now relies on this system, update its "Systems / handlers" list.
6. If the system introduces new components, call **add-component** first. If it introduces new events, handlers publish them — not the system.

## Anti-patterns

- **System that publishes events.** Delete the bus injection; return a result.
- **System that calls another domain system.** Insert a handler between them.
- **System that inspects commands or sessions.** Those live in handlers, not systems.
- **Static classes full of gameplay rules.** Those were the legacy shape. Convert to injectable classes.

See [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md) for services-raising-events and domain-leaking-to-core anti-patterns.
