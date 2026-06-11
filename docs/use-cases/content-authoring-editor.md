# Use Case: Offline Blazor Content-Authoring Editor (T2)

**Status:** planned
**Actors:** Content designer / Administrator (offline, at a localhost browser)
**Module:** new `Core/Modules/Authoring/` (`IContentDefinitionCatalog` facade + per-kind definition operations); new `Core/Modules/World/Systems/IContentValidator` factored from `RegistryValidationBootstrap`; new `Hedron.Web` Blazor Server host (boots the engine via `CompositionRoot.Register`); reuses the existing `I*ContentWriter` family, `*TemplateDeserializer`s, `IContentSerializer`, `ITemplateRegistry`, and the `reload` Initiator.

> **Platform brief.** This is track **T2** of the [content-tooling platform](content-tooling-platform.md). The platform doc owns the durable seam rationale, the family/host dispositions, the invariants-in-tension table, and the resolved-decisions intake (INV-D1) — read it first. This doc does **not** restate that rationale; it folds the brief's seam decisions into Design notes and decomposes the T2 track into shippable work packages. The sibling **T1** (headless bulk-generation, `IContentGenerationSystem`) is a separate slice that shares only the `IContentValidator` and `IContentDefinitionCatalog` seams this track owns.

---

## Description

Content authoring today is command-driven over telnet (`mkarea`, `mkitem`, `dig`, `set*`): each verb is a thin caller of a builder system and an `I*ContentWriter`, mutating the live world *and* writing YAML in one fused step. That is fine for spot edits but tedious for assembling rooms/areas/items/mobs at volume. This slice adds an **offline Blazor Server editor**, hosted in-process with the engine, that **reads, lists, loads, edits, validates, and writes the YAML content definitions** — and never mutates the live world. "Apply to live" is a thin button that calls the existing `reload` Initiator (`ReloadCommand` path → `ContentReloadedEvent`), which re-derives world content from YAML; the editor itself stays entirely off the heartbeat (INV-12 preserved, all live-edit concurrency deferred).

The editor re-implements **no** authoring logic. All read/list/load/edit/validate/write logic lives in a shared **content-definition layer** (`IContentDefinitionCatalog`) factored out of the existing builders/writers and the boot-time validator. Blazor components are thin adapters over the catalog — the fat-component analogue of a fat command is the anti-pattern this track guards against (INV-8 extended). The on-demand `IContentValidator` factored out of `RegistryValidationBootstrap` is the INV-19 framework-parity obligation for the new surface; the boot hosted-service, the editor, and the future generator all call it.

---

## Preconditions

- `content-tooling-platform.md` brief exists and its resolved decisions stand (offline-only target, Blazor Server in-process, separate `Hedron.Web` host, loopback-only auth for v1).
- `IAreaContentWriter`/`AreaContentWriter`, `IRoomContentWriter`/`RoomContentWriter`, `IItemContentWriter`, `IMobContentWriter` all exist (the write-half of the family is already shipped).
- `AreaTemplateDeserializer`, `RoomTemplateDeserializer`, `ItemTemplateDeserializer`, `MobTemplateDeserializer` exist and are registered through `IContentSerializer.Deserialize(kind, body)`.
- `ITemplateRegistry` (`AllBlueprintIds`, `TryGet`, `Register`, `Clear`) and `IWorldContentLoader.ReloadAsync` exist.
- `RegistryValidationBootstrap` exists (hosted service) and contains the validation logic to be factored out.
- `CompositionRoot.Register(IServiceCollection, IConfiguration)` is the shared engine DI entry point; `Server` is a plain `Microsoft.NET.Sdk` Exe and must remain headless-runnable.
- `WorldOptions.ContentDirectory` resolves the `content/` root; the per-kind subdirectories are `areas/`, `rooms/`, `items/`, `mobs/`.

---

## Postconditions

- **Definition layer.** `Core/Modules/Authoring/IContentDefinitionCatalog.cs` + `ContentDefinitionCatalog.cs` exist. The catalog exposes, over the four content kinds (`area`/`room`/`item`/`mob`):
  - `IReadOnlyList<ContentSummary> List(ContentKind kind)` — enumerates definitions on disk (id, name, short-desc) by reading the kind subdirectory.
  - `ContentDefinition? Load(ContentKind kind, string blueprintId)` — reads + deserializes one YAML file into an editable definition DTO.
  - `Task<ContentWriteResult> SaveAsync(ContentDefinition definition, CancellationToken ct)` — validates then writes via the matching `I*ContentWriter`; refuses to write on validation failure.
  - `ContentDefinition CreateNew(ContentKind kind, string name)` — produces a new, un-persisted definition with a generated blueprint id (mirrors the `*.adhoc.<base36>` id pattern from the builders, without creating a live entity).
  - The catalog is a **thin facade** over per-kind operations; it owns no live-entity creation (that half stays in the `mk*` builders).
- **Callable validator.** `Core/Modules/World/Systems/IContentValidator.cs` + `ContentValidator.cs` exist. `ValidationReport Validate(...)` runs the referential-integrity + composition checks currently inline in `RegistryValidationBootstrap`, returning structured errors (never throwing). `RegistryValidationBootstrap` is refactored to call `IContentValidator` and keep its fail-fast-and-throw boot behavior; the editor calls the same validator per-edit and surfaces errors in the UI.
- **No live-world mutation from the editor.** The catalog reads/writes YAML only. It never calls `EntityService.CreateEntity`, never adds `PersistentEntity`, never calls `SaveEntityAsync`. Apply-to-live is solely via `reload`.
- **Split hosted-service registration.** `CompositionRoot.Register` is refactored to register **DI services only**; the six `AddHostedService` calls move out so each host composes its own set (e.g. a `services.AddGameplayHostedServices()` / `services.AddContentBootstrapHostedServices()` split, exact shape per WP-2). `Server` composes the full gameplay set (telnet + heartbeat + all bootstraps + flush timer); behavior is unchanged. This is the seam that scales one process to the deferred three-suite superset.
- **Blazor host.** New project `Hedron.Web` (`Microsoft.NET.Sdk.Web`) boots the engine via `CompositionRoot.Register(...)`, composes **bootstraps-only** hosted services (content load + registry validation — no telnet, no heartbeat), adds Blazor Server services, and **binds loopback-only** (`http://127.0.0.1:<port>`). `Server` is unchanged and still runs headless telnet-only.
- **Editor surfaces.** Blazor pages: a content browser (list all four kinds), a definition editor for **areas and rooms** (load → edit fields → validate → save), a "Create new" flow for areas and rooms, and an "Apply to live (reload)" action that calls the reload path and shows the `ContentReloadResult` counts. Items and mobs are **list/read-only** in this slice (full edit lands in WP-3).
- **Catalog reload coupling.** The editor's "Apply to live" calls a thin caller of `IWorldContentLoader.ReloadAsync` and renders the counts. No new event type is introduced; if the reload runs in a context with an admin identity, the existing `ContentReloadedEvent` is reused (publishing belongs to the Initiator, not the catalog — INV-5).
- **No event subscriptions in the web host (v1).** `Hedron.Web` composes **no** `bus.Subscribe(...)` wiring (all of which lives in `Server/Program.cs` today). This is deliberate: authoring is off the bus, the host runs no heartbeat/combat/session handlers, and it spawns no players to hydrate. The apply step's `reload` publishes `ContentReloadedEvent` from its Initiator, but the web host need not subscribe any handler to it for v1. (When the deferred player/admin suites land, they add their own subscriptions to this host's composition.)
- **Docs.** `docs/architecture/flows/README.md` gains Flow 29 (offline content edit → save → apply). `docs/reference/systems.md` gains `IContentDefinitionCatalog` (Authoring) and `IContentValidator` (World). `docs/documentation-architecture.md` / module map notes the new `Hedron.Web` host. `docs/reference/components.md` is unchanged (no new components).
- **Agent tooling (INV-20).** The split hosted-service registration + the second engine host make the "register in `Server/Program.cs`" / "root DI composition" guidance in `.claude/skills/add-core-system/SKILL.md` (step 2) and `.claude/skills/add-domain-system/SKILL.md` (step 2) stale. Both are updated in this slice (WP-2 task) to note (a) the engine now has **two hosts** (`Server`, `Hedron.Web`) booting via the shared `CompositionRoot.Register`, and (b) **hosted services are composed per-host, not in `Register`** — a new hosted service is added to each host's set, not to `Register`.

---

## Main Flow

### Flow 29 — Offline content edit → save → apply (`Hedron.Web`)

1. **Browse.** Designer opens the loopback Blazor app; the browser page calls `IContentDefinitionCatalog.List(kind)` for the selected kind and renders the table (id | name | short-desc).
2. **Load.** Designer picks a definition; the editor page calls `Load(kind, blueprintId)`, deserializing the YAML into an editable `ContentDefinition` DTO bound to the form.
3. **Edit.** Designer mutates fields in the Blazor form (name, description, exits, aspect affinities, etc.). No catalog call yet — the form holds a working copy.
4. **Validate (on demand).** On a validate/save action, the page calls `IContentValidator.Validate(...)` (directly or via the catalog) against the working copy; structured errors render inline. Invalid definitions block the write.
5. **Write.** On save of a valid definition, the page calls `IContentDefinitionCatalog.SaveAsync(definition)`, which re-validates and writes YAML through the matching `I*ContentWriter` (atomic tmp → rename). The live world is untouched.
6. **Apply to live.** Designer clicks "Apply"; a thin host service calls `IWorldContentLoader.ReloadAsync()`, the new templates are seeded into the live world, and the page renders the `ContentReloadResult` counts. Existing live entities are not mutated (the documented `reload` contract).

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `ContentReloadedEvent` (reused) | reload Initiator (apply action) | `int TemplatesLoaded, int TemplatesUnchanged, int TemplatesRemoved` | Existing audit/notify path; reused, not redefined. |

No events from `IContentDefinitionCatalog` or `IContentValidator` (domain systems return results — INV-5). The editor's read/list/load/save are off the event bus; only the apply step touches the existing reload path.

---

## Systems / Handlers Involved

| Artifact | Reuse / New | Location |
|---|---|---|
| `IContentDefinitionCatalog` / `ContentDefinitionCatalog` | New | `Core/Modules/Authoring/Systems/` |
| `ContentDefinition`, `ContentSummary`, `ContentKind`, `ContentWriteResult` DTOs | New | `Core/Modules/Authoring/` |
| `IContentValidator` / `ContentValidator` | New (factored from bootstrap) | `Core/Modules/World/Systems/` |
| `RegistryValidationBootstrap` | Refactored to call `IContentValidator` | `Server/` |
| `AuthoringModule` (`AddAuthoringModule`) | New | `Core/Modules/Authoring/` |
| `IAreaContentWriter`, `IRoomContentWriter`, `IItemContentWriter`, `IMobContentWriter` | Reused | `Core/Modules/{World,Items,Mobs}/Systems/` |
| `IContentSerializer` (`Deserialize(kind, body)`) | Reused (read-side) | `Core/Systems/` |
| `ITemplateRegistry`, `IWorldContentLoader` | Reused | `Core/Systems/`, `Core/Modules/World/Systems/` |
| Blazor pages/components (browser, editor, apply) | New (presentation) | `Hedron.Web/Components/` |
| `Hedron.Web` host (`Program.cs`, boots `CompositionRoot`) | New | `Hedron.Web/` |
| `reload` Initiator path (`IWorldContentLoader.ReloadAsync`, `ContentReloadedEvent`) | Reused | `Core/Modules/Admin/`, `Core/Modules/World/` |

---

## Implementation Plan — Work Packages

### WP-1 — Content-definition layer + callable validator (lands first; T1 also depends on it) — ✅ implemented

> **Status:** landed. `IContentValidator`/`ContentValidator`/`ValidationReport` (World module), `IContentDefinitionCatalog`/`ContentDefinitionCatalog` + `ContentKind`/`ContentDefinition`/`ContentSummary`/`ContentWriteResult`/`AdhocBlueprintId` (new Authoring module), `AuthoringModule.AddAuthoringModule` wired in `CompositionRoot`, and `RegistryValidationBootstrap` refactored to delegate to `IContentValidator`. Tests: `Hedron.Tests/World/ContentValidatorTests.cs`, `Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs`, and the existing `Registry/RegistryValidationTests.cs` (helper retargeted; cases unchanged). `reference/systems.md` updated. Build + 603 tests green. WP-2 (Blazor host, split hosted-service registration) and WP-3 (items/mobs editing) remain.

**Scope:** Factor the read/list/load/edit/write seam and the on-demand validator. No Blazor, no host changes.

- `Core/Modules/Authoring/ContentKind.cs` — enum `{ Area, Room, Item, Mob }` with kind-string + subdirectory mapping.
- `Core/Modules/Authoring/ContentDefinition.cs`, `ContentSummary.cs`, `ContentWriteResult.cs` — editable DTOs / results. `ContentDefinition` wraps the relevant `*Template` (or a flat editable projection of it) plus `ContentKind`.
- `Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs` + `ContentDefinitionCatalog.cs` — `List` / `Load` / `SaveAsync` / `CreateNew`. Reads via directory scan + `IContentSerializer.Deserialize`; writes via the matching `I*ContentWriter`; `SaveAsync` calls `IContentValidator` and refuses to write on failure. Owns the `*.adhoc.<base36>` id generation for `CreateNew` (extracted from the builders' helper, or a shared `IBlueprintIdGenerator`).
- `Core/Modules/World/Systems/IContentValidator.cs` + `ContentValidator.cs` — pure-decision validator returning `ValidationReport` (never throws). Lift the cross-ref + composition logic out of `RegistryValidationBootstrap`.
- `Server/RegistryValidationBootstrap.cs` — refactor to inject `IContentValidator`, call it, and keep its boot-time throw-on-error behavior.
- `Core/Modules/Authoring/AuthoringModule.cs` — `AddAuthoringModule(IServiceCollection)`; wire the catalog. Validator wired in `WorldModule` (its home module). Call `AddAuthoringModule` from `CompositionRoot.Register`.
- Tests (see Test Plan): catalog list/load/save round-trips per kind; validator accepts valid + rejects malformed content; bootstrap-still-throws guard.

**Exit criterion:** `dotnet build` + `dotnet test` green. Catalog round-trips an area/room YAML; `IContentValidator.Validate` flags a deliberately broken aspect composition; `RegistryValidationBootstrap` still throws on boot for invalid content. **T1 (bulk-gen) is unblocked.**

**Out of scope:** Blazor, the web host, any UI.

### WP-2 — `Hedron.Web` Blazor host + areas/rooms editing (depends on WP-1)

**Scope:** Stand up the in-process Blazor Server host and the first edit surfaces.

- `Hedron.Web/Hedron.Web.csproj` (`Microsoft.NET.Sdk.Web`), `ProjectReference` → `Core`; `Program.cs` calls `CompositionRoot.Register(builder.Services, builder.Configuration)`, adds Blazor Server, and **binds `http://127.0.0.1:<port>` only** (Kestrel loopback; document that real authn/z gates any non-local bind later).
- `Hedron.Web/appsettings.json` — same content/world config the engine consumes.
- Blazor components: content **browser** (lists all four kinds via `List`), **area editor**, **room editor** (load → edit → validate → save via the catalog), **create-new** for area + room, **apply-to-live** action (thin caller of `ReloadAsync`, renders counts).
- **Host hosted-service set (resolved — split registration).** `CompositionRoot.Register` is refactored to do **pure DI only**; the `AddHostedService` calls (`TelnetServer`, `HeartbeatBackgroundService`, `PersistenceBootstrap`, `WorldContentBootstrap`, `RegistryValidationBootstrap`, `PersistenceFlushTimer`) move out of `Register` and each host composes its own set. `Server` composes the full gameplay set; `Hedron.Web` v1 composes **bootstraps only** (content load + registry validation, so the validator's live-scan has data) and runs **neither** `TelnetServer` nor `HeartbeatBackgroundService`. A host-role *flag inside* `Register` is rejected (it grows a conditional arm per host; three surfaces are planned). This split is the seam that lets the same `Hedron.Web` process later compose the **superset** (engine + heartbeat + SignalR player sessions + admin) without reshaping `Register`. See Design notes.
- `docs/architecture/flows/README.md` + `flow-29-offline-content-edit.md`; `docs/reference/systems.md` rows; module-map note for `Hedron.Web`.
- **Update `.claude/skills/add-core-system/SKILL.md` and `.claude/skills/add-domain-system/SKILL.md`** (step 2 in each) for the two-host / per-host hosted-service reality (INV-20 — see Postconditions "Agent tooling").

**Exit criterion:** `dotnet build` green for the new host; launching `Hedron.Web` on loopback lets a designer create + edit an area and a room, save YAML, and apply via reload. Blazor components are presentation/skip-tier (no unit tests — see Test Plan).

**Out of scope:** items/mobs editing; any live-world mutation; player-facing UI / SignalR.

### WP-3 — Items & mobs editing (fast-follow; depends on WP-2)

**Scope:** Extend the editor with item and mob create/edit/write using the same catalog (`ContentKind.Item`, `ContentKind.Mob`), the same `I*ContentWriter`s, and the same validation/apply flow. No new seams — purely additional Blazor editor pages + catalog DTO coverage for the item/mob template field shapes.

**Justification for splitting:** items and mobs reuse the WP-1 seam wholesale; deferring them keeps WP-2 focused on proving the host + first edit loop. Because no new seam is introduced, WP-3 is a low-risk additive work package and could equally be a sibling slice — kept here as a labeled WP so the platform brief's "first-slice edit scope" decision is traceable in one doc.

---

## Content Tooling Impact

- **New offline authoring surface.** The Blazor editor is the first non-telnet authoring tool. It reads/edits/writes the same `content/*.yaml` the telnet `mk*`/`set*` verbs write, so authored content is interchangeable between surfaces. INV-18 is satisfied: this slice's gameplay-state surface (content definitions) ships its own inspect/author tooling.
- **No new YAML field shape.** The catalog reads and writes the existing camelCase DTO shapes (`AreaDto`, `RoomDto`, `ItemDto`, `MobDto`) already round-tripped by the deserializers and `I*ContentWriter`s. No schema change.
- **No new `TemplateRegistry` shape.** The catalog writes YAML; templates are picked up by `WorldContentLoader` on the next `reload`/restart, exactly as `mkarea` does today.
- **On-demand validation is now reusable tooling.** `IContentValidator` makes the boot-only integrity sweep available to the editor (per-edit) and the future generator (pre-write) — the INV-19 framework the new surface obligates.

---

## Cross-Cutting Surfaces Stressed

**Commands framework:** Not stressed (adequate) — the editor is not a telnet command; it triggers the existing `reload` path. No new `ICommand`. The brief notes telnet authoring can stay minimal while the editor grows — surface parity is unnecessary because both call the shared catalog.

**Output framework:** Not stressed — Blazor renders HTML directly; the `IOutputMessage`/telnet formatter path is not involved. The editor surface deliberately produces no game output.

**Persistence — entity domain classification:** No entity construction path is introduced. The catalog writes **world content** (YAML only, INV-23) and never enrolls an entity (`PersistentEntity` never added; `SaveEntityAsync` never called). The only live-world change is via `reload`, which fresh-spawns world content from YAML — no SQLite rows. **INV-22/INV-23 preserved.**

**Persistence — component inclusion:** No new components. No existing component's `[Persistent]` status is touched. World-content templates (area/room/item/mob) are not persistent entities; the editor edits their YAML, not their components.

**Persistence — save-on-change scope:** No `SaveEntityAsync` calls anywhere in this slice. Compliant with INV-22 by construction.

**Event bus:** Adequate (reuse) — only the apply step touches the bus, via the existing `ContentReloadedEvent` published by the reload Initiator. The catalog and validator are off the bus (INV-5).

**ECS queries:** Not stressed — the catalog reads files, not the live `EntityService`. **`IContentValidator` exposes two call modes (resolved):** (a) a **single-definition** overload (`Validate(ContentDefinition)`) that checks an in-memory DTO with no live entities — the per-edit editor path and T1's pre-write generation path; (b) a **registry/live-scan** overload (`ValidateRegistry(...)`) that scans `EntityService.GetAllComponents<AreaComponent>()` etc. for area-affinity/cross-ref checks — the boot bootstrap's current behavior. The editor calls (a); `RegistryValidationBootstrap` calls (b). Both return a structured `ValidationReport` and never throw.

**Time:** Not stressed — authoring is entirely off the heartbeat (the brief's core concurrency-avoidance decision).

**Configuration:** Adequate — `Hedron.Web` consumes the same `WorldOptions`/`PersistenceOptions` via `CompositionRoot`. New keys: a loopback bind URL (host config) and a web port. No engine config changes.

**Sessions:** Not stressed for v1 — the editor has no `ISession`. The host is, per the brief, the foundation the deferred player-UI/SignalR `ISession` unification (slice 14) inherits; that surface is **not** built here.

**Broadcast / Modules:** Broadcast not stressed. Modules: one new `AuthoringModule` + one new host project — both additive, composed via the existing DI extension convention.

**Web transport / host (NEW SURFACE — INV-19 framework parity):** Gap exposed → resolved in this track. Standing up an HTTP/WebSocket surface (Blazor Server) is new infrastructure. The framework obligations it pulls in are (a) the callable `IContentValidator` (WP-1) and (b) the **split hosted-service registration** so each host composes its own service set (the web host runs neither telnet listener nor heartbeat). Both are resolved this slice. This is deliberately shaped as the foundation of the **unified three-suite web surface** (authoring now; player client + live-admin suites deferred — they re-add the live-world concurrency this track avoids); the split-registration seam means those land as additive page-suites + hosted services, not a host restructure. Loopback-only bind is the v1 auth posture; real authn/z is an explicit deferred backlog item (any non-local exposure gates on it).

---

## Flows Introduced or Modified

| # | Flow | Change |
|---|---|---|
| 29 | Offline content edit → save → apply | New — append row to `flows/README.md`; create `flow-29-offline-content-edit.md`. |

Flow 29 reuses Flow 5 (content reload) as its apply leg — the new flow links to Flow 5 rather than redefining it. No existing flow is modified (the reload path is unchanged; the editor is a new front door to it).

---

## Test Plan / Verification

**Definition-layer round-trip tests (tier: system-unit, `Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs` — flat-by-feature, matching the existing suite layout):**

1. `Catalog_Load_DeserializesExistingDefinition` — write a known area YAML to a temp content dir, `Load(Area, id)`, assert the DTO fields match the file.
2. `Catalog_SaveAsync_WritesValidDefinition_RoundTrips` — `CreateNew(Area, "Test")` → mutate fields → `SaveAsync` → re-`Load` and assert equality (load→edit→write→load round-trip). Repeat for `Room`.
3. `Catalog_SaveAsync_RejectsInvalidDefinition` — `SaveAsync` a definition with an invalid aspect composition; assert it returns a failed `ContentWriteResult` and **writes no file** (validation-before-write contract).
4. `Catalog_List_EnumeratesAllDefinitionsOfKind` — seed N area files, assert `List(Area)` returns N summaries with correct ids/names.
5. `Catalog_CreateNew_GeneratesUniqueBlueprintId` — two `CreateNew` calls return distinct `*.adhoc.*` ids; neither creates a live entity (assert `EntityService` count unchanged).

**Validator tests (tier: system-unit, `Hedron.Tests/World/ContentValidatorTests.cs`; test 8 extends the existing `Hedron.Tests/Registry/RegistryValidationTests.cs`):**

6. `ContentValidator_AcceptsValidContent` — valid ability/aspect/area set returns an empty `ValidationReport`.
7. `ContentValidator_RejectsBrokenCrossRef` — ability referencing a missing effect / area with a non-normalized aspect composition returns errors and does **not** throw (the editor needs structured errors, not an exception).

**Architecture-guard / regression (tier: handler or architecture-guard):**

8. `RegistryValidationBootstrap_StillThrowsOnInvalidContent` — after the refactor, the bootstrap that wraps `IContentValidator` still throws on boot for invalid content (preserves the existing fail-fast boot contract; guards against the factoring silently weakening startup validation).
9. `ServerHostComposition_RegistersFullGameplayHostedServiceSet` — after `AddHostedService` moves out of `Register`, assert the `Server` composition still registers all six hosted services (telnet, heartbeat, both bootstraps, validation, flush timer). Guards the split-registration refactor against silently dropping a gameplay service from the telnet host. (The `Hedron.Web` bootstraps-only set is exercised by launching the host, not unit-tested — host plumbing, skip-tier.)

**Legitimately skipped (per rubric in `docs/architecture/07-testing.md`):**

- **All Blazor components/pages** — presentation tier; UI rendering, form binding, and button wiring are skip-tier. They contain no game-rule logic (all logic is in the catalog/validator, tested above). This is the explicit "Blazor is skip-tier" call the platform brief anticipates.
- **`Hedron.Web/Program.cs` host wiring** — DI composition + Kestrel bind; thin host plumbing, exercised by launching the host, not unit-tested.
- **`I*ContentWriter`s** — thin I/O adapters already accepted by the suite (the catalog round-trip tests cover the write path end-to-end).
- **DTO records (`ContentDefinition`, `ContentSummary`, `ContentWriteResult`, `ContentKind`)** — pure data; no logic.

**Coverage contract:** the player-invisible internal-state postconditions are (a) validation-blocks-write (test 3), (b) `CreateNew` makes no live entity + unique id (test 5), (c) validator returns structured errors without throwing (test 7), and (d) the boot validator still fails fast after factoring (test 8). All four map to named tests above. **Testability:** no un-injected seam is introduced — the catalog takes a content directory / `IContentSerializer` / writers by DI, so tests point it at a temp dir; the validator is a pure-decision system. No `IRandom`/clock seam needed in T2 (the seeded-generation seam is T1's obligation).

---

## Design Notes

> Durable seam rationale lives in [`content-tooling-platform.md`](content-tooling-platform.md) (Design notes + Architecture brief). This section records only the T2-specific seam resolutions (INV-D1 — no restatement).

- **The catalog is one facade, not per-feature services (resolves brief Open Question 1).** A single `IContentDefinitionCatalog` in `Core/Modules/Authoring/` over all four kinds is chosen over definition-services-in-each-feature-module. Rationale: the editor and the generator both want **one entry point** to enumerate and operate across content types; a per-feature split would force every consumer to fan out across four modules and re-implement the kind→subdirectory→writer→deserializer mapping. The per-kind specifics (DTO shape, which `I*ContentWriter`) are dispatched **inside** the catalog by `ContentKind`. This is the "build the family seam now" disposition from the brief (areas/rooms/items/mobs would immediately repeat ≥3× — the INV-19 bar).

- **`CreateNew` factors the template/author half out of the builders, not the live-spawn half.** `AreaBuilderSystem.CreateArea` fuses (a) template construction + id generation with (b) live `EntityService.CreateEntity`. The catalog takes **(a) only** — it generates the `*.adhoc.<base36>` id and an empty template, writes YAML, and stops. The live `mk*` builders keep (b) and could later delegate (a) to the catalog (a follow-up refactor, not required here). This preserves INV-12: the editor never touches the live world.

- **`IContentValidator` lives in `Core/Modules/World/`, the bootstrap stays in `Server/`.** The validation logic is World-adjacent (it knows ability/aspect/area cross-refs); the *boot hosted-service* stays a `Server` concern (it owns the fail-fast-and-throw boot policy). The bootstrap becomes a thin caller: `Validate(...)` → if errors, log + throw. Same logic, two call sites (boot fail-fast; editor structured-error). This is the INV-19 framework parity the new surface obligates; T1 inherits it for pre-write generation validation.

- **Separate `Hedron.Web` host + split hosted-service registration (resolves brief Open Question 2 / host-wiring fork).** A new `Microsoft.NET.Sdk.Web` project boots the engine via the shared `CompositionRoot.Register`, keeping `Server` a plain headless telnet Exe. Rationale: the telnet server must stay runnable without a web stack; mixing the web SDK into `Server` would couple every telnet deploy to ASP.NET. The host-role separation is handled by **moving `AddHostedService` out of `Register`** so `Register` is pure DI and each host composes its own hosted-service set — *not* a host-role flag inside `Register` (which would grow a conditional arm per host). This is the load-bearing choice for the end-state: the user's intended **single web app with three page-suites — authoring, player client, live admin** — is one process composing the full superset (engine + heartbeat + SignalR sessions + admin), and split registration scales to it with zero churn to the shared composition method. v1 `Hedron.Web` composes bootstraps-only (authoring is off the tick); the player/admin suites (which touch the live world) are deferred with their concurrency work but inherit this host shape.

- **Loopback-only auth for v1 (resolves brief Open Question 3).** Kestrel binds `127.0.0.1` only; no auth system is built. This is the minimum safe posture for a single-developer offline authoring tool. The brief's deferred "Web/SignalR dual client" slice owns real authn/z; this doc records loopback as a v1 gate and adds a backlog entry: *no non-local bind until authn/z lands*.

- **Apply-to-live is a thin caller of `ReloadAsync`; the catalog never reloads.** Keeping the reload call in a host-level action (not the catalog) preserves the INV-5/INV-8 split: the catalog is a pure read/write-YAML domain system; the *Initiator* (the apply action) publishes/triggers. The editor never mutates the live world directly — the brief's INV-12 guarantee.

- **Items/mobs editing deferred to WP-3, not a separate slice (resolves brief Open Question 5 for T2 scope).** First-slice edit scope is list/read for all four kinds + full create/edit/write for areas and rooms. Items and mobs reuse the WP-1 seam with zero new architecture, so they ship as a labeled fast-follow WP. The brief's edit-vs-create flow question is resolved as: `CreateNew` + `Load`/`SaveAsync` are distinct catalog operations; create produces a fresh id, edit re-writes an existing one.

---

## Related

- [`content-tooling-platform.md`](content-tooling-platform.md) — the platform brief this track (T2) implements; owns the durable seam rationale and the resolved-decisions intake. The sibling T1 (bulk generation) is the other track over the same catalog/validator seams.
- [`admin-area-authoring.md`](admin-area-authoring.md) — `IAreaBuilderSystem`/`AreaBuilderSystem` (the fused builder this slice factors the template-half out of) and `IAreaContentWriter` (reused write-half).
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — `WorldContentLoader`, `ITemplateRegistry`, `IContentSerializer`, the `content/` layout, and the `reload` path the apply action reuses.
- [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — the builder/writer pattern across rooms/items the catalog generalizes.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — "Full-featured content editor" (this slice activates its offline-authoring portion); "Web / SignalR dual client" (the deferred player-UI foundation this host seeds); "Thread-safety review" (the deferred live-edit concurrency work); **new:** "Web auth before non-local bind".
- [`../architecture/checklist.md`](../architecture/checklist.md) — invariants in tension: INV-5, INV-8, INV-12, INV-15, INV-18, INV-19, INV-23, INV-25.
