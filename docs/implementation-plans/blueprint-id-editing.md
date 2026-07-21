# Blueprint ID editing (rename + choose-at-creation)

**Status:** planned

**Actors:** Administrator / content author (via the offline Blazor content editor)

**Module:** [`Core/Modules/Authoring/`](../../Core/Modules/Authoring/) — the content-tooling layer ([admin-authoring feature](../features/admin-authoring/admin-authoring.md), [content-tooling.md](../features/admin-authoring/content-tooling.md)). Also touches `Core/Modules/World/Templates` + the `I*ContentWriter` family (already catalog dependencies) and the `Hedron.Web` editor pages (thin surface).

## Description

Authored world content (areas, rooms, items, mobs) is stuck with the generated `*.adhoc.*` blueprint id it is minted with — the editor exposes no way to choose a deliberate id at creation or change it afterward. This slice lets an author **set a clean blueprint id at creation** and **rename an existing definition's blueprint id**, with the rename **cascading through every YAML reference** to the old id so the content graph stays consistent. Rename is the structural sibling of the existing `DeleteAsync` cascade — same reference-index traversal, but each referring field is *rewritten to the new id* rather than cleared. It is a **YAML-only, offline** operation (matching the editor's whole posture, INV-22/23): the live world adopts the new ids on the next `reload`; references that live outside YAML (persistent player state in SQLite, config) are surfaced as warnings, not rewritten.

## Preconditions

- The offline Blazor content editor is running against the loopback `Hedron.Web` host (`AddContentBootstrapHostedServices` only — no telnet, no heartbeat, no SQLite session, no live world).
- The content directory is writable and the per-kind `I*ContentWriter`s and serializer are composed (existing catalog dependencies).
- **Rename:** the target definition file for `oldId` of the given `ContentKind` exists on disk.
- **Create-with-id:** the author has entered a deliberate blueprint id on the New form (blank falls back to the generated `*.adhoc.*` id).

## Postconditions

*The coverage contract — every invisible-state assertion below maps to a named test in [Test plan / Verification](#test-plan--verification).*

- **Rename — own file:** a YAML file for `newId` exists carrying the definition's full state; the `oldId` file is deleted (no orphan).
- **Rename — cascade:** every external referring field is rewritten `oldId → newId` across all six declared edges: `Room.AreaId`, `Room.Exits[dir]`, `Item.SpawnRoomBlueprintId`, `Mob.SpawnRoomBlueprintId`, `Area.Rooms[]`, and the **new** `Room.SpawnRules[].BlueprintId → Mob/Item`.
- **Rename — self-reference:** the renamed definition's own self-referential fields (a self-loop exit, `Exits[dir] == oldId`) point at `newId`, not `oldId`, in the new file.
- **Rename — validation refusal:** rename refuses (no file written, `oldId` file intact, structured errors returned) when `newId` is malformed (empty, path separator, `..`, or an illegal filename char) or already taken by a definition of the same kind (collision → **refuse, no merge**).
- **Rename — soft prefix:** a kind-prefix mismatch on `newId` (e.g. a room id lacking the `room.` prefix) is a **non-blocking warning**, not a refusal (the loader keys off the kind subdirectory, not the prefix).
- **Rename — out-of-YAML warn-not-rewrite:** renaming a Room whose `oldId` equals the configured `World:StartingRoomBlueprintId` (or any room) surfaces an advisory warning; **no `appsettings.json` and no SQLite row is modified**. Persistent player/item `LocationComponent.RoomBlueprintId` and the config re-key on the next `reload`; a parked player falls back to the starting room via existing hydration recovery.
- **New spawn-rule edge — two-kind resolution:** `Referrers(Mob, X)` and `Referrers(Item, Y)` return rooms whose `SpawnRules` reference `X`/`Y`; `SweepBroken`/`BrokenFor` flag a spawn-rule id that resolves as **neither** mob nor item and do **not** flag one that resolves as the other kind.
- **Delete parity:** deleting a mob/item now also removes matching `Room.SpawnRules` entries (the edge fix applies to delete uniformly, closing the pre-existing SR-2 gap).
- **Create-with-id:** a definition created with a deliberate id carries that id on disk; the create write refuses a malformed or already-taken id (same validation as rename).
- **Boundary invariants:** no event is published (INV-5); no `EntityService`/SQLite is touched (INV-22/23); a mid-cascade referrer failure does not throw (best-effort, matching `DeleteAsync`).

## Main flow

*Rename (the structural sibling of `DeleteAsync`; create-with-id variant follows).*

1. Author opens a definition in an editor page and invokes **Rename**, entering `newId`.
2. The page calls `IContentDefinitionCatalog.RenameAsync(kind, oldId, newId)` — a thin caller (INV-8); no logic in the component.
3. Catalog validates `newId` via `IContentValidator.ValidateBlueprintId(kind, newId)` — non-empty + filename-safe (format error → `ContentRenameResult.Failed`, no write; kind-prefix mismatch → deferred warning).
4. Catalog checks uniqueness via `IContentReferenceIndex.Resolves(kind, newId)` (and loads the target via `Load(kind, oldId)`); collision or missing target → `Failed`, no write.
5. Catalog constructs a fresh template with `BlueprintId = newId`, copies all state, rewrites the new template's **own** self-references `oldId → newId`, and writes the new file via the matching `I*ContentWriter` (atomic `tmp → rename`).
6. Catalog queries `IContentReferenceIndex.Referrers(kind, oldId)` (excluding the target's own self-reference — handled in step 5) and, per referrer, calls the generalized `TryApplyCascadeAsync(referrer, oldId, newId)` rewriting the referring field to `newId` (best-effort; log-and-continue on per-referrer failure).
7. Catalog deletes the old YAML file (`File.Delete`) — no `EntityService`, no SQLite, no live-world mutation (INV-22/23).
8. Catalog folds out-of-YAML advisories into `Warnings`: if `kind == Room` and `oldId == WorldOptions.StartingRoomBlueprintId`, warn to update `appsettings.json`; a standing advisory notes persistent player/item locations (SQLite) re-key on `reload`. Returns `ContentRenameResult(oldPath, newPath, oldId, newId, cascadeEdits, warnings)`.
9. The page renders the summary; the author later runs **Apply to live** (`IWorldContentLoader.ReloadAsync`, [Flow 5](../architecture/flows/flow-05-content-reload.md)) — the live world adopts the new ids on reload.

**Create-with-id variant.** (a) On the New form the author enters a deliberate id (or leaves it blank for adhoc); the page calls `CreateNew(kind, name, blueprintId)` to build the template, then a create-guarded write (`CreateAsync`) that runs `ValidateBlueprintId` + a uniqueness check and refuses a malformed or already-taken id before writing via the matching writer. (b) On success the page navigates to the edit route for the new id. No cascade, no delete — it is a first write.

## Events fired

**None.** This is an offline catalog operation. `IContentDefinitionCatalog`, `IContentReferenceIndex`, and `IContentValidator` are domain systems that **return results and publish nothing** (INV-5); the Blazor page is the initiating surface and consumes the result directly. There is no event bus, no heartbeat, and no live-world mutation in this slice.

## Systems / handlers involved

| Component | Role | New / reused |
|---|---|---|
| `IContentDefinitionCatalog` | `RenameAsync` verb (rebuild + generalized cascade + delete); `CreateNew(kind, name, id?)` overload; `CreateAsync` create-guard; generalized `TryApplyCascadeAsync(referrer, oldId, newId?)` | **extended** |
| `IContentReferenceIndex` | `Referrers`/`SweepBroken`/`BrokenFor` over the **two-kind** `(Room, SpawnRules[]) → Mob/Item` edge; `EdgeDeclaration.TargetKind` → `TargetKinds` | **extended** |
| `IContentValidator` | `ValidateBlueprintId(ContentKind, string)` — format + soft prefix rule | **extended** |
| `IAreaContentWriter` / `IRoomContentWriter` / `IItemContentWriter` / `IMobContentWriter` | atomic `tmp → rename` file writes for new-file + cascade rewrites | reused |
| `ContentRenameResult` | result DTO (modeled on `ContentDeleteResult` + `ContentWriteResult`) | **new** |
| `AreaEditor` / `RoomEditor` / `ItemEditor` / `MobEditor` + grid detail panel | thin surface: editable id on create + rename affordance (shared component) | **extended** |
| — handlers | **none** — offline, no event bus, no heartbeat path | n/a |

## Design notes

*Durable seam rationale — kept in the shipped feature docs on disintegration (INV-28).*

- **Rename is delete's sibling; the verb belongs with the data it mutates.** The rename *mechanism* is a new operation on [`IContentDefinitionCatalog`](../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs) — the system that already owns YAML definition read/write and the delete cascade — reusing [`IContentReferenceIndex.Referrers`](../../Core/Modules/Authoring/Systems/IContentReferenceIndex.cs) to find who points at the old id. Putting it anywhere that merely *consumes* ids (a command, the loader, the UI) would be a layering inversion (INV-1/INV-2). The UI is a thin caller (INV-8, extended to this surface as delete/create already are).
- **Cascade generalizes over the write value, not a new traversal.** `DeleteAsync`'s [`TryCascadeClearAsync`](../../Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs) already walks referrers and mutates each referring field (clear/remove). Clear is just *rewrite-to-empty*; rename is *rewrite-to-newId*. One shared "apply cascade edit(newValue)" over the per-kind field switch serves sweep-read, delete-clear, and rename-rewrite — meeting the ≥3-consumer bar for a shared seam (INV-19) rather than a copy of the delete logic.
- **The spawn-rule reference edge is declared now, closing a pre-existing gap.** `RoomTemplate.SpawnRules[].BlueprintId` is a real reference to the mob/item the `SpawnSystem` spawns, but it is **absent** from `ContentReferenceIndex.DeclaredEdges` — so today deleting a mob does not clean up rooms that spawn it, and the integrity sweep misses it (an SR-2 pre-existing gap). Adding `(Room, SpawnRules[].BlueprintId) → Mob/Item` as a declared edge is a one-line data change per the index's extension design and fixes rename, delete-cascade, and the integrity sweep **uniformly** (INV-19). Resolved into scope at intake. *Bounded over-match (spec gate):* a `SpawnRule` id carries no kind discriminator, so in the pathological case where a mob file and an item file share the same id, delete-cascade of one kind removes the room's spawn-rule entry that may have meant the other — a pre-existing property of `SpawnRule`, harmless for rename (pure id-equality rewrite) and bounded by the single-author offline scope. Not a claimed postcondition; noted, not fixed here.
- **YAML-only boundary is preserved (INV-22/23).** Rename never opens SQLite or mutates the live world. The blueprint id appears in two places the offline editor cannot/should not touch: `LocationComponent.RoomBlueprintId` (persistent player/item state, SQLite) and `World:StartingRoomBlueprintId` (appsettings). These are **warned, not rewritten**; the live world re-keys on the next `reload`; a player saved in a renamed room falls back to the starting room via `CharacterHydrationHandler`'s existing unresolvable-`RoomBlueprintId` recovery.
- **Template ids are immutable, so rename rebuilds the definition.** `RoomTemplate.BlueprintId` (and peers) is get-only, constructor-set, and the id *is* the on-disk file name. Rename therefore constructs a fresh template with the new id, copies state across, writes the new file, and deletes the old — mechanical, and the writers already do atomic `tmp → rename` file writes.
- **Choose-at-creation and post-hoc rename share the same id validation.** Creating with a deliberate id (editable id field on the New form, or an explicit-id `CreateNew` path) validates the same rules as rename — non-empty, filename-safe, and not already taken.
- **The cascade stays best-effort (non-transactional), matching delete.** A mid-cascade failure can leave a partially-rewritten set, recoverable via the integrity sweep — the same accepted posture as `DeleteAsync`, tracked by the existing [*Atomic multi-file content cascade*](../roadmap/backlog.md) backlog item (extend it to name rename).
- **The two-kind spawn-rule edge resolves against either kind (OQ1 resolved).** `SpawnRule.BlueprintId` points at a mob *or* an item, so the edge model generalizes `EdgeDeclaration.TargetKind` (single) to `TargetKinds` (one-or-more). `Referrers(kind, id)` matches an edge when the searched kind is *in* the set — so `Referrers(Mob, X)` and `Referrers(Item, Y)` both surface the room; the rename/delete cascade only needs string equality on the id, so an over-match is harmless (there is nothing else to rewrite). `SweepBroken`/`BrokenFor` report a spawn-rule id broken only when it resolves against **none** of the target kinds — the one place the two-kind distinction matters, and resolve-against-either removes the false-positive a two-single-edge model would produce. The existing five edges migrate to single-element `TargetKinds` (a mechanical change); the new edge is a one-line data addition (INV-19 preserved). The label is `SpawnRules[{referencedId}]` (encodes the id, not a shifting list index), so the id-matched cascade apply is stable and idempotent. The public `ReferenceEdge` record's `TargetKind` is doc-only (unused by the engine — the private `EdgeDeclaration` drives behavior); update it for parity or leave it.
- **Rename ordering handles self-references without double-writing (OQ4 resolved).** The target's *own* self-loop exits are rewritten in-memory when the new template is built (step 5) — *not* by loading the about-to-be-deleted `oldId` file. `Referrers(kind, oldId)` naturally includes the target itself when it self-references, so step 6 explicitly **excludes** the self-referrer (matching `ReferrerKind == kind && ReferrerBlueprintId == oldId`) to avoid a redundant load-and-write against the stale file. An area renaming a room that also appears in that area's `Rooms[]` is a *different* definition (the area) and is handled as an ordinary external referrer — no special case.

## Architecture brief

*In-flight forward analysis — trimmed on ship (INV-28).*

### Seams + recommended homes

| New seam | Kind | Home / layer |
|---|---|---|
| `RenameAsync(ContentKind kind, string oldId, string newId, CancellationToken)` | verb | `IContentDefinitionCatalog` (domain-tier authoring system). Returns a structured result mirroring `ContentDeleteResult` — the renamed file, each cascade edit, and warnings; **returns results, publishes nothing** (INV-5; offline, no bus). |
| Id validation (format + uniqueness) | rule | Catalog + `IContentValidator`. Non-empty; **filename-safe** (no path separators, no `..`, no reserved chars — the id becomes a file name); unique (`IContentReferenceIndex.Resolves(kind, newId)` false / `ITemplateRegistry.TryGet` false). Kind-prefix (`room.`, `area.`…) is a **soft convention → warn-not-error** (the loader keys off the kind subdirectory, not the prefix). |
| `(Room, SpawnRules[].BlueprintId) → Mob/Item` | reference edge | `ContentReferenceIndex.DeclaredEdges` (data). |
| Choose-id-at-creation | verb path | Editable id on the New form / explicit-id `CreateNew` overload — flows into the first `SaveAsync` with the same validation. |
| Editor id field + "Rename" action | surface | `Hedron.Web` `AreaEditor`/`RoomEditor`/`ItemEditor`/`MobEditor` + the grid editor's detail panel; mirrors the existing delete-confirm affordance. Thin — all logic in the catalog (INV-8). |

### Family disposition

- **General-by-construction across all four `ContentKind`s.** The edge model + per-kind writer dispatch the catalog already uses generalize rename with no per-kind special-casing — build it general.
- **Ability / Effect / Aspect id references are out of scope.** Those definitions cross-reference by id too (checked by `IContentValidator.ValidateRegistry`), but they are **not `ContentKind`** and live outside `IContentDefinitionCatalog` / `IContentReferenceIndex` entirely. A future rename over them is a *shape-for-later* generalization gated on those families gaining a catalog — a Design note, not built now.

### Observers, contributors, ordering

- **No observers, no contributors, no events.** Offline file operation; nothing reacts, nothing aggregates. INV-6/7/24 moot.
- **No ordering/timing constraints.** Single-threaded offline file IO. **INV-31 moot** (no live-world mutation, no new thread/timer). **INV-26 moot** (no randomness, no clock).

### Invariants in tension (cite)

- **INV-22 / INV-23** — the YAML-only boundary is the load-bearing constraint; the resolved scope keeps rename off SQLite and the live world.
- **INV-19** — three parity concerns: the cascade generalization (clear/rewrite), the declared-edge extension, and the new editor surface framework (id field + rename action) all land in this slice.
- **INV-21** — rename changes the blueprint *template* id; live instances re-key on the next `reload`, never retroactively — consistent with the offline/reload model and with INV-21's template/instance separation.
- **INV-8** — the editor pages stay thin.
- **INV-16 / INV-29 / INV-17** — the [`reference/systems.md`](../reference/systems.md) rows for `IContentDefinitionCatalog` and `IContentReferenceIndex` gain the rename verb + the new edge; the [Flow 29 content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md) gains the rename leg.
- **INV-25** — new system behavior (rename cascade, id validation, the new edge) ships tests; the planner's Test plan owns the enumeration.
- **INV-20** — swept at the spec gate: no `.claude/skills/*` or `.claude/agents/*` advises the content-catalog / `EdgeDeclaration` / cascade / content-authoring patterns this slice changes, so nothing goes stale. Nothing to update.

### Resolved decisions (do not relitigate)

1. **Scope = YAML-only.** Persistent (`LocationComponent.RoomBlueprintId`) and config (`World:StartingRoomBlueprintId`) references are **warned, not rewritten**; live world re-keys on `reload`. (Intake.)
2. **Declare the spawn-rule edge in this slice** — `(Room, SpawnRules[].BlueprintId) → Mob/Item`, fixing rename + delete + integrity sweep together. (Intake.)
3. **Both choose-at-creation and post-hoc rename are in scope.** (Original ask.)
4. **Surface = offline Blazor editor only.** No telnet `rename` and no live-world rename this slice.

## Open questions

*Load-bearing for the planner / spec gate.*

1. ~~**Two-kind target for the spawn-rule edge.**~~ **Resolved (planner):** generalize `EdgeDeclaration.TargetKind` → `TargetKinds` and **resolve-against-either-kind** — `Referrers` matches when the searched kind is in the set; `SweepBroken`/`BrokenFor` report broken only when the id resolves against none. Rationale in Design notes. No residual.
2. ~~**Id-format charset.**~~ **Resolved (planner):** `ValidateBlueprintId` accepts `^[A-Za-z0-9._-]+$`, non-empty, rejects `/`, `\`, and any `..` segment (the id becomes a file name). Kind-prefix (`room.`, `area.`…) is **warn-not-error**. Settle exact reserved-name handling (Windows `CON`/`PRN`/… — low risk) in the validator. No load-bearing residual.
3. ~~**Collision semantics.**~~ **Resolved (planner):** if `newId` already exists for the same kind → **refuse** (no merge), for both rename and create-with-id.
4. ~~**Cascade coverage of self-references.**~~ **Resolved (planner):** the target's own self-loop exit is rewritten when the new template is built (Main-flow step 5); `Referrers` self-inclusion is excluded from the external cascade (step 6). Area-renaming-a-listed-room is an ordinary external referrer. Rationale in Design notes.

**New open questions surfaced by the planner** *(one genuinely load-bearing for the spec gate)*:

5. ~~**Create-write path + create-time uniqueness enforcement.**~~ **Resolved (spec gate):** a free implementer choice — every candidate shape stays inside the domain-tier catalog, publishes nothing (INV-5), touches no persistence (INV-22/23), and keeps the Blazor page thin (INV-8), so no INV is load-bearing on it (SR-5 does not fire). Take the planner's recommendation: a dedicated `CreateAsync(definition)` guard (`ValidateBlueprintId` + `Resolves` uniqueness refusal + delegate to the existing write); all creates route through it, edits stay on `SaveAsync` (overwrite-on-edit, contract unchanged). The Test plan is already written against this guard (`CreateWithId_RefusesTakenId` / `_RefusesMalformedId`).
6. **Shared editor id/rename component (non-blocking).** The id-field-on-create + rename-confirm affordance repeats across four editor pages + the grid detail panel (≥3×). The planner recommends extracting a shared `BlueprintIdField`/`RenameAction` razor component (mirroring the existing shared `RoomBasicsFields`/`RoomExitsEditor`) rather than hand-rolling it five times (INV-19). Confirm the component boundary in WP2. Surfaced as a Gap-exposed cross-cutting finding, not a design blocker.

## Proposed backlog dispositions

*Both landed in [`backlog.md`](../roadmap/backlog.md) at the spec gate.*

- ✅ **🔵 Live-world / persistent blueprint-id rename** (the deferred alternative to decision 1) — a telnet `rename` command and/or SQLite `LocationComponent.RoomBlueprintId` rewrite + live `TemplateRegistry` re-keying, so a rename can reach the running world without a `reload` and without orphaning parked players. Crosses INV-22/23 and needs its own persistence/concurrency design (relates to the world-state threading-model decision). *Added.*
- ✅ **Extended the existing *Atomic multi-file content cascade* item** to name rename alongside delete (same best-effort posture). *Added.*

## Implementation plan — work packages

Two packages; **WP2 depends on WP1**. The primary agent runs `architecture-reviewer` (code mode) across the combined diff once both land.

### WP1 — Catalog rename + validation + two-kind edge (Core, testable in isolation)

**Scope.** All non-UI behavior. Independently executable and fully covered by system-unit tests against a temp content directory (the existing `ContentDefinitionCatalogTests` / `ContentReferenceIndexTests` fixture pattern — real writers + serializer, no mocks).

**Files.**
- `Core/Modules/Authoring/Systems/IContentReferenceIndex.cs` + `ContentReferenceIndex.cs` — generalize `EdgeDeclaration.TargetKind` → `TargetKinds` (`IReadOnlyList<ContentKind>`); update `Referrers` (contains-match) and `SweepBroken`/`BrokenFor` (broken iff resolves against none); add the `(Room, SpawnRules[].BlueprintId) → {Mob, Item}` declared edge (extractor yields `("SpawnRules[{id}]", id)` per rule). Add `using Hedron.Core.ECS.Components;` for `SpawnRule`.
- `Core/Modules/World/Systems/IContentValidator.cs` + `ContentValidator.cs` — add `ValidateBlueprintId(ContentKind kind, string blueprintId)` returning a `ValidationReport` (format errors; kind-prefix mismatch as a warning).
- `Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs` + `ContentDefinitionCatalog.cs` — add `RenameAsync(kind, oldId, newId, ct)`; generalize `TryCascadeClearAsync` → `TryApplyCascadeAsync(referrer, oldId, string? newId, ct)` (newId null = clear/remove; non-null = rewrite), extended with the `Room.SpawnRules` case; add the `CreateNew(kind, name, string? blueprintId = null)` overload and the `CreateAsync(definition, ct)` create-guard; wire the `WorldOptions.StartingRoomBlueprintId` advisory into rename warnings. `DeleteAsync` now delegates to `TryApplyCascadeAsync(..., newId: null, ...)`.
- `Core/Modules/Authoring/ContentRenameResult.cs` — **new** record (see Content tooling impact).
- `Core/Modules/Authoring/ContentReference.cs` — optionally align the doc-only `ReferenceEdge.TargetKind` (non-load-bearing).
- Tests: `Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs`, `ContentReferenceIndexTests.cs` (extend), and `Hedron.Tests/World/ContentValidatorTests.cs` (id-format; create if absent).

**Out of scope.** Any Blazor / `Hedron.Web` change; any telnet command; any SQLite/config rewrite; any live-world path.

**Exit criterion.** `dotnet test` green with the new rename-cascade, two-kind-edge, id-format, uniqueness/collision, warn-not-rewrite, and create-with-id tests passing (see Test plan).

### WP2 — Editor surface (Hedron.Web, thin)

**Scope.** The editable-id-on-create field and the rename affordance across the four editor pages and the grid detail panel, all delegating to WP1's catalog verbs (INV-8). Extract a shared razor component so the affordance is not hand-rolled five times.

**Files.**
- `Hedron.Web/Components/Pages/AreaEditor.razor`, `RoomEditor.razor`, `ItemEditor.razor`, `MobEditor.razor` — editable id input on the New form (currently `<p class="mono blueprint-id">` read-only); a **Rename** action mirroring the existing delete-confirm affordance; route creates through `CreateAsync`, rename through `RenameAsync`; render `ContentRenameResult.Warnings`/`Errors` in the existing warning/error boxes; on rename success navigate to the new id's edit route.
- The grid editor's detail panel (`AreaGridEditor.razor` — world-editor-grid) — same rename affordance for the selected room.
- **New** shared component `Hedron.Web/Components/Shared/BlueprintIdField.razor` (+ a rename-confirm control, or a single `BlueprintIdEditor`) mirroring the shared `RoomBasicsFields`/`RoomExitsEditor` precedent.

**Out of scope.** Any Core behavior change (all in WP1); presentation polish/CSS beyond wiring.

**Exit criterion.** Build green; manual smoke in the loopback editor (create with a chosen id; rename a room referenced by an exit + a spawn rule; observe cascade summary). UI logic stays thin — no authoring logic in a component (INV-8, guarded by the code-mode review).

## Content tooling impact

*Required (INV-18).*

- **New authoring verb:** `RenameAsync(ContentKind, oldId, newId)` on `IContentDefinitionCatalog` — the offline editor's rename surface. Returns `ContentRenameResult`; publishes nothing.
- **New authoring path:** create-with-id (`CreateNew(kind, name, id?)` + `CreateAsync`) — the author chooses a deliberate blueprint id at creation instead of the generated `*.adhoc.*` id.
- **New result DTO:** `ContentRenameResult(string OldPath, string NewPath, string OldBlueprintId, string NewBlueprintId, IReadOnlyList<ReferrerEdit> CascadeEdits, IReadOnlyList<string> Warnings, bool Success, IReadOnlyList<string> Errors)` with `Ok`/`Failed` factories — modeled on `ContentDeleteResult` (cascade edits) fused with `ContentWriteResult` (success/errors/warnings, because rename can be refused).
- **No new YAML file shape:** `Room.SpawnRules` already round-trips losslessly through `RoomContentWriter`/`RoomTemplateDeserializer`; the slice only *declares the reference edge* over the existing field. No new template field, no schema-version bump.
- **No new `TemplateRegistry` entries and no live spawn:** YAML-only; the live world adopts renamed ids on the next `reload` (existing Flow 5). The `generate` run-mode is untouched.
- **Inspect path:** the existing integrity page (`SweepBroken`) now also covers spawn-rule references — a designer can see a dangling `Room.SpawnRules[]` id where before it was invisible.

## Cross-cutting surfaces stressed

*Required (INV-19). Classification: **Adequate** / **Gap exposed** / **Acknowledged debt**.*

- **Content templates — cascade generalization (clear/rewrite):** **Gap exposed → framework lands in this slice.** `TryCascadeClearAsync` hard-codes clear-to-empty; rename needs rewrite-to-newId. Resolved by generalizing to `TryApplyCascadeAsync(referrer, oldId, newId?)` — one shared apply serving delete-clear, rename-rewrite, and the in-memory self-reference rewrite (≥3 consumers). Meets INV-19 parity in-slice; no debt.
- **Content templates — declared-edge model (two-kind target):** **Gap exposed → framework lands in this slice.** `EdgeDeclaration.TargetKind` is single-valued and cannot express `SpawnRules[] → Mob/Item`. Resolved by generalizing to `TargetKinds` with resolve-against-either semantics (OQ1). Keeps the "add an edge = one-line data change" property; all consumers (referrer lookup, delete-cascade, integrity sweep, save-warn) pick it up automatically.
- **Editor create surface (id field + rename action):** **Gap exposed → shared component + create-guard land in this slice.** (a) The id-field/rename affordance repeats across four pages + the grid panel (≥3×) — extract a shared razor component (WP2, OQ6). (b) There is no create-time uniqueness guard today (adhoc ids never collide); a deliberate id needs one — add `CreateAsync` (WP1, OQ5). Both land in-slice; neither is deferred debt.
- **Persistence / SQLite:** **Adequate (boundary preserved).** Rename never opens SQLite. `LocationComponent.RoomBlueprintId` (the only `[Persistent]` blueprint-id reference) is **warned, not rewritten** — the deliberate INV-22/23 scope (decision 1). The reach-into-persistence alternative is the deferred backlog item.
- **Configuration:** **Adequate.** `WorldOptions.StartingRoomBlueprintId` is read-only-compared to emit a specific advisory when the starting room is renamed; `appsettings.json` is never written.
- **Event bus / broadcast / output / sessions / commands / time / ECS queries / modules:** **Adequate / N/A.** Offline catalog operation — no bus (INV-5), no telnet command (decision 4), no output, no session, no heartbeat, no clock/RNG (INV-26 moot), no ECS query, no new module. Registration is unchanged (`AuthoringModule`, `WorldModule`).
- **Acknowledged debt (one):** the cascade stays **best-effort / non-transactional** — a mid-rename crash can leave a partially-rewritten set, recoverable via `SweepBroken`. Same accepted posture as `DeleteAsync`; folds into the existing [*Atomic multi-file content cascade*](../roadmap/backlog.md) backlog item, extended to name rename. Rationale: transactional multi-file writes are a cross-cutting framework of their own, out of proportion to this slice.

### Persistence opt-in audit

*Mandatory sub-check (INV-22/23).*

- **Level 1 — entity domain classification:** the slice introduces **no entity construction path**. It reads/writes YAML template POCOs only; no `EntityService.CreateEntity`, no `AddComponent<PersistentEntity>`. World-content entities re-key on `reload` (world-content domain — never carry `PersistentEntity`). Clean.
- **Level 2 — component inclusion:** the slice **touches no components**. `SpawnConfigComponent` (the runtime form of `Room.SpawnRules`) is correctly **not** `[Persistent]` — world content, re-applied from YAML on each spawn ("Not persisted — the YAML template is the authoritative source"); unchanged. `LocationComponent.RoomBlueprintId` is `[Persistent]` and correct (player/item durable state); the slice does **not** touch it — it is the warned-not-rewritten reference. No `[Persistent]`-status gap.
- **Level 3 — save-on-change scope:** **no `SaveEntityAsync` anywhere.** Offline catalog operation; no admin boundary save, no session-end save. Clean.

## Flows introduced or modified

*Required (INV-17).*

- **[Flow 29 — Content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md), leg B (offline Blazor editor):** gains the **rename leg** and the **create-with-id id field**. The B diagram/steps add: `UI → Cat: RenameAsync(kind, oldId, newId)` → `Cat → Idx: Referrers(kind, oldId)` (now over the two-kind spawn-rule edge) → per-referrer writer rewrite-to-newId → new-file write + old-file delete → `ContentRenameResult` (with the out-of-YAML warning). Step 2 (`CreateNew`) notes the optional deliberate-id path via `CreateAsync`. The INV-19 note updates the declared-edge count to include `(Room, SpawnRules[]) → Mob/Item` and its resolve-against-either two-kind semantics. **No new flow file** — it is a leg on the existing journey.
- The **Apply-to-live** leg ([Flow 5](../architecture/flows/flow-05-content-reload.md)) is unchanged and reused (the reload is where renamed ids reach the live world).

## Test plan / Verification

*Required (INV-25). Derived from Postconditions + Main flow via [`07-testing.md`](../architecture/07-testing.md). All tiers are **system-unit** against a temp content directory with the real writers/serializer/validator (the existing `ContentDefinitionCatalogTests` / `ContentReferenceIndexTests` fixture) — offline, no bus, no live world, so no handler/flow/persistence-round-trip tier applies. INV-26 moot (no RNG/clock → no injected seam needed → no testability gap).*

**`ContentReferenceIndex` — two-kind spawn-rule edge (system-unit):**
- `Referrers_MatchesRoom_ViaSpawnRule_MobTarget` — room with `SpawnRules[X]`, `Referrers(Mob, X)` returns the room with label `SpawnRules[X]`. *(Postcondition: spawn-rule edge referrers.)*
- `Referrers_MatchesRoom_ViaSpawnRule_ItemTarget` — same for an item id via `Referrers(Item, Y)`.
- `SweepBroken_DoesNotFlag_SpawnRule_ResolvingAsItem_WhenSearchedAsMob` — a spawn-rule id present as an item file is **not** flagged broken (resolve-against-either). *(Postcondition: two-kind resolution, false-positive guard.)*
- `SweepBroken_FlagsSpawnRule_ResolvingAsNeither` — a spawn-rule id with no mob and no item file **is** flagged. *(Postcondition: two-kind resolution.)*
- `BrokenFor_Room_FlagsDanglingSpawnRule` — in-memory room with a spawn rule resolving as neither kind.
- Regression: the existing five-edge `Referrers`/`SweepBroken`/`BrokenFor` tests stay green under the `TargetKinds` migration.

**`ContentDefinitionCatalog` — rename cascade (system-unit):**
- `Rename_WritesNewFile_DeletesOld` — `newId` file present with copied state; `oldId` file gone. *(Postcondition: own file.)*
- `Rename_RewritesReferrer_AreaId` / `_ExitDirection` / `_ItemSpawnRoom` / `_MobSpawnRoom` / `_AreaRoomsList` / `_RoomSpawnRule` — one per declared edge, asserting the referring field now equals `newId` and the cascade edit is enumerated in the result. *(Postcondition: cascade — six edges.)*
- `Rename_RewritesOwnSelfLoopExit` — a room whose `Exits[dir] == oldId` yields a new file with `Exits[dir] == newId`; no stale-file load. *(Postcondition: self-reference.)*
- `Rename_RefusesCollision_NoWrite_OldFileIntact` — `newId` already a same-kind file → `Failed`, old file present, no new file. *(Postcondition: collision refusal.)*
- `Rename_RefusesMalformedId_NoWrite` — `newId` with a path separator / `..` / illegal char → `Failed`, no write. *(Postcondition: validation refusal.)*
- `Rename_MissingTarget_Fails` — no `oldId` file → `Failed`.
- `Rename_KindPrefixMismatch_Warns_NotRefuses` — room renamed to a prefix-less id succeeds with a warning. *(Postcondition: soft prefix.)*
- `Rename_StartingRoom_EmitsAdvisoryWarning_NoConfigOrSqliteWrite` — renaming the room equal to `WorldOptions.StartingRoomBlueprintId` returns the advisory warning; assert (structurally) no persistence/config dependency is invoked. *(Postcondition: out-of-YAML warn-not-rewrite.)*
- `Rename_BestEffort_ContinuesPastReferrerFailure` — a referrer that cannot be loaded/written does not throw; the result still returns and other referrers are applied. *(Postcondition: best-effort parity.)*
- `Rename_TouchesNoEntityService_NoSqlite` — analog of the existing `Delete_TouchesNoEntityService_NoSqlite` guard: the ecs from the fixture is unchanged; the ctor takes no `EntityService`/persistence dep. *(Postcondition: boundary invariants — architecture-guard flavor.)*

**`ContentDefinitionCatalog` — delete parity + create-with-id (system-unit):**
- `Delete_RemovesRoomSpawnRule_ReferencingDeletedMob` — deleting a mob removes matching `Room.SpawnRules` entries (edge fix applies to delete uniformly). *(Postcondition: delete parity.)*
- `Delete_RemovesRoomSpawnRule_ReferencingDeletedItem` — same for an item.
- `CreateWithId_UsesDeliberateId_RoundTrips` — `CreateNew(kind, name, id)` → create write → file present under the chosen id. *(Postcondition: create-with-id.)*
- `CreateWithId_RefusesTakenId` / `_RefusesMalformedId` — the create-guard refuses collision / bad format, no overwrite. *(Postcondition: create-with-id validation.)*

**`ContentValidator` — id format (system-unit):**
- `ValidateBlueprintId_AcceptsFilenameSafe` / `_RejectsPathSeparators` / `_RejectsDotDot` / `_RejectsEmpty` / `_WarnsOnKindPrefixMismatch`. *(Postcondition: validation rules; the OQ2 charset.)*

**Skipped (with reason):**
- Blazor presentation / exact warning prose / navigation (WP2) — thin surface (INV-8); the catalog verbs carry all logic and are tested above. No component-render test tier in this repo.
- The `ReloadAsync` apply-to-live leg — unchanged existing Flow 5; re-keying on reload is covered by existing world-load tests.
- `ContentRenameResult` as pure data — no behavior; asserted indirectly via the catalog tests that read its fields.
- Exact YAML byte output of the new/rewritten files — round-trip (write→`Load`) is the contract, per the existing catalog test style.

## Related

- [`content-tooling.md`](../features/admin-authoring/content-tooling.md) — durable home for `IContentDefinitionCatalog` / `IContentReferenceIndex` / `IContentValidator` (gains the rename verb + the two-kind edge on ship).
- [`content-authoring.md`](../features/admin-authoring/content-authoring.md) — the Blazor editor surface that calls the catalog (gains the id field + rename action).
- [`admin-authoring.md`](../features/admin-authoring/admin-authoring.md) — the holistic authoring feature.
- [Flow 29 — content-tooling journey](../architecture/flows/flow-29-bulk-content-generation.md) (leg B, the rename leg) · [Flow 5 — content reload](../architecture/flows/flow-05-content-reload.md) (the reused apply-to-live leg).
- [`reference/systems.md`](../reference/systems.md) — `IContentDefinitionCatalog` / `IContentReferenceIndex` / `IContentValidator` rows (INV-16/29 diffs land on ship).
- [`content-editor-integrity.md`](../roadmap/completed/content-editor-integrity.md) — as-built record for the `DeleteAsync` cascade, `IContentReferenceIndex`, and warn-but-allow save this slice generalizes.
- [`persistence-reform.md`](persistence-reform.md) · [`06-persistence.md`](../architecture/06-persistence.md) — the INV-22/23 two-domain model that bounds the YAML-only scope.
- [`admin-area-authoring.md`](admin-area-authoring.md) — sibling in-flight authoring plan (`mkarea` / `listents`).
