# Output Framework

> **Introduced:** Phase 3 slice 4.  
> **Updated:** Phase 3 slice 12-a (WP-A) — per-session output buffering and prompt source port added.  
> **Summary:** A formatter-backed rendering pipeline that converts typed `IOutputMessage` values into transport-correct, capability-aware strings before writing them to sessions. Messages are coalesced in a per-session `SessionOutputBuffer`; flush is triggered by command completion or heartbeat.

---

## Layer position

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

---

## Interfaces

### IOutputMessage

```csharp
public interface IOutputMessage
{
    OutputCategory Category { get; }
}
```

Every typed output shape implements this. Commands and systems produce shapes; the formatter consumes them. Callers never stringify.

### IOutputWriter / IOutputWriterFactory

```csharp
public interface IOutputWriter
{
    Task WriteAsync(IOutputMessage message);
    Task FlushAsync();
}

public interface IOutputWriterFactory
{
    IOutputWriter Create(ISession session);
}
```

`IOutputWriter` is the single-session output seam. `IOutputWriterFactory` is bound once per request by the `CommandDispatcher` and internally by `BroadcastSystem` per recipient.

`SessionBufferedOutputWriter` (the implementation) enqueues messages into a `SessionOutputBuffer` and auto-flushes synchronously only for `OutputCategory.Chat` messages. All other messages batch until `FlushAsync()` is called explicitly (by WP-C flush triggers). `FlushAsync()` delegates to `ISessionOutputBuffer.FlushAsync()`. The `IOutputWriter` and `IOutputWriterFactory` interfaces are stable.

### IOutputFormatter / IOutputFormatterRegistry

```csharp
public interface IOutputFormatter
{
    string TransportKey { get; }    // "telnet", "signalr", etc.
    string Format(IOutputMessage message, ISession session);
}

public interface IOutputFormatterRegistry
{
    IOutputFormatter Resolve(ISession session);    // by session.TransportKey
}
```

`IOutputFormatterRegistry` resolves the formatter whose `TransportKey` matches `session.TransportKey`. If no exact match is registered, it falls back to the first registered formatter (safe while only telnet exists).

### ISession — capability flags

```csharp
public interface ISession
{
    string TransportKey { get; }      // "telnet" for TelnetSession
    bool SupportsColor { get; }       // initial value from Output:DefaultColor config
    // ... existing members ...
}
```

`SupportsColor` defaults to `Output:DefaultColor` (config, default `true`). A private setter on `TelnetSession` provides the seam for a future per-session `/color off` command; the command is not built this slice.

---

## Per-session buffering

### ISessionOutputBuffer / SessionOutputBuffer

```csharp
public interface ISessionOutputBuffer
{
    bool HasPending { get; }
    void Enqueue(IOutputMessage message);
    Task FlushAsync();
}
```

`SessionOutputBuffer` is bound to one `ISession`. `FlushAsync` atomically snapshots and clears the queue under a lock, then sends each message and (optionally) a prompt outside the lock. Lock granularity is minimal — no I/O under lock.

### ISessionBufferRegistry / SessionBufferRegistry

```csharp
public interface ISessionBufferRegistry
{
    ISessionOutputBuffer GetOrCreate(ISession session);
    void Release(Guid sessionId);
    Task FlushAllPendingAsync();
}
```

Singleton `ConcurrentDictionary<Guid, SessionOutputBuffer>`. `GetOrCreate` uses `GetOrAdd`. `FlushAllPendingAsync` iterates entries where `HasPending` is true. `Release` removes the buffer when a session disconnects.

### CategoryFlushPolicy

```csharp
public static class CategoryFlushPolicy
{
    public static FlushPolicy GetPolicy(OutputCategory category);
}
```

Maps `OutputCategory.Chat` → `FlushPolicy.Immediate`; all other categories → `FlushPolicy.Batched`. `SessionBufferedOutputWriter` uses this to decide whether to call `FlushAsync()` immediately after enqueue.

### IPromptSource

```csharp
public interface IPromptSource
{
    PromptMessage? GetPrompt(uint playerEntityId);
}
```

Called by `SessionOutputBuffer.FlushAsync()` after all queued messages have been sent. Returns `null` for unbound sessions (`playerEntityId == 0`) to suppress the prompt. The concrete implementation is `PromptComposerSystem` (`Core/Modules/Prompt/Systems/`), which reads `IEntityStateService` and `IStatSystem` — domain-aware, wired via DI.

`PromptMessage` shape:

```csharp
public sealed record PromptMessage(string? StateLabel, IReadOnlyList<PoolDisplay> Pools) : IOutputMessage;
public sealed record PoolDisplay(string Name, int Current, int Max);
```

---

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

---

## TelnetOutputFormatter

`Core/Output/TelnetOutputFormatter.cs`. `TransportKey = "telnet"`.

### ANSI palette (four semantic roles)

| Tag | ANSI code | Appearance | Used for |
|---|---|---|---|
| `<system>` | `\x1B[96m` (bright cyan) | Cyan | System notices, status output |
| `<error>` | `\x1B[91m` (bright red) | Red | Error / blocked-action feedback |
| `<room-name>` | `\x1B[93m` (bright yellow) | Yellow | Room name header, help verb names |
| `<direction>` | `\x1B[32m` (green) | Green | Exit directions |

Reset: `\x1B[0m`. No other palette roles exist this slice; theme management is out of scope.

### Inline color marker syntax

Designer-friendly XML-like tags embedded in output strings:

```
<role>text</role>
```

Valid roles: `system`, `error`, `room-name`, `direction`. Tags are **YAML-safe** in unquoted scalars (angle brackets have no special meaning in YAML plain scalars). Convention: quote YAML strings that contain color markers.

**With `SupportsColor = true`:** markers are replaced with the ANSI code for the role; the content is wrapped between the ANSI opener and `\x1B[0m`.

**With `SupportsColor = false`:** tags and their delimiters are stripped; only the inner text remains.

Nesting and hex colors are not supported. Escaping a literal `<` is not defined this slice.

### Per-shape rendering

- **`PlainMessage`** — severity `Error` wraps in `<error>...</error>`; severity `System` wraps in `<system>...</system>`; Chat/Confirmation renders plain.
- **`RoomDescriptionMessage`** — room name in `<room-name>`, each exit key in `<direction>`. Description and occupants are plain.
- **`MovementMessage(Blocked)`** — "You cannot go that way." in `<system>`.
- **`HelpIndexMessage`** — section headers in `<system>`, verb names in `<room-name>` (padded before colorizing to preserve column alignment).
- **`HelpEntryMessage`** — verb/alias header in `<room-name>`.

---

## Broadcast model

`IBroadcastSystem` delivers typed messages to multiple recipients, each rendered through their own formatter:

```csharp
// Room broadcast with optional audience filter
Task SendToRoomAsync(uint roomEntityId, IOutputMessage message, Func<uint, bool>? audienceFilter = null);

// System-wide broadcast (shutdown notices, global admin announcements)
Task SendToAllAsync(IOutputMessage message);

// Convenience: builds RoomDescriptionMessage and delivers to one player
Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId);
```

`BroadcastSystem` composes `IOutputWriterFactory` per recipient. The old `excludeEntityId` pattern degenerates to `audienceFilter: entityId => entityId != excluded`.

**Channel mode (global chat, newbie channel):** not built this slice. Needs channel-membership state. Tracked in [`backlog.md`](../../roadmap/backlog.md).

---

## Configuration

| Key | Default | Effect |
|---|---|---|
| `Output:DefaultColor` | `true` | Initial `SupportsColor` for new telnet sessions. Operators set `false` to disable color globally. |

---

## Seams for future transports

`IOutputFormatter.TransportKey` + `IOutputFormatterRegistry` are the extension points. A future SignalR/web formatter:

1. Implements `IOutputFormatter` with `TransportKey = "signalr"`.
2. Maps the same named roles to HTML/CSS classes instead of ANSI codes.
3. Is registered in DI alongside `TelnetOutputFormatter`.
4. `TelnetSession` returns `TransportKey = "telnet"`; a future `SignalRSession` returns `"signalr"`. No caller changes.

---

## Cross-references

- [`flows/README.md`](../flows/README.md) — Flow 3 (output leg), Flow 6 (output rendering)
- [`systems.md`](../../reference/systems.md) — `IBroadcastSystem`, output infrastructure catalog
- [`output-framework.md`](../../use-cases/output-framework.md) — slice 4 spec
- [`Core/Output/`](../../../Core/Output/) — all message shapes, interfaces, formatter, registry
