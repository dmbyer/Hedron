# Entity Movement

**Status:** partial
**Actors:** Player, Mob
**Module:** `Core/Modules/Movement/`

## Description

An entity moves from one room to another — possibly crossing an area boundary. Movement is either player-initiated (direction command, teleport, portal) or AI-initiated (mob wander).

## Preconditions

- The entity is in a state capable of movement (not stunned, not dead, not mid-cast)
- The destination room/area is accessible (exit exists, no barrier)
- For player: move command received and validated

## Postconditions

- The entity's `TransformComponent.RoomId` (and `AreaId` if changed) reflects the new location
- Origin room and destination room occupants receive arrival/departure notifications (subject to visibility)
- Movement-based triggers (traps, ambushes, room entry scripts) fire
- On failure, the initiator receives the reason

## Main flow

1. Move is initiated (player command / AI / portal / teleport)
2. `MovementSystem.CanMove(entity, direction)` gates it
3. On success, `MovementSystem.Move` updates `TransformComponent`
4. `PlayerExitRoomEvent` fires → notification to origin-room occupants
5. `PlayerEnterRoomEvent` fires → room description to mover, notification to destination-room occupants
6. Triggers evaluate the new location
7. If movement fails, the initiator is notified of the reason

## Events fired

- `PlayerMoveEvent` — published by: `PlayerMovementHandler` on direction command
- `PlayerTeleportEvent` — published by: `SpellHandler` (teleport spell) or admin `goto`
- `PlayerExitRoomEvent` — published by: `PlayerMovementHandler` after `Move`
- `PlayerEnterRoomEvent` — published by: `PlayerMovementHandler` after `Move`

## Systems / handlers

- `MovementSystem` (domain) — validates and executes
- `VisibilitySystem` (domain) — filters notification recipients
- `LocationSystem` (domain) — room/area queries
- `PlayerMovementHandler` — orchestrates
- `NotificationHandler` — witness messages

## Related

- [mob-wandering.md](mob-wandering.md) — AI-initiated movement
- [../architecture/01-layers.md](../architecture/01-layers.md)
