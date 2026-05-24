# Command Framework

> Introduced in Phase 3 slice 3. Authoritative implementation: `Core/Commands/`. See also [Flow 3](../flows/README.md#flow-3--player-command-lifecycle) for the runtime call chain.

---

## Overview

Every player-facing verb is an `ICommand` implementation. The command framework provides:

1. **Declarative argument parsing** — each command declares its argument schema; `ICommandArgumentParser` handles tokenization, type coercion, and enum-prefix matching.
2. **Structural privilege gate** — commands declare `RequiredPrivileges`; the dispatcher calls `IAuthorizationChecker` for each requirement before invoking the command body. Forgetting a gate is impossible — `RequiredPrivileges` is a required interface member.
3. **Typed output** — output goes through `IOutputWriter.WriteAsync(IOutputMessage)`, never `session.SendLineAsync`. Slice 4 replaces the writer implementation with a formatter-backed one.
4. **Universal audit** — `CommandExecutedEvent` is published for every dispatch outcome (success, parse-fail, unauthorized, threw). `CommandLoggingHandler` writes one structured-log line per command.
5. **Built-in help** — `help` and `commands` enumerate DI-collected `IEnumerable<ICommand>` with visibility filtering. Every command's `LongDescription` and `Usage` are surfaced automatically.

---

## ICommand shape

```csharp
public interface ICommand
{
    string Name { get; }
    IReadOnlyList<string> Aliases { get; }
    CommandCategory Category { get; }              // Player | Admin | System
    string ShortDescription { get; }               // one-liner for 'commands'
    string LongDescription { get; }                // multi-line body for 'help <verb>'
    string Usage { get; }                          // formal grammar
    IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } // empty = public
    CommandArgumentSchema ArgumentSchema { get; }
    CommandMatchingMode MatchingMode { get; }       // Partial (player default) | Full (admin default)
    Task ExecuteAsync(CommandContext context);
}
```

`CommandMatchingMode` controls how the dispatcher resolves the command name:

- **`Partial`** — prefix resolution enabled; the shortest unambiguous prefix dispatches this command (`lo` → `look`). Use for player commands.
- **`Full`** — exact match (or declared alias) required; prefix resolution is skipped. Use for admin commands where misfiring a prefix match is dangerous.

**Resolution rules** (in priority order):
1. **Exact match** — the typed verb matches a primary name or declared alias in the verb map. Always checked first; static aliases like `d` → `down` are resolved here, never in the prefix pool.
2. **Prefix resolution** — only runs if step 1 misses. Collects all `Partial`-mode commands whose `Name` starts with the typed verb; sorts alphabetically; dispatches if exactly one match. Two or more → disambiguation error listing **all** matching names. Zero → unknown-command error.

`IVerbRegistry` (implemented by `CommandDispatcher`) exposes the read-only command namespace:

```csharp
public interface IVerbRegistry
{
    IReadOnlyCollection<ICommand> AllCommands { get; }
    bool TryGetExact(string verb, out ICommand? command);
}
```

`HelpCommand` uses `IVerbRegistry` so that `help lo` resolves to `look` identically to how dispatch would resolve it — no duplicated matching logic.

`CommandContext`:

```csharp
public sealed record CommandContext(
    ISession Session,
    uint InvokerEntityId,
    ParsedArguments Args,
    IOutputWriter Output,
    IServiceProvider Services);
```

Read typed args: `context.Args.Get<Direction>("direction")`. Write output: `context.Output.WriteAsync(new PlainMessage(...))`. Publish events via the `IEventBus` injected into the command constructor. `Services` is an escape hatch — prefer constructor injection.

---

## Argument schema

```csharp
public CommandArgumentSchema ArgumentSchema { get; } = new(new[]
{
    new CommandArgument("direction", typeof(Direction), CommandArgumentKind.Token,
        Required: true, "Direction to move."),
    new CommandArgument("message", typeof(string), CommandArgumentKind.RestOfLine,
        Required: true, "The text to say."),
});
```

`CommandArgumentKind` values:
- `Token` — one whitespace-delimited token (or double-quoted group)
- `RestOfLine` — everything from current position to end-of-line
- `Quantified` — leading count + token (deferred; not used in slice 3)

Type coercion supports `string`, `int`, `uint`, and any `enum`. Enum coercion uses prefix matching (`n` → `North`, `s` → `South`). No-arg commands use `CommandArgumentSchema.Empty`.

---

## Privilege gate

```csharp
// Public command — no gate
public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
    Array.Empty<IAuthorizationRequirement>();

// Admin command — structural gate
public IReadOnlyList<IAuthorizationRequirement> RequiredPrivileges { get; } =
    new IAuthorizationRequirement[] { new AdminRequirement() };
```

The dispatcher iterates `RequiredPrivileges` and calls `IAuthorizationChecker.IsSatisfied(req, session)` for each before invoking `ExecuteAsync`. Never put `IAdminAuthorizer.IsPrivileged` calls inside a command body — that is the pre-slice-3 convention this framework replaced.

Future requirement types (guild-leader, zone-owner, faction-rank) register new implementations of `IAuthorizationRequirement` and extend `AuthorizationChecker` without touching the dispatcher.

---

## Output

```csharp
// Plain text (error, confirmation, system message)
await context.Output.WriteAsync(new PlainMessage("Spawned entity #42.", OutputSeverity.Confirmation));

// Help display (used by HelpCommand / CommandsCommand — but commands can use these too)
await context.Output.WriteAsync(new HelpEntryMessage(verb, longDesc, usage));
await context.Output.WriteAsync(new HelpIndexMessage(entries));
```

`OutputSeverity` values: `System | Error | Confirmation | Chat`. Slice 4 will use these to apply color / ANSI formatting. For now the writer stringifies and forwards via `session.SendLineAsync`.

---

## File placement

```
Core/Modules/<Feature>/Commands/<X>Command.cs   # feature-owned
Core/Commands/<X>Command.cs                     # cross-cutting (look, who, etc.)
```

Use the `add-command` skill (`.claude/skills/add-command/SKILL.md`) for step-by-step guidance.

---

## Registration

Register in the feature module:

```csharp
services.AddSingleton<ICommand, MyCommand>();
```

The dispatcher resolves all `IEnumerable<ICommand>` at construction and builds the verb map. Duplicate verbs throw at startup.

---

## CommandExecutedEvent

```csharp
public record CommandExecutedEvent(
    uint InvokerEntityId,
    string Verb,
    string ArgsSummary,     // truncated at 200 chars; plaintext — see backlog for redaction
    CommandOutcome Outcome) : IEvent;
```

`CommandOutcome`: `Success | ParseFailed | Unauthorized | Threw`. Published by `CommandDispatcher` for every dispatch. `CommandLoggingHandler` (priority 80) writes one structured-log line per dispatch. `AdminAuditHandler` does **not** subscribe to this event — it uses the richer slice-2 admin events.

---

## Related

- [Flow 3](../flows/README.md#flow-3--player-command-lifecycle) — runtime call chain
- [`commands.md`](../../reference/commands.md) — living command catalog
- [`command-framework.md`](../../use-cases/command-framework.md) — slice 3 spec
- [`01-layers.md`](../01-layers.md) — Initiators tier (commands and dispatcher may publish events)
- [`checklist.md`](../checklist.md) — INV-8 through INV-11 govern the command tier
