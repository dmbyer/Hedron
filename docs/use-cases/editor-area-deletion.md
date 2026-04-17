# Editor: Area Deletion

**Status:** planned
**Actors:** Administrator, Web Editor (Blazor UI)
**Module:** Admin app + `Core/Modules/WorldEditing/`

## Description

An administrator deletes an area via the Blazor editor. All child rooms, their prototype references, and any players currently in the area must be resolved safely. The deletion is persisted atomically — partial deletes must not corrupt on-disk state.

## Preconditions

- Administrator has the required privilege (`AccessControlSystem.IsAdmin`)
- Area prototype exists
- Optional: administrator has confirmed the destructive action via UI prompt

## Postconditions

- Area prototype + all child room prototypes removed from cache and disk
- Any live instances spawned from these prototypes are destroyed
- Players currently in affected rooms are teleported to the configured safe fallback
- Mob/item references from other areas are cleaned or marked orphaned per policy
- UI reflects the removal

## Main flow

1. Admin clicks "Delete Area" → server-side editor action
2. `WorldEditingSystem.PlanAreaDeletion(areaId)` returns a manifest (rooms, exits-into-area, players-present)
3. Admin reviews the impact; confirms
4. `WorldEditingHandler` orchestrates:
   - Teleport each player-in-area via `MovementSystem.Teleport` to fallback room
   - Fire `PlayerTeleportEvent` for each
   - Destroy all instances spawned from prototypes in the area via `EntityService.DestroyEntity`
   - Remove prototypes from `ComponentRepository` and mark for disk deletion
   - `PersistenceHandler` deletes the area's JSON tree on the next flush
5. Handler publishes `AreaDeletedEvent`
6. UI refreshes area list

## Events fired

- `AreaDeletedEvent` _(planned)_
- `PlayerTeleportEvent` — for each displaced player
- `EntityDestroyedEvent` — per destroyed instance

## Systems / handlers

- `WorldEditingSystem` (domain) — planning and execution
- `EntityService` (core) — destroy / unregister
- `MovementSystem` — teleport displaced players
- `PersistenceHandler` — disk cleanup
- `WorldEditingHandler` — orchestrator

## Design notes

- **Plan before execute.** A dry-run manifest gives the admin clear impact before the irreversible step.
- **Atomic on-disk deletion** — write a deletion log, flush, then remove files, so a crash mid-delete is recoverable.
- **Orphan policy is a config** — delete orphaned references vs. mark and quarantine.

## Related

- [editor-mob-deletion-with-inventory.md](editor-mob-deletion-with-inventory.md)
- [../architecture/02-ecs.md](../architecture/02-ecs.md) — prototype vs instance
