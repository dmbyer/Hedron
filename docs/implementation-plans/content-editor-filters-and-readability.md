# Content Editor — area filters, selection fields & readability (Slice A)

**Status:** planned
**Actors:** Content author (offline Blazor editor)
**Module:** `Core/Modules/Authoring/` (catalog read-model enrichment); `Hedron.Web/` (presentation: filters, selectors, theme)

---

## Description

The first of two content-editor slices. This one is **read-only**: it does not change any
content-mutation path. It enriches the authoring catalog's read side with the area association
each surface needs, then layers presentation on top — area filters on the three listing pages
(rooms, mobs, items, each unselectable back to "all"), filterable selection fields on the editor
pages (area picker for rooms; a room-lookup picker for exit targets; a cascading area→room picker
for item and mob spawn rooms), and a dark-theme readability pass (lighter foreground text, dark
backgrounds, dark-blue unselected buttons). The cross-definition **write** behaviors (bidirectional
exit linking), **delete**, and **reference-integrity** validation are deliberately held for
[Slice B](content-reference-integrity-and-delete.md).

---

## Design notes

*Durable seam rationale — survives into the shipped feature doc on ship (INV-28).*

- **The area association is computed in the catalog, not in components.** A room's area is its
  `AreaId`; an item's/mob's area is a **two-hop resolution** (`spawnRoomId` → that room's
  `AreaId`). That two-hop projection is *content knowledge*, and it is needed by **five** surfaces
  (three listing filters + two area→room cascade selectors) — well past the ≥3× bar (INV-19). It
  therefore lands **once** in the `Authoring` catalog read-model, never re-implemented in a Blazor
  page. Components filter/display over the catalog's projection; they never resolve references
  themselves. This keeps Blazor presentation-only (Blazor discipline / INV-8 analogue) and keeps the
  reference-resolution logic in one place so [Slice B](content-reference-integrity-and-delete.md)'s
  reference model can subsume it rather than duplicate it.
- **Filtering itself is presentation.** Choosing a filter value and hiding non-matching rows is a UI
  concern and stays in the component. Only the *data* that makes filtering possible (the area
  association, the area list, the rooms-in-area list) is catalog-owned.
- **Read-only slice — no INV-12/22/23 surface.** Nothing here creates a live entity, writes YAML,
  touches SQLite, or runs the event bus. The web host already wires no `bus.Subscribe`
  ([content-authoring.md](../features/admin-authoring/content-authoring.md)); this slice keeps it
  that way.

---

## Preconditions

- The `Hedron.Web` Blazor authoring host is running with `IContentDefinitionCatalog` registered (via
  `AddAuthoringModule`); content YAML exists on disk under the configured content root.
- Room definitions carry `RoomTemplate.AreaId` (the area's *blueprint id*, mirrored from
  `areaBlueprintId` by `AreaSystem`); item/mob definitions carry `SpawnRoomBlueprintId`.
- No live-world entities, SQLite rows, or event bus are involved — this is offline tooling.

## Postconditions

- The catalog exposes, per room/item/mob definition, the **area blueprint id** it belongs to:
  - room → its own `AreaId` (one hop);
  - item/mob → `SpawnRoomBlueprintId` → that room's `AreaId` (two hops);
  - missing/blank/dangling `SpawnRoomBlueprintId` or `AreaId` → **no area** (`null`), never a throw.
- The catalog exposes selection-source data for editor pickers: the area list (already
  `List(Area)`) and the rooms-in-area projection (rooms whose resolved area equals a given area
  blueprint id).
- The two-hop resolution lives **only** in `Core/Modules/Authoring/`; no Blazor component resolves a
  cross-reference.
- The Browser listing pages for rooms/mobs/items render an area filter that defaults to "all" and is
  always returnable to "all"; selecting an area hides non-matching rows.
- The Room editor offers an area picker (selecting writes `RoomTemplate.AreaId`) and a filterable
  room-lookup picker per exit direction (selecting writes a blueprint id into `RoomTemplate.Exits`
  exactly as the raw field does today — no counterpart write).
- The Item and Mob editors offer a cascading area→room picker that writes `SpawnRoomBlueprintId`.
- The shared stylesheet renders lighter foreground text on dark backgrounds, dark-blue unselected
  buttons, and white-on-blue selected buttons.
- No content-mutation path, validation rule, YAML shape, or save behavior changes.

## Main flow

This is a read/presentation slice with **two independent entry points**; neither is a runtime
event-driven flow.

**A. Browse + filter (listing page)**

1. Author opens the Browser, selects a kind tab (room/mob/item).
2. The page calls `Catalog.List(kind)`; each returned `ContentSummary` now carries its resolved
   `AreaBlueprintId`.
3. The page renders an area-filter control populated from `Catalog.List(Area)` plus an "All areas"
   default.
4. Author picks an area; the component hides rows whose `AreaBlueprintId` does not match. Picking
   "All areas" restores the full list. (Filtering is component-side; the data it filters on is
   catalog-computed.)

**B. Edit with selection pickers (editor pages)**

1. Author opens the Room/Item/Mob editor for a definition.
2. The editor calls `Catalog.List(Area)` (and, for the cascade, `Catalog.RoomsInArea(areaId)` or
   filters the enriched room summaries) to populate pickers.
3. Room editor: area picker bound to `AreaId`; per-direction exit picker filters the room summary
   list by name/blueprint id and on selection writes the chosen blueprint id into `Exits[dir]`.
4. Item/Mob editor: an area select narrows a dependent room select; choosing a room writes
   `SpawnRoomBlueprintId`.
5. Author clicks Save → existing `Catalog.SaveAsync` path, unchanged (validate → write YAML).

## Events fired

**None.** Offline authoring tooling: the web host wires no `bus.Subscribe`, runs no heartbeat, and
publishes no events (per the content-authoring feature doc and the Architecture brief's
observers/ordering finding). This absence is intentional — no events are invented for a read-only
slice (INV-5/6/7 N/A).

## Systems / handlers involved

- **`IContentDefinitionCatalog` / `ContentDefinitionCatalog`** (`Core/Modules/Authoring/Systems/`) —
  gains the area-association projection and the rooms-in-area query. The only system touched.
- **No handlers.** No event bus, so no subscribers.
- Reuses (read-only): `IContentSerializer` (deserialize room/item/mob templates to read
  `AreaId`/`SpawnRoomBlueprintId`), `RoomTemplate`, `ItemTemplate`, `MobTemplate`, `AreaTemplate`.

## Architecture brief

*In-flight; trimmed on ship (INV-28).*

### Placement

- **Catalog read-model** → `Core/Modules/Authoring/` (the `IContentDefinitionCatalog` family). The
  catalog already owns the on-disk definition set (`List`/`Load`); the area-association projection
  and the area→room lookup are natural extensions of its read side.
- **Presentation** → `Hedron.Web/Components/Pages/` (`Browser`, `RoomEditor`, `ItemEditor`,
  `MobEditor`) + the shared stylesheet.
- Spine: none — this is tooling, not gameplay. No substrate/aspect/effect involvement.

### Seams

1. **Area-association read-model (build now).** Today
   [`ContentSummary`](../../Core/Modules/Authoring/ContentSummary.cs) carries only
   `(BlueprintId, Name, Description)` — no area, so the listing pages have nothing to filter on. The
   catalog needs to expose, per kind, the area each definition belongs to (room: `AreaId` directly;
   item/mob: resolved through `spawnRoomId`/`spawnRoomBlueprintId` → room → `AreaId`). **Planner
   decides the exact shape** — an added optional `AreaBlueprintId` on `ContentSummary`, or a
   dedicated reference-projection row — but the *resolution* is catalog-side. Catalog change →
   update `docs/reference/systems.md` (INV-16/29).
2. **Selection-source queries (build now).** Editor pickers need "all areas" (→ `List(Area)`) and
   "rooms in area X". The area→room lookup is the same association data inverted; expose it as a
   catalog query (e.g. `RoomsInArea(areaBlueprintId)`) or let components filter the enriched room
   summaries. Planner picks; either way the mapping is computed catalog-side once.
3. **Exit-target lookup (build now, read-only).** The room editor's exit fields become a filterable
   room picker (name + blueprint id) instead of a raw paste field. This is presentation over the
   room summary list — **no write-path change**; it still writes a blueprint id into
   `RoomTemplate.Exits` exactly as today. (The *bidirectional* counterpart-write is Slice B.)
4. **Readability theme (build now).** Pure CSS in `Hedron.Web` (lighter foreground, dark
   backgrounds, dark-blue unselected buttons, white-on-blue selected buttons kept). No architecture;
   listed only so the slice owns it.

### Family / forward-generalization

The area-association projection is the *specific* case of "resolve a definition's cross-reference to
a target definition." Slice B generalizes exactly this into a declared-edge reference model. To keep
B a refactor and not a rewrite, **key the projection by the reference field, not by hardcoding "area"
five times** — the room→area and item/mob→room→area hops should read as instances of one resolver so
B can fold them into its reference index.

### Observers / contributors / ordering

None. Offline tooling: no event bus, no contributor ports, no heartbeat, no handler ordering. The
absence is intentional and called out so the planner does not invent events for a read-only slice
(INV-5/6/7 N/A).

### Invariants in tension

- **INV-19** (≥3× → framework) — the five-surface area projection lands once in the catalog. *This
  is the gap this slice closes.*
- **INV-8 / Blazor discipline** — reference resolution is authoring/content knowledge → catalog;
  components stay presentation-only.
- **INV-16 / INV-29** — any `IContentDefinitionCatalog` surface change updates
  `docs/reference/systems.md` in the same PR.
- **INV-25** — the catalog gains read methods with real logic (two-hop resolution); they get unit
  tests (resolution correctness, missing-spawn-room → no area, unfiltered "all"). Determinism: no
  randomness/clock, so no INV-26 surface.

### Resolved decisions (do not relitigate)

- **Slicing:** two slices; this is the read-only/presentation half. (User-confirmed.)
- **Bidirectional linking, delete, integrity validation:** explicitly **not** here — Slice B.

---

## Implementation plan — work packages

### Resolved read-model shape (planner decision)

Extend `ContentSummary` with one **optional nullable** field, `string? AreaBlueprintId` (defaults
`null`), rather than a parallel projection record. Rationale: every listing page already consumes
`ContentSummary`; a second record would force the pages to merge two lists. The field is `null` for
areas (areas have no parent area) and for any room/item/mob whose area cannot be resolved. The
resolution stays entirely catalog-side (see WP1) — the field is *carried* by the summary, not
*computed* by the component.

To honor the family/forward-generalization note (key by the reference field, not "area" hardcoded
five times), the catalog implements **one private resolver** `ResolveAreaBlueprintId(kind, template,
roomsByBlueprintId)` invoked uniformly: room hop = `RoomTemplate.AreaId`; item/mob hop =
`SpawnRoomBlueprintId` → room → `AreaId`. Slice B can fold this single resolver into its reference
index rather than untangling five copies.

### WP1 — Catalog area-association read-model + rooms-in-area query

- **Scope:** Add `string? AreaBlueprintId` to `ContentSummary`. In `ContentDefinitionCatalog`,
  populate it in `List(kind)` via a single `ResolveAreaBlueprintId` resolver. Add the
  selection-source query `IReadOnlyList<ContentSummary> RoomsInArea(string areaBlueprintId)` (rooms
  whose resolved area equals the argument) to `IContentDefinitionCatalog`. Build a one-shot
  `blueprintId → RoomTemplate.AreaId` map per call by listing/deserializing rooms once, so item/mob
  two-hop resolution is O(1) per definition.
- **Files:** `Core/Modules/Authoring/ContentSummary.cs`,
  `Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs`,
  `Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs`; `docs/reference/systems.md`
  (INV-16/29 — catalog surface change).
- **Dependencies:** none.
- **Out of scope:** any reference-integrity validation, dangling-reference reporting, or bidirectional
  linking (Slice B). A dangling reference here simply yields `AreaBlueprintId == null`.
- **Exit criterion:** unit tests green — room one-hop, item/mob two-hop, blank/missing/dangling →
  `null`, `RoomsInArea` returns exactly the matching rooms and excludes non-matches.

### WP2 — Blazor filters, pickers, and cascade selectors

- **Scope:** Add the area filter (defaulting to "All areas") to the Browser listing for
  room/mob/item kinds. Add the area picker to RoomEditor (bound to `AreaId`) and the per-direction
  filterable room-lookup exit picker (writes into `Exits[dir]`, same write as today). Add the
  cascading area→room picker to ItemEditor and MobEditor (writes `SpawnRoomBlueprintId`). All data
  comes from `Catalog.List`/`Catalog.RoomsInArea`; components do **no** reference resolution. Filter
  state and row hiding are component-local.
- **Files:** `Hedron.Web/Components/Pages/Browser.razor`, `RoomEditor.razor`, `ItemEditor.razor`,
  `MobEditor.razor`; optionally one shared picker component under
  `Hedron.Web/Components/` (see open question — not load-bearing).
- **Dependencies:** WP1 (consumes `AreaBlueprintId` and `RoomsInArea`).
- **Out of scope:** the counterpart exit write (Slice B), delete buttons (Slice B), the readability
  theme (WP3).
- **Exit criterion:** manual smoke — filters hide/show rows and reset to "all"; editor pickers write
  the same field values the raw text inputs do today; Save path unchanged.

### WP3 — Readability CSS pass

- **Scope:** Update the shared stylesheet for lighter foreground text on dark backgrounds, dark-blue
  unselected buttons, and white-on-blue selected/active buttons. Pure presentation.
- **Files:** `Hedron.Web/wwwroot/app.css` (and any component-scoped `.razor.css` if introduced).
- **Dependencies:** independent of WP1/WP2 (can land in parallel).
- **Out of scope:** any markup/behavior change.
- **Exit criterion:** visual review against the four button/text states; no class renames that break
  WP2 markup.

After all three packages land, the primary agent runs `architecture-reviewer` (code mode) across the
combined diff.

## Content tooling impact

This slice **is** content-tooling work; it adds no gameplay state, no new YAML shape, no admin
command, and no `TemplateRegistry` entry (INV-18 — nothing to author or inspect beyond what already
exists). It changes only how existing definitions are *browsed and cross-referenced* in the offline
editor. The catalog gains read methods (`AreaBlueprintId` projection, `RoomsInArea`) that must be
reflected in `docs/reference/systems.md` (INV-16/29).

## Cross-cutting surfaces stressed

Ground-rule-9 audit. The Architecture brief's dispositions map in: the area-projection *Build now*
→ **Gap exposed (closed by this slice)**; everything else → **Adequate**.

- **Content templates / catalog read-model — Gap exposed (closed here).** The five-surface area
  association (3 listing filters + 2 cascade selectors) is past the ≥3× bar (INV-19). The framework
  — a single catalog-side resolver — lands **in this slice** (WP1), not deferred. This is exactly the
  gap the brief flagged; it is resolved here, not absorbed silently.
- **ECS queries / event bus / persistence / sessions / time / broadcast / configuration / modules —
  Adequate (N/A).** Offline tooling touches none of them: no live entity, no `bus.Subscribe`, no
  SQLite, no heartbeat. The web host already wires no event bus; this slice keeps it that way.
- **Commands — Adequate (N/A).** No telnet command surface; the editor is the only caller, already a
  thin caller of the catalog facade.
- **Output — Adequate (N/A).** Blazor rendering, not the typed `IOutputMessage` pipeline.

### Persistence opt-in audit (INV-22/23)

- **Level 1 — entity domain:** N/A. No entity construction path. The catalog reads/writes **YAML
  only** and never creates a live entity, adds `PersistentEntity`, or calls `SaveEntityAsync` (per the
  catalog's own contract). World content (rooms/items/mobs/areas) spawned from this YAML at startup
  carries no `PersistentEntity` — unchanged and untouched here.
- **Level 2 — components:** N/A. No component introduced or touched; `ContentSummary` is a
  presentation DTO, not an ECS component (no `[Persistent]` question).
- **Level 3 — save-on-change:** N/A. No `SaveEntityAsync` call anywhere in scope.

## Flows introduced or modified

- **Flow 29 — [Content-tooling journey (bulk generate · offline edit)](../architecture/flows/flow-29-bulk-content-generation.md)** —
  *modified.* The "offline edit" leg gains the catalog area-association read step before a listing
  renders, and the editor-picker selection sub-steps (area filter, exit room-lookup, area→room
  cascade). The WP2/WP1 PR must update flow-29's offline-edit steps to match the as-built read calls
  (`List` now returns `AreaBlueprintId`; new `RoomsInArea`). No new flow file — the change is
  additive within flow 29. No runtime/event flow is introduced (no event bus).

## Test plan / Verification

Per INV-25 and the rubric in [docs/architecture/07-testing.md](../architecture/07-testing.md).
Determinism: no randomness or wall-clock in scope, so no INV-26 seam is required.

**System-unit (catalog) — the real logic, every Postcondition asserting invisible state:**

- `List(Room)` populates `AreaBlueprintId` from `RoomTemplate.AreaId` (one-hop). — *asserts the
  room-association postcondition.*
- `List(Item)` / `List(Mob)` populate `AreaBlueprintId` via `SpawnRoomBlueprintId` → room → `AreaId`
  (two-hop). — *asserts the two-hop postcondition.*
- Blank `SpawnRoomBlueprintId`, blank `AreaId`, and a `SpawnRoomBlueprintId` pointing at a
  non-existent room each yield `AreaBlueprintId == null` and **do not throw**. — *asserts the
  missing/dangling postcondition.*
- `RoomsInArea(areaId)` returns exactly the rooms resolving to that area and excludes
  non-matching rooms; an unknown area id returns empty. — *asserts the selection-source postcondition.*

**Skipped, with reason:**

- **WP2 Blazor filters/pickers/cascade** — presentation: filter state, row hiding, and option
  population are thin component plumbing over catalog data already covered above. No domain decision
  to assert; component-render tests are out of the tier rubric for this codebase. Manual smoke per WP2
  exit criterion.
- **WP3 CSS** — pure presentation, no logic.
- **`ContentSummary` shape** — pure-data DTO, not a `[Persistent]` component; no round-trip test
  (nothing is persisted). Exercised transitively by the `List` tests above.
- **`SaveAsync`/validation** — unchanged; existing tests cover it.

No testability gap: the catalog's new methods are pure functions of on-disk YAML, exercisable through
the existing content-directory test fixture; no un-injected seam (randomness/clock/external I/O beyond
the already-testable file read) is introduced.

## Related

- [content-reference-integrity-and-delete.md](content-reference-integrity-and-delete.md) — Slice B;
  consumes this slice's single area resolver as the seed of its declared-edge reference model
  (bidirectional exits, delete, integrity validation, health page).
- [admin-area-authoring.md](admin-area-authoring.md) — `mkarea` + `list`; the in-game side of area
  authoring whose `AreaSystem` mirrors `areaBlueprintId` into `RoomTemplate.AreaId` (the field this
  slice resolves).
- [content-authoring feature](../features/admin-authoring/content-authoring.md) — the offline catalog
  + Blazor editor this slice enriches.
- [flow-29-bulk-content-generation.md](../architecture/flows/flow-29-bulk-content-generation.md) — the
  content-tooling journey modified here.

## Open questions

- **Filter widget vs. cascade widget reuse** — whether the area→room cascade selector and the
  listing-page area filter share one Blazor component. Presentation detail; not load-bearing for the
  seam. (WP2.)
- **`RoomsInArea` query vs. client-side filter of enriched room summaries** — the planner exposes
  `RoomsInArea` as a catalog method (keeps the inversion catalog-side, consistent with the seam).
  If WP2 finds the cascade is simpler filtering the already-enriched room summaries client-side, that
  is acceptable *only* because the `AreaBlueprintId` it filters on is still catalog-computed; confirm
  before dropping the method. Not blocking.

> **Resolved by planner (was open in the seed):** the read-model shape is an optional
> `string? AreaBlueprintId` on `ContentSummary` (not a separate projection record), with a single
> catalog-side resolver. Item/mob resolution stays in the catalog. See *Implementation plan — work
> packages*.
