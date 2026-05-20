# Use Case: Command Framework

**Status:** implemented
**Actors:** Player, Administrator, System
**Module:** `Core/Commands/`, `Core/Modules/Help/` (new); refactor touches `Core/Modules/Admin/`, `Core/Modules/Movement/`, `Core/Modules/Chat/`, `Core/Modules/World/`

---

## Description

Pure-infrastructure slice that closes the command-framework gap surfaced after [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) merged. The slice-2 `ICommand` shape (`Name`, `Aliases`, `ExecuteAsync(ISession, string)`) forces every command author to roll their own argument parsing, privilege gate, and help-text wording. The four slice-2 admin commands demonstrate the cost: each duplicates `Trim()`/`Split()`, reimplements the privilege check by convention, and emits one-off rejection usage strings. There is no `help` command, no `commands` index, and the required `reload` long-form help wording from slice 2 has nowhere to live.

This slice introduces a first-class **command framework**:

1. A typed `CommandContext` that replaces `(ISession, string)` outright — no compatibility shim.
2. A declarative `ICommandArgumentParser` with a `Kind` discriminator and a forward-compatible `Resolver` seam.
3. A structural privilege gate driven by `IAuthorizationRequirement` / `IAuthorizationChecker`, enforced by dispatcher middleware (not per-command convention) — this **reopens and replaces** slice 2's per-command `IsPrivileged` convention.
4. A `CommandExecutedEvent` fired on every dispatch, consumed by a lightweight `CommandLoggingHandler`.
5. `help` / `commands` commands and a `HelpModule`.

It also lands the **bare-minimum output seam** so commands have a typed sink and `help` can dogfood typed output: `IOutputMessage`, `PlainMessage`, `HelpEntryMessage`, `HelpIndexMessage`, and a stringify-and-forward `IOutputWriter`. **No formatter, no color, no `RoomDescription`/`Movement`/`Combat`/`PlayerInformation` shapes, no SignalR seam** — those are explicitly slice 4 ([`output-framework.md`](output-framework.md)). Slice 3 leaves these as named stubs so the slice-4 planner knows precisely what is owed.

The 12 existing commands (`look`, `say`, six `MoveCommand` instances, `spawn`, `teleport`/`tp`, `dig`, `reload`) are refactored onto the new shape. No gameplay change. This is an **infrastructure slice**, like [`persistence-substrate.md`](persistence-substrate.md); it introduces no new player-facing verbs beyond `help` and `commands`, and the slice-2 smoke still passes.

---

## Preconditions

- Phase 3 slices 1 and 2 have merged: `IPersistenceSystem`, `ITemplateRegistry`, `IWorldContentLoader`, `IAdminAuthorizer`, `AdminAuditHandler`, and the four admin commands exist and run.
- `ICommand`, `ICommandDispatcher`, `CommandDispatcher`, `ISession`, `ISessionManager`, `IEventBus` continue to exist and are in the keep list.
- `Direction` enum continues to exist.
- The 12 existing commands are wired through `CommandDispatcher` and behave per the slice 2 smoke.

---

## Postconditions

- Every existing command implements the new `ICommand` shape: declares `Category`, `ShortDescription`, `LongDescription`, `Usage`, `RequiredPrivileges`, an `ArgumentSchema`, and an `ExecuteAsync(CommandContext)` body. None call `session.SendLineAsync(...)` directly; output goes through `CommandContext.Output`.
- **No `session.SendLineAsync` survives anywhere on the dispatch path, including the dispatcher's own branches.** The existing unknown-verb call site at `Core/Commands/CommandDispatcher.cs:43` (`session.SendLineAsync($"Unknown command: {verb}")`) and any parse-error / unauthorized / exception output the dispatcher emits are routed through an `IOutputWriter` obtained via `IOutputWriterFactory.Create(session)`. This call site is named explicitly in the refactor checklist (Main Flow step 8) so it is not missed — it is *not* one of the 12 command files and would otherwise survive the refactor and break this postcondition on day one (INV-11).
- Argument parsing is performed by a shared `ICommandArgumentParser` against each command's declarative schema. Commands no longer call `Trim()` / `Split()` in their bodies. Double-quoted arguments are supported. Enum-prefix matching works from day one (`n`/`no`/`nor` resolves to `north`).
- Admin commands no longer call `IAdminAuthorizer.IsPrivileged` as their first line. Privilege is enforced structurally by `CommandDispatcher` consulting each command's `RequiredPrivileges` and an injected `IAuthorizationChecker`. Forgetting the gate is impossible — `RequiredPrivileges` is a required interface member; empty list = public.
- `help` and `help <verb>` are usable from any session. `help` lists every command visible to the caller (admin commands hidden when authorization fails), grouped by `Category`, rendered via typed `HelpIndexMessage`. `help <verb>` shows `LongDescription` and `Usage` via `HelpEntryMessage`. The required `reload` long-form wording from slice 2 is emitted by `help reload`.
- `commands` (alias none) prints a terser category-grouped one-line index, same visibility filtering.
- Every dispatch — success, parse-fail, unauthorized, threw — publishes a `CommandExecutedEvent`. `CommandLoggingHandler` (priority 80) writes one structured-log line per command. `AdminAuditHandler` continues to subscribe to its four slice-2 admin events; it does **not** subscribe to `CommandExecutedEvent`.
- The rejection-branch usage strings inside the four admin commands are removed; malformed input is routed to `help <verb>` by the dispatcher's parse-error response. Slice-2 admin events still fire with identical payloads.
- No regressions: host starts, void room loads, telnet listener accepts connections, all four admin commands still emit their slice-2 admin events with the same payloads.

---

## Main Flow

1. **Input arrives.** A line is received on a session. `CommandDispatcher.DispatchAsync(session, input)` splits the verb (first whitespace token, case-insensitive) and looks it up in the verb map (exact + alias; enum/verb-prefix matching for verbs is deferred). Unknown verb → `Output.WriteAsync(PlainMessage("Unknown command: <verb>. Type 'help' for a list."))`.

2. **Privilege gate.** The dispatcher iterates `command.RequiredPrivileges` and calls `IAuthorizationChecker.IsSatisfied(requirement, session)` for each. Slice 3 ships exactly one requirement type, `AdminRequirement`, whose checker delegates to the existing `IAdminAuthorizer`. Empty list = public, passes. Any unsatisfied requirement → a single rejection line via `IOutputWriter`, `CommandOutcome.Unauthorized`, short-circuit. The dispatcher knows nothing about *what* a requirement means — that lives behind `IAuthorizationChecker`.

3. **Argument parse.** The dispatcher invokes `ICommandArgumentParser.Parse(command.ArgumentSchema, rawTail)`. The parser tokenizes (whitespace + double-quoted strings), then walks the schema's argument list applying each argument's `Kind` (`Token` — single token; `RestOfLine` — consume remainder; `Quantified` — leading count + token) and coerces against the CLR type (`string`, `int`, `uint`, `Direction`). `Direction` (and any enum) uses prefix matching. The `Resolver` seam is null this slice. On failure → `ParseResult.Failure(reason)`; the dispatcher writes the reason + "Type 'help <verb>' for usage." and short-circuits with `CommandOutcome.ParseFailed`.

4. **Execute.** The dispatcher constructs `CommandContext { Session, InvokerEntityId, Args, Output, Services }` and calls `command.ExecuteAsync(context)`. The body reads typed args via `context.Args.Get<T>(name)`, calls a domain system or publishes an event, and writes results via `context.Output.WriteAsync(IOutputMessage)`. No string parsing, no `session.SendLineAsync`.

5. **Output (minimal seam).** `IOutputWriter.WriteAsync(IOutputMessage)` stringifies the message (`PlainMessage` → its text; `HelpIndexMessage`/`HelpEntryMessage` → a plain-text rendering) and awaits `session.SendLineAsync(text)`. No formatter, no color. Slice 4 replaces this implementation with a formatter-backed one.

6. **Post-execute event.** Whether the body returned or threw, the dispatcher publishes `CommandExecutedEvent(uint InvokerEntityId, string Verb, string ArgsSummary, CommandOutcome Outcome)`. Uncaught exceptions are trapped, logged with a stack trace at `Error`, and the session gets a generic `PlainMessage` — never a stack trace. `Outcome` ∈ `Success | ParseFailed | Unauthorized | Threw`.

7. **`help` / `commands`.** `HelpCommand` enumerates DI-collected `IEnumerable<ICommand>`, filters by visibility (admin commands hidden when their `RequiredPrivileges` are unsatisfied for the caller — checked via `IAuthorizationChecker`), groups by `Category`, and writes `HelpIndexMessage` (no arg) or `HelpEntryMessage` (with `<verb>`). `CommandsCommand` is a thinner shortcut to the same index.

8. **Refactor pass.** The dispatcher's own internal output call sites move first, then every existing command:
   - **`CommandDispatcher` internals** — the unknown-verb branch (`CommandDispatcher.cs:43`), the parse-error response, the unauthorized response, and the exception-trap response all write through an `IOutputWriter` (via `IOutputWriterFactory.Create(session)`), never `session.SendLineAsync`. This is the V3 fix from the spec-mode review; it is a checklist item, not an implied consequence of the command refactor.
   - **`look`** — Category `Player`, empty `RequiredPrivileges`, no args. Writes whatever room-description output the command currently produces via `IBroadcastSystem` (broadcast body is untouched this slice; slice 4 reworks it).
   - **`say`** — Category `Player`, one `RestOfLine` `string` arg ("message"), publishes `PlayerSaidEvent` unchanged.
   - **`MoveCommand` (×6)** — Category `Player`, no args, publishes `PlayerMovedEvent` unchanged. Failure path writes a `PlainMessage`.
   - **`spawn` / `teleport` / `dig` / `reload`** — Category `Admin`, `RequiredPrivileges = [AdminRequirement]`, declarative schemas (`spawn`: one `Token` `string`; `teleport`: one `Token` `string`; `dig`: one `Token` `Direction`, one `Token` `string`; `reload`: none). Slice-2 admin events still published unchanged. In-body `IsPrivileged` calls and rejection usage strings removed.

9. **Audit hand-off.** `AdminAuditHandler` (existing) keeps subscribing to its four slice-2 admin events for the rich payload. `CommandLoggingHandler` (new, priority 80) consumes `CommandExecutedEvent` for a low-fidelity command trace controllable via log level.

10. **No gameplay or persistence behaviour changes.** The 12 commands' downstream effects are byte-for-byte unchanged. Persistence dirty-marking, broadcast, and admin-audit paths run unchanged.

---

## Events Fired

| Event | Publisher | Scope | Purpose |
|---|---|---|---|
| `CommandExecutedEvent(uint InvokerEntityId, string Verb, string ArgsSummary, CommandOutcome Outcome)` | `CommandDispatcher` | Every dispatch (success, parse-fail, unauthorized, threw) | Cross-cutting audit/logging seam. Future telemetry subscribes without per-command code. |
| `PlayerSaidEvent` (existing — `Core/Modules/Chat/Events/`) | `SayCommand` | Per `say` | Unchanged — refactor preserves payload exactly. |
| `PlayerMovedEvent` (existing — `Core/Modules/Movement/Events/`) | `MoveCommand` | Per successful move | Unchanged. |
| `EntitySpawnedByAdminEvent` (existing — slice 2) | `SpawnCommand` | Per `spawn` | Unchanged. |
| `PlayerTeleportedByAdminEvent` (existing — slice 2) | `TeleportCommand` | Per `teleport` | Unchanged. |
| `RoomExitAuthoredByAdminEvent` (existing — slice 2) | `DigCommand` | Per `dig` | Unchanged. |
| `ContentReloadedEvent` (existing — slice 2) | `ReloadCommand` | Per `reload` | Unchanged. |

### `CommandOutcome` enum

```
Success | ParseFailed | Unauthorized | Threw
```

### `ArgsSummary` content

A short normalized rendering of the parsed args for log readability — e.g. `dig east room.crossroads` summarizes to `east room.crossroads`. Dispatcher truncates at 200 characters. **Known operational gap (spec-mode S4):** there is no argument-redaction mechanism this slice, so `say` content (and any future `tell` / `password` / auth-bearing verb) is written to logs in plaintext. This is acceptable for slice 3 because the only verb with free-text args is `say` and the logger is local, but it **must not** ship to any environment with retained/forwarded logs without redaction. Tracked as an explicit acknowledged-debt item in [`../roadmap/backlog.md`](../roadmap/backlog.md) ("Command-arg log redaction") with the proposed fix (a per-command `[NoLogArgs]` / `RedactArgs` declaration honored by the dispatcher before it builds `ArgsSummary`). It is a prerequisite for any non-local logging sink. Partial-success vs command-success is a layer above and stays inside the command body — it does not affect `Outcome`.

---

## Systems / Handlers Involved

### ICommand (existing — replaced shape)

```csharp
public interface ICommand
{
    string Name { get; }
    IReadOnlyList<string> Aliases { get; }
    CommandCategory Category { get; }                              // Player | Admin | System
    string ShortDescription { get; }                               // one-line, for `commands`
    string LongDescription { get; }                                // multi-line body for `help <verb>`
    string Usage { get; }                                          // formal grammar
    IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } // empty = public
    CommandArgumentSchema ArgumentSchema { get; }
    Task ExecuteAsync(CommandContext context);
}
```

`Core/Commands/ICommand.cs`. The legacy `ExecuteAsync(ISession, string)` overload is removed — no shim; all 12 commands refactor in the same PR.

### CommandContext (new — core type)

```csharp
public sealed record CommandContext(
    ISession Session,
    uint InvokerEntityId,
    ParsedArguments Args,
    IOutputWriter Output,
    IServiceProvider Services);
```

`Core/Commands/CommandContext.cs`. `ParsedArguments` exposes `T Get<T>(string name)`, `bool TryGet<T>(string name, out T value)`, `bool Has(string name)`. `Services` is an escape hatch; constructor injection is preferred.

### ICommandDispatcher (existing — extended)

Same interface signature (`Task DispatchAsync(ISession session, string input)`). Implementation gains: privilege gate (`IAuthorizationChecker`), parse step (`ICommandArgumentParser`), `CommandContext` construction, exception trap, `CommandExecutedEvent` publish. Constructor gains `IAuthorizationChecker`, `ICommandArgumentParser`, `IOutputWriterFactory`, `IEventBus`, `ILogger<CommandDispatcher>`. It does not know admin policy or output transport — it asks the injected abstractions.

**Layer classification (resolves spec-mode V1).** `CommandDispatcher` is the **runtime of the command-Initiator tier**, not a domain or core system, despite living under `Core/`. Per [`../architecture/01-layers.md`](../architecture/01-layers.md#initiators--entry-points) and [`../architecture/checklist.md`](../architecture/checklist.md) INV-5, Initiators and Handlers are the tiers permitted to publish events; systems are not. The dispatcher publishing `CommandExecutedEvent` is therefore correct by the architecture — it is the only component that observes every dispatch outcome (success / parse-fail / unauthorized / threw), so the event cannot be sourced anywhere else. This was flagged as a violation by the spec-mode review *against the pre-Initiators `01-layers.md`*; the architecture doc was the thing that was wrong (it never named the command tier) and has been corrected. No code change implied — the spec was right; the doc caught up.

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

public sealed record CommandArgumentSchema(IReadOnlyList<CommandArgument> Arguments);

public enum CommandArgumentKind { Token, RestOfLine, Quantified }

public sealed record CommandArgument(
    string Name,
    Type ClrType,                          // string, int, uint, Direction, ...
    CommandArgumentKind Kind,
    bool Required,
    string? HelpText,
    IArgumentResolver? Resolver = null);   // null this slice; future dynamic entity-name matching
```

`Core/Commands/CommandArgumentParser.cs`. Default impl: whitespace + double-quoted tokenization, fixed coercion table (`string`, `int`, `uint`, `Direction`), enum-prefix matching for any enum type. `Resolver` is a documented seam — future slices populate it for entity-name resolution; the parser checks for null and skips. Fluent schemas and varargs are out of scope.

### IAuthorizationRequirement / AdminRequirement / IAuthorizationChecker (new — core abstraction)

```csharp
public interface IAuthorizationRequirement { }

public sealed record AdminRequirement : IAuthorizationRequirement;

public interface IAuthorizationChecker
{
    bool IsSatisfied(IAuthorizationRequirement requirement, ISession session);
}
```

`Core/Commands/Authorization/`. The default `AuthorizationChecker` pattern-matches the requirement: `AdminRequirement` delegates to the existing `IAdminAuthorizer`. Future slices register new requirement types (guild-leader, zone-owner, faction-rank) and extend the checker without touching the dispatcher. This **replaces** slice 2's per-command `IsPrivileged` convention. `IAdminAuthorizer` itself is unchanged (still used by the checker and the future `admin-privilege-elevation` slice).

### IOutputMessage + minimal shapes + IOutputWriter (new — minimal seam; full framework is slice 4)

```csharp
public interface IOutputMessage { OutputCategory Category { get; } }

public sealed record PlainMessage(string Text, OutputSeverity Severity) : IOutputMessage; // System | Error | Confirmation | Chat
public sealed record HelpIndexMessage(IReadOnlyList<HelpIndexEntry> Entries) : IOutputMessage;
public sealed record HelpEntryMessage(string Verb, string LongDescription, string Usage) : IOutputMessage;

public interface IOutputWriter { Task WriteAsync(IOutputMessage message); }
public interface IOutputWriterFactory { IOutputWriter Create(ISession session); }
```

`Core/Output/`. Slice-3 `OutputWriter` stringifies and awaits `session.SendLineAsync`. **Stubbed for slice 4 (named here so the slice-4 planner has an exact owed list):** `IOutputFormatter`, `TelnetOutputFormatter`, color/ANSI, `RoomDescriptionMessage`, `MovementMessage`, reserved `CombatMessage`/`PlayerInformationMessage`, `ISession.SupportsColor`, broadcast expansion, SignalR seam. Slice 3 does **not** ship any of these.

### HelpCommand / CommandsCommand (new — `Core/Modules/Help/Commands/`)

`HelpCommand`: `Name = "help"`, `Aliases = ["?"]`, Category `Player`, empty `RequiredPrivileges`, optional `<verb>` `Token` string arg. Depends on `IEnumerable<ICommand>` (DI-collected) and `IAuthorizationChecker` for visibility filtering. `CommandsCommand`: `Name = "commands"`, terser sibling, same dependencies; shares a private formatting helper. Composed via `AddHelpModule(IServiceCollection)` in `Core/Modules/Help/HelpModule.cs`.

### CommandLoggingHandler (new — `Core/Handlers/CommandLoggingHandler.cs`)

**Subscribes:** `CommandExecutedEvent`. **Priority:** 80 (`HandlerPriority.Notification`). Writes one structured-log line per command via `ILogger<CommandLoggingHandler>` with stable event name `CommandExecuted`. Log-only, never throws. Deliberately separate from `AdminAuditHandler` (which keeps the compliance-grade slice-2 admin events).

### AdminAuditHandler / PersistenceHandler (existing — unchanged)

`AdminAuditHandler` still subscribes to the four slice-2 admin events; not `CommandExecutedEvent`. `PersistenceHandler` still subscribes to `EntitySpawnedByAdminEvent` / `RoomExitAuthoredByAdminEvent`; unaffected.

---

## Content Tooling Impact

Pure infrastructure. **No new gameplay state, no new authored data files, no new `TemplateRegistry` entries, no new admin commands beyond `help` / `commands` (tooling for the framework itself, not for content).**

- The slice-2 admin commands keep their verbs, effects, and events; bodies refactor onto the framework. Their hand-written rejection-branch `"Usage: ..."` strings retire in favour of `help <verb>`.
- The `reload` long-form help wording required by slice 2 moves into `ReloadCommand.LongDescription`, emitted by `help reload`.
- `Admin:PrivilegedNames` and `Persistence:DataDirectory` keys unchanged. No new config keys this slice (`Output:DefaultColor` belongs to slice 4 — the slice that introduces color).

Per ground rule 8: this slice introduces no new gameplay state; the tooling it adds (`help`, `commands`) inspects the framework itself.

---

## Cross-cutting surfaces stressed

- **Commands** — *This IS the framework being built.* The slice is the structural promotion of the hand-rolled slice-2 command surface (per-command parsing, convention privilege, no help) to a typed `CommandContext` + declarative schema + structural authorization gate. Resolved by construction.
- **Output** — **Gap exposed.** Commands need a sink and `help` needs typed output, but the full output framework is a separate concern (slice 4). Disposition: slice 3 ships the **minimal seam** (`IOutputMessage`, `PlainMessage`, `HelpIndexMessage`, `HelpEntryMessage`, stringify-and-forward `IOutputWriter`/`IOutputWriterFactory`); slice 4 ([`output-framework.md`](output-framework.md)) lands the formatter, color, full shape catalog, and broadcast expansion and replaces the writer impl. The split is explicit and the owed stub list is enumerated above so the slice-4 planner has a precise contract. **Framework slice lands alongside (slice 4, immediately following).** Not absorbed silently; not acknowledged debt.
- **Event bus** — **Adequate.** `CommandExecutedEvent` reuses the existing `IEventBus` publish + priority-ordered handler model; `CommandLoggingHandler` is a conventional `HandlerPriority.Notification` subscriber. No new bus machinery.
- **Sessions** — **Extends.** `CommandContext` wraps the existing `ISession` and adds an `InvokerEntityId` + `IOutputWriter` around it. No change to `ISession` itself this slice (`SupportsColor` is slice 4).
- **Persistence** — **Adequate.** No new `[Persistent]` components; dirty-marking and flush paths unaffected. Slice-2 commands' persistence side effects are byte-for-byte unchanged.
- **Configuration** — **Adequate.** No new config keys. Authorization policy is sourced via the existing `IAdminAuthorizer` (which already reads `Admin:PrivilegedNames`).
- **Broadcast** — **Adequate (this slice).** `look` keeps calling `IBroadcastSystem` with its slice-2 body untouched; broadcast expansion (audience filter, system-wide, channels) is explicitly slice 4.

---

## Flows introduced or modified

- **Replaces Flow 3 — Player command lifecycle** ([`../architecture/06-flows.md`](../architecture/06-flows.md)). The current Flow 3 is marked "slice 3 will replace this flow." Slice 3's PR rewrites Flow 3 to the framework-driven trace: input → verb lookup → **authorization gate (`IAuthorizationChecker` over `RequiredPrivileges`)** → **argument parse (`ICommandArgumentParser` over `ArgumentSchema`)** → `CommandContext` construction → `ExecuteAsync(context)` → output via the minimal `IOutputWriter` (stringify-and-forward) → exception trap → `CommandExecutedEvent` publish → priority-ordered handlers (`CommandLoggingHandler` at 80). The "What's hand-rolled today" subsection is deleted; replaced with the structural guarantees. The mermaid diagram is re-drawn. Slice 4 will modify the output leg of this same flow again.
- **No new canonical flow introduced.** The command lifecycle is the replacement of an existing flow, not a new recurring chain. (Output rendering becomes its own flow in slice 4.)
- **Flow 5 (`reload`) — mermaid re-draw + prose, not prose-only.** `ReloadCommand`'s in-body `IsPrivileged` call moves to the dispatcher gate. Flow 5's current mermaid diagram has an explicit `RC->>Auth: IsPrivileged` participant interaction and an `alt not privileged` branch *inside the command*; those must be **removed and re-drawn** so the gate sits in the dispatcher before `ReloadCommand` is invoked — a prose-only edit would leave the diagram contradicting the code (spec-mode S2/D5). The reload mechanics (steps 3–10) are unchanged. The slice-3 PR re-draws the Flow 5 diagram and rewrites its step 2.

---

## Design Notes

- **`CommandContext` replaces `(ISession, string)` outright.** No shim. Cost: every command moves in this PR (12 known files). Benefit: one shape, no risk of new commands sneaking in against the old interface.
- **Privilege gate is structural and extensible.** `RequiredPrivileges` is a required interface member with no default; the compiler forces a choice (empty list = explicit "public"). The dispatcher is decoupled from policy via `IAuthorizationChecker`. Future requirement types register without dispatcher edits — this is the forward-compatible replacement for slice 2's convention.
- **Argument schema is a declarative POCO list with a `Kind` discriminator.** `Token` / `RestOfLine` / `Quantified` cover the 12 commands. `Resolver` is a null seam today; future slices populate it for dynamic entity-name matching. Enum-prefix matching ships day one (`n`/`no`/`nor` → `north`). Verb-prefix matching (`d` → `dig`) and entity-name matching are explicitly deferred.
- **`CommandExecutedEvent` fires for every dispatch.** Including parse-fail and unauthorized — both useful operationally. Not consumed by `AdminAuditHandler` (the slice-2 events carry richer payloads). Opt-out is log-level filtering, not event filtering.
- **`help` dogfoods typed output.** `HelpIndexMessage` / `HelpEntryMessage` are real `IOutputMessage` shapes even though the slice-3 writer only stringifies them. This sets the documentation-during-development bar and gives slice 4 a real consumer to render with color on day one.
- **Minimal output seam is deliberately incomplete.** The owed-to-slice-4 list is enumerated in the `IOutputMessage` section above so the split is auditable. Slice 4 replaces the writer impl, not the interface.
- **No exception leaks.** The dispatcher wraps `ExecuteAsync` in try/catch; uncaught exceptions log a stack trace at `Error` and emit `PlainMessage("Something went wrong. The error has been logged.", Error)`. `CommandOutcome.Threw` flags the event.
- **Sanctioned by the Initiators tier (resolves spec-mode V2).** The six commands that publish events (`SayCommand`, `MoveCommand`, the four admin commands) are *Initiators*, not systems — INV-5/INV-8 explicitly permit them to publish. The spec preserving "slice-2 events still published unchanged" is correct, not a carried-forward violation; the pre-Initiators `01-layers.md` simply had no tier for commands. Recorded here so it is not re-flagged.
- **`CommandDispatcher` dependency count is a known smell (spec-mode S1), deferred.** Five injected dependencies plus authorization/parsing/output/publish/exception-trap responsibilities trend toward a god-class. A `CommandPipeline` middleware chain would isolate these. Deferred deliberately: introducing a middleware framework inside slice 3 would balloon scope and risk the 12-command refactor. Tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md); revisit if a sixth concern is added to the dispatcher.
- **Docs to author / update alongside the implementation slice (not this planning step).**
  - `docs/architecture/06-commands.md` (new — command framework design)
  - `docs/reference/commands.md` (new — living command catalog, parallel to `systems.md`/`handlers.md`)
  - `docs/architecture/00-overview.md` (link the two new docs)
  - `CLAUDE.md` ground rules — every command lives in the framework (arg parsing, help, and privilege are not hand-rolled)
  - `docs/architecture/06-flows.md` — rewrite Flow 3 (body **and** re-drawn mermaid); re-draw Flow 5's mermaid and rewrite its step 2 (the privilege participant moves out of the command into the dispatcher gate — diagram change, not prose-only)
  - `docs/reference/handlers.md` — add `CommandLoggingHandler` (priority 80, subscribes `CommandExecutedEvent`); retire the legacy `CommandHandler` / `CommandReceivedEvent` row that the new dispatcher pipeline supersedes (INV-16; spec-mode review SR-4)
  - `.claude/skills/add-command/SKILL.md` — **rewrite the skill against the new `ICommand` shape.** The current skill shows a `void Execute(uint playerId, string args)` shape with direct `_bus.Publish` calls and no `CommandContext`/`RequiredPrivileges`/`ArgumentSchema`. A developer following the skill as-written would author a command that won't compile against the slice-3 interface. The rewrite must show the new shape (`Task ExecuteAsync(CommandContext)`, declarative `ArgumentSchema`, `RequiredPrivileges`, typed-message output via `context.Output`) and point at `docs/architecture/06-commands.md` for full detail (spec-mode review SR-3).
- **Out of scope for this slice.** All output formatting/color (slice 4). Broadcast expansion (slice 4). Tab completion. Verb-prefix matching. Entity-name argument resolution. Output streaming/paging. Prompt customization. i18n templates.

---

## Open Questions

None. The seven planning-round questions were resolved by the user and are baked in. The broadcast model and color-DSL syntax are slice-4 concerns (see [`output-framework.md`](output-framework.md)).

## Spec-review provenance

This spec passed a **spec-mode `architecture-reviewer`** pass (the first run of the new pre-implementation gate). Findings and disposition:

- **V1** (dispatcher publishes `CommandExecutedEvent`) — *root cause: the architecture doc, not the spec.* `01-layers.md` had no tier for commands, so "only handlers publish" read as if it banned the dispatcher. Resolved by adding the **Initiators** tier to `01-layers.md` and `checklist.md` (INV-5/INV-8–11). Spec unchanged; classification recorded in the `ICommandDispatcher` section.
- **V2** (six commands publish directly) — same root cause; sanctioned by INV-5/INV-8. Recorded in Design Notes; spec unchanged.
- **V3** (`CommandDispatcher.cs:43` survives the refactor) — *real spec gap.* Fixed: the dispatcher's own output call sites are now an explicit postcondition + Main-Flow step 8 checklist item.
- **S1** (dispatcher god-class) — accepted for slice 3, middleware refactor deferred to backlog.
- **S2/D3/D5** (Flow 3/5 mermaid re-draw) — spec wording corrected to require diagram re-draw, not prose-only.
- **S4** (arg-log PII) — promoted from vague "may" to a tracked acknowledged-debt backlog item gating non-local logging.
- **S3** (command layer ambiguity) — was the root cause of V1/V2; resolved by the Initiators tier.

This section is the audit trail for why the spec is shaped as it is; it is not an open-items list.

---

## Related

- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; established the four admin commands, `IAdminAuthorizer`, and the convention-only privilege check this slice replaces with a structural gate. Defined the `reload` help-text wording surfaced here via `help reload`.
- [`output-framework.md`](output-framework.md) — slice 4; consumes the minimal output seam shaped here and replaces the stringify-and-forward writer with the formatter-backed implementation. The owed stub list in this doc is its input contract.
- [`persistence-substrate.md`](persistence-substrate.md) — slice 1; precedent for a pure-infrastructure slice with no gameplay-visible behaviour change.
- [`admin-privilege-elevation.md`](admin-privilege-elevation.md) — deferred; future `grant`/`revoke` will register new `IAuthorizationRequirement` types against this slice's checker rather than be retrofitted.
- `account-character-creation.md` — slice 5 (renumbered; was slice 3). Its login-prompt verbs and character-management commands are authored against the framework landed here, not the legacy `(ISession, string)` shape.

**Roadmap impact:** this is **Phase 3 slice 3**. Account / character creation becomes slice 5; downstream slices shift +2. The plan, use-case index, and `06-flows.md` are updated in this slice's PR (the slice-numbering question was resolved: "slice 3 and shift"). For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
