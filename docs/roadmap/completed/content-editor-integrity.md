# Content-editor area filters, referential integrity & delete (completed)

> Implemented on branch `claude/goofy-ride-54525e`, 2026-06-20. Living docs: [`content-authoring.md`](../../features/admin-authoring/content-authoring.md) (Blazor editor) · [`content-tooling.md`](../../features/admin-authoring/content-tooling.md) (catalog + reference index). Advisor-initiated, off the numbered queue. Shipped as two slices (A read-only, B write/integrity) in one PR.

## Outcome

The offline Blazor content editor gained area filtering, lookup-driven selection fields, a dark-readable theme, delete operations, and cross-definition referential integrity. Listing pages filter rooms/mobs/items by area (unselectable back to "all"); editors offer an area picker, a filterable exit room-lookup, and cascading area→room spawn pickers. A new declared-edge **reference model** (`IContentReferenceIndex`) backs three new behaviors: `DeleteAsync` cascade-clears every referrer (YAML-file-only — no live world / no SQLite), `SaveAsync` warns-but-allows on broken cross-references, and an Integrity page sweeps and reports every broken link. Bidirectional exit linking writes the inverse exit on the target room (warn-and-skip on conflict). Build + suite green at 687/687.

## Behavior digest

*As-specified snapshot from the two (now-deleted) implementation plans. Present-truth lives in the feature docs + flow-29.*

**Slice A — filters, selectors, readability (read-only):**
- The catalog exposes, per room/item/mob definition, the **area blueprint id** it belongs to: room → its own `AreaId` (one hop); item/mob → `SpawnRoomBlueprintId` → that room's `AreaId` (two hops); blank/missing/dangling → `null`, never a throw. Resolution lives only in `Core/Modules/Authoring/` — no Blazor component resolves a cross-reference.
- Listing pages render an area filter defaulting to "all", always returnable to "all"; selecting an area hides non-matching rows. Room editor: area picker (writes `AreaId`) + per-direction filterable exit room-lookup (writes a blueprint id into `Exits`, no counterpart write). Item/Mob editors: cascading area→room picker writes `SpawnRoomBlueprintId`.
- Stylesheet: lighter foreground text on dark backgrounds, dark-blue unselected buttons, white-on-blue selected. No content-mutation path or save behavior changed.

**Slice B — reference integrity, delete, bidirectional linking:**
- The reference index resolves a declared edge set over the on-disk definitions and answers *does target T resolve?* and *who references id B?*. A reference whose target file is absent is reported broken.
- `DeleteAsync(kind, id)` removes exactly one YAML file and cascade-clears every referrer: referring room `AreaId` cleared; exit entry pointing at the id removed; item/mob `SpawnRoomBlueprintId` cleared; area `Rooms` entry removed. The result enumerates the deleted file + each cascade edit. **No SQLite row, live entity, or live-world state is touched** (INV-22/23 by construction).
- `SaveAsync` warns-but-allows on a dangling cross-reference (file written, `Warnings` populated) but still hard-blocks structural-validation failure (no file written). The Integrity page lists every broken link across all kinds.
- Bidirectional room save also writes the inverse exit (`Direction.Opposite`); a *different* existing inverse exit → warn-and-skip; an already-correct inverse or self-loop → silent no-op.

## Shipped pieces

| Surface | Location |
|---|---|
| `ContentSummary.AreaBlueprintId` — resolved area projection (optional) | `Core/Modules/Authoring/ContentSummary.cs` |
| `ResolveAreaBlueprintId` (one resolver, field-keyed) + `RoomsInArea` query + per-`List` room→area map | `Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs` |
| `DeleteAsync` (cascade-clear, file-only) + `SaveRoomAsync(bidirectional)` + warn-but-allow `SaveAsync` | `Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs` + `IContentDefinitionCatalog.cs` |
| `IContentReferenceIndex` / `ContentReferenceIndex` — declared-edge model (5 edges) | `Core/Modules/Authoring/Systems/IContentReferenceIndex.cs`, `ContentReferenceIndex.cs` |
| `ReferenceEdge` / `BrokenReference` / `ReferrerEdit` records | `Core/Modules/Authoring/ContentReference.cs` |
| `ContentDeleteResult` record | `Core/Modules/Authoring/ContentDeleteResult.cs` |
| `ContentWriteResult.Warnings` + `OkWithWarnings` factory | `Core/Modules/Authoring/ContentWriteResult.cs` |
| `Direction.Opposite()` (`DirectionExtensions`) | `Core/Direction.cs` |
| `IContentReferenceIndex` DI registration | `Core/Modules/Authoring/AuthoringModule.cs` |
| Area filters, area/exit/spawn pickers, dark theme | `Hedron.Web/Components/Pages/{Browser,RoomEditor,ItemEditor,MobEditor}.razor`, `wwwroot/app.css` |
| Delete buttons (two-step confirm) + warning rendering | `Hedron.Web/Components/Pages/{Browser,RoomEditor,ItemEditor,MobEditor,AreaEditor}.razor` |
| `Integrity.razor` broken-link sweep page + nav link | `Hedron.Web/Components/Pages/Integrity.razor`, `Components/Layout/MainLayout.razor` |

## Tests shipped

Per both plans' Test plans (INV-25), all system-unit over a temp content directory; `dotnet test` green at **687/687** (+60 vs. the 627 baseline). No randomness/clock introduced → INV-26 N/A.

- **`ContentDefinitionCatalogTests`** — one/two-hop area resolution; blank/missing/dangling → `null` no-throw; `RoomsInArea` match/exclude/unknown; per-referrer cascade-clear (room `AreaId`, exit entry, item spawn, mob spawn, area `Rooms`); **delete-touches-no-SQLite** (structural: no `EntityService`/persistence ctor dependency; behavioral: only file/writer ops); warn-but-allow writes file + populates `Warnings`; structural failure still blocks; bidirectional write, conflict→warn-and-skip, already-correct→silent no-op, self-loop no-op.
- **`ContentReferenceIndexTests`** — `Resolves`/`Referrers`/`SweepBroken`/`BrokenFor` per declared edge (incl. the `(Area, Rooms[]) → Room` edge); `Direction.Opposite` totality + self-inverse.

## Decisions

- **Two slices, one PR.** Slice A (read-only read-model + presentation) was kept separate from Slice B (cross-definition writes + integrity) so the seam work was not held hostage to CSS, and so A's catalog read-model could land before B folded it into the reference index.
- **Area resolution is catalog-owned, computed once.** The room/item/mob → area projection (a two-hop resolution for item/mob) serves five surfaces (3 filters + 2 cascade pickers) — past the INV-19 ≥3× bar — so it lives once in the catalog, keyed by reference field (not "area" hardcoded), never in a Blazor component. This kept Blazor presentation-only and let Slice B subsume it.
- **One declared-edge reference model, four consumers; mechanism vs. policy separated.** A `ReferenceEdge` set (5 edges) drives one index; broken-link detection, delete-impact, the integrity sweep, and the save-warn input all read the same who-points-at-whom data. The index answers only *does it resolve / who refers*; **policy** is applied per layer — save → warn, delete → cascade-clear, integrity page → report, runtime loader → degrade (pre-existing). Adding an edge is a one-line declaration, no new code path.
- **Reference index lives in `Authoring`, not `IContentValidator` (World).** The index must read the on-disk definition set, which the catalog owns; `IContentValidator` is a pure registry/live-entity reader and was kept disk-free. Two validation concerns, two homes: structural/registry (World) vs. cross-definition graph (Authoring).
- **Delete is a file operation, never an entity destroy.** World content carries no `PersistentEntity` and the loopback web host runs no persistence; `DeleteAsync` is `File.Delete` + writer rewrites only. The catalog gained no `EntityService`/persistence dependency — asserted structurally and behaviorally in tests (the load-bearing INV-22/23 guard).
- **Warnings are Authoring-owned.** Warn-but-allow extends `ContentWriteResult.Warnings`, not the World-owned `ValidationReport` (structural-only). Forward-references are normal mid-authoring, so cross-ref misses warn rather than block.
- **Bidirectional conflict = warn-and-skip; already-correct = silent no-op.** No silent overwrite of a target's existing exit; an already-correct inverse is not mis-flagged.
- **Fifth edge `(Area, Rooms[]) → Room` added during WP2.** The plan's declared edge set listed four, but the cascade postcondition required removing a deleted room from a referring area's `Rooms` list — modeled as a full declared edge (also surfaced by the integrity sweep), keeping the framework uniform.
- **Integrity-page symmetry deferred.** The page reports broken *resolution*, not exit *symmetry* (a same-direction inverse mismatch); symmetry surfacing would be a purely additive sweep over the same index, foreclosed by nothing.

## Deviations / Follow-ups

- **Deviation:** WP2 added a 9-arg "forwarding" constructor on `ContentDefinitionCatalog` that constructs its own `ContentReferenceIndex` (for manually-constructed test sites) alongside the DI-injected 10-arg primary. The code-review gate adjudicated it an acceptable test-convenience overload — production always binds the DI primary; no `EntityService`/persistence dependency added. Optional future simplification: have the test helper construct a real index and call the single primary ctor.
- **Debt parked:** [🔵 Atomic multi-file content cascade](../backlog.md) — `DeleteAsync` cascade rewrites are best-effort (each writer write atomic tmp→rename, the *set* not transactional). A mid-cascade failure can leave a partially-cascaded state; the Integrity page surfaces any resulting broken link on the next sweep. Full transactional cascade lands if/when multi-author or large content sets make a partial cascade hard to recover.
- **Pre-existing fix:** corrected a stale "Flow 30" reference in `08-blazor.md` to the offline-edit leg of Flow 29.
