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

## Extending a core system without depending on a domain — the contributor seam

Sometimes a core-tier system must aggregate data **owned by domain modules** — e.g. `EffectSystem.GetModifiers` needs passive-ability modifiers (owned by the Abilities domain) and, later, equipment/aura modifiers. It **must not** reference those modules (INV-2), and a synchronous read can't use events. Invert the dependency:

- The core system defines a **contributor port** (e.g. `IEffectContributor`) and DI-collects `IEnumerable<IContributor>`.
- Each domain source ships an **adapter** implementing the port, in *its own* module, and registers it.
- The aggregator sums what is registered and never changes as sources are added (open/closed).
- Contributions are **pulled on read, never materialized** into a stored component (compute-on-read).

The dependency arrow points domain → core interface — legal. This is **INV-24**; the worked precedent is `IEffectContributor` ([docs/architecture/effects.md](../../../docs/architecture/effects.md#the-contributor-seam), one of the three [composition shapes](../../../docs/architecture/01-layers.md#the-three-composition-shapes)). Use this whenever a core aggregator needs heterogeneous domain contributions; do **not** reach for a direct domain reference or push derived state into a component.

> Note: a core-*tier* system may itself live inside a feature module (`Core/Modules/<Feature>/Systems/`) when it is that feature's primary mechanic (e.g. `EffectSystem`). Tier is a role, not a path (INV-2); the rules below apply wherever it sits.

## Definition registries

A **definition registry** is a distinct pattern from a general core system: it is a read-only, keyed lookup table of authored definitions (ability descriptions, aspect vocabularies, score registrations). Use `DefinitionRegistry<TKey, TDef>` (in `Core/Systems/DefinitionRegistry.cs`) rather than a hand-rolled dictionary when:

- A module owns a family of authored definitions looked up by a stable key.
- ≥2 call sites need `TryGet`, `Get`, `AllIds`, or `All` on the same data.

**Key-type rule — pick by family nature, not preference:**

| Family character | Key type | Examples | Why |
|---|---|---|---|
| Fixed, code-owned, closed vocabulary | `enum` | `AspectId`, `ScoreId` | Compile-time safety; enum ordinals are never persisted (INV-23) |
| Open, content-authored, persisted by reference | `string` | `AbilityId`, `EffectId` | Data-file extensible; used in player saves (`AbilitiesComponent.Known`) |

**Shape:** pass a `Func<TDef, TKey> keySelector` to the base constructor — this avoids requiring a shared `IHasId<TKey>` interface when families' key property names differ.

```csharp
public interface IAspectRegistry : IRegistry<AspectId, AspectDefinition> { }
public sealed class AspectRegistry : DefinitionRegistry<AspectId, AspectDefinition>, IAspectRegistry
{
    public AspectRegistry() : base(CreateRows(), d => d.Id) { }
    private static IEnumerable<AspectDefinition> CreateRows() { … }
}
```

Precedents: `AspectRegistry` (enum key) · `AbilityRegistry` · `EffectRegistry` (string key) · `StatRegistry` (`ScoreId` enum).

**Companion: startup validation.** Every slice that adds a new definition family should also extend `Server/RegistryValidationBootstrap.cs` to assert referential integrity at boot (dangling cross-refs fail startup with a full report — INV-10). The generic `defs <family> [id]` admin inspector covers any `IRegistry`-implementing registry automatically (INV-18).

## Steps

1. Create `Core/Systems/<X>System.cs` + interface `I<X>System.cs`.
2. Register as a singleton in the root DI composition (`Server/Program.cs`, or a dedicated `AddCoreSystems(IServiceCollection)` extension invoked from it).
3. Add signature to [docs/reference/systems.md](../../../docs/reference/systems.md) under the **Core systems** heading.

## Anti-patterns

- **Core system that knows about combat, inventory, or shop concepts.** Move it to that feature's domain system, or split out a core helper that's feature-agnostic (e.g. "range formula" not "combat range formula").
- **Core system that publishes events.** Never. Core is pure computation; events are a domain/handler concern.
- **Core system with a `switch` over feature enums.** Indicates the logic belongs in the feature, not in core.

See domain-leaking-to-core in [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md).
