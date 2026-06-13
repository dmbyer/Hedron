# Command Framework

> The dispatcher-backed pipeline that turns a raw input line into a typed `CommandContext`, enforces the structural privilege gate, resolves and parses arguments, invokes the matching `ICommand`, and publishes a `CommandExecutedEvent` for every outcome. **Authoring checkpoint:** slice 3 (command framework); slice 3a (prefix matching, `IVerbRegistry`, argument resolver wiring). Living document.

## What it is / does

The command framework sits **between the session transport and the domain layer**. A raw input line in → a resolved, authorized, argument-parsed command execution out. No command body ever calls `session.SendLineAsync` or branches on verb strings — INV-11.

```
TelnetSession.ReadLine()
    ↓  raw input string
CommandDispatcher.DispatchAsync(session, input)
    ↓  IOutputWriterFactory.Create(session)  [try { ... } finally { output.FlushAsync() }]
    ↓  trim + split → verb, rawTail
Phase 1: exact lookup (_byVerb by name + alias)
Phase 2: prefix resolution (Partial-mode commands, A–Z sort)
Phase 3: IAbilityVerbResolver.TryResolve (bare skill verbs, fallback only)
    ↓  IEntityStateService: incapacitation gate
    ↓  IAuthorizationChecker: RequiredPrivileges loop
    ↓  ICommandArgumentParser.Parse(schema, rawTail, resolverContext)
    ↓  ICommand.ExecuteAsync(CommandContext)
    ↓  IEventBus.Publish(CommandExecutedEvent)
    ↓  finally: output.FlushAsync() → session buffer drain + prompt
```

## How it works

### ICommand shape

Every player-facing verb is an `ICommand`. The interface requires: `Name`, `Aliases`, `Category` (`Player | Admin | System`), `ShortDescription`, `LongDescription`, `Usage`, `RequiredPrivileges` (empty = public), `ArgumentSchema`, `MatchingMode` (`Partial | Full`), and `Task ExecuteAsync(CommandContext)`.

See [`Core/Commands/ICommand.cs`](../../../Core/Commands/ICommand.cs).

`CommandContext` carries `ISession`, `InvokerEntityId`, `ParsedArguments`, `IOutputWriter`, and `IServiceProvider`. Read typed args via `context.Args.Get<T>(name)`; write output via `context.Output.WriteAsync(IOutputMessage)`. `Services` is an escape hatch — prefer constructor injection.

See [`Core/Commands/CommandContext.cs`](../../../Core/Commands/CommandContext.cs).

### Verb resolution (three-phase lookup)

| Phase | Condition | Action |
|---|---|---|
| 1 — Exact | Primary name or declared alias in `_byVerb` | Dispatch; static aliases (`d` → `down`) always win here |
| 2 — Prefix | Phase 1 missed; `MatchingMode == Partial`; `Name.StartsWith(verb)` | Zero → unknown-command error; 2+ → ambiguity error listing all matches; 1 → dispatch |
| 3 — Ability verb | Both phase 1 and 2 missed; `IAbilityVerbResolver.TryResolve` | Routes to `SkillInvocationCommand` (not an `ICommand`; not enumerable by `help`/`commands`) |

`CommandMatchingMode.Partial` — prefix resolution enabled; use for player commands. `CommandMatchingMode.Full` — exact match required; default for admin commands where misfiring a prefix is dangerous.

`IVerbRegistry` (implemented by `CommandDispatcher`) exposes the read-only command namespace for `HelpCommand` and future tab-completion without coupling to dispatcher internals.

See [`Core/Commands/IVerbRegistry.cs`](../../../Core/Commands/IVerbRegistry.cs) · [`Core/Commands/CommandDispatcher.cs`](../../../Core/Commands/CommandDispatcher.cs).

### Argument schema

Each command declares a `CommandArgumentSchema`: an ordered list of `CommandArgument(name, type, kind, required, description)`.

| `CommandArgumentKind` | Meaning |
|---|---|
| `Token` | One whitespace-delimited token (or double-quoted group) |
| `RestOfLine` | Everything from current position to end-of-line |
| `Quantified` | Leading count + token (deferred; not used in slice 3) |

Type coercion supports `string`, `int`, `uint`, and any `enum`. Enum coercion uses prefix matching (`n` → `North`). `Token string` arguments that declare a non-null `IArgumentResolver` have prefix matching applied against the candidate list — the interface and parser call-site ship in slice 3a; concrete implementations (`ItemInRoomResolver`, `ItemInInventoryResolver`) ship in slice 6. No-arg commands use `CommandArgumentSchema.Empty`.

See [`Core/Commands/CommandArgumentParser.cs`](../../../Core/Commands/CommandArgumentParser.cs) · [`Core/Commands/CommandArgumentSchema.cs`](../../../Core/Commands/CommandArgumentSchema.cs).

### Resolver model

`IArgumentResolver.GetCandidates(context)` returns `IReadOnlyList<ResolvedCandidate>?` where each `ResolvedCandidate(string MatchString, string CanonicalValue)` allows keyword aliases to map to a canonical value. The parser deduplicates by `CanonicalValue` after prefix matching so multiple keyword aliases for the same entity do not produce false ambiguity.

`CommandArgumentResolverContext` carries `ISession`, `InvokerEntityId`, `IServiceProvider`. It is constructed inside the parser before `CommandContext` exists, so resolvers fetch room/inventory context via `IServiceProvider`.

See [`Core/Commands/IArgumentResolver.cs`](../../../Core/Commands/IArgumentResolver.cs).

### Privilege gate

`RequiredPrivileges` is a required interface member; empty list = public. The dispatcher iterates the list and calls `IAuthorizationChecker.IsSatisfied(req, session)` for each before invoking `ExecuteAsync`. Never put authorization checks inside a command body. Slice 3 ships one requirement type, `AdminRequirement` (delegates to `IAdminAuthorizer`). Future requirement types register new `IAuthorizationRequirement` implementations without touching the dispatcher.

See [`Core/Commands/Authorization/IAuthorizationChecker.cs`](../../../Core/Commands/Authorization/IAuthorizationChecker.cs) · [`Core/Commands/Authorization/IAuthorizationRequirement.cs`](../../../Core/Commands/Authorization/IAuthorizationRequirement.cs).

### Incapacitation gate

After verb resolution, the dispatcher calls `IEntityStateService.IsInState(session.PlayerEntityId, Incapacitated)`. If the player is incapacitated and `command.UsableWhileIncapacitated` is `false` (the default), the dispatch short-circuits with a rejection message and publishes `CommandExecutedEvent(Refused)`. Commands explicitly opting in (`help`, `commands`, `score`) bypass this gate. Incapacitation is a transient entity state, not a privilege — this gate lives in the dispatcher, not in `IAuthorizationChecker`.

### Audit event

`CommandExecutedEvent(InvokerEntityId, Verb, ArgsSummary, CommandOutcome)` is published on every dispatch path — `Success | ParseFailed | Unauthorized | Refused | Threw`. The `Verb` field always carries the **resolved canonical name** (e.g. `look`), never the raw typed prefix (`lo`). `CommandLoggingHandler` (priority 80, [`Core/Handlers/CommandLoggingHandler.cs`](../../../Core/Handlers/CommandLoggingHandler.cs)) writes one structured-log line per dispatch.

**Known gap:** `ArgsSummary` is truncated at 200 chars with no redaction. Tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md) ("Command-arg log redaction").

See [`Core/Commands/Events/CommandExecutedEvent.cs`](../../../Core/Commands/Events/CommandExecutedEvent.cs) · [`Core/Commands/CommandOutcome.cs`](../../../Core/Commands/CommandOutcome.cs).

### AbilityInvocationPipeline

An **initiator-tier shared helper** called exclusively by `CastCommand` and `SkillInvocationCommand`. It handles target resolution (Self / explicit token / in-combat opponent / `"whom?"` prompt), combat entry for offensive abilities, `IAbilitySystem.Activate`, event publication, and `ICombatSystem.ResolveAbilityStrike` for offensive abilities. Holds no domain logic — all decisions are delegated to systems.

See [`Core/Modules/Abilities/Commands/AbilityInvocationPipeline.cs`](../../../Core/Modules/Abilities/Commands/AbilityInvocationPipeline.cs).

## Dispatch table

| Step | Actor | Outcome path |
|---|---|---|
| Trim + split verb / rawTail | `CommandDispatcher` | Always |
| Phase 1 exact + Phase 2 prefix + Phase 3 ability verb | `CommandDispatcher` / `IAbilityVerbResolver` | On miss: unknown-command or ambiguity error → `CommandExecutedEvent(ParseFailed)` |
| Incapacitation gate | `IEntityStateService` | On blocked: rejection → `CommandExecutedEvent(Refused)` |
| Privilege gate | `IAuthorizationChecker` | On fail: rejection → `CommandExecutedEvent(Unauthorized)` |
| Argument parse | `ICommandArgumentParser` | On fail: reason + help hint → `CommandExecutedEvent(ParseFailed)` |
| Execute | `ICommand.ExecuteAsync(CommandContext)` | On exception: generic error logged → `CommandExecutedEvent(Threw)` |
| Flush | `output.FlushAsync()` (finally) | Always — drains buffer + appends one `PromptMessage` |

## File placement and registration

```
Core/Modules/<Feature>/Commands/<X>Command.cs   # feature-owned
Core/Commands/<X>Command.cs                     # cross-cutting (look, who, etc.)
```

```csharp
services.AddSingleton<ICommand, MyCommand>();
```

The dispatcher resolves all `IEnumerable<ICommand>` at construction and builds the verb map. Duplicate verbs throw at startup. Use the `add-command` skill (`.claude/skills/add-command/SKILL.md`) for step-by-step guidance.

## Related

- [`commands.md`](commands.md) — holistic feature view and player-facing surfaces.
- [`../../architecture/flows/flow-03-player-command-lifecycle.md`](../../architecture/flows/flow-03-player-command-lifecycle.md) — command journey: input → dispatch → resolve args → execute → output.
- [`../../reference/commands.md`](../../reference/commands.md) — living command catalog.
- [`../../reference/systems.md`](../../reference/systems.md) — dispatcher / parser / resolver catalog rows.
- [`../../architecture/01-layers.md`](../../architecture/01-layers.md) — Initiators tier; why commands and the dispatcher may publish events.
- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-8 through INV-11 govern the command tier.
- [`../../roadmap/completed/slice-3-command-framework.md`](../../roadmap/completed/slice-3-command-framework.md) · [`../../roadmap/completed/slice-3a-command-prefix-matching.md`](../../roadmap/completed/slice-3a-command-prefix-matching.md) — as-built history and design decisions.
