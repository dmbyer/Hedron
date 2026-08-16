# Output

> How the game talks to the player — typed messages, ANSI color, the status prompt, and per-session output batching so many sources flush as one coherent response. **Status:** live (slices 4, 12-a WP-A/B/C).

## What it is

Every line the player sees — a room description, a combat round, a help entry, a status prompt — is a typed `IOutputMessage` value. Commands and systems produce shapes; a formatter pipeline converts them to transport-correct strings; the TCP stream delivers bytes. No command or handler ever constructs a raw terminal string.

The output feature has three cooperating pieces at the orchestration level:

- **The formatter pipeline** (`Core/Output/`) converts typed messages to transport-encoded strings. For telnet it applies a four-role ANSI palette and inline `<role>text</role>` color markers. A future SignalR formatter is a registration, not a rewrite.
- **The session buffer** (`Core/Output/`) coalesces messages from all sources — the player's own command, room broadcasts, combat rounds, effect ticks — into one session-scoped queue, then flushes as a batch at defined boundaries (command-end, chat-immediate, or tick-end). One prompt trails each flush.
- **`BroadcastSystem`** (`Core/Systems/`) fans typed messages to rooms or all sessions, rendering per recipient through their own formatter so a future mixed-transport world works without callers changing.

## How it works

A command writes `context.Output.WriteAsync(IOutputMessage)`. `SessionBufferedOutputWriter` enqueues the message and checks `CategoryFlushPolicy`: `Chat` category triggers an immediate flush; all other categories batch. At a flush boundary (`CommandDispatcher` `finally` block, `OutputFlushTickHandler` at heartbeat priority 85, or a Chat-immediate), `ISessionOutputBuffer.FlushAsync` atomically drains the queue, formats each message through `IOutputFormatterRegistry.Resolve(session)`, writes each to `session.SendLineAsync`, then appends one `PromptMessage` composed by `IPromptSource`.

The full rendering model — pipeline shape, buffer threading guarantees, flush boundaries, broadcaster fan-out — is the [output-framework design doc](output-framework.md). The prompt source design is the [prompt system doc](prompt.md).

## Systems

| System | Role |
|---|---|
| [`output-framework.md`](output-framework.md) | Formatter pipeline, session buffer, flush policy, broadcast model, message shape catalog, ANSI palette |
| [`prompt.md`](prompt.md) | Prompt composition — `IPromptSource`, `PromptComposerSystem`, state labels, pool display |

## Surfaces

- **Message shapes** — `PlainMessage`, `RoomDescriptionMessage`, `MovementMessage`, `HelpIndexMessage`, `HelpEntryMessage`, `PromptMessage`. Shapes live in `Core/Output/`; each is an `IOutputMessage`. See the catalog in [`output-framework.md#message-shape-catalog`](output-framework.md#message-shape-catalog).
- **`IBroadcastSystem`** — `SendToRoomAsync` (with optional audience filter), `SendToEntityAsync` (direct to one player — the intended single-recipient write), `SendToAllAsync`, `SendRoomDescriptionAsync`. See [`../../reference/systems.md`](../../reference/systems.md) for the `BroadcastSystem` catalog row.
- **Configuration** — `Output:DefaultColor` (default `true`): initial `SupportsColor` for new telnet sessions. Set `false` to disable ANSI globally.

## Flows

- [Output journey (typed message → formatter → session write)](../../architecture/flows/flow-06-output-rendering.md) — enqueue, flush policy, atomic drain, formatter render, prompt append; includes batching and broadcast fan-out.

## Related

- [`../../architecture/03-events.md`](../../architecture/03-events.md) — handler priorities; `OutputFlushTickHandler` runs at priority 85.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-2 (no core-tier domain read), INV-24 (contributor seam: `IPromptSource` as the core-owned domain port).
- [`../../roadmap/completed/slice-4-output-framework.md`](../../roadmap/completed/slice-4-output-framework.md) · [`../../roadmap/completed/output-batching.md`](../../roadmap/completed/output-batching.md) — as-built history and design decisions.
- **Commands** (not yet migrated) — `CommandDispatcher.DispatchAsync` owns the command-end flush; see [`../../reference/systems.md`](../../reference/systems.md) for the dispatcher row.
