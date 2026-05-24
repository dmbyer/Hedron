# Use Case: Command Framework

**Status:** implemented
**Actors:** Player, Administrator, System
**Module:** `Core/Commands/`, `Core/Modules/Help/`

---

## Description

Pure-infrastructure slice that closes the command-framework gap surfaced after [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) merged. The slice-2 `ICommand` shape (`ExecuteAsync(ISession, string)`) forced every command author to roll their own argument parsing, privilege gate, and help-text wording, with no `help`/`commands` index.

This slice introduces a first-class **command framework**:

1. A typed `CommandContext` that replaces `(ISession, string)` outright — no compatibility shim.
2. A declarative `ICommandArgumentParser` with a `Kind` discriminator and a forward-compatible `Resolver` seam.
3. A structural privilege gate driven by `IAuthorizationRequirement` / `IAuthorizationChecker`, enforced by the dispatcher — replacing slice 2's per-command `IsPrivileged` convention.
4. A `CommandExecutedEvent` fired on every dispatch, consumed by a lightweight `CommandLoggingHandler`.
5. `help` / `commands` commands and a `HelpModule`.

It also lands the **bare-minimum output seam** (`IOutputMessage`, `PlainMessage`, `HelpEntryMessage`, `HelpIndexMessage`, a stringify-and-forward `IOutputWriter`) — the full formatter/color/broadcast work is slice 4 ([`output-framework.md`](output-framework.md)). The 12 existing commands are refactored onto the new shape with no gameplay change.

---

## Preconditions

- Phase 3 slices 1 and 2 merged: `IPersistenceSystem`, `ITemplateRegistry`, `IWorldContentLoader`, `IAdminAuthorizer`, `AdminAuditHandler`, and the four admin commands exist and run.
- `ICommand`, `ICommandDispatcher`, `CommandDispatcher`, `ISession`, `ISessionManager`, `IEventBus`, and the `Direction` enum exist.
- The 12 existing commands are wired through `CommandDispatcher` and behave per the slice-2 smoke.

---

## Postconditions

- Every command implements the new `ICommand` shape (`Category`, `ShortDescription`, `LongDescription`, `Usage`, `RequiredPrivileges`, `ArgumentSchema`, `ExecuteAsync(CommandContext)`). None call `session.SendLineAsync` — output goes through `CommandContext.Output`.
- **No `session.SendLineAsync` survives anywhere on the dispatch path, including the dispatcher's own branches** (unknown-verb, parse-error, unauthorized, exception) — all route through an `IOutputWriter` from `IOutputWriterFactory.Create(session)` (INV-11).
- Argument parsing is performed by a shared `ICommandArgumentParser` against each command's declarative schema. Double-quoted arguments are supported; enum-prefix matching works (`n`/`no`/`nor` → `north`).
- Admin commands no longer call `IAdminAuthorizer.IsPrivileged`. Privilege is enforced structurally by the dispatcher consulting `RequiredPrivileges` + an injected `IAuthorizationChecker`. `RequiredPrivileges` is a required member; empty list = public.
- `help` / `help <verb>` / `commands` are usable from any session, grouped by `Category`, with admin commands hidden when authorization fails. Rendered via typed `HelpIndexMessage` / `HelpEntryMessage`.
- Every dispatch (success, parse-fail, unauthorized, threw) publishes `CommandExecutedEvent`; `CommandLoggingHandler` (priority 80) writes one structured-log line. `AdminAuditHandler` keeps its four slice-2 admin events; it does not subscribe to `CommandExecutedEvent`.
- No regressions: host starts, void room loads, telnet accepts connections, all admin commands emit their slice-2 events with identical payloads.

---

## Main Flow

1. **Input arrives.** `CommandDispatcher.DispatchAsync(session, input)` splits the verb and looks it up (exact + alias). Unknown verb → `PlainMessage("Unknown command…")`.
2. **Privilege gate.** The dispatcher iterates `command.RequiredPrivileges` and calls `IAuthorizationChecker.IsSatisfied(requirement, session)`. Slice 3 ships one requirement type, `AdminRequirement` (delegates to `IAdminAuthorizer`). Any unsatisfied requirement → rejection line, `CommandOutcome.Unauthorized`, short-circuit. The dispatcher knows nothing about what a requirement *means*.
3. **Argument parse.** `ICommandArgumentParser.Parse(schema, rawTail)` tokenizes (whitespace + double-quoted), walks the schema applying each argument's `Kind` (`Token` / `RestOfLine` / `Quantified`), and coerces to the CLR type (`string`, `int`, `uint`, `Direction`; enum prefix-matched). Failure → reason + "Type 'help <verb>' for usage.", `CommandOutcome.ParseFailed`.
4. **Execute.** The dispatcher builds `CommandContext { Session, InvokerEntityId, Args, Output, Services }` and calls `ExecuteAsync(context)`. The body reads typed args via `context.Args.Get<T>(name)`, calls a domain system or publishes an event, writes via `context.Output.WriteAsync(IOutputMessage)`.
5. **Output (minimal seam).** `IOutputWriter.WriteAsync` stringifies the message and awaits `session.SendLineAsync`. No formatter/color — slice 4 replaces this impl.
6. **Post-execute event.** The dispatcher publishes `CommandExecutedEvent(InvokerEntityId, Verb, ArgsSummary, Outcome)`. Uncaught exceptions are trapped, logged with stack trace at `Error`, and the session gets a generic `PlainMessage` — never a stack trace.
7. **`help` / `commands`.** `HelpCommand` enumerates DI-collected `IEnumerable<ICommand>`, filters by visibility, groups by `Category`, writes `HelpIndexMessage` / `HelpEntryMessage`. `CommandsCommand` is a thinner shortcut.
8. **Refactor pass.** The dispatcher's internal output call sites move first (unknown-verb, parse-error, unauthorized, exception-trap → `IOutputWriter`), then the 12 commands onto the new shape. Slice-2 admin events still fire unchanged; in-body `IsPrivileged` calls and rejection usage strings removed.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `CommandExecutedEvent(uint InvokerEntityId, string Verb, string ArgsSummary, CommandOutcome Outcome)` | `CommandDispatcher` | Every dispatch | Cross-cutting audit/logging seam. |
| `PlayerSaidEvent` (existing) | `SayCommand` | Per `say` | Unchanged. |
| `PlayerMovedEvent` (existing) | `MoveCommand` | Per successful move | Unchanged. |
| `EntitySpawnedByAdminEvent` / `PlayerTeleportedByAdminEvent` / `RoomExitAuthoredByAdminEvent` / `ContentReloadedEvent` (existing — slice 2) | the four admin commands | Per admin action | Unchanged. |

`CommandOutcome`: `Success | ParseFailed | Unauthorized | Threw`. `ArgsSummary` is a short normalized rendering of parsed args, truncated at 200 chars. **Known gap:** no argument redaction this slice — `say` content (and any future auth-bearing verb) is logged in plaintext; acceptable while the only free-text verb is `say` and the logger is local, but a prerequisite for any retained/forwarded sink. Tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md) ("Command-arg log redaction").

---

## Design Notes

- **`CommandContext` replaces `(ISession, string)` outright.** No shim — every command moves in one PR. Benefit: one shape, no risk of new commands sneaking in against the old interface.
- **Privilege gate is structural and extensible.** `RequiredPrivileges` is a required member with no default (empty list = explicit "public"). The dispatcher is decoupled from policy via `IAuthorizationChecker`; future requirement types (guild-leader, zone-owner) register without dispatcher edits.
- **`CommandDispatcher` is an Initiator-tier runtime, not a system** — INV-5/INV-8 permit it to publish `CommandExecutedEvent`; it is the only component that observes every dispatch outcome, so the event cannot be sourced elsewhere. (The six commands that publish events are Initiators too.)
- **Argument schema is a declarative POCO list** with a `Kind` discriminator. `Resolver` is a null seam today (future entity-name matching). Enum-prefix matching ships; verb-prefix matching is deferred (added in slice 3a).
- **`CommandExecutedEvent` fires for every dispatch** — including parse-fail and unauthorized, both useful operationally. Opt-out is log-level filtering, not event filtering.
- **`help` dogfoods typed output** — `HelpIndexMessage`/`HelpEntryMessage` are real shapes even though the slice-3 writer only stringifies them, giving slice 4 a real consumer to render with color.
- **The minimal output seam is deliberately incomplete** — slice 4 replaces the writer impl, not the interface.
- **No exception leaks** — the dispatcher wraps `ExecuteAsync` in try/catch; uncaught exceptions log a stack trace and emit a generic `PlainMessage`; `CommandOutcome.Threw` flags the event.
- **`CommandDispatcher` dependency count is a known smell, deferred** — five injected dependencies trend toward a god-class; a `CommandPipeline` middleware chain would isolate the concerns. Deferred to avoid ballooning the 12-command refactor; tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md).

---

## Related

- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; established the four admin commands and the convention-only privilege check this slice replaces.
- [`output-framework.md`](output-framework.md) — slice 4; consumes the minimal output seam shaped here and replaces the stringify-and-forward writer.
- [`command-prefix-matching.md`](command-prefix-matching.md) — slice 3a; adds verb-prefix resolution deferred here.
- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; precedent for a pure-infrastructure slice.
- [`../architecture/subsystems/commands.md`](../architecture/subsystems/commands.md) — the command framework design reference.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
