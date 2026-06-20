# Content Editor — reference integrity, delete & bidirectional linking (Slice B)

**Status:** planned
**Actors:** Content author (offline Blazor editor)
**Module:** `Core/Modules/Authoring/` (reference model + `Delete` + bidirectional write); `Core/Modules/World/` (`Direction.Opposite` if absent); `Hedron.Web/` (delete buttons, integrity/health page, bidirectional toggle)

---

## Description

The second content-editor slice — the cross-definition **write and integrity** half. It introduces a
single cross-definition **reference model** (a declared set of typed edges over the on-disk YAML
definitions: room→area, exit→room, item→room, mob→room) and uses that one model to power four
consumers: **delete** with **cascade-clear** of referrers, **warn-but-allow** broken-reference
feedback on save, an editor **integrity/health page** that sweeps and reports every broken link, and
**bidirectional exit linking** (writing room A's `east → B` optionally also writes B's `west → A`).
Delete and cascade are **YAML-file operations only** — they never touch the live world or SQLite.
This builds on [Slice A](content-editor-filters-and-readability.md)'s catalog read-model.

---

## Preconditions

- The offline Blazor editor (`Hedron.Web`, loopback host) is running; the designer is browsing/editing YAML
  content definitions — no live game, no telnet, no heartbeat, no persistence (`AddContentBootstrapHostedServices`
  only).
- The content directory holds the on-disk YAML definition set (areas/rooms/items/mobs) the catalog reads.
- Slice A's catalog read-model (`List` + filters) is in place; this slice adds the write/integrity half.
- `IContentDefinitionCatalog`, the four `I*ContentWriter`s, `IContentSerializer`, and the templates
  (`AreaTemplate`, `RoomTemplate`, `ItemTemplate`, `MobTemplate`) exist with their reference fields:
  `RoomTemplate.AreaId`, `RoomTemplate.Exits[Direction]`, `ItemTemplate.SpawnRoomBlueprintId`,
  `MobTemplate.SpawnRoomBlueprintId`.

## Postconditions

- **Reference index resolves edges over the on-disk set.** Given the declared edge set, the index answers
  *does target T resolve?* and *who references blueprint id B?* over every definition on disk. A reference whose
  target file is absent is reported as broken.
- **`Delete(kind, id)` removes exactly one YAML file and cascade-clears every referrer.** After delete: the
  target file is gone; every room referencing it via `AreaId` has `AreaId` cleared; every room with an exit
  pointing at it has that exit entry removed; every item/mob with `SpawnRoomBlueprintId == id` has it cleared;
  every area with the deleted room in its `Rooms` list has the entry removed. The result enumerates the deleted
  file and each cascade edit.
- **No SQLite row, live entity, or live-world state is touched by delete.** Delete invokes no
  `EntityService.DestroyEntity`, no SQLite delete, no `SaveEntityAsync`. (INV-22/23 hold by construction —
  world content carries no `PersistentEntity`; the web host runs no persistence flush.) Applying the deletion
  to the live world remains a separate `reload`.
- **Save warns-but-allows on broken cross-references; still hard-blocks structural failure.** A `SaveAsync`
  whose definition has a dangling cross-reference (e.g. room → nonexistent area) **writes the file** and returns
  a result whose **warnings** list the broken refs; a definition that fails structural validation returns
  **failure**, no file written (today's behavior preserved).
- **The integrity/health page lists every broken link across all kinds.** A full reference-index sweep reports
  each broken edge as (source kind, source id, field/direction, missing target id).
- **Bidirectional exit linking writes the inverse exit on the target room.** When enabled on a room save,
  saving room A's `east → B` also writes B's `west → A` via the catalog (one cross-definition write, catalog-owned).
  If B already has a *different* `west` exit, the paired write is **skipped with a warning** (warn-and-skip; no
  silent overwrite). `Direction.Opposite` supplies the canonical inverse.

---

## Main flow

*Three independent author actions over the same reference index; presented as one numbered trace per action.*

**Delete (with cascade-clear):**
1. Designer clicks **Delete** on a definition (browser row or editor). The Blazor page calls
   `Catalog.Delete(kind, blueprintId)`.
2. The catalog builds the reference index over the on-disk set and finds every referrer of `blueprintId`.
3. For each referrer the catalog rewrites the dangling field (room `AreaId` cleared; exit entry removed; item/mob
   `SpawnRoomBlueprintId` cleared; area `Rooms` entry removed) and writes it via the matching `I*ContentWriter`.
4. The catalog deletes the target YAML file.
5. The catalog returns a `ContentDeleteResult` (deleted path + per-referrer cascade edits); the page renders it.

**Save (warn-but-allow):**
6. Designer saves a definition. `SaveAsync` runs structural `Validate`; on structural failure it returns
   `Failed` and writes nothing (unchanged).
7. On structural pass, the catalog checks the definition's cross-references against the reference index; any
   dangling target becomes a **warning** (non-blocking). The file is written via the `I*ContentWriter`.
8. `SaveAsync` returns `Success` carrying the warnings; the page renders them as non-blocking notices.

**Integrity sweep + bidirectional link:**
9. Designer opens the **Integrity** page; it calls a catalog sweep that returns every broken edge across all
   kinds; the page tabulates them with edit links.
10. On a room save with **bidirectional** enabled, the catalog also writes the inverse exit on the target room
    (`Direction.Opposite`), skipping with a warning on inverse-exit conflict.

---

## Events fired

**None.** This is offline authoring tooling running in the loopback `Hedron.Web` host (and unit tests): there is
no event bus in scope, no heartbeat, no handlers. The catalog returns structured results (`ContentWriteResult`,
`ContentDeleteResult`, the sweep report) to its Blazor callers — consistent with the existing catalog surface,
which already publishes nothing (INV-5; flow-29 invariant note). This is called out explicitly so a reviewer
does not look for a publisher.

---

## Systems / handlers involved

- **`IContentDefinitionCatalog` / `ContentDefinitionCatalog`** (`Core/Modules/Authoring/Systems/`) — gains
  `Delete`, a reference-sweep accessor, warn-but-allow `SaveAsync`, and the bidirectional paired write. Domain
  system; returns results, publishes nothing (INV-5).
- **`IContentReferenceIndex` / `ContentReferenceIndex`** (new, `Core/Modules/Authoring/Systems/`) — declared-edge
  reference model over the on-disk definitions. Pure read; returns structured resolution/referrer/broken-edge
  results.
- **`I*ContentWriter` ×4** (existing) — the cascade rewrites and the bidirectional inverse write reuse these.
- **`IContentValidator`** (existing, World) — unchanged; keeps its structural/registry role. The reference index
  does **not** live here (no disk access by design).
- **Handlers:** none (no event bus in the authoring host).

---

## Design notes

*Durable seam rationale — survives into the shipped feature doc on ship (INV-28).*

- **One reference model, four consumers — keyed by a declared edge, not hardcoded per kind.** The
  four cross-references (room→area, exit→room, item→spawnRoom, mob→spawnRoom) are instances of one
  thing: a typed reference from a (kind, field) to a target kind. Building four bespoke
  detect/cascade paths is the ≥3× smell (INV-19). Instead a declared **edge set** drives a reference
  index computed over the on-disk definitions; broken-link detection, delete-impact, the health
  sweep, and Slice A's filter-association all read the *same* who-points-at-whom data. This is
  "general now," restraint-justified: three-plus live consumers exist the day it ships, so it is not
  speculative.
- **Mechanism vs. consequence — the index holds the graph, each layer owns its policy.** The
  reference index answers only "who points at whom / does this target resolve." *What to do about a
  broken or about-to-dangle link* is policy applied per layer: **save → warn** (non-blocking),
  **delete → cascade-clear**, **health page → report**, **runtime loader → degrade gracefully**
  (already exists). Keeping policy out of the index is exactly what lets one model serve all four
  layers without coupling them.
- **The reference model lives in `Authoring`, beside the catalog — not in `IContentValidator`.** The
  index must read the full on-disk definition set, which the catalog already owns. The existing
  [`IContentValidator`](../../Core/Modules/World/Systems/IContentValidator.cs) is a pure
  registry/live-entity reader (abilities/aspects/effects + live area affinities) and deliberately has
  **no disk access**; making it reach into YAML would muddy that role. So cross-definition reference
  integrity is a **new Authoring capability**; `IContentValidator` keeps its structural/registry
  rules. Two distinct validation concerns, two homes: *structural/registry* (World) vs.
  *cross-definition graph* (Authoring). The runtime **load-time** resolution stays in
  `WorldContentLoader` (World) where it already degrades gracefully — this slice adds the *offline
  authoring-time* surface, not a second runtime path.
- **Delete is a file operation, never an entity destroy.** World content carries no
  `PersistentEntity`; the loopback web host runs no persistence flush. `Delete(kind, blueprintId)`
  removes the YAML file and cascade-rewrites referrer YAML via the existing `I*ContentWriter`s. It
  does **not** call `EntityService.DestroyEntity`, does **not** issue any SQLite delete, and does
  **not** mutate the live world (INV-22/23 hold by construction — applying changes is still a
  separate `reload`).
- **Bidirectional linking is a cross-definition write → catalog-owned, not Blazor.** Writing the
  counterpart exit on a second definition is authoring logic; it lands as a catalog operation (a
  `bidirectional` option on the room save, or an explicit paired-write method — planner's call), with
  the canonical inverse from `Direction.Opposite`. A Blazor component must never write the second
  file itself (INV-8 / Blazor discipline).

---

## Architecture brief

*In-flight; trimmed on ship (INV-28).*

### Placement

- **Reference model + `Delete` + bidirectional write** → `Core/Modules/Authoring/` (catalog family).
- **`Direction.Opposite`** → `Core/Modules/World/` (add only if it does not already exist).
- **Presentation** (delete buttons on all four editor pages + browser rows; integrity/health page;
  bidirectional toggle on the room editor) → `Hedron.Web/`.
- Spine: none — tooling.

### Seams

1. **Cross-definition reference index (build now — the load-bearing seam).** A declared edge set
   `{ (Room, AreaId) → Area; (Room, Exits[dir]) → Room; (Item, spawnRoomId) → Room;
   (Mob, spawnRoomBlueprintId) → Room }` drives a reference index over the catalog's on-disk
   definitions. Exposes: *does this target resolve?* (broken-link detection) and *who references this
   blueprint id?* (delete-impact). Pure read over YAML; returns structured results; publishes nothing
   (no bus in the web host).
2. **`Delete(kind, blueprintId)` with cascade-clear (build now).** New catalog verb. Uses the
   reference index to find referrers, rewrites each to drop the dangling link (rooms lose `AreaId`,
   exits pointing at the room are removed, item/mob spawn rooms cleared), then deletes the target
   file. Returns a result describing the deleted file + every cascade edit so the UI can report it.
   File IO + writer calls only — no entity/SQLite path.
3. **Warn-but-allow save (build now).** `SaveAsync` keeps hard-blocking on *structural* validation
   failure (today's `ValidationReport` errors), but **cross-reference problems become non-blocking
   warnings** — the file still writes. This needs a **warning channel distinct from the blocking
   error list**: `ContentWriteResult`/`ValidationReport` model only hard errors today, so a
   warnings collection is added (small read-model addition — flag for the planner).
4. **Integrity / health page (build now).** A new Blazor page runs a full reference-index sweep and
   lists every broken link across all kinds. The "assurance" surface — offline, before `reload`.
   Presentation over the reference index; no new domain logic of its own.
5. **Bidirectional exit linking (build now).** Room-editor option; on save, also writes the inverse
   exit on the target room via the catalog. Needs `Direction.Opposite` and a **conflict policy**:
   if the target already has a different exit in the inverse direction, surface a warning rather than
   silently overwriting (planner to confirm the exact UX).

### Family / forward-generalization

Already generalized: the declared-edge model **is** the family seam (passes restraint — four edges,
four consumers today). Forward room left open: new content kinds or new edges (e.g. item→item
containment, mob→loot-table) join by **declaring an edge**, not by adding a code path — the
open/closed property to preserve. A possible *further* unification — folding the registry validator
and the reference index behind one validation facade — is **not** in scope (speculative; the two
read different sources). Noted as a design observation, not built.

### Observers / contributors / ordering

None. Offline tooling — no event bus, no contributor ports, no heartbeat. Cascade rewrites multiple
files sequentially; there is no handler-ordering or intra-tick concern (INV-5/6/7 N/A). The one real
sequencing question is *file-write atomicity across a multi-file cascade* — see Deferred.

### Invariants in tension

- **INV-22 / INV-23** — delete must be file-only; **no** `DestroyEntity`, **no** SQLite delete, **no**
  live-world mutation. Called out so the code-review gate confirms it by construction.
- **INV-19** — the declared-edge reference model is the framework that prevents four hand-rolled
  detect/cascade copies. *This is the gap this slice closes.*
- **INV-8 / Blazor discipline** — delete, cascade, bidirectional write, integrity sweep are all
  authoring logic → catalog/reference model; Blazor pages stay presentation-only.
- **INV-16 / INV-29** — new catalog surfaces (`Delete`, reference API, warnings) update
  `docs/reference/systems.md`.
- **INV-18** — delete *is* content tooling; the slice ships it (Content tooling impact section).
- **INV-25** — reference resolution, cascade-clear (each referrer type), warn-not-block on broken
  ref, bidirectional write + conflict, and *delete-touches-no-SQLite* are all system behaviors → unit
  tested. Determinism: no randomness/clock → INV-26 N/A.
- **INV-17** — the implementation PR updates the content-tooling journey
  ([flow-29](../architecture/flows/flow-29-bulk-content-generation.md)) leg B with the delete/cascade,
  integrity-sweep, and warn-but-allow legs (detailed in "Flows introduced or modified").

### Resolved decisions (do not relitigate — all user-confirmed)

- **Delete policy: cascade-clear referrers** (self-healing; no dangling links left behind).
- **Save policy: warn but allow** on broken cross-references; hard structural errors still block.
- **Broken-link surfacing: a dedicated editor integrity/health page** (not log-only).
- **Slicing: this is Slice B** (write + integrity); read-model/presentation is Slice A.

---

## Implementation plan — work packages

> Build order: **WP1 (reference index + `Direction.Opposite`)** → **WP2 (delete cascade + warn-but-allow save
> + bidirectional write on the catalog)** → **WP3 (Blazor: delete buttons, integrity page, bidirectional
> toggle)**. WP2 depends on WP1; WP3 depends on WP2. Primary agent runs `architecture-reviewer` (code mode)
> across the combined diff once all three land.

### WP1 — Reference model + `Direction.Opposite` (Core)

- **Scope:** declared-edge reference index over the on-disk YAML definitions; `Direction.Opposite`.
- **Files:**
  - `Core/Modules/Authoring/Systems/IContentReferenceIndex.cs` + `ContentReferenceIndex.cs` — the declared edge
    set `{ (Room, AreaId)→Area; (Room, Exits[dir])→Room; (Item, SpawnRoomBlueprintId)→Room;
    (Mob, SpawnRoomBlueprintId)→Room }` as data (one `ReferenceEdge` record per edge — source kind, an
    extractor that yields `(field-label, target-kind, target-id)` tuples from a template), plus the index
    methods. Built by loading every definition of each kind via the existing serializer/catalog read path.
  - `Core/Modules/Authoring/ContentReference.cs` (or co-located records) — `ReferenceEdge`, `BrokenReference`
    (source kind/id, field label, missing target id), `ReferrerEdit` (referrer kind/id, field label, action).
  - `Core/Direction.cs` — add `Opposite()` extension (N↔S, E↔W, Up↔Down). **Confirmed absent** — no existing
    `Opposite` on `Direction` (only the enum members).
  - `Core/Modules/Authoring/AuthoringModule.cs` — register `IContentReferenceIndex`.
- **Index API (read-only, no policy):**
  - `bool Resolves(ContentKind targetKind, string targetId)` — does a definition file exist for that id.
  - `IReadOnlyList<ReferrerEdit> Referrers(ContentKind targetKind, string targetId)` — every edge pointing at it,
    described as the cascade-clear edit that *would* drop the link (the same shape delete applies).
  - `IReadOnlyList<BrokenReference> SweepBroken()` — every edge across all kinds whose target does not resolve.
  - `IReadOnlyList<BrokenReference> BrokenFor(IEntityTemplate definition)` — the dangling refs of one in-memory
    definition (the save-warning path).
- **Out of scope:** no delete, no writes, no Blazor, no policy. Pure read returning structured results.
- **Exit criterion:** unit tests prove `Resolves`/`Referrers`/`SweepBroken`/`BrokenFor` over a temp content
  directory for each of the four edge types, and `Direction.Opposite()` is exhaustive over the enum.

### WP2 — Delete cascade, warn-but-allow save, bidirectional write (Core catalog)

- **Scope:** `Delete`, the warnings channel, the warn-but-allow `SaveAsync` change, and the catalog-owned
  bidirectional paired write.
- **Files:**
  - `Core/Modules/Authoring/ContentWriteResult.cs` — **extend with a `Warnings` collection** (resolves the
    warning-channel open question: extend `ContentWriteResult`, *not* `ValidationReport` — `ValidationReport`
    is World-owned and structural-only; cross-reference warnings are an Authoring concern and belong on the
    Authoring result. `Ok`/`Failed` factories gain a warnings-aware overload; existing call sites default to
    empty.)
  - `Core/Modules/Authoring/ContentDeleteResult.cs` (new) — `record(bool Success, string DeletedPath,
    IReadOnlyList<ReferrerEdit> CascadeEdits, IReadOnlyList<string> Errors)`.
  - `Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs` + `ContentDefinitionCatalog.cs`:
    - `Task<ContentDeleteResult> Delete(ContentKind kind, string blueprintId, CancellationToken ct)` — query the
      index for referrers, rewrite each via the matching `I*ContentWriter` (room `AreaId` cleared; exit entry
      removed; item/mob `SpawnRoomBlueprintId` cleared; area `Rooms` entry removed), then `File.Delete` the
      target. **No `EntityService`, no SQLite, no `SaveEntityAsync`.** Best-effort sequential cascade (not
      transactional — see backlog).
    - `SaveAsync` — after the structural `Validate` pass, call `_referenceIndex.BrokenFor(definition)`; fold any
      dangling refs into `ContentWriteResult.Warnings` and **still write the file**. Structural failure path
      unchanged (returns `Failed`, no write).
    - `Task<ContentWriteResult> SaveRoomAsync(RoomTemplate room, bool bidirectional, CancellationToken ct)`
      (or a `bidirectional` flag threaded onto the room save) — after writing the room, for each exit write the
      inverse on the target room via `_roomWriter`, using `Direction.Opposite`. **Conflict policy: warn-and-skip**
      (resolves the bidirectional open question) — if the target room already has a *different* exit in the
      inverse direction, skip that paired write and add a warning; do not overwrite. (Self-loops and already-correct
      inverses are no-ops.)
  - `Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs` ctor — inject `IContentReferenceIndex`.
- **Out of scope:** Blazor; transactional/atomic multi-file cascade (deferred — backlog
  "🔵 Atomic multi-file content cascade").
- **Exit criterion:** unit tests (see Test plan) prove each referrer-type cascade-clear, delete touches no
  SQLite/entity path, warn-not-block on broken ref, and the bidirectional write + conflict-skip.

### WP3 — Blazor surfaces (Hedron.Web)

- **Scope:** delete buttons on all four editor/browser surfaces; the integrity/health page; the bidirectional
  toggle on the room editor; rendering save warnings.
- **Files:**
  - `Hedron.Web/Components/Pages/Browser.razor` — a **Delete** action per row + confirm; calls `Catalog.Delete`,
    renders the `ContentDeleteResult` cascade summary, reloads the list.
  - `Hedron.Web/Components/Pages/{Area,Room,Item,Mob}Editor.razor` — a **Delete** button (confirm → `Catalog.Delete`
    → navigate back); render `ContentWriteResult.Warnings` as a non-blocking notice block on save.
  - `Hedron.Web/Components/Pages/RoomEditor.razor` — a **"Write inverse exits"** checkbox bound to the
    `bidirectional` save flag.
  - `Hedron.Web/Components/Pages/Integrity.razor` (new, e.g. `@page "/integrity"`) — calls the catalog sweep,
    tabulates broken edges with edit links; linked from the browser nav.
- **Out of scope:** no authoring logic in any component — every write/delete/sweep goes through the catalog
  (INV-8).
- **Exit criterion:** manual smoke in the loopback host (delete cascades + reloads, warnings render, integrity
  page lists broken links, bidirectional checkbox writes the inverse). Blazor presentation is not unit-tested
  (see Test plan "skipped").

---

## Content tooling impact

*Required (INV-18). This slice **is** content tooling — the entire deliverable is authoring surface.*

- **Delete** — new authoring capability: `Catalog.Delete(kind, id)` plus delete buttons on all four editor pages
  and the browser. Deleting a definition is now a first-class, cascade-safe author action (was previously
  impossible from the editor).
- **Integrity/health page** — new inspection surface: a full broken-link sweep, the "is my content consistent?"
  view authors run before `reload`.
- **Save warnings** — authors now see non-blocking broken-cross-reference notices inline on save.
- **Bidirectional exit linking** — author convenience: one room save optionally writes both halves of a passage.
- **No new data-file shape, no `TemplateRegistry` entry, no admin command** — this operates over the *existing*
  YAML shapes (area/room/item/mob) via the existing serializer/writers; the catalog's new verbs are the tooling.
- **Reference-catalog updates (INV-16/29):** `docs/reference/systems.md` gains `IContentReferenceIndex` and the
  new `IContentDefinitionCatalog` members (`Delete`, sweep accessor, bidirectional save, warnings). `Direction.Opposite`
  is a small Core helper (not a catalog row).

---

## Cross-cutting surfaces stressed

*Required (INV-19). Ground-rule-9 audit.*

| Surface | Disposition | Rationale |
|---|---|---|
| **Cross-definition reference integrity** | **Gap exposed → closed in-slice** | Four cross-references (room→area, exit→room, item→room, mob→room) would otherwise be four hand-rolled detect/cascade copies (the ≥3× smell). The declared-edge `IContentReferenceIndex` **is** the framework that closes the gap; it lands in WP1, ahead of its consumers (delete, save-warn, sweep, and Slice A's filter-association). This is the load-bearing seam of the slice. |
| **Result/warning model** (`ContentWriteResult`) | **Gap exposed → closed in-slice** | Today's result models only blocking errors; warn-but-allow needs a non-blocking channel. Extending `ContentWriteResult` with a `Warnings` list (rather than a parallel result type) closes it without a new type. Landed in WP2. |
| **Content writers** (`I*ContentWriter`) | **Adequate** | Cascade rewrites and the bidirectional inverse write reuse the existing per-kind writers (atomic tmp→rename each). No new writer surface. |
| **`Direction`** | **Adequate after a 1-line add** | `Opposite()` is a pure, total helper on the existing enum — confirmed absent, added in WP1. Not a framework gap. |
| **Persistence** | **Adequate (N/A by construction)** | World content carries no `PersistentEntity`; the web host runs no persistence flush. Delete is YAML-file-only. See the persistence opt-in audit below. |
| **Event bus / handlers / heartbeat** | **Adequate (N/A)** | Offline tooling; no bus in the authoring host. Catalog returns results (INV-5). Stated explicitly so the gap is a deliberate non-finding, not an omission. |
| **Multi-file cascade atomicity** | **Acknowledged debt** | The cascade is best-effort sequential (each write atomic, the *set* not transactional). Rationale + fix trigger already tracked: backlog "🔵 Atomic multi-file content cascade." Not re-planned here. |
| **Blazor presentation** | **Adequate** | New pages/buttons are thin callers of the catalog; no authoring logic crosses into a component (INV-8). |

### Persistence opt-in audit (INV-22/23 — mandatory)

- **Level 1 — entity domain:** this slice constructs **no entities at all.** It operates exclusively over
  on-disk YAML definitions (world-content domain — fresh-spawned from YAML on startup, never carrying
  `PersistentEntity`, no SQLite row). Delete removes a YAML file; it does **not** classify, construct, destroy,
  or transition any entity. No domain transition exists in this slice.
- **Level 2 — components:** **no component is introduced or touched.** The new types (`ContentReferenceIndex`,
  result records, `ReferenceEdge`) are plain systems/data, not ECS components — no `[Persistent]` decision applies.
- **Level 3 — save-on-change:** **no `SaveEntityAsync` call exists anywhere in this slice.** Delete is a `File.Delete`
  + writer rewrites; save is a writer write. None of the three permitted `SaveEntityAsync` cases (construction,
  admin boundary save, session-end force-save) is reachable — there is no persistent entity and no SQLite path.
  This is the single most important invariant for the slice and the Test plan asserts it directly.

---

## Flows introduced or modified

*Required (INV-17).*

- **[flow-29 — Content-Tooling Journey](../architecture/flows/flow-29-bulk-content-generation.md), leg B (Offline
  Blazor editor):** gains a **delete + cascade leg** and an **integrity-sweep leg**, and the save leg's "valid →
  write" branch gains the **warn-but-allow** sub-path (file still writes; warnings returned). The implementation
  PR updates flow-29 leg B's sequence + steps:
  - new author action: `UI → Cat.Delete(kind, id)` → index lookup → per-referrer `WriteAsync` → `File.Delete` →
    `ContentDeleteResult`; note "no `EntityService`/SQLite" beside the existing INV-12/23 invariant line.
  - new author action: `UI → Cat.<sweep>` → `ContentReferenceIndex.SweepBroken` → broken-edge list (integrity page).
  - save `alt valid` branch annotated: cross-ref dangling → `ContentWriteResult` with `Warnings`, file still written.
  - bidirectional room save: `Cat → I RoomContentWriter` second write (inverse exit), conflict → warn-and-skip.
- **No new top-level flow file** — these are legs on the existing content-tooling journey, not a new journey.
  flow-29's invariant list is extended with the delete-is-file-only and warn-but-allow notes.

---

## Test plan / Verification

*Required (INV-25). Derived from Postconditions + Main flow per the rubric in
[`../architecture/07-testing.md`](../architecture/07-testing.md). No randomness/clock → INV-26 N/A; no injected-seam
gap.*

**System-unit — reference index (`ContentReferenceIndex`, over a temp content dir):**
- `Resolves` true for a present target, false for an absent one — per edge target-kind (Area, Room).
- `Referrers(Room, X)` returns each referrer-type edge pointing at room X: a room with `AreaId`/exit→X is *not*
  matched as an area-target, an item and a mob with `SpawnRoomBlueprintId == X` *are* matched, a room with
  `Exits[dir] == X` *is* matched. (One test per referrer type — exercises every declared edge.)
- `SweepBroken` enumerates exactly the dangling edges across a mixed fixture (one broken per kind).
- `BrokenFor(definition)` returns the in-memory definition's dangling refs (the save-warn input).
- `Direction.Opposite()` is total and correct over all six members (architecture/data-completeness guard).

**System-unit — catalog (`ContentDefinitionCatalog`, over a temp content dir):**
- **Delete cascade-clear, one test per referrer type:** deleting room X clears `AreaId` on a referring room /
  removes the exit entry on a referring room / clears `SpawnRoomBlueprintId` on a referring item / clears it on a
  referring mob / removes X from a referring area's `Rooms`; the target file is gone; `ContentDeleteResult`
  enumerates each edit.
- **Delete touches no SQLite / no entity path (explicit, per the spec):** delete runs with **no `EntityService`
  and no SQLite/persistence dependency wired** (the catalog has none to begin with) — asserted structurally (the
  catalog's constructor takes no `EntityService`/persistence port) and behaviorally (only `File.Delete` + writer
  calls occur; a fixture with a sentinel persistence/entity spy is never invoked). This is the load-bearing
  INV-22/23 assertion.
- **Warn-but-allow save:** saving a structurally-valid room whose `AreaId` does not resolve → result `Success`,
  `Warnings` non-empty naming the broken ref, **and the YAML file is written** (assert file present + content).
- **Structural failure still blocks:** a structurally-invalid definition → `Failed`, no file written (regression
  guard on existing behavior).
- **Bidirectional write:** saving room A `east → B` with bidirectional on writes B's `west → A` (assert B's file).
- **Bidirectional conflict → warn-and-skip:** if B already has `west → C`, A's save does **not** overwrite B's
  west; a warning is returned; B's file is unchanged.
- **Bidirectional already-correct inverse → silent no-op:** saving room A `east → B` with bidirectional on when B
  already has the correct `west → A` produces **no warning and no spurious rewrite** of B (the already-correct
  inverse is not mis-flagged as a conflict; self-loops handled the same way).

**Skipped (with reason):**
- **Blazor pages** (Browser/editors/Integrity) — presentation; thin catalog callers, no logic. Per the testing
  rubric, UI plumbing and exact prose are not unit-tested; covered by manual loopback smoke.
- **Result/record types** (`ContentDeleteResult`, `ContentWriteResult.Warnings`) — pure data; exercised
  transitively by the catalog tests, no dedicated test.
- **`I*ContentWriter` round-trip** — existing, already covered; this slice reuses them unchanged.

---

## Open questions

*All four seed policy forks are user-confirmed (see Resolved decisions). The seed's three planner open questions
are now resolved in-plan:*

- **Warning channel shape** — **Resolved:** extend `ContentWriteResult` with a `Warnings` list (Authoring-owned),
  **not** `ValidationReport` (World-owned, structural-only). Rationale in WP2.
- **Bidirectional conflict UX** — **Resolved:** warn-and-skip (no silent overwrite, no blocking). Confirm at the
  spec gate.
- **Cascade reporting granularity** — **Resolved:** the `ContentDeleteResult` carries the **per-referrer edit list**
  (`ReferrerEdit[]`), not just a count — cheap, and the integrity story benefits from the detail. Presentation may
  collapse it to a summary line.

*No remaining blocking questions.*

- **Bidirectional symmetry on the integrity page — Resolved (spec gate, accepted default):** the integrity page
  reports *resolution* (broken target), **not** *symmetry* (a same-direction inverse mismatch). Bidirectional
  conflicts surface as a save-time warning only. If symmetry surfacing is wanted later it is a purely additive
  sweep over the same reference index — no model change — so deferring it forecloses nothing.

## Related

- [Content Editor — filters & readability (Slice A)](content-editor-filters-and-readability.md) — the read-model
  this slice builds on.
- [`flow-29` — Content-Tooling Journey](../architecture/flows/flow-29-bulk-content-generation.md) — the flow this
  slice extends (leg B).
- [`docs/features/world/world-content.md`](../features/world/world-content.md) — `WorldContentLoader`'s runtime
  link phases already degrade gracefully on broken refs; this slice adds the *offline* authoring-time surface, not
  a second runtime path.
- [`docs/architecture/06-persistence.md`](../architecture/06-persistence.md) — the two-domain model (INV-22/23)
  that makes delete file-only by construction.
- Backlog: **🔵 Atomic multi-file content cascade** — the deferred transactional-cascade concern referenced here.
