---
name: add-core-system
description: Use when adding a core system (DiceSystem, TimeSystem, SkillSystem, etc.) at Core/Systems/ — cross-cutting utilities that multiple domains depend on. Covers the rule that core systems cannot depend on domain systems, and the shape of pure, reusable logic. Invoke when the user asks to add a shared helper system or cross-cutting service like randomness, scheduling, or skill math.
---

# Add a Core System

A core system is cross-cutting — many domains use it, it depends only on other core systems and primitives. Think: dice rolling, scheduling, attribute math, skill improvement rolls, random generation.

Authoritative rules: [docs/architecture/01-layers.md](../../../docs/architecture/01-layers.md) · catalog: [docs/reference/systems.md](../../../docs/reference/systems.md) (core section).

## When is a core system the right call?

Ask:
- Is this logic useful to ≥2 unrelated feature modules? If no → domain system.
- Does the logic know anything about a specific feature (combat, shop, crafting)? If yes → domain system.
- Could this be a pure static function? If yes, consider keeping it as a static helper instead of a system.

Core systems are for genuinely shared, usually-stateful utilities (a scheduler holds timers; a dice system is injectable for testability).

## Shape

```csharp
public interface ISkillSystem
{
    bool TryImprove(uint entityId, SkillId skill, int difficulty);
    int GetLevel(uint entityId, SkillId skill);
}

public class SkillSystem : ISkillSystem
{
    private readonly EntityService _ecs;
    private readonly IDiceSystem _dice;
    // core-only deps
}
```

## Dependency rules

- **Core systems may depend on other core systems** (e.g. `SkillSystem` uses `DiceSystem`).
- **Core systems MUST NOT depend on domain systems.** If a skill calculation needs combat context, the *handler or domain system* gathers that context and passes it in as parameters.
- **Core systems MUST NOT know about handlers or the event bus.**

## Steps

1. Create `Core/Systems/<X>System.cs` + interface `I<X>System.cs`.
2. Register as a singleton in the root DI composition (`Server/Program.cs`, or a dedicated `AddCoreSystems(IServiceCollection)` extension invoked from it).
3. Add signature to [docs/reference/systems.md](../../../docs/reference/systems.md) under the **Core systems** heading.

## Anti-patterns

- **Core system that knows about combat, inventory, or shop concepts.** Move it to that feature's domain system, or split out a core helper that's feature-agnostic (e.g. "range formula" not "combat range formula").
- **Core system that publishes events.** Never. Core is pure computation; events are a domain/handler concern.
- **Core system with a `switch` over feature enums.** Indicates the logic belongs in the feature, not in core.

See domain-leaking-to-core in [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md).
