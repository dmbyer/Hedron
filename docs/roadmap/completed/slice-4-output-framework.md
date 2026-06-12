# Phase 3 slice 4 — Output framework (completed)

> Implemented on `master` (no PR number yet — uncommitted working-tree changes as of this ledger entry). The full feature spec lives in [`../../implementation-plans/output-framework.md`](../../implementation-plans/output-framework.md). This file records the as-built state and any deviations from the spec.

## Outcome

The stringify-and-forward `IOutputWriter` from slice 3 is replaced by a formatter-backed pipeline: `IOutputFormatter`/`IOutputFormatterRegistry` resolve the right renderer per session transport, and `TelnetOutputFormatter` applies a four-role ANSI palette (`system`/`error`/`room-name`/`direction`) plus `<role>text</role>` inline color markers. Two new message shapes — `RoomDescriptionMessage` and `MovementMessage` — complete the shape catalog the existing 12 commands need. `IBroadcastSystem` is rewritten to fan typed messages through `IOutputWriterFactory` per recipient (replacing raw-string forwarding), gains a system-wide `SendToAllAsync`, and generalizes the `excludeEntityId` pattern to an `Func<uint,bool>? audienceFilter` predicate. Players see color-formatted room descriptions, exit lists, and help output; operators can disable ANSI globally via `Output:DefaultColor`.

## Shipped pieces

| Surface | Location |
|---|---|
| `IOutputFormatter` (transport abstraction) | `Core/Output/IOutputFormatter.cs` |
| `IOutputFormatterRegistry` (resolves formatter by `session.TransportKey`) | `Core/Output/IOutputFormatterRegistry.cs` |
| `OutputFormatterRegistry` (dictionary lookup + fallback) | `Core/Output/OutputFormatterRegistry.cs` |
| `TelnetOutputFormatter` (ANSI palette, inline marker regex) | `Core/Output/TelnetOutputFormatter.cs` |
| `RoomDescriptionMessage` (room name, description, exit map, occupants) | `Core/Output/RoomDescriptionMessage.cs` |
| `MovementMessage` + `MovementDirectionKind` (blocked movement) | `Core/Output/MovementMessage.cs`, `MovementDirectionKind.cs` |
| `ISession.SupportsColor` + `ISession.TransportKey` (new capability flags) | `Core/Sessions/ISession.cs` |
| `TelnetSession.SupportsColor` (private setter; defaults from `Output:DefaultColor`) | `Server/Sessions/TelnetSession.cs` |
| `OutputWriter` — body replaced (formatter-backed; interface unchanged) | `Core/Output/OutputWriter.cs` |
| `OutputWriterFactory` — injects `IOutputFormatterRegistry` | `Core/Output/OutputWriterFactory.cs` |
| `IBroadcastSystem` — rewritten (typed `SendToRoomAsync` w/ predicate, `SendToAllAsync`, `SendRoomDescriptionAsync`) | `Core/Systems/IBroadcastSystem.cs` |
| `BroadcastSystem` — rewritten (composes `IOutputWriterFactory` per recipient) | `Core/Systems/BroadcastSystem.cs` |
| `OutputConfiguration` (bound from `Output:` config section) | `Server/OutputConfiguration.cs` |
| `Output:DefaultColor` config key (default `true`) | `Server/appsettings.json` |
| `MoveCommand` — failure writes `MovementMessage` instead of `PlainMessage` | `Core/Modules/Movement/Commands/MoveCommand.cs` |
| `PlayerMovedHandler` — uses typed `PlainMessage` + predicate filter | `Core/Modules/Movement/Handlers/PlayerMovedHandler.cs` |
| `PlayerSaidHandler` — uses typed `PlainMessage` | `Core/Modules/Chat/Handlers/PlayerSaidHandler.cs` |
| `PlayerSessionHandler` — uses typed `PlainMessage` + predicate filter | `Core/Modules/Session/Handlers/PlayerSessionHandler.cs` |
| `LookCommand` — stale comment removed; delegate to `SendRoomDescriptionAsync` unchanged | `Core/Modules/World/Commands/LookCommand.cs` |
| `Program.cs` — registers `IOutputFormatter`, `IOutputFormatterRegistry`; `Configure<OutputConfiguration>` | `Server/Program.cs` |
| `TelnetServer` — injects `IOptions<OutputConfiguration>`, passes `defaultColor` to `TelnetSession` | `Server/Sessions/TelnetServer.cs` |
| `docs/architecture/07-output.md` (new) | — |
| `docs/architecture/06-flows.md` — Flow 3 output leg updated; Flow 6 (output rendering) added | — |
| `docs/architecture/00-overview.md` — links `07-output.md` | — |
| `docs/reference/systems.md` — `BroadcastSystem` entry rewritten; Output Infrastructure section added | — |

## Spec-review provenance

**Spec-mode gate (before implementation):** APPROVE WITH NITS. No blocking findings. Non-blocking nits: `BroadcastSystem`'s "core system that does I/O" classification (noted in `systems.md`); missing `IOutputFormatter`/`IOutputFormatterRegistry` catalog entry in `systems.md` (added); `SupportsColor` setter placement (confirmed on concrete class only). The single open question (inline color syntax) was resolved before implementation: `<role>text</role>` angle-bracket tags.

**Code-mode gate (after implementation):** APPROVE WITH NITS. No blocking findings. Two doc nits fixed inline: `HelpEntryMessage` code block in the use-case doc was missing the `Aliases` parameter; `look` command prose updated from "builds a `RoomDescriptionMessage`" to "delegates to `SendRoomDescriptionAsync`."

## Notable design points

- **`<role>text</role>` inline color syntax.** YAML-safe (angle brackets have no special meaning in YAML plain scalars), HTML-familiar, strippable via a single regex pass. Four named semantic roles only; hex colors and nesting are out of scope. The syntax decision was explicitly deferred from the use-case doc and resolved at planning time before any code was written.
- **`look` delegates to `SendRoomDescriptionAsync`.** `LookCommand` body is unchanged — it still calls `_broadcast.SendRoomDescriptionAsync(...)`. The room description building moved from a raw `StringBuilder` into `BroadcastSystem.SendRoomDescriptionAsync`, which now constructs a typed `RoomDescriptionMessage` and routes it through the formatter. This keeps the command thin and centralizes room-description assembly.
- **`BroadcastSystem` composes `IOutputWriterFactory` per recipient.** Every broadcast path (room, system-wide, single-player) goes through `_writerFactory.Create(session).WriteAsync(message)`. No recipient ever receives a raw string. The old `SendToPlayerAsync(uint, string)` method is removed from the interface and becomes an implementation detail folded into the per-recipient writer call.
- **`OutputFormatterRegistry` fallback.** If a session's `TransportKey` has no registered formatter, the registry falls back to the first registered entry. This is safe while only `TelnetOutputFormatter` exists and makes adding a second formatter (SignalR) a pure addition.
- **`SupportsColor` setter is private.** `ISession` exposes `{ get; }` only. The setter lives on `TelnetSession` as `private set`, providing the future `/color off` seam without mutating the interface. The runtime command is deferred.
- **Channel mode is acknowledged debt.** `SendToAllAsync` lands here; per-channel/newbie-channel broadcast needs membership state that no slice has introduced. Explicitly tracked in `backlog.md`.

## Deviations from the use-case doc

The use-case doc's prose implied `LookCommand` would build the `RoomDescriptionMessage` in its own body. As built, the message is constructed inside `BroadcastSystem.SendRoomDescriptionAsync`; the command body is unchanged. The observable behavior is identical and the invariants are satisfied. The doc was corrected in-slice to say "delegates to `SendRoomDescriptionAsync`."

`HelpEntryMessage` code-block in the use-case doc omitted the `Aliases` parameter that was already in the as-built record. Fixed in the same PR.

## Follow-ups unlocked by this slice

- **Slice 5 — Account / character creation.** Login prompts and character-info output are authored against `RoomDescriptionMessage` / `PlainMessage` / `PlayerInformationMessage` (placeholder) shapes and the formatter pipeline, not raw `SendLineAsync` calls.
- **Future gameplay slices.** Every slice's output leg — item descriptions, combat messages, stats — plugs into `IOutputMessage` + `TelnetOutputFormatter` without touching transport code.
- **SignalR / web transport (deferred).** `IOutputFormatter` + `IOutputFormatterRegistry` are the seam: register a `SignalROutputFormatter` with `TransportKey = "signalr"` and no caller changes.
- **Per-session `/color off` command (deferred).** `TelnetSession.SupportsColor` has a `private set`; the command needs only a one-liner setter call once the command framework dispatches to it.
