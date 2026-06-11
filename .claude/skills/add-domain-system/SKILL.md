---
name: add-domain-system
description: Use when adding a new domain (feature) system under Core/Modules/<Feature>/Systems/. Covers interface-first shape, dependency rules (domain-on-core fine; same-or-lower-level domain-on-domain direct calls permitted; peer/lateral coordination prefers events), pure-result pattern, and registration. Invoke when the user asks to add a system, extract gameplay logic, or stand up a new feature module.
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
    // core systems; lower-level domain systems are also permitted (see Dependency rules)
    ...
}
```

## Dependency rules

- **Domain systems can depend on core systems** (DiceSystem, SkillSystem, AttributeCalculator, EffectTracker, TimeSystem, RandomGeneratorSystem).
- **Domain systems may depend on other domain systems** when the dependency is lower-level in the feature graph and does not form a cycle. The architecture checklist (`01-layers.md`) explicitly permits `Domain → Domain (same or lower level)`. Example: `ICombatSystem` calling `IStatSystem` to compute effective stats is valid — `IStatSystem` is a pure-read computation layer that `ICombatSystem` consumes.
- **Prefer event-driven coordination for lateral/peer domain systems** when the callee may itself need to publish downstream events, or when tight coupling would prevent independent testing of each system. This is a design preference, not an invariant.
- **The hard prohibition is Core → Domain** (INV-2). Core systems are generic and must not know about game-specific domain concepts.
- **No `IEventBus` inside any system, domain or core.** Systems compute and return results; Initiators and Handlers publish events (INV-5).

Why: domain systems may compose each other to implement game rules (e.g. combat calls stat system calls attribute system). The constraint is that the dependency graph must remain acyclic and flow downward within the domain layer.

## Pure where possible

Prefer pure resolvers:
- `ResolveAutoAttack(a, d) → AttackResult` — no state mutation
- `ValidateRecipe(player, id) → ValidationResult` — no side effects

Then the handler calls the pure resolver, decides what to do with the result, and mutates state via a second call.

**Void state-mutating methods are also permitted** when the operation is a single atomic mutation with no meaningful return value. Example: `IItemSystem.MoveToInventory(uint itemEntityId, uint holderEntityId)` removes `LocationComponent` from an item and appends it to `InventoryComponent` — there is no useful result to return, and artificially wrapping this in a dummy return type would be noise. `IRoomBuilderSystem.LinkExits` follows the same pattern. "Pure where possible" means prefer a return value when you have a choice; it does not prohibit inherently mutating operations.

The key constraint is the same in both cases: **the system never publishes events or calls persistence** (INV-5, INV-22). Those stay in the command (Initiator). In particular, systems must never inject or call `IPersistenceSystem` — persistence lifecycle is owned by `EntityService` and the periodic flush timer.

## Steps

1. Create the module folder if new: `Core/Modules/<Feature>/Systems/<X>System.cs` + interface `I<X>System.cs`.
2. Register in the feature's `AddXModule(IServiceCollection)` extension (e.g. `Core/Modules/<Feature>/<Feature>Module.cs`). The extension is called from `Server/CompositionRoot.Register` — the **shared, pure-DI engine composition both hosts boot** (the telnet `Server` and the Blazor authoring `Hedron.Web`). Register the system once here and both hosts get it. (Only *hosted services* are composed per-host — `AddGameplayHostedServices` vs `AddContentBootstrapHostedServices` — never in `Register`. A domain system is not a hosted service.)
3. Keep the constructor's dependencies tight — only core systems and `EntityService`.
4. Add the system's signature to [docs/reference/systems.md](../../../docs/reference/systems.md).
5. If a use case now relies on this system, update its "Systems / handlers" list.
6. If the system introduces new components, call **add-component** first. If it introduces new events, handlers publish them — not the system.

## Anti-patterns

- **System that publishes events.** Delete the bus injection; return a result.
- **Lateral/peer domain system call that couples sibling features.** Same-or-lower-level domain-on-domain calls are explicitly permitted (INV-1, `01-layers.md`). Prefer event-driven coordination instead when the callee's result needs to trigger downstream event chains that only a handler can properly publish — this is a design preference, not a blanket prohibition.
- **System that inspects commands or sessions.** Those live in handlers, not systems.
- **Static classes full of gameplay rules.** Those were the legacy shape. Convert to injectable classes.

See [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md) for services-raising-events and domain-leaking-to-core anti-patterns.
