# Use Case: Command + Output Framework

**Status:** planned
**Actors:** Player, Administrator, System
**Module:** `Core/Commands/`, `Core/Output/` (new), `Core/Modules/Help/` (new), `Core/Modules/Admin/`, `Core/Modules/Movement/`, `Core/Modules/Chat/`, `Core/Modules/World/`

---

## Description

Pure-infrastructure slice that closes the command-framework gap surfaced after [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) merged. The current `ICommand` shape (`Name`, `Aliases`, `ExecuteAsync(ISession, string)`) forces every command author to roll their own argument parsing, privilege gate, help-text wording, and output formatting. Slice 2 shipped four admin commands that demonstrate the cost: each duplicates `Trim()`/`Split()`, reimplements the privilege check by convention, and emits one-off success/error lines. There is no `help` command, no `commands` index, and the required `@reload` long-form help wording from slice 2 has nowhere to live.

This slice introduces:

1. A first-class **command framework** — typed `CommandContext`, declarative argument parsing, structural privilege gate, help/index commands, and a `CommandExecutedEvent` for cross-cutting audit.
2. A **skeletal output framework** — typed `IOutputMessage` shapes (`Plain`, `RoomDescription`, `Movement` this slice; `Combat`, `PlayerInformation` reserved), a channel-aware `IOutputFormatter` (telnet now, SignalR-shaped later), capability flags on `ISession`, and an `IOutputWriter` that commands talk to via `CommandContext.Output` instead of calling `session.SendLineAsync` directly.

The 12 existing commands (`look`, `say`, six `MoveCommand` instances, `@spawn`, `@teleport`/`@tp`, `@dig`, `@reload`) are refactored onto the new shape. No gameplay change. The slice is positioned ahead of account / character creation so slice-3 commands are authored against the new framework, not retrofitted.

This is an **infrastructure slice**, like [`persistence-substrate.md`](persistence-substrate.md). It introduces no new player-facing verbs beyond `help` and `commands`, and the smoke from slice 2 (host starts, void room loads, telnet listener up) still passes.

---

## Preconditions

- Phase 3 slices 1 and 2 have merged: `IPersistenceSystem`, `ITemplateRegistry`, `IWorldContentLoader`, `IAdminAuthorizer`, `AdminAuditHandler`, and the four admin commands exist and run.
- `ICommand`, `ICommandDispatcher`, `CommandDispatcher`, `ISession`, `ISessionManager`, `IBroadcastSystem`, `IEventBus` continue to exist and are in the keep list.
- `Direction` enum continues to exist.
- The 12 existing commands are wired through `CommandDispatcher` and behave per the slice 2 smoke.

---

## Postconditions

- Every existing command (`look`, `say`, six movement directions, `@spawn`, `@teleport`/`@tp`, `@dig`, `@reload`) implements the new `ICommand` shape: declares `Category`, `ShortDescription`, `LongDescription`, `Usage`, `RequiredPrivilege`, an argument schema, and an `ExecuteAsync(CommandContext)` body. None of them call `session.SendLineAsync(...)` directly; output goes through `CommandContext.Output`.
- Argument parsing is performed by a shared `ICommandArgumentParser` against each command's declarative schema. Commands no longer call `Trim()` / `Split()` in their `ExecuteAsync` body. Quoted-string arguments are supported.
- Admin commands no longer call `IAdminAuthorizer.IsPrivileged` as their first line. Privilege is enforced structurally by `CommandDispatcher` consulting each command's `RequiredPrivilege` property and an injected `IAdminAuthorizer`. Forgetting the gate is no longer possible.
- `help` and `help <verb>` are usable from any session. `help` lists every command visible to the caller (admin commands hidden from non-privileged sessions), grouped by `Category`. `help <verb>` shows the verb's `LongDescription` and `Usage`. The required `@reload` long-form wording from slice 2 is emitted by `help @reload`.
- `commands` is usable from any session and prints a category-grouped one-line index (each entry is `Name` + `ShortDescription`, filtered by visibility).
- Every successful or failed command publishes a `CommandExecutedEvent` whose payload identifies the invoker, verb, normalized-args summary, and result. `AdminAuditHandler` continues to subscribe to its four slice-2 admin events for the rich payload it already uses; it does not subscribe to the generic `CommandExecutedEvent`.
- The telnet output path emits ANSI color codes for the basic palette (system / error / room name / direction) when `ISession.SupportsColor` is `true`, and strips them when `false`. The default for telnet sessions is `true`.
- The rejection-branch usage strings inside the four admin commands (`"Usage: @spawn ..."`, `"Usage: @dig ..."`, etc.) are removed; users who type a malformed command are routed to `help <verb>` by the dispatcher's parse-error response.
- No regressions to existing player-facing behaviour. Slice 2's smoke still passes: host starts, void room loads, telnet listener accepts connections, all four admin commands still emit their slice-2 admin events with the same payloads.

---

## Main Flow

1. **Input arrives.** A line is received from a session. `CommandDispatcher.DispatchAsync(session, input)` parses the verb (first whitespace-delimited token, case-insensitive) and looks up the matching `ICommand` in its verb map. Unknown verb → `Output.WritePlain("Unknown command: <verb>. Type 'help' for a list.")`.

2. **Privilege gate.** The dispatcher consults `command.RequiredPrivilege`. If `Privileged`, it calls `IAdminAuthorizer.IsPrivileged(session)`; if false, it writes a single rejection line via `IOutputWriter` and short-circuits. If `Public`, it proceeds. The dispatcher knows nothing about *what* "privileged" means — that lives behind `IAdminAuthorizer`.

3. **Argument parse.** The dispatcher invokes `ICommandArgumentParser.Parse(command.ArgumentSchema, rawArgsTail)`. The parser tokenizes (respecting quoted strings) and coerces each token against the schema's positional argument list (`string`, `int`, `uint`, `Direction`, etc.). On failure (missing required arg, type coercion error, too many args), the parser returns a structured `ParseResult.Failure` with a human-readable reason. The dispatcher writes the failure reason via `IOutputWriter` followed by a "Type 'help <verb>' for usage." nudge and short-circuits.

4. **Execute.** The dispatcher constructs a `CommandContext { Session, Invoker, ParsedArgs, Output, Services }` and calls `command.ExecuteAsync(context)`. The command body reads typed args from `context.Args.Get<T>(name)`, calls a domain system or publishes an event, and writes results via `context.Output.Write<T>(IOutputMessage)`. Commands no longer touch `session.SendLineAsync` and no longer parse strings.

5. **Output formatting.** `IOutputWriter.Write(IOutputMessage)` resolves the formatter for the session's transport (`TelnetOutputFormatter` for telnet sessions; SignalR formatter is not built this slice but the resolver shape supports it). The formatter renders the typed message — applying ANSI color when `session.SupportsColor` is true, stripping when false — and the writer awaits `session.SendLineAsync(rendered)`.

6. **Post-execute event.** After the command body returns (success or thrown), the dispatcher publishes a `CommandExecutedEvent(uint InvokerEntityId, string Verb, string ArgsSummary, CommandOutcome Outcome)`. `Outcome` is one of `Success`, `ParseFailed`, `Unauthorized`, `Threw`. The dispatcher catches and logs uncaught exceptions; the session sees a generic error line, never a stack trace.

7. **`help` / `commands`.** `HelpCommand` enumerates `IEnumerable<ICommand>` (DI-collected), filters by visibility (admin commands hidden when `IAdminAuthorizer.IsPrivileged(session)` returns false), groups by `Category`, and writes a `HelpIndexMessage` (typed output shape) when no argument is provided, or a `HelpEntryMessage` (also typed) when called with `<verb>`. `CommandsCommand` is a thinner shortcut to the same index — same filtering, terser formatting.

8. **Refactor pass.** Every existing command moves to the new shape:
   - **`look`** — Category `Player`, `RequiredPrivilege.Public`, no args. Writes `RoomDescriptionMessage` (composed by `IBroadcastSystem.SendRoomDescriptionAsync` — see Design Notes for how the broadcast system composes onto the new output abstraction).
   - **`say`** — Category `Player`, one required `string` arg ("message"), publishes `PlayerSaidEvent` unchanged.
   - **`MoveCommand` (×6)** — Category `Player`, no args, publishes `PlayerMovedEvent` unchanged. Failure path writes a `PlainMessage` (system error category).
   - **`@spawn` / `@teleport` / `@dig` / `@reload`** — Category `Admin`, `RequiredPrivilege.Privileged`, declarative arg schemas (`@spawn`: one required `string`; `@teleport`: one required `string`; `@dig`: one `Direction`, one `string`; `@reload`: no args). Slice-2 admin events still published unchanged. The `IAdminAuthorizer.IsPrivileged` calls inside each `Execute` body are removed; the rejection-branch `"Usage: ..."` strings are removed.

9. **Audit hand-off.** `AdminAuditHandler` (existing) continues to subscribe to `EntitySpawnedByAdminEvent`, `PlayerTeleportedByAdminEvent`, `RoomExitAuthoredByAdminEvent`, `ContentReloadedEvent`. It does **not** subscribe to the new `CommandExecutedEvent` — see Design Notes (open question 5 resolution path). The new event is consumed by a separate lightweight `CommandLoggingHandler` that writes a low-volume structured log line per command (or the slice may choose to leave the event unconsumed and let future slices subscribe).

10. **No gameplay or persistence behaviour changes.** The 12 commands' downstream effects (events published, components mutated) are byte-for-byte the same as before the refactor. Persistence dirty-marking, broadcast, and admin-audit paths run unchanged.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `CommandExecutedEvent(uint InvokerEntityId, string Verb, string ArgsSummary, CommandOutcome Outcome)` | `CommandDispatcher` | Per command invocation (success, parse-fail, unauthorized, or threw) | Cross-cutting audit/logging seam. Lets future telemetry / analytics / debugging tools subscribe without per-command code. |
| `PlayerSaidEvent` (existing — `Core/Modules/Chat/Events/`) | `SayCommand` | Per `say` invocation | Unchanged. Refactor must preserve payload exactly. |
| `PlayerMovedEvent` (existing — `Core/Modules/Movement/Events/`) | `MoveCommand` | Per successful move | Unchanged. |
| `EntitySpawnedByAdminEvent` (existing — slice 2) | `SpawnCommand` | Per `@spawn` | Unchanged. |
| `PlayerTeleportedByAdminEvent` (existing — slice 2) | `TeleportCommand` | Per `@teleport` | Unchanged. |
| `RoomExitAuthoredByAdminEvent` (existing — slice 2) | `DigCommand` | Per `@dig` | Unchanged. |
| `ContentReloadedEvent` (existing — slice 2) | `ReloadCommand` | Per `@reload` | Unchanged. |

### `CommandOutcome` enum

```
Success | ParseFailed | Unauthorized | Threw
```

### `ArgsSummary` content

A short normalized rendering of the parsed args for log readability — e.g. `"@dig east room.crossroads"` becomes `"east room.crossroads"`. The dispatcher truncates at 200 characters. Argument values that come from `say`-style free text are also truncated. **No PII filtering** in this slice — chat content is logged in plain. If that becomes an operational concern, a follow-up slice can add a per-command `[NoLogArgs]` attribute.

---

## Systems / Handlers Involved

### ICommand (existing — replaced shape)

Replaces the current three-member interface. Existing properties (`Name`, `Aliases`) are retained; everything else is new.

```csharp
public interface ICommand
{
    string Name { get; }
    IReadOnlyList<string> Aliases { get; }
    CommandCategory Category { get; }              // Player | Admin | System
    string ShortDescription { get; }               // one-line, for `commands` index
    string LongDescription { get; }                // multi-line body for `help <verb>`
    string Usage { get; }                          // formal grammar, e.g. "@dig <direction> <targetRoomBlueprintId>"
    CommandPrivilege RequiredPrivilege { get; }    // Public | Privileged
    CommandArgumentSchema ArgumentSchema { get; }  // declarative arg list
    Task ExecuteAsync(CommandContext context);
}
```

`Core/Commands/ICommand.cs` (extended). The legacy `ExecuteAsync(ISession, string)` overload is removed in this slice — there is no compatibility shim, since the refactor of all 12 commands is in the same PR.

### CommandContext (new — core type)

```csharp
public sealed record CommandContext(
    ISession Session,
    uint InvokerEntityId,
    ParsedArguments Args,
    IOutputWriter Output,
    IServiceProvider Services);   // for the rare command that needs a scoped service
```

Lives at `Core/Commands/CommandContext.cs`. `ParsedArguments` exposes `T Get<T>(string name)`, `bool TryGet<T>(string name, out T value)`, and `bool Has(string name)`. `Services` is included as an escape hatch for commands that genuinely need DI access; the preferred path is constructor injection.

### ICommandDispatcher (existing — extended)

Same interface signature (`Task DispatchAsync(ISession session, string input)`). Implementation gains: privilege gate (consults `IAdminAuthorizer`), parse step (consults `ICommandArgumentParser`), `CommandContext` construction, exception handler, `CommandExecutedEvent` publish.

`CommandDispatcher` constructor gains dependencies on `IAdminAuthorizer`, `ICommandArgumentParser`, `IOutputWriterFactory`, `IEventBus`, `ILogger<CommandDispatcher>`. The dispatcher does **not** know about admin policy — it asks `IAdminAuthorizer` and routes the result. It does not know about formatter choice — it asks `IOutputWriterFactory` for a writer bound to the session.

### ICommandArgumentParser (new — core utility)

```csharp
public interface ICommandArgumentParser
{
    ParseResult Parse(CommandArgumentSchema schema, string rawTail);
}

public abstract record ParseResult
{
    public sealed record Success(ParsedArguments Args) : ParseResult;
    public sealed record Failure(string Reason) : ParseResult;
}

public sealed record CommandArgumentSchema(
    IReadOnlyList<CommandArgument> Positional);

public sealed record CommandArgument(
    string Name,
    Type ClrType,                  // string, int, uint, Direction, ...
    bool Required,
    string? HelpText);
```

Lives at `Core/Commands/CommandArgumentParser.cs`. Default implementation handles tokenization (whitespace + double-quoted strings) and a fixed coercion table for `string`, `int`, `uint`, `Direction`. Adding a new arg type means registering a coercer; this is intentionally simple — fluent schemas and varargs are out of scope.

### IAdminAuthorizer (existing — unchanged)

Same interface. Now consulted by `CommandDispatcher` rather than by each admin command. The slice-2 `IsPrivileged(uint)` overload is retained (still used by the authorizer's own internal logic and by the future `admin-privilege-elevation` slice).

### IOutputWriter / IOutputWriterFactory (new — core abstraction)

```csharp
public interface IOutputWriter
{
    Task WriteAsync(IOutputMessage message);
}

public interface IOutputWriterFactory
{
    IOutputWriter Create(ISession session);
}
```

Lives at `Core/Output/IOutputWriter.cs`. The factory binds a writer to a single session so commands don't pass session references through the formatter. The default `OutputWriter` resolves the right `IOutputFormatter` for the session's transport, calls `Format`, and awaits `session.SendLineAsync(rendered)`.

### IOutputMessage and the typed shapes (new)

```csharp
public interface IOutputMessage { OutputCategory Category { get; } }

public sealed record PlainMessage(string Text, OutputSeverity Severity) : IOutputMessage; // System | Error | Confirmation | Chat
public sealed record RoomDescriptionMessage(uint RoomEntityId, string Name, string Description,
    IReadOnlyDictionary<Direction, string> Exits, IReadOnlyList<string> OccupantNames) : IOutputMessage;
public sealed record MovementMessage(MovementDirectionKind Kind, Direction? Direction, string ActorName) : IOutputMessage;
// Reserved (not built this slice): CombatMessage, PlayerInformationMessage.
```

Lives at `Core/Output/Messages/`. Combat and PlayerInformation shapes are documented placeholders only — they land with their respective slices.

### IOutputFormatter (new — core abstraction; one impl this slice)

```csharp
public interface IOutputFormatter
{
    string TransportKey { get; }            // "telnet" this slice; "signalr" later
    string Format(IOutputMessage message, ISession session);
}
```

`TelnetOutputFormatter` lives at `Core/Output/Formatters/TelnetOutputFormatter.cs`. Knows the basic ANSI palette (system / error / room name / direction). Strips when `session.SupportsColor == false`. SignalR formatter is **not** built — the interface is the seam.

### ISession (existing — extended)

Adds:

```csharp
bool SupportsColor { get; }
```

Defaults to `true` for the telnet implementation. The override path (`/color off` or similar runtime toggle) is **not built** this slice — only the read-only flag and its telnet-default. SignalR sessions, when added, can default differently.

### IBroadcastSystem (existing — composed onto IOutputWriter)

Recommended path (see open question 4): `IBroadcastSystem` continues to exist with the same interface, but its implementation is rewritten to compose `IOutputWriterFactory` + `IOutputMessage` shapes internally. `SendRoomDescriptionAsync` in particular now constructs a `RoomDescriptionMessage` and routes through the formatter rather than building a `StringBuilder` of raw text. Multi-recipient broadcast (`SendToRoomAsync`) loops over recipients, builds an appropriate message, and calls each session's writer. The interface of `IBroadcastSystem` doesn't change — only the body. Commands continue to call `IBroadcastSystem` for room-scope messaging; only single-session output goes through `CommandContext.Output`.

### HelpCommand (new — `Core/Modules/Help/Commands/HelpCommand.cs`)

```csharp
class HelpCommand : ICommand
// Name = "help"; Aliases = ["?"]; Category = Player; RequiredPrivilege = Public.
// ArgumentSchema: optional <verb> string.
// ExecuteAsync: if no verb → write HelpIndexMessage; else write HelpEntryMessage(commands[verb]).
```

Depends on `IEnumerable<ICommand>` (DI-collected), `IAdminAuthorizer`. Visibility filtering happens here.

### CommandsCommand (new — `Core/Modules/Help/Commands/CommandsCommand.cs`)

Thin sibling: terser category-grouped index. Same dependencies as `HelpCommand`. Could share a private formatting helper.

### CommandLoggingHandler (new — `Core/Handlers/CommandLoggingHandler.cs`)

**Events subscribed:** `CommandExecutedEvent`.
**Priority:** 80 (`HandlerPriority.Notification`).
**Responsibilities:** writes a single structured-log line per command via `ILogger<CommandLoggingHandler>` with stable event name `CommandExecuted`. Best-effort, log-only, never throws.

This handler is intentionally *separate* from `AdminAuditHandler`. The latter consumes the rich slice-2 admin events for compliance-grade audit detail. The former is a low-fidelity command trace useful in development; it can be turned off by log-level filtering without affecting audit.

### AdminAuditHandler (existing — unchanged)

Still subscribes to the four slice-2 admin events. Does not subscribe to `CommandExecutedEvent`.

### PersistenceHandler (existing — unchanged)

Still subscribes to `EntitySpawnedByAdminEvent` and `RoomExitAuthoredByAdminEvent`. Not affected by this slice.

---

## Content Tooling Impact

This slice is pure infrastructure. **No new gameplay state, no new authored data files, no new `TemplateRegistry` entries, no new admin commands beyond `help` / `commands` (which are tooling for the framework itself, not for content).**

- The slice-2 admin commands (`@spawn`, `@teleport`, `@dig`, `@reload`) remain as they were — same verbs, same effects, same events. Their bodies are refactored onto the new framework, and their hand-written rejection-branch `"Usage: ..."` strings are retired in favour of `help <verb>`.
- The `@reload` long-form help wording required by slice 2 is moved into `ReloadCommand.LongDescription` and emitted by `help @reload`.
- The configurable `Admin:PrivilegedNames` and `Persistence:DataDirectory` keys are unchanged.
- One new optional config key — `Output:DefaultColor` (default `true`) — sets the initial value of `ISession.SupportsColor` for new telnet sessions. Operators can disable color globally by setting it to `false`.

Per ground rule 8: no gameplay state is added, so a single sentence justifies the absence of authored content — **this slice introduces no new gameplay state; the tooling it does add (`help`, `commands`) inspects the framework itself.**

---

## Configuration

| Config key | Default | Source |
|---|---|---|
| `Output:DefaultColor` | `true` | New in this slice — initial `ISession.SupportsColor` for new telnet sessions. |

All slice 1 and slice 2 keys are unchanged.

---

## Design Notes

- **`CommandContext` replaces `(ISession, string)` outright.** No compatibility shim. Cost: every command moves in this PR. Benefit: there is no second shape to maintain, no risk of new commands sneaking in against the old interface, and the refactor is bounded by 12 known files. See open question 3.
- **Privilege gate is structural.** The dispatcher consults `command.RequiredPrivilege` and asks `IAdminAuthorizer`. A new admin command author who forgets to set `RequiredPrivilege = Privileged` exposes an admin verb to non-privileged sessions — but the property is a required interface member with no default, so the compiler enforces a choice. This is the strongest "structural guarantee" available without coupling the dispatcher to admin policy. See open question 2.
- **Argument schema shape — declarative POCO list.** The schema is a `record` containing a list of `CommandArgument` records. Commands declare their schema as a static or constructor-built `CommandArgumentSchema` property. Attributes-on-method and fluent builders were considered and rejected for this slice (attributes need reflection at startup; builders read poorly for 1–2-arg commands). A migration to attribute or builder shapes can be added later without changing `ParseResult`. See open question 1.
- **`IBroadcastSystem` composes onto `IOutputWriter`.** `IBroadcastSystem` is the multi-recipient/room-scope path; `IOutputWriter` is the single-session path. Implementation-wise, `BroadcastSystem` will use `IOutputWriterFactory` internally to render typed messages per-recipient. Commands continue to call `IBroadcastSystem.SendToRoomAsync`/`SendRoomDescriptionAsync` directly when they need room-scope output; they call `context.Output.WriteAsync` for single-session output. Two seams, one rendering pipeline. See open question 4.
- **`CommandExecutedEvent` is fired for every dispatch.** Including parse failures and unauthorized attempts — both are useful in development. The event is *not* consumed by `AdminAuditHandler` because the slice-2 admin events carry richer payloads (spawned entity id, teleport destination room, etc.) that a generic `CommandExecutedEvent` cannot. Granularity of the new event is "every invocation"; opt-out is via log filtering, not event filtering. See open question 5.
- **`help` formatting uses typed output shapes.** `HelpIndexMessage` and `HelpEntryMessage` are `IOutputMessage` implementations. The telnet formatter renders them with section headers in the system color, verb names in the room-name color, and short descriptions in plain text. This validates the typed-output abstraction on the first non-trivial use case. See open question 6.
- **Color palette is intentionally minimal.** Four roles only this slice — `System`, `Error`, `RoomName`, `Direction`. Anything more is deferred. The palette lives as constants in `TelnetOutputFormatter`; theme management is out of scope.
- **`MovementMessage` is reserved usage.** The two consumers in this slice are `MoveCommand` (the failed-move-cannot-go-that-way line) and any future arrival/departure flavour text the broadcast system emits. Slice-3 and onward can add finer-grained kinds (`Bumped`, `Slipped`, `Stumbled`).
- **No exception leaks.** The dispatcher wraps `ExecuteAsync` in try/catch. Uncaught exceptions log a stack trace at `Error` level via `ILogger<CommandDispatcher>` and emit a `PlainMessage(text: "Something went wrong. The error has been logged.", severity: Error)` to the session. `CommandOutcome.Threw` flags the event for the logging handler.
- **Slice numbering.** This slice is being inserted ahead of "account / character creation" in the slice queue. The roadmap (`docs/roadmap/plan.md`) will need to renumber slices 3 onward when this slice merges. The numbering choice itself is open question 7.
- **Docs to author alongside the implementation slice (not this planning step).** The `implement-use-case` agent is expected to land:
  - `docs/architecture/06-commands.md` — framework design (or fold into 07).
  - `docs/architecture/07-output.md` — output abstraction + telnet formatter.
  - `docs/reference/commands.md` — living catalog of every command (parallel to `systems.md`/`handlers.md`).
  - Update `docs/architecture/00-overview.md` to link the new docs.
  - Update `CLAUDE.md` ground rules to add a rule that every command lives in the framework — arg parsing, help, and privilege are not hand-rolled.
  - The slice should also retire the rejection-branch usage strings inside slice-2's admin commands now that `help <verb>` exists.
- **Out of scope for this slice.** Tab completion. Output streaming/paging. Prompt customization. Output template files (i18n). Color-theme management. The SignalR transport itself. A `/color off` runtime toggle (the seam exists; the command does not).

---

## Open Questions

To be resolved before `implement-use-case` runs.

1. **Argument schema shape.** Three options:
   - **(a)** Declarative POCO list — `CommandArgumentSchema(IReadOnlyList<CommandArgument>)`. Simple, no reflection. **Recommended for this slice.**
   - **(b)** Attributes on method parameters — `ExecuteAsync(CommandContext ctx, [Required] string blueprintId)`. Reads cleanly but requires reflection at startup and doesn't compose with the `CommandContext` shape.
   - **(c)** Fluent builder in the command's constructor — `Schema.Required<string>("blueprintId")`. Reads cleanly for multi-arg commands but verbose for zero-arg (movement) commands.
   - Decision affects parser implementation and ergonomic feel of every future command.

2. **Privilege gate enforcement seam.** Three options:
   - **(a)** `RequiredPrivilege` property on `ICommand` + dispatcher middleware. Structural; can't be forgotten because it's a required interface member. **Recommended.**
   - **(b)** Attribute on the command class — `[RequiresPrivilege]`. Optional by default, easy to forget.
   - **(c)** Per-command call to `IAdminAuthorizer` (status quo from slice 2). Convention only, no structural guarantee.
   - Decision is the central bet of this slice; (a) is the only option that delivers "forgetting is impossible."

3. **`CommandContext` vs compatibility shim.** Options:
   - **(a)** Replace the old `(ISession, string)` shape outright. All 12 commands move in this PR. No second shape to maintain. **Recommended.**
   - **(b)** Keep both shapes; new commands use `CommandContext`, old commands stay on `(ISession, string)` until they're touched for other reasons.
   - (a) is feasible only because the surface is small (12 commands, all in the keep list).

4. **`IBroadcastSystem` and the new output channel.** Options:
   - **(a)** `BroadcastSystem` is rewritten to compose `IOutputWriterFactory` internally. Interface unchanged; commands keep calling `IBroadcastSystem` for room-scope output. **Recommended.**
   - **(b)** `IBroadcastSystem` is retired and folded into a multi-recipient method on `IOutputWriter`. Larger churn; saves an interface.
   - (a) preserves the slice-2 surface and lets us change the broadcast implementation later if (b) becomes attractive.

5. **`CommandExecutedEvent` granularity.** Options:
   - **(a)** Fire for every dispatch (success, parse-fail, unauthorized, threw). Audit-log noise is controlled by log-level filtering. **Recommended.**
   - **(b)** Fire only for `Success`. Smaller stream, but loses unauthorized-attempt detection — useful for ops.
   - **(c)** Opt-in via a per-command `[Logged]` attribute. Inconsistent coverage.

6. **`help` formatting via typed output shapes.** Options:
   - **(a)** Yes — `HelpIndexMessage` and `HelpEntryMessage` are `IOutputMessage` implementations rendered by `TelnetOutputFormatter`. Validates the typed-output abstraction on the first non-trivial output. **Recommended.**
   - **(b)** No — `help` writes a plain string; the typed-output system is reserved for future combat/inventory/look outputs.
   - (b) keeps this slice smaller; (a) gets us one real consumer of the typed shapes immediately so we can shake out the formatter design.

7. **Slice number.** Options:
   - **(a)** Slice 3, renumbering current slice 3 ("account / character creation") to slice 4 and shifting downstream. **Recommended (matches user instinct).**
   - **(b)** Slice 2.5 / postscript. Avoids renumbering downstream slices but is awkward in `done.md`.
   - (a) is the cleaner ledger entry; the cost is a one-time edit of `docs/roadmap/plan.md`.

---

## Related

- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; established the four admin commands, the `@`-prefix admin convention, `IAdminAuthorizer`, and the convention-only privilege check that this slice replaces with a structural gate. Also defined the required `@reload` help-text wording that this slice finally surfaces via `help @reload`.
- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; precedent for a pure-infrastructure slice with no gameplay-visible behaviour change.
- [`admin-privilege-elevation.md`](admin-privilege-elevation.md) — deferred / placeholder; the future `@grant` / `@revoke` commands will plug into this slice's framework on first write rather than be retrofitted.
- `account-character-creation.md` — currently labelled slice 3 in `docs/roadmap/plan.md`. **This slice is being inserted ahead of it**, so account/character-creation will renumber to slice 4 (subject to open question 7). Once renumbered, its login-prompt verbs and character-management commands should be authored against the framework landed here, not against the legacy `(ISession, string)` shape.
- `inventory-get-drop.md` (future) — first consumer of an `IOutputMessage` shape this slice doesn't ship (item descriptions). Will validate that `PlayerInformationMessage` or a new shape fits without ripping plumbing.
- `combat` (future) — first consumer of `CombatMessage`. Will validate the actor/target/damage shape.

**Roadmap impact:** the slice queue in [`../roadmap/plan.md`](../roadmap/plan.md) needs revision when this slice merges (current "Phase 3 slice 3 — account / character creation" shifts to slice 4, and downstream slices follow). The exact renumbering is gated on open question 7. **Do not edit `plan.md` until that question is resolved**; that edit is part of the implementation slice's PR, not this planning step.

For the slice queue and ordering rationale, see [`../roadmap/plan.md`](../roadmap/plan.md).
