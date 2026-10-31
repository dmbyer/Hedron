# Help

> In-game reference: `help` (verbs, authored topics and abilities in one lookup) and `commands` (the verb list), with authored topic articles from an engine layer and the world's own overlay. **Status:** live (Help module; topics since Phase 5 slice 4).

## What it is

A player who types `help` sees every command available to them, grouped by category, followed by a **Topics** block. `help <thing>` explains a command (`help look`, or a prefix like `help lo`), an authored topic (`help combat`, `help getting started`), or an ability (`help kick`). `help skills`, `help spells` and `help abilities` also list every registered ability of that kind. `commands` is the terse verbs-only list. Both work while incapacitated, so a downed player can still read.

Topics are short articles about how the game works. The engine ships generic ones (getting started, combat, death & respawn, shops & money, progression); a world can replace any of them with its own version or add new ones — an operator's lore, a local guide. Administrators see a one-line footer saying where a topic came from (`[engine topic]`, `[world topic]`, `[world topic — overrides engine]`), and author world topics in the editor's **Help topics** page.

## How it works

- **Lookup** — `IHelpLookupSystem` resolves a query across verbs, topics and abilities: exact matches before prefix matches, verb → topic → ability within each, visibility-gated. `HelpCommand` renders the result. See [`help-system.md`](help-system.md).
- **Topics** — `HelpTopicReloadParticipant` rebuilds the merged engine + world topic snapshot whenever world content loads (boot, `reload`, the editor's Apply), reporting problems without ever blocking a load; `IHelpTopicAuthoring` backs the editor. See [`help-topics.md`](help-topics.md).

## Systems

| System | Role |
|---|---|
| [`help-system.md`](help-system.md) | The `help`/`commands` lookup, index, rendering and admin footer |
| [`help-topics.md`](help-topics.md) | The authored topic family: layers, validation, reload participant, authoring |

## Surfaces

- **Commands** — `help` / `?`, `commands`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Output shapes** — `HelpIndexMessage`, `HelpEntryMessage`, `HelpTopicMessage`, `HelpTopicIndexMessage`, `HelpTopicOriginMessage` (in `Core/Output/`).
- **Content** — engine topics in [`content/help/`](../../../content/help/); world topics in `<content>/help/`.
- **Config** — `Help:EngineTopicDirectory` (see [`05-configuration.md`](../../architecture/05-configuration.md)).
- **Editor** — `/help-topics` in `Hedron.Web`.
- **Events / components** — none. Help is a read path; topics are definitions, not entities.

## Flows

- [Flow 5 — Content reload](../../architecture/flows/flow-05-content-reload.md) and [Flow 1 — Server startup](../../architecture/flows/flow-01-server-startup.md): the reload-participant step that rebuilds topics.
- [Flow 29 — Content-tooling journey](../../architecture/flows/flow-29-bulk-content-generation.md): the Help topics page leg.

The `help` command itself is a single synchronous read with no cross-system leg, so it has no journey of its own.

## Related

- [`../communication/communication.md`](../communication/communication.md) — chat (`say`), Help's former home.
- [`../commands/command-framework.md`](../commands/command-framework.md) — `IVerbRegistry` and the dispatcher.
- [`../abilities/abilities.md`](../abilities/abilities.md) — the ability registry read by the lookup.
- [`../../design/feature-horizon.md`](../../design/feature-horizon.md) — "Help system & in-game wiki" (search and cross-linking remain future work).
