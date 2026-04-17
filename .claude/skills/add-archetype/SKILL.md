---
name: add-archetype
description: Use when adding a new entity archetype (e.g. Portal, Trigger, Mount, NPC variant) or when the user asks "what archetype should this be?". Covers the EntityArchetype enum entry, ArchetypeDefinition, registry wiring, and doc updates. Invoke when introducing a new gameplay entity class or validating an existing archetype composition.
---

# Add an Entity Archetype

An archetype is a named component composition. `EntityFactory.CreateEntity(EntityArchetype.X, ...)` builds an entity with that archetype's required components; optional ones are attached later by systems.

Authoritative rules: [docs/architecture/02-ecs.md](../../../docs/architecture/02-ecs.md) · catalog: [docs/reference/archetypes.md](../../../docs/reference/archetypes.md).

## Before you add one

First check [docs/reference/archetypes.md](../../../docs/reference/archetypes.md) — there are already 15 archetypes. Most new ideas are a **variant** (different default data) of an existing archetype, not a new archetype. A new archetype is only justified when the **required component set** differs.

Examples:
- A "cursed weapon" → still `Weapon` archetype; curse is an `EffectsComponent` entry.
- A "shopkeeper" → still `Mob` archetype; a `ShopComponent` is added.
- A "portal" → new archetype; its required components (TransformComponent + PortalComponent) differ from Static Item.

## Steps

1. **Add to `EntityArchetype` enum** — `Core/ECS/EntityArchetype.cs`.
2. **Build its `ArchetypeDefinition`** in `Core/ECS/ArchetypeRegistry.cs`:
   - List required component types (what `EntityFactory` auto-attaches)
   - List optional component types (documentary — systems may add later)
3. **Update `docs/reference/archetypes.md`** with the archetype's row: required components, optional components, example use.
4. If the archetype needs new feature-specific components, add those via the **add-component** skill before wiring them here.
5. If the archetype has its own feature logic (a `PortalSystem`, for example), stub the module via **add-domain-system**.

## Required-vs-optional rule

- **Required**: the entity cannot meaningfully exist without this component. Going from absent → present is a bug, not a state change.
- **Optional**: the entity may have it; systems check presence before using it.

If you'd ever spawn the entity *without* component X and then attach X conditionally, X is optional.

## Do not

- Add archetypes that differ only by data values — that's a prototype-level concern (`Weapon` + Tier data, not `WeakWeapon` vs `StrongWeapon`).
- Give an archetype inheritance-like parents. Archetypes are flat.
- Forget `PrototypeComponent` — every archetype requires it (all persistable entities are prototypes or instances of prototypes).
