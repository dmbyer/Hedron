# Use Case: Headless Bulk Content Generation (`IContentGenerationSystem` → validated YAML)

**Status:** planned
**Actors:** Developer (headless CLI), System
**Module:** new `Core/Modules/Authoring/` (`IContentGenerationSystem`, `GenerationProfile`, `GenerationResult`); `Server/` (headless CLI run-mode entry point); reuses `Core/Modules/World/`, `Core/Modules/Items/`, `Core/Modules/Mobs/` writers + templates.

> **Track T1 of the [content-tooling platform](content-tooling-platform.md).** The shared architecture, seam rationale, family disposition, and resolved decisions live in that platform brief (INV-D1); this slice references them rather than restating. This is the bulk-generation track named in the platform's scope note; the Blazor authoring track (T2) is a sibling slice. The two share only the content-definition writer seam, so neither blocks the other.

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

## Systems / Handlers Involved

| Artifact | Reuse / New | Location |
|---|---|---|
| `IContentGenerationSystem` / `ContentGenerationSystem` | New | `Core/Modules/Authoring/Systems/` |
| `GenerationProfile`, `GenerationResult`, `AspectMixEntry`, `ScalingCurve` | New (pure data) | `Core/Modules/Authoring/` |
| `AuthoringModule` (`AddAuthoringModule`) | New | `Core/Modules/Authoring/` |
| `generate` run-mode entry point | New | `Server/` (`Program.cs` branch or `GenerationRunMode.cs`) |
| `IAreaContentWriter` / `AreaContentWriter` | Reused | `Core/Modules/World/Systems/` |
| `IRoomContentWriter` / `RoomContentWriter` | Reused | `Core/Modules/World/Systems/` |
| `IItemContentWriter` / `ItemContentWriter` | Reused | `Core/Modules/Items/Systems/` |
| `IMobContentWriter` / `MobContentWriter` | Reused | `Core/Modules/Mobs/Systems/` |
| `AreaTemplate`, `RoomTemplate`, `ItemTemplate`, `MobTemplate` | Reused | per-module `Templates/` |
| `IRandom` (seedable instance) | Reused / adjusted (see OQ1) | `Core/Systems/` |
| `IContentValidator` | Reused (landed in WP-1) — precondition | `Core/Modules/World/Systems/` |
| `CompositionRoot` | Reused | `Server/` |
| `WorldOptions` | Reused | `Core/Modules/World/` |

---

## Implementation Plan — Work Packages

### WP-1 — `GenerationProfile` + `IContentGenerationSystem` core (lands first)

**Scope:** the `Authoring` module, the profile/result data types, and `ContentGenerationSystem.GenerateAsync` composing the four reused writers, fully driven by an injected `IRandom`. Deterministic blueprint-ID derivation. No CLI, no validation wiring yet (validation may be stubbed to re-deserialize in WP-2).

**Files:**
- `Core/Modules/Authoring/GenerationProfile.cs`, `GenerationResult.cs`, `AspectMixEntry.cs`, `ScalingCurve.cs`
- `Core/Modules/Authoring/Systems/IContentGenerationSystem.cs`, `ContentGenerationSystem.cs`
- `Core/Modules/Authoring/AuthoringModule.cs` (`AddAuthoringModule`)
- `Server/CompositionRoot.cs` (call `AddAuthoringModule`)
- `Hedron.Tests/Modules/Authoring/Systems/ContentGenerationSystemTests.cs`

**Exit criterion:** `GenerateAsync` with a fixed-seed profile, given a fake/seeded `IRandom`, produces an identical set of templates across two runs (determinism); writers are invoked the expected number of times for given counts/densities. `dotnet test` green.

**Out of scope:** CLI run-mode, profile-file loading, validation of emitted files.

---

### WP-2 — `generate` CLI run-mode + profile loading + output validation (depends on WP-1)

**Scope:** the `Server` run-mode branch that parses `generate --profile <path> [--seed N]`, composes services without gameplay hosted services, loads + deserializes the profile YAML, runs one `GenerateAsync`, validates emitted files via the shared `IContentValidator`, prints the summary, and sets the exit code. Flows + reference updates. **Depends on the shared `IContentValidator` (T2) having landed (Resolved Decision 2).**

**Files:**
- `Server/GenerationRunMode.cs` (or a branch in `Server/Program.cs`)
- `Server/Program.cs` (run-mode dispatch on `args`)
- a sample `content/profiles/example.yaml` (or `docs/`-adjacent sample) documenting the profile shape
- `Hedron.Tests/Modules/Authoring/...` (profile round-trip + emitted-YAML round-trip tests)
- `docs/architecture/flows/README.md` + `docs/architecture/flows/flow-29-bulk-content-generation.md`
- `docs/reference/systems.md`

**Exit criterion:** `dotnet run --project Server -- generate --profile <sample>` writes valid YAML under `content/`, prints a summary, exits `0`; a deliberately broken profile or write produces non-zero exit. Emitted YAML round-trips through the existing deserializers. `dotnet build` + `dotnet test` green.

**Out of scope:** admin verb caller, applying generated content to a live world (`reload`), the shared `IContentValidator` factoring (T2), procedural in-game generation.

---

## Content Tooling Impact

- **`generate` CLI run-mode** — `dotnet run --project Server -- generate --profile <path> [--seed N]` is the new authoring surface this slice ships. It is the inspect/author tooling for the bulk content it produces: the run prints a summary (counts + blueprint IDs) so the developer can verify and locate generated content, and the output is plain reviewable/version-controllable YAML under `content/`.
- **`GenerationProfile` YAML shape** — a new authored data-file shape (camelCase, same YamlDotNet convention as content files). A documented sample profile ships in WP-2 so a developer can author and edit runs.
- **No new `TemplateRegistry` shape** — generation composes the existing `AreaTemplate`/`RoomTemplate`/`ItemTemplate`/`MobTemplate` types; the emitted YAML is loaded by `WorldContentLoader` on the next `reload`/restart, exactly like hand-authored content.
- **No new content-YAML field shape** — emitted files use the exact DTO fields the existing deserializers read (validated by the round-trip test).

---

## Cross-Cutting Surfaces Stressed

**Commands framework:** Not stressed — no in-game command. The trigger is a CLI run-mode, not an `ICommand`. (A later admin verb is deferred; it would reuse the existing command framework with `AdminRequirement`.)

**Output framework:** Not stressed in-engine — the run-mode writes a summary to stdout (console), not to an `ISession`/`IOutputWriter`. No new `IOutputMessage` type.

**Content writing:** Adequate — the four `I*ContentWriter`s already exist and are the composition target. **This is the platform brief's "build the family seam now" disposition realized:** all four writers ship, so `IContentGenerationSystem` is a thin composer, not a per-type re-implementation (INV-19 family seam already paid down).

**Event bus:** Not stressed — no publish (INV-10 no-chain Initiator; INV-5 system returns results).

**ECS queries:** Not stressed — generation creates **no** entities and reads no live components; it composes templates and writes YAML. This is the INV-12/INV-23 guard: the generator never touches the live world model.

**Time:** **Gap-adjacent (resolved by design).** Generation must be deterministic (INV-26), so it must not read the wall clock or `Guid`. The `mk*` builders derive blueprint IDs from `Guid.NewGuid()` — non-deterministic. The generator therefore derives IDs from the seed + a counter instead. No new time seam is introduced; the existing `IRandom` seam is the determinism source. See Resolved Decision 1 on the seedable `IRandom`.

**Configuration:** Adequate — `WorldOptions.ContentDirectory` already drives the writers' output paths. The profile path is a CLI argument, not a config key.

**Persistence — entity domain classification:** No entities are constructed by this slice. Generated content is **world content** (INV-23): areas/rooms/world-spawn items/mobs that live as YAML and spawn fresh on load. No `PersistentEntity` anywhere. The generator produces definitions, never live or persistent entities.

**Persistence — component inclusion:** No components introduced or touched. The reused `*Template.Apply` paths (which attach world-content components) are **not** invoked by the generator — it writes YAML, it does not `Apply`. No `[Persistent]` question arises.

**Persistence — save-on-change scope:** No `SaveEntityAsync` call anywhere (INV-22 satisfied trivially — world content is YAML-only, never SQLite).

**Broadcast / Sessions:** Not stressed — headless; no session context.

**Modules:** Adequate — new `Core/Modules/Authoring/` is the home, composed via `AddAuthoringModule` from `CompositionRoot` (standard module pattern). The `generate` run-mode lives in `Server` (the host owns process entry points and run-modes).

**Host run-mode:** **Gap exposed (resolved in this slice, WP-2).** `Server` currently has a single run-mode: build the listener host and `RunAsync`. A headless one-shot mode that composes services, runs one operation, and exits is new. The branch is small and self-contained; it lands in WP-2. (Surfaced rather than absorbed silently, per ground rule 9. Disposition: framework slice lands in-slice, not deferred.)

---

## Flows Introduced or Modified

| # | Flow | Change |
|---|---|---|
| 29 | Headless bulk content generation (`generate` run-mode) | New — append row to `flows/README.md`; create `flow-29-bulk-content-generation.md` |

No existing flow is modified. Flow 29 is the first headless one-shot run-mode flow; it shares no call chain with Flow 1 (server startup / listener path). It is structurally a compose → load profile → generate → write → validate → exit sequence with no event fan-out.

---

## Test Plan / Verification

**System-unit tests (tier: system-unit, `Hedron.Tests/Modules/Authoring/Systems/ContentGenerationSystemTests.cs`):**

1. `ContentGeneration_SameSeed_IsDeterministic` — call `GenerateAsync` twice with the same profile + a deterministic `IRandom` seeded identically; assert the two `GenerationResult` blueprint-ID lists and the captured writer inputs (templates) are equal. **This is the INV-26 reproducibility contract** — the property that makes reproducible scaling-regression worlds possible.
2. `ContentGeneration_DifferentSeed_DiffersStructurally` — two different seeds produce different blueprint sets / placements (guards against the seed being ignored).
3. `ContentGeneration_RespectsProfileCounts` — `AreaCount`, `RoomsPerArea`, and density inputs map to the expected number of writer calls / result counts (using fake writers that record invocations).
4. `ContentGeneration_RollsThroughInjectedRandom` — inject a counting/fake `IRandom`; assert no output varies when the wall clock / ambient state changes (i.e. the system has no hidden non-determinism). Architecturally this is the INV-26 seam assertion.

**Round-trip / integration tests (tier: persistence/flow-adjacent, same test project):**

5. `ContentGeneration_EmittedYaml_RoundTrips` — run a small generation against a temp `content/` dir; deserialize each emitted file with its existing deserializer (`AreaTemplateDeserializer`, `RoomTemplateDeserializer`, `ItemTemplateDeserializer`, `MobTemplateDeserializer`) and assert it loads without error and key fields survive. **This is the emitted-YAML-validity contract.**
6. `GenerationProfile_RoundTrips` — a sample profile YAML deserializes into the expected `GenerationProfile` (guards the new authored data shape).

**Fail-fast test:**

7. `GenerationRunMode_InvalidProfile_NonZeroExit` (tier: handler/run-mode) — a missing/malformed profile file yields a non-zero exit and a clear error. (May be a thin run-mode test; if the run-mode is hard to test in isolation, assert on the profile-loader method instead and note the run-mode plumbing as skipped.)

**Legitimately skipped (per rubric in `docs/architecture/07-testing.md`):**

- `GenerationProfile` / `GenerationResult` / `AspectMixEntry` / `ScalingCurve` — pure-data records; no logic.
- The four `I*ContentWriter`s — reused thin I/O adapters already accepted by the suite; covered transitively by the round-trip test (5).
- The `generate` run-mode argument-parsing plumbing — thin host wiring; the decision logic lives in `ContentGenerationSystem` (covered above) and the profile loader (test 6/7). Exact stdout prose is presentation, skipped.

**Coverage contract:** the postconditions asserting player-invisible internal state — (a) determinism under fixed seed, (b) no ambient non-determinism in the generation path (INV-26), (c) emitted YAML round-trips through the deserializers, (d) profile-count fidelity — map to tests 1/4, 4, 5, and 3 respectively.

---

## Design Notes

> Slice-specific seam rationale. Platform-wide rationale lives in [`content-tooling-platform.md`](content-tooling-platform.md) Design notes + Architecture brief (INV-D1); not restated here.

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

## Resolved Decisions

*(Settled at the architecture-advisor intake; see [`content-tooling-platform.md`](content-tooling-platform.md) Open questions. No longer open for the spec gate.)*

1. **`IRandom` gains a seedable implementation.** `SystemRandom` (the DI singleton) stays the production binding for ambient randomness, but a small **`SeededRandom : IRandom`** (backed by `new Random(seed)`) is added so `ContentGenerationSystem` constructs a deterministic per-run instance from `profile.Seed` instead of consuming the singleton. This satisfies the INV-26 determinism postcondition. WP-1 owns this addition. **Determinism scope:** reproducibility is **within a runtime/CI image** — `new Random(seed)` is deterministic per run but its sequence is not guaranteed stable across .NET versions, which is sufficient for reproducible scaling-regression worlds. "Byte-identical" claims below mean within-runtime; a stable PRNG can replace `new Random(seed)` later behind the same seam without reshaping it.
2. **Shared `IContentValidator` lands first — no interim throwaway validation.** The callable validator factored out of `RegistryValidationBootstrap` is a **shared prerequisite** (owned by T2) sequenced **before** this slice's validation WP. T1 depends on it directly; it does **not** ship self-contained re-deserialization-only validation as a stopgap. (The emitted-YAML round-trip *test* — test 5 — still exists as a test, but per-run validation routes through the shared validator.) This is a light, deliberate serialization of the two parallel tracks: the validator is a small factoring, and sharing it avoids throwaway code.
3. **Generated rooms form a connected graph.** Generation wires `RoomTemplate.Exits` into a walkable graph within each area (e.g. a chain/grid) and makes areas reachable, so a generated world can be traversed by a character to test mob/item scaling as skill grows — a flat, exit-less room set is **not** the v1 target. Profile knobs confirmed: area count, rooms-per-area range, level range, mob/item density, aspect mix, scaling curve, seed, blueprint prefix.
4. **Registry-hydration posture (resolves the former dangling "Open question 2" — spec-gate SR-5).** The `generate` run-mode hydrates the **definition registries** (Ability/Effect/Aspect/Stat — Spine F, populated from code/YAML) so the validator's cross-reference checks can run, but spawns **no world-content entities** (no area/room/mob ECS entities in `EntityService`). Validation routes through T2's **single-definition (in-memory) call mode** (`Validate(ContentDefinition)`), never the live-`AreaComponent`-scan mode the boot bootstrap uses. This is the load-bearing distinction that keeps INV-12/INV-23 satisfiable: *definition registries* are not *world entities*, so loading the former to validate does not violate "the generator creates no live world entities." The T1↔T2 contract is therefore explicit: T2's validator must expose a single-definition overload that needs no live entities (confirmed in T2's design — its two call modes).
