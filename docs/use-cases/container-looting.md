# Container Looting

**Status:** planned
**Actors:** Player, Container entity
**Module:** `Core/Modules/Inventory/` (container access)

## Description

A player takes items from a container in the current room (chest, corpse, pile, bag on the ground). Access control checks run first; matching items are moved to the player's inventory.

## Preconditions

- A container entity (has `InventoryComponent` and optionally `AccessControlComponent`) exists in the player's room
- Player has sufficient privilege (if `AccessControlComponent` is present)
- Target item(s) exist in the container

## Postconditions

- Items are moved from the container's `InventoryComponent` to the player's
- Container state is updated (empty corpses may be queued for cleanup)
- Player receives a per-item feedback line
- Witnesses may see a "looting" message (visibility-filtered)

## Main flow

1. `get <items> from <container>` command → `InventoryHandler`
2. `LocationSystem.FindContainerInRoom(player, name)` — resolve container
3. `AccessControlSystem.CanAccess(player, container)` — gate (see [access-control-violation.md](access-control-violation.md))
4. `InventorySystem.MatchItems(container, query)` — find matches
5. For each item: `InventorySystem.Move(container, player, item)`
6. `InventoryHandler` publishes `ItemMovedEvent` per item
7. If the container is empty and ephemeral (e.g. corpse), `ContainerEmptiedEvent` triggers cleanup
8. `NotificationHandler` messages the player + witnesses

## Events fired

- `ItemMovedEvent` — per transferred item
- `ContainerEmptiedEvent` _(planned)_ — for ephemeral containers

## Systems / handlers

- `InventorySystem`, `AccessControlSystem`, `LocationSystem`, `VisibilitySystem`
- `InventoryHandler` — orchestrator
- `NotificationHandler`

## Design notes

- **Containers are entities, not special data.** Any entity with `InventoryComponent` can be looted — corpses, chests, tables, packs on the floor.
- **Ephemeral vs persistent** containers differ only in what fires `ContainerEmptiedEvent`. The looting logic is identical.

## Related

- [access-control-violation.md](access-control-violation.md)
- [mob-death-and-loot.md](mob-death-and-loot.md)
