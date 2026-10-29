# Dev-world foundation: policy, world manifest, launch profile, hub skeleton

- **Status:** planned
- **Actors:** Player (lands and respawns in the hub), Administrator (declares and inspects the starting room in game), Designer (declares it in the editor, maintains dev-world), Operator (boots the dev-world profile), System (startup/reload resolution)
- **Module:**
  - `Core/Modules/World` owns the manifest, its store, the starting-room resolution, and the loader integration.
  - `Core/Modules/Admin` owns the `startroom` verb and its audit event.
  - `Core/Modules/Authoring` owns the editor-side manifest authoring and the rename cascade.
  - `Core/Modules/Account` and `Core/Modules/Death` change only their consumer reads.
  - `Server` gets the launch profile and the run-mode configuration helper; `Hedron.Web` gets the World page.
  - Content: `content/dev-world/`.
  - Feature homes: [`world/world-content.md`](../features/world/world-content.md), [`world/world.md`](../features/world/world.md), [`admin-authoring/`](../features/admin-authoring/admin-authoring.md).
- **Program:** slice 1 of [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md#slice-decomposition). Covers **R1–R3 and R15**, plus the world-manifest and dev-world launch-profile seams from that program's Architecture brief (resolved decision 5).

## Description

Three things land in this slice. First, the dev-world becomes the primary playable baseline: the content policy is amended so `content/dev-world/` stays generic but is no longer minimum-volume (R1–R2). Second, a world declares its own starting room through an optional `world.yaml` manifest at the content root. Every consumer then reads one resolved, atomically published starting-room snapshot, resolved in this order: manifest, then `World:StartingRoomBlueprintId`, then the void room, then any room. Third, a committed `DevWorld` environment profile boots dev-world with its own database, and a minimal hub skeleton gives new characters a safe place to land and respawn (R3, R15). The manifest gets the authoring and inspection tooling INV-18 requires: an editor World page, an admin `startroom` verb, and a rename cascade. Once this slice lands, the playtest program's PT-1/PT-2 slices can boot dev-world without any config lock-step.

## Preconditions

- Program R1–R3, R15 and resolved decision 5 (manifest plus launch profile; the default content directory is **unchanged**) are settled ([program](phase-5-content-baseline-program.md#resolved-decisions-advisor-intake)).
- `WorldContentLoader.ResolveStartingRoom` exists. It reads the raw `WorldOptions.StartingRoomBlueprintId`, falls back to `room.void`, then to the first `room.*` blueprint in dictionary order, and writes two mutable fields on the `WorldConfiguration` singleton.
- The starting-room consumers already read the **resolved** `WorldConfiguration` (verified in code):
  - `AccountSystem.CreateCharacterAsync` sets `LocationComponent` plus `RespawnComponent.RoomBlueprintId`;
  - `DeathSystem.Respawn` uses it as the fallback;
  - `CharacterHydrationHandler.ResetToStartingRoom` uses it, but reads `StartingRoomEntityId` **twice** (once to assign it, once to derive the blueprint id; `CharacterHydrationHandler.cs:103-104`). With read-through properties that is two volatile reads, so this slice changes it to one snapshot read (spec-review INV-31 finding).
  The only raw-config reader is the rename advisory in `ContentDefinitionCatalog` (ctor line ~202, use ~line 483). `GenerationRunMode`, `SimulateRunMode` and `Hedron.Web` do not read it.
- Per-kind loading is subdirectory-scoped in every consumer, so a root-level `world.yaml` is invisible to all of them:
  - `WorldContentLoader.LoadKindAsync` (`areas/`, `rooms/`, `items/`, `mobs/`);
  - `ContentDefinitionCatalog` and `ContentReferenceIndex` (via `ContentKind` subdirectories);
  - `GenerationRunMode.ValidateEmitted`.
- `**/Properties/launchSettings.json` is **gitignored by policy** (`.gitignore` line 9), so the committed launch profile must be an environment appsettings file, not a launch profile.
- `content/dev-world/` holds only `rooms/room.void.yaml` (no area, no coordinates, no exits).

## Postconditions

*(The coverage contract. Every item marked ◆ asserts internal state and maps to a named test in the Test plan.)*

1. ◆ A `world.yaml` whose `startingRoomId` names a live **room** makes `WorldConfiguration.StartingRoom` equal `(entityId, blueprintId, Source = Manifest)` after `LoadAndSpawnAsync`.
2. ◆ The fallback chain works as follows; no step throws, and the host stays up:
   - A declared id that is unknown, or names a non-room blueprint, logs a warning and falls to `World:StartingRoomBlueprintId` (`Source = Configuration`).
   - If that is also unresolved, it falls to `room.void` (`VoidFallback`).
   - Then to the **ordinal-least** live `room.*` id (`AnyRoomFallback`, now deterministic).
   - With no rooms at all, it logs an error and publishes `Source = Unresolved` with entity id 0.
3. ◆ With no `world.yaml`, resolution is identical to today (config → void → any room). Existing operator worlds need no migration.
4. ◆ A malformed `world.yaml` logs an error and is treated as an empty manifest for **resolution** (warn-not-error). Unknown keys are ignored on read and are **not preserved** on write. **Writes refuse while the existing file is malformed:** `startroom here`, the World-page save and the rename cascade return an error and leave the hand-edited file untouched, so a broken manifest is fixed by hand, never silently overwritten.
5. ◆ `ReloadAsync` re-reads `world.yaml` and re-resolves. The snapshot is replaced as **one immutable reference**, so a reader never sees an entity id from one resolution paired with a blueprint id from another.
6. ◆ A newly created character's `LocationComponent.RoomEntityId`/`RoomBlueprintId` and `RespawnComponent.RoomBlueprintId` all come from **one** read of the resolved snapshot (R15).
7. ◆ `DeathSystem.Respawn`'s fallback, and `CharacterHydrationHandler`'s reset of a character whose stored room is unresolvable, each use **one** read of the resolved snapshot for both the entity id and the blueprint id (R16 unchanged otherwise).
8. ◆ `startroom here` (admin) does three things: (a) writes `world.yaml` with `startingRoomId` set to the admin's current room, preserving the known manifest fields (`schemaVersion`; unknown keys are not preserved; refused if the existing file is malformed, see Postcondition 4); (b) re-resolves live, so the snapshot is now `Source = Manifest`; (c) publishes `StartingRoomSetByAdminEvent` with the previous and new ids. `PreviousStartingRoomId` is the **previous resolved snapshot's** `BlueprintId`, which may come from config or a fallback, not only the previous manifest declaration. No `SaveEntityAsync`. Existing characters' `RespawnComponent` is untouched.
9. ◆ `startroom here` outside a world room refuses with an error: no file write, no snapshot change, no event.
10. `startroom <any token other than here>` writes the usage line (a standard argument error) and changes nothing. `startroom` (no argument) is read-only. It shows the resolved room's name and id, the source, and any declared-but-unresolved id.
11. ◆ Catalog `RenameAsync(Room, old, new)` behaves as follows:
    - If `world.yaml` declares `old` (case-insensitive), it rewrites the manifest to `new` under the catalog write gate (file-only) and adds an advisory.
    - If `World:StartingRoomBlueprintId == old`, the existing config advisory is still emitted.
    - If `world.yaml` is **malformed**, a **room** rename is refused, because the cascade can't tell whether the manifest declares `old`. This is intended: fix the manifest by hand first. The manifest parse check runs **before any referrer write** in the gated operation, so a refused rename leaves no partial cascade. Renames of other kinds never read the manifest and are unaffected.
12. ◆ `IWorldManifestAuthoring.SaveAsync`:
    - refuses a malformed room id (format errors from `ValidateBlueprintId`);
    - saves an id that matches no authored room, with a warning;
    - with no declared id, writes a manifest with no `startingRoomId` (the config fallback applies);
    - serializes with every other catalog write.
13. ◆ A root `world.yaml` causes no load error and no template registration; the template registry holds only the per-kind templates. *(Not ◆, by rubric:* it also causes no `generate` validation error. `GenerationRunMode.ValidateEmitted` is private and scans only the kind subdirectories (`Server/GenerationRunMode.cs:167-185`), so a root file is structurally invisible to it. This is plumbing with no decision to test.)
14. ◆ Configuration layering for the `DevWorld` environment:
    - The Server host and the `generate`/`simulate` run modes layer `appsettings.{Environment}.json` over `appsettings.json`, with `HEDRON_` env vars winning.
    - `DOTNET_ENVIRONMENT=DevWorld` points the host at `content/dev-world/` and `data/dev-world.db`.
    - With no environment set, the content directory stays `data/content/`.
15. ◆ Dev-world boots on its manifest:
    - `world.yaml` declares `room.hub.square`, which resolves `Source = Manifest`.
    - Hub area `area.hub` exists (`pvp: false`), and every dev-world room carries `x`/`y`/`z`.
    - Every room is reachable from the start and has a path back (R6 applied to the skeleton).
    - `room.void.yaml` is gone.
16. The policy text is amended in every place it is stated (R1). The operator-world rules are verbatim unchanged (R2).

## Main flow

1. **Boot.** The operator runs `DOTNET_ENVIRONMENT=DevWorld dotnet run --project Server`. The generic host layers `appsettings.DevWorld.json`, giving `World:ContentDirectory=content/dev-world/` and `Persistence:DatabasePath=data/dev-world.db`. `PersistenceBootstrap` hydrates players as today.
2. **Load.** `WorldContentBootstrap` calls `WorldContentLoader.LoadAndSpawnAsync`. The per-kind loaders register and spawn the hub area and rooms; root-level `world.yaml` is not a kind and is never scanned.
3. **Resolve.** The loader calls its public `ResolveStartingRoom()`, which:
   - calls `IWorldManifestStore.Read()` (missing file means an empty manifest; a malformed one is logged and treated as empty);
   - builds the live **room** set (`RoomComponent` + `BlueprintComponent`, excluding `PersistentEntity`);
   - calls `StartingRoomResolver.Resolve(manifestId, configId, roomIds)`, following manifest → config → void → least room id;
   - logs the returned warnings and publishes `WorldConfiguration.StartingRoom = new ResolvedStartingRoom(...)` as a single reference swap.
4. **Fan-out.** `WorldContentBootstrap` publishes `WorldContentReadyEvent`. `CharacterHydrationHandler` resets characters with an unresolvable room from **one** read of the snapshot (`var start = _worldConfig.StartingRoom;` sets both `RoomEntityId` and `RoomBlueprintId`). Its pre-existing reset-time `SaveEntityAsync` is untouched (acknowledged debt; see the persistence audit).
5. **Create.** On the login path, `AccountSystem.CreateCharacterAsync` reads `_worldConfig.StartingRoom` **once** and seeds `LocationComponent` and `RespawnComponent` from it (R15). Starter abilities come from `CharacterDefaults:StartingAbilities` as today.
6. **Die.** `PlayerDeathHandler` calls `DeathSystem.Respawn`, which resolves `RespawnComponent.RoomBlueprintId` (the hub start for dev-world characters). If that is unresolvable, it uses one read of the snapshot (R16).
7. **Admin declares in game.** `StartRoomCommand` handles `startroom here`:
   - validates that the invoker's location is a world room with a `BlueprintComponent`;
   - calls `IWorldManifestStore.Read()`, sets `manifest with { StartingRoomId = bp }`, then `WriteAsync`;
   - calls `IWorldContentLoader.ResolveStartingRoom()` to swap the live snapshot (the INV-21 analogue: durable definition **and** live state);
   - publishes `StartingRoomSetByAdminEvent`, which `AdminAuditHandler` logs, and writes the confirmation.
8. **Designer declares in the editor.** The `/world` page loads its view through `IWorldManifestAuthoring` (implemented by `ContentDefinitionCatalog`), which reads `IWorldManifestStore` plus the room list plus the config fallback. The page previews the resolution through the same `StartingRoomResolver` and saves through `SaveAsync` under the catalog write gate. The change takes effect on `reload`/restart: `ReloadAsync` re-runs step 3 (flow-05).
9. **Rename.** `ContentDefinitionCatalog.RenameAsync(Room, old, new)` rewrites `world.yaml` when it declares `old`, inside the same gated operation as the referrer cascade.

## Events fired

| Event | Publisher | Subscribers | Status |
|---|---|---|---|
| `StartingRoomSetByAdminEvent(AdminEntityId, RoomEntityId, RoomBlueprintId, PreviousStartingRoomId?)` | `StartRoomCommand` (Initiator) | `AdminAuditHandler` (existing fan-in pattern, same priority as its other `*ByAdminEvent` subscriptions) | **new** — `Core/Modules/Admin/Events/` |
| `WorldContentReadyEvent` | `WorldContentBootstrap`, `ReloadCommand` | unchanged | reused |
| `ContentReloadedEvent` | `ReloadCommand` | unchanged | reused |
| `CharacterCreatedEvent`, `PlayerDiedEvent`/`PlayerRespawnedEvent` | unchanged | unchanged | reused |

Resolution itself publishes nothing. The loader is a system, so the snapshot swap is a state write, not an event (INV-5).

## Systems / handlers involved

| Piece | Kind | Status | Change |
|---|---|---|---|
| `WorldManifest` (`Core/Modules/World/WorldManifest.cs`) | sealed record (data) | **new** | `int? SchemaVersion`, `string? StartingRoomId`; `static Empty` |
| `IWorldManifestStore` / `WorldManifestStore` (`Core/Modules/World/Systems/`) | file store (World module) | **new** | `string FilePath`; `WorldManifestReadResult Read()` → `(WorldManifest Manifest, bool Exists, string? Error)`; `Task WriteAsync(WorldManifest, CancellationToken)` (`AtomicFileWrite`, camelCase, nulls omitted) |
| `StartingRoomResolver` (`Core/Modules/World/Systems/`) | static pure helper (precedent: `RoomCoordinateCollisions`) | **new** | `StartingRoomDecision Resolve(string? manifestId, string? configId, IReadOnlyCollection<string> roomIds)` → `(string? BlueprintId, StartingRoomSource Source, IReadOnlyList<string> Warnings)`, case-insensitive |
| `ResolvedStartingRoom` + `StartingRoomSource` (`Core/`, beside `WorldConfiguration`) | immutable record + enum | **new** | `(uint EntityId, string? BlueprintId, StartingRoomSource Source, string? DeclaredId)`; enum `Manifest, Configuration, VoidFallback, AnyRoomFallback, Unresolved` |
| `WorldConfiguration` | runtime config singleton | **changed** | `ResolvedStartingRoom StartingRoom { get; set; }` backed by `Volatile.Read`/`Write`, **defaulting to a non-null `Unresolved` snapshot** (`CompositionRoot.cs:125` and four test files construct `new WorldConfiguration()` without setting it); `StartingRoomEntityId`/`StartingRoomBlueprintId` become **get-only read-throughs** |
| `IWorldContentLoader` / `WorldContentLoader` | domain system | **changed** | + ctor `IWorldManifestStore`; private `ResolveStartingRoom` becomes public `ResolvedStartingRoom ResolveStartingRoom()` (re-reads the manifest, resolves over the live room set, publishes, returns) |
| `AccountSystem.CreateCharacterAsync` | domain system | **changed (read only)** | one snapshot read feeds Location and Respawn |
| `DeathSystem.Respawn` | domain system | **changed (read only)** | the fallback reads the snapshot once |
| `CharacterHydrationHandler` | handler | **changed (read only)** | `ResetToStartingRoom` takes one `StartingRoom` snapshot and sets `RoomEntityId` and `RoomBlueprintId` from it. Previously it read `StartingRoomEntityId` twice, a torn read across a concurrent `reload`/`startroom`. The existing `SaveEntityAsync` stays unchanged (acknowledged debt). |
| `IWorldManifestAuthoring` (`Core/Modules/Authoring/Systems/`) | authoring interface, implemented by `ContentDefinitionCatalog` | **new** | `WorldManifestView Load()`; `WorldManifestView Preview(WorldManifest draft)`; `Task<ContentWriteResult> SaveAsync(WorldManifest, CancellationToken)`. `WorldManifestView = (Manifest, FilePath, Exists, ConfigFallbackId, StartingRoomDecision Preview)`. A separate interface keeps the four `IContentDefinitionCatalog` test fakes untouched; DI forwards the catalog singleton under both (precedent: `IVerbRegistry` forwarding in `CompositionRoot`) |
| `ContentDefinitionCatalog.RenameCoreAsync` | authoring system | **changed** | + ctor `IWorldManifestStore`; rewrites a matching manifest declaration under the gate; the config advisory text is kept |
| `StartRoomCommand` (`Core/Modules/Admin/Commands/`) | admin command (Initiator) | **new** | `startroom [here]`, `AdminRequirement`, `CommandMatchingMode.Full` |
| `AdminAuditHandler` | handler | **changed** | + `IEventHandler<StartingRoomSetByAdminEvent>`; subscribed in `Server/Program.cs` |
| `RunModeConfiguration` (`Server/RunModeConfiguration.cs`) | host plumbing | **new** | `IConfiguration Build(string basePath, string? environmentName)`; shared by `generate` and `simulate` (replaces two hand-rolled builders) |
| `World.razor` (`Hedron.Web/Components/Pages/World.razor`, route `/world`) + nav link | Blazor page | **new** | binds `IWorldManifestAuthoring`; no logic in the component (INV-8) |

## Implementation plan — work packages

### WP1 — World manifest and the resolved starting-room snapshot (engine)

- **Scope:**
  - add `WorldManifest`, `IWorldManifestStore`/`WorldManifestStore`, `StartingRoomResolver`, `ResolvedStartingRoom`/`StartingRoomSource`, and the `WorldConfiguration` snapshot;
  - make `WorldContentLoader.ResolveStartingRoom()` public and manifest-aware over a **room-only** live set (today's map includes every blueprint kind, so a mob id would "resolve"; this fixes that);
  - make the any-room fallback deterministic (ordinal-least);
  - convert `AccountSystem`, `DeathSystem` and `CharacterHydrationHandler.ResetToStartingRoom` to a single snapshot read (the handler change is read-only; its save is untouched);
  - register the store in `WorldModule`.
- **Files:**
  - `Core/Modules/World/{WorldManifest.cs, WorldModule.cs}`;
  - `Core/Modules/World/Systems/{IWorldManifestStore.cs, WorldManifestStore.cs, StartingRoomResolver.cs, IWorldContentLoader.cs, WorldContentLoader.cs}`;
  - `Core/WorldConfiguration.cs` (+ `ResolvedStartingRoom.cs`);
  - `Core/Modules/Account/Systems/AccountSystem.cs`;
  - `Core/Modules/Death/Systems/DeathSystem.cs`;
  - `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs`;
  - tests under `Hedron.Tests/World/`, `Account/` (including T14 `CharacterHydrationHandlerTests`), `Death/`;
  - the existing `new WorldContentLoader(...)` test harnesses (2 files) get a stub store.
- **Docs:**
  - `features/world/world-content.md`: the manifest subsection, startup phase 7, the content-file-shape table row for `world.yaml`, and the config-keys consideration. Also fix the phase-7 drift: it says "fail fast", but the code falls back.
  - `features/world/world.md`: config keys.
  - `features/combat/death-system.md` line 38 and `features/accounts/account-system.md` (the snapshot read).
  - `reference/handlers.md` (`CharacterHydrationHandler` row: one snapshot read).
  - **INV-26 wall-clock-debt text (this slice owns the fix; PT-1 defers to it):** both systems it names already use the injected `IClock`: `AccountSystem.cs:70/93/168`, and `SpawnSystem` (`_clock.UtcNow`). So the debt is gone, and three places still list it:
    - `architecture/checklist.md`, INV-26 *Code* clause: drop the "pre-existing wall-clock reads in `AccountSystem` and `SpawnSystem`" parenthetical;
    - `architecture/07-testing.md` ~line 168: time is now fully sealed;
    - `.claude/agents/architecture-reviewer.md:44`: replace the private debt list with a pointer to the checklist (INV-20 / INV-27; no private copy).
  - `architecture/05-configuration.md`: the starting room becomes Category 2 (manifest) with a Category 1 fallback.
  - Flows 01, 05, 07, 20.
  - The `reference/systems.md` rows for `WorldContentLoader` (its dependency list is stale: it reads `IOptions<WorldOptions>`, not `IConfiguration`), `WorldManifestStore` and `StartingRoomResolver`.
- **Depends on:** nothing.
- **Out of scope:** any authoring surface (WP2), dev-world content and profile (WP3), and any change to `CharacterHydrationHandler` beyond its single snapshot read (its save behavior stays as is).
- **Exit criterion:** the WP1 tests in the Test plan are green; `dotnet build` and `dotnet test` are green; booting against a directory with no `world.yaml` behaves exactly as before (Postcondition 3).

### WP2 — Manifest authoring and inspection tooling (INV-18)

- **Scope:**
  - the `startroom [here]` admin verb, plus `StartingRoomSetByAdminEvent` and the `AdminAuditHandler` subscription;
  - `IWorldManifestAuthoring` on `ContentDefinitionCatalog` (Load/Preview/Save under `_writeGate`) with DI forwarding;
  - the rename cascade into `world.yaml`;
  - the `/world` editor page and its nav link.
- **Files:**
  - `Core/Modules/Admin/{Commands/StartRoomCommand.cs, Events/StartingRoomSetByAdminEvent.cs, Handlers/AdminAuditHandler.cs, AdminModule.cs}`;
  - `Server/Program.cs` (subscription);
  - `Core/Modules/Authoring/{Systems/IWorldManifestAuthoring.cs, Systems/ContentDefinitionCatalog.cs, WorldManifestView.cs, AuthoringModule.cs}`;
  - `Hedron.Web/Components/Pages/World.razor`, `Hedron.Web/Components/Layout/MainLayout.razor`;
  - tests under `Hedron.Tests/Admin/` (or `World/`) and `Authoring/`;
  - the 7 `new ContentDefinitionCatalog(...)` test harnesses get a store (or the store is an optional ctor parameter defaulting to one rooted at `WorldOptions.ContentDirectory`; the implementer picks, matching the existing optional `IContentFileReader` precedent).
- **Docs:**
  - `features/admin-authoring/admin-commands.md` (`startroom`);
  - `features/admin-authoring/content-tooling.md` (World page; rename cascade);
  - `features/admin-authoring/content-authoring.md` (~line 23, the editor-page list: add `/world`);
  - `.claude/skills/add-command/SKILL.md`, section "Admin blueprint-authoring commands (INV-21)": one line for admin mutation of a **non-entity world definition** (write the definition file, re-resolve the live derived state through the owning system, no `SaveEntityAsync`), with `startroom` as the precedent (INV-20). Help topics in slice 4 repeat the pattern;
  - `reference/commands.md` (`startroom`);
  - `reference/handlers.md` (`AdminAuditHandler` subscription);
  - `reference/systems.md` (`IContentDefinitionCatalog` row: the manifest authoring interface and the rename cascade);
  - flow-08 (the `startroom` leg) and flow-29 (World page leg; the rename-advisory text at line ~184 and the INV-12/23 note at line ~353 now include the manifest rewrite).
- **Depends on:** WP1.
- **Out of scope:**
  - a JSON authoring-API endpoint (the API stays scoped to the bakeoff page);
  - a delete guard: deleting the declared room leaves a dangling declaration, which the loader warns about and the World page preview shows (`ContentDeleteResult` has no warnings channel; widening it is not worth it here);
  - an integrity-sweep entry for the manifest.
- **Exit criterion:**
  - the WP2 tests are green;
  - the DI-smoke guard resolves `StartRoomCommand` and `IWorldManifestAuthoring`;
  - a manual smoke shows the `/world` page loading, previewing and saving a manifest against a temp content directory.

### WP3 — Dev-world: policy amendment, `DevWorld` profile, hub skeleton

- **Scope:**
  1. **Policy (R1–R2).** Amend the dev-world clauses. Dev-world stays **generic, lore-free, with functional names**. The **minimum-volume** and **"never the basis for a game / not a starting point"** clauses are lifted. It is the **primary playable baseline**, grown as development proceeds. The operator-world rules are verbatim unchanged. Places to amend:
     - `features/world/world-content.md` (the "Content ownership" table row and the "Keep `content/dev-world/` generic" bullet; also correct the phantom `isSafe`/`lightLevel` in the room row of the content-file-shape table, since neither field exists);
     - `CLAUDE.md` (line 17: "sized to the mechanic" goes; line 15's "test world" becomes "the primary playable baseline");
     - `content/dev-world/README.md`, `content/README.md` and the root `README.md` §"Your world, your content" (each restates the old clause, so all must agree; INV-27 one-fact-one-home: each keeps a one-line summary plus a link);
     - the root `README.md` **Configuration** section (`README.md:82-102`): the `DevWorld` environment profile and `DOTNET_ENVIRONMENT=DevWorld`; `appsettings.{env}.json` layering for the hosts and run modes; the `World` row (~`:88`) now says `World:StartingRoomBlueprintId` is the **fallback** behind `world.yaml`. Also correct `README.md:82` ("Each host reads an `appsettings.json` next to its project") to whatever the entry check establishes;
     - `docs/roadmap/plan.md`: the "Content ownership shapes the program" paragraph (line 49), which now states the decided answer: acceptance, CI goldens and playtests run on dev-world, and the operator's world is outside Phase 5's definition of done.
  2. **Profile.** Add the following and **leave the default `appsettings.json` content directory unchanged**:
     - `Server/appsettings.DevWorld.json` (`World:ContentDirectory=content/dev-world/`, `Persistence:DatabasePath=data/dev-world.db`; `CopyToOutputDirectory` in the csproj);
     - `Hedron.Web/appsettings.DevWorld.json` (`World:ContentDirectory` only, since the web host has no SQLite);
     - `Server/RunModeConfiguration.cs`, used by both run modes;
     - for Hedron.Web under a non-`Development` environment: a `builder.WebHost.UseStaticWebAssets()` call so Blazor's static assets are served when the environment is `DevWorld` (they are only auto-wired in `Development`).
  3. **Hub skeleton (R3, R15).**
     - `content/dev-world/world.yaml` (`schemaVersion: 1`, `startingRoomId: room.hub.square`);
     - `areas/area.hub.yaml` (name `Hub`, `pvp: false`, no aspect affinities);
     - three rooms with coordinates and two-way exits: `room.hub.square` (`Hub Square`, 0,0,0, the start), `room.hub.north` (`Hub North`, 0,1,0) and `room.hub.south` (`Hub South`, 0,-1,0). Descriptions are functional placeholders. **No spawn rules, mobs or items.**
     - Delete `rooms/room.void.yaml`: it is disconnected (violates R6) and is superseded by the manifest start. The void room remains the engine's in-memory empty-directory fallback.
     - Author the rooms with the editor or grid editor under the `DevWorld` profile, per R31.
  4. **Fixture guard test** (Test plan). Update `content/dev-world/README.md`'s "Running against it" section to the `DOTNET_ENVIRONMENT=DevWorld` form for Server, Web and `generate`, keeping the env-var form as the override.
- **Entry check (do this first):** determine which directory `dotnet run --project Server` and `--project Hedron.Web`, invoked from the **repo root**, use as the content root, and therefore whether `appsettings*.json` is actually loaded. The data paths (`content/dev-world/`, `data/dev-world.db`) are cwd-relative file paths, so the profile is documented as run from the repo root. If repo-root invocation does not load `appsettings.json` today, stop and raise it (see Open question 4). Do not silently change content-root semantics.
- **Depends on:** WP1 (the `world.yaml` schema; the guard test boots the loader). The policy text (item 1) can land independently.
- **Out of scope:**
  - onboarding rooms, shops, hunting areas, spawns, help topics and band content (slices 2–8);
  - an `isSafe` mechanic (Open question 1);
  - the playtest harness.
- **Exit criterion:**
  - the fixture guard test is green;
  - a manual smoke of `DOTNET_ENVIRONMENT=DevWorld dotnet run --project Server` from the repo root, connecting and creating a character, puts it in `Hub Square`; `startroom` shows `Source: world.yaml`; `data/dev-world.db` is created and `data/hedron.db` is untouched;
  - `DOTNET_ENVIRONMENT=DevWorld dotnet run --project Hedron.Web` serves an interactive editor listing the three hub rooms.

**Primary agent:** once all three packages land, run `architecture-reviewer` (code mode) across the combined diff.

## Content tooling impact

*(INV-18.)*

**New data file: `<World:ContentDirectory>/world.yaml`** (optional, world-content YAML only, INV-23):

```yaml
schemaVersion: 1                 # reserved; logged on mismatch, not enforced (same as other content)
startingRoomId: room.hub.square  # optional; room blueprint id where new characters land and fall back to on respawn
```

- **Shape for later, nothing speculative:** this is the home for future world-level facts (a respawn default distinct from the start, a display name). None is added now.
- **Resolution order:** `startingRoomId`, then `World:StartingRoomBlueprintId` (operator config, unchanged default `room.crossroads`), then `room.void`, then the ordinal-least `room.*`.
- **Write behavior:** writes do not preserve YAML comments (same as every content writer).

**Authoring and inspection surfaces:**

| Surface | Author | Inspect | Applies |
|---|---|---|---|
| Editor `/world` page (`Hedron.Web`) | pick the starting room from the authored room list, or "not declared — use host fallback" | file path, declared id, config fallback, resolution preview + warnings | on `reload` / restart (callout on the page, same as Standards) |
| Admin `startroom here` (telnet) | sets the manifest to the admin's current room | — | **live** (snapshot swapped) and durable (`world.yaml`) |
| Admin `startroom` | — | resolved room name + id, source, any declared-but-missing id | read-only |
| Catalog rename (editor `BlueprintIdField`) | cascades a declared starting room into `world.yaml` | advisory in the rename result | file-only |
| Hand-edit `world.yaml` | yes | `startroom`, World page | on `reload` / restart |

**Other content:**
- `content/dev-world/{world.yaml, areas/area.hub.yaml, rooms/room.hub.{square,north,south}.yaml}`; `room.void.yaml` is removed.
- No `TemplateRegistry` entry: the manifest is a non-entity world definition, never spawned (INV-12).
- No change to `generate` (it neither reads nor writes the manifest).

## Cross-cutting surfaces stressed

*(INV-19, INV-31, INV-17.)*

| Surface | Classification | Notes |
|---|---|---|
| **Commands** | Adequate | `startroom` uses the existing framework: an `ArgumentSchema` with one optional token, `AdminRequirement` via the dispatcher, and `CommandMatchingMode.Full` like the other admin verbs. |
| **Output** | Adequate | `PlainMessage` with System/Confirmation/Error severities. No new message type. |
| **Persistence** | Adequate | No new persistent state; details in the opt-in audit below. |
| **Event bus** | Adequate | One audit event on the established `*ByAdminEvent` → `AdminAuditHandler` fan-in. The loader still publishes nothing (INV-5). |
| **ECS queries** | Adequate | The room set is `GetAllComponents<RoomComponent>` joined with `BlueprintComponent`, excluding `PersistentEntity` (the same exclusion as `BuildLiveBlueprintMap`). |
| **Broadcast** | N/A | — |
| **Time / randomness** | N/A | No chance or time. The any-room fallback becomes deterministic (ordinal-least), not random (INV-26 not engaged). |
| **Content templates (INV-12)** | Adequate | The manifest is **not** a `TemplateRegistry` kind. It is a non-entity world definition with its own store, read by the loader. |
| **YAML definition store** | **Gap exposed → acknowledged debt (bounded)**, *Open question 2* | `WorldManifestStore` is a hand-rolled single-document YAML load/save. That is the third such path (balance standards, sim scenarios, manifest) **before** the help-topics slice, which the program brief counts as #3 and where it schedules the shared-store extraction. Disposition: the manifest ships hand-rolled (~40 lines), and slice 4's extraction must cover the **single-document** shape (standards is one too) and migrate `WorldManifestStore` with standards and scenarios. Rationale: slice 1 is the root dependency of the playtest substrate and every content drop, and pulling the extraction forward would serialize the program behind slice 4's largest piece. Backlog: amend the "YAML-authored definition pipeline" entry to list `world.yaml` as a migration target, and record the same obligation on slice 4's row in the program file. |
| **Configuration** | **Gap exposed → fixed in-slice** | `generate`/`simulate` each hand-roll a `ConfigurationBuilder` with `appsettings.json` only, so an environment profile would silently not apply to them. Together with the host's builder that makes three compositions. `RunModeConfiguration.Build` replaces the two run-mode builders and layers `appsettings.{env}.json`. The configuration category also shifts: the starting room is now primarily **Category 2** (world content, the manifest), with the Category 1 key kept as the operator fallback ([05-configuration.md](../architecture/05-configuration.md) updated). |
| **Sessions** | Adequate | No session-lifecycle change. Character creation and respawn read the snapshot. |
| **Modules / DI** | Adequate | `WorldModule` registers the store; `AdminModule` registers the command; `AuthoringModule` forwards the catalog singleton as `IWorldManifestAuthoring`. The DI-smoke guard covers resolution. |
| **Web host (INV-8)** | Adequate | The `/world` page only binds. Load/preview/save/validation live in the catalog, following the Standards-page precedent with the logic extracted. |
| **Validation posture** | Adequate | Warn-not-error throughout. An unknown declared room logs a warning and falls back; it does not fail startup, and `RegistryValidationBootstrap` is unchanged. A malformed id is refused at **save** time in the editor only. |
| **Concurrency (INV-31)** | Adequate (posture declared, see below) | No new thread, timer or background service. |
| **Flows (INV-17)** | Adequate | Existing flows are extended; no new recurring flow. See the next section. |

**INV-31 posture:**
- **`WorldConfiguration.StartingRoom`: guarded by immutability.**
  - **Writers:** `WorldContentBootstrap` (startup, before the listener), `ReloadCommand` and `startroom` (session threads), and `Apply.razor` → `ReloadAsync` (a circuit thread, in the web host's own process).
  - **Readers:** session threads (character creation), the heartbeat (`PlayerDeathHandler` → `DeathSystem`) and `CharacterHydrationHandler`.
  - **Guarantee:** every write publishes one new `ResolvedStartingRoom` with a `Volatile.Write`. **Every reader takes exactly one read of `StartingRoom` per operation** and uses that snapshot for both the entity and blueprint ids: `AccountSystem`, `DeathSystem` and `CharacterHydrationHandler` (changed in this slice; it previously read `StartingRoomEntityId` twice). The get-only read-through properties exist only for single-field callers; no caller may combine two of them. This fixes the existing two-field torn-read window. Concurrent writers are last-writer-wins on a whole snapshot, and both writers derive from the same file.
- **`WorldManifestStore`: confined/stateless.** It keeps no cached manifest and builds a YamlDotNet (de)serializer per call (YamlDotNet is not thread-safe; precedent: the `ThreadLocal` deserializers). Writes go through `AtomicFileWrite`.
  - **In the web host:** every manifest write goes through the catalog's `_writeGate`, so it serializes with rename cascades.
  - **In the Server process:** `startroom` writes are **not** gated (the gate is the web host's catalog). Two admins running `startroom here` at once is last-writer-wins on the file (`AtomicFileWrite` keeps each write whole), and the live snapshot converges because each resolution re-reads the file.
  - **Cross-process:** `startroom` in the Server and the editor in Web can both write `world.yaml`. That is the existing, backlogged cross-process staleness between telnet builder verbs and the editor; this slice does not widen it.
- **Live world components:** no new mutation path. `startroom` mutates only the config snapshot and a file.

**Persistence opt-in audit (INV-14 / INV-22 / INV-23):**
- **Level 1 — entity domains:** no new entity construction. The hub area and rooms are **world content**: fresh-spawned from YAML, never `PersistentEntity`. Characters are unchanged (persistent, created by `AccountSystem`).
- **Level 2 — components:**
  - `RespawnComponent` (`[Persistent]`) and `LocationComponent` are touched as **values only**, with no shape change. They are correctly persistent for characters.
  - `WorldConfiguration` and `ResolvedStartingRoom` are not components. They are runtime state re-derived on every load/reload and never persisted.
  - `world.yaml` is content YAML and never SQLite (INV-23).
- **Level 3 — save-on-change:** no new `SaveEntityAsync` anywhere. `startroom` changes no persistent entity: existing characters' respawn points are deliberately left alone (`setrespawn` exists for that). *Pre-existing, acknowledged debt (SR-2):* `CharacterHydrationHandler` already saves a character it resets (`CharacterHydrationHandler.cs:70`), which falls outside INV-22's three permitted boundary saves. This slice changes only that handler's snapshot read, not its save. The debt is tracked in the backlog's persistence save-on-change audit entry ([`../roadmap/backlog.md`](../roadmap/backlog.md), "Persistence save-on-change cleanup + manual `save`/`quit` commands").

## Flows introduced or modified

*(INV-17.)* The PR updates each flow file's body (and its mermaid where the step appears) plus the `flows/README.md` index text where noted.

- **[Flow 1 — Server startup](../architecture/flows/flow-01-server-startup.md)** (WP1): step 4's "resolves `StartingRoomEntityId`" becomes the manifest → config → void → any-room resolution, published as one snapshot. Note that `world.yaml` is read, but is not a kind.
- **[Flow 5 — Content reload](../architecture/flows/flow-05-content-reload.md)** (WP1): after re-spawn, `ReloadAsync` re-reads `world.yaml` and re-resolves (a new step between 5 and 6). "Moved to the starting room" now means the resolved snapshot.
- **[Flow 7 — Login journey](../architecture/flows/flow-07-login-character-flow.md)** (WP1): step 5's character creation seeds `LocationComponent` + `RespawnComponent` from one read of the resolved starting room.
- **[Flow 20 — Death & respawn](../architecture/flows/flow-20-mob-death-respawn.md)** (WP1): step 7's fallback reads the resolved snapshot, not "`WorldConfiguration.StartingRoomBlueprintId`" as a config value.
- **[Flow 8 — Admin authoring journey](../architecture/flows/flow-08-admin-room-creation.md)** (WP2): add the `startroom [here]` leg (manifest write → live re-resolve → audit). Index-row title gains `startroom`.
- **[Flow 29 — Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md)** (WP2): add the World-page leg (load/preview/save under the write gate) and the rename cascade into `world.yaml`. Update the "never a config write" advisories so they distinguish config (advisory) from manifest (rewritten).

No new canonical flow: manifest resolution is a sub-step of the startup and reload flows, not a separate recurring chain.

## Test plan / Verification

*(INV-25; rubric in [07-testing.md](../architecture/07-testing.md).)*

| # | Tier | Target | Asserts | Postcondition |
|---|---|---|---|---|
| T1 | system-unit | `StartingRoomResolverTests` | declared + present → `Manifest`; declared + missing → `Configuration` with a warning; manifest null → `Configuration` (no warning); config missing → `VoidFallback`; no void → `AnyRoomFallback` picks the ordinal-least id regardless of input order; empty set → `Unresolved`; case-insensitive match | 1, 2, 3 |
| T2 | system-unit (temp dir) | `WorldManifestStoreTests` | missing file → `Empty`, `Exists=false`, no error; write→read round-trip preserves `StartingRoomId`/`SchemaVersion`; malformed YAML → `Empty` + `Error`, no throw; an unknown key is ignored on read and absent after a rewrite; a null `StartingRoomId` is omitted from the written file and reads back null | 4, 8 |
| T3 | flow (temp content dir, real loader + registry) | `WorldContentLoaderStartingRoomTests` | `world.yaml` + rooms → `StartingRoom = (entity, id, Manifest)`; `world.yaml` naming a **mob** blueprint → not accepted (falls to config); a root `world.yaml` registers no template and logs no load error; after editing `world.yaml`, `ReloadAsync` → the snapshot names the new room (and is a new reference); public `ResolveStartingRoom()` re-reads the file; a content directory with **no room templates** → `StartingRoom.Source == Unresolved` with entity id 0, an error logged and no throw | 1, 2, 5, 13 |
| T4 | flow (existing) | `WorldContentLoaderTests`, `WorldContentReloadTests` (on touch) | still green with a stub/empty store; no-manifest behavior unchanged | 3 |
| T5 | system-unit | `AccountSystemTests` (extend `CreateCharacterAsync_attaches_RespawnComponent` / `_attaches_LocationComponent`) | Location entity id + blueprint id and `RespawnComponent.RoomBlueprintId` all equal the snapshot's values; fixtures move from setting the two old fields to setting `StartingRoom` | 6 |
| T6 | system-unit | `DeathSystemTests` (update the two `Respawn_falls_back_to_starting_room_*` tests) | the fallback location equals the snapshot's `(EntityId, BlueprintId)` | 7 |
| T7 | flow | `StartRoomCommandFlowTests` | admin in room X runs `startroom here` → `world.yaml` declares X with other fields preserved; `StartingRoom.Source == Manifest` and its entity is X; `StartingRoomSetByAdminEvent` is captured with the previous id; a character created afterwards lands at X with respawn X; a pre-existing character's `RespawnComponent` is unchanged; no persistence save is recorded | 8 |
| T8 | flow | same class | `startroom here` with the invoker in a non-room location → error output; file unchanged; snapshot reference unchanged; no event. `startroom here` with a **malformed existing `world.yaml`** → error, file byte-identical, no event. `startroom bogus` → usage error, nothing changed | 4, 9, 10 |
| T9 | system-unit (temp dir) | `ContentDefinitionCatalogTests` | `Rename_ManifestStartingRoom_RewritesWorldYaml` (the manifest now names the new id, and the advisory is present); the existing `Rename_StartingRoom_EmitsAdvisoryWarning_NoConfigOrSqliteWrite` stays green (no manifest present); renaming a non-starting room leaves `world.yaml` byte-identical; with a **malformed `world.yaml`**, a room rename → error, `world.yaml` **and every referrer file** byte-identical (no half-cascade); a non-room rename (e.g. an item) → succeeds, unaffected by the manifest | 4, 11 |
| T10 | system-unit (temp dir) | `WorldManifestAuthoringTests` (against `ContentDefinitionCatalog`) | `SaveAsync` refuses a malformed id (error, nothing written); `SaveAsync` over a **malformed existing `world.yaml`** → error, file byte-identical; unknown room → `Success` + warning; null → writes a manifest with no `startingRoomId`; `Preview` returns the resolver decision over catalog room ids plus the config fallback | 4, 12 |
| T11 | system-unit | `RunModeConfigurationTests` (temp dir) | `appsettings.{env}.json` overrides `appsettings.json`; a missing env file is tolerated; a null environment loads the base only | 14 |
| T12 | flow (fixture guard) | `DevWorldFixtureTests` | copy the repo's `content/dev-world/` (located by walking up to `Hedron.sln`) to a temp dir; boot the real loader; assert: `StartingRoom.Source == Manifest` and its id is `room.hub.square`; `area.hub` exists with `Pvp == false`; **every** dev-world room template has `X`/`Y`/`Z`; every room is reachable from the start, and the start is reachable from every room (BFS over template exits both ways); no `room.void` template. The guard keeps paying as content drops grow the world (R6) | 15 |
| T13 | architecture-guard (existing) | DI-smoke `CompositionRoot_resolves_all_commands_handlers_and_systems` | resolves `StartRoomCommand`, `IWorldManifestStore` and `IWorldManifestAuthoring` (no new guard; the existing test covers new registrations) | — |
| T14 | handler (on-touch ratchet; the handler had no tests) | `CharacterHydrationHandlerTests` | a character whose stored room is unresolvable is reset to the snapshot's `(EntityId, BlueprintId)` as a pair (both from one `StartingRoom` read); a character with a resolvable room is untouched | 7 |
| T15 | system-unit (extend existing) | `ContentDefinitionCatalogWriteSerializationTests.Every_public_mutator_completes_in_sequence_without_deadlock` (`Hedron.Tests/Authoring/ContentDefinitionCatalogWriteSerializationTests.cs:128`) | add `IWorldManifestAuthoring.SaveAsync` **and** a room rename that rewrites the manifest to the mutator sequence; both complete without deadlock under the non-re-entrant `_writeGate` | 12 (serialization), 11 |

**Skipped (with reason):**
- `World.razor` markup (presentation tier; its logic is T10).
- The exact prose of `startroom` output and the advisories (assert type/severity only).
- `StartingRoomSetByAdminEvent` (a thin payload record).
- `AdminAuditHandler`'s new branch: a one-line structured log. `AdminAuditHandler` has no tests today, and the branch makes no decision. T7's event capture covers the fan-in.
- The `appsettings.DevWorld.json` files themselves, and `UseStaticWebAssets`: host lifetime and config data, covered by the WP3 manual smoke plus T11 for the layering logic.
- Policy prose.
- The `WorldConfiguration` volatile swap's thread-safety: a reference assignment, with no meaningful deterministic test. T3 asserts that a new reference is published on reload.

**Testability gaps:** none. Resolution is a pure function, the store takes its directory from options (temp-dir testable), and nothing touches randomness or wall-clock time.

## Design notes

*(Slice-relevant rationale. The full seam reasoning lives in the program's [Design notes](phase-5-content-baseline-program.md#design-notes) ("A world declares its own start") and [Architecture brief](phase-5-content-baseline-program.md#architecture-brief). It is not duplicated here.)*

- **Why a manifest, not just a profile:** the starting room is a fact about the world, not the host. Operator worlds, dev-world, the playtest harness's per-world composition (PT-1) and future instances all boot different content, so a host key that must change in lock-step with `ContentDirectory` breaks in every new launch context. *Program resolved decision 5.*
- **The consumer change is smaller than the brief implied:** all runtime consumers already read the resolved `WorldConfiguration`. The work is therefore (a) the resolution **source** and (b) making the published value **one immutable snapshot**. Only the catalog's rename guard read raw config, and it now also reads the manifest.
- **Per-world, no statics:** the resolver is a pure function, the store is rooted at its `WorldOptions.ContentDirectory`, and the snapshot lives on the `WorldConfiguration` instance. PT-1's per-world composition therefore gets an independent resolution by constructing its own trio. Nothing static or process-global is added.
- **Room-only resolution:** today's resolution searches every blueprint kind, so a manifest or config naming a mob id would "resolve" to a mob. Restricting it to `RoomComponent` entities is a correctness fix that falls out of the new resolver.
- **Deterministic last resort:** "first `room.*` in dictionary order" becomes "ordinal-least `room.*`". This only matters for broken worlds, but it makes the fallback testable and reproducible.
- **The respawn point is per-character state:** `RespawnComponent.RoomBlueprintId` is seeded at creation, so moving a world's start does not move existing characters' respawn points. This is deliberate: `setrespawn` exists, and respawn may later diverge from the start (a future manifest field). A character whose stored respawn no longer resolves still falls back to the resolved start (R16, edge case "Death far from the hub").
- **Rename cascades, delete warns:** a rename is a cascade-rewrite operation across referrers, and the manifest is a referrer living in the same content directory. Leaving it dangling would silently drop the world onto the config or void fallback. Delete has no warnings channel, so a deleted start surfaces through the loader warning, the World-page preview and `startroom`.
- **Hub "safe" is by construction in this slice** (see Open question 1). R3 says the hub area is "marked safe (`isSafe`)", but no such field or mechanic exists. `components-planned.md` lists a planned `RoomDataComponent.IsSafe`, and world-content.md documents a phantom room `isSafe`. With R10 (no mob initiates combat) and a hub that has no spawns, nothing can attack a player in the hub. The area is authored `pvp: false`.
- **Launch profile = environment appsettings:** `launchSettings.json` is gitignored by policy (machine-specific, and it pins the environment for everyone), so `appsettings.DevWorld.json` selected by `DOTNET_ENVIRONMENT=DevWorld` is the committed, IDE-agnostic form. The env-var override documented in the dev-world README keeps working on top.
- **The dev-world profile deliberately does not set `World:StartingRoomBlueprintId`:** the manifest supplies it, and the fixture guard proves the manifest path rather than a config coincidence.

## Open questions

*(Q1–Q3 resolved by the user on 2026-10-29. Q4 stays open until the WP3 entry check.)*

1. ~~**`isSafe` (R3).**~~ **Resolved: safe by construction.** The hub has no hostile spawns, is authored `pvp: false`, and R10 means no mob starts a fight. Hub shopkeepers (later content drops) carry the existing mob protection (`protection: [untargetable]`, slice 12b), so the hub holds nothing a player can fight. The phantom `isSafe`/`lightLevel` entry in world-content.md is corrected. A mechanical safe-area rule is **not** scheduled; revisit it when aggro mobs or PvP arrive. Program R3 is reworded to match.
2. ~~**YAML store ordering (INV-19).**~~ **Resolved: accept bounded debt.** The manifest store ships hand-rolled. Slice 4 (`help-topics`) migrates it onto the shared store along with standards and scenarios. The program's slice-4 row and the backlog entry are amended.
3. ~~**Admin verb scope.**~~ **Resolved: ship both.** `startroom [here]` with `StartingRoomSetByAdminEvent`, plus the `/world` editor page.
4. **Config file location (WP3 entry check).** Code reading so far:
   - The live host uses `Host.CreateDefaultBuilder`, whose content root is the current directory.
   - The `generate`/`simulate` run modes call `AddJsonFile("appsettings.json")` with no base path.
   - `dotnet run --project Server` keeps the shell's working directory.

   So from the repo root, `Server/appsettings.json` is most likely **not loaded today**. The match between `WorldOptions` defaults and the file hides it. WP3's first step confirms this empirically. **If confirmed**, the proposed fix (anchor the JSON sources to `AppContext.BaseDirectory`; data paths stay cwd-relative) needs explicit user sign-off before implementation, because it changes which `appsettings.json` copy an operator's edits take effect from. Not load-bearing for any INV (spec review: SR-5 clear); WP1–WP2 don't depend on it. **If confirmed and approved, the fix also covers:**
   - the README configuration text (above);
   - routing the Server and Web hosts' JSON sources through the shared `RunModeConfiguration` too (INV-19), so there's no third and fourth hand-rolled layering;
   - a guard that the anchored JSON sources never take precedence over per-test overrides, specifically `AuthoringApiFactory`'s `World:ContentDirectory` (`Hedron.Tests/Web/Api/AuthoringApiFactory.cs:48-52`). Pin this in T11 so the Tier-6 tests can never write into the repo's content tree.

## Related

- [`phase-5-content-baseline-program.md`](phase-5-content-baseline-program.md): the umbrella (R1–R3, R15; slice decomposition row 1).
- [`playtest-automation-program.md`](playtest-automation-program.md): PT-1/PT-2 boot dev-world through this slice's `world.yaml`; PT-2's smoke golden walks this hub skeleton.
- [`tiered-overworld-program.md`](tiered-overworld-program.md): R22's "Phase-5 curated starting region" is the dev-world baseline this slice founds.
- Features: [`world-content.md`](../features/world/world-content.md) · [`world.md`](../features/world/world.md) · [`account-system.md`](../features/accounts/account-system.md) · [`death-system.md`](../features/combat/death-system.md) · [`admin-commands.md`](../features/admin-authoring/admin-commands.md) · [`content-tooling.md`](../features/admin-authoring/content-tooling.md)
- Architecture: [`05-configuration.md`](../architecture/05-configuration.md) · [`07-testing.md`](../architecture/07-testing.md) · [`checklist.md`](../architecture/checklist.md) (INV-12, 17, 18, 19, 21, 22, 23, 25, 31)
- Backlog: "YAML-authored definition pipeline" (Open question 2); the cross-process authoring staleness entry (INV-31 posture).
