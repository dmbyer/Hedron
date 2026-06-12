# Use Case: Command Prefix Matching

**Status:** implemented
**Actors:** Player, Administrator, System
**Module:** `Core/Commands/` (infrastructure enhancement to the command-framework slice)

---

## Description

Extends the slice-3 command framework with dynamic prefix resolution for verb lookup. Typing `lo` resolves to `look`; `dr` resolves to `drop`. Each command declares a `MatchingMode` (`Partial` or `Full`): player commands default to `Partial` (prefix resolution enabled); admin commands default to `Full` (exact match required, because misfiring `dig`/`reload` is costly). Static aliases on `ICommand.Aliases` stay at the exact-match tier and are consulted before prefix resolution — so `d` continues to route to `down`, not into a `dig`/`drop`/`down` ambiguity pool. The `IArgumentResolver` seam (null in slice 3) gets its concrete interface + parser wiring here; concrete resolver implementations are deferred to slice 6. `HelpCommand` is extended to display each command's declared aliases.

---

## Preconditions

- Phase 3 slices 1–3 merged: the slice-3 `ICommand` shape (`MatchingMode` is **added by this slice**), `ICommandDispatcher`, `CommandDispatcher`, `ICommandArgumentParser`, `CommandArgumentSchema`, `IArgumentResolver` (null stub), and the 14 commands (12 slice-2 + `help` + `commands`) exist.
- `CommandDispatcher._byVerb` is built at construction from exact name + alias entries (case-insensitive).
- No existing player command verb is ambiguous under its own prefix; static aliases may cover short prefixes (`d` = `down`) — these keep working because exact-match runs before prefix resolution.

---

## Postconditions

- `ICommand` gains a required `MatchingMode` property (`Partial` | `Full`); player commands default `Partial`, admin `Full`; compile-enforced choice.
- `CommandDispatcher` performs a two-phase verb lookup: (1) exact match (name + aliases); (2) on exact-miss, prefix resolution across `Partial`-mode commands whose `Name` starts with the typed verb — unique match dispatches; multiple → an `Ambiguous` disambiguation error listing **all** matches (never truncated); zero → unknown-command error.
- `CommandArgumentParser` gains the wiring to invoke `IArgumentResolver` for `string`-typed `Token` arguments whose command declares a resolver, applying prefix matching against the candidate list. Concrete resolvers are **deferred to slice 6** — interface + call-site only. Enum arguments are unchanged.
- An `IVerbRegistry` interface is introduced (implemented by `CommandDispatcher`) exposing a read-only view of registered commands for prefix resolution and `help`/`commands` display.
- `help <prefix>` participates in prefix resolution (help displays the command that would dispatch; lists all matches if ambiguous).
- `HelpCommand` and `commands` display each command's declared `Aliases` (first time aliases are surfaced to players).
- `CommandLoggingHandler` records the *resolved* canonical verb, not the raw typed prefix.
- All 14 commands compile against the new interface with the appropriate default mode. No gameplay semantics change.

---

## Main Flow

1. **Input arrives.** `CommandDispatcher.DispatchAsync(session, input)` splits verb + raw tail.
2. **Phase 1: exact lookup.** `_byVerb.TryGetValue(verb)` — includes names and static aliases. Found → privilege gate (unchanged). `d` → `down` resolves here; prefix resolution never reached.
3. **Phase 2: prefix resolution.** Collect commands where `MatchingMode == Partial` AND `Name.StartsWith(verb, OrdinalIgnoreCase)`; sort by `Name` (ordinal, case-insensitive). Zero → `"Unknown command…"` + `CommandExecutedEvent(ParseFailed)`. One → proceed as exact. Two+ → `"Ambiguous command '<verb>'. Did you mean: <all matches>?"` + `CommandExecutedEvent(ParseFailed)`. The disambiguation list is complete — never truncated.
4. **Privilege gate + argument parse.** Unchanged from slice 3. `CommandContext` is built with the *resolved* canonical `ICommand.Name`, so logging/audit are stable.
5. **Argument resolver wiring (if configured).** For each `Token` string arg with a non-null `IArgumentResolver`, the parser calls `GetCandidates(context)` and applies prefix matching (unique → canonical form; ambiguous → `ParseResult.Failure`; none → raw literal). No command registers a resolver until slice 6.
6. **Execute.** `command.ExecuteAsync(context)` runs as today.
7. **Logging.** `CommandLoggingHandler` receives `CommandExecutedEvent`; `Verb` carries the resolved canonical name (e.g. `look`), not the raw prefix (`lo`).

---

## Events Fired

| Event | Publisher | Change |
|---|---|---|
| `CommandExecutedEvent` (existing) | `CommandDispatcher` | `Verb` now carries the resolved canonical verb, not the raw typed input. Payload shape otherwise unchanged. |

No new events — prefix resolution is a synchronous in-dispatcher decision.

---

## Design Notes

- **Aliases win before prefix resolution.** Static aliases are exact entries in `_byVerb`, never visible to the prefix loop — `d` → `down` is guaranteed even with `dig`/`drop` present. No special-casing.
- **Alphabetically first, not "most popular".** Deterministic and stable as commands are added; muscle memory matters in a MUD. (Used only after mode filtering; ambiguity still errors.)
- **Ambiguous prefixes are errors, not silent guesses.** A clear disambiguation message teaches the minimum distinguishing prefix — matches DikuMUD/ROM/SMAUG convention. The list is complete (no truncation).
- **`Full` mode for admin commands is a safety default, not a hard constraint.** A future admin command can opt into `Partial`; the failure mode for mistaken `Full` (type more) is far cheaper than mistaken `Partial` (firing a destructive verb from an accidental prefix).
- **`IVerbRegistry` is also the tab-completion seam** — extracted now so a future tab-completion slice consumes it without touching dispatcher internals. `help <prefix>` reuses it so help and dispatch agree with no duplicated matching code.
- **`CommandArgumentResolverContext` is minimal** — `ISession`, `InvokerEntityId`, `IServiceProvider`; it runs inside the parser before `CommandContext` exists, so resolvers fetch room/inventory context via `IServiceProvider`.
- **`CommandExecutedEvent` schema unchanged** — only the *meaning* of `Verb` shifts from raw-typed to resolved-canonical (a strict improvement for log consumers; no existing queries break).
- **Out of scope.** Tab-completion, fuzzy/edit-distance matching, concrete `IArgumentResolver` implementations (slice 6), per-session matching-mode override, locale-sensitive sort.

---

## Related

- [`command-framework.md`](command-framework.md) — slice 3; introduced the framework and explicitly deferred verb-prefix matching, which this slice discharges.
- [`output-framework.md`](output-framework.md) — slice 4; disambiguation messages use its `PlainMessage`/`IOutputWriter` path.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — slice 2; established the four admin commands this slice assigns `Full` mode.
- [`../architecture/subsystems/commands.md`](../architecture/subsystems/commands.md) — the command framework design reference (two-phase lookup, `IVerbRegistry`).

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
