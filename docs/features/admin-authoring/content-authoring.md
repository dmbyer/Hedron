# Content Authoring (Blazor Editor)

> The offline Blazor Server editor for browsing, filtering, creating, editing, deleting, and applying world content definitions. **Status:** live (content-tooling platform WP-2/WP-3; area filters + referential integrity + delete).

## What it is / does

`Hedron.Web` is a `Microsoft.NET.Sdk.Web` Blazor Server application that boots the engine via the shared `CompositionRoot.Register` (identical DI to `Server`) with a **bootstraps-only** hosted-service composition — content load and registry validation only; no telnet listener, no heartbeat, no persistence flush. It binds loopback-only (`http://127.0.0.1:<port>`); no auth system is built for v1.

The editor provides: a content browser (list all four kinds, **filterable by area**), a definition editor per kind (load → edit fields → validate → save, with **lookup-driven selection fields** and **delete**), a "Create new" flow per kind, an **Integrity** page (broken-link sweep), and an "Apply to live (reload)" action. All read/list/load/validate/write/delete/reference logic lives in [`IContentDefinitionCatalog` / `IContentReferenceIndex`](content-tooling.md) — Blazor components are thin adapters that choose filter values and bind fields, never containing authoring or reference-resolution logic (INV-8 extended to the new surface).

## How it works

The Blazor host is the presentation tier over the same content-tooling systems the telnet commands call:

1. **Browse + filter.** A page calls `IContentDefinitionCatalog.List(kind)` and renders id | name | short-desc. Each `ContentSummary` carries its resolved `AreaBlueprintId` (room → its `AreaId`; item/mob → spawn-room → area, computed catalog-side), so the page renders an **area filter** (defaulting to "all", always returnable to "all") that hides non-matching rows. Filtering is component-side over the catalog-computed projection — the component resolves nothing.
2. **Load / create.** The editor calls `Load(kind, blueprintId)` or `CreateNew(kind, name)` (ad-hoc id from `AdhocBlueprintId`). The returned `ContentDefinition` binds to the form. No live entity is created.
3. **Edit with selection fields.** The designer mutates form fields; the form holds a working copy. Selection fields are lookup-driven, populated from `List(Area)` / `RoomsInArea(...)`: the room editor offers an **area picker** and a **filterable exit room-lookup** per direction; the item/mob editors offer a **cascading area→room picker** for the spawn room. An optional **bidirectional** toggle on the room editor routes the save through `SaveRoomAsync(room, bidirectional: true)`, which also writes the inverse exit on each target room (warn-and-skip on conflict).
4. **Save (validate-then-write, warn-but-allow).** `SaveAsync(definition)` runs `IContentValidator.Validate`; structural failure → `ContentWriteResult.Failed(errors)`, **no file written**. On structural pass it checks cross-references via `IContentReferenceIndex.BrokenFor`; any dangling ref becomes a non-blocking **warning** in `ContentWriteResult.Warnings` and the YAML is still written through the matching `I*ContentWriter` (atomic tmp → rename). The page renders warnings distinctly from blocking errors. The live world is untouched.
5. **Delete (cascade-clear).** A two-step delete on a browser row or editor calls `DeleteAsync(kind, id)`: the catalog cascade-clears every referrer (via `IContentReferenceIndex.Referrers`) and removes the YAML file — **file-only, no live world / no SQLite** (INV-22/23). The page renders the `ContentDeleteResult` cascade summary.
6. **Integrity sweep.** The Integrity page calls `IContentReferenceIndex.SweepBroken()` and tabulates every broken link (source kind, id, field/direction, missing target) with edit links — the offline "assurance" surface before reload.
7. **Apply to live.** The "Apply" action calls `IWorldContentLoader.ReloadAsync()` — [Flow 5 (content reload)](../../architecture/flows/flow-05-content-reload.md). This is a full rebuild: world content is torn down and re-spawned from YAML (players preserved), so edits to existing entities take effect and runtime instance state resets.

The full sequence is [Flow 29 (content-tooling journey)](../../architecture/flows/flow-29-bulk-content-generation.md).

## Host composition

Split hosted-service registration is the seam that lets one engine DI serve multiple host shapes (INV-19):

- `CompositionRoot.Register(IServiceCollection, IConfiguration)` — **pure DI** (all `Add*Module` extensions; no `AddHostedService`).
- `AddGameplayHostedServices` — telnet host: TelnetServer + HeartbeatBackgroundService + PersistenceFlushTimer + bootstraps.
- `AddContentBootstrapHostedServices` — web host: content-load bootstrap + registry-validation bootstrap only.

`Hedron.Web/Program.cs` calls `Register` + `AddContentBootstrapHostedServices` + Blazor services. `Server/Program.cs` calls `Register` + `AddGameplayHostedServices`. Both are sealed at composition time; `Register` never grows a host-role flag.

## Interface

The editor's backing seam is in [`content-tooling.md`](content-tooling.md):

- [`IContentDefinitionCatalog.cs`](../../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs) — the thin facade all editor pages call (list/load/save/delete/create).
- [`IContentReferenceIndex.cs`](../../../Core/Modules/Authoring/Systems/IContentReferenceIndex.cs) — the declared-edge reference model behind filtering associations, delete cascade, save warnings, and the Integrity sweep.
- [`IContentValidator.cs`](../../../Core/Modules/World/Systems/IContentValidator.cs) — per-edit and per-write structural validation, returns `ValidationReport`.

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
- [`../../roadmap/completed/content-editor-integrity.md`](../../roadmap/completed/content-editor-integrity.md) — as-built history: area filters, selection fields, delete + cascade, reference integrity, bidirectional linking.
