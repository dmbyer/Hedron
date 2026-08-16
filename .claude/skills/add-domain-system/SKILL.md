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

**Void state-mutating methods are also permitted** when the operation is a single atomic mutation with no meaningful return value. Examples:
- `IItemSystem.MoveToInventory(uint itemEntityId, uint holderEntityId)` removes `LocationComponent` from an item and appends it to `InventoryComponent` — no useful result to return.
- `IItemSystem.MoveBetweenInventories(uint itemEntityId, uint fromHolderEntityId, uint toHolderEntityId)` removes an item from one holder's `InventoryComponent` and appends it to another's — touches no `LocationComponent` and no `BlueprintComponent` (INV-21); no useful result.
- `IRoomBuilderSystem.LinkExits` follows the same pattern.

"Pure where possible" means prefer a return value when you have a choice; it does not prohibit inherently mutating operations.

The key constraint is the same in both cases: **the system never publishes events or calls persistence** (INV-5, INV-22). Those stay in the command (Initiator). In particular, systems must never inject or call `IPersistenceSystem` — persistence lifecycle is owned by `EntityService` and the periodic flush timer.

## Stateful / cached systems

Most domain systems are stateless — they read `EntityService` and compute. A few legitimately hold
state: `ITemplateRegistry` (boot-loaded blueprint definitions) and `IContentDefinitionCatalog` (an
in-memory index over the on-disk YAML corpus, added by authoring-editor-repair). If you are adding a
cache to a system, four rules apply — do **not** copy the stateless shape and hand-roll around them.

1. **Invalidate at every mutator, and invalidate what the write actually reaches.** A write that
   cascades to other entries cannot be invalidated entry-scoped. If any mutator has a cascade, drop
   the whole cache — that is what `ContentDefinitionCatalog` does, because delete clears fields on
   referrers, rename rewrites them, and a bidirectional room save writes a *different* room.
2. **Pick population granularity against the caller's loop, not the happy path.** Whole-cache
   invalidation plus corpus-wide population makes any `Load`→write loop quadratic. Cache
   per-entry-on-demand so a read after an invalidation is one read, not a sweep.
3. **Declare the concurrency posture (INV-31).** A DI singleton reached from multiple Blazor
   circuits (or any background initiator) is concurrent state. **A thread-affine
   `ReaderWriterLockSlim` is unusable if any mutator is `async`** — it cannot be held across an
   `await`, and every write-then-invalidate path has an `await` in the middle. Use an immutable
   snapshot object swapped under a plain `lock`: readers take the reference with no lock, writers
   build and swap. If population is lazy, every reader is also a writer — carry a generation (or
   publish into the captured snapshot object, which `Invalidate` has already detached) so a read
   that began before a concurrent write cannot republish pre-write state.
4. **Serialize the mutators when there is more than one entry point — with a non-re-entrant gate,
   deliberately shaped.** Rule 3 guards *cache consistency*; it does nothing for two callers writing
   at once. Once mutators can run concurrently from more than one entry point (a Blazor circuit
   **and** an HTTP request thread, say), wrap each public mutator in a `SemaphoreSlim(1,1)` +
   `WaitAsync` — a *guard*, not confinement, and async-compatible for the same reason rule 3 avoids
   a thread-affine lock. One critical section spans the write cascade **and** the invalidation that
   ends it. Serializing also closes the check-then-write TOCTOU that a create-guard has by
   construction.

   **The trap: the gate is not re-entrant, and the failure is a silent hang, not a compile error.**
   If any public mutator is defined in terms of another (`CreateAsync` → `SaveAsync` on
   `ContentDefinitionCatalog`), taking the semaphore inside every public method deadlocks the moment
   that path runs. Required shape: a private `*Core` body per mutator that calls **only** other
   `*Core` bodies, plus a thin public wrapper that takes the gate exactly once. Never call a public
   mutator from inside the gate. Test the composed path explicitly — with a timeout, so a
   regression fails the suite instead of hanging it.

   Readers stay lock-free, which puts three requirements on everything *outside* the system that its
   reads touch. `ContentDefinitionCatalog` needed all three: writes publish via
   `AtomicFileWrite` (write-temp-then-`File.Replace`), reads open with
   `FileShare.ReadWrite | FileShare.Delete`, and the YAML deserializers are thread-local (YamlDotNet's
   is not thread-safe). Check the libraries and I/O your read path uses before claiming reads are safe
   unguarded — "it is only a read" is not an argument.

5. **Cache what is safe to hand out.** If callers mutate what they get back (editors bind forms to
   it), cache the raw source and re-materialize per call. Handing out a shared mutable instance
   leaks in-progress edits into the cache.

Record the posture in the system's XML docs *and* its [`docs/reference/systems.md`](../../../docs/reference/systems.md)
row, and test the invalidation — one test per mutator, plus one that asserts a *cascaded* entry is
observed on the next read.

## Steps

1. Create the module folder if new: `Core/Modules/<Feature>/Systems/<X>System.cs` + interface `I<X>System.cs`.
2. Register in the feature's `AddXModule(IServiceCollection)` extension (e.g. `Core/Modules/<Feature>/<Feature>Module.cs`). The extension is called from `Server/CompositionRoot.Register` — the **shared, pure-DI engine composition both hosts boot** (the telnet `Server` and the Blazor authoring `Hedron.Web`). Register the system once here and both hosts get it. (Only *hosted services* are composed per-host — `AddGameplayHostedServices` vs `AddContentBootstrapHostedServices` — never in `Register`. A domain system is not a hosted service.)
3. Keep the constructor's dependencies tight — only core systems and `EntityService`.
4. Add the system's signature to [docs/reference/systems.md](../../../docs/reference/systems.md).
5. If a use case now relies on this system, update its "Systems / handlers" list.
6. If the system introduces new components, call **add-component** first. If it introduces new events, handlers publish them — not the system.
7. **Does this system affect power, and how does its contribution enter the snapshot?** If the system computes or grants something that should feed the power-budget oracle (`IPowerBudgetSystem`), it never gains a reference to the oracle's internals or teaches it a domain concept — it either (a) contributes via an existing/new `ScoreId` the caller already includes in its `PowerSnapshot`, or (b) computes its own estimated contribution that the caller sums into the snapshot before calling `Estimate` (the `IEffectContributor` precedent). See [`docs/design/power-model.md`](../../../docs/design/power-model.md) — this is a hard rule, not a suggestion, and violating it breaks INV-2.

## Anti-patterns

- **System that publishes events.** Delete the bus injection; return a result.
- **Lateral/peer domain system call that couples sibling features.** Same-or-lower-level domain-on-domain calls are explicitly permitted (INV-1, `01-layers.md`). Prefer event-driven coordination instead when the callee's result needs to trigger downstream event chains that only a handler can properly publish — this is a design preference, not a blanket prohibition.
- **System that inspects commands or sessions.** Those live in handlers, not systems.
- **Static classes full of gameplay rules.** Those were the legacy shape. Convert to injectable classes.

See [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md) for services-raising-events and domain-leaking-to-core anti-patterns.
