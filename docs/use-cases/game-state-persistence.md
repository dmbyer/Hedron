# Game State Persistence

**Status:** partial
**Actors:** System, Player (optional, for manual saves)
**Module:** `Core/Modules/Persistence/`

## Description

Dirty entities are serialized to disk as JSON. Saves may be triggered automatically (on shutdown, periodic flush, or via events) or manually (player `save` command, admin command).

## Preconditions

- Persistence path is configured and writable
- Entities are tagged as dirty via `PersistenceSystem.MarkDirty`
- Entities carry a `PrototypeComponent` (only prototypes are persisted)

## Postconditions

- Each dirty prototype is serialized to `<PersistencePath>/<Archetype>/<PrototypeId>.json`
- Directory structure is created if missing
- Entities are marked clean on success
- Errors are logged; partial saves do not leave inconsistent state (write-and-rename pattern)

## Main flow

1. Save trigger fires (timer, shutdown, `save` command, `EntityMutatedEvent` for persistent archetypes)
2. `PersistenceHandler` collects dirty prototypes via `PersistenceSystem.GetDirty`
3. For each prototype:
   - `PersistenceSystem.Serialize(entity)` → JSON string
   - Write to temp file; atomic rename to final path
   - Mark clean on success
4. `PersistenceHandler` publishes `StatePersistedEvent` summarizing counts
5. `NotificationHandler` informs the initiator if manual

## Events fired

- `EntityMutatedEvent` — small events that flag individual entities dirty
- `StatePersistedEvent` _(planned)_ — summary after a save pass

## Systems / handlers

- `PersistenceSystem` (core) — serialization + dirty tracking
- `PersistenceHandler` — orchestrator (timers, manual triggers, shutdown)
- `NotificationHandler`

## Design notes

- **Only prototypes persist.** Instances are runtime state and are regenerated via spawning. See [../architecture/02-ecs.md](../architecture/02-ecs.md).
- **Dirty tracking is the source of truth** — don't write a full-world dump on every save.
- **Atomic writes** prevent corruption: write to `<file>.tmp`, then rename.

## Related

- [../architecture/02-ecs.md](../architecture/02-ecs.md) — PrototypeComponent
- [../reference/handlers.md](../reference/handlers.md) — PersistenceHandler
