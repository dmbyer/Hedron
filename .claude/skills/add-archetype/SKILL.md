---
name: add-archetype
description: Use when adding a new entity archetype (e.g. Portal, Trigger, Mount, NPC variant) or when the user asks "what archetype should this be?". Archetypes are validation + detection, not construction — covers the enum entry, ArchetypeDefinition, registry wiring, and doc updates. Invoke when introducing a new gameplay entity class or validating an existing archetype composition.
---

# Add an Entity Archetype

An archetype is a named component composition used for **validation and detection** — *"does this entity carry the components a Weapon must have?"* and *"given an unknown entity, which archetype does it look like?"*. Archetypes do not build entities. Construction lives in `TemplateRegistry` (authored content) or feature-specific systems (`ItemGeneratorSystem`, `PlayerCreationSystem`, etc.).

Authoritative rules: [docs/architecture/02-ecs.md](../../../docs/architecture/02-ecs.md) · catalog: [docs/reference/archetypes.md](../../../docs/reference/archetypes.md).

## Before you add one

First check [docs/reference/archetypes.md](../../../docs/reference/archetypes.md) — there are already 15 archetypes. Most new ideas are a **variant** (different default data) of an existing archetype, not a new archetype. A new archetype is only justified when the **required component set** differs.

Examples:
- A "cursed weapon" → still `Weapon` archetype; curse is an effect on `PersistentEffectsComponent`.
- A "shopkeeper" → still `Mob` archetype; a `ShopComponent` is added.
- A "portal" → new archetype; its required components (`TransformComponent` + `PortalComponent`) differ from Static Item.

## Steps

1. **Add to `EntityArchetype` enum** — `Core/ECS/EntityArchetype.cs`.
2. **Build its `ArchetypeDefinition`** in `Core/ECS/ArchetypeRegistry.cs`:
   - List required component types (`Validate` checks these)
   - List optional component types (documentary — systems may add later)
3. **Update detection order** in `ArchetypeRegistry` if the required set overlaps with an existing archetype — more specific archetypes must be detected first.
4. **Update `docs/reference/archetypes.md`** with the archetype's row: required components, optional components, example use.
5. **Construction** happens elsewhere:
   - If the archetype is authored content, add a template to `TemplateRegistry` for it.
   - If it's generated at runtime, the feature owning the logic (`ItemGeneratorSystem`, `LootSystem`, etc.) builds the entity via `EntityService.CreateEntity()` + `AddComponent` calls.
6. If the archetype needs new feature-specific components, add those via the **add-component** skill before wiring them here.
7. If the archetype has its own feature logic (a `PortalSystem`, for example), stub the module via **add-domain-system**.

## Required-vs-optional rule

- **Required**: the entity cannot meaningfully exist without this component. Going from absent → present is a bug, not a state change. `Validate` returns false if missing.
- **Optional**: the entity may have it; systems check presence before using it.

If you'd ever construct the entity *without* component X and then attach X conditionally, X is optional.

## Do not

- Add archetypes that differ only by data values — that's a template-level concern (`Weapon` template with tier data, not `WeakWeapon` vs `StrongWeapon`).
- Give an archetype inheritance-like parents. Archetypes are flat.
- Put construction logic in the archetype definition. Construction code lives in the system or template that builds the entity, not in the registry.
- Reintroduce a `PrototypeComponent` or cache-type marker. Templates handle authored content; `[Persistent]` on components handles save/load.
