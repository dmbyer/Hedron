# Use Case: Command Prefix Matching

**Status:** implemented
**Actors:** Player, Administrator, System
**Module:** `Core/Commands/` (infrastructure enhancement to the command-framework slice)

---

## Description

Extends the slice-3 command framework with dynamic prefix resolution for verb lookup. A player typing `lo` resolves to `look`; typing `dr` resolves to `drop`. Each command declares a `MatchingMode` (`Partial` or `Full`): player commands default to `Partial` (prefix resolution enabled); admin commands default to `Full` (exact match required, because the cost of misfiring `dig` or `reload` is high). Static aliases registered on `ICommand.Aliases` remain in the verb map at the exact-match tier and are consulted before prefix resolution begins — the existing `d` alias on `MoveCommand` therefore continues to route to `down`, not to the `dig`/`drop`/`down` ambiguity pool. The `IArgumentResolver` seam already present on `CommandArgument` (null in slice 3) is given its concrete interface definition and parser wiring in this slice; concrete resolver implementations (entity-name lookup, inventory lookup, etc.) are deferred to slice 6. `HelpCommand` is extended to display each command's declared aliases so players can discover shorthand forms.

---

## Preconditions

- Phase 3 slices 1–3 have merged: the slice-3 `ICommand` shape (`Name`, `Aliases`, `MatchingMode` is **added by this slice**), `ICommandDispatcher`, `CommandDispatcher`, `ICommandArgumentParser`, `CommandArgumentSchema`, `IArgumentResolver` (null stub), and the 14 commands (12 original slice-2 commands + `help` + `commands`, both added by slice 3) (`look`, `say`, six `MoveCommand` instances, `spawn`, `teleport`/`tp`, `dig`, `reload`, `help`, `commands`) all exist.
- `CommandDispatcher._byVerb` is built at construction time from exact name + alias entries (a `Dictionary<string, ICommand>` with case-insensitive comparer).
- `CommandArgumentParser.Coerce` already does enum-prefix matching; the `Resolver` field of `CommandArgument` is null.
- No existing player command verb is ambiguous under its own prefix (e.g. `look` is the only verb starting with `lo`). Static aliases may cover short prefixes (e.g. `d` = `down`) — these continue to work unchanged because exact-match lookup runs before prefix resolution.

---

## Postconditions

- `ICommand` gains a `MatchingMode` property (`CommandMatchingMode.Partial | Full`). Player commands default to `Partial`; admin commands default to `Full`. The property is a required interface member with no default; compile-enforced choice.
- `CommandDispatcher` performs a two-phase verb lookup: (1) exact match (name + aliases in `_byVerb`) — if found, dispatch as today; (2) if no exact match found AND the session input is not an exact alias, run prefix resolution across all registered commands whose `MatchingMode == Partial`. Prefix resolution returns the unique command whose `Name` starts with the typed verb; alphabetically first if multiple match; `Ambiguous` if multiple remain after mode filtering. Ambiguous prefixes write a disambiguation error listing **all** matching command names — the list is never truncated and they do not silently pick one.
- `CommandArgumentParser` gains the wiring to invoke `IArgumentResolver` for `string`-typed `Token` arguments whose command declares a resolver, and applies prefix matching against the candidate list when the resolver returns non-null. Concrete resolver implementations (entity-name, inventory, room-exit lookup) are **deferred to slice 6**; this slice ships the interface and the parser call-site only. Enum arguments are unchanged (they already do prefix matching).
- An `IVerbRegistry` interface is introduced. `CommandDispatcher` implements it. It exposes a read-only view of registered command names for use by prefix resolution and `help`/`commands` display. This replaces the private `_byVerb` dictionary as the surface other components (tests, future tab-completion) can inspect without coupling to the full dispatcher.
- `help <prefix>` behaviour: if the exact verb is not found, prefix resolution is attempted on the help argument — the command that would have been dispatched is what help displays. If ambiguous, help lists all matching commands.
- `HelpCommand` displays each command's declared `Aliases` alongside its name and description. A command with no aliases renders nothing in that field. This is the only place aliases are currently surfaced to players; the `commands` list command is updated identically.
- The slice-3 `CommandLoggingHandler` records the *resolved* verb (the canonical `ICommand.Name`), not the raw typed prefix, so log lines are stable regardless of what the player typed.
- All 14 existing commands compile against the new interface with the appropriate default mode. No gameplay semantics change.

---

## Main Flow

1. **Input arrives.** `CommandDispatcher.DispatchAsync(session, input)` splits verb + raw tail as today.

2. **Phase 1: Exact lookup.** `_byVerb.TryGetValue(verb, out command)` — includes all exact names and static aliases. If found, proceed to the privilege gate (unchanged from slice 3). Static alias `d` → `down` resolves here; prefix resolution is never reached.

3. **Phase 2: Prefix resolution.** Collect all commands where `command.MatchingMode == Partial` AND `command.Name.StartsWith(verb, OrdinalIgnoreCase)`. Sort candidates by `Name` (ordinal, case-insensitive) — alphabetically first wins. Zero candidates: write `"Unknown command: <verb>. Type 'help' for a list."` and publish `CommandExecutedEvent(ParseFailed)`. Exactly one candidate: proceed as if exact match, using the candidate. Two or more candidates: write `"Ambiguous command '<verb>'. Did you mean: <all matching names, comma-separated>?"` and publish `CommandExecutedEvent(ParseFailed)`. The disambiguation list is **all** matches; nothing is omitted or truncated.

4. **Privilege gate and argument parse.** Unchanged from slice 3. `CommandContext` is constructed with the *resolved* `ICommand.Name` as the canonical verb (not the raw typed input), so logging and audit are always stable.

5. **Argument resolver wiring (if configured).** `CommandArgumentParser` contains the call-site: for each `Token`-kind string argument that has a non-null `IArgumentResolver`, it calls `resolver.GetCandidates(context)` and applies prefix matching. Unique match → substitute canonical form. Ambiguous → `ParseResult.Failure`. No match → fall through to raw literal. **No concrete resolver implementations ship in this slice** — the wiring is present and correct, but no command registers a non-null resolver until slice 6.

6. **Execute.** `command.ExecuteAsync(context)` runs as today.

7. **Logging.** `CommandLoggingHandler` receives `CommandExecutedEvent`; `Verb` field contains the resolved canonical name (e.g. `look`), not the raw prefix (`lo`). `ArgsSummary` contains the post-resolution argument values.

---

## Events Fired

| Event | Publisher | Change |
|---|---|---|
| `CommandExecutedEvent` (existing) | `CommandDispatcher` | `Verb` field now carries the resolved canonical verb, not the raw typed input. Payload shape is otherwise unchanged. |

No new events. Prefix resolution is a synchronous, in-dispatcher decision; it produces no state change that warrants a past-tense event.

---

## Systems / Handlers Involved

### ICommand (existing — extended)

New required property:

```
CommandMatchingMode MatchingMode { get; }
```

`CommandMatchingMode` is a new enum in `Core/Commands/`:

```
public enum CommandMatchingMode { Partial, Full }
```

Default convention (enforced by documentation and the `add-command` skill, not by the interface itself since C# requires explicit implementation): player commands return `Partial`; admin commands return `Full`.

### IVerbRegistry (new — `Core/Commands/IVerbRegistry.cs`)

Read-only view of the registered verb space, consumed by prefix resolution, `HelpCommand`, and future tab-completion:

```
public interface IVerbRegistry
{
    IReadOnlyCollection<ICommand> AllCommands { get; }
    bool TryGetExact(string verb, out ICommand? command);
}
```

`CommandDispatcher` implements `IVerbRegistry` (it already holds the verb map). Registered as `IVerbRegistry` in DI alongside `ICommandDispatcher`.

### CommandDispatcher (existing — modified)

- Constructor and `_byVerb` unchanged.
- `DispatchAsync` gains the two-phase lookup described in the main flow.
- Implements `IVerbRegistry`.
- No new constructor dependencies — prefix resolution is a pure in-memory set operation over the already-held command list.

### ICommandArgumentParser / CommandArgumentParser (existing — modified)

`CommandArgumentParser.Coerce` gains the call-site wiring: for `string`-typed `Token` arguments with a non-null `IArgumentResolver`, the resolver is invoked to get candidates, then prefix matching is applied. The resolver returns `IReadOnlyList<string>?` — null means "not applicable; pass through." The concrete interface definition replaces the null stub:

```
public interface IArgumentResolver
{
    /// Returns the candidate strings for prefix matching, or null if
    /// prefix matching does not apply for this invocation.
    IReadOnlyList<string>? GetCandidates(CommandArgumentResolverContext context);
}

public readonly record struct CommandArgumentResolverContext(
    ISession Session,
    uint InvokerEntityId,
    IServiceProvider Services);
```

`Core/Commands/IArgumentResolver.cs` — replaces the current empty stub.
`Core/Commands/CommandArgumentResolverContext.cs` — new value type.

**No concrete `IArgumentResolver` implementations are delivered in this slice.** No live command sets a non-null resolver. The entity-name and inventory resolver implementations land in slice 6 (items + inventory). The parser wiring is present and exercised by unit-testable stub scenarios.

### HelpCommand (existing — modified)

`help <verb>` falls through to prefix resolution using `IVerbRegistry` when exact lookup misses. Writes a disambiguation list (complete, no truncation) if the prefix is ambiguous. No change to `HelpCommand`'s DI dependencies — it already holds `IEnumerable<ICommand>`; it gains `IVerbRegistry` to delegate the two-phase lookup rather than duplicating it.

In addition, the help output for a command is extended to include the command's `ICommand.Aliases` list. When a player runs `help look` (for example), the rendered block now shows:

```
look  (aliases: l)
  Describes your current surroundings.
```

A command with no aliases omits the aliases line entirely. The `commands` verb-listing command receives the same treatment: each line shows the canonical name followed by any aliases in parentheses. This is the first time aliases are surfaced to the player; no new DI dependencies are required since `HelpCommand` already iterates `ICommand` instances.

---

## Content Tooling Impact

Pure infrastructure. No new authored data files, no new `TemplateRegistry` entries, no new admin commands.

The 14 existing commands each gain a `MatchingMode` declaration. By convention: the four admin commands (`spawn`, `teleport`/`tp`, `dig`, `reload`) declare `Full`; the remaining player commands declare `Partial`. The `add-command` skill (`../skills/add-command/SKILL.md`) must be updated to include `MatchingMode` as a required step, with the correct default shown for each `CommandCategory`.

No config keys are added. Matching mode is static (per-command declaration), not a runtime setting.

---

## Cross-cutting surfaces stressed

- **Commands** — **Adequate.** The slice-3 command framework is the surface being extended. Verb-prefix lookup is a pure modification to `CommandDispatcher._byVerb` lookup logic; the `ICommand` interface gains one required property. No hand-rolled patterns: the resolution logic is in one place (`CommandDispatcher`) and the test surface is `IVerbRegistry`.

- **Output** — **Adequate.** Disambiguation error and ambiguous-argument messages are `PlainMessage(OutputSeverity.Error)` values written via the existing `IOutputWriter`. No new output shape is needed.

- **Event bus** — **Adequate.** `CommandExecutedEvent` is reused unchanged (the `Verb` field meaning is clarified, not the schema changed). No new bus machinery.

- **Persistence** — **Adequate.** No `[Persistent]` components are added or changed. Matching mode is a static code declaration, not saved state.

- **Configuration** — **Adequate.** Matching mode is a static compile-time declaration on each command; no runtime config override is needed and the static approach is demonstrably sufficient. No config keys are added or consulted.

- **ECS queries** — **Adequate.** No entity queries in this slice.

- **Sessions** — **Adequate.** `CommandArgumentResolverContext` wraps `ISession` the same way `CommandContext` does. No change to `ISession`.

- **Content templates** — **Adequate.** No template schema changes.

- **`add-command` skill** — **Gap exposed — must update.** The `add-command` skill currently shows the post-slice-3 `ICommand` shape. Adding `MatchingMode` as a required property without updating the skill would cause every future command author to produce a non-compiling stub on the first attempt. **Resolution:** the skill update is in scope for this slice's PR. In addition, `docs/architecture/06-commands.md` contains the authoritative `ICommand` interface shape verbatim and must also be updated in the same PR to include the `MatchingMode` property. This is a documentation surface, not a framework surface, but the CLAUDE.md ground rule ("if the slice adds a new required interface member, the skill must reflect it") makes it a merge requirement.

- **`IArgumentResolver` seam** — **Gap discharged (interface + wiring only).** The seam was null in slice 3. This slice gives it a concrete interface, `CommandArgumentResolverContext`, and the parser call-site. The entity-name resolution scenario (finding the nearest orc by typing "orc" into a `get` command) is deferred — concrete resolver implementations land in slice 6. No command in this slice registers a non-null resolver, so the path is wired but not exercised against live data until slice 6.

---

## Flows introduced or modified

### Flow 3 — Player command lifecycle (extended)

The existing Flow 3 traces: input → verb lookup (exact only) → authorization → argument parse → execute → output → `CommandExecutedEvent`.

This slice modifies **step 2** (verb lookup) by adding the prefix-resolution phase after an exact-miss. The mermaid diagram must be updated to add the prefix-resolution branch. Specifically:

- The `alt verb unknown` branch becomes `alt verb exact-miss` with two sub-branches: prefix resolves uniquely (proceed), or zero/ambiguous (write error).
- The `Verb` field in the `CommandExecutedEvent` publish calls must note that the resolved canonical name is used.

In addition, the **step 8 prose** (the description of `CommandLoggingHandler` receiving `CommandExecutedEvent`) must be corrected in the same PR. Prior to this slice the prose states that `Verb` equals the raw typed input; after this slice `Verb` is the resolved canonical command name (e.g. `look` when the player typed `lo`). The step 8 prose must reflect this change.

No other flows are affected. The architecture-reviewer PR gate requires `06-flows.md` to be updated with the redrawn Flow 3 diagram and corrected step 8 prose before merge.

### No new canonical flow introduced.

Prefix resolution is an inline extension of the existing dispatch flow, not a new chain.

---

## Design Notes

- **Aliases win before prefix resolution.** Static aliases (e.g. `d` = `down`, `n` = `north`) are in `_byVerb` as exact entries and are never visible to prefix resolution. This is by construction — if `d` resolves via alias before the prefix loop runs, `d` → `down` is guaranteed even when `dig` and `drop` exist. No special-casing needed.

- **Alphabetically first, not "most popular".** The disambiguation rule is deterministic and stable as new commands are added. "Most recently used" or "most popular" would require state and would change behaviour across sessions — unacceptable for an MUD where players learn muscle memory. Alphabetical is predictable.

- **Ambiguous prefixes are errors, not silent guesses.** Silently picking a command from an ambiguous prefix would produce surprising action (a player typing `d` in a world without the `down` alias could suddenly `dig` or `drop`). A clear disambiguation message teaches the player the minimum required prefix. This matches classic MUD conventions (DikuMUD, ROM, SMAUG all produce "AMBIGUOUS COMMAND" lists).

- **`Full` mode for admin commands is a safety default, not a hard constraint.** A future admin command that the author explicitly wants prefix-accessible (e.g. a short `stat` inspection verb) can declare `Partial`. The point is that the failure mode for mistaken `Full` (typing more characters) is much lower-risk than mistaken `Partial` (firing a destructive admin command from an accidental prefix).

- **`IVerbRegistry` as a seam for tab-completion.** Tab-completion is not in scope for this slice, but it requires the same read-only view of the command namespace. By extracting `IVerbRegistry` now, the tab-completion slice (deferred) can consume it without touching `CommandDispatcher`'s internals.

- **`CommandArgumentResolverContext` is intentionally minimal.** It carries `ISession`, `InvokerEntityId`, and `IServiceProvider`. Future resolver implementations that need room contents or inventory lists obtain them via `IServiceProvider`. No dependency on `CommandContext` — the resolver runs inside the parser, which runs before the `CommandContext` is constructed.

- **`help <prefix>` reuses dispatcher resolution logic via `IVerbRegistry`.** `HelpCommand` gains `IVerbRegistry` as a dependency to perform the same two-phase lookup. This ensures help and dispatch agree on what verb a prefix resolves to, with no duplicated matching code.

- **No change to `CommandExecutedEvent` schema.** The `Verb` field already carries a `string`. Its *meaning* shifts from "raw typed verb" to "resolved canonical verb name", which is strictly an improvement for log consumers. This is a breaking semantic change for any log query that expected the raw typed prefix; since no such queries exist yet, it is an acceptable evolution. The `ArgsSummary` field carries post-resolution argument values; this was always the intent.

- **Aliases in help output.** `ICommand.Aliases` existed in slice 3 but was never shown to the player. This slice adds aliases to `HelpCommand` output and the `commands` listing. Players learn shorthands (e.g. `l` for `look`) through the help system rather than word of mouth. The formatting is minimal: `name  (aliases: a, b)` on the header line; commands with no aliases omit the parenthetical. No new data structure is required — `ICommand.Aliases` is already `IReadOnlyList<string>`.

- **Disambiguation list is complete.** When a prefix is ambiguous the full set of matching command names is written. Truncation (e.g. "did you mean: drop, dig, and 3 more?") is explicitly rejected — the complete list is what teaches the player the minimum distinguishing prefix.

- **Out of scope for this slice.** Tab-completion (needs session-level interrupt handling and partial-line buffering). Fuzzy/edit-distance matching (not MUD-conventional; deferred indefinitely). Concrete `IArgumentResolver` implementations (slice 6). Per-session matching-mode override (e.g. "always require full match for this player"). Locale-sensitive sort order (English-only MUD; not a priority).

---

## Open Questions

None. The following were resolved in planning:

- **What wins when a static alias collides with a prefix match?** Alias (exact match runs first). Established by the `d`/`down` example in the idea itself.
- **Ambiguous prefix → pick first or error?** Error with disambiguation list. Matches MUD convention; prevents silent misfire.
- **Admin commands default to `Full` or `Partial`?** `Full`. The idea specifies this explicitly.
- **Does `help <prefix>` participate in prefix resolution?** Yes, via `IVerbRegistry`, so help and dispatch agree.
- **Disambiguation list: truncated or complete?** Complete — all matching command names are shown, no truncation.
- **Argument resolver concrete implementations: this slice or later?** Interface + parser wiring land here; concrete resolver implementations (entity-name, inventory) are deferred to slice 6.
- **Are aliases surfaced to players?** Yes, in this slice — `HelpCommand` and `commands` display each command's declared aliases.

---

## Related

- [`command-framework.md`](command-framework.md) — slice 3; introduced `ICommand`, `CommandDispatcher`, `ICommandArgumentParser`, `IArgumentResolver` (null stub), and deferred verb-prefix matching explicitly ("Verb-prefix matching (`d` → `dig`) ... explicitly deferred"). This slice discharges that deferral.
- [`output-framework.md`](output-framework.md) — slice 4; disambiguation messages use the `PlainMessage`/`IOutputWriter` output path this slice reuses.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; established the four admin commands that this slice assigns `Full` matching mode.
- [`admin-privilege-elevation.md`](admin-privilege-elevation.md) — deferred; future `grant`/`revoke` admin commands would also declare `Full` matching mode as admin commands.
