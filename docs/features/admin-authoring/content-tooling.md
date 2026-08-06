# Content Tooling

> `IContentDefinitionCatalog`, `IContentReferenceIndex`, `IContentValidator`, `IAreaLayoutSystem`, and `IContentGenerationSystem` — the shared content-definition layer both the Blazor editor and the headless bulk generator call. **Status:** live (content-tooling platform WP-1, T1; reference integrity + delete Slice B; grid-editor auto-layout world-editor-grid; rename + choose-at-creation blueprint-id-editing).

## What it is / does

The content-tooling layer is the backing of every authoring surface. Instead of per-surface logic, a single shared system set handles read/list/load/create/validate/write/delete over all four content kinds (area, room, item, mob). Telnet commands, the Blazor editor, and the bulk generator are all thin callers — surface parity is free as long as no authoring logic leaks into a command body, a Blazor component, or a run-mode (INV-8).

All tooling writes **YAML only** and never touches the live world (INV-12/23). Applying content to a running server is a separate `reload` step.

## IContentDefinitionCatalog

The shared facade both tooling tracks call. Reads, lists, loads, creates, validates, writes, and deletes content definitions across the four kinds.

**Interface:** [`IContentDefinitionCatalog.cs`](../../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs)

Operations:
- `List(ContentKind kind)` — enumerates definitions on disk (id, name, short-desc) by reading the kind subdirectory.
- `Load(ContentKind kind, string blueprintId)` — reads and deserializes one YAML file into an editable `ContentDefinition` DTO.
- `SaveAsync(ContentDefinition definition, CancellationToken ct)` — validates then writes via the matching `I*ContentWriter`; refuses to write (returns `ContentWriteResult.Failed`) on structural validation failure. On structural pass, cross-reference misses become non-blocking **warnings** in `ContentWriteResult.Warnings` — the file is still written (warn-but-allow; INV-19). See [`reference/systems.md`](../../reference/systems.md) for the full interface.
- `SaveRoomAsync(RoomTemplate room, bool bidirectional, CancellationToken ct)` — like `SaveAsync` for rooms, plus: when `bidirectional: true`, also writes the inverse exit on each target room. Conflict policy: if the target already has a *different* exit in the inverse direction, the paired write is skipped and a warning is returned (warn-and-skip; no silent overwrite).
- `DeleteAsync(ContentKind kind, string blueprintId, CancellationToken ct)` — deletes the YAML file for the given definition and **cascade-clears** every definition that references it (rooms lose `AreaId`, exit entries pointing at the deleted id are removed, items/mobs lose `SpawnRoomBlueprintId`, areas lose the entry from their `Rooms` list). **YAML-file-only** — no `EntityService.DestroyEntity`, no SQLite delete, no live-world mutation (INV-22/23). Returns `ContentDeleteResult` with the deleted path and each cascade edit.
- `CreateNew(ContentKind kind, string name)` / `CreateNew(ContentKind kind, string name, string? blueprintId)` — produces a new, un-persisted definition. With a `null`/empty `blueprintId`, mints a generated `AdhocBlueprintId`; with a deliberate id, the definition carries that id (validated by `CreateAsync` at write time, not here). No live entity is created.
- `CreateAsync(ContentDefinition definition, CancellationToken ct)` — the create-guarded write: runs `IContentValidator.ValidateBlueprintId` + a `IContentReferenceIndex.Resolves` uniqueness check before delegating to `SaveAsync`; refuses (no write) on a malformed or already-taken id. Use this for first-write creation with a deliberate id — edits stay on `SaveAsync` (overwrite-on-edit, unchanged). Added blueprint-id-editing.
- `RemoveRoomExitAsync(string roomBlueprintId, Direction direction, bool bidirectional, CancellationToken ct)` — removes one room exit; the mirror of `SaveRoomAsync`'s bidirectional *add* policy. Removing an absent exit is a no-op success. When `bidirectional: true`, also removes the target's inverse exit, but only when it still points back at the source (an inverse pointing elsewhere is left untouched). Added world-editor-grid.
- `WithBlueprintId(ContentDefinition definition, string? blueprintId)` — returns the definition with **only its id replaced**; every other authored field is preserved, and self-referential ids (a room's self-loop exit) are rewritten by the same `CloneWithNewId` rule `RenameAsync` applies. Blank falls back to a freshly minted ad-hoc id. Pure — writes nothing. This is what the New form's blueprint-id field calls; calling `CreateNew` there instead silently discards the in-progress form (see [`content-authoring.md`](content-authoring.md) step 2). Added authoring-editor-repair.
- `CreateNextFrom(ContentDefinition previous, string name)` — mints the next definition in a "save and create next" run, delegating id minting to `CreateNew`. Carry-forward is per kind: **Area** nothing (areas are authored individually); **Room** `AreaId`; **Item** Tier, Band, `ItemType`, `WornSlots`; **Mob** Tier, Band, `SpawnRoomBlueprintId`. Everything else resets. This is authoring policy *plus* kind dispatch, which a component may not hold ([`08-blazor.md`](../../architecture/08-blazor.md)) — hence its home here. Pure. Added authoring-editor-repair.
- `Invalidate()` — drops the whole in-memory index so the next read re-populates from disk. The escape hatch for content written outside this process; catalog-mediated writes invalidate on their own. Added authoring-editor-repair.
- `RenameAsync(ContentKind kind, string oldId, string newId, CancellationToken ct)` — renames a definition's blueprint id, the structural sibling of `DeleteAsync`. Validates `newId` (format via `IContentValidator.ValidateBlueprintId`, uniqueness via `IContentReferenceIndex.Resolves`) — malformed or already-taken refuses with no write, no delete of `oldId`. On pass: writes a fresh `newId` file carrying the definition's full state (with its own self-referential fields, e.g. a self-loop exit, rewritten `oldId → newId`), cascade-*rewrites* every external referrer found via `IContentReferenceIndex.Referrers` (best-effort, same posture as delete), then deletes the `oldId` file. Folds out-of-YAML advisories into `Warnings` (persistent player/item locations re-key on `reload`; a specific note when the renamed room matches `World:StartingRoomBlueprintId`) — never writes SQLite or `appsettings.json` (INV-22/23). Returns `ContentRenameResult`. Added blueprint-id-editing.

The catalog dispatches by `ContentKind` internally; it is a thin facade over per-kind operations and carries no live-entity creation (that half stays in the `mk*` builders, per the platform brief's "builders fuse two concerns" split).

**Registered:** `AuthoringModule.AddAuthoringModule`. Located at `Core/Modules/Authoring/Systems/`.

### Read semantics — cached, coherent for catalog-mediated writes

Reads are served from an in-memory index (added authoring-editor-repair, because the editor called `List` from inside render loops and each call re-read and re-deserialized the whole corpus):

- **Per-kind summary lists** and the **room→area map** are corpus sweeps. The map is *derived from the cached room summaries*, so listing all four kinds sweeps each kind's directory at most once per invalidation.
- **The per-id definition map fills one id at a time, on demand.** This granularity is load-bearing, not an optimization detail: `ITemplateConformanceSystem.ApplyFlaggedAsync` and `IAreaLayoutSystem.ApplyProposalAsync` both loop `Load`→write per entry, and corpus-populated per-id caching under whole-index invalidation would turn each into N full sweeps. As built, a `Load` after an invalidation is one file read and both loops stay O(N).
- It caches file **text**, not templates — `Load` deserializes per call, because callers mutate what they get back (the editors bind form fields to it).

**Invalidation is whole-index.** Every mutator drops the entire index, because writes cascade across files: `DeleteAsync` clears fields on referrers, `RenameAsync` rewrites them, `SaveRoomAsync(bidirectional: true)` writes an inverse exit on a *different* room. Entry-scoped invalidation cannot express those cascades. So a read after any catalog write observes the write **and** everything its cascade touched.

**What the cache does not cover.** The catalog is not the only writer of content YAML: the `generate` CLI runs in its own process, and the game host's `mk*`/`set*`/`dig` verbs write through the `I*ContentWriter` family directly. Neither invalidates a running editor's index, so a long-lived editor host can serve a stale listing after an out-of-process write. `Invalidate()` (surfaced as the browser's **Refresh from disk** action) is the mitigation; the residual staleness is [acknowledged debt](../../roadmap/backlog.md). Note also that `IContentReferenceIndex` and `IBalanceAuditSystem` do **not** read through the catalog and gain nothing from the index.

**Concurrency posture (INV-31).** The catalog is a DI singleton reached concurrently from multiple Blazor circuits. Every mutator is `async` and invalidates *after* an awaited write, so a thread-affine `ReaderWriterLockSlim` is unusable — it cannot be held across an `await`. The index is a snapshot object swapped under a plain `lock`: readers take the reference with no lock and populate lazily into it, and snapshot identity carries the generation, so a sweep begun before a concurrent write cannot republish pre-write state. The guard covers **index consistency only** — it does not make YAML writes atomic, and the non-transactional multi-file cascade is unchanged.

`IContentFileReader` (`Core/Modules/Authoring/Systems/`) wraps the catalog's directory listings and file reads so a test can count them deterministically. It is an infrastructure **port**, not a domain system — no `reference/systems.md` row (INV-16/29) — but is DI-registered for the composition-root smoke guard. It is deliberately catalog-scoped: the authoring module already has three filesystem styles (`ContentReferenceIndex` reads directly, the `I*ContentWriter` family writes, this seam wraps catalog reads); a slice needing broader indirection should widen it rather than add a fourth (INV-19).

## IContentReferenceIndex

Declared-edge reference model over the on-disk YAML definition set. Answers three read questions without applying policy: *does this target exist?*, *who points at this id?*, and *what is broken across all definitions?* Pure read — returns structured results, publishes nothing.

The declared edge set is: `(Room, AreaId) → Area`, `(Room, Exits[dir]) → Room`, `(Item, SpawnRoomBlueprintId) → Room`, `(Mob, SpawnRoomBlueprintId) → Room`, `(Area, Rooms[]) → Room`, `(Room, SpawnRules[].BlueprintId) → {Mob, Item}`. Adding a new edge requires only a new `ReferenceEdge` declaration — no additional code paths (INV-19). The last edge is **two-kind**: a `SpawnRule` id carries no kind discriminator, so `Referrers` resolves it against *either* Mob or Item, and `SweepBroken`/`BrokenFor` flag it broken only when it resolves against *neither* — closing a pre-existing gap where deleting a mob/item left dangling `Room.SpawnRules` entries invisible to the sweep (blueprint-id-editing).

Operations:
- `Resolves(ContentKind targetKind, string targetBlueprintId)` — whether a definition file exists for that id.
- `Referrers(ContentKind targetKind, string targetBlueprintId)` — every definition pointing at the given id, described as the cascade-clear edit the delete path applies.
- `SweepBroken()` — every edge across all definitions whose target does not resolve; used by the integrity/health page.
- `BrokenFor(IEntityTemplate definition)` — dangling refs in one in-memory definition; used by the save warn-but-allow path.

**Interface:** [`IContentReferenceIndex.cs`](../../../Core/Modules/Authoring/Systems/IContentReferenceIndex.cs)

**Registered:** `AuthoringModule.AddAuthoringModule`. Located at `Core/Modules/Authoring/Systems/`.

## IContentValidator

On-demand content validator factored out of `RegistryValidationBootstrap`. Two call modes share the same referential-integrity rules:

- `ValidateRegistry(startingAbilityIds)` — whole-registry sweep (ability→effect/aspect cross-refs, aspect-composition normalization, area affinity normalization, and a warn-not-error room-coordinate-collision check sourced from `ITemplateRegistry`). Used at boot by `RegistryValidationBootstrap`.
- `Validate(IEntityTemplate)` — single in-memory definition check. Used by `IContentDefinitionCatalog.SaveAsync` per edit and by the `generate` run-mode per emitted definition.
- `ValidateBlueprintId(ContentKind, string)` — validates a candidate blueprint id: non-empty, filename-safe (`[A-Za-z0-9._-]+`, no path separator, no `..` segment, not a reserved Windows device name — the id becomes a file name) refuses on failure; a kind-prefix mismatch (e.g. a room id not starting with `room.`) is a non-blocking warning. Shared by `RenameAsync` and the create-with-id guard (`CreateAsync`). Added blueprint-id-editing.

Returns a structured `ValidationReport` (`Errors` + `Warnings`; `IsValid` is errors-only); **never throws** (INV-5). The host decides fail-fast policy — the boot bootstrap logs and throws on errors, logs and continues on warnings; the editor and generator surface structured errors.

**Interface:** [`IContentValidator.cs`](../../../Core/Modules/World/Systems/IContentValidator.cs)

**Registered:** `WorldModule.AddWorldModule`. Located at `Core/Modules/World/Systems/`.

## IAreaLayoutSystem — visual grid area editor

Deterministic auto-layout for an area's rooms that lack authored `X`/`Y`/`Z` coordinates. Backs the visual grid area editor's (`/area/{id}/grid`) ghost-cell proposal and its "Apply layout" bulk write.

- `Propose(areaBlueprintId)` — loads the area's rooms via `IContentDefinitionCatalog` and runs a pure BFS placement over the exit graph: anchored (fully-coordinate) rooms are fixed and never moved; placement is seeded from anchors in ordinal blueprint-id order with exits iterated in `Direction` enum order; an occupied target cell spills to the nearest free cell via an expanding Chebyshev ring scan (deterministic tie-break); a component with no anchor is placed at the next deterministic free origin. Never writes — the same disk state always yields the same proposal. Returns `AreaLayoutProposal(Anchored, Proposed, Collisions)`, where `Collisions` is the same `RoomCoordinateCollisions` detection the registry-validation warning uses, scoped to this area's anchored rooms.
- `ApplyProposalAsync(areaBlueprintId, ct)` — re-derives the proposal from disk and writes coordinates only for rooms that still lack them (`SaveRoomAsync(bidirectional: false)` per room), best-effort — one room's failure is recorded as a warning and does not stop the rest. Already-anchored rooms are never rewritten.

**Returns results; never publishes** (INV-5). No RNG or wall-clock — the placement is a pure function of on-disk state (INV-26 moot).

**Interface:** [`IAreaLayoutSystem.cs`](../../../Core/Modules/Authoring/Systems/IAreaLayoutSystem.cs)

**Registered:** `AuthoringModule.AddAuthoringModule`. Located at `Core/Modules/Authoring/Systems/`.

## IContentGenerationSystem

Headless bulk content generator (content-tooling track T1). Composes the four existing `I*ContentWriter`s + `*Template` types to emit a connected, walkable swath of world-content YAML from a `GenerationProfile`.

**Interface:** [`IContentGenerationSystem.cs`](../../../Core/Modules/Authoring/Systems/IContentGenerationSystem.cs)

Profile parameters: `Seed`, `AreaCount`, `RoomsPerArea`, `LevelRange`, `MobDensity`, `ItemDensity`, `AspectMix`, `ScalingCurve`, `BlueprintPrefix`.

Key properties:
- **Deterministic.** All randomness flows through `SeededRandom(profile.Seed)` (INV-26). Blueprint ids are `prefix + per-kind counter` (e.g. `gen.area.0001`), never `Guid`. A fixed-seed run is byte-reproducible within a runtime image.
- **Connected graph.** Rooms within an area are wired east/west; consecutive areas are joined up/down — the generated world is one reachable graph.
- **YAML only.** Never creates live entities, never registers in `TemplateRegistry`, never calls persistence (INV-12/23).
- **Returns results; never publishes** (INV-5). Validation is the caller's (run-mode's) concern.

**Registered:** `AuthoringModule.AddAuthoringModule`. Located at `Core/Modules/Authoring/Systems/`.

## `generate` run-mode

`Server` recognizes a `generate` run-mode: `dotnet run --project Server -- generate --profile <path> [--seed N]`. In this mode the host composes services via `CompositionRoot` (bootstraps-only, no telnet/heartbeat/persistence, no world-content entity spawn), calls `IContentGenerationSystem.GenerateAsync(profile)`, validates each emitted definition via `IContentValidator.Validate`, prints the `GenerationResult` summary, and exits with code `0` (clean) or non-zero (validation/write failure). It is a **no-chain Initiator** (INV-10): purely offline, publishes nothing.

The full sequence is [Flow 29 (content-tooling journey)](../../architecture/flows/flow-29-bulk-content-generation.md).

## Design notes

- **The catalog is one facade, not per-feature services.** A single entry point over all four kinds avoids forcing every consumer to fan out across four modules and re-implement the `ContentKind → subdirectory → writer → deserializer` mapping.
- **`CreateNew` takes the template-construction half of the builders, not the live-spawn half.** The `mk*` builders fuse (a) template construction + id generation with (b) live `EntityService.CreateEntity`. The catalog takes (a) only — generates an id and an empty template, writes YAML, stops. The live spawn half stays in the `mk*` commands.
- **The cache is framework-independent.** It lives behind the existing interface with no call-site change, so it is equally valuable whichever way the [client-tier gate](../../design/client-tier.md) falls — a React port inherits it unchanged. That is why it ran as a Phase 5 no-regret slice.
- **Keyed by `GenerationProfile` for forward generalization.** The profile key lets the eventual in-game procedural-content feature (horizon §1) reuse `IContentGenerationSystem` by supplying a runtime-built profile instead of a file-loaded one, writing to a live-spawn sink instead of YAML writers.

## Related

- [`admin-authoring.md`](admin-authoring.md) — the holistic feature view.
- [`content-authoring.md`](content-authoring.md) — the Blazor editor that calls `IContentDefinitionCatalog`.
- [`../../architecture/flows/flow-29-bulk-content-generation.md`](../../architecture/flows/flow-29-bulk-content-generation.md) — the content-tooling journey: bulk `generate` + offline editor.
- [`../../reference/systems.md`](../../reference/systems.md) — `IContentDefinitionCatalog`, `IContentValidator`, `IAreaLayoutSystem`, `IContentGenerationSystem`, the `I*ContentWriter` family.
- [`../../roadmap/completed/content-tooling-platform.md`](../../roadmap/completed/content-tooling-platform.md) — as-built record: shipped pieces, design decisions, spec-review provenance.
- [`../../roadmap/completed/content-editor-integrity.md`](../../roadmap/completed/content-editor-integrity.md) — as-built record for `IContentReferenceIndex`, `DeleteAsync` cascade, warn-but-allow save, and bidirectional linking.
- [`../../roadmap/completed/authoring-editor-repair.md`](../../roadmap/completed/authoring-editor-repair.md) — as-built record for the catalog index, `WithBlueprintId`/`CreateNextFrom`/`Invalidate`, and the editor UX ratchet.
- [`../../roadmap/completed/blueprint-id-editing.md`](../../roadmap/completed/blueprint-id-editing.md) — as-built record for `RenameAsync`, choose-at-creation (`CreateAsync`), `ValidateBlueprintId`, and the two-kind spawn-rule edge.
- [`../../design/feature-horizon.md`](../../design/feature-horizon.md) — "Procedural / generated areas" (§1), the gameplay generalization of `IContentGenerationSystem`.
