# Movement System

> Pure domain system that resolves a direction to a room exit, validates the move, and updates `LocationComponent`. **Authoring checkpoint:** slice 2 (wired). Living document.

## What it is / does

`MovementSystem` is a **domain-tier pure system** that owns the mechanics of player movement between rooms. It looks up the current room's exit for the requested direction, checks that the exit exists, and updates `LocationComponent.RoomEntityId` and `LocationComponent.RoomBlueprintId` on success. It returns a structured `MoveResult` and never publishes events, never calls persistence (INV-5, INV-8). `MoveCommand` is the Initiator: it calls `IMovementSystem.TryMove`, then publishes `PlayerMovedEvent` on success.

## How it works

### Move resolution

`TryMove(playerEntityId, direction)`:

1. Reads `LocationComponent.RoomEntityId` from the entity.
2. Reads `RoomComponent.Exits[direction]` from the current room entity.
3. If no exit exists → returns `MoveResult.Blocked("You can't go that way.")`.
4. Updates `LocationComponent.RoomEntityId = targetRoomEntityId` and `LocationComponent.RoomBlueprintId` (resolved from the target room's `BlueprintComponent`).
5. Returns `MoveResult.Moved(fromRoomEntityId, toRoomEntityId)`.

### `MoveCommand` as Initiator

`MoveCommand` is the thin orchestrator:
1. Calls `IMovementSystem.TryMove`.
2. On success: publishes `PlayerMovedEvent { PlayerEntityId, FromRoomEntityId, ToRoomEntityId, Direction }`.
3. `PlayerMovedHandler` (priority 20) handles the event — broadcasts departure to the old room, arrival to the new room, and triggers `look` for the moving player.

### `PlayerTeleportedByAdminEvent`

`TeleportCommand` (admin) resolves a blueprint ID or player name to a room entity, then calls a direct `LocationComponent` update instead of `IMovementSystem` (no direction involved). It publishes `PlayerTeleportedByAdminEvent`; `PlayerMovedHandler` subscribes to that as well, producing the same broadcast + look experience.

## Interface

- [`IMovementSystem.cs`](../../../Core/Modules/Movement/Systems/IMovementSystem.cs) — `TryMove(playerEntityId, direction) → MoveResult`. Pure: returns `MoveResult { Success, FromRoomEntityId, ToRoomEntityId, ErrorMessage? }`; never touches the bus or persistence.

## Considerations

- **`LocationComponent` carries both ids.** `RoomEntityId` (runtime, `[JsonIgnore]`) is the fast query handle; `RoomBlueprintId` (string, persisted) is the stable cross-restart ref resolved at startup from the hydrated snapshot. `MovementSystem` updates both on every successful move.
- **No state gates yet.** Movement is unconditional in the current model; a future slice can add an `InCombat` or `Incapacitated` gate by checking `IEntityStateService` before the exit lookup.
- **Cross-area exits are transparent.** `RoomComponent.Exits` maps `Direction → entity ID` regardless of area membership. `IAreaSystem` is not consulted during movement.
- **No encumbrance or cooldown.** Movement rate and encumbrance are deferred horizon items (§2 of feature horizon).

## Extensibility

- **Mount/following** — `IMovementSystem` is the single seam; mount or follow mechanics add pre-move validation and post-move side effects in the calling command, not in this system.
- **State gating** — `IEntityStateService.IsInState(entityId, InCombat)` can precede the `TryMove` call in `MoveCommand` with no system change.

## Related

- [`world.md`](world.md) — holistic feature view.
- [`../../reference/systems.md`](../../reference/systems.md) — `IMovementSystem` catalog row (see WorldContentLoader section).
- [`../../reference/components.md`](../../reference/components.md) — `LocationComponent`, `RoomComponent` rows.
- [`../../architecture/03-events.md`](../../architecture/03-events.md) — `PlayerMovedEvent` and `PlayerTeleportedByAdminEvent`.
