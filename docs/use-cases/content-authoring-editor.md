# Use Case: Offline Blazor Content-Authoring Editor (T2)

**Status:** implemented
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
- **Split hosted-service registration — ✅ landed in S0 (shared prereq).** `CompositionRoot.Register` is DI-only; the six `AddHostedService` calls live in `CompositionRoot.AddGameplayHostedServices(services)`, which `Server/Program.cs` calls. `Server` behavior is unchanged (guarded by `Hedron.Tests/Composition/HostCompositionTests.cs`). WP-2 adds only the web host's **trimmed** hosted-service composition (bootstraps-only, no telnet/heartbeat) — e.g. a sibling `AddContentBootstrapHostedServices()` or inline in `Hedron.Web`'s `Program.cs`. This is the seam that scales one process to the deferred three-suite superset.
- **Blazor host.** New project `Hedron.Web` (`Microsoft.NET.Sdk.Web`) boots the engine via `CompositionRoot.Register(...)`, composes **bootstraps-only** hosted services (content load + registry validation — no telnet, no heartbeat), adds Blazor Server services, and **binds loopback-only** (`http://127.0.0.1:<port>`). `Server` is unchanged and still runs headless telnet-only.
- **Editor surfaces.** Blazor pages: a content browser (list all four kinds), a definition editor for **all four kinds** (load → edit fields → validate → save; areas/rooms in WP-2, items/mobs in WP-3), a "Create new" flow per kind, and an "Apply to live (reload)" action that calls the reload path and shows the `ContentReloadResult` counts.
- **Catalog reload coupling.** The editor's "Apply to live" calls a thin caller of `IWorldContentLoader.ReloadAsync` and renders the counts. No new event type is introduced; if the reload runs in a context with an admin identity, the existing `ContentReloadedEvent` is reused (publishing belongs to the Initiator, not the catalog — INV-5).
- **No event subscriptions in the web host (v1).** `Hedron.Web` composes **no** `bus.Subscribe(...)` wiring (all of which lives in `Server/Program.cs` today). This is deliberate: authoring is off the bus, the host runs no heartbeat/combat/session handlers, and it spawns no players to hydrate. The apply step's `reload` publishes `ContentReloadedEvent` from its Initiator, but the web host need not subscribe any handler to it for v1. (When the deferred player/admin suites land, they add their own subscriptions to this host's composition.)
- **Docs.** `docs/architecture/flows/README.md` gains Flow 30 (offline content edit → save → apply). `docs/reference/systems.md` gains `IContentDefinitionCatalog` (Authoring) and `IContentValidator` (World). `docs/documentation-architecture.md` / module map notes the new `Hedron.Web` host. `docs/reference/components.md` is unchanged (no new components).
- **Agent tooling (INV-20).** The split hosted-service registration + the second engine host make the "register in `Server/Program.cs`" / "root DI composition" guidance in `.claude/skills/add-core-system/SKILL.md` (step 2) and `.claude/skills/add-domain-system/SKILL.md` (step 2) stale. Both are updated in this slice (WP-2 task) to note (a) the engine now has **two hosts** (`Server`, `Hedron.Web`) booting via the shared `CompositionRoot.Register`, and (b) **hosted services are composed per-host, not in `Register`** — a new hosted service is added to each host's set, not to `Register`.

---

## Main Flow

### Flow 30 — Offline content edit → save → apply (`Hedron.Web`)

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
