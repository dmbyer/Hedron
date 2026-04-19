# Game State Persistence

**Status:** partial
**Actors:** System, Player (optional, for manual saves)
**Module:** `Core/Modules/Persistence/`

## Description

Dirty entities are serialized to disk. Saves may be triggered automatically (on shutdown, periodic flush, or via events) or manually (player `save` command, admin command).

## Preconditions

- Persistence path is configured and writable
- Entities are tagged dirty via `PersistenceSystem.MarkDirty`
- Entities carry at least one component type marked `[Persistent]` (otherwise the entity is transient and isn't saved)

## Postconditions

- Each dirty entity is serialized to `<PersistencePath>/<Archetype>/<EntityId>.json`; the file contains only its `[Persistent]` components
- Directory structure is created if missing
- Entities are marked clean on success
- Errors are logged; partial saves do not leave inconsistent state (write-and-rename pattern)

## Main flow

1. Save trigger fires (timer, shutdown, `save` command, `EntityMutatedEvent` for entities with `[Persistent]` components)
2. `PersistenceHandler` collects dirty entities via `PersistenceSystem.GetDirty`
3. For each entity:
   - `PersistenceSystem.Serialize(entityId)` → JSON of the entity's `[Persistent]` components only
   - Write to temp file; atomic rename to final path
   - Mark clean on success
4. `PersistenceHandler` publishes `StatePersistedEvent` summarizing counts
5. `NotificationHandler` informs the initiator if manual

## Load path

On host startup:

1. `PersistenceSystem.Hydrate()` walks the persistence directory
2. For each saved entity file: create the entity via `EntityService.CreateEntity()`, deserialize the stored components, attach each via `entityService.AddComponent` — **with no events published** (silent load)
3. `TemplateRegistry` then seeds any authored content (rooms, mobs, items) that wasn't found in the persisted set — persisted components win over template defaults
4. Systems that own transient state (combat, sessions, pathing caches) re-attach their own components as needed at runtime

## Events fired

- `EntityMutatedEvent` — small events that flag individual entities dirty
- `StatePersistedEvent` _(planned)_ — summary after a save pass

## Systems / handlers

- `PersistenceSystem` (core) — serialization, dirty tracking, hydration
- `PersistenceHandler` — orchestrator (timers, manual triggers, shutdown)
- `NotificationHandler`

## Design notes

- **Persistence is per-component-type.** Components marked `[Persistent]` are saved; untagged components are rebuilt on demand. There is no prototype cache and no separate "instance save" path — one world, one save model.
- **Blueprint seeds, persisted state wins.** Authored templates seed the world on first boot (or for entities that haven't been persisted). Once a `[Persistent]` component is saved for an entity, that state wins over the blueprint on subsequent boots. Handles the "player changed an authored room permanently" case without a special prototype/instance split.
- **Silent load.** Hydration writes components directly via `EntityService.AddComponent` without going through the event bus — runtime change events must not fire for "this entity was loaded from disk."
- **Dirty tracking is the source of truth.** Don't write a full-world dump on every save.
- **Atomic writes** prevent corruption: write to `<file>.tmp`, then rename.

## Related

- [../architecture/02-ecs.md](../architecture/02-ecs.md) — `[Persistent]` attribute, blueprint-seeds-world, silent load
- [../reference/handlers.md](../reference/handlers.md) — PersistenceHandler
