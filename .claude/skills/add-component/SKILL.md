---
name: add-component
description: Use when adding a new ECS component. Handles file placement (shared vs feature-owned), shape of the component class, registration on archetypes, and updating docs/reference/components.md. Invoke when the user asks to add a component, extract state into a component, or split an existing component.
---

# Add a Component

A component is a POCO with public fields/auto-properties. No behaviour, no cross-references, no constructors beyond default. Components are the only place runtime state lives.

Authoritative rules: [docs/architecture/02-ecs.md](../../../docs/architecture/02-ecs.md) · catalog: [docs/reference/components.md](../../../docs/reference/components.md).

## Decision: shared vs feature-owned

- **Shared** (many features read it) → `Core/ECS/Components/<X>Component.cs`
- **Feature-owned** (one feature mutates, others mostly read or ignore) → `Core/Modules/<Feature>/Components/<X>Component.cs`

When in doubt, start feature-owned. Promote to shared only when a second feature *mutates* it.

## Shape

```csharp
namespace Hedron.Core.ECS.Components;

public class FooComponent : IComponent
{
    public int Bar { get; set; }
    public List<uint> Related { get; set; } = new();
}
```

Do **not**:
- Add logic methods (belongs on a system)
- Reference other components (the system composes the lookup)
- Include events/delegates (use the event bus)
- Use inheritance for components (composition only)

## Steps

1. Create the file at the chosen location.
2. **Decide persistence opt-in.** Explicitly answer: does this component's data need to survive a server restart?
   - **Yes → add `[Persistent]`** on the class. `PersistenceSystem` will save and restore it automatically.
   - **No → omit `[Persistent]`** and document why (e.g. transient session reference, frame-only state, derived/recomputed on load).
   - **Unsure?** Default to `[Persistent]` for any component that holds world or character state a player or designer authored. Default to omitting it for components that hold runtime-only references (`Session`, timers, cached lookups). If the decision is non-obvious, call it out explicitly in the use-case doc's Cross-cutting surfaces section.
   - **Existing components touched by this work** must have their `[Persistent]` status confirmed as part of this step, not assumed.
3. Decide the archetype set that requires this component. Update `Core/ECS/ArchetypeRegistry.cs` so the right archetypes include it as required or optional.
4. If it's a shared component, add a one-line row to [docs/reference/components.md](../../../docs/reference/components.md) with its shape and owner. Include the persistence decision (`[Persistent]` or "transient — reason").
5. If any existing system will now read/write it, note the dependency in [docs/reference/systems.md](../../../docs/reference/systems.md).
6. Archetype composition docs: update [docs/reference/archetypes.md](../../../docs/reference/archetypes.md) if a standard archetype changes.

## Anti-patterns

- **Component with behaviour.** Move methods to a system.
- **One mega-component.** Split so each component has one cohesive concept.
- **Component as an event.** Events are published on the bus; components persist between frames.

See [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md).
