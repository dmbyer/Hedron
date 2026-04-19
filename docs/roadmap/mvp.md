# MVP Specification

> **Purpose.** Defines the thinnest viable Hedron. Every Phase 2 task rolls up to "this works." Phase 3 vertical slices add behavior one at a time, each compared against its own spec in `docs/use-cases/`. Nothing in this doc is aspirational — if MVP doesn't need it, it isn't here.

## Scope

The smallest thing that proves the target architecture works end-to-end: a multi-user walking simulator with a chat line. No combat, NPCs, inventory, items, skills, persistence, admin UI, or authentication beyond "pick a name."

## Behaviors

1. **Telnet listener** accepts concurrent client connections on a configurable port.
2. **Login** — connecting client is prompted for a name; that name becomes their display name. No password, no account record, no persistence. Duplicate names get a numeric suffix or a reprompt — implementer's choice.
3. **Hand-authored world** — three connected rooms, declared in code at startup (no JSON, no editor). Each room has a name, a description, and exits to other rooms.
4. **`look`** — prints the current room's name, description, exit list, and the names of other players present.
5. **`north` / `south` / `east` / `west` / `up` / `down`** (and short aliases `n/s/e/w/u/d`) — moves the player between rooms if an exit exists in that direction. Players in the departed room see a "<name> leaves <direction>" message; players in the arrival room see a "<name> arrives from <opposite direction>" message. The moving player sees the new room (same output as `look`).
6. **`say <message>`** — broadcasts "<name> says: <message>" to every player in the same room, including the speaker.

Unknown commands respond with a short error. Disconnection silently removes the player and does not broadcast.

## Out of scope for MVP

Anything not in the six behaviors above. Notably:

- Combat, weapons, damage
- Inventory, items, containers, equipment
- NPCs, mob AI, wandering
- Skills, spells, effects, cooldowns
- Persistence of any kind — state exists only for the server's lifetime
- Admin UI, building commands, mgenerate
- Authentication, accounts, character sheets
- Stats, levels, pools, currency
- Emotes, tells, channels, who lists, help system
- Colors, terminal width handling, pagination

Adding any of these is a **Phase 3 vertical slice**, not MVP work.

## Architectural exercise

What MVP forces us to get right:

| Layer | Must be present for MVP |
|---|---|
| Components | `LocationComponent` (entity → room entity), `RoomComponent` (name, description, exits), `PlayerComponent` (display name, session ref) |
| Core systems | `BroadcastSystem` (send message to one player or a room's occupants) |
| Domain systems | `MovementSystem` (validate exit, move entity, emit events) |
| Handlers | `PlayerMovedHandler` (notifies rooms on move), `PlayerSaidHandler` (broadcasts to room) |
| Events | `PlayerMovedEvent`, `PlayerSaidEvent`, `PlayerConnectedEvent`, `PlayerDisconnectedEvent` |
| Infrastructure | `IEventBus`, handler registry, command dispatcher, telnet session I/O, DI host |

Everything here is in `docs/architecture/` already. MVP is the smallest integration of those pieces.

## Acceptance

Two terminals connect, both pick names, both type `look`, one types `east`, the other sees the leave message and can `east` after them to meet again, either types `say hi` and the other sees it. That's the gate.

Once that works, we delete no code; we **add** vertical slices from Phase 3.
