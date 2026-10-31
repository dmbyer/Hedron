# Help topics — Phase 5 slice 4

- **Status:** planned (full plan; next step is the spec-mode `architecture-reviewer` gate, then `implement-plan`)
- **Actors:** Player (reads topics), Administrator (designer or operator authoring world topics, inspecting topic origin), System (loads and layers topics at boot and on `reload`)
- **Module:** `Core/Modules/Help` (topic registry, `help` topic step), a shared YAML definition store beside the registry base (unless [PT-2](playtest-run-mode.md) lands it first, R14), `Hedron.Web` (topic editor). Feature home today: [`communication/help-system.md`](../features/communication/help-system.md). That doc already says authored topics are where Help would split into its own feature folder; the planner decides whether this slice does that. **Planner: yes.** Help moves to `docs/features/help/` in WP3 (see [Planner resolutions](#planner-resolutions) §6). The reload-participant port and its diagnostic type are core-owned in `Core/Systems/Definitions/` (§1).
- **Program:** slice 4 of [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md#slice-decomposition). It covers **R18–R19** and inherits the program's help Design notes and advisor resolved decision 1. Parallel with slices 2, 3 and 5; content drops 6–8 depend on it for help topic text.

**Description.** `help` today covers verbs only: the verb index, verb help, an ability fallback, and the `skills`/`spells`/`abilities` catalogs. This slice adds **authored topic articles**. A player who types `help combat` or `help getting started` reads a short article about a mechanic. The engine ships a committed, generic set of mechanic topics. Each world's content directory can **override** them by id or **add** its own (an operator's lore topics, for example). Designers author world topics in the editor without a recompile, and the engine layer is shown read-only with an override action. Underneath, the slice delivers the program's scheduled extraction of the shared YAML definition store, which help topics need as the fourth hand-rolled family, and migrates the balance standards, sim scenarios and world manifest onto it. Whichever of this slice and PT-2 merges first does that extraction (R14).

## Requirements

*(In-flight requirements tier, produced by the `requirements-detailing` intake on 2026-10-31. The advisor extends this file with its brief. The planner absorbs the R-numbers into Preconditions/Postconditions and Main flow. Program R-numbers are cited as "program R18". This file's own R-numbers are local.)*

### User stories

- *As a new player*, I want `help getting started` (and `help combat`, `help death`, …) to explain the game's mechanics in plain prose, so I can learn without asking an admin. The verb list alone tells me *what* I can type, not *how the game works*.
- *As a player*, I want bare `help` to show me which topics exist, not just which verbs, so I can discover the articles at all.
- *As a designer*, I want to write and edit world help topics in the editor and see them live after `reload`, without a recompile (program R19, INV-18).
- *As an operator*, I want the engine's generic topics to work out of the box, and I want to override any of them with my world's version or add my own topics. That way my world's help speaks in its own voice without me forking engine files.
- *As an administrator*, I want to see where a topic I'm reading came from (engine, world, or a world override of an engine topic), so I can tell which file to fix.
- *As the system*, I want a broken topic file to be reported without taking down help or blocking a world reload.

**Main scenario (player).**
1. The player types `help` and sees the existing category-grouped verb index, followed by a **Topics** block listing `combat — Combat`, `death — Death & respawn`, `getting-started — Getting started`, and so on.
2. They type `help getting started` and see the title line, the article body, and a `See also: help combat, help progression` line.
3. An administrator typing the same command sees the same output plus a one-line origin footer, e.g. `[engine topic]`.

### Scope

**In scope:**
- A help-topic definition family with two layers: the committed **engine layer** (generic mechanics) and the **world layer** (`<ContentDirectory>/help/`). The world wins by topic id. *(Program design note.)*
- The `help` topic step in the lookup (R5–R7), the Topics block in the index (R3), topic output (R4), and the admin origin footer (R8).
- The five engine topics from program R18 (R11).
- Authoring and inspection in the Blazor editor: the world layer is editable, and the engine layer is read-only with an "override" action (R12).
- Topic validation and warnings (R9–R10), and re-derivation on `reload` (R13).
- The shared YAML definition store extraction and the migration of standards, scenarios and the world manifest, **if this slice lands before PT-2 WP1** (R14–R15).

**Out of scope (confirmed exclusions):**
- **Search or keyword lookup** (`help search …`). The horizon's "Help system & in-game wiki" entry asks for it; it stays there.
- Cross-link rendering beyond the `seeAlso` line, and colour/markup inside topic bodies.
- **Per-topic privilege**, i.e. admin-only topics. Every topic is public (Parked, see Open questions).
- **In-game admin write verbs** for topics. Authoring happens in the editor, or by editing the file and running `reload`.
- **A dev-world help overlay.** No `content/dev-world/help/` ships in this slice. Overrides are proven in tests. Content drops 6–8 add world topics if the hub needs them.
- Help text inside room descriptions. That is program R17, in the content drops.
- Changes to the `commands` command (it stays verbs-only) or to the `skills`/`spells`/`abilities` special topics.

### Behavioral requirements

**A. Topic article**
- **R1.** A topic has:
  - an **id**: one lowercase token, hyphens allowed, e.g. `getting-started`;
  - a **title**: display text, e.g. `Getting started`;
  - optional **aliases**: extra lookup tokens, same token rules as the id;
  - a **body**: plain text, multi-paragraph;
  - an optional **`seeAlso`** list of topic ids.
- **R2.** A multi-word argument is normalized before lookup: whitespace runs become hyphens and the result is matched case-insensitively. `help getting started`, `help Getting-Started` and `help getting-started` resolve to the same topic.

**B. Index and output**
- **R3.** Bare `help` writes the existing visibility-filtered verb index unchanged, followed by a **Topics** block listing every visible topic as `id — title`, in ordinal order by id. When no topics exist the block is omitted. `commands` is unchanged and lists verbs only.
- **R4.** A resolved topic writes, in order:
  1. a title line;
  2. the body;
  3. a `See also: help <id>, help <id>` line, when `seeAlso` names at least one topic that resolves.

  A `seeAlso` id that doesn't resolve is left out of the output (and flagged at authoring time, R10).

**C. Lookup**
- **R5.** Lookup goes **exact before prefix**, and verb → topic → ability within each tier:
  1. an exact verb or verb alias;
  2. an exact topic id or alias;
  3. an exact ability id or display name;
  4. a verb prefix;
  5. a topic prefix;
  6. an ability prefix.

  The first tier with a unique visible match wins. *(This refines the program's adopted "verb → topic → ability" default: the family order holds within a tier, but an exact topic match is no longer shadowed by a verb prefix.)*
- **R6.** An ambiguous prefix inside a tier behaves as ambiguous verb prefixes do today (disambiguate, or prompt with the candidates). Topics and verbs are never mixed in one candidate list unless the planner finds the existing prompt shape supports that cleanly. *(Planner confirms.)*
- **R7.** Topic lookup, like the rest of `help`, works while incapacitated and in combat. It is read-only and fires no events.

**D. Administrator inspection**
- **R8.** When the invoker satisfies the admin requirement, topic output ends with a one-line origin footer: `[engine topic]`, `[world topic]`, or `[world topic — overrides engine]`. Players never see it.

**E. Validation and failure**
- **R9.** A topic file that fails to parse, or that is missing its id, title or body, is **warned and skipped**. The warning appears in the boot report, in the `reload` output and in the editor. The rest of help, and the world reload itself, proceed normally. A help-topic error never refuses a `reload`.
- **R10.** **Authoring warnings** (non-blocking, shown in the boot report and in the editor):
  - a topic id or alias that exactly equals a visible verb or verb alias (the topic would be unreachable by that token, since verb wins);
  - a topic alias that collides with another topic's id or alias;
  - a duplicate id within one layer;
  - a `seeAlso` entry that names no topic.

  For a duplicate id within one layer, the planner picks the deterministic winner, e.g. ordinal-first file.

**F. Engine topic set**
- **R11.** The engine layer ships at least these topics (program R18): `getting-started`, `combat`, `death` (title *Death & respawn*), `shops` (title *Shops & money*), `progression`. Aliases are chosen by the author, e.g. `respawn`, `money`, `xp`. The text is **engine-generic** (mechanics, no lore, no world-specific room names, program R19) and **qualitative about tunables**: it says "you return to your respawn point with reduced health", never "25%". That way it can't drift from per-server configuration.

**G. Authoring and lifecycle**
- **R12.** The **topic editor** (Blazor):
  - lists every topic with its layer and override status;
  - creates, edits and deletes **world** topics;
  - shows engine topics **read-only** with an **Override** action, which copies the topic into the world layer for editing;
  - restores the engine version when a world override is deleted;
  - surfaces the R9 errors and R10 warnings;
  - previews a topic as the player will see it (R4).
- **R13.** Topics are loaded at boot and **re-derived on `reload`**. Edits to world topic files take effect on the next `reload`, the same as other content. Readers (sessions, editor circuits) always see one complete topic set, never a half-swapped one (program INV-31 note).
- **R14.** **Shared YAML store: whichever lands first extracts.** [PT-2 WP1](playtest-run-mode.md)'s type shapes (`YamlDocumentFile`, `YamlDefinitionDirectory`, `YamlReadResult`, `DefinitionValidation`) are the **shared contract**.
  - If this slice merges before PT-2 WP1, it performs that extraction and migrates `BalanceStandardsStore`, `SimScenarioStore` and `WorldManifestStore`.
  - If PT-2 merged first, this slice consumes the store as shipped.
  - Either way this slice adds the **layered** engine + world composition over two directory roots; PT-2 explicitly leaves that to slice 4.
- **R15.** The migration has **no visible effect**. Standards, scenarios and `world.yaml` load, validate, warn and save exactly as today, and their existing tests pass with their existing cases unmodified.

### Edge cases & failure behavior

- **No topics at all** (engine layer missing or empty, no world layer): `help` shows the verb index with no Topics block, and `help <x>` falls through to abilities and then "No help found", as today. Startup logs a warning if the engine layer can't be found.
- **A world override that fails to parse:** the **engine version is served** and a warning is logged (R9). The player never loses a topic because of a world-side typo.
- **A topic id equal to a verb** (e.g. a world topic `kill`): the verb wins (R5), and the R10 warning tells the author to rename the topic or use an alias.
- **An exact topic match versus a verb prefix** (`help combat` while some verb starts with `combat…`): the topic wins (R5).
- **An unknown topic with no verb or ability match:** the existing "No help found for '<x>'" message.
- **`help` with only whitespace or hyphens:** treated as bare `help` (the index).
- **A reload while a player reads help:** the player gets either the old or the new topic set, complete (R13).
- **Two editor circuits saving the same world topic:** the last save wins, under the existing content-catalog write posture. *(Planner confirms the gate applies.)*

### Content & authoring needs

- **Engine topic files:** the five R11 articles, authored by the maintainer as committed engine content, edited by hand and reviewed in PRs. They are not editable from the editor (R12).
- **World topic files** under `<ContentDirectory>/help/`: authored in the topic editor or by hand, applied with `reload`, and inspected in-game with `help <id>` plus the admin origin footer (R8).
- **Validation feedback** (R9–R10) in the boot report, the `reload` output and the editor.

### Grounding notes

- **Exists:** verb-only `help` (exact → prefix → ability fallback, special ability catalogs, visibility gate, `UsableWhileIncapacitated`) and `commands`, with structured output shapes `HelpIndexMessage`/`HelpEntryMessage` ([help-system.md](../features/communication/help-system.md)). Its Extensibility section anticipated "hand-authored help topics … would require an `IHelpRegistry`" and the feature-folder split.
- **The program fixes these** ([Design notes](phase-5-content-baseline-program.md#design-notes), advisor resolved decision 1):
  - topics are a `DefinitionRegistry` family, never `TemplateRegistry`;
  - the engine layer comes first and the world layer overrides by id;
  - lookup is verb → topic → ability (refined by R5);
  - topics are re-derived on `reload` as an atomic snapshot;
  - the editor shows the engine layer read-only with an override action;
  - topics are YAML world-content definitions, never persisted entities.
- **Reload:** [flow-05](../architecture/flows/flow-05-content-reload.md) stages and validates world content and **refuses** on shape errors. R9 deliberately takes the opposite posture for help, because help is non-gameplay.
- **YAML store collision:** [PT-2](playtest-run-mode.md) WP1 branch B ("the expected case") plans the extraction itself, with the type shapes above, and expects help-topics to consume it. The [backlog](../roadmap/backlog.md) entry ("YAML-authored definition pipeline") records instances #1–#4 and the shape obligation: cover both the single-document shape (standards, manifest) and the per-file directory shape (scenarios, help topics). Resolved by R14.
- **Existing stores:** [`BalanceStandardsStore`](../features/progression/power-budget-system.md) (single document, under `Balance:StandardsPath`), [`SimScenarioStore`](../features/simulation/simulation-engine.md) (directory, `Simulation:ScenarioDirectory`, validate-then-write, sanitized filename), and `WorldManifestStore` ([dev-world-foundation](../roadmap/completed/dev-world-foundation.md), accepted bounded debt).
- **Horizon:** "Help system & in-game wiki" ([feature-horizon.md](../design/feature-horizon.md)) covers topic articles and search. This slice delivers the articles; search stays on the horizon.
- **Neighbours:**
  - the playtest program's newcomer persona deliberately never types `help`, so onboarding must not depend on it alone (program R17 room text covers that);
  - PT-2's acceptance walker plan includes a `help` step.

### Resolved decisions

*(Confirmed by the user during intake on 2026-10-31. Do not relitigate downstream.)*

1. **YAML store ownership:** whichever of slice 4 and PT-2 WP1 merges first performs the extraction, on PT-2 WP1's type shapes as the shared contract. Slice 4 always adds the layered composition. Rejected: slice 4 always extracts (it would force ordering); slice 4 waits for PT-2.
2. **Admin authoring surface:** the editor plus an in-game admin origin footer. No in-game write verbs. Rejected: editor only with no in-game inspection; in-game create/set/delete verbs (multi-paragraph text over telnet is clunky).
3. **Malformed topic posture:** warn and skip, and never block boot or `reload`. A broken world override falls back to the engine version. Rejected: refusing like world content.
4. **Matching:** exact before prefix across families, and verb → topic → ability within a tier. Rejected: strict family order, where a verb prefix could shadow an exact topic.
5. **Suppositions S1–S11 approved as proposed:**
   - the article shape (R1) and multi-word normalization (R2);
   - the Topics block in the index (R3) and `seeAlso` output (R4);
   - the five qualitative engine topics (R11);
   - no dev-world overlay in this slice;
   - all topics public;
   - collision warnings (R10);
   - engine fallback for a broken override;
   - the out-of-scope list;
   - a behavior-preserving migration (R15).

## Preconditions

- Phase 5 slice 1 has shipped ([`dev-world-foundation`](../roadmap/completed/dev-world-foundation.md)): `WorldManifestStore`, `IWorldManifestAuthoring`, and the `DataPaths` rule (`Paths:DataDirectory` plus per-location keys, relative paths resolved against the Hedron root) composed by every host through `CompositionRoot.Register`.
- The command framework provides `IVerbRegistry` (`AllCommands`, `TryGetExact`, `GetPrefixCandidates`), `IAuthorizationChecker` + `AdminRequirement`, `CommandArgumentKind.RestOfLine`, and the `UsableWhileIncapacitated` gate.
- `WorldContentLoader` stages the corpus and refuses on shape errors ([flow-05](../architecture/flows/flow-05-content-reload.md)). Its callers are `WorldContentBootstrap` (Server, `Hedron.Web`, and PT-1 playtest worlds), `ReloadCommand`, and the web Apply page. The `generate`/`simulate` run modes never load world content, so they never need topics.
- **WP1 start check:** whether [PT-2 WP1](playtest-run-mode.md) has merged. At planning time (2026-10-31) `Core/Systems/Definitions/` does not exist and PT-1 (PT-2's dependency) has not merged, so **branch B (this slice extracts) is the expected case**.

## Postconditions

*(The coverage contract. Each maps to a Test-plan row.)*

1. **Shared YAML store (R14, R15).** `Core/Systems/Definitions/` holds `YamlDocumentFile<TDto>`, `YamlDefinitionDirectory<TDto>`, `YamlReadResult<TDto>`, `YamlFileEntry`, `YamlCodecOptions` and `DefinitionValidation` with exactly PT-2 WP1's shapes, plus two additive members on `YamlDefinitionDirectory<TDto>`: `string Directory { get; }` and `bool Delete(string path)`. `Delete` throws `ArgumentException` for any path outside `Directory` and returns `false` when the file is absent.
   - **The two records PT-2 WP1 names but never defines** are pinned here, and PT-2 copies them verbatim (spec review):
     - `sealed record YamlFileEntry(string Path, string FileName, string Name, string? Error)`. `Name` is the `nameOf` result, falling back to the file-name stem. `Error` is non-null for an unparseable file, which is still listed. This is today's `ScenarioFileSummary` plus `Error`.
     - `sealed record YamlCodecOptions(bool OmitNulls = false)`. CamelCase naming and `IgnoreUnmatchedProperties` are always on, since all three stores use them. `OmitNulls` maps to `DefaultValuesHandling.OmitNull`: the manifest sets it; standards, scenarios and help topics don't.
   - `YamlReadResult<TDto>` also carries **`Exception? Fault`**, the caught exception behind `Error`. `Read` never throws, but a migrated store that throws today can rethrow the original exception unchanged (WP1 branch B, step 2).

   *Branch B:* `BalanceStandardsStore`, `SimScenarioStore` and `WorldManifestStore` run on the store. None of them keeps a `new DeserializerBuilder()`/`new SerializerBuilder()`, and their existing test classes pass with their existing cases unmodified. *Branch A:* only the two additive members are new.
2. **Reload-participant port.** `IContentReloadParticipant` (`string Name`; `Task<IReadOnlyList<ContentDiagnostic>> RebuildAsync(CancellationToken)`), `ContentDiagnostic(Severity, Source, Message)` and `ContentDiagnosticSeverity { Warning, Error }` live in `Core/Systems/Definitions/`. `WorldContentLoader` takes `IEnumerable<IContentReloadParticipant>` as an optional trailing constructor parameter, so existing test constructions compile unchanged.
3. **When participants run (R9, R13).** After a successful `LoadAndSpawnAsync` or `ReloadAsync`, the loader invokes every participant in registration order, *after* `ResolveStartingRoom`. A participant that throws (other than cancellation) becomes one `Error` diagnostic, and the load still succeeds. A **refused** reload (`ContentLoadException`) runs no participant, and the topic snapshot is unchanged.
4. **Diagnostics reach every report (R9).** The loader logs participant diagnostics: warnings at Warning level, errors at Error level, one block per load. That log is the boot report. `ContentReloadResult` (a `readonly record struct`) gains a non-positional `Diagnostics` property. Its getter falls back to an empty list over a null backing field, so `default(ContentReloadResult).Diagnostics` is empty as well. `ContentReloadedEvent` is unchanged. `ReloadCommand` writes one line per diagnostic after its unchanged confirmation, and the web Apply page lists them.
5. **Topic file shape (R1, R9).** One YAML file per topic: `id`, `title`, `aliases` (optional), `seeAlso` (optional), `body` (block scalar).
   - The id is trimmed and lowercased, then must match `^[a-z0-9]+(-[a-z0-9]+)*$`.
   - A file that fails to parse, lacks `id`/`title`/`body`, or has an invalid id is **skipped** with an `Error` diagnostic.
   - An invalid alias is dropped with a `Warning`.
   - The diagnostic `Source` is `<layer>/<file name>`, e.g. `world/kill.yaml`.
6. **Layers.** Two ordered roots.
   - **Engine root:** `Help:EngineTopicDirectory`, default `content/help`. It is resolved by `DataPaths` against the Hedron root, **not** under `Paths:DataDirectory`.
   - **World root:** `<World:ContentDirectory>/help/`. It has no key of its own.
   - If the engine root is missing, that is a `Warning`. If the world root resolves to the same full path as the engine root, the world layer is skipped with a `Warning`.
   - `Server.csproj` and `Hedron.Web.csproj` copy `content/help/**` to `content/help/` in their output, so a published install resolves the same relative path.
7. **Merge (layering design note, R9).** The later root wins by id, and the topic is tagged `HelpTopicOrigin.WorldOverride`. A world file that is skipped never displaces the engine topic, so the engine version is served (the R9 edge case). When a skipped world file's name stem equals an engine id, the diagnostic says the engine version is served. The other origins are `Engine` and `World`.
8. **Authoring warnings (R10).** Each of these is a `Warning`, and none blocks anything:
   - a topic id or alias exactly equal to a registered verb name or verb alias (the message says "for administrators" when the verb is admin-only);
   - an alias equal to another topic's id: the alias is dropped and the id wins;
   - an alias shared by two topics: the ordinal-first topic id keeps it;
   - a duplicate id within one layer: the **ordinal-first file name** (`StringComparer.Ordinal`) wins and the later file is skipped;
   - a `seeAlso` entry that resolves to no topic id or alias.
9. **Registry snapshot (R13, INV-31).** `HelpTopicSet` is an immutable `DefinitionRegistry<string, HelpTopic>` subclass. Its rows are built once at construction, alongside its alias index and id-ordered list. `HelpTopicRegistry` (behind `IHelpTopicRegistry : IRegistry<string, HelpTopic>` plus `HelpTopicSet Current`) holds one `HelpTopicSet` reference, published with `Volatile.Write`. Readers never lock and always read one complete set. Before the first rebuild it holds `HelpTopicSet.Empty`.
10. **Index (R3).** Bare `help` (or an argument that is only whitespace or hyphens) writes the unchanged `HelpIndexMessage`. If the set is non-empty, it then writes `HelpTopicIndexMessage(Topics)` with `HelpTopicIndexEntry(Id, Title)` in ordinal id order. `commands` is unchanged.
11. **Topic output (R4).** A resolved topic writes `HelpTopicMessage(Id, Title, Body, SeeAlso)`. `SeeAlso` holds only the canonical ids of entries that resolve, and the telnet formatter renders `See also: help a, help b` only when that list is non-empty. Every public property is player-visible, which satisfies PT-1's agent-formatter rule. The source path and origin are **not** on this message. **Authored text is rendered literally.** The formatter escapes the title, body and ids before wrapping them in its own tags, so authored `<system>`/`<error>`/`<room-name>`/`<direction>` text never reaches `ApplyColor` as markup. Markup stays out of scope.
12. **Lookup (R2, R5, R6).** `IHelpLookupSystem.Resolve(HelpQuery, ISession)` reads `Current` once and returns the first tier with a visible match:
    1. exact verb or alias;
    2. exact topic id or alias;
    3. exact ability id or display name;
    4. verb prefix;
    5. topic prefix;
    6. ability prefix.

    The tiers have these rules:
    - For a single-token query, all six tiers apply as listed.
    - For a multi-word query, the whole normalized query first walks only the topic and ability tiers (2, 3, 5, 6). If nothing matches, the **first token alone** resolves through the verb tiers (1, 4). `help getting started` finds the topic, and `help kill orc` still shows `kill`, as today.
    - An ambiguous visible verb prefix returns `Ambiguous(Verb, names)`; an ambiguous topic prefix returns `Ambiguous(Topic, ids)`. Both stop the walk.
    - An ambiguous ability prefix stays `NotFound`, as today.
    - Invisible verbs are never matches.
    - `skills`/`spells`/`abilities` keep their appended catalog.
13. **Read-only (R7).** `help` publishes no event and keeps `UsableWhileIncapacitated = true`.
14. **Admin footer (R8).** When the invoker satisfies `AdminRequirement`, a topic result is followed by `HelpTopicOriginMessage(Origin)`, rendered `[engine topic]`, `[world topic]` or `[world topic — overrides engine]`. It is a separate message written only to admins, so the agent formatter never serialises an origin to a player.
15. **Engine topics (R11).** `content/help/` holds `getting-started`, `combat`, `death` (*Death & respawn*), `shops` (*Shops & money*) and `progression`. They load with zero errors and zero warnings against the real verb set. Their bodies contain no digit characters, which enforces the qualitative-tunables rule.
16. **Editor (R12).** `IHelpTopicAuthoring` and the `/help-topics` pages list every topic with its layer and origin, together with file-level errors. They create, edit and delete world topics, and show engine topics read-only with **Override**. They surface R9/R10 diagnostics and preview the player rendering. Writes:
    - go to the world root only, as `<id>.yaml`. When a topic was loaded from a differently named world file, that file is deleted after the new write succeeds.
    - run under the Help module's own `SemaphoreSlim(1,1)` write gate.
    - never touch the engine root.
17. **DI and layering.** Every host composes without a cycle. `HelpLookupSystem`, `HelpTopicReloadParticipant` and `HelpTopicAuthoring` take `Lazy<IVerbRegistry>`.
    - They only **read command metadata** (names, aliases, descriptions, usage, required privileges). They never execute an `ICommand`, and never hold one in a result.
    - That read is sanctioned doc-first (spec-review decision, Option A): `01-layers.md` §Cross-Layer Dependency Rules gains an Allowed row, "Domain systems → `IVerbRegistry`: read-only command metadata; never execute; always through `Lazy<>`", and `IVerbRegistry`'s XML doc says the same.
18. **Docs.** Help lives in `docs/features/help/`. Flows 01, 05 and 29 are updated, and the catalogs and skills carry the new rows (see [Flows](#flows-introduced-or-modified) and the per-WP **Docs** lists).

## Main flow

**A. Load and re-derive (boot, `reload`, web Apply)**
1. `WorldContentBootstrap` calls `LoadAndSpawnAsync`, or `ReloadCommand`/Apply calls `ReloadAsync`. The corpus is staged and validated. On a shape error the loader throws, and nothing below runs, so topics keep their snapshot.
2. On success the loader finishes spawning, runs `ResolveStartingRoom`, and then calls each `IContentReloadParticipant.RebuildAsync` in registration order.
3. `HelpTopicReloadParticipant` calls `IHelpTopicLayerReader.ReadLayers()`. It reads the engine root and then the world root through `YamlDefinitionDirectory<HelpTopicDocument>`.
4. The participant takes the verb tokens from `Lazy<IVerbRegistry>.Value.AllCommands` (names and aliases) and calls `HelpTopicSetBuilder.Build(layers, verbTokens)`. Build validates each file (R9), merges by id (Post. 7), checks collisions (R10), and resolves `seeAlso`. It returns a new `HelpTopicSet` and its diagnostics.
5. `HelpTopicRegistry.Publish(set)` swaps the snapshot in one reference.
6. The loader logs the diagnostics and, on reload, returns them in `ContentReloadResult.Diagnostics`. `ReloadCommand` then publishes `WorldContentReadyEvent`, writes its confirmation plus the diagnostic lines, and publishes `ContentReloadedEvent` (unchanged). Apply lists them.

**B. Player lookup**
7. A player sends `help getting started`. The dispatcher runs `HelpCommand`, which is allowed while incapacitated, and the command builds `HelpQuery.Parse(rest-of-line)`: raw `getting started`, topic key `getting-started`, multi-token.
8. `IHelpLookupSystem.Resolve(query, session)` captures `Current` once and walks the six tiers (Post. 12) with the visibility gate. Here it returns `HelpResolution.Topic(topic)`.
9. `HelpCommand` writes `HelpTopicMessage` and, for an admin, `HelpTopicOriginMessage`. No event is published.

**C. Authoring**
10. A designer opens `/help-topics` in `Hedron.Web`. `IHelpTopicAuthoring` lists, loads, previews, saves, overrides or deletes **world** topics under the Help write gate. A save takes effect on the next load: the Server's `reload` or restart, or the web host's Apply.

## Events fired

**None new.** Help stays read-only (R7). Participants return diagnostics instead of publishing. `WorldContentReadyEvent` and `ContentReloadedEvent` (and its `AdminAuditHandler` row) keep their payloads and order, and editor saves publish nothing (the catalog posture, [08-blazor](../architecture/08-blazor.md)).

## Systems / handlers involved

| Piece | Kind | Home | New / changed / reused | Shape |
|---|---|---|---|---|
| `YamlDocumentFile<TDto>`, `YamlDefinitionDirectory<TDto>`, `YamlReadResult<TDto>`, `YamlFileEntry`, `YamlCodecOptions`, `DefinitionValidation` | core infrastructure | `Core/Systems/Definitions/` | **new (branch B)** / reused (branch A) | PT-2 WP1 shapes verbatim, plus `Directory` and `Delete(path)` (Post. 1) |
| `IContentReloadParticipant`, `ContentDiagnostic`, `ContentDiagnosticSeverity` | core port | `Core/Systems/Definitions/` | **new** | Post. 2 |
| `WorldContentLoader` / `IWorldContentLoader` / `ContentReloadResult` | domain system | `Core/Modules/World/Systems/` | **changed** | optional participants ctor param; private `RunParticipantsAsync`; `Diagnostics` init property |
| `ReloadCommand` | Initiator | `Core/Modules/Admin/Commands/` | **changed** | appends diagnostic lines; refusal branch unchanged |
| `BalanceStandardsStore`, `SimScenarioStore`, `WorldManifestStore` | domain stores | as today | **changed (branch B)** | internals only; public interfaces unchanged |
| `HelpOptions` | options | `Core/Modules/Help/` | **new** | `EngineTopicDirectory` |
| `DataPaths.AddDataPaths` | composition | `Server/` | **changed** | `PostConfigure<HelpOptions>`: an **install-relative** location (`Resolve(root, Help:EngineTopicDirectory ?? "content/help")`), deliberately not under the data directory |
| `HelpTopic`, `HelpTopicOrigin`, `HelpTopicDocument` (YAML DTO), `HelpTopicDraft` (authoring input) | definition records | `Core/Modules/Help/` | **new** | `HelpTopic(Id, Title, Aliases, Body, SeeAlso /*resolved ids*/, Origin, SourceFile)` |
| `IHelpTopicLayerReader` → `HelpTopicLayerReader` | domain system (I/O) | `Core/Modules/Help/Systems/` | **new** | `IReadOnlyList<HelpLayerRead> ReadLayers()`; `HelpLayerRead(HelpLayer Layer, string Root, bool Exists, IReadOnlyList<HelpTopicFileRead> Files)`; `HelpTopicFileRead(string FileName, HelpTopicDocument? Document, string? Error)`; the roots are an ordered list (engine, world) |
| `HelpTopicSetBuilder` | core compute (pure, static) | `Core/Modules/Help/Systems/` | **new** | `HelpTopicBuildResult Build(IReadOnlyList<HelpLayerRead>, IReadOnlySet<string> verbTokens, IReadOnlySet<string> adminOnlyVerbTokens)` → `(HelpTopicSet Set, IReadOnlyList<ContentDiagnostic> Diagnostics)` |
| `HelpTopicSet` | immutable `DefinitionRegistry<string, HelpTopic>` | `Core/Modules/Help/Systems/` | **new** | `TryResolve(token, out HelpTopic)` (id or alias, case-insensitive); `PrefixMatches(prefix)` (distinct topics, id order); `Ordered`; `static Empty` |
| `IHelpTopicRegistry` → `HelpTopicRegistry` | core-tier lookup registry | `Core/Modules/Help/Systems/` | **new** | `IRegistry<string, HelpTopic>` delegating to `Current`; `HelpTopicSet Current`; `Publish(HelpTopicSet)` on the concrete type only |
| `HelpTopicReloadParticipant` | domain system (participant) | `Core/Modules/Help/Systems/` | **new** | deps: `IHelpTopicLayerReader`, `HelpTopicRegistry`, `Lazy<IVerbRegistry>` |
| `HelpQuery` | value | `Core/Modules/Help/Systems/` | **new** | `readonly record struct HelpQuery(string Raw, string TopicKey, bool IsSingleToken)`; `static Parse(string?)`; `IsEmpty` |
| `IHelpLookupSystem` → `HelpLookupSystem` | domain system | `Core/Modules/Help/Systems/` | **new** (extracted from `HelpCommand`) | `HelpIndex BuildIndex(ISession invoker)` → `HelpIndex(IReadOnlyList<VerbHelp> Verbs, IReadOnlyList<HelpTopic> Topics)`; `HelpResolution Resolve(HelpQuery, ISession invoker)`; deps: `Lazy<IVerbRegistry>`, `IAuthorizationChecker`, `IAbilityRegistry`, `IHelpTopicRegistry` |
| `HelpResolution` | result | `Core/Modules/Help/Systems/` | **new** | cases `Index`, `Verb(VerbHelp, HelpAbilityCatalog?)`, `Topic(HelpTopic)`, `Ability(AbilityDefinition)`, `Ambiguous(HelpMatchFamily, IReadOnlyList<string>)`, `NotFound`; `VerbHelp(Name, ShortDescription, LongDescription, Usage, Category, Aliases)`, which means the result never carries an `ICommand` |
| `HelpTopicRendering.ToMessage(HelpTopic)` | presentation helper | `Core/Modules/Help/` | **new** | the one mapping used by `HelpCommand` and the editor preview |
| `IHelpTopicAuthoring` → `HelpTopicAuthoring` | authoring domain system | `Core/Modules/Help/Systems/` | **new** | `List()`, `Load(id)`, `Preview(HelpTopicDraft)`, `Validate()`, `SaveAsync(draft, previousId?, ct)`, `OverrideAsync(engineId, ct)`, `DeleteAsync(id, ct)`, returning the reused `ContentWriteResult` (`Core/Modules/Authoring/`); has its own write gate |
| `HelpCommand` | Initiator | `Core/Modules/Help/Commands/` | **changed** | arg `topic` (`RestOfLine`, optional); deps `IHelpLookupSystem`, `IAuthorizationChecker`, `IAbilityRegistry` (catalog rendering only); gather → resolve → render |
| `CommandsCommand` | Initiator | as today | reused, unchanged | — |
| `HelpTopicMessage`, `HelpTopicIndexMessage`, `HelpTopicIndexEntry`, `HelpTopicOriginMessage` | typed output | `Core/Output/` | **new** | Post. 10/11/14; `OutputCategory.Help` |
| `TelnetOutputFormatter` | output | `Core/Output/` | **changed** | three new cases |
| `HelpModule.AddHelpModule` | DI | `Core/Modules/Help/` | **changed** | registers the above; `HelpTopicRegistry` concrete + interface forward; participant as `IContentReloadParticipant`; keeps both existing `Lazy<>` registrations |
| `CompositionRoot.Register` | DI | `Server/` | **changed** | `Configure<HelpOptions>(GetSection("Help"))` |
| `HelpTopics.razor` (`/help-topics`), `HelpTopicEditor.razor` (`/help-topics/edit/{Id?}`), nav link; `Apply.razor` | Blazor | `Hedron.Web/Components/` | **new** / Apply **changed** | the preview formats `HelpTopicRendering.ToMessage` through `TelnetOutputFormatter` with colour off |
| Handlers | — | — | **none new or changed** | — |

## Implementation plan — work packages

*The primary agent runs `architecture-reviewer` (code mode) across the combined diff once all three land. Build order: WP1 → WP2 → WP3.*

### WP1 — Shared YAML store: consume or extract (conditional on PT-2's merge state)

- **Start check:** does `Core/Systems/Definitions/YamlDefinitionDirectory.cs` exist on `master`?
- **Branch A (PT-2 WP1 merged):** consume as shipped. Add only `YamlDefinitionDirectory<TDto>.Directory` and `Delete(string path)` (Post. 1) with their tests (T2's delete cases). Leave the migrated stores alone.
- **Branch B (not merged, the expected case):**
  1. Add `Core/Systems/Definitions/{YamlDocumentFile.cs, YamlDefinitionDirectory.cs, YamlReadResult.cs, YamlFileEntry.cs, YamlCodecOptions.cs, DefinitionValidation.cs}` with **exactly** PT-2 WP1's signatures (this slice does not redesign them), plus the two additive members. YamlDotNet builders are created per call, because they are not thread-safe. Writes go through `AtomicFileWrite.ReplaceAsync`.
  2. **Characterize first, then migrate.** Before touching a store, add **new** characterization cases (T3b) that pin today's failure behavior. Existing cases stay untouched. Then migrate `BalanceStandardsStore` (single document), `SimScenarioStore` (directory; `Sanitize` moves into `WriteAsync`) and `WorldManifestStore` (single document, which retires slice 1's bounded debt). Each family's mapping, validation, warnings and public interface stay where they are. `Read` never throws, so each store maps `YamlReadResult` back to its current behavior:
     - **`BalanceStandardsStore.Load`:** `Exists = false` → today's defaults path. `Fault != null` → rethrow the original exception (`ExceptionDispatchInfo.Capture(fault).Throw()`), keeping today's YamlDotNet exception type and message. Codec: `OmitNulls = false`.
     - **`SimScenarioStore.Load`:** `Exists = false` → `throw new FileNotFoundException($"scenario file not found at '{path}'.")` (today's message). `Fault` → rethrow the original. An empty document → today's `InvalidOperationException("scenario YAML is empty.")`. `List` maps `YamlFileEntry` to `ScenarioFileSummary` and drops `Error`. Codec: `OmitNulls = false`.
     - **`WorldManifestStore.Read`:** already never throws. `Error` maps to today's `WorldManifestReadResult` error text. Codec: `OmitNulls = true`.
  3. Amend [`playtest-run-mode.md`](playtest-run-mode.md) WP1 so branch A ("consume") is now its live case. Copy in the two additive members, the `YamlFileEntry`/`YamlCodecOptions` definitions and `YamlReadResult.Fault` verbatim from Post. 1, so both plans carry one contract.
- **Files:** `Core/Systems/Definitions/*`. Branch B also touches `Core/Modules/BalanceInspection/Standards/BalanceStandardsStore.cs`, `Core/Modules/Simulation/Systems/SimScenarioStore.cs`, `Core/Modules/World/Systems/WorldManifestStore.cs`, and `Hedron.Tests/Systems/Definitions/{YamlDocumentFileTests.cs, YamlDefinitionDirectoryTests.cs}`.
- **Docs (branch B):**
  - `reference/systems.md` rows for the shared types and the three migrated stores;
  - the backlog "YAML-authored definition pipeline" entry: the extraction landed in help-topics, the manifest debt is closed, and Ability/Effect coexistence is still open;
  - the program's slice-4 row;
  - flows 01 and 33, where they name a store's hand-rolled YAML load (no behavior change);
  - skills `add-core-system`/`add-domain-system`: "a new YAML family builds on `Core/Systems/Definitions/`".
- **Depends on:** slice 1 (manifest migration only).
- **Out of scope:** layering (WP2); the participant port (WP2); any store semantics change; scenario `SaveAsync`/editor changes.
- **Exit criterion:** T1–T4 and T3b green (T3b written and green *before* the migration commit). In branch B, `BalanceStandardsStoreTests`, `SimScenarioStoreTests` and `WorldManifestStoreTests` are green with **no edits to their existing cases** (verify with `git diff --stat` on those files). Build and `dotnet test` green. Manual check: the Standards and World pages load and save as before.

### WP2 — Re-derivation port + topic family + engine topics

- **Scope:**
  1. Port: `IContentReloadParticipant`, `ContentDiagnostic`, `ContentDiagnosticSeverity` (Post. 2).
  2. `WorldContentLoader`: an optional `participants` constructor parameter and `RunParticipantsAsync` at the end of both `LoadAndSpawnAsync` and `ReloadAsync`, after `ResolveStartingRoom`. It catches per participant, logs, and folds results into `ContentReloadResult.Diagnostics` (Post. 3–4). `ReloadCommand` writes the diagnostic lines (`PlainMessage`, `OutputSeverity.System`, `OutputCategory.System`, `  [warning] world/kill.yaml: …`), and `Apply.razor` lists them.
  3. `HelpOptions` + `Configure` in `CompositionRoot.Register` + `PostConfigure` in `DataPaths` (install-relative). Add `<None Include="..\content\help\**\*.yaml" Link="content\help\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />` (or the equivalent) to `Server.csproj`. `Hedron.Web` and `Hedron.Tests` reference `Server`, so the copied item should flow into their output. Check `Hedron.Web`'s build output, and add a second rule to `Hedron.Web.csproj` **only if** the files are missing.
  4. The topic family: `HelpTopic`, `HelpTopicOrigin`, `HelpTopicDocument`, `HelpTopicLayerReader`, `HelpTopicSetBuilder`, `HelpTopicSet`, `HelpTopicRegistry`, `HelpTopicReloadParticipant`, plus their DI in `HelpModule` (Post. 5–9, 17).
  5. The engine content: `content/help/{getting-started,combat,death,shops,progression}.yaml`, written for R11 (generic, qualitative, no digits). Each article names only verbs that **exist when this slice merges** (today: `kill`, `flee`, `list`/`buy`/`sell`, `score`/`progress`), so `seeAlso` and the walker's `help` step have real targets. `consider` isn't a command yet. Slice 5 (`relative-power-consider`) adds a mention of it to the `combat` article as part of its own docs and content step. If slice 5 merges first, this slice mentions it. Add a `help/` row to `content/README.md`.
- **Files:**
  - `Core/Systems/Definitions/{IContentReloadParticipant.cs, ContentDiagnostic.cs}`;
  - `Core/Modules/World/Systems/{WorldContentLoader.cs, IWorldContentLoader.cs}`, `Core/Modules/Admin/Commands/ReloadCommand.cs`;
  - `Core/Modules/Help/{HelpOptions.cs, HelpTopic.cs, HelpTopicOrigin.cs, HelpTopicDocument.cs, HelpModule.cs}`, `Core/Modules/Help/Systems/{IHelpTopicLayerReader.cs, HelpTopicLayerReader.cs, HelpLayerRead.cs, HelpTopicSetBuilder.cs, HelpTopicSet.cs, IHelpTopicRegistry.cs, HelpTopicRegistry.cs, HelpTopicReloadParticipant.cs}`;
  - `Server/{DataPaths.cs, CompositionRoot.cs, Server.csproj}`, `Hedron.Web/{Hedron.Web.csproj, Components/Pages/Apply.razor}`;
  - `content/help/*.yaml`, `content/README.md`;
  - tests under `Hedron.Tests/{Help, World, Composition, Architecture, Admin}/`.
- **Docs:**
  - flow-01 (participants at the end of the content-load leg; diagnostics are logged in the boot report);
  - flow-05 (a new step between 7 and 8 for participants, so the result carries diagnostics and the refusal branch runs none);
  - flows `README.md` rows for 01 and 05 ("extended in help-topics");
  - `features/world/world-content.md` (the participant phase and the `help/` subdirectory being reserved, which the loader never reads as world content);
  - `reference/systems.md`: the `WorldContentLoader` row, the port, `HelpTopicLayerReader`, `HelpTopicRegistry`, `HelpTopicReloadParticipant` and `HelpTopicSetBuilder`;
  - **layering (spec-review Option A):** `architecture/01-layers.md` §Cross-Layer Dependency Rules gains the Allowed row "Domain systems → `IVerbRegistry` (read-only command metadata; never execute; always through `Lazy<>`)". `Core/Commands/IVerbRegistry.cs` gains the matching XML doc. `add-domain-system` names `Lazy<IVerbRegistry>` as the sanctioned read path, under its layering rule rather than only as a DI note;
  - **data locations:** `architecture/05-configuration.md` §"Data locations — one rule" gains the install-relative exception (`Help:EngineTopicDirectory`, engine-owned, resolved against the Hedron root, never under `Paths:DataDirectory`) and the new key;
  - **committed content:** in `README.md`, the committed-content line names `content/help/` (the engine help layer), and the config table gets the key. In `CLAUDE.md` (user sign-off at spec review, 2026-10-31), the project-layout "Content" line names `content/help/` as the generic engine help layer beside `dev-world/` and the profiles. Its "content a slice needs goes in `content/dev-world/`" line notes that engine help topics go in `content/help/`;
  - `reference/commands.md`: the `reload` row gains diagnostics;
  - the `DataPaths` XML doc and the [content ownership](../features/world/world-content.md#content-ownership--the-engine-ships-no-game-world) policy: `content/help/` is committed **engine** content, an install artifact, never operator-owned;
  - skills `add-core-system`/`add-domain-system`: "a re-derivable definition family implements `IContentReloadParticipant`". In `add-core-system`'s "Content I/O: fail-fast shape errors" section, add that participants run **after** the swap, are **warn-only**, and **never refuse**. They are the second content-I/O posture beside staged fail-fast;
  - skill `add-command`: a new verb whose name or alias equals a help topic id or alias shadows that topic (an R10 warning). T11 fails if a new verb collides with an engine topic, so rename one of them;
  - amend the backlog "`reload` skips registry validation" entry. Help topics now validate on every reload through their participant; area affinities are still open. **Why the entry's "next touched" trigger doesn't fire here:** participants run after the swap and only report. They cannot refuse a reload, so running `ValidateRegistry` against the *staged* registry, which can refuse, is still a separate change.
- **Depends on:** WP1.
- **Out of scope:** lookup, `HelpCommand` and output (WP3); the editor (WP3); migrating the manifest or standards into participants; a dev-world help overlay.
- **Exit criterion:** T5–T12 green. Build and test green. Manual check: `DOTNET_ENVIRONMENT=DevWorld dotnet run --project Server` logs zero help diagnostics, and `reload` as admin shows the unchanged confirmation with no diagnostic lines.

### WP3 — Player surface + topic editor + docs split

- **Scope:**
  1. `HelpQuery`, `HelpResolution`, `VerbHelp`, `HelpIndex`, and `IHelpLookupSystem` → `HelpLookupSystem` (Post. 12). This ports today's exact-then-prefix, visibility and ability-fallback behavior into tiers 1, 3, 4 and 6.
  2. `HelpCommand` becomes thin: `RestOfLine` arg `topic`, updated `Usage`/`LongDescription`/`ShortDescription`, rendering per resolution (Post. 10–14). It keeps `FormatAbilityHelp` and the catalog appender as presentation, and drops its direct `IVerbRegistry`/`IEnumerable<ICommand>` dependencies.
  3. `HelpTopicMessage`, `HelpTopicIndexMessage`, `HelpTopicIndexEntry`, `HelpTopicOriginMessage`, the `TelnetOutputFormatter` cases (title as `<room-name>[Title]</room-name>`, then the body, then the see-also line; the index as `=== Topics ===` with `  id — title` rows), and `HelpTopicRendering`.
  4. `IHelpTopicAuthoring` → `HelpTopicAuthoring` (Post. 16) and the two Blazor pages plus the nav link.
     - The list page shows id, title, origin, file(s), and a status of ok / skipped / warnings, plus file-level errors and a "New topic" action.
     - The editor page has id, title, aliases (comma list), seeAlso (comma list), body (textarea) and a live preview. Engine topics are read-only with **Override**. World topics have **Save** and **Delete**. The page states that changes apply on `reload`/Apply.
- **Files:**
  - `Core/Modules/Help/{HelpTopicDraft.cs, HelpTopicRendering.cs, HelpModule.cs, Commands/HelpCommand.cs}`, `Core/Modules/Help/Systems/{HelpQuery.cs, HelpResolution.cs, IHelpLookupSystem.cs, HelpLookupSystem.cs, IHelpTopicAuthoring.cs, HelpTopicAuthoring.cs}`;
  - `Core/Output/{HelpTopicMessage.cs, HelpTopicIndexMessage.cs, HelpTopicIndexEntry.cs, HelpTopicOriginMessage.cs, TelnetOutputFormatter.cs}`;
  - `Hedron.Web/Components/{Pages/HelpTopics.razor, Pages/HelpTopicEditor.razor, Layout/MainLayout.razor}`;
  - tests under `Hedron.Tests/Help/`.
- **Docs:**
  - **The feature-folder split** (under the `manage-docs` skill): create `docs/features/help/help.md` (the holistic feature doc). Move `communication/help-system.md` to `help/help-system.md` and rewrite it for the lookup system and the tiers. Add `help/help-topics.md` as the topic family's design doc: layers, validation, the participant, authoring. Drop Help from `communication/communication.md` (lines 34 and 40) and link out to it, and update `features/README.md` (row 28). Re-point every other inbound link too: `reference/commands.md`, the program doc, and the historical backlog line. Before moving, grep for `help-system.md` so the list is complete; the planner's earlier count of three was low.
  - **Checklist wording (user decision at spec review):** INV-17 and INV-27 in `architecture/checklist.md` still say a flow's diagram lives in `flows/README.md` ("body *and* mermaid"). Flows are now one file each (`flows/flow-NN-*.md`), with README as the index. Reword both to match. This is wording only, with no rule change. Check that `09-documentation.md` agrees.
  - `features/admin-authoring/content-tooling.md` gets an `IHelpTopicAuthoring` section, and `content-authoring.md` gets the Help topics page.
  - flow-29 gets a new **B5 — Help topics page** leg, and flows `README.md` row 29 is updated.
  - `features/output/output.md` gets the four messages, with the admin-only footer rationale.
  - `reference/systems.md` gets the `HelpLookupSystem` and `HelpTopicAuthoring` rows, and `reference/commands.md` gets the `help` row (topic argument, rest of line, tiers).
- **Depends on:** WP2.
- **Out of scope:** search; per-topic privilege; tab completion; converting the ability help block to a typed message; the authoring JSON API (no topic endpoints, the same as the manifest).
- **Exit criterion:** T13–T18 green. Build and test green. Manual telnet smoke on dev-world:
  - `help` shows the Topics block;
  - `help getting started` shows the article;
  - admin `help combat` shows `[engine topic]`;
  - `help lo` still shows `look`.

  Manual web smoke: override `combat`, Apply, and `help combat` in a fresh `Hedron.Web` preview shows `[world topic — overrides engine]`. Delete the override and the engine version returns.

## Content tooling impact

- **Data-file shape (new):** `<root>/<id>.yaml` with `id`, `title`, `aliases?`, `seeAlso?`, `body` (Post. 5). There are two roots (Post. 6). The engine root is committed and edited by hand in PRs. The world root holds operator/designer content.
- **Editor (new):** `/help-topics` and `/help-topics/edit/{Id?}` in `Hedron.Web` (R12, INV-18). This is the authoring path for world topics and the inspection path for both layers.
- **In-game inspection (new):** `help <id>` plus the admin origin footer (R8). There are no in-game write verbs (resolved decision 2).
- **Reload feedback (changed):** `reload` and web Apply list participant diagnostics, and the boot log carries them.
- **Config (new):** `Help:EngineTopicDirectory`, default `content/help`, resolved against the Hedron root.
- **`TemplateRegistry`:** no entries. Topics are definitions, never templates or entities (INV-12, INV-23).
- **Authoring JSON API:** unchanged, with no topic endpoints. The API covers template kinds only, and the manifest has none either.

## Cross-cutting surfaces stressed

| Surface | Classification | Notes |
|---|---|---|
| Commands | **Adequate** | `RestOfLine` covers multi-word topics. The visibility gate reuses `IAuthorizationChecker`, and `UsableWhileIncapacitated` is unchanged. |
| Output | **Adequate** | Four new typed messages plus formatter cases (INV-11). The ambiguity prompts stay `PlainMessage`, as the existing verb prompt is. **PT-1 agent-formatter rule:** every public property of `HelpTopicMessage`/`HelpTopicIndexMessage` is player-visible, and the origin is a separate admin-only message. No `[JsonIgnore]` is needed, and `SourceFile` never reaches a message. |
| YAML definition store | **Gap exposed → closed in-slice** (WP1 branch B) or already closed by PT-2 (branch A) | The ≥3 trigger (INV-19). It is satisfied exactly once across the two plans. |
| Content re-derivation lifecycle | **Gap exposed → closed in-slice** (WP2) | The participant port (advisor resolved decision 1). There is one consumer. The manifest, standards and Ability/Effect are shaped for later and are not migrated. |
| Registry validation on reload | **Acknowledged debt (existing backlog entry, amended)** | Help topics now validate on every reload. Area affinities and `ValidateRegistry` warnings stay boot-only, under the existing entry. |
| Configuration / data paths | **Adequate (extended)** | `DataPaths` stays the one rule. It gains its first *install-relative* location: engine-owned, committed, resolved against the Hedron root so that source runs and installs agree, and never under `Paths:DataDirectory`. The same `Register` path serves Server, `Hedron.Web`, the run modes and PT-1 worlds. |
| Content templates / ECS / persistence | **Adequate (not touched)** | **Persistence opt-in audit:** L1, no entity construction path. L2, no component added or touched. L3, no `SaveEntityAsync` call (INV-22/23). |
| Event bus | **Adequate** | No events. Existing payloads are unchanged. |
| Time / randomness (INV-26) | **Adequate** | Lookup and build are deterministic. No seam is needed. |
| Sessions | **Adequate** | `ISession` is passed to the lookup only for the visibility check. |
| Modules / DI | **Adequate** | `AddHelpModule` grows. The `IVerbRegistry` cycle is broken by the existing `Lazy<IVerbRegistry>` registration for all three new consumers, and the DI-smoke guard (T12) covers it. **Layering:** domain systems reading the initiator-tier dispatcher's metadata is sanctioned doc-first in `01-layers.md` (Post. 17, Option A). |
| Concurrency (INV-31) | **Adequate, posture declared** | **Shared singleton state:** `HelpTopicRegistry`, *guarded by single-reference publication*. Readers lock-free. Concurrent rebuilds from two `reload`s are each complete, and the last publish wins. The last publisher may hold the *older* disk read, so the snapshot is complete but possibly stale until the next load. That is the same posture as today's loader, which doesn't serialize reloads. **Authoring:** `HelpTopicAuthoring`, *guard* (`SemaphoreSlim(1,1)`, `*Core` re-entrancy pattern); last save wins across circuits. **Web-host call path:** editor circuits read disk, not the registry. Server and web are separate processes, so edits reach the server on its `reload`. **Participants:** run on the loader caller's thread (bootstrap, the `reload` session thread, the Apply circuit) — *confined*. No new background thread; no live-world component touched. |
| Content tooling (INV-18) | **Adequate** | Editor plus footer plus reload feedback, all in this slice. |

## Flows introduced or modified

- **[Flow 01 — Server startup](../architecture/flows/flow-01-server-startup.md)** (WP2): the `WorldContentBootstrap` leg ends with "the loader runs content reload participants (help topics) and logs their diagnostics". The bootstrap order is unchanged.
- **[Flow 05 — Content reload](../architecture/flows/flow-05-content-reload.md)** (WP2): a new step after "re-resolve the starting room", in which participants rebuild and return diagnostics. Step 9's result gains `Diagnostics`, and the confirmation lists them. The refusal branch states that participants do not run. The mermaid's clean-read branch gains `WCL->>Parts: RebuildAsync` before `ContentReloadResult`.
- **[Flow 29 — Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md)** (WP3): a new **B5 — Help topics page** leg (list → edit → preview → save/override/delete under the Help write gate → applies on `reload`/Apply). The Apply step mentions diagnostics.
- **Flows 01/33 wording (WP1 branch B only):** cite the shared store wherever a hand-rolled store load is named. There is no behavior change.
- **[Flow 03 — Command journey](../architecture/flows/flow-03-player-command-lifecycle.md): not modified.** The help lookup is one command body with no cross-system leg, so it is documented in `features/help/help-system.md`. No new flow is warranted: no recurring multi-system journey is introduced.

## Test plan / Verification

| # | Tier | Target | Asserts | Post. |
|---|---|---|---|---|
| T1 | system-unit (temp dir) | `YamlDocumentFileTests` (branch B) | missing file → `Exists=false`, no error; malformed → `Error` plus the original exception in `Fault`, no throw; write → read round-trip; null omission when opted in | 1 |
| T2 | system-unit (temp dir) | `YamlDefinitionDirectoryTests` | `List` is ordinal with filename fallback, and unparseable files are listed; `Read`; `WriteAsync` sanitizes and is atomic; `Delete` removes a file, returns `false` for an absent one, and **throws `ArgumentException` for a path outside `Directory`** (the cases in both branches) | 1 |
| T3 | existing suites | `BalanceStandardsStoreTests`, `SimScenarioStoreTests`, `WorldManifestStoreTests` (branch B) | green with existing cases unmodified | 1 (R15) |
| T3b | system-unit (temp dir), **new cases, written before migration** | the same three suites (branch B) | malformed standards YAML → the same exception type and message as today; missing scenario → `FileNotFoundException` with today's message; malformed scenario → the same exception type; empty scenario → `InvalidOperationException("scenario YAML is empty.")`; `List` with an unparseable file → listed under its filename name; serialized output byte-equal to today's for a fixed document (standards, scenario, manifest: null omission only on the manifest) | 1 (R15) |
| T4 | architecture-guard (source scan) | `SharedYamlStoreGuardTests` | no `new DeserializerBuilder(`/`new SerializerBuilder(` in the three stores or under `Core/Modules/Help/` | 1 |
| T5 | system-unit | `HelpTopicSetBuilderTests` | valid parse; missing id/title/body → `Error` and skipped; invalid id → `Error`; uppercase id normalized; invalid alias dropped with a `Warning`; world overrides engine (`WorldOverride`); world-only → `World`; engine-only → `Engine`; **broken world override → engine served** plus a diagnostic naming it; duplicate id in a layer → ordinal-first file wins plus a `Warning`; alias equal to another id → dropped plus a `Warning`; alias shared → ordinal-first id keeps it; id/alias equal to a verb → `Warning` (admin-only wording); unresolved `seeAlso` → `Warning` and excluded; `seeAlso` given as an alias → canonical id; `Ordered` in ordinal id order | 5, 7, 8, 11 |
| T6 | system-unit | `HelpTopicRegistryTests` | `Empty` before the first publish; `Publish` swaps the whole set; a captured `Current` is unaffected by a later `Publish`; `IRegistry` members delegate to `Current` | 9 (R13) |
| T7 | system-unit (temp dir) | `HelpTopicReloadParticipantTests` | reads both roots in order; missing engine root → `Warning`, world-only set; world root equal to engine root → world skipped plus a `Warning`; `Source` is `<layer>/<file>`; malformed files never throw | 3, 6 |
| T8 | flow (temp dir, real loader) | `WorldContentLoaderParticipantTests` | `LoadAndSpawnAsync` runs participants after the starting room is resolved (the recording participant sees `WorldConfiguration.StartingRoom` set); `ReloadAsync` returns their diagnostics; a refused reload runs none and leaves the snapshot; a throwing participant → one `Error`, load succeeds; no participants → empty `Diagnostics`; `default(ContentReloadResult).Diagnostics` is empty; **two recording participants run in registration order**; **a capturing logger sees warnings at Warning, errors at Error, in one block per load** (the R9 boot report) | 2, 3, 4 |
| T9 | flow | `WorldContentReloadTests` (extended) | `reload` output appends one line per diagnostic after the unchanged confirmation; `ContentReloadedEvent` payload unchanged | 4 |
| T10 | system-unit | `DataPathsTests` (extended) | `Help:EngineTopicDirectory` absent → `<root>/content/help`; relative → against the root; absolute → as given; setting `Paths:DataDirectory` does **not** move it | 6 |
| T11 | flow (committed content) | `EngineHelpTopicsTests` | the real `content/help/` builds against the real composed verb set with **zero** diagnostics; contains the five R11 ids and titles; no body contains a digit | 15 |
| T12 | architecture-guard | DI smoke (extended, `ArchitectureGuardTests`) | one `Register` provider resolves `IEnumerable<ICommand>`, `IVerbRegistry`, `IEnumerable<IContentReloadParticipant>`, `IWorldContentLoader`, `IHelpLookupSystem` and `IHelpTopicAuthoring` without a cycle | 17 |
| T13 | system-unit | `HelpQueryTests` | whitespace runs → hyphen; case-insensitive; `getting started` = `Getting-Started` = `getting-started`; only whitespace or hyphens → `IsEmpty`; `IsSingleToken`; **one pair of surrounding quotes is stripped** (`"fire bolt"` → `fire bolt`, matching today's `ReadToken` unquoting, so `RestOfLine` doesn't regress quoted input) | 10, 12 (R2) |
| T14 | system-unit | `HelpLookupSystemTests` | each of the six tiers; **exact topic beats verb prefix**; exact verb beats exact topic; an admin-only verb is invisible to a player, so the same-named topic resolves; ambiguous verb prefix → `Ambiguous(Verb)` with visible candidates only; ambiguous topic prefix → `Ambiguous(Topic)`; multi-word walks topic/ability first (`help getting started` → topic) and then falls back to a first-token verb (`help kill orc` → `kill`); a multi-word topic beats a verb named by its first token; ability exact by id and by display name; ambiguous ability prefix → `NotFound`; `skills`/`spells`/`abilities` → catalog scope; one `Current` per resolve (a publish mid-test doesn't tear the result); `BuildIndex` visibility and order. *(On-touch ratchet for the previously untested `HelpCommand` lookup.)* | 10, 12 (R5, R6) |
| T15 | flow (harness session) | `HelpCommandFlowTests` | bare `help` → `HelpIndexMessage` then `HelpTopicIndexMessage`; no topics → no topic message; topic → `HelpTopicMessage` with resolved `SeeAlso`; admin → `HelpTopicOriginMessage` follows; player → none; no event published; still runs while incapacitated | 10, 11, 13, 14 (R7, R8) |
| T16 | system-unit | `TelnetOutputFormatter` help-topic cases | the three footer strings exactly; the see-also line omitted when empty; the Topics block omitted when empty; **a body or title containing `<error>x</error>` renders literally, with no colour codes** | 11, 14 |
| T17 | system-unit (temp dirs) | `HelpTopicAuthoringTests` | `List` shows origin and file errors; `SaveAsync` writes `world/<id>.yaml` only; an invalid draft is refused with nothing written; R10 warnings are returned; `OverrideAsync` copies an engine topic and refuses if a world copy exists; deleting an override → `List` shows the engine version; renaming on save deletes the old file; **the engine directory is byte-identical after every operation**; concurrent saves are serialized (the catalog's write-serialization test pattern) | 16 |
| T18 | system-unit | `HelpTopicRendering` | `ToMessage` carries only the resolved `SeeAlso`, and no origin or path | 11 |

**Skipped, with reasons:**
- the exact prose of the engine articles (content, reviewed in PR; T11 guards shape and the digit rule);
- the Blazor page markup and interaction (no bUnit tier; manual smoke in the WP3 exit criterion);
- `FormatAbilityHelp` and the catalog text (pre-existing presentation, unchanged);
- `CommandsCommand` (unchanged);
- the csproj copy rule (build plumbing; T10 covers resolution, and installs are smoke-tested on publish);
- `HelpOptions`, `HelpTopicDocument`, `HelpTopicDraft` (pure data).

## Design notes

*(Durable seam rationale. These survive disintegration into the living docs and the `completed/` record.)*

- **Lookup is a decision, so it lives in a system.** R5's six-tier resolution (exact, then prefix, each verb → topic → ability, with visibility gating at every tier) is a game-facing rule, not argument parsing.
  - It moves out of `HelpCommand` into a domain **`IHelpLookupSystem`** in `Core/Modules/Help/Systems/`. The system returns a **resolution result**: a verb entry, a topic plus its origin, an ability, an ambiguous match with its candidates, or nothing.
  - `HelpCommand` gathers the query and the invoker's visibility, calls the system, and renders the result (INV-8). Today the command body *is* the lookup. Keeping it there while adding three tiers would push it well past "thin".
  - **Shaped for later:** tab completion (the help-system doc already names `IVerbRegistry.GetPrefixCandidates` as its seam) and horizon search both consume the same resolution, so neither re-implements the tier order.
- **Topics are a reloadable `DefinitionRegistry` family over the shared YAML store.** `IHelpTopicRegistry` (string-keyed, Spine F) holds one immutable snapshot of the merged topic set, swapped atomically on rebuild (INV-31: guarded by single-reference publication; readers never lock).
  - This is the **first** `DefinitionRegistry` family to be YAML-backed and to re-derive at runtime. The base's instance-held, reload-shaped rows were built for exactly this ([reference/systems.md](../reference/systems.md#definitionregistry--iregistry-generic-registry-infrastructure)), so this slice sets the precedent the backlogged Ability/Effect YAML pipeline follows.
  - Topics are world-content definitions: YAML only, never entities (INV-12, INV-23).
- **Layering: ordered roots, override by id. Shaped for later, not general now.** The merge of the engine root then `<ContentDirectory>/help/` (later root wins by id, with a broken later entry falling back to the earlier one per R9) lives in Help. It is written over an **ordered list** of `YamlDefinitionDirectory` roots keyed by string id, not hard-coded to "engine + world".
  - **Promotion trigger:** a second family wants committed-plus-world layering. Candidates: socials, PT-2's committed-plus-operator scenario roots, or Ability/Effect, although Ability/Effect is compiled-plus-YAML and may not fit. When that happens the merge moves to `Core/Systems/Definitions/` as a move, not a rewrite. One consumer today is below the INV-19 bar.
- **Content re-derivation enters through a reload-participant port.**
  - **The seam:** a small, DI-collected port (working name `IContentReloadParticipant`) that the world content loader invokes **after a successful load or reload, in every host** (the telnet server, the run modes, and `Hedron.Web`, which composes no bus subscriptions). Each participant rebuilds its own snapshot and **returns diagnostics** (errors and warnings, per file), which the loader folds into the boot report and the `reload` result. That is how R9's "warnings in the reload output" is met without the reload path knowing about Help.
  - **Why a port rather than a handler:** an event handler on `WorldContentReadyEvent` cannot return diagnostics to the command's output, and it would not run in the web host. A direct call from World or Admin into Help would point the dependency the wrong way, and the next family would have to repeat it.
  - **Mechanism versus consequence:** the loader owns *when* content re-derives. Each family owns *how* its definitions rebuild.
  - **Shaped for later:**
    - the world manifest's resolution (today special-cased at the end of `ReloadAsync`) can become a participant;
    - the standards registry's restart-to-apply (backlog) can become live through it;
    - YAML-backed Ability/Effect registries re-derive through it.
  - None of those migrate in this slice.
  - **This also closes the "`reload` skips registry validation" gap for help topics** ([backlog](../roadmap/backlog.md)): the topic family validates, including the R10 verb-collision warnings, on every boot *and* every reload, because validation is part of its rebuild.
- **Help never gates the world.** A participant's diagnostics are reported but never refuse a load (R9). If a world reload is refused for shape errors, participants don't run, so topics stay on their previous snapshot, consistent with flow-05's whole-or-nothing posture.

**Planner refinements** *(added at planning; they refine the notes above without reversing any of them)*:
- **The snapshot *is* the `DefinitionRegistry`.** Each rebuild constructs a new immutable `HelpTopicSet : DefinitionRegistry<string, HelpTopic>`. It carries its rows loaded once at construction, exactly as the base intends, plus its alias index and id order. `HelpTopicRegistry` swaps one reference to the whole set.
  - **Why not a `Reload(rows)` on the base:** aliases and origins must swap in the same reference as the rows, or a reader could see new rows with old aliases.
  - The base stays unchanged. Ability/Effect can still add `Reload(rows)` to it later, if id-keyed rows are all they need.
- **The participant orchestrates; the registry stays dumb.** The participant does the reading (layer reader), gathers the verb tokens (`Lazy<IVerbRegistry>`), and runs the pure decision (`HelpTopicSetBuilder`) before publishing. As a result the registry has no `IVerbRegistry` dependency, and the editor reuses the same reader and builder for its listing, preview and validation. Boot, reload and the editor therefore apply one rule set.
- **Disk is the editor's truth; the registry is the game's truth.** The editor never reads `IHelpTopicRegistry`, which reflects the last load. It re-reads the layers on each call. That is why a save can show its warnings immediately while taking effect in-game only on `reload`/Apply.
- **Multi-word queries: topic first, then a first-token verb fallback** *(user decision at planning, 2026-10-31; replaces the planner's "verb tiers are single-token" call)*. A verb is one token. Letting `help get started` match the exact verb `get` would shadow every multi-word topic whose first word is a verb. So a multi-word query first walks the topic and ability tiers with the whole normalized query (abilities match on their raw display name, e.g. `help fire bolt`). Only if nothing matches does its first token resolve through the verb tiers. That keeps today's `help kill orc` → `kill` behavior, so there is no regression. Rejected: skipping the verb tiers entirely for multi-word input, which would make `help kill orc` report no help found.
- **The digit ban stays** *(user decision at planning)*. T11 rejects any digit in an engine topic body, as a mechanical guard for R11's qualitative-tunables rule. Revisit it if an article genuinely needs a numeral.
- **The lookup result carries `VerbHelp`, not `ICommand`.** The domain system reads command *metadata* through `IVerbRegistry` and never executes a command. The renderer therefore gets plain data, and nothing above the system layer leaks into its result.

## Architecture brief

*(In-flight. Trimmed on ship.)*

### Seams and homes

| Seam | Kind | Home / layer | Disposition |
|---|---|---|---|
| Shared YAML store (`YamlDocumentFile`, `YamlDefinitionDirectory`, `YamlReadResult`, `DefinitionValidation`) | Core infrastructure | `Core/Systems/Definitions/`; the shapes are fixed by [PT-2 WP1](playtest-run-mode.md) | **Build now if first to land, else consume** (R14); migrate standards, scenarios and manifest with no visible change (R15) |
| Topic DTO + model (`HelpTopic`: id, title, aliases, body, seeAlso) | Definition record | `Core/Modules/Help/` | **Build now** |
| `IHelpTopicRegistry` | String-keyed `DefinitionRegistry` family, snapshot-swapped | `Core/Modules/Help/Systems/` (core-tier lookup infrastructure, like `AbilityRegistry`) | **Build now** |
| Layered merge over ordered roots | Family-local composition | `Core/Modules/Help/` | **Shape for later** (promotion trigger above) |
| Reload-participant port | DI-collected port, invoked by the loader; returns diagnostics | Port owned beside the world content loader or in `Core/Systems/Definitions/` (planner); Help implements it | **Build now** (one participant; the manifest, standards and Ability/Effect are shaped for later) |
| `IHelpLookupSystem` | Domain system: query → resolution result | `Core/Modules/Help/Systems/` | **Build now** (extracted from `HelpCommand`) |
| `HelpCommand` | Thin initiator: gather → lookup → render | Existing | **Modify** (on-touch ratchet: lookup and command tests) |
| `HelpTopicMessage` (+ the Topics block in `HelpIndexMessage`) | Typed output | `Core/Output/` | **Build now** (INV-11, INV-19 output format) |
| Admin origin footer | Output, privilege-gated in the command | Rendered only for admin invokers | **Build now**. It must not be a plain public property on a player-visible message, because PT-1's agent formatter serialises every public property ([playtest-world-substrate](playtest-world-substrate.md)). Use a separate admin-only message or `[JsonIgnore]` |
| Topic authoring (`IHelpTopicAuthoring`: list with layer/origin, load, preview, validate, save, delete, override) | Authoring domain system | Help module or the `IContentDefinitionCatalog` family (planner); writes the world layer only, never the engine root | **Build now** (INV-18) |
| Topic editor page | Blazor | `Hedron.Web/Components/Pages/` | **Build now** |
| Engine topic set (5 articles) | Committed engine content | An install artifact copied to output, not under `Paths:DataDirectory`, never written by tooling | **Build now** (location: Open questions) |

### Family dispositions

- **YAML definition families:** the store is general now (the ≥3 trigger, shared with PT-2). The layering is shaped for later. Ability/Effect coexistence stays in its backlog entry, which now has two precedents to follow: the store, and the reload-participant re-derivation.
- **Re-derivable content:** the participant port is general in shape with one consumer. Manifest, standards and Ability/Effect are shaped for later. No backlog entry is added: each is a migration onto an existing seam, and its own slice decides when.
- **Help lookup:** the system result is shaped for tab completion and search (both on the horizon or backlog already).

### Observers, contributors, ordering

- **No new events.** Help is read-only and fires nothing (R7). Topic rebuilds return diagnostics rather than publishing. The existing `ContentReloadedEvent` audit is unchanged. Editor saves publish nothing, following the established catalog posture ([08-blazor.md](../architecture/08-blazor.md)).
- **No contributors** (INV-24 is not involved).
- **Ordering:** participants run after the world corpus is swapped and spawned (and after starting-room resolution), and before `WorldContentReadyEvent` is published, so a reload's confirmation can include their diagnostics. Help needs no world entities. The ordering only makes the report complete, and puts it outside the refused branch.

### Invariants in tension

- **INV-8:** `HelpCommand` must shrink to gather/call/render once the lookup tiers move into `IHelpLookupSystem`.
- **INV-1 / INV-15, layer direction and DI cycle:** the R10 verb-collision check (in the participant and in authoring) and the lookup system all read `IVerbRegistry`. Its implementation, `CommandDispatcher`, is the runtime of the command-initiator tier, so a domain system reading it reaches *up* a tier. The DI cycle (`ReloadCommand` → loader → participant → `IVerbRegistry` → every `ICommand`) is the symptom.
  - **Resolved at spec review (user, Option A, doc-first):** `IVerbRegistry` is classified as a read-only command-metadata registry, like `ITemplateRegistry`. Domain systems may read it through `Lazy<>` and never execute what it returns. `01-layers.md` gains that Allowed row in the same PR (INV-15), along with `IVerbRegistry`'s XML doc and the `add-domain-system` skill.
  - Rejected: Option B, where `HelpCommand` gathers verb matches and passes them in. The collision check would still need the verb set, so it only partly removes the dependency.
- **INV-19:** the YAML store's third-instance trigger is satisfied here or in PT-2, never twice. The new output format lands with its typed message.
- **INV-18:** world topics are authorable (editor) and inspectable (`help` plus the admin footer, and the editor's origin column). The engine layer is inspectable and overridable, never editable from tooling.
- **INV-31:**
  - the topic snapshot is guarded by an atomic reference swap;
  - authoring writes go through a declared write gate (the planner picks the catalog gate or its own);
  - the telnet and editor processes are separate, so editor saves reach the server on its next `reload` (the existing cross-process posture);
  - no new background thread.
- **INV-17:** flows touched:
  - [flow-01](../architecture/flows/flow-01-server-startup.md) (participants at boot);
  - [flow-03](../architecture/flows/flow-03-player-command-lifecycle.md) or the help path (the lookup system);
  - [flow-05](../architecture/flows/flow-05-content-reload.md) (participants, plus diagnostics in the result);
  - [flow-29](../architecture/flows/flow-29-bulk-content-generation.md) (the topic editor leg).
- **INV-16 / INV-20:** catalog rows for the registry, the lookup system, the port, the store types and the migrated stores. The `add-core-system`/`add-domain-system` skills gain "a new YAML family builds on `Core/Systems/Definitions/`, and re-derives through the participant port".
- **INV-25:** tests cover:
  - the lookup tiers (R5–R6);
  - normalization (R2);
  - layering and fallback (R9, S9);
  - validation warnings (R10);
  - participant diagnostics in the reload result;
  - the store migrations' existing tests, unmodified (R15);
  - a DI-smoke guard for the cycle.

### Resolved decisions (advisor intake)

*(Confirmed by the user on 2026-10-31. Do not relitigate.)*

1. **Re-derivation seam:** a DI-collected reload-participant port, invoked by the loader in every host, returning diagnostics into the boot and reload reports. Help is the first participant. Rejected: a `WorldContentReadyEvent` handler (no diagnostics path, no web-host refresh); a direct call from reload into Help (inverted coupling, repeated per family).
2. **Layering breadth:** shaped for later. Help-local, over ordered roots keyed by id, promoted when a second family needs it. Rejected: a generic layered source now (one consumer, and it may not fit Ability/Effect's compiled-plus-YAML case).

**Advisor calls** *(cheap to veto at planning)*:
- `IHelpLookupSystem` is extracted from `HelpCommand`;
- `HelpTopicMessage` is a new typed message, and the admin footer is kept out of the agent formatter's player-visible properties;
- participants don't run on a refused reload (topics keep their previous snapshot);
- no participant migrations (manifest, standards) in this slice.

## Planner resolutions

*(Answers to the brief's planner-owned open questions, settled with code access on 2026-10-31.)*

1. **Reload-participant port home: core-owned, in `Core/Systems/Definitions/`**, next to the store, together with `ContentDiagnostic`.
   - `WorldContentLoader` (World) depends on the port and Help implements it. Neither module references the other, so the dependency direction the brief requires holds.
   - A World-owned port would have made every future participant (standards in BalanceInspection, Ability/Effect) depend on World just to implement a lifecycle hook.
   - **DI-cycle note:** `ReloadCommand` and `StartRoomCommand` depend on `IWorldContentLoader`, which now depends on the participants. Any participant that needs the verb set must take `Lazy<IVerbRegistry>`. This is recorded in the skills text.
2. **Topic authoring home: Help-owned `IHelpTopicAuthoring` with its own `SemaphoreSlim(1,1)` write gate**, not a member of `ContentDefinitionCatalog`.
   - The manifest joined the catalog because a room **rename cascades** into `world.yaml`, so the two must serialize under one gate. Topics have no cross-reference with templates in either direction: they live outside the catalog's index, rename cascade and reference index.
   - Sharing the gate would buy no consistency, and it would couple Authoring to Help. The catalog is already the codebase's largest authoring class.
   - The gate keeps the catalog's `*Core` re-entrancy pattern, and the editor-race edge case keeps its posture: the last save wins.
   - The result type reuses `ContentWriteResult`.
3. **Engine help layer:** committed at **`content/help/`** in the repo, a sibling of `content/dev-world/` and outside any world's content directory. It is configured by **`Help:EngineTopicDirectory`** (default `content/help`), which `DataPaths.AddDataPaths` resolves against the **Hedron root**. That root is the checkout when run from source and the install folder when published.
   - It is deliberately **not** derived from `Paths:DataDirectory`: it is an engine install artifact, not mutable operator data. Moving the data directory never moves or hides the engine topics.
   - `Server.csproj` and `Hedron.Web.csproj` copy `content/help/**` to `content/help/` in their output, so the same relative path resolves in an install.
   - Every host and run mode composes `Register` → `AddDataPaths`, so Server, `Hedron.Web`, `generate`/`simulate` (which never load topics) and PT-1 worlds all agree.
   - The world layer is `<World:ContentDirectory>/help/`. It has no key of its own, so it travels with the world. `WorldContentLoader` reads only `areas`/`rooms`/`items`/`mobs`, so `help/` is never mistaken for world content.
4. **Topic file layout: one file per topic** (`<id>.yaml`), which is the backlog's per-file directory shape.
   - It makes a broken file cost exactly one topic.
   - It makes engine fallback per topic.
   - It makes every editor save a single atomic file write, with no multi-topic rewrite.
   - It gives R10's "duplicate id within a layer" a natural deterministic winner (the ordinal-first file name).
5. **Mixed-family ambiguity prompt (R6): never mixed.** The existing prompt is a free-text `PlainMessage`, so it *could* list both families. But R5's tiers are per family, so the verb-prefix tier resolves before the topic-prefix tier and no ambiguity spans both families. An ambiguous verb prefix prompts `Ambiguous command '<x>'. Did you mean: …?` (unchanged). An ambiguous topic prefix prompts `Ambiguous topic '<x>'. Did you mean: …?`. Ability prefix ambiguity stays "No help found", as today.
6. **Feature-folder split: yes, in WP3.** Help moves to `docs/features/help/`: `help.md` (holistic), `help-system.md` (moved and rewritten) and `help-topics.md` (the family's design doc). Communication keeps chat. A handful of inbound links point at the old path (the feature docs, `reference/commands.md`, the program doc and one historical backlog line; WP3 greps for the full list), so the move is cheap now and only gets dearer.
7. **`IVerbRegistry` `Lazy<>` cycle** (and see Post. 17: the layer direction is sanctioned doc-first in `01-layers.md`, spec-review Option A): the existing `Lazy<IVerbRegistry>` singleton in `HelpModule` is reused by `HelpLookupSystem` (reached through `HelpCommand` → `CommandDispatcher`), `HelpTopicReloadParticipant` (reached through `ReloadCommand` → `IWorldContentLoader`) and `HelpTopicAuthoring`.
   - `Lazy<IEnumerable<ICommand>>` stays registered for `CommandsCommand`. `HelpCommand` drops both direct dependencies.
   - The first `.Value` resolves during boot's `LoadAndSpawnAsync`, after the provider is built. At that point `IWorldContentLoader` is already constructed, so no re-entrant singleton construction occurs.
   - Covered by T12.

## Spec-review resolutions

*(`architecture-reviewer`, spec mode, 2026-10-31. Verdict: pass-with-fixes. All fixes are folded into the sections above.)*

- **User decisions:**
  1. The `IVerbRegistry` read is sanctioned **doc-first** (Option A: a `01-layers.md` Allowed row, the XML doc, the skill). Rejected: Option B, where the command gathers verb matches (Post. 17; Invariants in tension).
  2. `CLAUDE.md`'s project-layout and content lines are amended for `content/help/`, alongside `README.md` and `05-configuration.md` (WP2 Docs).
  3. The INV-17/INV-27 checklist wording, which still says one `flows/README.md`, is fixed in this slice (WP3 Docs).
- **Mechanical fixes applied:**
  - `YamlFileEntry`/`YamlCodecOptions` are defined, and `YamlReadResult.Fault` added, as the shared contract PT-2 copies (Post. 1, WP1 step 3).
  - Each migrated store's `Read` maps back to today's behavior, with T3b characterization cases written before the migration (WP1 step 2).
  - `consider` is dropped from the engine articles until slice 5 (WP2).
  - `HelpTopicLayerReader` has its catalog row (WP2).
  - T8 asserts registration order and boot-report log levels.
  - `HelpQuery` unquotes (T13).
  - The "`reload` skips validation" trigger rationale is stated.
  - `default(ContentReloadResult)` is safe.
  - The stale-snapshot clause is added (INV-31).
  - Authored text renders literally (Post. 11, T16).
  - The csproj copy rule is checked before duplicating it into `Hedron.Web.csproj`.
  - The skills `add-core-system`/`add-command`/`add-domain-system` are updated.
  - The inbound-link list for the docs split is completed.

## Open questions

- ~~**Reload-participant port home.**~~ Resolved: [Planner resolutions](#planner-resolutions) §1.
- ~~**Topic authoring home.**~~ Resolved: §2.
- ~~**Engine help layer location.**~~ Resolved: §3.
- ~~**Topic file layout.**~~ Resolved: §4.
- ~~**Mixed-family ambiguity prompt (R6).**~~ Resolved: §5.
- ~~**Feature-folder split.**~~ Resolved: §6.
- ~~**`IVerbRegistry` DI cycle** (brief, invariants in tension).~~ Resolved: §7.
- **Per-topic privilege (Parked):** admin-only topics, such as builder guides. It's cheap to add later as an optional field. It waits for a real consumer, for example authoring-guide topics for operators. *(User, later.)*

## Related

- [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md): the program (R18–R19, help design notes, resolved decision 1).
- [`playtest-run-mode.md`](playtest-run-mode.md): PT-2 WP1, the co-owner of the shared YAML store (R14).
- [`playtest-world-substrate.md`](playtest-world-substrate.md): PT-1, the agent-formatter rule that `HelpTopicMessage` and the footer satisfy.
- [`../roadmap/completed/dev-world-foundation.md`](../roadmap/completed/dev-world-foundation.md): `WorldManifestStore`, `IWorldManifestAuthoring`, `DataPaths`.
- [`../roadmap/completed/area-designation.md`](../roadmap/completed/area-designation.md): the warn-only diagnostics precedent and the boot report.
- [`../features/communication/help-system.md`](../features/communication/help-system.md): today's Help, which moves to `features/help/` in WP3.
- [`../features/admin-authoring/content-tooling.md`](../features/admin-authoring/content-tooling.md): catalog write-gate posture.
- [`../roadmap/backlog.md`](../roadmap/backlog.md): "YAML-authored definition pipeline" and "`reload` skips registry validation".
- [`../design/feature-horizon.md`](../design/feature-horizon.md): "Help system & in-game wiki" (search stays there).
