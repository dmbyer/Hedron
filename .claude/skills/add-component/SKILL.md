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
2. **Decide persistence — two persistence domains and two questions per component.** See INV-23 and [docs/architecture/06-persistence.md](../../../docs/architecture/06-persistence.md) for the full model.

   **Which persistence domain does this entity class belong to?**

   | Domain | Examples | `PersistentEntity`? | Data components `[Persistent]`? |
   |---|---|---|---|
   | World content | Rooms, areas, mobs, world-spawn items | **No** | N/A — always fresh-spawned from YAML/templates |
   | Persistent entities | Players, accounts, player-owned items, crops, items in containers | **Yes** | **Yes** — all state that must survive restart |

   Never add `PersistentEntity` to rooms, areas, mobs, or world-spawn items. Never tag `RoomComponent` or `AreaComponent` as `[Persistent]`.

   **Question A — Should entities of this type survive a restart?**
   World content (rooms, areas, mobs, world-spawn items) **never** carries `PersistentEntity` — always fresh-spawned from YAML/templates on startup. Persistent entities (players, accounts, player-owned items, crops, items in containers) always carry `PersistentEntity`. If an item transitions between domains at runtime (e.g. a world-spawn item picked up by a player), `ItemContextHandler` (or equivalent) adds `PersistentEntity` when the item enters a persistent context and removes it when dropped — the transition happens in the handler, not the pickup command.

   **Question B — If the owning entity IS saved, should this component's data be included in the snapshot?**
   - **Yes → add `[Persistent]`** on the class. `PersistenceSystem` includes it when serializing entities that carry `PersistentEntity`.
   - **No → omit `[Persistent]`** and note why (transient session reference, frame-only state, derived/recomputed on load, YAML-authoritative data).
   - **Existing components touched by this work** must have both questions confirmed, not assumed.
3. Decide the archetype set that requires this component. Update `Core/ECS/ArchetypeRegistry.cs` so the right archetypes include it as required or optional.
4. If it's a shared component, add a one-line row to [docs/reference/components.md](../../../docs/reference/components.md) with its shape and owner. Include the persistence decision (`[Persistent]` or "transient — reason").
5. If any existing system will now read/write it, note the dependency in [docs/reference/systems.md](../../../docs/reference/systems.md).
6. Archetype composition docs: update [docs/reference/archetypes.md](../../../docs/reference/archetypes.md) if a standard archetype changes.

## Custom dictionary-key types on a persisted component

If a component's dictionary is keyed by a type of your own (not an enum, not a string), the converter mechanics have two traps — precedent: `ProgressionTrack`/`ProgressionTrackJsonConverter` (slice prog-6).

1. **Attach the converter with `[JsonConverter]` on the type itself**, not by registering it in `ComponentSerializer.Options` — that field is `private static`, so nothing can be injected into it.
2. **Override `WriteAsPropertyName`/`ReadAsPropertyName`, not just `Write`/`Read`.** `System.Text.Json` routes *dictionary key* serialization through the property-name pair; a converter that implements only `Write`/`Read` compiles and then silently does nothing for keys. (`AbilitiesComponentJsonConverter` is **not** the precedent here — it is a whole-component converter and says nothing about keys.)

Check the back-compat shape before assuming a migration is needed: `ComponentSerializer.Options` sets `PropertyNamingPolicy = CamelCase` but **not** `DictionaryKeyPolicy`, so enum keys are already emitted as bare enum names. A key type whose serialized form reproduces that exactly (as `ProgressionTrack` does for its score case) widens the component with **no migration** — prove it with a round-trip test asserting a literal pre-change payload re-serializes byte-identically, rather than asserting it in a comment.

## Anti-patterns

- **Component with behaviour.** Move methods to a system.
- **One mega-component.** Split so each component has one cohesive concept.
- **Component as an event.** Events are published on the bus; components persist between frames.

See [docs/architecture/04-pitfalls.md](../../../docs/architecture/04-pitfalls.md).
