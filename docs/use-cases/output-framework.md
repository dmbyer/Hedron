# Use Case: Output Framework

**Status:** planned
**Actors:** Player, Administrator, System
**Module:** `Core/Output/`; refactor touches `Core/Modules/Help/`, `Core/Modules/Admin/`, `Core/Modules/Movement/`, `Core/Modules/Chat/`, `Core/Modules/World/`, broadcast

---

## Description

Pure-infrastructure slice that completes the output half of the command/output split. [`command-framework.md`](command-framework.md) (slice 3) shipped a deliberately minimal output seam — `IOutputMessage`, `PlainMessage`, `HelpIndexMessage`, `HelpEntryMessage`, and a stringify-and-forward `IOutputWriter` — and left an explicit owed list of stubs. This slice discharges that debt.

This slice introduces:

1. The remaining `IOutputMessage` shapes the existing 12 commands and broadcast need: `RoomDescriptionMessage`, `MovementMessage`. Plus documented placeholder shapes `CombatMessage` and `PlayerInformationMessage` (those land with their gameplay slices, not here).
2. An `IOutputFormatter` abstraction with one implementation, `TelnetOutputFormatter` (basic ANSI palette), shaped so a future SignalR/web formatter drops in without touching callers.
3. Capability flags on `ISession` (`SupportsColor`, default `true` for telnet, operator-overridable seam).
4. A formatter-backed `IOutputWriter` that **replaces** slice 3's stringify-and-forward implementation (same interface, new body).
5. Designer-friendly inline color formatting embedded in output strings — specified here as an **abstract requirement only**; concrete syntax is a deliberate slice-4-planning-time open question.
6. Broadcast model expansion: room broadcast gains an audience-filter predicate, a system-wide `SendToAllAsync`, with channel mode explicitly deferred as acknowledged debt.

No gameplay change. The slice-3 command framework, the 12 commands, `help`/`commands`, and the slice-2 smoke all continue to pass; the difference players see is color and correctly formatted room/movement output instead of raw stringified messages.

---

## Preconditions

- Phase 3 slice 3 ([`command-framework.md`](command-framework.md)) has merged: `ICommand` (new shape), `CommandContext`, `ICommandArgumentParser`, `IAuthorizationChecker`/`AdminRequirement`, `CommandDispatcher` (extended), `CommandExecutedEvent`/`CommandLoggingHandler`, `help`/`commands`/`HelpModule`, and the minimal output seam (`IOutputMessage`, `PlainMessage`, `HelpIndexMessage`, `HelpEntryMessage`, stringify-and-forward `IOutputWriter`/`IOutputWriterFactory`) exist and run.
- All 12 commands are on `ExecuteAsync(CommandContext)` and write via `context.Output`.
- `ISession`, `ISessionManager`, `IBroadcastSystem`, `IEventBus` continue to exist and are in the keep list.
- `Direction` enum continues to exist.

---

## Postconditions

- The full `IOutputMessage` shape catalog the existing surface needs exists: `PlainMessage`, `HelpIndexMessage`, `HelpEntryMessage` (from slice 3, unchanged), plus `RoomDescriptionMessage` and `MovementMessage`. `CombatMessage` and `PlayerInformationMessage` exist as documented placeholders only — they ship with their gameplay slices, not this one.
- `IOutputFormatter` exists with `TelnetOutputFormatter` as the sole implementation. The telnet formatter renders the basic ANSI palette (system / error / room name / direction) and strips all color when the session signals no support.
- `ISession` exposes `bool SupportsColor { get; }`, default `true` for telnet sessions, sourced from a config default and overridable via an operator seam (the seam exists; the runtime `/color off` command does not).
- The slice-3 stringify-and-forward `IOutputWriter` is replaced by a formatter-backed implementation: `OutputWriter` resolves the `IOutputFormatter` for the session's transport, calls `Format(message, session)`, and awaits `session.SendLineAsync(rendered)`. The `IOutputWriter` / `IOutputWriterFactory` interfaces are unchanged — only the body.
- `help` / `commands` now render with color (section headers in system color, verb names in room-name color) — the typed shapes shaped in slice 3 are now formatted, validating the abstraction end-to-end.
- `look` writes a real `RoomDescriptionMessage` through the formatter (replacing slice 3's broadcast-body passthrough); `MoveCommand` failure writes a `MovementMessage`.
- `IBroadcastSystem` gains an audience-filter predicate on its room-scope method and a system-wide `SendToAllAsync`; its implementation composes `IOutputWriterFactory` + typed messages internally. Interface methods are additive/extended; channel mode is **not** built (acknowledged debt with backlog pointer).
- Inline color formatting is parseable by `IOutputFormatter`, transport-aware (ANSI for telnet, HTML/CSS-class seam reserved for a future web formatter), strippable when unsupported, and aligned with YAML conventions where markers live inside YAML-authored content. The concrete syntax is recorded as the single slice-4 open question, resolved before implementation.
- No gameplay or persistence behaviour changes. Slice-2 and slice-3 smoke still passes; admin events and `CommandExecutedEvent` fire unchanged.

---

## Main Flow

1. **Command produces typed output.** A command body (unchanged from slice 3) calls `context.Output.WriteAsync(IOutputMessage)` — e.g. `look` builds a `RoomDescriptionMessage`, a failed move builds a `MovementMessage`, `help` builds `HelpIndexMessage`/`HelpEntryMessage`.

2. **Writer resolves the formatter.** The replaced `OutputWriter` (still behind the slice-3 `IOutputWriter` interface) asks an `IOutputFormatterRegistry` for the formatter whose `TransportKey` matches the session's transport. This slice only registers `TelnetOutputFormatter` (`TransportKey = "telnet"`); the registry shape supports a future `"signalr"` entry without caller changes.

3. **Formatter renders.** `IOutputFormatter.Format(message, session)` pattern-matches the message shape and produces a transport-encoded string. For telnet: the basic ANSI palette is applied for the four roles (system / error / room name / direction); inline color markers embedded in the message text (per the abstract requirement) are parsed and converted to ANSI. If `session.SupportsColor == false`, every color marker and palette code is stripped, leaving plain text.

4. **Transport emits bytes.** The writer awaits `session.SendLineAsync(rendered)`. The session writes the encoded bytes to its transport stream. No command or domain code is aware of the encoding.

5. **Broadcast — room with audience filter.** A command or system calls `IBroadcastSystem.SendToRoomAsync(roomId, message, audienceFilter)`. The implementation enumerates room occupants, applies the optional `Func<uint,bool>? audienceFilter` predicate (the slice-2 `excludeEntityId` becomes a degenerate filter), and for each surviving recipient resolves their session's writer and emits the typed message — so every recipient gets transport-correct, capability-correct rendering.

6. **Broadcast — system-wide.** `IBroadcastSystem.SendToAllAsync(message)` enumerates every registered session via `ISessionManager` and routes the typed message through each session's writer. Used for shutdown notices, global admin announcements, etc.

7. **Broadcast — channel mode (deferred).** Channel/global-newbie chat is **not** built this slice. It is acknowledged debt: it requires channel-membership state that no slice has introduced yet. Tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md); lands with whichever later slice introduces channel membership.

8. **No gameplay or persistence behaviour changes.** Command bodies, events, components, persistence dirty-marking are byte-for-byte unchanged. The only observable difference is correctly formatted, colorized output instead of slice-3's raw stringification.

---

## Events Fired

This slice fires **no new events**. Output rendering is a synchronous transformation between a command/system and a session; it is not an event-bus concern.

Existing events continue to fire unchanged: `CommandExecutedEvent` (slice 3), `PlayerSaidEvent`, `PlayerMovedEvent`, and the four slice-2 admin events. The output framework neither publishes nor subscribes.

---

## Systems / Handlers Involved

### IOutputMessage and the full shape catalog (extended)

```csharp
public interface IOutputMessage { OutputCategory Category { get; } }

// From slice 3, unchanged:
public sealed record PlainMessage(string Text, OutputSeverity Severity) : IOutputMessage;
public sealed record HelpIndexMessage(IReadOnlyList<HelpIndexEntry> Entries) : IOutputMessage;
public sealed record HelpEntryMessage(string Verb, string LongDescription, string Usage) : IOutputMessage;

// New this slice:
public sealed record RoomDescriptionMessage(uint RoomEntityId, string Name, string Description,
    IReadOnlyDictionary<Direction, string> Exits, IReadOnlyList<string> OccupantNames) : IOutputMessage;
public sealed record MovementMessage(MovementDirectionKind Kind, Direction? Direction, string ActorName) : IOutputMessage;

// Documented placeholders — NOT built this slice; land with combat / player-info slices:
//   CombatMessage, PlayerInformationMessage
```

`Core/Output/Messages/`. Only the shapes the existing 12 commands + broadcast need ship: Room and Movement (plus the slice-3 Plain/Help). `MovementMessage.Kind` is reserved for future finer-grained kinds (`Bumped`, `Slipped`, `Stumbled`).

### IOutputFormatter + TelnetOutputFormatter (new — core abstraction; one impl this slice)

```csharp
public interface IOutputFormatter
{
    string TransportKey { get; }                 // "telnet" this slice; "signalr" later
    string Format(IOutputMessage message, ISession session);
}

public interface IOutputFormatterRegistry
{
    IOutputFormatter Resolve(ISession session);  // by transport key
}
```

`Core/Output/Formatters/TelnetOutputFormatter.cs`. Knows the basic ANSI palette (system / error / room name / direction) as constants. Parses inline color markers (abstract requirement; concrete syntax per open question) into ANSI. Strips all color when `session.SupportsColor == false`. The SignalR/web formatter is **not** built — `IOutputFormatter` + the registry are the seam, deliberately shaped so an HTML/CSS-class implementation drops in without changing any caller or message shape.

### IOutputWriter / IOutputWriterFactory (existing from slice 3 — implementation replaced)

Interfaces unchanged. `OutputWriter` body replaced: instead of stringifying, it resolves the formatter via `IOutputFormatterRegistry`, calls `Format(message, session)`, and awaits `session.SendLineAsync(rendered)`. `IOutputWriterFactory` still binds a writer to a single session. This is the "replace the impl, keep the interface" handoff slice 3 set up.

### ISession (existing — extended)

```csharp
bool SupportsColor { get; }
```

Telnet implementation defaults to `Output:DefaultColor` (new config key, default `true`). The operator override seam exists (config + a settable backing path); the runtime `/color off` command is **not** built this slice. Future SignalR sessions can default differently.

### IBroadcastSystem (existing — extended + reimplemented)

```csharp
Task SendToRoomAsync(uint roomEntityId, IOutputMessage message, Func<uint,bool>? audienceFilter = null);
Task SendToAllAsync(IOutputMessage message);
// channel mode: NOT added this slice — deferred (see backlog)
```

The slice-2 `excludeEntityId` parameter becomes a degenerate `audienceFilter`. Implementation rewritten to compose `IOutputWriterFactory` + typed messages per recipient, so every recipient gets transport- and capability-correct rendering through one pipeline. `SendRoomDescriptionAsync` (if it exists as a slice-2 convenience) now constructs a `RoomDescriptionMessage` and routes through the formatter instead of building a raw `StringBuilder`. Channel mode is acknowledged debt with a backlog pointer.

### Commands (existing — output bodies updated, no signature change)

- **`look`** — now builds a real `RoomDescriptionMessage` (instead of slice 3's broadcast-body passthrough) and routes through the formatter via broadcast.
- **`MoveCommand` (×6)** — failure path writes a `MovementMessage` (instead of slice 3's `PlainMessage`).
- **`help` / `commands`** — typed shapes now rendered with color by the telnet formatter; no command-body change, only the formatter now exists to render them.
- All other commands unchanged from slice 3.

### Formatter does not involve handlers

Output rendering is layer-internal to the output module; it does not subscribe to or publish events and introduces no handlers.

---

## Content Tooling Impact

Pure infrastructure. **No new gameplay state, no new authored data files, no new `TemplateRegistry` entries, no new admin commands.**

- One new config key — `Output:DefaultColor` (default `true`) — sets the initial `ISession.SupportsColor` for new telnet sessions. Operators disable color globally by setting it `false`. This is the operator-override seam; the per-session runtime toggle command is out of scope.
- Where inline color markers live inside YAML-authored content (e.g. prompt config, room descriptions added by later slices), the marker syntax must align with YAML conventions — markers must round-trip cleanly through `YamlDotNet` without quoting hazards. This constrains the open-question syntax decision; it does not by itself add an authored data file this slice.
- No new gameplay state is added; per ground rule 8 this single sentence justifies the absence of authored content — the tooling this slice adds (color rendering, capability flag) is presentation infrastructure, not content state.

---

## Configuration

| Config key | Default | Source |
|---|---|---|
| `Output:DefaultColor` | `true` | New in this slice — initial `ISession.SupportsColor` for new telnet sessions; operators set `false` to disable color globally. |

All slice 1, 2, and 3 keys are unchanged.

---

## Cross-cutting surfaces stressed

- **Output** — *This IS the framework being built.* Slice 4 discharges the explicit owed list slice 3 enumerated: full shape catalog (Room, Movement), `IOutputFormatter` + `TelnetOutputFormatter`, formatter-backed `OutputWriter`, color, capability flag. Resolved by construction.
- **Commands** — **Adequate.** Slice 3 deliberately shaped `CommandContext.Output` and the `IOutputWriter` interface to consume this framework without change. Command signatures and the dispatcher are untouched; only command output *bodies* (`look` builds a real `RoomDescriptionMessage`, move failure builds `MovementMessage`) and the writer *impl* change. No command-framework rework.
- **Broadcast** — **Gap exposed.** Slice-2 broadcast is single-mode (room, with `excludeEntityId`). The audience-filter predicate and system-wide `SendToAllAsync` are required here and resolved in this slice (interface extended, impl recomposed onto the output pipeline). **Channel mode is acknowledged debt** — it needs channel-membership state no slice has introduced; deferred with a backlog entry and a pointer to the future channel-membership slice. The two modes that do not need new state land here; the one that does is honestly deferred, not silently absorbed.
- **Sessions** — **Extends.** `ISession` gains `bool SupportsColor` with a config-sourced telnet default and an operator-override seam. No transport contract change beyond the read-only capability flag.
- **Transport / SignalR** — **Acknowledged debt.** The web/SignalR formatter is not built. `IOutputFormatter` + `IOutputFormatterRegistry` are shaped as the seam so an HTML/CSS-class implementation drops in without caller changes. Deferred per the existing backlog dual-client entry; rationale: web transport is a separate strategic decision and no caller needs it yet.
- **Event bus** — **Adequate.** Output is a synchronous transform, not an event concern; no new events, no new handlers, no bus change.
- **Persistence** — **Adequate.** No `[Persistent]` components; no flush-path impact. `Output:DefaultColor` is read-only config, not persisted state.
- **Content templates** — **Adequate (constrained).** No new authored files this slice; but the inline-color syntax decision is constrained to remain YAML-safe for later content slices that embed markers in YAML.

---

## Flows introduced or modified

- **Modifies Flow 3 — Player command lifecycle** ([`../architecture/06-flows.md`](../architecture/06-flows.md)) again. Slice 3 rewrote Flow 3 with a stringify-and-forward output leg. Slice 4's PR updates Flow 3's output step: `context.Output.WriteAsync` now resolves a formatter, renders with the transport-correct encoding (ANSI + capability strip for telnet), and emits bytes — instead of stringifying. Only the output leg of the diagram and the corresponding step prose change; the parse → authorize → execute → event legs are untouched.
- **Introduces Flow 6 — Output rendering** (new canonical flow; the slice's PR adds it to `06-flows.md` and the index). Trigger: any command or system calls `IOutputWriter.WriteAsync(IOutputMessage)` or `IBroadcastSystem.SendToRoomAsync`/`SendToAllAsync`. Trace: typed `IOutputMessage` → `IOutputWriter` → `IOutputFormatterRegistry.Resolve(session)` → `IOutputFormatter.Format(message, session)` (shape pattern-match → palette + inline-marker parse → capability strip if `!SupportsColor`) → transport-encoded string → `session.SendLineAsync` → bytes. The broadcast variants (audience filter, system-wide) fan the same trace per recipient via `IOutputWriterFactory`. This is a recurring chain every future gameplay slice's output leg plugs into, so it is promoted to a canonical flow rather than left as a one-off.

---

## Design Notes

- **Interface stability across the split.** Slice 3 shaped `IOutputMessage`, `IOutputWriter`, `IOutputWriterFactory` precisely so this slice changes only implementations and adds shapes — no caller churn. That is the whole point of the command/output split being two reviewable slices instead of one.
- **`IBroadcastSystem` composes onto the output pipeline.** Broadcast is the multi-recipient seam; `IOutputWriter` is the single-session seam. Broadcast's body now uses `IOutputWriterFactory` per recipient so all output — single or fanned — goes through one rendering pipeline. The slice-2 `excludeEntityId` degenerates to a filter predicate; no separate exclude path survives.
- **Channel mode is honestly deferred.** It is not silently absorbed: it needs membership state that does not exist yet. The backlog gets an explicit entry pointing at the future channel-membership slice. The two state-free broadcast modes land here so the slice is complete for what the current world can exercise.
- **Color is an abstract requirement, not a pinned syntax.** The requirement: designer-friendly inline color embedded in output strings, parseable by `IOutputFormatter`, transport-aware (ANSI for telnet, HTML/CSS-class seam for future web), strippable when unsupported, YAML-safe where markers live in YAML content. The concrete tag form, named-vs-hex palette, and nesting rules are deliberately left as the single slice-4 open question to resolve at slice-4 planning time. Do not treat any illustrative form as canonical.
- **Color palette is intentionally minimal.** Four roles only — system, error, room name, direction — as constants in `TelnetOutputFormatter`. Theme management is out of scope.
- **SignalR is a seam, not an implementation.** `IOutputFormatter.TransportKey` + `IOutputFormatterRegistry` resolution mean the web formatter is a registration away. Building it is gated on the deferred dual-client decision.
- **Docs to author alongside the implementation slice (not this planning step).** `docs/architecture/07-output.md` (output abstraction + telnet formatter + color requirement + the resolved syntax); update `docs/architecture/00-overview.md` to link it; add Flow 6 and update Flow 3's output leg in `06-flows.md`; add the channel-mode backlog entry to `docs/roadmap/backlog.md`; update `docs/reference/systems.md` for the broadcast changes.
- **Out of scope.** Web/SignalR formatter implementation. Output streaming/paging. Prompt customization. i18n/output template files. Color-theme management. The runtime `/color off` command (the capability seam exists; the verb does not). Channel/global chat.

---

## Open Questions

To be resolved at slice-4 planning time, before `implement-use-case` runs:

1. **Inline color formatting syntax (deliberate deferred decision).** The abstract requirement is fixed (designer-friendly, formatter-parseable, transport-aware ANSI/HTML, strippable, YAML-safe). The concrete decision is open: tag form (e.g. brace/bracket/angle marker shape), named-palette vs hex vs both, nesting/escaping rules, and how a marker survives `YamlDotNet` round-trips in authored content. This was explicitly left open by the user for slice-4 planning; no illustrative form is canonical. Resolve and bake into `docs/architecture/07-output.md` before implementation.

No other open questions. The broadcast model (room+filter, system-wide, channel-deferred), the SignalR-as-seam decision, and the command/output split contract are all resolved and baked in above.

---

## Related

- [`command-framework.md`](command-framework.md) — slice 3; shipped the minimal output seam (`IOutputMessage`, `PlainMessage`, `HelpIndexMessage`, `HelpEntryMessage`, stringify-and-forward `IOutputWriter`) and the explicit owed-stub list this slice discharges. Its `CommandContext.Output` is the consumer this framework serves.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; established `IBroadcastSystem` with the single-mode `excludeEntityId` shape this slice generalizes to an audience filter.
- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; precedent for a pure-infrastructure slice with no gameplay-visible behaviour change.
- `account-character-creation.md` — slice 5 (renumbered); its prompts and character-info output are authored against this framework's shapes, not raw `session.SendLineAsync`.
- `inventory-get-drop.md` (future) — first consumer of a shape this slice doesn't ship (item descriptions); validates that `PlayerInformationMessage` or a new shape fits without ripping plumbing.
- `combat` (future) — first consumer of the `CombatMessage` placeholder; validates the actor/target/damage shape.

**Roadmap impact:** this is **Phase 3 slice 4**. Account / character creation is slice 5; downstream slices shift +2 (this shift is shared with slice 3 and applied once). The plan, use-case index, `06-flows.md`, and backlog are updated in this slice's PR. For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
