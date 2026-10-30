# Dev-world foundation — Phase 5 slice 1 (completed)

> Implemented on branch `claude/wp1-implementation-plan-2606f0`, 2026-10-30. Living docs: [`world-content`](../../features/world/world-content.md#world-manifest--worldyaml) (manifest, content ownership), [`content-tooling`](../../features/admin-authoring/content-tooling.md#iworldmanifestauthoring--the-world-page) (World page, rename cascade), [`admin-commands`](../../features/admin-authoring/admin-commands.md) (`startroom`). Program: [`phase-5-content-baseline-program.md`](../../implementation-plans/phase-5-content-baseline-program.md) (R1–R3, R15).

## Outcome

A world now declares its own starting room in an optional `world.yaml` at the content root. Every consumer reads one resolved, atomically published starting-room snapshot, resolved in this order: manifest → `World:StartingRoomBlueprintId` → `room.void` → ordinal-least room. The manifest ships with its INV-18 tooling: the admin `startroom [here]` verb, an editor `/world` page, and a rename cascade into `world.yaml`. `content/dev-world/` is now the primary playable baseline (policy amended). It boots through a committed `DevWorld` environment profile with its own database, into a three-room hub skeleton.

## Behavior digest

- **Resolution.** `WorldContentLoader.ResolveStartingRoom()` (public; it ends both load and reload) reads `world.yaml` through `IWorldManifestStore`, then runs the pure `StartingRoomResolver` over the live **room** set (`RoomComponent` + `BlueprintComponent`, persistent entities excluded). It publishes `ResolvedStartingRoom(EntityId, BlueprintId, Source, DeclaredId)` to `WorldConfiguration.StartingRoom` with one `Volatile.Write`.
  - Never throws. Each fallback logs a warning.
  - With no rooms at all it logs an error and publishes `Unresolved` with entity id 0.
  - With no `world.yaml`, behavior is unchanged. A malformed file is logged and treated as empty.
- **Single-read consumers.** `AccountSystem.CreateCharacterAsync` (Location + Respawn), `DeathSystem.Respawn`'s fallback and `CharacterHydrationHandler`'s reset each take one snapshot read for both ids. This closes a torn-read window across a concurrent `reload`/`startroom`.
- **`startroom`.** Bare `startroom` is read-only: the resolved room, its source, and any declared-but-unresolved id. `startroom here`:
  - writes the invoker's world room to `world.yaml`;
  - re-resolves the live snapshot;
  - publishes `StartingRoomSetByAdminEvent` with the previous **resolved** id.

  It refuses outside a world room or while the file is malformed. Any other token gets the usage line. It never calls `SaveEntityAsync`, and existing respawn points are untouched.
- **Authoring.** `IWorldManifestAuthoring` (on `ContentDefinitionCatalog`) provides Load / Preview / Save. Save runs under the catalog write gate:
  - a malformed id is refused;
  - a malformed existing file is refused;
  - an unknown room saves with a warning;
  - null writes no declaration.

  A room `RenameAsync` rewrites a matching declaration, and refuses before any write while the manifest is malformed.
- **Profile.** `DOTNET_ENVIRONMENT=DevWorld` layers `appsettings.DevWorld.json` for the Server host, the run modes and `Hedron.Web`. `HEDRON_` env vars still win. Dev-world boots on `room.hub.square` from its manifest.

## Shipped pieces

| Surface | Location |
|---|---|
| `WorldManifest` (+ `Empty`, `CurrentSchemaVersion`) | `Core/Modules/World/WorldManifest.cs` |
| `IWorldManifestStore` / `WorldManifestStore` (`Read`, `WriteAsync`, `UpdateAsync`) | `Core/Modules/World/Systems/` |
| `StartingRoomResolver` + `StartingRoomDecision` | `Core/Modules/World/Systems/StartingRoomResolver.cs` |
| `ResolvedStartingRoom` + `StartingRoomSource`; `WorldConfiguration.StartingRoom` snapshot (old fields → get-only read-throughs) | `Core/ResolvedStartingRoom.cs`, `Core/WorldConfiguration.cs` |
| `WorldContentLoader.ResolveStartingRoom()` public, manifest-aware, room-only | `Core/Modules/World/Systems/WorldContentLoader.cs` |
| Single-snapshot reads | `AccountSystem`, `DeathSystem`, `CharacterHydrationHandler` |
| `StartRoomCommand`, `StartingRoomSetByAdminEvent`, `AdminAuditHandler` branch | `Core/Modules/Admin/` (subscription in `Server/Program.cs`) |
| `IWorldManifestAuthoring`, `WorldManifestView`; catalog implementation + rename cascade; DI forwarding | `Core/Modules/Authoring/` |
| `/world` page + nav link | `Hedron.Web/Components/Pages/World.razor`, `Layout/MainLayout.razor` |
| `RunModeConfiguration` (one JSON layering for the Server host and run modes) | `Server/RunModeConfiguration.cs`, `Server/Program.cs` |
| `DevWorld` profiles; `UseStaticWebAssets` under `DevWorld` | `Server/appsettings.DevWorld.json`, `Hedron.Web/appsettings.DevWorld.json`, `Hedron.Web/Program.cs` |
| Hub skeleton (`world.yaml`, `area.hub`, `room.hub.{square,north,south}`); `room.void.yaml` removed | `content/dev-world/` |

## Tests shipped

- **System-unit:**
  - `StartingRoomResolverTests` (T1);
  - `WorldManifestStoreTests` (T2, plus `UpdateAsync` refuse/default);
  - `AccountSystemTests` and `DeathSystemTests` extended (T5/T6);
  - `ContentDefinitionCatalogTests` rename-cascade and manifest-save cases (T9/T10);
  - `RunModeConfigurationTests` (T11) and `DataPathsTests` (the data-location rule).
- **Flow:**
  - `WorldContentLoaderStartingRoomTests` (T3);
  - `StartRoomCommandFlowTests` (T7/T8);
  - `DevWorldFixtureTests` (T12): manifest start, non-PvP hub, every room on the grid, strongly connected from the start, no void room.
- **Handler (on-touch ratchet):** `CharacterHydrationHandlerTests` (T14; the handler previously had no tests).
- **Extended:** `ContentDefinitionCatalogWriteSerializationTests` (T15); the existing loader harnesses (T4); the DI-smoke guard covers the new registrations (T13).
- `dotnet build` and `dotnet test` are green: 1548 tests.

## Decisions

- **Manifest, not just a profile.** The starting room is a fact about the world, not the host. A host key that must change in lock-step with `ContentDirectory` breaks in every new launch context: operator worlds, dev-world, PT-1's per-world composition, future instances. `World:StartingRoomBlueprintId` is kept as the operator fallback (Category 1 behind Category 2).
- **One immutable snapshot.** The consumer change turned out to be smaller than expected, because every runtime consumer already read the resolved config. The work was the resolution source plus making the published value one reference. The old fields stay only as single-field read-throughs, and no caller may combine them.
- **Room-only, deterministic resolution.** The old map searched every blueprint kind, so a mob id would "resolve". The last resort was dictionary order and is now the ordinal-least room id. Both are correctness fixes that fall out of the pure resolver.
- **Per-world, no statics.** The resolver is pure, the store is rooted at its `WorldOptions`, and the snapshot lives on the `WorldConfiguration` instance, so PT-1's per-world composition gets an independent resolution.
- **Respawn is per-character state.** `RespawnComponent` is seeded at creation, so moving the start does not move existing respawn points (`setrespawn` exists). An unresolvable respawn still falls back to the resolved start.
- **Rename cascades, delete warns.** The manifest is a referrer in the same directory, so rename rewrites it. `ContentDeleteResult` has no warnings channel, so a deleted start surfaces through the loader warning, the World-page preview and `startroom`.
- **Write behavior.** Writes refuse while the manifest is malformed: a hand-broken file is fixed by hand, never silently overwritten. All writers share `IWorldManifestStore.UpdateAsync` (added after code review found three hand-written copies that had already drifted on the `schemaVersion` default).
- **Hub safety and the profile.**
  - **Hub "safe" by construction:** no spawns, `pvp: false`, and R10 means no mob starts a fight. There is no `isSafe` field (the phantom doc entry was corrected); a mechanical safe-area rule waits for aggro mobs or PvP.
  - **Launch profile = environment appsettings:** `launchSettings.json` is gitignored by policy.
  - **No `StartingRoomBlueprintId` in the DevWorld profile:** the manifest supplies the start, so the fixture guard proves the manifest path rather than a config coincidence.
- **Configuration: one data-location rule (plan Open question 4, user sign-off 2026-10-30).** The entry check found the hosts disagreed. `dotnet run --project Hedron.Web` runs from `Hedron.Web/`, but `dotnet run --project Server` runs from the repo root, so:
  - `Server/appsettings*.json` were never loaded from the repo root;
  - relative data paths meant different folders per host (the editor's default content lived in an un-ignored `Hedron.Web/data/`).

  Two changes, as built:
  - The Server process (telnet host and run modes) reads its JSON from the build output through `RunModeConfiguration` (JSON → command line → `HEDRON_`; unprefixed env vars dropped so `HEDRON_` is the single env-var mechanism).
  - **Every data location follows one rule** (`Server/DataPaths`, applied by the shared composition root):
    - everything lives under `Paths:DataDirectory` (default `data`), with per-location keys as optional overrides;
    - relative paths resolve against the **Hedron root** (the checkout holding `Hedron.sln`, else the install folder), never the working directory.

  Consequences:
  - Both hosts and every launcher agree. Both DevWorld profiles are identical.
  - A machine-level `HEDRON_Paths__DataDirectory` no longer overrides the DevWorld profile's explicit locations.
  - `Server/appsettings.json` now applies to repo-root runs (admin names, starting abilities, …).
- **YAML store debt (INV-19).** `WorldManifestStore` ships as a hand-rolled single-document store. Slice 4's shared YAML-definition store must cover the single-document shape and migrate it along with standards and scenarios ([backlog](../backlog.md)).

## Deviations / Follow-ups

- **Deviations from the plan:**
  - `WorldManifestView` gained `ReadError`, so the page can show a malformed file and disable save.
  - T10 lives in `ContentDefinitionCatalogTests` to reuse its harness, instead of a separate class.
  - `IWorldManifestStore.UpdateAsync` was added after code review.
  - The data-location rule (`Paths:DataDirectory`, root-anchored) replaced per-path, working-directory-relative settings (see Decisions).
  - Only the Server host's JSON was re-anchored; `Hedron.Web` already loads its JSON from its content root. The plan's AuthoringApiFactory precedence guard became a `Paths:DataDirectory` temp override in the factory.
  - T11 uses probe keys, so a machine-level `HEDRON_World__*` override (which correctly wins) can't skew it.
- **Follow-ups:**
  - Slices 2–8 grow the dev-world baseline: onboarding, shops, hunting areas, help topics.
  - PT-1/PT-2 boot dev-world through `world.yaml` and the `DevWorld` profile.
  - Slice 4 migrates `WorldManifestStore` onto the shared YAML store.
- **Acknowledged debt (unchanged):**
  - `CharacterHydrationHandler`'s reset-time `SaveEntityAsync` (backlog persistence save-on-change audit);
  - cross-process staleness between telnet `startroom` and the editor (backlog).
