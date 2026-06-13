# Content Authoring (Blazor Editor)

> The offline Blazor Server editor for browsing, creating, editing, and applying world content definitions. **Status:** live (content-tooling platform WP-2, WP-3).

## What it is / does

`Hedron.Web` is a `Microsoft.NET.Sdk.Web` Blazor Server application that boots the engine via the shared `CompositionRoot.Register` (identical DI to `Server`) with a **bootstraps-only** hosted-service composition — content load and registry validation only; no telnet listener, no heartbeat, no persistence flush. It binds loopback-only (`http://127.0.0.1:<port>`); no auth system is built for v1.

The editor provides four pages: a content browser (list all four kinds), a definition editor per kind (load → edit fields → validate → save), a "Create new" flow per kind, and an "Apply to live (reload)" action. All read/list/load/validate/write logic lives in [`IContentDefinitionCatalog`](content-tooling.md) — Blazor components are thin adapters, never containing authoring logic (INV-8 extended to the new surface).

## How it works

The Blazor host is the presentation tier over the same content-tooling systems the telnet commands call:

1. **Browse.** A page calls `IContentDefinitionCatalog.List(kind)` and renders id | name | short-desc.
2. **Load / create.** The editor calls `Load(kind, blueprintId)` (existing) or `CreateNew(kind, name)` (new, ad-hoc id from `AdhocBlueprintId`). The returned `ContentDefinition` binds to the form. No live entity is created.
3. **Edit.** The designer mutates form fields (name, description, exits, aspect affinities, etc.). No catalog call; the form holds a working copy.
4. **Save (validate-then-write).** `SaveAsync(definition)` runs `IContentValidator.Validate` against the working copy; on failure returns a `ContentWriteResult.Failed(errors)` and **writes no file**. On success, writes YAML through the matching `I*ContentWriter` (atomic tmp → rename). The live world is untouched.
5. **Apply to live.** The "Apply" action calls `IWorldContentLoader.ReloadAsync()` — [Flow 5 (content reload)](../../architecture/flows/flow-05-content-reload.md). New templates are seeded; existing live entities are not mutated. The page renders the `ContentReloadResult` counts.

The full sequence is [Flow 29 (content-tooling journey)](../../architecture/flows/flow-29-bulk-content-generation.md).

## Host composition

Split hosted-service registration is the seam that lets one engine DI serve multiple host shapes (INV-19):

- `CompositionRoot.Register(IServiceCollection, IConfiguration)` — **pure DI** (all `Add*Module` extensions; no `AddHostedService`).
- `AddGameplayHostedServices` — telnet host: TelnetServer + HeartbeatBackgroundService + PersistenceFlushTimer + bootstraps.
- `AddContentBootstrapHostedServices` — web host: content-load bootstrap + registry-validation bootstrap only.

`Hedron.Web/Program.cs` calls `Register` + `AddContentBootstrapHostedServices` + Blazor services. `Server/Program.cs` calls `Register` + `AddGameplayHostedServices`. Both are sealed at composition time; `Register` never grows a host-role flag.

## Interface

The editor's backing seam is in [`content-tooling.md`](content-tooling.md):

- [`IContentDefinitionCatalog.cs`](../../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs) — the thin facade all editor pages call.
- [`IContentValidator.cs`](../../../Core/Modules/World/Systems/IContentValidator.cs) — per-edit and per-write validation, returns `ValidationReport`.

## Blazor discipline

See [`../../architecture/08-blazor.md`](../../architecture/08-blazor.md) for the Blazor architecture rules. Key points:

- Blazor components are **presentation-only** — all logic lives in the catalog/validator (the fat-component analogue of a fat command, INV-8).
- The web host composes **no** `bus.Subscribe` wiring; it runs no gameplay handlers.
- Loopback-only for v1. A non-local bind requires authn/z (tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md)).
- This host is the foundation the deferred player-client and live-admin suites will inherit (same process, same engine DI, `ISession`/SignalR seam pre-shaped).

## Related

- [`admin-authoring.md`](admin-authoring.md) — the holistic feature view.
- [`content-tooling.md`](content-tooling.md) — `IContentDefinitionCatalog`, `IContentValidator`, `IContentGenerationSystem`.
- [`../../architecture/08-blazor.md`](../../architecture/08-blazor.md) — Blazor discipline and the three-suite web surface roadmap.
- [`../../architecture/flows/flow-29-bulk-content-generation.md`](../../architecture/flows/flow-29-bulk-content-generation.md) — the content-tooling journey (offline edit leg).
- [`../../architecture/flows/flow-05-content-reload.md`](../../architecture/flows/flow-05-content-reload.md) — the apply-to-live leg reused by the editor.
- [`../../roadmap/completed/content-tooling-platform.md`](../../roadmap/completed/content-tooling-platform.md) — as-built history: host shape decisions, split registration, loopback auth, deferred suites.
