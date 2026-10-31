# Help Topics

> Authored `help <topic>` articles: an engine layer plus a world overlay, validated and merged into one snapshot on every content load through the reload-participant port, and authored in the editor. **Status:** live (Phase 5 slice 4, [help-topics](../../roadmap/completed/help-topics.md)). Living document.

## What it is / does

A help topic is a short article about a mechanic or a piece of the world — `help combat`, `help getting started`. Topics are **world-content definitions** (a string-keyed Spine F `DefinitionRegistry` family), never entities (INV-12/23) and never persisted.

They come from two **layers**, merged by id with the later layer winning:

| Layer | Where | Owned by | Written by tooling? |
|---|---|---|---|
| **Engine** | `Help:EngineTopicDirectory` (default `content/help/`, committed, copied to the build output; resolved against the Hedron root, never under `Paths:DataDirectory`) | the engine | **never** — edited by hand in PRs |
| **World** | `<World:ContentDirectory>/help/` | the world's author (operator or designer) | yes — the topic editor |

So an operator gets the engine's generic mechanic topics out of the box, can **override** any of them with a world file of the same id, and can **add** their own (lore, local guides). The engine set (`getting-started`, `combat`, `death`, `shops`, `progression`) is generic and **qualitative about tunables**: it never states a number that per-server configuration could make wrong (a content test bans digits in engine bodies).

## How it works

### File shape

One YAML file per topic, conventionally `<id>.yaml`:

```yaml
id: getting-started          # lowercase letters, digits, single hyphens (trimmed + lowercased on load)
title: Getting started
aliases: [start, newbie]     # optional extra lookup tokens, same token rules
seeAlso: [combat, death]     # optional topic ids or aliases
body: |
  Plain text, multi-paragraph, rendered literally.
```

### Rebuild: the reload-participant port

`HelpTopicReloadParticipant` implements the core-owned `IContentReloadParticipant` port (`Core/Systems/Definitions/`, see [world-content.md](../world/world-content.md#content-reload-participants)). The world content loader invokes it after every **successful** boot load and `reload`, in every host — telnet server, run modes, playtest worlds and `Hedron.Web`. A **refused** reload runs no participant, so topics keep their previous snapshot. A rebuild:

1. `IHelpTopicLayerReader.ReadLayers()` reads the engine root then the world root through the shared `YamlDefinitionDirectory` (missing directories and unparseable files never throw);
2. the participant gathers the verb tokens (names and aliases, flagging admin-only ones) from `Lazy<IVerbRegistry>`;
3. the pure `HelpTopicSetBuilder.Build(layers, verbs, adminVerbs)` validates, merges and checks collisions, returning a new immutable `HelpTopicSet` and its diagnostics;
4. `HelpTopicRegistry.Publish(set)` swaps the snapshot in one reference;
5. the diagnostics go back to the loader, which logs them as one block (the boot report) and returns them in `ContentReloadResult.Diagnostics` — `reload` prints one line each, the editor's Apply page lists them.

Help **never gates the world**: a topic problem is reported, never a refusal.

### Validation

Errors skip the file; warnings load it with a note. Diagnostic sources are `<layer>/<file>` (e.g. `world/kill.yaml`).

| Finding | Severity | Effect |
|---|---|---|
| Unparseable file; missing `id`/`title`/`body`; invalid id | Error | file skipped |
| A skipped world file whose name matches an engine topic | Warning | the **engine version is served** (a world typo never removes a topic) |
| Invalid alias | Warning | alias dropped |
| Duplicate id within one layer | Warning | the ordinal-first file name wins |
| Alias equal to another topic's id | Warning | alias dropped (the id wins) |
| Alias shared by two topics | Warning | the ordinal-first topic id keeps it |
| Id or alias equal to a verb name or verb alias | Warning | the topic is unreachable by that token, since verb help wins (the message says "for administrators" when the verb is admin-only) |
| `seeAlso` entry naming no topic | Warning | entry left out |
| Engine directory missing | Warning | no engine topics |
| World directory resolving to the engine directory | Warning | world layer skipped |

### The snapshot

`HelpTopicSet` is a `DefinitionRegistry<string, HelpTopic>` built once per rebuild, carrying its alias index and id order alongside the rows, so rows, aliases and origins always swap **together** (a `Reload(rows)` on the base would let a reader see new rows with old aliases). Each `HelpTopic` records its `Origin` (`Engine`, `World`, `WorldOverride`) and `SourceFile`; neither is ever sent to a player. `IHelpTopicRegistry.Current` is published with `Volatile.Write` and read lock-free (INV-31, guarded by single-reference publication); it is `HelpTopicSet.Empty` until the first load.

### Authoring

`IHelpTopicAuthoring` (Help module) is the editor's thin-caller seam (INV-18). It reads disk on every call — **disk is the editor's truth, the registry is the game's** — and runs the same reader and builder, so the editor shows exactly what the next load will produce. It writes the **world layer only**:

- `List()` — every merged topic with its origin and file, plus all diagnostics;
- `Load(id)` / `Preview(draft)` — the preview renders through the engine's telnet formatter (colour off) via `HelpTopicRendering`, the same mapping `help` uses;
- `SaveAsync(draft, previousId?)` — writes `<world>/<id>.yaml`; a rename, or a topic loaded from a differently named file, deletes the old file after the new write succeeds; returns the R10 warnings for the topic;
- `OverrideAsync(engineId)` — copies an engine topic into the world layer (refused if a world copy exists);
- `DeleteAsync(id)` — deletes a world topic; deleting an override brings the engine version back; engine topics cannot be deleted.

Writes run under the module's own `SemaphoreSlim(1,1)` gate (last save wins). Saves take effect in-game on the next `reload` (or the editor's Apply to live). Editor pages: `/help-topics` (list) and `/help-topics/edit/{id}`. There are no in-game write verbs — multi-paragraph text over telnet is clunky — and administrators inspect in-game with `help <id>` plus the origin footer.

## Interface

- [`HelpTopic.cs`](../../../Core/Modules/Help/HelpTopic.cs) — `HelpTopic`, `HelpTopicOrigin`, `HelpLayer`, the YAML DTO `HelpTopicDocument`.
- [`HelpOptions.cs`](../../../Core/Modules/Help/HelpOptions.cs) — `EngineTopicDirectory`; `WorldTopicDirectory` (derived by `DataPaths`, not a config key).
- `Core/Modules/Help/Systems/`: `IHelpTopicLayerReader`/`HelpTopicLayerReader`, `HelpTopicSetBuilder`, `HelpTopicSet`, `IHelpTopicRegistry`/`HelpTopicRegistry`, `HelpVerbTokens`, `HelpTopicReloadParticipant`, `IHelpTopicAuthoring`/`HelpTopicAuthoring`.
- [`HelpTopicRendering.cs`](../../../Core/Modules/Help/HelpTopicRendering.cs) — topic → `HelpTopicMessage`.
- `Hedron.Web`: `HelpTopics.razor`, `HelpTopicEditor.razor`, `Services/HelpTopicPreview.cs`.
- Engine content: [`content/help/`](../../../content/help/).

## Considerations

- **Layering is help-local, shaped for later.** The merge runs over an ordered list of roots keyed by string id. It moves to `Core/Systems/Definitions/` when a second family needs committed-plus-world layering (socials, playtest scenarios, possibly Ability/Effect, though those are compiled-plus-YAML).
- **Verb collisions are a moving target.** A new command whose name equals a topic id shadows that topic; the engine-topic content test (zero diagnostics against the real verb set) catches it for engine topics.
- **Two processes.** The editor (`Hedron.Web`) and the game server are separate processes; an editor save reaches the server on its next `reload`.

## Related

- [`help.md`](help.md) · [`help-system.md`](help-system.md) — the feature and the lookup.
- [`../world/world-content.md`](../world/world-content.md#content-reload-participants) — the reload-participant phase.
- [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md) — the install-relative engine help location.
- [`../../reference/systems.md`](../../reference/systems.md) — the shared YAML store and the Help system rows.
