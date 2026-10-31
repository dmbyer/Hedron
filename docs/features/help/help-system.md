# Help System

> The `help` and `commands` surface: the six-tier lookup over verbs, help topics and abilities, the category-grouped index and Topics block, the admin origin footer, and the typed output shapes. **Authoring checkpoint:** shipped with the Help module; lookup extracted into `IHelpLookupSystem` in Phase 5 slice 4 ([help-topics](../../roadmap/completed/help-topics.md)). Living document.

## What it is / does

`help` resolves one query against three families — **verbs** (the registered commands), **help topics** (authored articles, see [help-topics.md](help-topics.md)) and **abilities** — and renders whatever matched. `commands` is the terse verbs-only index. Both are read-only and usable while incapacitated.

The lookup rule lives in the domain system **`IHelpLookupSystem`** (`Core/Modules/Help/Systems/`); `HelpCommand` is a thin initiator that parses the argument, calls the system, and renders the result (INV-8).

## How it works

### The query (`HelpQuery`)

The argument is the **rest of the line** (`CommandArgumentKind.RestOfLine`). `HelpQuery.Parse` strips one pair of surrounding quotes (as the old token parser did), keeps the typed text as `Raw`, and builds a `TopicKey` by joining whitespace runs with hyphens and lowercasing — so `help getting started`, `help Getting-Started` and `help getting-started` share one key. An argument that is only whitespace or hyphens is treated as bare `help`.

### The tiers (`IHelpLookupSystem.Resolve`)

Exact matches beat prefix matches; within each tier the family order is verb → topic → ability. The first tier with a **visible** match wins:

1. exact verb or verb alias;
2. exact topic id or alias;
3. exact ability id or display name;
4. verb prefix — one visible candidate resolves, several give `Ambiguous command '<x>'. Did you mean: …?`;
5. topic prefix — one resolves, several give `Ambiguous topic '<x>'. Did you mean: …?`;
6. ability prefix — a unique id/display-name prefix resolves; an ambiguous one is "No help found" rather than a guess.

Verbs and topics never share one ambiguity prompt: the tiers are per family, so the verb-prefix tier settles before the topic-prefix tier.

**Visibility.** A command whose `RequiredPrivileges` the invoker does not satisfy is never a match at any tier — `help reload` for a player falls through to a topic or ability of that name, exactly as admin commands stay hidden from the index.

**Multi-word queries.** A query of several words first walks tiers 2, 3, 5 and 6 with the **whole** query (so `help getting started` finds the topic, and `help fire bolt` an ability by display name). Only if nothing matches does its **first word** resolve through the verb tiers 1 and 4 — `help kill orc` still shows `kill`. A multi-word topic therefore beats a verb named by its first word.

**Ability catalogs.** When the *typed* token is exactly `skills`, `spells` or `abilities` and resolves to a verb, the result carries a catalog request and the command appends the global ability list of that kind (all kinds for `abilities`). A prefix such as `help ski` shows the verb without the catalog — unchanged behavior.

**One snapshot per call.** `Resolve` reads `IHelpTopicRegistry.Current` once, so a concurrent `reload` never tears a result (INV-31).

The system returns a `HelpResolution` — `Index`, `Verb(VerbHelp, catalog?)`, `Topic(HelpTopic)`, `Ability(AbilityDefinition)`, `Ambiguous(family, candidates)` or `NotFound`. `VerbHelp` is plain command metadata: the result never carries an `ICommand`, so nothing above the system layer leaks into it.

### The index (bare `help`)

`BuildIndex(invoker)` returns the visible verbs (category order, then name) and every topic (id order). `HelpCommand` writes the unchanged `HelpIndexMessage`, then — when at least one topic exists — a `HelpTopicIndexMessage` (the **Topics** block, `id — title` rows). `commands` writes only the verb index.

### Rendering

| Resolution | Output |
|---|---|
| `Index` | `HelpIndexMessage`, then `HelpTopicIndexMessage` if any topics |
| `Verb` | `HelpEntryMessage(verb, longDescription, usage, aliases)`, then the ability catalog lines if requested |
| `Topic` | `HelpTopicMessage(id, title, body, seeAlso)`; **administrators** also get `HelpTopicOriginMessage(origin)` |
| `Ability` | a formatted ability block (`PlainMessage`) |
| `Ambiguous` / `NotFound` | a `PlainMessage` prompt |

The **admin origin footer** — `[engine topic]`, `[world topic]` or `[world topic — overrides engine]` — is a *separate* message written only when the invoker satisfies `AdminRequirement`, so a player's output (and any serializing formatter, such as the playtest agent formatter) never carries a topic's origin. `HelpTopicMessage` itself has only player-visible properties.

The telnet formatter renders a topic as `[Title]`, the body, and a `See also: help a, help b` line when there are resolved see-also ids. **Authored title and body text is rendered literally** — it never passes through the colour-marker parser, so authored `<error>…</error>` text shows as typed.

## Interface

- [`IHelpLookupSystem.cs`](../../../Core/Modules/Help/Systems/IHelpLookupSystem.cs) / [`HelpLookupSystem.cs`](../../../Core/Modules/Help/Systems/HelpLookupSystem.cs) — `BuildIndex(ISession)`, `Resolve(HelpQuery, ISession)`.
- [`HelpQuery.cs`](../../../Core/Modules/Help/Systems/HelpQuery.cs), [`HelpResolution.cs`](../../../Core/Modules/Help/Systems/HelpResolution.cs) — the query value and the result union (`VerbHelp`, `HelpAbilityCatalog`, `HelpMatchFamily`, `HelpIndex`).
- [`HelpCommand.cs`](../../../Core/Modules/Help/Commands/HelpCommand.cs) — gather → resolve → render; ability formatting and catalogs are presentation.
- [`CommandsCommand.cs`](../../../Core/Modules/Help/Commands/CommandsCommand.cs) — terse verb index only.
- [`HelpModule.cs`](../../../Core/Modules/Help/HelpModule.cs) — DI composition (lookup, topic family, commands).
- Output shapes in `Core/Output/`: `HelpIndexMessage`/`HelpIndexEntry`, `HelpEntryMessage`, `HelpTopicMessage`, `HelpTopicIndexMessage`/`HelpTopicIndexEntry`, `HelpTopicOriginMessage` — all `OutputCategory.Help`.

## Considerations

- **The verb registry is read as metadata.** `IVerbRegistry` is implemented by `CommandDispatcher` (the command-initiator tier). Domain systems may read it — names, aliases, descriptions, usage, privileges — but never execute a command; the sanctioned rule is in [`01-layers.md`](../../architecture/01-layers.md#cross-layer-dependency-rules).
- **Circular dependency.** `help` is an `ICommand`, and the dispatcher depends on every `ICommand`. Every Help consumer of the verb set (`HelpLookupSystem`, the topic reload participant, topic authoring) takes `Lazy<IVerbRegistry>`; `HelpModule` registers that `Lazy<>` once. The DI-smoke guard covers the chain.
- **`UsableWhileIncapacitated: true`** on both commands.
- **No persistence, no events.** Help is a pure read path.
- **Verb help wins over a same-named topic.** A topic id or alias equal to a visible verb is reachable only by another alias; the topic family warns about such collisions at load time ([help-topics.md](help-topics.md#validation)).

## Extensibility

- **Tab completion** — `IVerbRegistry.GetPrefixCandidates` plus the topic set's `PrefixMatches` are the seams; completion should consume `IHelpLookupSystem`'s tier order rather than re-implement it.
- **Search** — the horizon's "searchable help" would be a new query mode on `IHelpLookupSystem` over the same topic snapshot.
- **Admin-only topics** — parked; an optional privilege field on topics, filtered in `BuildIndex`/`Resolve` with the same visibility gate.

## Related

- [`help.md`](help.md) — the holistic Help feature.
- [`help-topics.md`](help-topics.md) — the authored topic family (layers, validation, reload participant, authoring).
- [`../../reference/commands.md`](../../reference/commands.md) — `help` / `commands` rows.
- [`../commands/command-framework.md`](../commands/command-framework.md) — `IVerbRegistry`, the dispatcher's lookup, `IAuthorizationChecker`.
- [`../abilities/abilities.md`](../abilities/abilities.md) — `IAbilityRegistry`, read for the ability tiers.
