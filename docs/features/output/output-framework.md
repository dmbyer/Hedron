# Output Framework

> The formatter-backed rendering pipeline that converts typed `IOutputMessage` values into transport-correct strings, coalesces them in a per-session buffer, and flushes them at defined boundaries with a trailing status prompt. **Authoring checkpoint:** slice 4 (formatter pipeline); slice 12-a WP-A/B/C (session buffer, prompt, flush triggers). Living document.

## What it is / does

The output framework sits **between the command/handler tier and the session transport tier**. Commands and broadcast callers produce typed messages; the formatter converts them; the session transmits bytes. No command or handler ever constructs a raw terminal string.

```
ICommand / IBroadcastSystem
    ↓  IOutputMessage
SessionBufferedOutputWriter (IOutputWriter)
    ↓  enqueue; auto-flush if Chat category
ISessionOutputBuffer.FlushAsync()
    ↓  snapshot-and-drain under lock; I/O outside lock
IOutputFormatterRegistry.Resolve(session)
IOutputFormatter.Format(message, session)
    ↓  transport-encoded string (ANSI for telnet; future HTML/CSS-class for SignalR)
ISession.SendLineAsync(text)
    ↓  bytes
TCP stream
    [then, if IPromptSource returns non-null, prompt is sent last]
```

## How it works

### IOutputWriter / IOutputWriterFactory

`IOutputWriter` is the single-session output seam. `IOutputWriterFactory` is bound once per request by the `CommandDispatcher` and internally by `BroadcastSystem` per recipient.

`SessionBufferedOutputWriter` (the implementation) enqueues messages into a `SessionOutputBuffer` and auto-flushes synchronously only for `OutputCategory.Chat` messages. All other messages batch until `FlushAsync()` is called explicitly by flush triggers. `FlushAsync()` delegates to `ISessionOutputBuffer.FlushAsync()`. The `IOutputWriter` and `IOutputWriterFactory` interfaces are stable.

See [`Core/Output/IOutputWriter.cs`](../../../Core/Output/IOutputWriter.cs) · [`Core/Output/IOutputWriterFactory.cs`](../../../Core/Output/IOutputWriterFactory.cs).

### IOutputFormatter / IOutputFormatterRegistry

`IOutputFormatterRegistry` resolves the formatter whose `TransportKey` matches `session.TransportKey`. If no exact match is registered, it falls back to the first registered formatter (safe while only telnet exists).

See [`Core/Output/IOutputFormatter.cs`](../../../Core/Output/IOutputFormatter.cs) · [`Core/Output/IOutputFormatterRegistry.cs`](../../../Core/Output/IOutputFormatterRegistry.cs).

### ISession — capability flags

`SupportsColor` defaults to `Output:DefaultColor` (config, default `true`). A private setter on `TelnetSession` provides the seam for a future per-session `/color off` command; the command is not built yet.

See [`Core/Sessions/ISession.cs`](../../../Core/Sessions/ISession.cs).

## Per-session buffering

### ISessionOutputBuffer / SessionOutputBuffer

`SessionOutputBuffer` is bound to one `ISession`. `FlushAsync` atomically snapshots and clears the queue under a lock, then sends each message and (optionally) a prompt outside the lock. Lock granularity is minimal — no I/O under lock.

See [`Core/Output/ISessionOutputBuffer.cs`](../../../Core/Output/ISessionOutputBuffer.cs).

### ISessionBufferRegistry / SessionBufferRegistry

Singleton `ConcurrentDictionary<Guid, SessionOutputBuffer>`. `GetOrCreate` uses `GetOrAdd`. `FlushAllPendingAsync` iterates entries where `HasPending` is true. `Release` removes the buffer when a session disconnects.

See [`Core/Output/ISessionBufferRegistry.cs`](../../../Core/Output/ISessionBufferRegistry.cs).

### CategoryFlushPolicy

Maps `OutputCategory.Chat` and `OutputCategory.Notification` → `FlushPolicy.Immediate`; all other categories → `FlushPolicy.Batched`. `SessionBufferedOutputWriter` uses this to decide whether to call `FlushAsync()` immediately after enqueue. `Notification` is used for login-flow prompts and bystander movement messages — any push message that should reach the recipient without waiting for a tick boundary.

See [`Core/Output/CategoryFlushPolicy.cs`](../../../Core/Output/CategoryFlushPolicy.cs).

### IPromptSource

Called by `SessionOutputBuffer.FlushAsync()` after all queued messages have been sent. Returns `null` for unbound sessions (`playerEntityId == 0`) to suppress the prompt. The concrete implementation is `PromptComposerSystem` (`Core/Modules/Prompt/Systems/`), which reads `IEntityStateService` and `IStatSystem` — domain-aware, wired via DI. The full prompt design is the [prompt system doc](prompt.md).

See [`Core/Output/IPromptSource.cs`](../../../Core/Output/IPromptSource.cs).

## Message shape catalog

| Shape | Category | Used by |
|---|---|---|
| `PlainMessage(Text, Severity, Category)` | configurable | Commands (error/info), broadcast arrival/departure |
| `HelpIndexMessage(Entries)` | Help | `help`, `commands` |
| `HelpEntryMessage(Verb, LongDesc, Usage, Aliases)` | Help | `help <verb>` |
| `RoomDescriptionMessage(RoomEntityId, Name, Description, Exits, OccupantNames)` | Info | `look`, movement arrival, on-connect |
| `MovementMessage(Kind, Direction, ActorName)` | Info | `MoveCommand` failure path |
| `PromptMessage(StateLabel, Pools)` | System | Appended by `SessionOutputBuffer.FlushAsync()` when `IPromptSource` returns non-null |

**Planned — not yet built (ship with their gameplay slices):**
- `PlayerInformationMessage` — character stats display

## TelnetOutputFormatter

`Core/Output/TelnetOutputFormatter.cs`. `TransportKey = "telnet"`.

### ANSI palette (four semantic roles)

| Tag | ANSI code | Appearance | Used for |
|---|---|---|---|
| `<system>` | `\x1B[96m` (bright cyan) | Cyan | System notices, status output |
| `<error>` | `\x1B[91m` (bright red) | Red | Error / blocked-action feedback |
| `<room-name>` | `\x1B[93m` (bright yellow) | Yellow | Room name header, help verb names |
| `<direction>` | `\x1B[32m` (green) | Green | Exit directions |

Reset: `\x1B[0m`. No other palette roles exist today; theme management is out of scope.

### Inline color marker syntax

Designer-friendly XML-like tags embedded in output strings:

```
<role>text</role>
```

Valid roles: `system`, `error`, `room-name`, `direction`. Tags are **YAML-safe** in unquoted scalars. Convention: quote YAML strings that contain color markers.

**With `SupportsColor = true`:** markers are replaced with the ANSI code for the role; the content is wrapped between the ANSI opener and `\x1B[0m`.

**With `SupportsColor = false`:** tags and their delimiters are stripped; only the inner text remains.

Nesting and hex colors are not supported. Escaping a literal `<` is not defined today.

### Per-shape rendering

- **`PlainMessage`** — severity `Error` wraps in `<error>...</error>`; severity `System` wraps in `<system>...</system>`; Chat/Confirmation renders plain.
- **`RoomDescriptionMessage`** — room name in `<room-name>`, each exit key in `<direction>`. Description and occupants are plain.
- **`MovementMessage(Blocked)`** — "You cannot go that way." in `<system>`.
- **`HelpIndexMessage`** — section headers in `<system>`, verb names in `<room-name>` (padded before colorizing to preserve column alignment).
- **`HelpEntryMessage`** — verb/alias header in `<room-name>`.

## Broadcast model

`IBroadcastSystem` delivers typed messages to multiple recipients, each rendered through their own formatter. See [`Core/Systems/BroadcastSystem.cs`](../../../Core/Systems/BroadcastSystem.cs).

```
// Room broadcast with optional audience filter
Task SendToRoomAsync(uint roomEntityId, IOutputMessage message, Func<uint, bool>? audienceFilter = null);

// System-wide broadcast (shutdown notices, global admin announcements)
Task SendToAllAsync(IOutputMessage message);

// Convenience: builds RoomDescriptionMessage and delivers to one player
Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId);
```

`BroadcastSystem` composes `IOutputWriterFactory` per recipient. The `excludeEntityId` pattern degenerates to `audienceFilter: entityId => entityId != excluded`.

**Channel mode (global chat, newbie channel):** not built. Needs channel-membership state. Tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).

## Configuration

| Key | Default | Effect |
|---|---|---|
| `Output:DefaultColor` | `true` | Initial `SupportsColor` for new telnet sessions. Operators set `false` to disable color globally. |

## Flush boundaries

There are three:

- **Command-end** (`CommandDispatcher.DispatchAsync` `finally` block) — fires after every command path, including failures and exceptions.
- **Chat immediate** — any `Chat`-category message triggers an inline `buffer.FlushAsync()` before returning from `WriteAsync`.
- **Tick-end** (`OutputFlushTickHandler`, priority 85 on `HeartbeatTickEvent`) — flushes every session with pending output at the end of each heartbeat tick.

## Thread safety

The atomic drain (snapshot-and-clear under lock, I/O outside lock) guarantees that concurrent enqueues from the player's read loop, other players' read loops (chat broadcasts), and the heartbeat thread never corrupt the buffer. Two concurrent flushes produce at most one extra prompt — the second drain finds an empty list and appends only the prompt. Accepted consequence; see [`../../roadmap/completed/output-batching.md`](../../roadmap/completed/output-batching.md) for the rationale.

## Seams for future transports

`IOutputFormatter.TransportKey` + `IOutputFormatterRegistry` are the extension points. A future SignalR/web formatter:

1. Implements `IOutputFormatter` with `TransportKey = "signalr"`.
2. Maps the same named roles to HTML/CSS classes instead of ANSI codes.
3. Is registered in DI alongside `TelnetOutputFormatter`.
4. `TelnetSession` returns `TransportKey = "telnet"`; a future `SignalRSession` returns `"signalr"`. No caller changes.

## Related

- [`output.md`](output.md) — holistic feature view and player-facing surfaces.
- [`prompt.md`](prompt.md) — `IPromptSource` port and `PromptComposerSystem` design.
- [`../../architecture/flows/flow-06-output-rendering.md`](../../architecture/flows/flow-06-output-rendering.md) — output journey: typed message → formatter → session write.
- [`../../reference/systems.md`](../../reference/systems.md) — `IBroadcastSystem`, Output Infrastructure catalog rows.
- [`../../roadmap/completed/slice-4-output-framework.md`](../../roadmap/completed/slice-4-output-framework.md) · [`../../roadmap/completed/output-batching.md`](../../roadmap/completed/output-batching.md) — as-built records.
