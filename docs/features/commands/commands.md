# Commands

> How a player verb becomes an action — raw input through the dispatcher, prefix-resolved to a canonical `ICommand`, argument-parsed, authorization-checked, executed, and flushed to the session buffer. **Status:** live (slices 3, 3a).

## What it is

Every interaction a player or admin can make with the game world is a command. A player types `look`, `kill goblin`, or `cast mend`; the framework routes that string through a deterministic pipeline and invokes the right `ICommand` implementation. Commands are the **Initiator tier** — the thinnest possible layer between network input and the domain systems that compute game outcomes.

The framework has three cooperating pieces at the orchestration level:

- **`CommandDispatcher`** (`Core/Commands/`) — the central router. Parses verb from raw tail, runs the three-phase verb lookup (exact → prefix → ability-verb fallback), enforces the incapacitation gate and the structural privilege gate, invokes `ICommandArgumentParser`, builds `CommandContext`, calls `ICommand.ExecuteAsync`, publishes `CommandExecutedEvent`, and flushes the session buffer in a `finally` block.
- **`ICommandArgumentParser`** (`Core/Commands/`) — shared declarative parser. Single-pass tokenization; applies each argument's `Kind` (`Token` / `RestOfLine`) and coerces to the CLR type; invokes `IArgumentResolver` for entity-name prefix matching when configured.
- **`IVerbRegistry`** (implemented by `CommandDispatcher`) — read-only view of the registered command namespace, consumed by `HelpCommand` and the future tab-completion seam.

The full design — ICommand shape, verb resolution rules, argument schema, resolver model, privilege gate, incapacitation gate, audit event, `AbilityInvocationPipeline` — is the [command-framework design doc](command-framework.md).

## How it works

A line of input arrives on a session's read stream. `TelnetSession` calls `CommandDispatcher.DispatchAsync(session, input)`. The dispatcher creates an `IOutputWriter` for the session, wraps the entire dispatch in a `try/finally`, and runs the three-phase lookup:

1. **Exact** — primary name or declared alias in `_byVerb`. Static aliases (`d` → `down`) resolve here; the prefix pool is never reached.
2. **Prefix** — `Partial`-mode commands whose `Name.StartsWith(verb)`, sorted A–Z. Zero → unknown-command error; 2+ → ambiguity error listing all matches; 1 → dispatch.
3. **Ability verb fallback** — `IAbilityVerbResolver.TryResolve` for bare skill invocations (`kick`, `ki`) that matched no registered command.

After verb resolution: incapacitation gate → privilege gate → argument parse → `ExecuteAsync` → `CommandExecutedEvent`. The `finally` block calls `output.FlushAsync()`, draining the session buffer and appending one trailing `PromptMessage`.

Output from command bodies goes through `context.Output.WriteAsync(IOutputMessage)` — never `session.SendLineAsync` (INV-11). The [command journey](../../architecture/flows/flow-03-player-command-lifecycle.md) traces the full runtime call chain.

## Systems

| System | Role |
|---|---|
| [`command-framework.md`](command-framework.md) | Dispatcher pipeline, `ICommand` shape, verb resolution, argument schema, resolver model, privilege gate, incapacitation gate, audit event, `AbilityInvocationPipeline` |

## Surfaces

- **Command catalog** — see [`../../reference/commands.md`](../../reference/commands.md) for every registered `ICommand` with schema, aliases, `MatchingMode`, dependencies, and events.
- **`IVerbRegistry`** — `AllCommands`, `TryGetExact`. Consumed by `HelpCommand`; future tab-completion seam. See [`Core/Commands/IVerbRegistry.cs`](../../../Core/Commands/IVerbRegistry.cs).
- **`CommandExecutedEvent`** — published on every dispatch outcome (Success / ParseFailed / Unauthorized / Refused / Threw). `CommandLoggingHandler` (priority 80) writes one structured-log line per dispatch.
- **`help` / `commands`** — built-in discoverability. `HelpCommand` (alias `?`) and `CommandsCommand` enumerate DI-collected `IEnumerable<ICommand>` with visibility filtering; grouped by `CommandCategory`; admin commands hidden when authorization fails. `UsableWhileIncapacitated: true`.

## Flows

- [Command journey (input → dispatch → resolve args → execute → output)](../../architecture/flows/flow-03-player-command-lifecycle.md) — the full dispatch call chain from `TelnetSession.ReadLine` through `output.FlushAsync()`.

## Related

- [`../../architecture/01-layers.md`](../../architecture/01-layers.md) — Initiators tier; why commands and the dispatcher may publish events.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-8 (Initiators are thin), INV-9 (no direct handler calls), INV-11 (no `session.SendLineAsync` after slice 3).
- [`../../roadmap/completed/slice-3-command-framework.md`](../../roadmap/completed/slice-3-command-framework.md) · [`../../roadmap/completed/slice-3a-command-prefix-matching.md`](../../roadmap/completed/slice-3a-command-prefix-matching.md) — as-built history and design decisions.
- **Output** — [`../output/output-framework.md`](../output/output-framework.md) — the session buffer and formatter pipeline that `CommandDispatcher.FlushAsync()` drains; see the output journey for the full rendering trace.
- **Abilities** — [`../abilities/abilities.md`](../abilities/abilities.md) — Phase 3 ability-verb fallback and `AbilityInvocationPipeline`; `CastCommand` and bare skill verbs both route through the same pipeline.
