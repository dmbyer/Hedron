---
name: migrate-legacy-to-ecs
description: Use when converting legacy inheritance-based entity code (Player, Mob, ItemWeapon, ItemPotion, Storage, EntityContainer, Room, Area, World) to the ECS archetype + component pattern. Covers the migration recipe, is/as replacement, factory call-site updates, and how to stage waves of changes safely. Invoke when the user says "migrate this class", "kill the deprecated entity", or is executing any wave from docs/roadmap/api-alignment-plan.md.
---

# Migrate Legacy Code to ECS

This is the most common edit pattern during the ongoing API alignment (see [docs/roadmap/api-alignment-plan.md](../../../docs/roadmap/api-alignment-plan.md)). The legacy `Core/ECS/DEPRECATED - Entities/` classes still run the game; every feature touched should move to ECS on the way past.

Authoritative references: [docs/architecture/02-ecs.md](../../../docs/architecture/02-ecs.md), [docs/reference/archetypes.md](../../../docs/reference/archetypes.md), [docs/reference/components.md](../../../docs/reference/components.md).

## The recipe

For each legacy class:

1. **Find its archetype.** See the table in [docs/reference/archetypes.md](../../../docs/reference/archetypes.md). `Player` → `EntityArchetype.Player`, `ItemWeapon` → `Weapon`, `Room` → `Room`, etc.
2. **Find every construction site.** `new Player()`, `new ItemWeapon()`, `Player.NewPrototype()`, factory helpers in `Core/Factory/*`. Each one becomes:
   ```csharp
   var id = EntityFactory.CreateEntity(EntityArchetype.Player, CacheType.Instance, name);
   var data = entityService.GetComponent<PlayerDataComponent>(id);
   data.Field = ...;
   ```
3. **Find every `is`/`as` check.**
   ```csharp
   // before
   if (entity is Player p) { ... }
   var w = entity as ItemWeapon;

   // after
   if (entityService.HasComponent<PlayerDataComponent>(id))
   {
       var pdata = entityService.GetComponent<PlayerDataComponent>(id);
       ...
   }
   ```
4. **Find every property access.** `player.HP` becomes `entityService.GetComponent<PoolsComponent>(id).CurrentHP`. If that pattern repeats, extract a domain helper (`PoolsSystem.GetHP(id)`) — don't scatter the boilerplate.
5. **Find every downcast chain** like `var room = player.GetInstanceParentRoom()`. Replace with `locationSystem.GetRoom(playerId)`. Add the helper to `LocationSystem` if missing.
6. **Delete the legacy class** once zero references remain. `git grep "class Player"` must return nothing under `Core/ECS/DEPRECATED`.

## Stage the waves

One legacy class at a time, or one archetype family at a time (e.g. "all items today, all living entities tomorrow"). Each wave ends with:
- `dotnet build Hedron.sln` clean
- The game starts and the feature works
- The deleted class is gone from the repo

**Never** leave a half-migrated class where some call sites use the legacy constructor and others use `EntityFactory`. Pick one or the other for the whole wave.

## Common gotchas

- **`EcsManager.World` doesn't exist yet.** If the code you're migrating references it, it was written aspirationally. Route to an injected `EntityService` instead (resolve via DI — see Wave 0 in the alignment plan).
- **Prototype vs instance.** `CacheType.Prototype` for authored/editor content, `CacheType.Instance` for spawned runtime entities. See [docs/architecture/02-ecs.md](../../../docs/architecture/02-ecs.md#prototype-vs-instance).
- **`Storage` and `EntityContainer`** both collapse into `ContainerDataComponent` + (sometimes) `InventoryComponent`. Check what role the legacy usage is playing — spatial containment (room contains entities) vs carried inventory (entity carries items) — and pick the matching component.
- **Commands still use `DataAccess.Get<Player>`.** Replace with `entityService.GetComponent<PlayerDataComponent>(playerId)` plus any other components the command needed.

## When to stop

If the scope balloons (you touched 40 files to migrate one verb), stop and split the change. The point of the migration is small, green, shippable waves — not a big-bang rewrite.

Update [docs/roadmap/ecs-migration-status.md](../../../docs/roadmap/ecs-migration-status.md) when a wave completes.
