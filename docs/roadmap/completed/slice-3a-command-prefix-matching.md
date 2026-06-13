# Phase 3 slice 3a — Command prefix matching (completed)

> Implemented and merged on `master` (PR #68). The full feature spec lives in [`../../implementation-plans/command-prefix-matching.md`](../../features/commands/commands.md). This file records the as-built state and any deviations from the spec.

## Outcome

Players can type partial verbs (`lo` → `look`, `dr` → `drop`) without static aliases. Admin commands are protected from prefix resolution by a `Full` matching mode. The `IVerbRegistry` seam decouples help output and future tab-completion from `CommandDispatcher` internals. The `IArgumentResolver` interface and parser wiring land here; concrete resolver implementations are deferred to slice 6.

This is labeled "slice 3a" in the roadmap; the PR commit message called it "Phase 3 slice 5" (a transient numbering inconsistency resolved in the roadmap retrospectively).

## Shipped pieces

| Surface | Location |
|---|---|
| `CommandMatchingMode` enum (`Partial` / `Full`) | `Core/Commands/` |
| `ICommand.MatchingMode` (required property added to interface) | `Core/Commands/ICommand.cs` |
| `IVerbRegistry` (read-only view of registered command names + prefix candidates) | `Core/Commands/IVerbRegistry.cs` |
| `CommandDispatcher` implements `IVerbRegistry`; two-phase verb lookup (exact → prefix) | `Core/Commands/CommandDispatcher.cs` |
| `IArgumentResolver` interface + parser call-site wiring (concrete impls deferred) | `Core/Commands/` |
| `HelpCommand` extended to surface aliases in `help`/`commands` output | `Core/Modules/Help/Commands/HelpCommand.cs` |
| All existing player commands set `MatchingMode = Partial`; admin commands set `Full` | `Core/Modules/*/Commands/` |

## Notable design points

- **Two-phase dispatch.** Exact match (name + aliases in `_byVerb`) runs first; prefix resolution only fires when no exact match is found and input is not itself an exact alias.
- **Static aliases always win.** `d` → `down` continues to work because the exact-match tier is consulted before prefix resolution, preventing the `dig`/`drop`/`down` ambiguity pool from interfering.
- **Ambiguous prefixes are explicit.** The full list of matching command names is written to the session; no silent first-match guessing.
- **Admin commands use `Full` mode.** The cost of misfiring `dig` or `reload` is high; prefix resolution is opt-in per command.
- **`IArgumentResolver` seam.** The interface and parser call-site ship here so the architecture is stable before concrete entity-name/inventory resolvers arrive in slice 6.

## Deviations from the use-case doc

None. Both spec-mode and code-mode `architecture-reviewer` passes completed before merge.

## Follow-ups unlocked by this slice

- Slice 6 (items + inventory) — provides the first concrete `IArgumentResolver` implementations (entity-name lookup, inventory lookup, room-exit lookup).
- Tab-completion (backlog) — can consume `IVerbRegistry.GetPrefixCandidates` without coupling to `CommandDispatcher` internals.
