# Help topics — Phase 5 slice 4 (completed)

> Implemented on branch `claude/help-topics-slice-4-refine-6a591a`, 2026-10-31. Living docs: [`help`](../../features/help/help.md) ([`help-system`](../../features/help/help-system.md), [`help-topics`](../../features/help/help-topics.md)), [`world-content`](../../features/world/world-content.md#content-reload-participants) (reload participants), [`content-tooling`](../../features/admin-authoring/content-tooling.md#ihelptopicauthoring--the-help-topics-page), [`reference/systems.md`](../../reference/systems.md) (shared YAML store). Program: [`phase-5-content-baseline-program.md`](../../implementation-plans/phase-5-content-baseline-program.md) (R18–R19).

## Outcome

`help <topic>` now serves authored articles. The engine ships five generic, number-free mechanic topics in `content/help/`, and a world can override or extend them in `<content>/help/`, with the world copy winning by id. A world copy that fails to parse falls back to the engine version. Bare `help` adds a **Topics** block. The lookup moved into `IHelpLookupSystem`: exact matches before prefix matches, verb → topic → ability within each tier, visibility-gated, plus a multi-word fallback.

Topics rebuild on every boot and `reload` through a new core-owned, warn-only **`IContentReloadParticipant`** port. Its diagnostics reach the boot log, the `reload` output and the editor's Apply page. Topics are authored on new `/help-topics` editor pages, and administrators see an origin footer in game.

Underneath, the slice extracted the **shared YAML definition store** (`Core/Systems/Definitions/`), the INV-19 rule-of-three trigger. It migrated balance standards, sim scenarios and the world manifest onto that store with no visible change, closing slice 1's manifest debt. Help moved into its own `features/help/` folder.

## Behavior digest

- **Requirements (as confirmed).**
  - **R1–R2 article shape.** A topic has `id`, `title`, optional `aliases`, optional `seeAlso` and `body`. Lookup normalizes whitespace to hyphens and ignores case.
  - **R3 index.** Bare `help` writes the verb index plus a Topics block when topics exist. `commands` is unchanged.
  - **R4 rendering.** A topic renders as title, body, and a `See also` line built from resolved ids only.
  - **R5–R6 lookup.** Exact matches beat prefix matches, verb → topic → ability within each tier. Ambiguous verb and topic prefixes get separate prompts.
  - **R7.** `help` is read-only and usable while incapacitated.
  - **R8.** Administrators get an origin footer.
  - **R9–R10 validation.** Broken files are warned and skipped and never block a load. Collision and `seeAlso` warnings are reported.
  - **R11.** Five qualitative engine topics.
  - **R12.** The editor writes the world layer only: Override, Delete, preview.
  - **R13.** Topics are an atomic snapshot, rebuilt on every load.
  - **R14–R15.** The shared store follows "whichever slice lands first extracts", with a migration that changes nothing visible.
- **Main flow.**
  1. **Load or reload.** On a clean load, after `ResolveStartingRoom`, the loader runs its participants. The help participant reads both layers, gathers verb tokens, builds a new `HelpTopicSet` and publishes it, then returns diagnostics. The loader logs them and returns them in `ContentReloadResult.Diagnostics`. `reload` prints one line per diagnostic after its unchanged confirmation. A refused reload runs no participant.
  2. **Player lookup.** `HelpCommand` parses the rest of the line into a `HelpQuery`. `IHelpLookupSystem.Resolve` walks the tiers against one snapshot, and the command renders the resolution.
  3. **Authoring.** `IHelpTopicAuthoring` reads both layers from disk and writes the world layer under its own gate. A save applies on the next `reload`.
- **Events.** None new. `ContentReloadedEvent`'s payload and order are unchanged.

## Shipped pieces

| Surface | Location |
|---|---|
| Shared YAML store: `YamlDocumentFile`, `YamlDefinitionDirectory` (+ `Directory`, `Delete`), `YamlReadResult` (+ `Fault`), `YamlFileEntry`, `YamlCodecOptions`, `DefinitionValidation`, internal `YamlCodec` (literal `\|` blocks for multi-line strings) | `Core/Systems/Definitions/` |
| `IContentReloadParticipant`, `ContentDiagnostic`(`Severity`) | `Core/Systems/Definitions/` |
| Migrated stores (internals only) | `BalanceStandardsStore`, `SimScenarioStore`, `WorldManifestStore` |
| Loader: optional participants, `RunParticipantsAsync`, `ContentReloadResult.Diagnostics` (default-safe) | `Core/Modules/World/Systems/WorldContentLoader.cs`, `IWorldContentLoader.cs` |
| `reload` diagnostic lines | `Core/Modules/Admin/Commands/ReloadCommand.cs` |
| `HelpOptions` (`EngineTopicDirectory`, derived `WorldTopicDirectory`); `DataPaths` install-relative rule; `Configure<HelpOptions>`; `Server.csproj` copy of `content/help/**` | `Core/Modules/Help/HelpOptions.cs`, `Server/DataPaths.cs`, `Server/CompositionRoot.cs`, `Server/Server.csproj` |
| Topic family: `HelpTopic`/`HelpTopicOrigin`/`HelpLayer`/`HelpTopicDocument`; `HelpTopicLayerReader`; `HelpTopicSetBuilder`; `HelpTopicSet`; `HelpTopicRegistry`; `HelpVerbTokens`; `HelpTopicReloadParticipant` | `Core/Modules/Help/`, `Core/Modules/Help/Systems/` |
| Lookup: `HelpQuery`, `HelpResolution` (+ `VerbHelp`, `HelpAbilityCatalog`, `HelpMatchFamily`, `HelpIndex`), `IHelpLookupSystem`/`HelpLookupSystem`; thin `HelpCommand` (`RestOfLine` arg `topic`) | `Core/Modules/Help/Systems/`, `Core/Modules/Help/Commands/HelpCommand.cs` |
| Output: `HelpTopicMessage`, `HelpTopicIndexMessage`/`HelpTopicIndexEntry`, `HelpTopicOriginMessage`; telnet cases (authored text literal); `HelpTopicRendering` | `Core/Output/`, `Core/Modules/Help/HelpTopicRendering.cs` |
| Authoring: `IHelpTopicAuthoring`/`HelpTopicAuthoring` (`SemaphoreSlim` gate) | `Core/Modules/Help/Systems/` |
| Editor: `/help-topics`, `/help-topics/edit/{id}`, nav link, `HelpTopicPreview`; Apply page lists diagnostics | `Hedron.Web/Components/Pages/`, `Hedron.Web/Services/HelpTopicPreview.cs` |
| Engine topics: `getting-started`, `combat`, `death`, `shops`, `progression` | `content/help/*.yaml` |
| Layering rule: domain systems may read `IVerbRegistry` metadata via `Lazy<>` | `docs/architecture/01-layers.md`, `Core/Commands/IVerbRegistry.cs` |
| Docs split: `features/help/` (`help.md`, `help-system.md` moved/rewritten, `help-topics.md`); INV-17/INV-27 wording (per-file flows) | `docs/features/help/`, `docs/architecture/checklist.md` |

## Tests shipped

- **System-unit:**
  - `YamlDocumentFileTests` (T1) and `YamlDefinitionDirectoryTests` (T2, including `Delete` confinement and literal multi-line output);
  - `HelpTopicSetBuilderTests` (T5), `HelpTopicRegistryTests` (T6), `HelpTopicReloadParticipantTests` (T7), `HelpQueryTests` (T13);
  - `HelpLookupSystemTests` (T14). This is the on-touch ratchet for the previously untested lookup.
  - `HelpTopicFormattingTests` (T16/T18) and `HelpTopicAuthoringTests` (T17: engine directory byte-identical, concurrent saves serialized);
  - `DataPathsTests` extended (T10).
- **Characterization (T3b):** `YamlStoreMigrationCharacterizationTests`. These were written and passing against the hand-rolled stores **before** the migration: exception types and messages, empty-file behavior, listing of unparseable files, and byte-equal serialized YAML.
- **Flow:**
  - `WorldContentLoaderParticipantTests` (T8/T9): registration order, after the starting room, a refused reload runs none, a throwing participant gives one error, log levels in one block, and the `reload` output lines;
  - `HelpCommandFlowTests` (T15);
  - `EngineHelpTopicsTests` (T11): the real `content/help/` against the real composed verb set, with zero diagnostics, the five ids, and no digits.
- **Architecture guard:**
  - `YAML_definition_families_use_the_shared_store` (T4);
  - the DI smoke test extended (T12): `IVerbRegistry`, participants and the loader compose without a cycle.
- **Existing store suites** pass with their cases unmodified (T3).
- **Manual:** a `Hedron.Web` smoke on the DevWorld profile.
  - Boot logged zero help diagnostics.
  - `/help-topics` listed the five engine topics.
  - Override, then save, then delete round-tripped, and the engine version returned.
  - The dev-world tree was left clean.
- **Not run:** the telnet smoke. T15 covers the same paths.
- `dotnet build` and `dotnet test` are green: **1796 tests** (after the code-review fixes).

## Decisions

- **Reload-participant port, not a handler or a direct call** (advisor decision 1).
  - A `WorldContentReadyEvent` handler cannot return diagnostics to `reload`'s output, and `Hedron.Web` composes no bus subscriptions.
  - A direct call would point World at Help.
  - The port is core-owned. The loader owns *when* to rebuild; each family owns *how*.
  - It is **warn-only**, the opposite of the staged fail-fast posture world content uses.
- **Layering is help-local, shaped for later** (advisor decision 2). The merge runs over ordered roots keyed by id. It is promoted when a second committed-plus-world family appears.
- **The snapshot is the `DefinitionRegistry`.** Each rebuild constructs a new `HelpTopicSet` carrying its aliases and order. A `Reload(rows)` on the base class could tear rows from aliases.
- **The participant orchestrates; the builder is pure.** Boot, reload and the editor apply one rule set. The registry stays dumb: no verb dependency, and its only writer is the participant.
- **Domain systems may read `IVerbRegistry` metadata** (spec review, Option A, doc-first):
  - it is a read-only metadata registry, like `ITemplateRegistry`;
  - it is always reached through `Lazy<>`, and nothing ever executes a command;
  - Option B (the command gathers the verb matches) was rejected because the collision check still needs the whole verb set.
- **Multi-word queries try topics first, then a first-token verb** (planning decision). `help kill orc` keeps resolving to `kill`. Skipping the verb tiers entirely was rejected as a regression.
- **No digits in engine topic bodies.** The rule is enforced by a test, as a mechanical guard for "qualitative about tunables".
- **The engine help layer is install-relative.** It is resolved against the Hedron root and never moves with `Paths:DataDirectory`. The world layer has no key of its own.
- **Topic authoring is Help-owned**, not part of the catalog. Topics have no cascade into other content, so they get their own gate. Disk is the editor's truth; the registry is the game's.
- **The admin footer is a separate message.** This keeps a topic's origin out of player output and out of PT-1's agent formatter, which serializes every public property.
- **Authored text renders literally.** It is coloured directly, never passed through the marker parser.
- **The shared contract carries more than PT-2's plan named.** `YamlFileEntry`, `YamlCodecOptions` and `YamlReadResult.Fault` are pinned here and copied into PT-2's plan.
- **Why the "reload skips validation" trigger did not fire.** Participants run after the swap and cannot refuse, so validating the staged registry is still a separate change ([backlog](../backlog.md)).

## Deviations / Follow-ups

- **Deviations from the plan:**
  - **The world help root is derived into `HelpOptions.WorldTopicDirectory` by `DataPaths`.** The layer reader does not read `WorldOptions`, so Help never depends on World's options.
  - **One csproj copy rule (`Server.csproj`) is enough.** `Hedron.Web` and `Hedron.Tests` get the files through the project reference.
  - **The shared codec emits literal `|` blocks for multi-line strings.** The editor smoke test exposed YamlDotNet's folded default, which doubled line breaks. The other families have no multi-line strings, so their output is unchanged (T3b).
  - **T3b is its own test class**, not new cases appended to the three existing suites. Those suites stay byte-unmodified.
  - **T4 lives in `ArchitectureGuardTests`**, to reuse its source locator.
  - **`HelpVerbTokens` was added** as the one place verb tokens are gathered, shared by the participant and authoring.
  - **Editor-written world topic files omit nulls** (`YamlCodecOptions(OmitNulls: true)`), so they never carry `aliases: null`; the plan had help topics on the default codec. Reading is unaffected.
  - **`HelpOptions.WorldTopicDirectory` defaults to empty**, because `DataPaths` is its only writer. An unconfigured world root means no world layer, and authoring refuses to write (code-review fix).
  - **The editor preview builds a transient topic and renders it through `HelpTopicRendering.ToMessage`**, the one mapping `help` uses (code-review fix).
- **Follow-ups:**
  - Slice 5 adds `consider` to the engine `combat` article. Content drops 6–8 may add dev-world help overlays.
  - PT-2 now takes branch A: it consumes the shared store.
  - Promote the layering when a second committed-plus-world family needs it.
  - Admin-only topics stay parked.
  - Search and cross-linking remain on the horizon ([feature-horizon](../../design/feature-horizon.md)).
- **Acknowledged debt (unchanged):**
  - Ability/Effect compiled-plus-YAML coexistence ([backlog](../backlog.md), "YAML-authored definition pipeline");
  - staged-registry validation on `reload` ([backlog](../backlog.md));
  - cross-process editor/server staleness (existing backlog entry).
