# Phase 3 slice 3 — Command framework (completed)

> Implemented and merged on `master` (PR #67). The full feature spec lives in [`../../implementation-plans/command-framework.md`](../../implementation-plans/command-framework.md). This file records the as-built state and any deviations from the spec.

## Outcome

The hand-rolled `(ISession, string)` command surface from slice 2 is gone. Every command now implements a first-class framework: declarative argument schemas, structural authorization, typed output, and a cross-cutting audit event. Slice 3a (command prefix matching) followed immediately and extended this framework.

## Shipped pieces

| Surface | Location |
|---|---|
| `CommandContext` (replaces `(ISession, string)`) | `Core/Commands/CommandContext.cs` |
| `ICommandArgumentParser` / `CommandArgumentParser` | `Core/Commands/CommandArgumentParser.cs` |
| `CommandArgumentSchema`, `CommandArgument`, `CommandArgumentKind` | `Core/Commands/` |
| `ParseResult` (`Success`/`Failure`), `ParsedArguments` | `Core/Commands/` |
| `IAuthorizationRequirement`, `AdminRequirement` | `Core/Commands/Authorization/` |
| `IAuthorizationChecker` / `AuthorizationChecker` | `Core/Commands/Authorization/` |
| `IOutputMessage`, `PlainMessage`, `HelpIndexMessage`, `HelpEntryMessage` | `Core/Output/` |
| `IOutputWriter` / `IOutputWriterFactory` / stringify-and-forward impl | `Core/Output/` |
| `CommandExecutedEvent` (`InvokerEntityId`, `Verb`, `ArgsSummary`, `CommandOutcome`) | `Core/Commands/Events/` |
| `CommandLoggingHandler` (priority 80, subscribes `CommandExecutedEvent`) | `Core/Handlers/CommandLoggingHandler.cs` |
| `HelpCommand` (`help`, alias `?`), `CommandsCommand` | `Core/Modules/Help/Commands/` |
| `HelpModule` (`AddHelpModule`) | `Core/Modules/Help/HelpModule.cs` |
| All 12 existing commands refactored onto `Task ExecuteAsync(CommandContext)` | `Core/Modules/*/Commands/` |
| `ICommandDispatcher` extended (privilege gate, parse, `CommandContext`, exception trap, event publish) | `Core/Commands/CommandDispatcher.cs` |
| Architecture: Initiators tier added to `01-layers.md` + `checklist.md` (INV-5/8–11) | `docs/architecture/` |
| `docs/architecture/06-commands.md` (new), `docs/reference/commands.md` (new) | `docs/` |
| `.claude/skills/add-command/SKILL.md` rewritten for new `ICommand` shape | `.claude/skills/add-command/` |

## Spec-review provenance

Passed spec-mode `architecture-reviewer` (2 rounds). Key findings resolved:
- **V1/V2** (commands and dispatcher publishing events) — root cause was a missing Initiators tier in `01-layers.md`; architecture corrected, not the spec.
- **V3** (`CommandDispatcher` internal output call sites) — real gap; added as explicit postcondition + Main-Flow step 8.
- **S2/D5** (Flow 3/5 mermaid re-draws required) — spec corrected to mandate diagram changes.
- **S4** (arg-log PII for `say`) — promoted to a tracked acknowledged-debt backlog item.

Passed code-mode `architecture-reviewer` before merge.

## Notable design points

- **No compatibility shim.** `ExecuteAsync(ISession, string)` is deleted; all 12 commands migrate in one PR.
- **Privilege is structural.** `RequiredPrivileges` is a required interface member (empty = public); the dispatcher enforces it via `IAuthorizationChecker`. Per-command `IsPrivileged` calls are removed.
- **Minimal output seam is deliberately incomplete.** Slice 4 replaces the stringify-and-forward writer with a formatter-backed impl. The owed stub list is enumerated in the use-case doc's `IOutputMessage` section.
- **`CommandDispatcher` god-class acknowledged.** A `CommandPipeline` middleware refactor is tracked in backlog; deferred to avoid ballooning slice 3 scope.
- **`CommandExecutedEvent` fires for every outcome** — Success, ParseFailed, Unauthorized, Threw.

## Deviations from the use-case doc

None. All spec-mode findings were resolved in the spec before implementation began.

## Follow-ups unlocked by this slice

- Slice 3a (command prefix matching) — extended `ICommand` with `MatchingMode`; shipped immediately after.
- Slice 4 (output framework) — replaces the stringify-and-forward `IOutputWriter` with formatter-backed impl.
- Slice 5 (account/character creation) — login-prompt verbs and character-management commands are authored against this framework.
- Admin privilege elevation (deferred) — adds `IAuthorizationRequirement` types against this slice's checker.
- `CommandPipeline` middleware refactor (backlog) — once a sixth concern is added to the dispatcher.
- Command-arg log redaction (backlog) — gating on any non-local logging sink.
