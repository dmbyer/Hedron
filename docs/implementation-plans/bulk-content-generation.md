# Use Case: Headless Bulk Content Generation (`IContentGenerationSystem` → validated YAML)

**Status:** implemented
**Actors:** Developer (headless CLI), System
**Module:** new `Core/Modules/Authoring/` (`IContentGenerationSystem`, `GenerationProfile`, `GenerationResult`); `Server/` (headless CLI run-mode entry point); reuses `Core/Modules/World/`, `Core/Modules/Items/`, `Core/Modules/Mobs/` writers + templates.

> **Track T1 of the [content-tooling platform](content-tooling-platform.md).** The shared architecture, seam rationale, family disposition, and resolved decisions live in that platform brief (INV-27); this slice references them rather than restating. This is the bulk-generation track named in the platform's scope note; the Blazor authoring track (T2) is a sibling slice. The two share only the content-definition writer seam, so neither blocks the other.

---

## Description

A developer needs to assemble large swaths of areas/rooms/items/mobs to deeply test the engine (scaling regressions, spawn density, combat balance across level ranges). Hand-authoring this volume via telnet `mk*` verbs is intractable. This slice adds a headless **`IContentGenerationSystem`** that, from a **generation profile** (area count, level range, rooms-per-area range, mob/item density, aspect mix, scaling curve, RNG seed), composes the **existing** `I*ContentWriter`s and `*Template` types to emit a swath of **validated YAML** content under `content/`. It writes definition files only — it never mutates the live world (INV-23, INV-12); the generated content is applied to a running server via the existing `reload` path, which is out of scope here. All randomness rolls through the existing `IRandom` seam (INV-26), so a run with a fixed seed is **deterministic and reproducible** — the foundation for reproducible scaling-regression test worlds. The system is keyed by a profile so it later generalizes to in-game procedural content (gameplay-model Spine D) as a refactor, not a rewrite. The primary trigger is a **headless CLI run-mode** of the existing host (`Server`), scriptable and needing no live server or web stack; a later admin verb is a possible thin second caller, noted but not built.

---

## Preconditions

- The **content-definition writer seam** exists and is composable from outside an admin command: `IAreaContentWriter`/`AreaContentWriter`, `IRoomContentWriter`/`RoomContentWriter`, `IItemContentWriter`/`ItemContentWriter`, `IMobContentWriter`/`MobContentWriter` are all live (all four shipped). The `*Template` types (`AreaTemplate`, `RoomTemplate`, `ItemTemplate`, `MobTemplate`) and their deserializers exist and round-trip.
- `IRandom`/`SystemRandom` exists at `Core/Systems/`; a seedable implementation can be constructed for a generation run (`SystemRandom` currently wraps shared randomness — see Resolved Decision 1).
- `WorldOptions.ContentDirectory` resolves the `content/` root; writers already key their subdirectories (`areas/`, `rooms/`, `items/`, `mobs/`) off it.
- A callable **`IContentValidator`** (factored out of `RegistryValidationBootstrap`) is provided by the **sibling T2 authoring track** and **lands first as a shared prerequisite** (Resolved Decision 2). This slice does not spec or build it; it depends on it for pre-reload validation. **Required capability:** the validator must validate a single composed definition **in-memory, without loading it as a live `EntityService` entity** (T2's single-definition call mode) — this is what lets generation validate without spawning world content (Resolved Decision 4). No interim re-deserialization-only validation path ships.
- `CompositionRoot.Register(...)` composes the engine's services and is reusable by a non-listener host path.
- `TemplateRegistry.Register(blueprintId, template)` supports all four template types (already used by the `mk*` builders).

---

## Postconditions

- **`Core/Modules/Authoring/`** module exists with `AddAuthoringModule(IServiceCollection)` wired from `CompositionRoot`.
- **`GenerationProfile`** (pure-data record) exists at `Core/Modules/Authoring/`: `{ int Seed, int AreaCount, (int Min,int Max) RoomsPerArea, (int Min,int Max) LevelRange, double MobDensity, double ItemDensity, IReadOnlyList<AspectMixEntry> AspectMix, ScalingCurve Scaling, string BlueprintPrefix }` (exact field set firmed in WP-1). A profile is the sole input that determines a run's output; two runs with identical profiles (same seed) produce byte-identical YAML.
- **`IContentGenerationSystem`** / **`ContentGenerationSystem`** exist at `Core/Modules/Authoring/Systems/`. Signature: `Task<GenerationResult> GenerateAsync(GenerationProfile profile, CancellationToken ct = default)`. The system: (a) constructs a deterministic `IRandom` from `profile.Seed`; (b) composes `*Template` instances (areas → their rooms → mobs/items placed in rooms, scaled by the curve and level range); (c) registers nothing in the live `TemplateRegistry` and creates **no** entities in `EntityService` (INV-12, INV-23 — YAML only); (d) calls the four `I*ContentWriter.WriteAsync` to emit YAML; (e) returns a `GenerationResult { int AreasWritten, int RoomsWritten, int MobsWritten, int ItemsWritten, IReadOnlyList<string> BlueprintIds, IReadOnlyList<string> ValidationErrors }`. The system **returns results; it never publishes** (INV-5).
- **Determinism (INV-26):** all randomness in `ContentGenerationSystem` flows through the injected `IRandom`. No `Random.Shared`, `DateTime.Now`, `Guid.NewGuid`, or other ambient non-determinism appears in the generation path (blueprint IDs are derived deterministically from the seed + a per-run counter, **not** from `Guid` as the `mk*` builders do — see Design notes).
- **`GenerationProfile` loading:** profiles are read from a YAML file path passed on the CLI (`--profile <path>`), deserialized with the same YamlDotNet camelCase convention the content writers use. A missing/invalid profile file fails fast with a clear error and a non-zero exit code.
- **Headless CLI run-mode:** `Server` recognizes a `generate` run-mode (e.g. `dotnet run --project Server -- generate --profile <path> [--seed N]`). In this mode the host composes services via `CompositionRoot`, hydrates the **definition registries** (Ability/Effect/Aspect/Stat — populated from code/YAML, creating **no** world entities) so cross-ref validation can run, but spawns **no** world-content entities (Resolved Decision 4), executes one `GenerateAsync` call, prints the `GenerationResult` summary to stdout, and **exits** — it does **not** start `TelnetServer`, `HeartbeatBackgroundService`, `PersistenceFlushTimer`, or subscribe gameplay handlers. It is a **no-chain Initiator (INV-10)**: a pure offline sweep that publishes no events. Exit code is `0` on success, non-zero if any YAML fails validation or a write fails.
- **Validation before exit:** emitted YAML is validated before the run reports success by calling the shared `IContentValidator` (Resolved Decision 2). `GenerationResult.ValidationErrors` is non-empty ⇒ non-zero exit; the report lists each offending file.
- `docs/architecture/flows/README.md` gains Flow 29 (headless bulk content generation).
- `docs/reference/systems.md` gains `IContentGenerationSystem` under Domain Systems (Authoring).
- `docs/reference/commands.md` is **unchanged** (no in-game command; CLI run-mode is not a verb). A note is added to the run-mode/operations doc if one exists.

---

## Main Flow

### Flow 29 — Headless bulk content generation (`generate` run-mode)

1. **Run-mode dispatch.** `Server` `Main` inspects `args`. On the `generate` token it branches to the generation run-mode path instead of building the listener host; `--profile <path>` and optional `--seed N` (overrides the profile's seed) are parsed. Missing `--profile` ⇒ usage error + non-zero exit.
2. **Compose + hydrate.** The run-mode builds a host via `CompositionRoot.Register(...)` (shared composition), composing **bootstraps-only** hosted services — the **definition-registry** hydration needed for cross-ref validation runs, but `TelnetServer`/`HeartbeatBackgroundService` and the world-content **spawn** path are suppressed. No `EntityService` world-content entities are spawned (Resolved Decision 4) — INV-12/INV-23 hold.
3. **Load profile.** The profile YAML at `--profile` is deserialized into a `GenerationProfile`; `--seed` overrides `profile.Seed` if present. Invalid profile ⇒ fail fast, non-zero exit.
4. **Generate (deterministic).** The run-mode calls `IContentGenerationSystem.GenerateAsync(profile)`. The system seeds an `IRandom` from `profile.Seed` and composes `AreaTemplate` + child `RoomTemplate`s (with exits), then places `MobTemplate`s and `ItemTemplate`s per density/level-range/scaling-curve, rolling every choice through `IRandom`. Blueprint IDs are derived deterministically (prefix + zero-padded counter), not via `Guid`.
5. **Write YAML.** For each composed template the system calls the matching `I*ContentWriter.WriteAsync`, emitting files under `content/areas|rooms|items|mobs/`. Writers use their existing atomic tmp→rename path. No live-world mutation (INV-12, INV-23).
6. **Validate.** Each written file is validated through the shared `IContentValidator` (Resolved Decision 2). Failures accumulate into `GenerationResult.ValidationErrors`.
7. **Report + exit.** The run-mode prints the `GenerationResult` summary (counts + first N blueprint IDs + any validation errors) to stdout and exits with code `0` (clean) or non-zero (validation/write failure). No events are published; no telnet listener or heartbeat ever starts (INV-10).

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| *(none)* | — | — | Pure offline sweep. The `generate` run-mode is a **no-chain Initiator (INV-10)**; `IContentGenerationSystem` returns results and never publishes (INV-5). |

> If a later admin-verb caller is built (deferred — see Design notes), *that* Initiator would publish a `ContentGeneratedByAdminEvent` for audit, and `AdminAuditHandler` would handle it. Not in this slice's scope.

---

## Design Notes

> Slice-specific seam rationale. Platform-wide rationale lives in [`content-tooling-platform.md`](content-tooling-platform.md) Design notes + Architecture brief (INV-27); not restated here.

- **Bulk generation is a domain system over the existing writer seam (platform brief, "build the family seam now").** `IContentGenerationSystem` composes `I*ContentWriter` + `*Template` and emits YAML; it does not re-implement authoring logic. This is the brief's family disposition realized: the four writers already shipped, so the generator is a thin composer. The system returns a `GenerationResult` and never publishes (INV-5) — the run-mode Initiator owns process-level concerns.

- **Deterministic blueprint IDs, not `Guid`.** The `mk*` builders (`AreaBuilderSystem`, etc.) derive ad-hoc blueprint IDs from `Guid.NewGuid()` — correct for interactive one-offs but fatal to reproducibility. The generator instead derives IDs deterministically from `profile.BlueprintPrefix` + a per-run monotonic counter (e.g. `gen.area.0001`), so a fixed-seed run is byte-reproducible. This is the load-bearing reason generation cannot simply call the `mk*` builders — it composes the *writer* half of the seam, not the live-spawn half (which is exactly the split the platform brief's "builders fuse two concerns" note calls out).

- **No-chain Initiator (INV-10), justified.** The `generate` run-mode is a pure offline sweep: it composes services, runs one operation, writes files, and exits. It drives no gameplay, has no witnesses, and produces nothing the event bus needs to observe. Per INV-10 it publishes nothing and starts no heartbeat/listener. (The deferred admin-verb caller would run in a live context and *would* publish an audit event — a different Initiator, not this one.)

- **Profile-keyed for forward generalization (platform brief, "shape for later").** Keying generation on a `GenerationProfile` (rather than loose parameters) is the seam that lets the eventual in-game procedural-content feature (gameplay-model Spine D / feature-horizon "Procedural / generated areas") reuse `IContentGenerationSystem` by supplying a runtime-built profile instead of a file-loaded one, and writing to a live-spawn sink instead of the YAML writers. Recorded as a generalization; **the procedural-gameplay feature is not built here.**

- **Validation depends on the sibling track's `IContentValidator`, sequenced first.** Pre-reload, on-demand validation is the platform brief's INV-19 framework-parity obligation, **owned by T2** and shared. Per Resolved Decision 2 the validator factoring lands **before** this slice's validation WP, so T1 consumes it directly rather than shipping a throwaway re-deserialization-only path. The richer cross-reference validation (ability/aspect/effect/area composition, as `RegistryValidationBootstrap` does today) is exactly what the shared validator provides; the emitted-YAML round-trip remains a *test* (test 5), not the runtime validation mechanism.

- **The run-mode is a host concern, not a Core concern.** Generation *logic* lives entirely in `ContentGenerationSystem` (Core); the CLI argument parsing, service composition without gameplay hosted services, and process exit code live in `Server` (the host owns process entry points). This keeps the system testable without a process and honors INV-8 (thin Initiator).

---

## Related

- [`content-tooling-platform.md`](content-tooling-platform.md) — the architecture-advisor platform brief this slice (T1) extends; owns the shared seam rationale, family disposition, and resolved decisions. The Blazor authoring track (T2) and the shared `IContentValidator` live there.
- [`admin-area-authoring.md`](admin-area-authoring.md) — `IAreaBuilderSystem` + `IAreaContentWriter`; the builder/writer pattern this slice composes (the *writer* half).
- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — `IRoomBuilderSystem` / `IRoomContentWriter` patterns.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — `WorldContentLoader`, `TemplateRegistry`, the `reload` path that applies generated content to a live world (out of scope here).
- [`../design/feature-horizon.md`](../design/feature-horizon.md) — "Procedural / generated areas" (`[D, E]`), the gameplay generalization of `IContentGenerationSystem`.
- [`../architecture/checklist.md`](../architecture/checklist.md) — invariants cited: INV-5, INV-8, INV-10, INV-12, INV-15, INV-18, INV-19, INV-23, INV-25, INV-26.

---

