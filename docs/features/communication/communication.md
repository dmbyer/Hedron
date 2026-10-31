# Communication

> Player-to-player speech in the current room (`say`). **Status:** live (Chat module). In-game help (`help`/`commands` and authored help topics) moved to its own feature: [`../help/help.md`](../help/help.md).

## What it is

A player types `say <message>` to speak to everyone in the same room. Every connected player in that room — including the speaker — immediately receives the line. There is no channel filter, no moderation buffer, and no opt-out; all room occupants see all speech.

**Broadcast channel mode** (global chat, newbie channel, tell) is acknowledged backlog debt — it requires channel-membership state that does not yet exist. See [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

Help used to live here; it graduated to its own [`help/`](../help/help.md) feature folder when authored help topics landed (Phase 5 slice 4).

## How it works

- **`SayCommand`** (the Initiator) validates that a message argument is present, then publishes `PlayerSaidEvent(playerEntityId, message)`. It performs no output itself.
- **`PlayerSaidHandler`** subscribes to `PlayerSaidEvent`, reads the speaker's `LocationComponent` to find the current room, resolves the display name from `PlayerComponent`, and delegates fan-out to `IBroadcastSystem.SendToRoomAsync` with a `PlainMessage` in the `OutputSeverity.Chat` / `OutputCategory.Chat` slots. No system is involved — the handler calls the core broadcast port directly, which is appropriate because chat is pure output fan-out with no domain decision to make.

## Systems

There are no domain systems in the Chat module. Chat routes through the shared `BroadcastSystem` (core tier).

| System | Role |
|---|---|
| [`chat-system.md`](chat-system.md) | The `say` verb → `IBroadcastSystem` room broadcast path |

## Surfaces

- **Commands** — `say`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `PlayerSaidEvent` (thin past-tense payload: `PlayerEntityId`, `Message`). See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Components** — none; Chat carries no per-entity state.

## Flows

None. `say` is a two-step initiator→handler pipeline that does not warrant a dedicated journey.

## Related

- [`../../reference/systems.md`](../../reference/systems.md) — `BroadcastSystem` row (the core fan-out seam Chat uses).
- [`../../features/output/output-framework.md`](../output/output-framework.md) — the full broadcast model and `OutputCategory.Chat` slot design.
- [`../help/help.md`](../help/help.md) — in-game help (`help`/`commands`, help topics).
- [`../../roadmap/backlog.md`](../../roadmap/backlog.md) — broadcast channel-mode debt entry.
