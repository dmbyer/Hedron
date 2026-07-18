# Visual grid area editor (world-editor-grid)

- **Status:** planned
- **Actors:** Administrator (content designer, via the Blazor authoring host)
- **Module:** `Core/Modules/Authoring/` (layout system + catalog extensions) · `Core/Modules/World/Templates/` (RoomTemplate coordinate fields) · `Hedron.Web/` (grid page + shared editor components). Feature home: [`docs/features/admin-authoring/`](../features/admin-authoring/) (content-tooling tier).

## Description

A visual grid editor inside the area editor: the designer sees an area's rooms laid out on a 2-D grid (one Z-layer at a time, with a layer switcher), creates a room by clicking an empty cell, connects/disconnects exits between adjacent cells, deletes rooms, and edits a selected room's name, description, and full exit map in a detail panel. The exits editor and name/description fields are extracted into shared Razor components used by both the existing `RoomEditor` page and the grid's detail panel. Grid position is persisted as optional `X/Y/Z` fields on `RoomTemplate` YAML — the authoring-side half of the backlogged coordinate system — and every mutation is an immediate per-action write through `IContentDefinitionCatalog`. Scope is a **single area at a time**; the multi-area world view is deferred.

## Design notes

*(durable rationale — survives into `roadmap/completed/` on ship)*

- **Coordinates live on `RoomTemplate` (YAML), not in an editor sidecar or derived per-open.** The grid's layout data *is* the authored half of the backlogged coordinate system ([`backlog.md` → Locale enhancements](../roadmap/backlog.md)): optional `X/Y/Z` (nullable ints) on the room template. When the runtime `CoordinateComponent` slice lands it reads the same field — a refactor, not a migration. A sidecar layout file was rejected as a second coordinate home that would drift; a derive-on-open layout was rejected as unstable across sessions and lossy on non-planar graphs. Disposition: **shaped for later** — the YAML field lands now; the runtime component, cardinal-distance queries, and exit↔coordinate consistency *enforcement* stay deferred with the existing backlog item.
- **Coordinates are advisory authoring layout, not a constraint on exits.** An exit may target a non-adjacent cell, another Z-layer, or another area; the grid renders adjacency-friendly exits as edges and everything else as badges/markers, and the detail panel's exits component remains the full-fidelity editor. Enforcing "east means X+1" is a game-rule decision that belongs to the future runtime coordinate slice, not the editor.
- **Grid mutations are compositions of existing catalog verbs — no parallel authoring path.** Create-at-cell = `CreateNew` + set `AreaId`/coords + `SaveRoomAsync`; connect = set exit + `SaveRoomAsync(bidirectional: true)` (reusing its inverse-exit conflict policy); delete = `DeleteAsync` (existing cascade-clear of referrers). The one genuinely new piece of *logic* is auto-layout for legacy rooms without coordinates (BFS over exits proposing positions) — that is real computation and lands as a system in `Core/Modules/Authoring/` (testable, INV-25), never in the component or JS. The adjacency→`Direction` offset mapping is a pure Core helper beside `DirectionExtensions`.
- **JS/HTML is presentation-only — the 08-blazor "thin component" discipline extends one tier down.** Whatever JS the grid needs (pointer capture, drag-connect) renders and captures input; every mutation round-trips through the Blazor component into the catalog. No authoring logic (validation, ID generation, exit rules, layout math) in JS. This is the [`08-blazor.md`](../architecture/08-blazor.md) component discipline applied to the host's first JS asset; that doc gains a short section when the pattern lands (INV-20/INV-27: cite, don't restate).
- **Immediate per-action writes; no draft/transaction layer.** Each grid action is one atomic-per-file catalog call with the established warn-but-allow integrity posture. A draft working set would require the transactional multi-file cascade already deferred in [`backlog.md`](../roadmap/backlog.md) ("Atomic multi-file content cascade") — not rebuilt here.
- **Shared editor components (`Components/Shared/`)** — the exits editor and name/description fields extract into reusable Razor components consumed by both `RoomEditor` and the grid detail panel (precedent: `CombatantSideEditor`). Presentation tier, skip-tier for tests; the logic they call is already covered.

## Architecture brief *(in-flight; trimmed on ship)*

### Placement

- **Layer:** entirely the presentation/entry-point tier over existing domain systems — the [08-blazor](../architecture/08-blazor.md) authoring suite. No bus involvement, no live-world touch (INV-12/22/23 posture unchanged: YAML only, `reload` is the apply leg).
- **New Core surface:**
  - `RoomTemplate.X/Y/Z` (`int?`) + YAML (de)serialization + round-trip coverage. `Apply` does **not** attach any runtime component in this slice.
  - An auto-layout proposal system (working name `IAreaLayoutSystem`) in `Core/Modules/Authoring/Systems/`: input = an area's room summaries + exit maps; output = proposed coordinates for rooms lacking them (BFS from an anchor, collision-avoiding). Pure computation, returns results (INV-5 non-issue — never publishes). Deterministic (no chance/time — INV-26 non-issue).
  - Possibly a small catalog addition if the planner finds one needed (e.g. a bulk "write proposed coordinates" that loops `SaveAsync` best-effort, mirroring the cascade posture). Prefer composing existing verbs first (INV-15/19).
- **New Web surface:** grid page/panel inside the area editor route; `Components/Shared/RoomExitsEditor` + name/description component; first `wwwroot` JS asset if pure CSS-grid Blazor proves insufficient (try Blazor-only first — the interaction set is clicks + maybe drag).

### Family test

The coordinate field is the seam with siblings: **auto-map/ASCII minimap, cardinal-distance queries, ranged line-of-sight, overworld travel, procedural area generation** ([feature-horizon §1](../design/feature-horizon.md)) all read room coordinates. Breadth chosen: **shaped for later** — author-side data lands now in the exact shape the runtime slice consumes; nothing runtime is built. The **multi-area world view** is the editor's own sibling: per-area local coordinate spaces compose with a future per-area origin offset (overworld design), so nothing here forecloses it. Disposition: **defer** (backlog entry added).

### Observers & contributors

None. No events fired (authoring is off the bus per 08-blazor); no contributor ports touched. The only "observer" is the existing registry-validation sweep — coordinate *collision within an area* (two rooms, same X/Y/Z) should surface as a **warning**, not an error, consistent with warn-but-allow; planner decides whether that lives in `IContentValidator` or editor-side only.

### Ordering & timing

None — no heartbeat, no handlers, no event phases.

### Concurrency posture (INV-31)

Unchanged and named: Blazor circuit thread → synchronous catalog calls over YAML, single-author loopback host; no new background service, timer, or live-world path. (If a long-running bulk layout write ever needs a job, the sim-3 `SimulationRunService` promotion trigger applies — not this slice.)

### Invariants in tension

- **INV-8 / 08-blazor thin-component rule** — the grid is the biggest temptation yet to put logic in a component/JS; the layout system + catalog composition above is the answer.
- **INV-15** — `RoomTemplate` shape change updates the template docs/reference in the same PR; **INV-16/29** — new system rows in `reference/systems.md`, coordinate fields in the template's documented shape.
- **INV-18** — this slice *is* content tooling; the impact section is the slice itself.
- **INV-19** — shared Razor components close a repeated-pattern gap (exits UI existed once, about to be needed twice); the catalog reuse avoids a second authoring path.
- **INV-17** — Flow 29 (content-tooling journey) gains the grid leg; update in-PR.
- **INV-20** — 08-blazor.md gains the JS-presentation-only note; `add-tests`/reviewer guidance unchanged.
- **INV-25** — testable surface: layout proposal system, template YAML round-trip with coordinates, adjacency→Direction mapping, collision warning (wherever it lands). Blazor components + JS are skip-tier presentation.

### Resolved decisions (do not relitigate)

1. Coordinates persist on `RoomTemplate` YAML as optional `X/Y/Z` (user-confirmed).
2. Immediate per-action writes; no draft/transaction layer (user-confirmed).
3. Z-layer switcher in v1; Up/Down exits as cell badges (user-confirmed).
4. Single-area scope; world view deferred to backlog.
5. Runtime `CoordinateComponent` and exit↔coordinate enforcement stay in the existing backlog item — not this slice.

## Open questions

- **Auto-layout write-back UX:** proposal renders immediately, but does writing coordinates for *all* legacy rooms happen per-room-as-touched or via an explicit "apply layout" bulk write (best-effort loop)? Planner recommends one; either fits the immediate-write model.
- **Collision warning home:** `IContentValidator` (surfaces in Integrity page too) vs. grid-only visual overlap indicator. Small either way; validator preferred if it costs one rule.
- **Blazor-only vs. JS interop:** decide after a spike on drag-connect; the architecture is identical either way (presentation-only).
