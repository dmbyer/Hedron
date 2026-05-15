# Phase 3 slice 2 — World content loading + admin substrate (completed)

> Implemented and merged on `master`. The full feature spec lives in [`../../use-cases/world-content-loading-and-admin-substrate.md`](../../use-cases/world-content-loading-and-admin-substrate.md). This file records the as-built state and any deviations from the spec.

## Outcome

The hardcoded three-room MVP world is gone. World assembly is now driven by YAML files under `data/content/`, registered with a cross-cutting `TemplateRegistry` and seeded into `EntityService` by `WorldContentLoader` after `PersistenceBootstrap` has hydrated any persisted entities. Privileged sessions can spawn templated entities (`@spawn`), move themselves between rooms (`@teleport`), author connections (`@dig`), and refresh the registry without restart (`@reload`). Resolves Ticket B (admin tooling — telnet first, web/desktop UI deferred).

This is an **infrastructure-plus-tooling slice**. No new player-facing verbs. The value is making every subsequent gameplay slice authorable.

## Shipped pieces

| Surface | Location |
|---|---|
| `BlueprintComponent` (`[Persistent]`) | `Core/ECS/Components/BlueprintComponent.cs` |
| `AreaComponent` (`[Persistent]`) | `Core/ECS/Components/AreaComponent.cs` |
| `IEntityTemplate` | `Core/Systems/IEntityTemplate.cs` |
| `ITemplateRegistry` / `TemplateRegistry` | `Core/Systems/TemplateRegistry.cs` |
| `IContentSerializer` / `YamlContentSerializer` (kind-dispatcher; module-agnostic) | `Core/Systems/YamlContentSerializer.cs` |
| `ITemplateDeserializer` (per-kind translator interface) | `Core/Systems/ITemplateDeserializer.cs` |
| `RoomTemplate`, `RoomTemplateDeserializer` | `Core/Modules/World/Templates/` |
| `AreaTemplate`, `AreaTemplateDeserializer` | `Core/Modules/World/Templates/` |
| `IWorldContentLoader` / `WorldContentLoader` (with empty-dir void fallback) | `Core/Modules/World/Systems/WorldContentLoader.cs` |
| `WorldContentBootstrap` (`IHostedService`) | `Server/WorldContentBootstrap.cs` |
| `IAdminAuthorizer` / `AdminAuthorizer` (settings allowlist; component layer deferred) | `Core/Modules/Admin/Systems/AdminAuthorizer.cs` |
| `SpawnCommand`, `TeleportCommand` (alias `@tp`), `DigCommand`, `ReloadCommand` | `Core/Modules/Admin/Commands/` |
| `AdminAuditHandler` (priority 80; `ILogger` only — no audit file) | `Core/Modules/Admin/Handlers/AdminAuditHandler.cs` |
| Events: `EntitySpawnedByAdminEvent`, `PlayerTeleportedByAdminEvent`, `RoomExitAuthoredByAdminEvent` | `Core/Modules/Admin/Events/` |
| Event: `ContentReloadedEvent` | `Core/Modules/World/Events/ContentReloadedEvent.cs` |
| Module entry points | `Core/Modules/World/WorldModule.cs`, `Core/Modules/Admin/AdminModule.cs` |
| Extended: `PlayerMovedHandler` (now also subscribes `PlayerTeleportedByAdminEvent` via shared helper) | `Core/Modules/Movement/Handlers/PlayerMovedHandler.cs` |
| Extended: `PersistenceHandler` (subscribes to `EntitySpawnedByAdminEvent` and `RoomExitAuthoredByAdminEvent` for dirty-marking) | `Core/Handlers/PersistenceHandler.cs` |
| Removed: hardcoded `Server/WorldBootstrap.cs` | (deleted) |

## Configuration

Read via `IConfiguration` per [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md):

- `World:ContentDirectory` — default `data/content/` (new in this slice)
- `World:StartingRoomBlueprintId` — default `room.crossroads` (new in this slice)
- `Admin:PrivilegedNames` — default `[]`, string array (new in this slice)
- `Persistence:DataDirectory` — unchanged from slice 1; reaffirmed configurable
- `Persistence:FlushIntervalSeconds` — unchanged from slice 1

`.gitignore` updated: `/data/` and `/Server/data/` are not committed (per-developer runtime + local content).

## Resolved decisions (from planning round)

| # | Question | Decision |
|---|---|---|
| 1 | Content data file format — JSON or YAML? | **YAML** via `YamlDotNet`. Persistence stays JSON; both serializers coexist. |
| 2 | Admin privilege gate? | **Layered.** Settings allowlist now (`Admin:PrivilegedNames`); persisted `AdminPrivilegeComponent` later — placeholder use-case at [`../../use-cases/admin-privilege-elevation.md`](../../use-cases/admin-privilege-elevation.md). Settings is the floor. |
| 3 | `@reload` semantics? | **Additive only.** Refresh registry, seed missing entities. Live entities are not mutated. |
| 4 | `@dig` write-back to source files? | **No file round-trip in this slice.** In-memory template updated; durability via `PersistenceSystem` next flush. |
| 5 | Empty/missing content dir at startup? | **Spawn a single `room.void` and warn.** Host stays up. Audit sink is `ILogger<AdminAuditHandler>` only. |
| 6 | Should `data/` be committed? Configurable paths? | **No on commit; yes on configurable.** `data/` is `.gitignore`d. Both `World:ContentDirectory` and `Persistence:DataDirectory` are operator-controlled. |

## Notable design points (recap)

- **Startup ordering** is `PersistenceBootstrap` → `WorldContentBootstrap` → `TelnetServer`, enforced by `IHostedService` registration order in `Server/Program.cs`. Persistence hydrates first; the content loader's spawn pass sees the fully hydrated world and skips any blueprint that already has a live entity (`BlueprintComponent` lookup).
- **Silent startup spawn.** `WorldContentLoader.LoadAndSpawnAsync` does not publish per-entity events — matches the persistence hydration contract.
- **Admin privilege gate is per-command.** Each admin `ICommand.Execute` calls `IAdminAuthorizer.IsPrivileged` as the first line. `CommandDispatcher` itself is not modified — it remains policy-free.
- **`AdminAuditHandler` priority 80** (Notification) — runs after gameplay handlers and before `PersistenceHandler` at 90.
- **Two serializers coexist by design.** `System.Text.Json` (slice 1) for persistence component snapshots; `YamlDotNet` (this slice) for content authoring. Different audiences, different change cadence, no shared serializer code.
- **`YamlContentSerializer` is a kind-dispatcher** with no module knowledge. Per-module `ITemplateDeserializer` implementations register via DI (the World module wires `RoomTemplateDeserializer` and `AreaTemplateDeserializer`). Future content-bearing modules add their own kinds without editing the cross-cutting serializer.

## Deviations from the use-case doc

- **No seed YAML shipped.** The use-case had an internal contradiction: an early section described shipping the MVP rooms in `data/content/` while a later section said the `data/` tree is gitignored and seed content is deferred. Resolved by deleting the contradictory line and committing to the gitignore decision: a fresh clone gets the void-room fallback as the documented first-run experience. A future `seed-content/` slice can layer in.
- **`SpawnCommand` does not place spawned entities into the invoker's room.** The only spawnable templates in this slice are rooms and areas, neither of which warrant placement. Placement logic for items/mobs lands with their respective slices (4 / 6). The command short-circuits with a clear error when the invoker has no `LocationComponent`.

## Follow-ups unlocked by this slice

- Account/character creation (slice 3) — the first slice that produces user-authored persisted entities, which will conflict-resolve against blueprints via the model wired here.
- Items + inventory (slice 4) — reuses `ITemplateRegistry` for item templates; `@spawn` becomes useful for dropping test items in rooms.
- Mob wandering (slice 6) — reuses `ITemplateRegistry` for mob templates and the `WorldLoadedEvent` startup signal.
- Admin privilege elevation (deferred — placeholder use-case) — adds the persisted `AdminPrivilegeComponent` layer on top of the bootstrap allowlist.
- `seed-content/` bootstrap directory — committed reference content that a fresh clone copies into `data/content/` for the MVP three-room smoke test, deferred per the gitignore decision.
- Source-file round-trip for `@dig` (e.g. `@save-room`) — deferred. Currently durability comes from persistence flushing the live `[Persistent]` components.

## Architecture review notes

Reviewed by `architecture-reviewer` agent before merge: **APPROVE WITH NITS**. One real issue found and fixed during review (`YamlContentSerializer` originally hard-imported `Hedron.Core.Modules.World.Templates`, violating the core-doesn't-depend-on-domain rule; refactored to a kind-dispatcher with per-module `ITemplateDeserializer`s). Five inline nits also addressed (README index status bump; deferred-placement comment + no-location short-circuit on `SpawnCommand`; cached `BuildLiveBlueprintMap` during the startup pass; `PersistenceHandler` doc-comment lists subscribed events). Reference catalog drift (components / systems / handlers) was applied as part of the same PR rather than as a separate documentation pass.
