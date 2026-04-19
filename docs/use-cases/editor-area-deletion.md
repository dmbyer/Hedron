# Editor: Area Template Deletion

**Status:** planned (revisit when admin-tooling scope is settled — see Ticket B in [../roadmap/plan.md](../roadmap/plan.md); the original "Blazor editor" framing is no longer current now that Blazor has been dropped)
**Actors:** Administrator, admin tool
**Module:** Admin tooling + `Core/Modules/WorldEditing/`

## Description

An administrator deletes an area template. All child room templates, any cross-area template references, and any players currently standing in affected rooms must be resolved safely. The deletion is persisted atomically — partial deletes must not corrupt on-disk state.

## Preconditions

- Administrator has the required privilege (`AccessControlSystem.IsAdmin`)
- Area template exists in `TemplateRegistry`
- Optional: administrator has confirmed the destructive action via UI prompt

## Postconditions

- Area template + all child room templates removed from `TemplateRegistry` and disk
- Any live entities spawned from these templates are destroyed
- Players currently in affected rooms are teleported to the configured safe fallback
- Mob/item references from other areas are cleaned or marked orphaned per policy
- UI reflects the removal

## Main flow

1. Admin clicks "Delete Area" in the admin tool
2. `WorldEditingSystem.PlanAreaTemplateDeletion(areaTemplateId)` returns a manifest (room templates, exits-into-area, players-present)
3. Admin reviews the impact; confirms
4. `WorldEditingHandler` orchestrates:
   - Teleport each player-in-area via `MovementSystem.Teleport` to fallback room
   - Fire `PlayerTeleportEvent` for each
   - Destroy all live entities spawned from templates in the area via `EntityService.DestroyEntity`
   - Remove templates from `TemplateRegistry` and mark for disk deletion
   - `PersistenceHandler` deletes the area's on-disk files on the next flush
5. Handler publishes `AreaTemplateDeletedEvent`
6. UI refreshes area list

## Events fired

- `AreaTemplateDeletedEvent` _(planned)_
- `PlayerTeleportEvent` — for each displaced player
- `EntityDestroyedEvent` — per destroyed entity

## Systems / handlers

- `WorldEditingSystem` (domain) — planning and execution
- `EntityService` (core) — destroy / unregister
- `TemplateRegistry` — template removal
- `MovementSystem` — teleport displaced players
- `PersistenceHandler` — disk cleanup
- `WorldEditingHandler` — orchestrator

## Design notes

- **Plan before execute.** A dry-run manifest gives the admin clear impact before the irreversible step.
- **Atomic on-disk deletion** — write a deletion log, flush, then remove files, so a crash mid-delete is recoverable.
- **Orphan policy is a config** — delete orphaned references vs. mark and quarantine.

## Related

- [editor-mob-deletion-with-inventory.md](editor-mob-deletion-with-inventory.md)
- [../architecture/02-ecs.md](../architecture/02-ecs.md) — templates, one-world model, `[Persistent]` components
