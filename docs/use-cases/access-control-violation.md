# Access Control Violation

**Status:** planned
**Actors:** Player, restricted entity
**Module:** `Core/Modules/AccessControl/` (cross-cutting)

## Description

A player attempts to access a container, room, or item guarded by an `AccessControlComponent`. The check fails; the action is denied with a clear message; no state mutates.

## Preconditions

- Target entity has `AccessControlComponent` (privilege level, key ID, faction, etc.)
- Player's profile does not satisfy the requirement
- Action routes through an access-gated command (`get`, `open`, `enter`, `look in`, admin commands)

## Postconditions

- Action is aborted; no items moved, no state changed
- Player receives a failure message appropriate to the gate type (locked, restricted, need-key)
- Optional audit entry logged via `AccessViolationEvent`

## Main flow

1. Gated command reaches its handler (e.g. `InventoryHandler` for `get`)
2. Handler calls `AccessControlSystem.CanAccess(player, target)` before any mutation
3. On deny, handler **does not** proceed with the mutation
4. Handler publishes `AccessViolationEvent` (low-priority, for logging)
5. `NotificationHandler` sends a context-appropriate denial message

## Events fired

- `AccessViolationEvent` _(planned)_ — attempt, target, reason

## Systems / handlers

- `AccessControlSystem` (domain) — decides; never mutates
- Any gated handler (Inventory, Movement, Shop, Admin)
- `NotificationHandler`, `PersistenceHandler` (if audit log is enabled)

## Design notes

- **Check first, mutate second.** The gate is the first line of every gated handler — never layer it inside the mutation call.
- **One gate system, many callers.** `AccessControlSystem.CanAccess` is the single source of truth for "may X do Y to Z?" to avoid drift.
- **Failure reasons are structured** (`AccessDenyReason` enum) so notification copy can vary without leaking enforcement logic to notification code.

## Related

- [container-looting.md](container-looting.md)
- [../architecture/04-pitfalls.md](../architecture/04-pitfalls.md) — god handlers
