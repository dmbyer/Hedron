# Admin Authoring

> Building and editing world content in-game via builder verbs and offline via the Blazor editor. **Status:** live (slice 2 admin substrate, admin-area-authoring, content-tooling platform).

## What it is

Admins author world content — rooms, areas, items, mobs — through two surfaces: **in-game builder verbs** (`dig`, `mkitem`, `mkmob`, `mkarea`, `set*`, `list`, `reload`) that create and edit content while connected via telnet, and an **offline Blazor Server editor** (`Hedron.Web`) that reads, edits, validates, and writes the YAML content definitions from a localhost browser. Both surfaces call the same backing systems; neither re-implements authoring logic.

Privilege is **structural**: each admin command calls `IAdminAuthorizer.IsPrivileged` as its first line. There is no `@` prefix or special sigil. The settings-allowlist (`Admin:PrivilegedNames`) is the floor; the persisted `AdminPrivilegeComponent` layer is deferred — see `implementation-plans/admin-privilege-elevation.md`.

Content edits write YAML only. The live world is refreshed by the `reload` verb, which rebuilds the world from YAML — tearing down and re-spawning all world content (preserving players) so edits to existing content take effect and runtime instance state resets. The Blazor editor applies the same reload via its "Apply to live" action.

## How it works

The feature composes three cooperating subsystems:

- **Builder verbs + privilege gate** — `dig`/`mkitem`/`mkmob`/`mkarea`/`set*`/`list`/`reload` in `Core/Modules/Admin/` and the per-feature builder systems. Each command is a thin Initiator: it calls a builder system (pure result), writes YAML via an `I*ContentWriter`, publishes a past-tense audit event (caught by `AdminAuditHandler`), and in some cases calls `IPersistenceSystem.SaveEntityAsync` for an admin boundary-save. See the [admin-commands design doc](admin-commands.md).
- **Blazor editor** — `Hedron.Web` boots the engine via the shared `CompositionRoot.Register` with bootstraps-only hosted services (content load + registry validation; no telnet, no heartbeat). Blazor components are thin callers of `IContentDefinitionCatalog` — all read/list/load/validate/write logic lives in the catalog, not in page code. See the [content-authoring design doc](content-authoring.md).
- **Content-tooling layer** — `IContentDefinitionCatalog` (the shared facade for all four content kinds), `IContentValidator` (on-demand referential-integrity checks factored out of the boot validator), and `IContentGenerationSystem` (headless bulk YAML generator for scaling tests). See the [content-tooling design doc](content-tooling.md).

## Systems

| System | Role |
|---|---|
| [`admin-commands.md`](admin-commands.md) | Builder verbs, privilege gate, audit event wiring |
| [`content-authoring.md`](content-authoring.md) | Blazor editor, `Hedron.Web` host, definition/reload pipeline |
| [`content-tooling.md`](content-tooling.md) | `IContentDefinitionCatalog`, `IContentValidator`, `IContentGenerationSystem`, `generate` run-mode |

## Surfaces

- **Admin commands** — `dig`, `mkitem`, `mkmob`, `mkarea`, `set`, `setitem`, `setmob`, `setarea`, `list`, `reload`, `spawn`, `teleport`. See [`../../reference/commands.md`](../../reference/commands.md).
- **Builder systems** — `IRoomBuilderSystem`, `IItemBuilderSystem`, `IMobBuilderSystem`, `IAreaBuilderSystem`. See [`../../reference/systems.md`](../../reference/systems.md).
- **Content layer** — `IContentDefinitionCatalog`, `IContentGenerationSystem`, `IContentValidator`. See [`../../reference/systems.md`](../../reference/systems.md).
- **Events** — `RoomCreatedByAdminEvent`, `ItemCreatedByAdminEvent`, `MobCreatedByAdminEvent`, `AreaCreatedByAdminEvent`, `RoomPropertySetByAdminEvent`, `ContentReloadedEvent`. See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Config keys** — `Admin:PrivilegedNames` (privilege allowlist). See [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md).

## Flows

- [Admin authoring journey (dig · mkitem · mkmob · mkarea · list)](../../architecture/flows/flow-08-admin-room-creation.md) — the builder-verb loop: privilege gate → builder system → YAML write → audit event → confirmation.
- [Content-tooling journey (bulk generate · offline edit)](../../architecture/flows/flow-29-bulk-content-generation.md) — headless `generate` run-mode and offline Blazor editor save/apply path.

## Related

- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5 (builder systems return results; commands publish), INV-8 (thin initiators — no authoring logic in commands or Blazor components), INV-12 (one world model — catalog never creates live entities), INV-19 (new authoring surface obligates `IContentValidator` + split hosted-service registration), INV-22 (admin boundary-save for `mk*`/`set*` that mutate persisted entities), INV-23 (world-content entities carry no `PersistentEntity`).
- [`../../roadmap/completed/content-tooling-platform.md`](../../roadmap/completed/content-tooling-platform.md) — as-built history: `IContentDefinitionCatalog`, Blazor host, `generate` run-mode, split hosted-service registration, design decisions.
- [`../../roadmap/completed/slice-2-world-content-and-admin-substrate.md`](../../roadmap/completed/slice-2-world-content-and-admin-substrate.md) — admin substrate history: `AdminAuthorizer`, `DigCommand`, `ReloadCommand`, `AdminAuditHandler`.
- **World** — [`../world/world.md`](../world/world.md) — `WorldContentLoader`, `ITemplateRegistry`, the YAML content layout, and the `reload` path that applies authored content to the live world.
- **Items / Mobs** — [`../items/items.md`](../items/items.md) · [`../mobs/mobs.md`](../mobs/mobs.md) — builder systems for items and mobs live in their respective feature modules; admin commands are thin callers.
- [`../../architecture/08-blazor.md`](../../architecture/08-blazor.md) — Blazor Server discipline: thin components, `CompositionRoot` reuse, loopback-only v1 auth, the three-suite web surface roadmap.
- [`../../implementation-plans/admin-area-authoring.md`](../../implementation-plans/admin-area-authoring.md) — planned: deeper area authoring flows (deferred).
- [`../../implementation-plans/admin-privilege-elevation.md`](../../implementation-plans/admin-privilege-elevation.md) — deferred: persisted `AdminPrivilegeComponent` layer on top of the settings allowlist.
