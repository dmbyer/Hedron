# Mob Wandering

**Status:** planned
**Actors:** Mob, System
**Module:** `Core/Modules/AI/` + `Core/Modules/Movement/`

## Description

A mob with wandering behavior moves to a connected room on a timer. Movement obeys area boundaries, combat state, and restrictive tags on exits (no-wander, no-mob).

## Preconditions

- Mob has `AIComponent` with wander behavior enabled
- Mob is not in combat state
- Current room has at least one connected exit that permits wandering
- Destination room accepts mob entries (no restriction flag)

## Postconditions

- Mob's `TransformComponent.RoomId` updated
- Origin and destination room occupants receive arrival/departure messages (visibility-filtered)
- Mob's wander timer resets

## Main flow

1. `AISystem` tick (scheduled via `TimeSystem`)
2. `AISystem.ProcessBehavior(mob)` for each wandering mob
3. `MovementSystem.GetWanderableExits(mob)` — filter exits
4. Random exit selected via `RandomGeneratorSystem`
5. `MovementSystem.Move(mob, exit)` — same code path as player movement
6. `MobMovedEvent` published → `NotificationHandler` messages witnesses
7. Mob's wander timer is rescheduled

## Events fired

- `MobMovedEvent` _(planned)_ — symmetric to player enter/exit events
- `PlayerExitRoomEvent` / `PlayerEnterRoomEvent` are **not** fired for mobs — keep events typed to the actor

## Systems / handlers

- `AISystem`, `MovementSystem`, `VisibilitySystem`, `TimeSystem`
- `AIHandler` — orchestrator
- `NotificationHandler`

## Design notes

- **Movement reuses one system.** Don't write a separate "mob move" code path; `MovementSystem.Move` is agnostic — it just updates `TransformComponent`.
- **Area-boundary rules** (mobs leaving their home area) live as tags on exits, not as special-case code in `AISystem`.
- **Combat short-circuits wandering** — `AISystem.ProcessBehavior` checks state before picking a move.

## Related

- [entity-movement.md](entity-movement.md) — player-initiated counterpart
- [../reference/systems.md](../reference/systems.md) — AISystem, MovementSystem
