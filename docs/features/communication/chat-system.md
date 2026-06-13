# Chat System

> The `say` verb: validates input, publishes `PlayerSaidEvent`, and fans the message out to every player in the room via `IBroadcastSystem`. **Authoring checkpoint:** shipped with the Chat module (no dedicated slice plan). Living document.

## What it is / does

The Chat module owns the `say` surface. There is no `IChatSystem` or domain system — the logic is thin enough that the command (Initiator) and a single handler cover the full path without a decision layer in between. Chat is domain-tier in the sense that it lives under `Core/Modules/Chat/`, but it performs no domain computation: it is pure fan-out via a core-tier port.

## How it works

### The pipeline

1. **`SayCommand`** (Initiator) — declares `say <message>` with a `RestOfLine` argument, requires no privileges, and does nothing but publish `PlayerSaidEvent(playerEntityId, message)`. No output, no system calls (INV-5, INV-8).
2. **`PlayerSaidHandler`** — subscribes to `PlayerSaidEvent` at `HandlerPriority.Domain`. It reads `LocationComponent.RoomEntityId` from the speaker to determine the room, resolves the speaker's display name from `PlayerComponent` (falls back to `"Someone"` when absent), and calls `IBroadcastSystem.SendToRoomAsync(roomEntityId, new PlainMessage(..., OutputSeverity.Chat, OutputCategory.Chat))`. No audience filter — all players in the room receive the line.

### The broadcast seam

`IBroadcastSystem.SendToRoomAsync` iterates all sessions whose bound `PlayerEntityId` carries `LocationComponent.RoomEntityId == roomEntityId`, formats the message via each session's `IOutputFormatter`, and writes it. The speaker is not excluded — they see their own `say` line. See the [`BroadcastSystem` reference row](../../reference/systems.md) for the full fan-out design.

### Channel mode (deferred)

Room-scoped speech is all that is wired today. Global chat, area chat, tell, and newbie channels require channel-membership state on sessions or entities. That work is tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md) and will extend this module when it lands, adding channel commands and broadening `IBroadcastSystem` or introducing a `IChatSystem` if state management warrants a domain layer.

## Interface

The module ships no `IChat` seam — the command→event→handler path is the interface:

- [`SayCommand.cs`](../../../Core/Modules/Chat/Commands/SayCommand.cs) — Initiator; publishes `PlayerSaidEvent`.
- [`PlayerSaidEvent.cs`](../../../Core/Modules/Chat/Events/PlayerSaidEvent.cs) — thin past-tense record: `PlayerEntityId`, `Message`, `OccurredAt`, `EventId`.
- [`PlayerSaidHandler.cs`](../../../Core/Modules/Chat/Handlers/PlayerSaidHandler.cs) — fan-out via `IBroadcastSystem.SendToRoomAsync`.

## Considerations

- **No persistence.** Chat messages are fire-and-forget; no component, no log.
- **No filtering or moderation.** Every player in the room sees every `say` line; content moderation is out of scope for Phase 3.
- **`LocationComponent` required.** If the speaker has no `LocationComponent` (edge case during login flow), the handler returns early without output.
- **`PlainMessage` with `OutputCategory.Chat`.** The formatter pipeline applies the `chat` output category for potential future color-coding; today the telnet formatter maps it to the default palette.

## Extensibility

- **Channel mode** slots in by adding channel-routing logic to a new `IChatSystem` (or extending `IBroadcastSystem`) without changing `SayCommand` or `PlayerSaidEvent`. New channel commands become new Initiators in the same module.
- **Emotes / social commands** (`emote`, `shout`) follow the same command→event→handler shape; they can live in this module or split to a Social module when they grow.

## Related

- [`communication.md`](communication.md) — holistic feature view.
- [`../../reference/systems.md`](../../reference/systems.md) — `BroadcastSystem` row.
- [`../../reference/handlers.md`](../../reference/handlers.md) — `PlayerSaidHandler` is not yet a named catalog entry; Chat ships no dedicated handler row (the handler is documented here).
- [`../../features/output/output-framework.md`](../output/output-framework.md) — `IBroadcastSystem` design and `OutputCategory` table.
- [`../../roadmap/backlog.md`](../../roadmap/backlog.md) — channel-mode backlog entry.
