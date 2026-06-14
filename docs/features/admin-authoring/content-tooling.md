# Content Tooling

> `IContentDefinitionCatalog`, `IContentValidator`, and `IContentGenerationSystem` — the shared content-definition layer both the Blazor editor and the headless bulk generator call. **Status:** live (content-tooling platform WP-1, T1).

## What it is / does

The content-tooling layer is the backing of every authoring surface. Instead of per-surface logic, a single shared system set handles read/list/load/create/validate/write over all four content kinds (area, room, item, mob). Telnet commands, the Blazor editor, and the bulk generator are all thin callers — surface parity is free as long as no authoring logic leaks into a command body, a Blazor component, or a run-mode (INV-8).

All tooling writes **YAML only** and never touches the live world (INV-12/23). Applying content to a running server is a separate `reload` step.

## IContentDefinitionCatalog

The shared facade both tooling tracks call. Reads, lists, loads, creates, validates, and writes content definitions across the four kinds.

**Interface:** [`IContentDefinitionCatalog.cs`](../../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs)

Operations:
- `List(ContentKind kind)` — enumerates definitions on disk (id, name, short-desc) by reading the kind subdirectory.
- `Load(ContentKind kind, string blueprintId)` — reads and deserializes one YAML file into an editable `ContentDefinition` DTO.
- `SaveAsync(ContentDefinition definition, CancellationToken ct)` — validates then writes via the matching `I*ContentWriter`; refuses to write on validation failure. Returns `ContentWriteResult`.
- `CreateNew(ContentKind kind, string name)` — produces a new, un-persisted definition with a generated `AdhocBlueprintId`. No live entity is created.

The catalog dispatches by `ContentKind` internally; it is a thin facade over per-kind operations and carries no live-entity creation (that half stays in the `mk*` builders, per the platform brief's "builders fuse two concerns" split).

**Registered:** `AuthoringModule.AddAuthoringModule`. Located at `Core/Modules/Authoring/Systems/`.

## IContentValidator

On-demand content validator factored out of `RegistryValidationBootstrap`. Two call modes share the same referential-integrity rules:

- `ValidateRegistry(startingAbilityIds)` — whole-registry sweep (ability→effect/aspect cross-refs, aspect-composition normalization, area affinity normalization). Used at boot by `RegistryValidationBootstrap`.
- `Validate(IEntityTemplate)` — single in-memory definition check. Used by `IContentDefinitionCatalog.SaveAsync` per edit and by the `generate` run-mode per emitted definition.

Returns a structured `ValidationReport`; **never throws** (INV-5). The host decides fail-fast policy — the boot bootstrap logs and throws; the editor and generator surface structured errors.

**Interface:** [`IContentValidator.cs`](../../../Core/Modules/World/Systems/IContentValidator.cs)

**Registered:** `WorldModule.AddWorldModule`. Located at `Core/Modules/World/Systems/`.

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
- **Keyed by `GenerationProfile` for forward generalization.** The profile key lets the eventual in-game procedural-content feature (horizon §1) reuse `IContentGenerationSystem` by supplying a runtime-built profile instead of a file-loaded one, writing to a live-spawn sink instead of YAML writers.

## Related

- [`admin-authoring.md`](admin-authoring.md) — the holistic feature view.
- [`content-authoring.md`](content-authoring.md) — the Blazor editor that calls `IContentDefinitionCatalog`.
- [`../../architecture/flows/flow-29-bulk-content-generation.md`](../../architecture/flows/flow-29-bulk-content-generation.md) — the content-tooling journey: bulk `generate` + offline editor.
- [`../../reference/systems.md`](../../reference/systems.md) — `IContentDefinitionCatalog`, `IContentValidator`, `IContentGenerationSystem`, the `I*ContentWriter` family.
- [`../../roadmap/completed/content-tooling-platform.md`](../../roadmap/completed/content-tooling-platform.md) — as-built record: shipped pieces, design decisions, spec-review provenance.
- [`../../design/feature-horizon.md`](../../design/feature-horizon.md) — "Procedural / generated areas" (§1), the gameplay generalization of `IContentGenerationSystem`.
