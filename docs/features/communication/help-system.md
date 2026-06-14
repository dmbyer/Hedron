# Help System

> The `help` and `commands` surface: verb lookup, category-grouped index, ability fallback, and the `HelpIndexMessage`/`HelpEntryMessage` output shapes. **Authoring checkpoint:** shipped with the Help module (no dedicated slice plan). Living document.

## What it is / does

The Help module exposes two player commands backed directly by the registered command set and the ability registry — there is no `IHelpSystem` or stored help-entry data structure. Help is a read-only view over what already exists in DI; the "registry" is `IEnumerable<ICommand>` and `IAbilityRegistry`.

The module lives at `Core/Modules/Help/` and is composed by `HelpModule.AddHelpModule`. Because `HelpCommand` is itself an `ICommand`, and `IVerbRegistry` is implemented by `CommandDispatcher` which depends on all `ICommand` registrations, both dependencies are taken as `Lazy<T>` to break the circular dependency.

## How it works

### `HelpCommand` — verb lookup + ability fallback

**No argument** (`help`): builds a filtered, category-ordered list of `HelpIndexEntry` records from `IEnumerable<ICommand>` — visibility-gated via `IAuthorizationChecker` so admin commands are hidden from players — and writes `HelpIndexMessage(entries)`.

**With a verb argument** (`help <verb>`): two-phase resolution mirrors `CommandDispatcher`:
1. Exact match against `IVerbRegistry.TryGetExact` (covers primary verb and aliases).
2. Prefix resolution via `IVerbRegistry.GetPrefixCandidates`: zero matches → falls through to ability registry; one match → shows that command; multiple matches → filters to visible, disambiguates or prompts.

Once a command is resolved, the visibility gate is re-applied (prevents revealing admin commands to players via prefix), then `HelpEntryMessage(verb, longDescription, usage, aliases)` is written.

**Ability fallback**: when no command matches (or the matched command is not visible), `HelpCommand` queries `IAbilityRegistry` by exact id then prefix-match across all registered ability ids and display names. A match writes a formatted ability help block (name, kind, activation, targeting, cost, cooldown, invocation form) as a `PlainMessage`. Ambiguous prefix → "No help found for '{topic}'".

**Special topics** — `skills`, `spells`, `abilities`: after writing the command entry, appends a global catalog of all registered abilities of the matching kind.

### `CommandsCommand` — terse index

Same visibility filtering and `HelpIndexMessage` output as the no-argument `help` path, but declared as a separate command with no verb argument and no ability fallback. Useful when the player wants only the command list without accidentally entering ability-help territory.

### Output shapes

The three shapes in `Core/Output/` carry structured data, not pre-rendered strings; the `IOutputFormatter` for each transport decides how to render them:

| Shape | Carries |
|---|---|
| `HelpIndexMessage` | `IReadOnlyList<HelpIndexEntry>` |
| `HelpIndexEntry` | `Verb`, `ShortDescription`, `CommandCategory`, `Aliases` |
| `HelpEntryMessage` | `Verb`, `LongDescription`, `Usage`, `IReadOnlyList<string> Aliases` |

## Interface

No `IHelpSystem` seam exists; the module's contract is the two commands and the output shapes:

- [`HelpCommand.cs`](../../../Core/Modules/Help/Commands/HelpCommand.cs) — full lookup: index, verb help, ability fallback, special topics.
- [`CommandsCommand.cs`](../../../Core/Modules/Help/Commands/CommandsCommand.cs) — terse index only.
- [`HelpModule.cs`](../../../Core/Modules/Help/HelpModule.cs) — DI composition; registers `Lazy<IEnumerable<ICommand>>` and `Lazy<IVerbRegistry>` to break circular dependencies; wires `IAbilityRegistry` into `HelpCommand`.
- Output shapes: [`HelpIndexMessage.cs`](../../../Core/Output/HelpIndexMessage.cs), [`HelpIndexEntry.cs`](../../../Core/Output/HelpIndexEntry.cs), [`HelpEntryMessage.cs`](../../../Core/Output/HelpEntryMessage.cs) — all in `Core/Output/`, `OutputCategory.Help`.

## Considerations

- **Circular dependency.** `HelpCommand` is an `ICommand`; `CommandDispatcher` (which implements `IVerbRegistry`) depends on all `ICommand` registrations. Both `Lazy<IEnumerable<ICommand>>` and `Lazy<IVerbRegistry>` are registered in `HelpModule` to break the cycle at DI construction time. `AddAbilitiesModule` must be called before `AddHelpModule` in `Program.cs` so `IAbilityRegistry` is available.
- **`UsableWhileIncapacitated: true`.** Both `help` and `commands` are safe from downed players and set the flag in `ICommand`.
- **Visibility gate.** Admin commands are hidden when `RequiredPrivileges` are unsatisfied; the gate is evaluated on both the full command list and each prefix candidate independently.
- **No persistence, no events.** Help is a pure read path; it fires no events and has no component.

## Extensibility

- **Hand-authored help topics** (lore, game concepts) would require an `IHelpRegistry` that `HelpCommand` queries after the ability fallback. That is the point at which Help would likely split from the Communication feature into its own feature folder.
- **Tab completion** — `IVerbRegistry.GetPrefixCandidates` is the seam; a future completion system queries the same method.
- **Admin help** — admin commands are visible when the invoker satisfies `AdminRequirement`; no change to `HelpCommand` needed.

## Related

- [`communication.md`](communication.md) — holistic feature view.
- [`../../reference/commands.md`](../../reference/commands.md) — `help` / `commands` command rows.
- [`../../features/commands/command-framework.md`](../commands/command-framework.md) — `IVerbRegistry`, `CommandDispatcher` three-phase lookup, and `IAuthorizationChecker` design.
- [`../../features/abilities/abilities.md`](../abilities/abilities.md) — `IAbilityRegistry` and `AbilityDefinition` shape that `HelpCommand` reads for the ability fallback.
