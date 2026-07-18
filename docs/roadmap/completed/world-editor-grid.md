# Visual grid area editor (completed)

> Implemented on branch `claude/world-editor-grid-impl-u9hw7q`, 2026-07-18. Living docs: [`admin-authoring`](../../features/admin-authoring/admin-authoring.md), [`content-tooling`](../../features/admin-authoring/content-tooling.md), [`content-authoring`](../../features/admin-authoring/content-authoring.md).

## Outcome

The offline Blazor content editor gained a visual grid view of an area's rooms: `/area/{id}/grid` renders one Z-layer at a time, with legacy (coordless) rooms showing as a deterministic ghost-cell layout proposal until the designer applies it. Every grid action (create, connect, disconnect, edit, delete) composes the same `IContentDefinitionCatalog` verbs the existing `RoomEditor` already used, plus one earned addition — `RemoveRoomExitAsync`, the bidirectional-disconnect mirror of the existing bidirectional-connect policy. Room coordinates (`X`/`Y`/`Z`, optional `int`) landed on `RoomTemplate`/YAML as the authoring-side half of the backlogged runtime coordinate system; no runtime component or exit↔coordinate enforcement was built this slice. A pre-existing writer-fidelity bug (`RoomContentWriter` silently dropping `spawnRules` and `schemaVersion` on every re-save) was found and fixed in the same PR, since the grid's neighbor-rewriting bidirectional saves would have amplified it.

## Behavior digest

**Preconditions:** `Hedron.Web` host running (loopback); the target area exists on disk; its rooms may carry all, some, or no `X/Y/Z` coordinates.

**Postconditions (as specified):**
1. Room YAML round-trips `X/Y/Z` losslessly; a legacy file without the fields loads with `null`s and no warning.
2. Room YAML round-trips `spawnRules` and `schemaVersion` losslessly (writer-fidelity regression fix).
3. `RoomTemplate.Apply` attaches no coordinate data to any runtime component.
4. Create-at-cell writes exactly one new room file carrying the grid's `AreaId` and the clicked cell's coordinates.
5. Connect writes the source exit and, per the existing bidirectional policy, the inverse exit on the target (conflict → warn-and-skip).
6. `RemoveRoomExitAsync(bidirectional: true)` removes the source exit and the target's inverse exit only when it still points back at the source; an absent exit is a no-op success.
7. `IAreaLayoutSystem.Propose` is deterministic, never moves an anchored room, yields collision-free proposed cells, and places Up/Down exits at Z±1.
8. `ApplyProposalAsync` re-derives from disk and writes coordinates only for previously-coordless rooms, best-effort per room.
9. Two rooms in the same area at the same non-null `X/Y/Z` produce a registry-validation **warning**, never an error; `IsValid` stays `true` and boot does not abort.
10. `DirectionExtensions.Offset`/`FromOffset` are total, mutually inverse for unit offsets, and `Offset(d.Opposite()) == -Offset(d)`.
11. No event is published anywhere in the slice; the live world and SQLite are untouched.

**Main flow:** designer opens the grid page → page loads the area's rooms and calls `IAreaLayoutSystem.Propose` → grid renders the current Z-layer (solid/ghost cells, edge tabs for adjacent exits, badges for vertical/non-adjacent/cross-area exits, flagged cells for coordinate collisions) → click-driven create/connect/disconnect/edit/delete, each an immediate per-action catalog write → optional "Apply layout" bulk-anchors every ghost room → apply-to-live remains the existing, unchanged reload leg.

## Shipped pieces

| Surface | Location |
|---|---|
| `RoomTemplate.X/Y/Z` (`int?`) + `SchemaVersion` (`int?`) | `Core/Modules/World/Templates/RoomTemplate.cs` |
| `AreaTemplate.SchemaVersion` (`int?`) — same-class writer-fidelity fix | `Core/Modules/World/Templates/AreaTemplate.cs` |
| `RoomTemplateDeserializer` / `RoomContentWriter` — round-trip `x`/`y`/`z`, `spawnRules`, `schemaVersion` | `Core/Modules/World/Templates/RoomTemplateDeserializer.cs`, `Core/Modules/World/Systems/RoomContentWriter.cs` |
| `AreaTemplateDeserializer` / `AreaContentWriter` — round-trip `schemaVersion` | `Core/Modules/World/Templates/AreaTemplateDeserializer.cs`, `Core/Modules/World/Systems/AreaContentWriter.cs` |
| `DirectionExtensions.Offset` / `FromOffset` | `Core/Direction.cs` |
| `RoomCoordinateCollisions` (pure static helper) + `CoordinateCollision` record | `Core/Modules/World/Systems/RoomCoordinateCollisions.cs` |
| `ValidationReport.Warnings` channel | `Core/Modules/World/Systems/ValidationReport.cs` |
| `ContentValidator` coordinate-collision warning rule (registry mode, sourced from `ITemplateRegistry`) | `Core/Modules/World/Systems/ContentValidator.cs` |
| `RegistryValidationBootstrap` — logs warnings without aborting boot | `Server/RegistryValidationBootstrap.cs` |
| `IContentDefinitionCatalog.RemoveRoomExitAsync` | `Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs`, `ContentDefinitionCatalog.cs` |
| `IAreaLayoutSystem` / `AreaLayoutSystem` (`Propose`, `ApplyProposalAsync`) | `Core/Modules/Authoring/Systems/IAreaLayoutSystem.cs`, `AreaLayoutSystem.cs` |
| `RoomBasicsFields.razor` / `RoomExitsEditor.razor` (extracted shared components) | `Hedron.Web/Components/Shared/` |
| `AreaGridEditor.razor` (`/area/{id}/grid`) | `Hedron.Web/Components/Pages/AreaGridEditor.razor` |
| `RoomEditor.razor` — refit to consume the shared components (behavior-preserving) | `Hedron.Web/Components/Pages/RoomEditor.razor` |
| Grid links from `AreaEditor` and the browser's area rows | `AreaEditor.razor`, `Browser.razor` |
| Grid CSS | `Hedron.Web/wwwroot/app.css` (appended — see Deviations) |

## Tests shipped

- `Hedron.Tests/World/RoomCoordinateRoundTripTests.cs` (new) — X/Y/Z + spawnRules + schemaVersion round-trip, legacy-file nulls, `Apply` attaches no coordinate component (→ postconditions 1–3).
- `Hedron.Tests/DirectionExtensionsTests.cs` (new) — `Offset` totality, `Offset(d.Opposite()) == -Offset(d)`, `FromOffset` inverse for unit offsets / null otherwise (→ postcondition 10).
- `Hedron.Tests/World/ContentValidatorTests.cs` (additions) — same-area/same-cell warning, different-area / different-Z no-warning, warnings-only report stays valid (→ postcondition 9).
- `Hedron.Tests/Authoring/ContentDefinitionCatalogTests.cs` (additions) — `RemoveRoomExitAsync` policy matrix: removes + writes source, bidirectional removes reciprocal only when it points back, leaves a foreign inverse untouched, absent-exit no-op, unknown-room failure (→ postcondition 6).
- `Hedron.Tests/Authoring/AreaLayoutSystemTests.cs` (new) — determinism, anchors never move, collision-free placement (vs. anchors and each other), adjacent-offset placement, occupied-cell spill, disconnected-component origins, Up/Down → Z±1, empty/all-anchored → empty proposal, collisions reported, `ApplyProposalAsync` writes-only-coordless / re-derives-from-disk / best-effort warnings (→ postconditions 7–8).
- Five pre-existing `ContentValidator` construction call sites updated for the new `ITemplateRegistry` constructor parameter (`ContentValidatorTests`, `ConformanceRoundTripTests`, `ContentDefinitionCatalogTests`, `ContentReferenceIndexTests`, `RegistryValidationTests`).
- `dotnet test` green: 1265 tests (was 1212 before this slice).
- `AreaGridEditor.razor`, the two shared components, and the `RoomEditor` refit are presentation skip-tier per the rubric — every decision they invoke is covered at the system-unit tier above. Verified instead by an end-to-end manual pass against a scratch content directory (Playwright): legacy ghost-cell rendering, Apply layout, create, connect, disconnect (edge tab, disk round-trip verified), coordinate-collision flagging (grid cell **and** the boot-time registry-validation warning log), delete, and `RoomEditor` confirmed still working standalone after the shared-component refit.

## Decisions

- **Coordinates live on `RoomTemplate` YAML, not a sidecar or derive-on-open.** The grid's layout data *is* the authored half of the backlogged runtime coordinate system — when a future `CoordinateComponent` slice lands it reads the same field (a refactor, not a migration). A sidecar was rejected as a second coordinate home that would drift; derive-on-open was rejected as unstable across sessions and lossy on non-planar graphs.
- **Coordinate convention:** East = X+1, North = Y+1, Up = Z+1, encoded once in `DirectionExtensions.Offset`. The grid renders +Y toward the top of the screen.
- **Coordinates are advisory, not an exit constraint.** An exit may target a non-adjacent cell, another Z-layer, or another area; the grid renders adjacency-consistent exits as edge tabs and everything else as badge chips. The only consistency surface added is the warn-not-error coordinate-collision rule.
- **`ValidationReport` needed a `Warnings` channel before the collision rule could exist.** The report previously carried only `Errors`, and `RegistryValidationBootstrap` aborted boot on any non-empty report — warn-not-error was inexpressible. `Warnings` was added with `IsValid` staying errors-only; the bootstrap now logs warnings without throwing.
- **Grid mutations are compositions of existing catalog verbs — no parallel authoring path.** Create/connect/edit/delete all reuse `CreateNew`/`SaveRoomAsync`/`SaveAsync`/`DeleteAsync` exactly as `RoomEditor` does. Two additions earned their place: `RemoveRoomExitAsync` (bidirectional-disconnect symmetry — `RoomEditor` already left a dangling inverse exit when a designer cleared one side, a pre-existing bug the new verb also closes for both surfaces going forward) and `IAreaLayoutSystem.ApplyProposalAsync` (the bulk coordinate write follows the conformance-fitter precedent: a system under `Core/`, not component logic).
- **Auto-layout is a domain-tier system; write-back is one explicit bulk action.** `IAreaLayoutSystem.Propose` is deterministic BFS over the exit graph (anchored rooms fixed, directions in enum order, rooms in ordinal blueprint-id order, occupied-cell spill via a Chebyshev ring scan). Persisting proposed positions is one explicit **Apply layout** click (best-effort per room), not per-room-as-touched — a half-persisted layout that silently shifts between sessions as exits change would make the map appear to jump. An individual ghost room's own save (edit, connect) still naturally persists its proposed coordinates as part of that write.
- **Writer fidelity was a precondition for a write-heavy editor.** `RoomContentWriter` dropped `spawnRules` and `schemaVersion` on every write (a pre-existing bug); the grid's bidirectional saves touch neighbor rooms far more often than the single-room `RoomEditor`, so the loss was fixed in-slice with regression tests, and the sibling writers were audited — `AreaContentWriter` had the identical `schemaVersion` loss and was fixed the same way; `Item`/`MobContentWriter` were already lossless.
- **Blazor-only v1 — no JS asset.** The interaction set (click-select, click-create, click-connect, layer switch) needs no pointer capture; every mutation round-trips through the Blazor component into the catalog, matching the 08-blazor thin-component discipline.
- **Immediate per-action writes; no draft/transaction layer.** Each grid action is one atomic-per-file catalog call with the established warn-but-allow posture — a draft working set would require the transactional multi-file cascade already deferred in `backlog.md`.
- **Single-area scope; multi-area world view deferred.** Per-area local coordinate spaces compose with a future per-area origin offset (overworld design) without foreclosing it.

## Deviations / Follow-ups

- **Grid CSS lives in the global `app.css`, not a scoped `.razor.css` file.** The implementation initially used Blazor CSS isolation (`AreaGridEditor.razor.css`); manual verification found the SDK-generated `Hedron.Web.styles.css` bundle is never served under this host's actual run configuration — no `launchSettings.json` exists in the repo, so `dotnet run` defaults to the `Production` environment, and the CSS-isolation bundle (built into `obj/`) is only wired up automatically in `Development`. Rather than depend on an environment posture the repo doesn't otherwise establish, the grid's rules were folded into the existing global stylesheet, matching every other editor page's convention. Not a plan deviation in behavior — only in *where* the CSS lives.
- **Stacked-cell (coordinate-collision) UX shows only the primary occupant's name/id** in the cell body, with a `⚠ N rooms here` badge naming the collision; the other room(s) at that cell are edited by first resolving the collision (e.g. via `RoomEditor` directly) rather than a dedicated stacked-cell picker. Acceptable for v1 per the plan's "flagged, not resolved" posture; a picker is a natural follow-up if collisions prove common in practice.
- **Runtime `CoordinateComponent`, exit↔coordinate enforcement, and the multi-area world view** remain in the existing backlog item, unchanged by this slice — see [`../backlog.md`](../backlog.md).
- No deviations from the plan's postconditions, Main flow, or Work Package scope.
