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
2. **Decide persistence — three persistence domains and two questions per component.** See INV-23 and [docs/architecture/06-persistence.md](../../../docs/architecture/06-persistence.md) for the full model.

   **Which persistence domain does this entity class belong to?**

   | Domain | Examples | `PersistentEntity`? | Data components `[Persistent]`? |
   |---|---|---|---|
   | World structure | Rooms, areas | **Yes** — entity ID stability only | **No** — YAML is authoritative; `WorldContentLoader` refreshes on startup |
   | Respawnable world content | Mobs, world-spawn items (in room) | **No** | N/A |
   | Persistent entities | Players, accounts, player-owned items, crops, items in containers | **Yes** | **Yes** — all state that must survive restart |

   Never add `PersistentEntity` to a mob or world-spawn item entity. Never tag `RoomComponent` or `AreaComponent` as `[Persistent]`.

   **Question A — Should entities of this type survive a restart?**
   Answer using the domain table above. If the entity is world structure, add `PersistentEntity` but note that data components are still excluded from snapshots. If the entity is respawnable world content, do not add `PersistentEntity`. If the entity is a persistent entity, add `PersistentEntity`. If some instances persist and others don't (e.g. a world-spawn item that enters a player's inventory), the construction path diverges at that decision point — not at the component-type level.

   **Question B — If the owning entity IS saved, should this component's data be included in the snapshot?**
   - **Yes → add `[Persistent]`** on the class. `PersistenceSystem` includes it when serializing entities that carry `PersistentEntity`.
   - **No → omit `[Persistent]`** and note why (transient session reference, frame-only state, derived/recomputed on load, YAML-authoritative data).
   - **World structure data components** (`RoomComponent`, `AreaComponent`): always omit `[Persistent]` — YAML is the source of truth; SQLite only stores the entity's marker and blueprint ID.
   - **Existing components touched by this work** must have both questions confirmed, not assumed.
3. Decide the archetype set that requires this component. Update `Core/ECS/ArchetypeRegistry.cs` so the right archetypes include it as required or optional.
4. If it's a shared component, add a one-line row to [docs/reference/components.md](../../../docs/reference/components.md) with its shape and owner. Include the persistence decision (`[Persistent]` or "transient — reason").
5. If any existing system will now read/write it, note the dependency in [docs/reference/systems.md](../../../docs/reference/systems.md).
6. Archetype composition docs: update [docs/reference/archetypes.md](../../../docs/reference/archetypes.md) if a standard archetype changes.

## Anti-patterns

- **Component with behaviour.** Move methods to a system.
- **One mega-component.** Split so each component has one cohesive concept.
- **Component as an event.** Events are published on the bus; components persist between frames.

See [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md).
