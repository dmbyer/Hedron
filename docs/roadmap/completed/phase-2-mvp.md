# Phase 2 — Foundation / MVP (completed)

> Phase 2 built the target architecture from scratch and shipped the MVP walking-simulator. Both the Phase 2 step plan and the original `mvp.md` specification are absorbed here for archaeology — current planning lives in [`../plan.md`](../plan.md).

## MVP scope (frozen at design time)

The smallest thing that proves the target architecture works end-to-end: a multi-user walking simulator with a chat line. No combat, NPCs, inventory, items, skills, persistence, admin UI, or authentication beyond "pick a name."

### Behaviors

1. **Telnet listener** accepts concurrent client connections on a configurable port.
2. **Login** — connecting client is prompted for a name; that name becomes their display name. No password, no account record, no persistence. Duplicate names get a numeric suffix or a reprompt.
3. **Hand-authored world** — three connected rooms, declared in code at startup (no JSON, no editor). Each room has a name, a description, and exits to other rooms.
4. **`look`** — prints the current room's name, description, exit list, and the names of other players present.
5. **`north` / `south` / `east` / `west` / `up` / `down`** (and short aliases `n/s/e/w/u/d`) — moves the player between rooms if an exit exists. Departed-room players see a leave message; arrival-room players see an arrive message; mover sees the new room.
6. **`say <message>`** — broadcasts "<name> says: <message>" to every player in the same room, including the speaker.

Unknown commands respond with a short error. Disconnection silently removes the player and does not broadcast.

### Architectural pieces forced into existence

| Layer | Required for MVP |
|---|---|
| Components | `LocationComponent`, `RoomComponent`, `PlayerComponent` |
| Core systems | `BroadcastSystem` |
| Domain systems | `MovementSystem` |
| Handlers | `PlayerMovedHandler`, `PlayerSaidHandler` |
| Events | `PlayerMovedEvent`, `PlayerSaidEvent`, `PlayerConnectedEvent`, `PlayerDisconnectedEvent` |
| Infrastructure | `IEventBus`, handler registry, command dispatcher, telnet session I/O, DI host |

### Acceptance criterion (passed)

Two terminals connect, both pick names, both type `look`, one types `east`, the other sees the leave message and can `east` after them to meet again, either types `say hi` and the other sees it.

## Phase 2 implementation steps

Each step landed as a commit-sized chunk. Order mattered only where noted.

### Step 1 — ECS primitives audit

Verified `EntityService`, `ComponentRepository`, `IComponent`, `EcsManager` against [`../../architecture/02-ecs.md`](../../architecture/02-ecs.md). Added `Entity` record-struct wrapper, added `EntityService.CreateEntity()` with a monotonic id allocator (0 reserved), renamed `GetComponent<T>` → `Get<T>` and added `TryGet<T>`, cleared all four nullability warnings, trimmed the spurious `ref` in the computed-stats example in `02-ecs.md`.

### Step 2 — Event bus

Added `Core/Events/` with `IEvent`, `IEventHandler<T>`, `IEventBus`, in-memory `EventBus` (priority-ordered dispatch, snapshot-under-lock so handlers can sub/unsub during dispatch), and `HandlerPriority` constants (State / Domain / Notification / Persistence / Ai). Registered as a DI singleton in `Server/Program.cs`. Also fixed a doc drift: was `IGameEvent`, should be `IEvent` per [`../../architecture/03-events.md`](../../architecture/03-events.md).

### Step 3 — Handler / system contracts

No code landed. Step absorbed into step 2 (handler contract via `IEventHandler<T>`) and the later system-building steps (each system defines its own per-feature interface).

### Step 4 — Command dispatcher

Added `Core/Sessions/ISession.cs` (concrete telnet impl in step 5), plus `Core/Commands/` with `ICommand`, `ICommandDispatcher`, and `CommandDispatcher` (case-insensitive verb map, duplicate-verb guard, "Unknown command" fallback). Registered as a DI singleton.

### Step 5 — Telnet session layer

Added `Core/Sessions/ISessionManager.cs`; `Core/Modules/Session/Events/` with `PlayerConnectedEvent` and `PlayerDisconnectedEvent`; `Server/Sessions/` with `SessionManager` (concurrent dict, `Register`/`Unregister`/`GetSession`/`GetAll`), `TelnetSession` (implements `ISession` — login prompt, entity allocation, main I/O loop dispatching to `CommandDispatcher`, `PlayerConnected`/`PlayerDisconnected` events on connect/drop), and `TelnetServer` (`BackgroundService`, TCP listener on port 4000, one fire-and-forget task per client). `ISessionManager` and `TelnetServer` wired through DI.

### Step 6 — MVP components

Added `Core/Direction.cs` enum; `Core/ECS/Components/` with `PlayerComponent` (display name, session ref), `LocationComponent` (room entity id), `RoomComponent` (name, description, exits dictionary).

### Step 7 — MVP systems and handlers

Added `Core/WorldConfiguration.cs`; `Core/Systems/` with `IBroadcastSystem`/`BroadcastSystem` (send to player, send to room, send room description); `Core/Modules/Movement/Systems/` with `IMovementSystem`/`MovementSystem` + `MoveResult`; `Core/Modules/Movement/Handlers/PlayerMovedHandler` (leave/arrive notifications + room description on arrival); `Core/Modules/Chat/Handlers/PlayerSaidHandler` (broadcast to room); `Core/Modules/Session/Handlers/PlayerSessionHandler` (adds components on connect, announces + destroys on disconnect). All wired through DI; handlers subscribed to event bus before `host.RunAsync`.

### Step 8 — MVP commands

`Core/Modules/World/Commands/LookCommand` (`look`/`l`); `Core/Modules/Movement/Commands/MoveCommand` (six instances, one per direction, with `n/s/e/w/u/d` aliases); `Core/Modules/Chat/Commands/SayCommand` (`say`). All registered as `ICommand` in DI.

### Step 9 — World bootstrap

`Server/WorldBootstrap.cs` creates three rooms (West End → Crossroads → East End), wires their exits, and sets `WorldConfiguration.StartingRoomEntityId` to the Crossroads. Runs before `host.RunAsync`.

### Step 10 — Smoke

Build green, zero warnings. Live test passed: `dotnet run --project Server`, then `telnet localhost 4000` in two terminals satisfied every acceptance criterion above.

## Lessons feeding into Phase 3 planning

- Hand-authored world bootstrapping (step 9) was effective for MVP but does not scale. Content authoring needs first-class tooling early in Phase 3 — drove the resolution of Ticket B and the reordering of admin/content tooling in [`../plan.md`](../plan.md).
- The "no `[Persistent]` on MVP components" decision paid off: the persistence substrate slice (Phase 3 slice 1) shipped without retrofit cost.
