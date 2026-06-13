# Use Case: Output Framework

**Status:** implemented
**Actors:** Player, Administrator, System
**Module:** `Core/Output/`

---

## Description

Pure-infrastructure slice that completes the output half of the command/output split. [`command-framework.md`](command-framework.md) (slice 3) shipped a minimal output seam and an explicit owed list of stubs; this slice discharges that debt.

This slice introduces:

1. The remaining `IOutputMessage` shapes the 12 commands and broadcast need: `RoomDescriptionMessage`, `MovementMessage` (plus documented placeholders `CombatMessage` / `PlayerInformationMessage` that land with their gameplay slices).
2. An `IOutputFormatter` abstraction with one implementation, `TelnetOutputFormatter` (basic ANSI palette), shaped so a future SignalR/web formatter drops in without touching callers.
3. Capability flags on `ISession` (`SupportsColor`, default `true` for telnet, operator-overridable seam).
4. A formatter-backed `IOutputWriter` that **replaces** slice 3's stringify-and-forward implementation (same interface, new body).
5. Designer-friendly inline color markers — resolved to `<role>text</role>` angle-bracket tags (YAML-safe). Authoritative palette + syntax: [`../architecture/subsystems/output.md`](../architecture/subsystems/output.md).
6. Broadcast expansion: room broadcast gains an audience-filter predicate and a system-wide `SendToAllAsync`; channel mode is explicitly deferred (acknowledged debt).

No gameplay change. The difference players see is color and correctly formatted room/movement output instead of raw stringified messages.

---

## Preconditions

- Phase 3 slice 3 ([`command-framework.md`](command-framework.md)) merged: the new `ICommand` shape, `CommandContext`, parser, authorization, extended dispatcher, `CommandExecutedEvent`/`CommandLoggingHandler`, `help`/`commands`, and the minimal output seam exist and run.
- All 12 commands are on `ExecuteAsync(CommandContext)` and write via `context.Output`.
- `ISession`, `ISessionManager`, `IBroadcastSystem`, `IEventBus`, and the `Direction` enum exist.

---

## Postconditions

- The full `IOutputMessage` catalog the existing surface needs exists: `PlainMessage`, `HelpIndexMessage`, `HelpEntryMessage` (slice 3), plus `RoomDescriptionMessage` and `MovementMessage`. `CombatMessage` / `PlayerInformationMessage` are documented placeholders only.
- `IOutputFormatter` exists with `TelnetOutputFormatter` as the sole implementation (four-role ANSI palette: system / error / room-name / direction), stripping all color when the session signals no support.
- `ISession` exposes `bool SupportsColor` (default `true` for telnet, from `Output:DefaultColor`, with an operator-override seam; the runtime `/color off` command is not built).
- The slice-3 stringify-and-forward `IOutputWriter` is replaced by a formatter-backed `OutputWriter` (resolve formatter → `Format(message, session)` → `session.SendLineAsync(rendered)`). The `IOutputWriter` / `IOutputWriterFactory` interfaces are unchanged — only the body.
- `help` / `commands` now render with color; `look` writes a real `RoomDescriptionMessage`; `MoveCommand` failure writes a `MovementMessage`.
- `IBroadcastSystem` gains an audience-filter predicate on its room-scope method and a system-wide `SendToAllAsync`; its implementation composes `IOutputWriterFactory` + typed messages internally. Channel mode is **not** built (acknowledged debt).
- Inline color markers are parseable by the formatter, transport-aware (ANSI for telnet; HTML/CSS-class seam reserved for web), strippable when unsupported, and YAML-safe.
- No gameplay or persistence behaviour changes; slice-2/3 smoke still passes.

---

## Main Flow

1. **Command produces typed output.** A command body (unchanged from slice 3) calls `context.Output.WriteAsync(IOutputMessage)`.
2. **Writer resolves the formatter.** `OutputWriter` asks `IOutputFormatterRegistry` for the formatter whose `TransportKey` matches the session's transport. Only `TelnetOutputFormatter` (`"telnet"`) is registered; the registry supports a future `"signalr"` entry without caller changes.
3. **Formatter renders.** `IOutputFormatter.Format(message, session)` pattern-matches the shape and produces a transport-encoded string. Telnet applies the four-role ANSI palette and parses inline `<role>` markers into ANSI. If `SupportsColor == false`, all markers/codes are stripped.
4. **Transport emits bytes.** The writer awaits `session.SendLineAsync(rendered)`. No command or domain code is aware of the encoding.
5. **Broadcast — room with audience filter.** `IBroadcastSystem.SendToRoomAsync(roomId, message, audienceFilter)` enumerates occupants, applies the optional `Func<uint,bool>?` predicate (the slice-2 `excludeEntityId` becomes a degenerate filter), and renders per recipient through their own writer.
6. **Broadcast — system-wide.** `SendToAllAsync(message)` routes a typed message through every session's writer (shutdown notices, global announcements).
7. **Broadcast — channel mode (deferred).** Not built — needs channel-membership state no slice has introduced. Tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md).
8. **No gameplay or persistence behaviour changes** — only the rendering of output differs from slice 3.

---

## Events Fired

This slice fires **no new events**. Output rendering is a synchronous transform between a command/system and a session, not an event-bus concern. Existing events (`CommandExecutedEvent`, `PlayerSaidEvent`, `PlayerMovedEvent`, the four slice-2 admin events) fire unchanged.

---

## Design Notes

- **Interface stability across the split.** Slice 3 shaped `IOutputMessage`, `IOutputWriter`, `IOutputWriterFactory` precisely so this slice changes only implementations and adds shapes — no caller churn. That is the point of the command/output split being two reviewable slices.
- **`IBroadcastSystem` composes onto the output pipeline.** Broadcast is the multi-recipient seam; `IOutputWriter` is the single-session seam. Broadcast now uses `IOutputWriterFactory` per recipient so all output — single or fanned — goes through one rendering pipeline. The slice-2 `excludeEntityId` degenerates to a filter predicate.
- **Channel mode is honestly deferred** — it needs membership state that does not exist yet; the two state-free broadcast modes land here so the slice is complete for what the current world can exercise.
- **Color is a transport-aware abstraction.** `<role>` markers are parsed by the formatter (ANSI for telnet; HTML/CSS-class seam for future web), strippable when unsupported, YAML-safe. Four semantic roles only (system / error / room-name / direction) — theme management is out of scope. Full palette: [`../architecture/subsystems/output.md`](../architecture/subsystems/output.md).
- **SignalR is a seam, not an implementation.** `IOutputFormatter.TransportKey` + `IOutputFormatterRegistry` mean a web formatter is a registration away; building it is gated on the deferred dual-client decision.
- **Out of scope.** Web/SignalR formatter, output streaming/paging, prompt customization, i18n templates, color themes, the runtime `/color off` command (seam exists; verb does not), channel/global chat.

---

## Related

- [`command-framework.md`](command-framework.md) — slice 3; shipped the minimal output seam and owed-stub list this slice discharges.
- [`world-content-loading-and-admin-substrate.md`](../features/world/world.md) — slice 2; established `IBroadcastSystem` with the single-mode `excludeEntityId` shape this slice generalizes.
- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; precedent for a pure-infrastructure slice.
- [`../architecture/subsystems/output.md`](../architecture/subsystems/output.md) — the output framework design reference (palette, inline-marker syntax, broadcast model).

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
