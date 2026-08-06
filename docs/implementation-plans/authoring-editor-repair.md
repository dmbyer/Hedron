# Implementation Plan: Authoring editor repair (catalog cache + UX ratchet)

**Status:** planned
**Actors:** Administrator (content author)
**Module:** `Core/Modules/Authoring/` · Feature: [`admin-authoring`](../features/admin-authoring/admin-authoring.md)

## Description

Make the offline authoring editor fast and safe enough to author Phase 5's content baseline against,
without prejudging the client-tier decision ([`../design/client-tier.md`](../design/client-tier.md)).
Two independent problems: `IContentDefinitionCatalog` re-reads and re-deserializes the entire content
corpus on every `List`/`RoomsInArea` call — and the editor calls it from inside render loops — and the
four content editors silently discard an in-progress form when the author edits the blueprint id.
Everything here is framework-independent: a React port would inherit the same cache.

## Preconditions

- [`IContentDefinitionCatalog`](../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs) and
  [`ContentDefinitionCatalog`](../../Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs) exist and
  are the **editor's** sole authoring read/write path.
- **The catalog is not the only writer of content YAML.** `ContentGenerationSystem` and the eight
  `mk*`/`set*`/`dig` commands write through the `I*ContentWriter` family directly, bypassing the
  catalog. This is load-bearing for the cache design — see WP1's invalidation scope and out-of-scope
  bounds — and was mis-stated in this plan's first draft.

## Postconditions

1. Repeated `List(kind)` / `RoomsInArea(id)` calls with no intervening catalog write perform **no**
   filesystem reads after the first. *(Invisible state → tested, `INV-25`.)*
2. Any catalog mutation (`SaveAsync`, `SaveRoomAsync`, `CreateAsync`, `DeleteAsync`, `RenameAsync`,
   `RemoveRoomExitAsync`) invalidates the **whole index**, so a subsequent read observes both the
   target write and every definition its cascade touched. *(Invisible state → tested.)*
3. Each kind's summary is swept **at most once per invalidation**, not once per render.
   *(The first revision claimed "at most one sweep for N kinds," which the design does not deliver —
   each kind has its own directory. Restated to what is true and observable.)*
4. Changing the blueprint id on a New form preserves every other authored field on the definition.
5. The integrity sweep runs off the render thread and exposes observable in-progress state.

## Main Flow

*(Authoring edit loop — the offline-edit leg of Flow 29.)*

1. Author opens the content browser; the page reads counts **once** into local state rather than
   calling the catalog per rendered kind.
2. Catalog serves the read from its in-memory index; on a cold index it populates from disk once.
3. Author opens an editor, edits fields, and (on a New form) sets a deliberate blueprint id — the
   catalog returns the same definition carrying the new id, not a fresh default.
4. Author saves; the catalog validates, writes YAML, and invalidates the index.
5. The next read observes the write and its cascade.
6. If content was written **out of process** (a `generate` CLI run against the same directory while
   the host is up), the author uses the browser's explicit refresh action to drop the index.

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| *(none)* | — | — | Authoring is off the bus; the catalog is a domain system and never publishes (`INV-5`). The apply leg's `ContentReloadedEvent` is unchanged and out of scope. |

## Systems / handlers involved

- [`IContentDefinitionCatalog`](../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs) — gains the index, `WithBlueprintId`, `CreateNextFrom`, and an explicit `Invalidate()`.
- [`IContentReferenceIndex`](../../Core/Modules/Authoring/Systems/IContentReferenceIndex.cs) — does its **own** `Directory.GetFiles`/`File.ReadAllText` and does **not** read through the catalog; it gains no speedup here. Listed to correct the first draft's claim.
- [`IBalanceAuditSystem`](../../Core/Modules/BalanceInspection/Systems/BalanceAuditSystem.cs) — depends on `ITemplateRegistry`, not the catalog; also gains no speedup. The Integrity page is therefore slow for reasons WP1 does not address; WP2(e) makes it non-blocking rather than fast.
- No handlers. No live-world touch (`INV-12`, `INV-22`, `INV-23`).

## Implementation plan — work packages

### WP1 — Catalog index and render hot paths

**Scope.** Add an in-memory index inside `ContentDefinitionCatalog`: a per-kind summary list and a
per-id definition map. Keep it in the catalog rather than in a decorator — the invalidation points are
the catalog's own write methods.

**Invalidation is whole-index, not entry-scoped.** `DeleteAsync` clears `AreaId`/exits on other
definitions, `RenameAsync` rewrites every referrer, `SaveRoomAsync(bidirectional: true)` writes an
inverse exit on a *different* room, and `List`/`RoomsInArea` are backed by a derived room→area map.
Entry-scoped invalidation cannot express those cascades correctly; dropping the whole index is the
only safe rule at this content scale.

**Population granularity is what keeps that affordable.** The **per-id definition map fills per id on
demand** (one file read); only the per-kind summary list and the room→area map are corpus sweeps.
This is load-bearing, not an optimization detail: `TemplateConformanceSystem.ApplyFlaggedAsync` loops
`Load` → `SaveAsync` per drifted entry, and `IAreaLayoutSystem.ApplyProposalAsync` does per-room
best-effort writes. With corpus-populated per-id caching, whole-index invalidation would turn both
into N full sweeps — quadratic, on the very Integrity and grid pages this slice is meant to improve.
With per-id-on-demand population, a `Load` after invalidation is still one file read and both loops
stay O(N). The code gate should check this interaction explicitly.

**Concurrency posture (`INV-31`).** The index is mutable state on a DI singleton reached concurrently
from **multiple Blazor circuits** (and later from the sibling slice's request threads). *Not* from
WP2(e)'s sweep — that reads `IContentReferenceIndex`/`IBalanceAuditSystem`, neither of which touches
the catalog index; an earlier draft cited it, and a later reader could have correctly refuted that
justification and dropped the guard.

Every mutator is `async` and invalidates *after* an awaited file write, so a thread-affine
`ReaderWriterLockSlim` **must not** be used — it cannot be held across an `await`. Use an **immutable
snapshot swapped under a plain `lock`**: readers take the current snapshot reference with no lock,
writers build and swap.

**Population is lazy, which makes every reader a writer — so the swap needs a generation counter.**
Without one there is a lost-invalidation race: reader R begins a corpus sweep at T0 against an empty
snapshot; a circuit-thread `SaveAsync` completes at T1, writes YAML and invalidates (a no-op — the
snapshot is already empty); R completes at T2 and publishes a snapshot built from **pre-T1** disk
state, leaving the index permanently stale until the next write and violating Postcondition 2.
Single-threaded invalidation tests cannot catch it. Required shape: `Invalidate()` does
`lock { _generation++; _snapshot = null; }`; a populating reader captures `gen` before its sweep and
publishes only under `lock { if (_generation == gen) _snapshot = built; }`.

The guard covers **index consistency only** — it does not make YAML writes atomic, and the
non-transactional multi-file cascade recorded in [`../roadmap/backlog.md`](../roadmap/backlog.md) is
unchanged by this slice. No live-world component is touched, so the acknowledged `ComponentRepository`
exposure is not widened.

**Files.** `Core/Modules/Authoring/Systems/{IContentDefinitionCatalog,ContentDefinitionCatalog}.cs`;
the counting filesystem seam (below); `Hedron.Web/Components/Pages/Browser.razor` (hoist `CountFor`
out of the render loop into `Reload()`, add the explicit refresh action);
`Hedron.Web/Components/Pages/AreaGridEditor.razor` (hoist the repeated `Bounds()` evaluation).

**The counting filesystem seam — a deliberately catalog-scoped test port, not a general filesystem
abstraction.** Stated explicitly so the next slice does not hand-roll a second one (`INV-19`): the
authoring module already has three filesystem styles (`ContentReferenceIndex` reads directly, the
`I*ContentWriter` family writes, this seam wraps catalog reads). It is an infrastructure **port**, so
it gets no `reference/systems.md` row (`INV-16`/`INV-29` — note the exclusion rather than leaving it
ambiguous), but it does need DI registration in `AuthoringModule` to satisfy the DI-smoke guard.
`ContentDefinitionCatalog` has **two** public constructors and is constructed directly by
`Hedron.Tests/Simulation/SimulationTestFixtures.cs`, `Authoring/AreaLayoutSystemTests.cs`, and
`Modules/BalanceInspection/TemplateConformanceSystemTests.cs`; adding a parameter breaks those call
sites, so the seam ships with a defaulted/overloaded ctor or those three fixtures are updated in the
same PR.

**Dependencies.** None.

**Out of scope — and the honest rationale.** No `FileSystemWatcher`. The first draft justified this
with "the authoring host is the only writer," which is **false**: the `generate` CLI is a separate
process writing the same directory (Flow 29 leg A), and the telnet `mk*`/`set*` verbs write through
the `I*ContentWriter` family in the game host. A long-running editor host will therefore serve stale
lists after an out-of-process write. Mitigation in this slice is the explicit `Invalidate()` + browser
refresh action (Main Flow step 6); the residual staleness is **acknowledged debt** and gets a
`backlog.md` entry in the same PR (`INV-19`). A watcher is the eventual answer and carries its own
`INV-31` posture.

Also out of scope: the `Standards.razor` per-cell oracle hoist. The first draft claimed it here, but
that site is one of the `INV-8` extractions [`authoring-api-surface.md`](authoring-api-surface.md) WP1
owns, and optimizing it in place would be discarded when the extraction lands. **Hard ordering: that
slice's WP1 lands before anyone touches `Standards.razor`.**

**Merge coordination on shared files.** Both slices edit `AreaGridEditor.razor` (this one hoists
`Bounds()` and adds `@key`; the sibling rewrites the connect path) and both edit Flow 29 (this one
leg B/B3/B4 read semantics; the sibling leg B participants). The edits are disjoint and no invariant
is at risk, but **this slice lands its `AreaGridEditor` and Flow 29 edits first** so the sibling
rebases onto them rather than colliding.

**Exit criterion.** A cache-hit test, a whole-index invalidation test per mutating method, and a
cascade test (below) all green.

### WP2 — Editor UX ratchet

**Scope.**
(a) Add `ContentDefinition WithBlueprintId(ContentDefinition definition, string? blueprintId)` to
`IContentDefinitionCatalog` — returns the definition with only its id replaced, blank falling back to
a freshly minted ad-hoc id. **Reuse the catalog's existing private `CloneWithNewId`**, which already
encodes the self-referential-id rewrite rule `RenameAsync` uses; do not ship a second id-rewrite rule
(`INV-19`). The four editors' `OnBlueprintIdChanged` call it instead of `CreateNew`.
(b) Add `CreateNextFrom(ContentDefinition previous, string name)` to the catalog for the
"save and create next" flow. Which fields carry forward is **authoring policy plus kind-dispatch**,
which [`08-blazor.md`](../architecture/08-blazor.md) forbids in a component — so it lives on the
catalog with a test, not in the editors. It **delegates to `CreateNew(kind, name)` for id minting**,
so the slice adds no third id-minting path. The policy, resolved here because
[`README.md`](README.md) forbids unresolved decisions in a merged plan:

| Kind | Carried forward | Reset |
|---|---|---|
| Area | *(nothing — areas are authored individually)* | all fields |
| Room | `AreaId` | name, description, exits, coordinates |
| Item | Tier, Band, item type/slot | name, description, stat bonuses, value |
| Mob | Tier, Band, `SpawnRoomBlueprintId` | name, description, attributes, pools, loot, shop config |
(c) `@key` on the row/cell loops in `Browser`, `MobEditor`, `AreaGridEditor`, `Integrity`.
(d) Debounce the six per-direction filter inputs in `RoomExitsEditor`.
(e) Move the `Integrity` sweep off the render thread into a `Hedron.Web/Services/` type exposing a
status snapshot — the same shape as `SimulationRunService`, and testable in `Hedron.Tests/Web/`
(the tier [`07-testing.md`](../architecture/07-testing.md) already sanctions for non-presentation
web-host logic). **On the recorded promotion trigger:** [`08-blazor.md`](../architecture/08-blazor.md)
and `backlog.md` say a *second* long-running editor job wanting queue/progress/cancel should
generalize `SimulationRunService` rather than hand-roll a second one. The sweep is **progress-only —
no queue, no cancellation** — so it does not meet that shape and does not fire the trigger. The
recorded candidate remains the bulk conformance apply (`PreviewAllFlagged`/`ApplyAllFlagged`) — which
**still runs blocking on the circuit thread in the very page this item touches**, and stays that way
after this slice. That is the thing that fires the trigger; it is deliberately out of scope here.
State both halves at the code gate rather than letting the reviewer rediscover them.
(f) Keyboard-first authoring: autofocus the first field, Enter-to-save / Esc-to-cancel.

**Concurrency posture (`INV-31`) for (e).** The sweep runs on a background `Task`; its status record
is owned by the service and read by the circuit thread via a snapshot method; re-entry to the UI is
via `InvokeAsync(StateHasChanged)`. It reads `IContentReferenceIndex`/`IBalanceAuditSystem` and
mutates no live-world component.

**Files.** `Core/Modules/Authoring/Systems/{IContentDefinitionCatalog,ContentDefinitionCatalog}.cs`;
`Hedron.Web/Services/` (the sweep service); `Hedron.Web/Components/Pages/{Mob,Item,Area,Room}Editor.razor`,
`Integrity.razor`, `Browser.razor`, `AreaGridEditor.razor`; `Hedron.Web/Components/Shared/RoomExitsEditor.razor`.

**Dependencies.** Independent of WP1; land either order.

**Out of scope.** `EditForm`/`DataAnnotationsValidator` adoption and any JS-interop grid island — both
are Blazor investments the client-tier gate could strand; they are the no-go branch's follow-on work
in [`../design/client-tier.md`](../design/client-tier.md#if-no-go).

*The primary agent runs `architecture-reviewer` (code mode) across the combined WP1+WP2 diff.*

## Content tooling impact (INV-18)

No new gameplay state, so no new authoring surface is owed. This slice **is** content tooling — it
repairs the surface Phase 5 depends on. No data-file shape changes, no new admin command, no
`TemplateRegistry` entry.

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification |
|---|---|
| `IContentDefinitionCatalog` read path | **Adequate** — the index lands behind the existing interface; the editor benefits with no call-site change. Note the `generate` CLI and telnet verbs use the writers, not the catalog, so they neither benefit nor invalidate. |
| Concurrency posture (`INV-31`) | **Gap exposed → resolved in WP1 and WP2(e).** Both postures are specified above, including the no-lock-across-`await` constraint that made the first draft's `ReaderWriterLockSlim` unimplementable. |
| Cross-process cache coherence | **Acknowledged debt** — out-of-process writes (the `generate` CLI, the game host's `mk*` verbs) do not invalidate the editor's index. Mitigated by an explicit refresh; `backlog.md` entry added in the same PR. |
| Web-host background jobs | **Adequate** — WP2(e) reuses the established `Hedron.Web/Services/` + snapshot shape. The `SimulationRunService` generalization trigger is examined and explicitly **not** fired (rationale above). |
| Editor keyboard/interaction conventions | **Acknowledged debt** — WP2(f) introduces autofocus/Enter-to-save ad hoc across four editors. At a fifth editor this warrants a shared form-shell component; `backlog.md` entry added in the same PR (it does not exist yet — the first draft implied it did). |

## Flows introduced or modified (INV-17)

[Flow 29](../architecture/flows/flow-29-bulk-content-generation.md) **is modified** — the first draft
wrongly claimed no edit was needed. The flow asserts *disk-truth* catalog reads as a correctness
property in three places (the leg-B mermaid's `Load(kind, blueprintId) [disk truth]`, the fitter's
"loads the template **from disk** … never the audit's boot-time registry", and leg B4's "every action
ends with a full reload so the grid always reflects on-disk truth"). A cache changes that guarantee
from *disk truth* to *coherent for catalog-mediated writes*. This slice edits legs B/B3/B4 — body text
**and** the note over the mermaid — to state the new read semantics, the whole-index invalidation
rule, and the out-of-process staleness caveat. No new flow.

## Test plan / Verification (INV-25)

| Tier | Test | Asserts |
|---|---|---|
| System | Cache hit | A second `List(kind)` performs no filesystem read. **Method decided (`SR-5`): a counting filesystem seam injected into the catalog** — not a temp-dir mutation, which would assert *stale* behavior and freeze the no-watcher decision into a test. The seam is in WP1's Files list. |
| System | Per-kind sweep bound (Postcondition 3) | Listing a kind twice sweeps once; listing all four kinds sweeps each kind's directory at most once per invalidation. |
| System | Bulk-loop cost | A `Load` following an invalidation reads one file, not the corpus — the property that keeps `ApplyFlaggedAsync` O(N). |
| System | Lost-invalidation race | With population and invalidation interleaved (a write completing mid-sweep), the next read observes the write — the generation-counter guard. Deterministic via the counting seam, not timing. |
| System | Invalidation, one per mutating method | After each of the six mutators, the next `List`/`Load` observes the write. |
| System | **Cascade** invalidation | A `RenameAsync` referrer, a `DeleteAsync`-cleared `AreaId`, and a `SaveRoomAsync(bidirectional)` inverse exit are each observed on the next read — i.e. a definition the write *cascaded* to, not just the target. |
| System | `RoomsInArea` re-derivation | A room's `AreaId` change is reflected in both the old and new area's results. |
| System | `WithBlueprintId` | Preserves all non-id fields for each of the four kinds; mints an ad-hoc id when blank; rewrites self-referential ids consistently with `RenameAsync`. |
| System | `CreateNextFrom` | Carries the documented per-kind fields and resets the rest. |
| Web (`Hedron.Tests/Web/`) | Sweep service | Reports in-progress state while running and a completed result after. **Deterministic form, not a sleep:** a fake `IContentReferenceIndex` blocks until signaled; assert the start call returns and the snapshot reports in-progress while the fake is held, then release and assert completion. |
| Architecture guard | Blueprint-id call site | No `.razor` `OnBlueprintIdChanged` calls `CreateNew` — the defect survived in four files, so a guard is cheaper than trusting review. |

**Not tested, and why.** `@key`, debounce, and autofocus remain presentation skip-tier per
[`07-testing.md`](../architecture/07-testing.md) — markup plumbing with no decision content.
Postcondition 3 is asserted at the catalog level (one disk sweep per render pass) rather than by
counting calls inside a `.razor`, which no tier can observe. Note the skip-tier rationale is itself
under review at the client-tier gate; the component logic that does *not* qualify as presentation is
extracted in [`authoring-api-surface.md`](authoring-api-surface.md) WP1.

## Reference & tooling updates owed (INV-16, INV-20)

- [`../reference/systems.md`](../reference/systems.md) — the `IContentDefinitionCatalog` row reproduces
  the interface method list verbatim; add `WithBlueprintId`, `CreateNextFrom`, `Invalidate`, and note
  the caching/concurrency posture. Same PR.
- [`.claude/skills/add-domain-system/SKILL.md`](../../.claude/skills/add-domain-system/SKILL.md) —
  presents domain systems as stateless and "pure where possible"; this slice creates the first cached,
  guarded domain system. Add a short stateful/cached-systems note (guard or confine per `INV-31`,
  invalidate at every mutator, snapshot-swap over lock-across-`await`, `TemplateRegistry` precedent).
  Blocking under `INV-20` — without it the next slice copies the stateless shape and hand-rolls an
  unguarded cache.
- [`.claude/skills/add-tests/SKILL.md`](../../.claude/skills/add-tests/SKILL.md) — add the counting
  filesystem fixture to the harness section if it lands as shared.

"Ship green" = `dotnet build` **and** `dotnet test` (`INV-25`).
