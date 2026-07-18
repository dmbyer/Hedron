# Visual grid area editor (world-editor-grid)

- **Status:** planned
- **Actors:** Administrator (content designer, via the Blazor authoring host)
- **Module:** `Core/Modules/Authoring/` (layout system + catalog extension) · `Core/Modules/World/` (RoomTemplate coordinate fields, writer fidelity, collision rule) · `Hedron.Web/` (grid page + shared editor components). Feature home: [`docs/features/admin-authoring/`](../features/admin-authoring/) (content-tooling tier).

## Description

A visual grid editor inside the area editor: the designer sees an area's rooms laid out on a 2-D grid (one Z-layer at a time, with a layer switcher), creates a room by clicking an empty cell, connects/disconnects exits between adjacent cells, deletes rooms, and edits a selected room's name, description, and full exit map in a detail panel. The exits editor and name/description fields are extracted into shared Razor components used by both the existing `RoomEditor` page and the grid's detail panel. Grid position is persisted as optional `X/Y/Z` fields on `RoomTemplate` YAML — the authoring-side half of the backlogged coordinate system — and every mutation is an immediate per-action write through `IContentDefinitionCatalog`. Legacy rooms without coordinates get a deterministic BFS auto-layout proposal (`IAreaLayoutSystem`), rendered as ghost cells and persisted via an explicit **Apply layout** action. Scope is a **single area at a time**; the multi-area world view is deferred. v1 is **Blazor-only** (no JS asset).

## Design notes

*(durable rationale — survives into `roadmap/completed/` on ship)*

- **Coordinates live on `RoomTemplate` (YAML), not in an editor sidecar or derived per-open.** The grid's layout data *is* the authored half of the backlogged coordinate system ([`backlog.md` → Locale enhancements](../roadmap/backlog.md)): optional `X/Y/Z` (nullable ints) on the room template. When the runtime `CoordinateComponent` slice lands it reads the same field — a refactor, not a migration. A sidecar layout file was rejected as a second coordinate home that would drift; a derive-on-open layout was rejected as unstable across sessions and lossy on non-planar graphs. Disposition: **shaped for later** — the YAML field lands now; the runtime component, cardinal-distance queries, and exit↔coordinate consistency *enforcement* stay deferred with the existing backlog item.
- **Coordinate convention: East = X+1, North = Y+1, Up = Z+1.** Declared once here and encoded once in `DirectionExtensions.Offset`; the grid renders +Y toward the top of the screen. The future runtime coordinate slice inherits this convention.
- **Coordinates are advisory authoring layout, not a constraint on exits.** An exit may target a non-adjacent cell, another Z-layer, or another area; the grid renders adjacency-friendly exits as edges and everything else as badges/markers, and the detail panel's exits component remains the full-fidelity editor. Enforcing "east means X+1" is a game-rule decision that belongs to the future runtime coordinate slice, not the editor. The only consistency surface this slice adds is a **warn-not-error** coordinate-collision rule (two rooms in one area on the same cell), detected by one shared pure helper consumed by both the registry-validation sweep and the grid's rendering. Warn-not-error has a mechanical consequence this slice must land: `ValidationReport` today carries only `Errors` (and `RegistryValidationBootstrap` aborts boot on any non-empty report), so WP1 adds a `Warnings` channel to `ValidationReport` (`IsValid` unchanged — errors only) and updates the bootstrap to log warnings without throwing; the validator's registry mode sources room templates from `ITemplateRegistry` (a clean World→Core dependency).
- **Grid mutations are compositions of existing catalog verbs — no parallel authoring path.** Create-at-cell = `CreateNew` + set `AreaId`/coords + `SaveRoomAsync`; connect = set exit + `SaveRoomAsync(bidirectional: true)` (reusing its inverse-exit conflict policy); delete = `DeleteAsync` (existing cascade-clear of referrers). Two additions earned their place: (a) `RemoveRoomExitAsync(roomId, direction, bidirectional)` on the catalog — bidirectional *disconnect* is the mirror of the existing bidirectional *connect* policy, and today's `RoomEditor` already leaves a dangling inverse exit when a designer clears one side, so the verb closes a symmetry gap for both surfaces (INV-19); (b) `IAreaLayoutSystem.ApplyProposalAsync` — the bulk coordinate write follows the conformance-fitter precedent (bulk loops live in a system under `Core/`, re-deriving from disk, not in a component).
- **Auto-layout is a domain-tier system in `Core/Modules/Authoring/`, and write-back is an explicit bulk action.** The one genuinely new piece of *logic* is auto-layout for legacy rooms without coordinates: deterministic BFS over exits (anchored rooms fixed, directions in enum order, rooms in ordinal blueprint-id order, occupied cells resolved by nearest-free-cell spill on the same Z) — real computation, landing as `IAreaLayoutSystem` (domain tier: it depends on the domain-tier catalog; INV-2 unaffected), testable (INV-25), never in the component. Proposed positions render as ghost cells; persisting them is one explicit **Apply layout** click (best-effort per-room loop, mirroring the delete-cascade posture), not per-room-as-touched — a half-persisted layout that silently shifts between sessions as exits change would make the map appear to jump. Any *individual* save of a ghost room (edit, connect) naturally persists that one room's proposed coordinates as part of its own write — the proposal is already on the in-memory template.
- **Writer fidelity is a precondition for a write-heavy editor.** `RoomContentWriter`'s DTO omitted `spawnRules` **and `schemaVersion`** — the deserializer reads both, the writer dropped both, so any editor re-save of a room with authored spawn rules silently stripped them (and stripped a declared `schemaVersion`). The grid multiplies rewrites (bidirectional saves touch neighbor rooms), so both pre-existing round-trip losses are fixed in this slice with regression round-trip tests, and the other three writers are audited for the same class of loss.
- **JS/HTML is presentation-only — the 08-blazor "thin component" discipline extends one tier down.** v1 is Blazor-only: the interaction set (click-select, click-create, click-connect, layer switch) needs no pointer capture. If a later polish pass adds drag-connect via a JS asset, the JS renders and captures input only; every mutation still round-trips through the Blazor component into the catalog — [`08-blazor.md`](../architecture/08-blazor.md) gains its JS-presentation-only section **when** that asset lands, not now (INV-20/INV-27: cite, don't restate).
- **Immediate per-action writes; no draft/transaction layer.** Each grid action is one atomic-per-file catalog call with the established warn-but-allow integrity posture. A draft working set would require the transactional multi-file cascade already deferred in [`backlog.md`](../roadmap/backlog.md) ("Atomic multi-file content cascade") — not rebuilt here.
- **Shared editor components (`Components/Shared/`)** — the exits editor (`RoomExitsEditor`) and name/description fields (`RoomBasicsFields`) extract into reusable Razor components consumed by both `RoomEditor` and the grid detail panel (precedent: `CombatantSideEditor`). Presentation tier, skip-tier for tests; the logic they call is already covered.

## Preconditions

- `Hedron.Web` host running (content bootstrap hosted services only; loopback bind).
- The target area definition exists on disk; its rooms may carry all, some, or no `X/Y/Z` coordinates.
- Content directory writable (same posture as every existing editor page).

## Postconditions

*(coverage contract — each player-invisible assertion maps to a named test in the Test plan)*

1. Room YAML round-trips `X/Y/Z` losslessly: values survive write→read; `null` coordinates are omitted from the file and read back as `null`; a legacy file without the fields loads with `null`s and no warning.
2. Room YAML round-trips `spawnRules` and `schemaVersion` losslessly (writer-fidelity fix — regression-locked).
3. `RoomTemplate.Apply` attaches **no** coordinate data to any runtime component (authoring-only field this slice).
4. Create-at-cell writes exactly one new room file carrying the grid's `AreaId` and the clicked cell's coordinates; no other file is touched.
5. Connect writes the source exit and, per the existing bidirectional policy, the inverse exit on the target (conflict → warn-and-skip, unchanged).
6. `RemoveRoomExitAsync(bidirectional: true)` removes the source exit and removes the target's inverse exit **only when it points back at the source**; an inverse pointing elsewhere is left untouched; removing an absent exit is a no-op success.
7. `IAreaLayoutSystem.Propose` is deterministic (same disk state → same proposal), never moves an anchored room, and yields proposed cells that collide neither with anchors nor with each other; Up/Down exits propose Z±1; disconnected subgraphs are placed at deterministic free origins.
8. `ApplyProposalAsync` re-derives the proposal from disk and writes coordinates **only** for rooms that lacked them, best-effort with per-room warnings; already-anchored rooms are not rewritten.
9. Two rooms in the same area with identical non-null `X/Y/Z` produce a registry-validation **warning** (never an error); rooms in different areas or on different Z-layers do not. A report with warnings and no errors keeps `IsValid == true`, and `RegistryValidationBootstrap` logs warnings without aborting boot at either host.
10. `DirectionExtensions.Offset` is total over the six directions, `Offset(d.Opposite()) == -Offset(d)`, and `FromOffset` is its inverse for unit offsets (null otherwise).
11. No event is published anywhere in the slice; the live world and SQLite are untouched.

## Main flow

1. Designer opens `/area/{blueprintId}/grid` (linked from `AreaEditor` and the browser). The page loads the area's rooms (`RoomsInArea` + `Load` per room) and calls `IAreaLayoutSystem.Propose(areaBlueprintId)`.
2. The grid renders the selected Z-layer: anchored rooms as solid cells, proposal-only rooms as ghost cells, adjacent-cell exits as edges, Up/Down and non-adjacent/cross-area exits as cell badges, coordinate collisions as flagged stacks. A Z-layer switcher steps through the layers present (plus one above/below).
3. **Create:** click an empty cell → name prompt → `CreateNew(Room, name)`, set `AreaId` + cell coordinates, `SaveRoomAsync(bidirectional: false)`. File written immediately; grid refreshes.
4. **Connect:** select a room, click an orthogonally adjacent occupied cell → direction derived via `DirectionExtensions.FromOffset`, source exit set, `SaveRoomAsync(bidirectional: true)`; inverse-conflict warnings surface inline.
5. **Disconnect:** click an existing edge (or the detail panel) → `RemoveRoomExitAsync(roomId, direction, bidirectional: true)`.
6. **Detail panel:** selecting a cell shows shared `RoomBasicsFields` + `RoomExitsEditor` (full-fidelity — including non-adjacent, vertical, and cross-area exits); save = `SaveRoomAsync` with the page's bidirectional toggle, exactly like `RoomEditor`.
7. **Delete:** confirm → `DeleteAsync(Room, id)`; the existing cascade clears referrers (neighbor exits, area room list, spawn references); grid refreshes.
8. **Apply layout:** button (shown while ghosts exist) → `ApplyProposalAsync(areaBlueprintId)`; coordinates persist for all previously-coordless rooms; grid re-renders fully anchored.
9. Apply-to-live remains the existing reload leg ([Flow 5](../architecture/flows/flow-05-content-reload.md)) — unchanged, outside this page.

## Events fired

**None.** Authoring is off the bus ([`08-blazor.md`](../architecture/08-blazor.md), INV-5): the catalog, layout system, and validator return results; the Blazor page is the initiating surface and consumes them directly. The only publish in the neighborhood remains the reload Initiator's, unchanged.

## Systems / handlers involved

| Piece | Status | Role |
|---|---|---|
| `IContentDefinitionCatalog` (`ContentDefinitionCatalog`) | **extended** | Existing verbs compose all grid mutations; gains `RemoveRoomExitAsync(roomId, direction, bidirectional)` (mirror of the bidirectional-add policy). |
| `IAreaLayoutSystem` (new, `Core/Modules/Authoring/Systems/`) | **new** | `Propose(areaId)` — deterministic BFS layout proposal + collision report; `ApplyProposalAsync(areaId)` — re-derive-from-disk best-effort coordinate write via the catalog. Returns results; never publishes (INV-5); deterministic (INV-26 non-issue). |
| `RoomTemplate` + `RoomTemplateDeserializer` + `RoomContentWriter` | **extended** | Optional `X/Y/Z` (`int?`) fields, camelCase YAML `x`/`y`/`z`, omitted-when-null; writer DTO also gains `spawnRules` and `schemaVersion` (fidelity fix). `Apply` unchanged. |
| `DirectionExtensions` (`Core/Direction.cs`) | **extended** | `Offset(this Direction)` → unit `(dx, dy, dz)`; `FromOffset(dx, dy, dz)` → `Direction?`. Pure helpers. |
| `RoomCoordinateCollisions` (new static helper, `Core/Modules/World/Systems/`) | **new** | Pure grouping of room templates by `(AreaId, X, Y, Z)`; the single detection consumed by the validator rule and (via the layout proposal) the grid. |
| `IContentValidator` (`ContentValidator`) + `ValidationReport` | **extended** | Registry mode gains one warn-not-error rule: coordinate collision within an area, sourcing room templates from `ITemplateRegistry` (World→Core). `ValidationReport` gains a `Warnings` list; `IsValid` stays errors-only. |
| `RegistryValidationBootstrap` (`Server/`) | **extended** | Logs report warnings without throwing; still aborts boot on errors (both hosts). |
| `IContentReferenceIndex`, `I*ContentWriter`s, `IWorldContentLoader` | reused | Unchanged — cascade, warn-but-allow, and reload behavior as-is. |

Handlers: **none** (no bus involvement, no heartbeat, no live world).

## Implementation plan — work packages

### WP1 — Core: coordinates, round-trip fidelity, direction math, catalog disconnect verb

- **Scope:** `RoomTemplate.X/Y/Z` (`int?`, settable); `RoomTemplateDeserializer` reads `x`/`y`/`z`; `RoomContentWriter` DTO gains `X`/`Y`/`Z` (nullable, omitted when null) **and `SpawnRules` + `SchemaVersion`** (fidelity fix); quick audit of `Area/Item/MobContentWriter` for equivalent round-trip losses (fix in-scope if found, same class of bug); `DirectionExtensions.Offset`/`FromOffset`; `RoomCoordinateCollisions` helper; `ValidationReport` gains a `Warnings` list (`IsValid` unchanged — errors only); `ContentValidator` registry-mode collision warning, sourcing room templates from `ITemplateRegistry`; `RegistryValidationBootstrap` logs warnings without throwing (error behavior unchanged); `IContentDefinitionCatalog.RemoveRoomExitAsync` + implementation.
- **Files:** `Core/Modules/World/Templates/RoomTemplate.cs`, `RoomTemplateDeserializer.cs`; `Core/Modules/World/Systems/RoomContentWriter.cs`, `IContentValidator.cs`/`ContentValidator.cs`, `ValidationReport.cs`, `RoomCoordinateCollisions.cs` (new); `Server/RegistryValidationBootstrap.cs`; `Core/Direction.cs`; `Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs`, `ContentDefinitionCatalog.cs`; `.claude/skills/add-core-system/SKILL.md` (INV-20: the companion-validation paragraph learns the `Warnings` channel — errors abort boot, warnings log at both hosts, `IsValid` is errors-only); tests under `Hedron.Tests/World/` + `Hedron.Tests/Authoring/`, including **all direct `ContentValidator` construction sites** (`ContentValidatorTests`, `ConformanceRoundTripTests`, `ContentDefinitionCatalogTests`, `ContentReferenceIndexTests`, `RegistryValidationTests`) if the ctor signature changes.
- **Dependencies:** none.
- **Out of scope:** any runtime component or `Apply` change; exit↔coordinate enforcement; layout math; UI.
- **Exit criterion:** `dotnet test` green with new tests: room YAML round-trip (coords + spawnRules + schemaVersion + legacy-file nulls), offset totality/inverse, collision-warning rule (warnings-only report stays valid), `RemoveRoomExitAsync` policy matrix.

### WP2 — Core: `IAreaLayoutSystem` (depends on WP1)

- **Scope:** `IAreaLayoutSystem` + `AreaLayoutSystem` in `Core/Modules/Authoring/Systems/`: `Propose(areaBlueprintId)` loads the area's rooms via the catalog and delegates to a pure placement routine (anchors fixed; BFS seeded from anchored rooms in ordinal blueprint-id order, exits iterated in `Direction` enum order; component with no anchor placed at the next deterministic free origin; occupied target cell → nearest-free-cell spill on the same Z, Chebyshev ring scan, deterministic tie-break); returns `AreaLayoutProposal(Anchored, Proposed, Collisions)`. `ApplyProposalAsync(areaBlueprintId)` re-derives from disk and writes each previously-coordless room via `SaveRoomAsync(bidirectional: false)` best-effort, returning `AreaLayoutApplyResult(Written, Warnings)`. DI registration in the Authoring module wiring.
- **Files:** `Core/Modules/Authoring/Systems/IAreaLayoutSystem.cs` (new), `AreaLayoutSystem.cs` (new), module/DI registration; `Hedron.Tests/Authoring/AreaLayoutSystemTests.cs` (new).
- **Dependencies:** WP1 (coordinate fields, `Offset`, collision helper).
- **Out of scope:** any write during `Propose`; multi-area layout; UI.
- **Exit criterion:** `dotnet test` green with system-unit tests covering postconditions 7–8 (determinism, anchor preservation, collision-free placement, Z-handling, disconnected components, apply-only-writes-coordless, best-effort warnings).

### WP3 — Web: shared components + grid page + docs/flows (depends on WP1, WP2)

- **Scope:** extract `Components/Shared/RoomExitsEditor.razor` and `RoomBasicsFields.razor` from `RoomEditor.razor` and refit `RoomEditor` to consume them (behavior-preserving); new `Components/Pages/AreaGridEditor.razor` at `/area/{BlueprintId}/grid` implementing the Main flow (CSS-grid rendering, Z-layer switcher, badges, ghost cells, detail panel, Apply layout, per-action result/warning surfacing); links from `AreaEditor` and the browser row. Doc updates in the same PR: Flow 29 gains leg **B4** + README row wording; `reference/systems.md` rows (`IAreaLayoutSystem`, catalog verb) moved from planned; `features/world/world-content.md` room key-fields row gains `x`/`y`/`z`; `features/admin-authoring/` grid section; remove the shipped entries from `reference/systems-planned.md`.
- **Files:** `Hedron.Web/Components/Shared/RoomExitsEditor.razor` (new), `RoomBasicsFields.razor` (new); `Hedron.Web/Components/Pages/AreaGridEditor.razor` (new), `RoomEditor.razor`, `AreaEditor.razor`, `Browser.razor`; `docs/architecture/flows/flow-29-bulk-content-generation.md`, `flows/README.md`, `docs/reference/systems.md`, `systems-planned.md`, `docs/features/...`.
- **Dependencies:** WP1, WP2.
- **Out of scope:** JS assets, drag interactions, multi-area view, any authoring logic in components (INV-8/08-blazor).
- **Exit criterion:** build green, full suite green, and the manual checklist passes end-to-end against a scratch content directory: open grid on a legacy (coordless) area → ghosts render → create/connect/disconnect/delete/detail-edit each write immediately and re-render → layer switch shows vertical badges → Apply layout anchors all rooms → files inspected show expected YAML → `RoomEditor` behaves exactly as before.

The primary agent runs `architecture-reviewer` (code mode) across the combined diff once all packages land.

## Content tooling impact (INV-18)

This slice **is** content tooling. Specifics:

- **Data-file shape:** room YAML gains optional `x`, `y`, `z` (ints; omitted when unset). No schema-version bump — additive, ignored by older readers (`IgnoreUnmatchedProperties` posture), loaded as `null` when absent. Documented in `features/world/world-content.md` key-fields table in the same PR.
- **Authoring:** the grid page itself; the detail panel and `RoomEditor` share the same components, so both surfaces author the same fields. Coordinates are *not* exposed as raw number inputs in v1 — the grid cell is the coordinate editor (a raw-field escape hatch can ride a later polish pass if wanted).
- **Inspection:** grid rendering (including collision flags and ghost cells) plus the registry-validation warning at both hosts' boot. No new `TemplateRegistry` entry kinds; no admin telnet command (offline authoring surface, per the established editor posture).
- **Generator:** `ContentGenerationSystem` emits no coordinates — generated areas open as all-ghost grids and one Apply layout anchors them. Acceptable for v1; "generator emits coordinates" noted as an optional backlog nicety.

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification | Notes |
|---|---|---|
| Content templates / YAML round-trip | **Gap exposed → closed in-slice (WP1)** | `RoomContentWriter` drops `spawnRules` and `schemaVersion` today — pre-existing silent data loss on every editor re-save, amplified by the grid's neighbor rewrites. Fixed with regression round-trip tests; sibling writers audited for the same class. |
| Authoring catalog | **Adequate** (with one earned verb) | All grid mutations compose existing verbs; `RemoveRoomExitAsync` closes the pre-existing bidirectional-disconnect asymmetry for both `RoomEditor` and the grid — a pattern about to be needed twice. |
| Validation framework | **Gap exposed → closed in-slice (WP1)** | `ValidationReport` has no warnings channel and `RegistryValidationBootstrap` aborts boot on any non-empty report — warn-not-error is inexpressible today. WP1 adds the `Warnings` list (`IsValid` errors-only) and the warn-don't-throw bootstrap change; detection logic declared once (`RoomCoordinateCollisions`) and shared with the grid via the layout proposal. |
| Blazor/UI tier | **Adequate** | Thin-component discipline holds (all logic in catalog/layout system); shared components close the about-to-repeat exits-editor pattern; Blazor-only v1 means no JS discipline section needed yet (lands with the first JS asset, per Design notes). |
| Event bus / handlers / commands / broadcast / time / heartbeat | **Adequate (untouched)** | Authoring stays off the bus; no telnet surface. |
| Persistence (SQLite) / ECS / sessions | **Adequate (untouched)** | YAML-only; no `PersistentEntity`, no `SaveEntityAsync`, no live entities (INV-12/22/23). Persistence opt-in audit: no entity construction path is introduced or modified; `RoomTemplate.Apply` is unchanged and attaches no new component; world content never carries `PersistentEntity` — no Level-1/2/3 findings. |
| Configuration | **Adequate** | No new settings; same `World:ContentDirectory` + `Web:BindUrl`. |
| Concurrency (INV-31) | **Adequate (named)** | Unchanged posture: Blazor circuit thread → synchronous catalog calls over YAML, single-author loopback host; no background service, timer, or live-world path. `ApplyProposalAsync` is a bounded in-request loop, not a job (sim-3 promotion trigger not met). |

## Flows introduced or modified (INV-17)

- **Flow 29 — Content-tooling journey** ([`flow-29-bulk-content-generation.md`](../architecture/flows/flow-29-bulk-content-generation.md)): gains leg **B4 — visual grid editor** — an extension of leg B carrying the grid's per-action catalog sequence plus the `Propose`/`ApplyProposalAsync` layout calls; the README index row's title/description updated to mention the grid. Updated in the WP3 PR.
- **Flow 5 — content reload**: referenced unchanged (the apply-to-live leg).
- No new flow file — the grid is a new trigger surface over the existing journey, not a new runtime path.

## Test plan / Verification (INV-25)

| Tier | Target | Asserts (→ postcondition) |
|---|---|---|
| System unit (T1) | `AreaLayoutSystemTests` | Determinism; anchors never move; proposed cells collision-free vs. anchors and each other; adjacent placement follows `Offset` when free; occupied-cell spill; disconnected-component origins; Up/Down → Z±1; all-anchored/empty area → empty proposal; collisions reported (→ 7). |
| System unit (T1) | `AreaLayoutSystemTests` (apply) | `ApplyProposalAsync` writes only previously-coordless rooms; re-derives from disk; best-effort warnings on a failing room (→ 8). |
| System unit (T1) | `DirectionExtensionsTests` | Offset totality; `Offset(d.Opposite()) == -Offset(d)`; `FromOffset` inverse for unit offsets, null otherwise (→ 10). |
| System unit (T1) | `ContentDefinitionCatalogTests` (additions) | `RemoveRoomExitAsync` policy matrix: removes + writes source; bidirectional removes reciprocal only when it points back; leaves foreign inverse untouched; absent-exit no-op (→ 6). |
| System unit (T1) | `ContentValidatorTests` (addition) | Same-area same-cell duplicate → warning, report still `IsValid`; different area / different Z → no warning; errors-only `IsValid` semantics preserved (→ 9). |
| System unit (T1, YAML round-trip) | `RoomContentWriter` ↔ `RoomTemplateDeserializer` | `X/Y/Z` survive write→read; nulls omitted from file text and read back null; legacy file loads with nulls; **`spawnRules` and `schemaVersion` survive write→read** (regression) (→ 1, 2). (T1, not T4 — T4 is the SQLite `[Persistent]` harness; this is a unit test of the YAML DTO mapping.) |
| System unit (T1) | `RoomTemplate.Apply` (existing test file touch) | No coordinate-bearing runtime component attached (→ 3). |

**Skipped, with rubric reason ([`07-testing.md`](../architecture/07-testing.md)):** `AreaGridEditor.razor`, `RoomExitsEditor.razor`, `RoomBasicsFields.razor`, `RoomEditor` refit — presentation skip-tier; every decision they invoke (validation, bidirectional policy, layout, collision) is covered at T1. No handler/flow/architecture-guard additions — no bus, no live world, no new guard-worthy convention. Postconditions 4–5 are compositions of already-tested catalog verbs exercised by the WP3 manual checklist. Postcondition 11's "no event published" half is continuously enforced by the existing T5 guard `ArchitectureGuardTests.Systems_do_not_depend_on_IEventBus` (reflection picks up `AreaLayoutSystem` automatically); its live-world/SQLite half follows from the host composition (no gameplay hosted services), guarded by `HostCompositionTests`. **Testability gaps: none** — the layout system is deterministic by construction (no RNG, no clock; INV-26 moot) and the catalog test harness (temp content directory) already exists.

## Architecture brief *(in-flight; trimmed on ship)*

### Placement

- **Layer:** entirely the presentation/entry-point tier over existing domain systems — the [08-blazor](../architecture/08-blazor.md) authoring suite. No bus involvement, no live-world touch (INV-12/22/23 posture unchanged: YAML only, `reload` is the apply leg).
- **New Core surface** (final shapes):

```csharp
// Core/Modules/Authoring/Systems/IAreaLayoutSystem.cs
public interface IAreaLayoutSystem
{
    AreaLayoutProposal Propose(string areaBlueprintId);
    Task<AreaLayoutApplyResult> ApplyProposalAsync(string areaBlueprintId, CancellationToken ct = default);
}
public sealed record RoomPosition(int X, int Y, int Z);
public sealed record AreaLayoutProposal(
    IReadOnlyDictionary<string, RoomPosition> Anchored,
    IReadOnlyDictionary<string, RoomPosition> Proposed,
    IReadOnlyList<CoordinateCollision> Collisions);
public sealed record AreaLayoutApplyResult(int Written, IReadOnlyList<string> Warnings);

// IContentDefinitionCatalog addition
Task<ContentWriteResult> RemoveRoomExitAsync(
    string roomBlueprintId, Direction direction, bool bidirectional, CancellationToken ct = default);

// DirectionExtensions additions (East = X+1, North = Y+1, Up = Z+1)
public static (int Dx, int Dy, int Dz) Offset(this Direction direction);
public static Direction? FromOffset(int dx, int dy, int dz);
```

  `RoomTemplate.X/Y/Z` (`int?`) + YAML (de)serialization + round-trip coverage. `Apply` does **not** attach any runtime component in this slice. The catalog-addition question from the seed is resolved: one disconnect verb (symmetry with the existing bidirectional add) plus the layout system's own bulk apply (fitter precedent) — no other catalog growth.
- **New Web surface:** `/area/{id}/grid` page; `Components/Shared/RoomExitsEditor` + `RoomBasicsFields`; **no JS asset in v1** (resolved below).

### Family test

The coordinate field is the seam with siblings: **auto-map/ASCII minimap, cardinal-distance queries, ranged line-of-sight, overworld travel, procedural area generation** ([feature-horizon §1](../design/feature-horizon.md)) all read room coordinates. Breadth chosen: **shaped for later** — author-side data lands now in the exact shape the runtime slice consumes; nothing runtime is built. The **multi-area world view** is the editor's own sibling: per-area local coordinate spaces compose with a future per-area origin offset (overworld design), so nothing here forecloses it. Disposition: **defer** (backlog entry added).

### Observers & contributors

None. No events fired (authoring is off the bus per 08-blazor); no contributor ports touched. The registry-validation sweep gains the coordinate-collision **warning** (warn-but-allow) — resolved to live in `IContentValidator` registry mode, backed by the shared `RoomCoordinateCollisions` helper (see resolved decisions).

### Ordering & timing

None — no heartbeat, no handlers, no event phases.

### Concurrency posture (INV-31)

Unchanged and named: Blazor circuit thread → synchronous catalog calls over YAML, single-author loopback host; no new background service, timer, or live-world path. (If a long-running bulk layout write ever needs a job, the sim-3 `SimulationRunService` promotion trigger applies — not this slice.)

### Invariants in tension

- **INV-8 / 08-blazor thin-component rule** — the grid is the biggest temptation yet to put logic in a component/JS; the layout system + catalog composition above is the answer.
- **INV-15** — `RoomTemplate` shape change updates the template docs/reference in the same PR; **INV-16/29** — new system rows in `reference/systems.md` on ship, planned entries in `systems-planned.md` while in flight; coordinate fields in the template's documented shape.
- **INV-18** — this slice *is* content tooling; the impact section is the slice itself.
- **INV-19** — shared Razor components close a repeated-pattern gap (exits UI existed once, about to be needed twice); the catalog reuse avoids a second authoring path; the `spawnRules` writer fix closes a round-trip gap the slice would have amplified.
- **INV-17** — Flow 29 (content-tooling journey) gains the grid leg; update in-PR.
- **INV-20** — 08-blazor.md JS-presentation-only note deferred with the first JS asset (none in v1). The `add-core-system` skill's companion-validation paragraph **is** updated in WP1: it currently states validator rules "fail startup with a full report," which becomes wrong for warning-class rules once the `Warnings` channel lands.
- **INV-25** — testable surface: layout proposal system, template YAML round-trip with coordinates + spawnRules, adjacency→Direction mapping, collision warning, disconnect verb. Blazor components are skip-tier presentation.

### Resolved decisions (do not relitigate)

1. Coordinates persist on `RoomTemplate` YAML as optional `X/Y/Z` (user-confirmed).
2. Immediate per-action writes; no draft/transaction layer (user-confirmed).
3. Z-layer switcher in v1; Up/Down exits as cell badges (user-confirmed).
4. Single-area scope; world view deferred to backlog.
5. Runtime `CoordinateComponent` and exit↔coordinate enforcement stay in the existing backlog item — not this slice.

*Resolved in planning (seed open questions → recommendations; see Open items below for user-confirmation status):*

6. **Auto-layout write-back:** explicit **Apply layout** bulk action via `ApplyProposalAsync` (re-derive-from-disk, best-effort), plus natural persistence of a ghost room's coordinates whenever that individual room is saved by any grid action. Rationale in Design notes.
7. **Collision-warning home:** both — `IContentValidator` registry-mode warning **and** grid visual flag, sharing one detection helper (`RoomCoordinateCollisions`, World module, so the World-layer validator never depends on the Authoring module). Costs one rule, surfaces at both hosts' boot.
8. **Blazor-only v1:** no JS asset; click-based interaction set. Drag-connect (and the 08-blazor JS section) deferred to a polish pass.

## Open items

None — decisions 6 (Apply-layout bulk write UX) and 8 (Blazor-only v1, click-to-connect) and the in-slice scoping of the writer-fidelity fix (`spawnRules` + `schemaVersion`) were confirmed by the user before the spec gate; the spec gate's blocking finding (the `ValidationReport` warnings channel + `RegistryValidationBootstrap` warn-don't-throw consequence) is folded into WP1 above.

## Related

- [`admin-area-authoring.md`](admin-area-authoring.md) — telnet-side area authoring (in-flight); shares the catalog posture.
- [`../features/admin-authoring/admin-authoring.md`](../features/admin-authoring/admin-authoring.md) · [`content-authoring.md`](../features/admin-authoring/content-authoring.md) · [`content-tooling.md`](../features/admin-authoring/content-tooling.md) — the feature tier this extends.
- [`../architecture/flows/flow-29-bulk-content-generation.md`](../architecture/flows/flow-29-bulk-content-generation.md) (leg B → new leg B4) · [`flow-05-content-reload.md`](../architecture/flows/flow-05-content-reload.md) (apply leg).
- [`../architecture/08-blazor.md`](../architecture/08-blazor.md) — the tier's rules; [`../roadmap/backlog.md`](../roadmap/backlog.md) — coordinate-system runtime half, multi-area world view, atomic multi-file cascade.
