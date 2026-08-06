# Authoring editor repair — catalog index + editor UX ratchet (completed)

> Implemented on branch `claude/authoring-editor-repair-5a63f4`, 2026-08-05. Living docs: [`admin-authoring`](../../features/admin-authoring/admin-authoring.md) · [`content-tooling`](../../features/admin-authoring/content-tooling.md) · [Flow 29](../../architecture/flows/flow-29-bulk-content-generation.md) · [`reference/systems.md`](../../reference/systems.md).

The first of the two **no-regret Phase 5 slices** recorded in [`../../design/client-tier.md`](../../design/client-tier.md) — valuable whichever way the client-tier gate falls, and deliberately framework-independent: a React port inherits the catalog cache unchanged.

## Outcome

Made the offline authoring editor fast and safe enough to author Phase 5's content baseline against.

Two independent defects were fixed. **`IContentDefinitionCatalog` re-read and re-deserialized the entire content corpus on every `List`/`RoomsInArea` call**, and the editor called it from inside render loops — the content browser asked the catalog for a count once per rendered tab, per render pass. The catalog now serves reads from an in-memory index that every mutator invalidates. **The four content editors silently discarded an in-progress form when the author edited the blueprint id** — `OnBlueprintIdChanged` called `CreateNew`, which mints a fresh default definition. They now call a new `WithBlueprintId`, which replaces only the id; an architecture guard prevents the regression, since the defect had survived review in four separate files.

Alongside those: a per-kind carry-forward `CreateNextFrom` behind a "Save & create next" action on all four editors, `@key` on every row/cell loop, debounced exit-picker filters, keyboard-first authoring (autofocus / Enter-to-save / Esc-to-cancel), and the Integrity page's two corpus sweeps moved off the Blazor circuit thread. 1338 tests total (up from 1304).

## Behavior digest

**Preconditions.** `IContentDefinitionCatalog` is the *editor's* sole authoring read/write path — but **not the only writer of content YAML**: `ContentGenerationSystem` and the eight `mk*`/`set*`/`dig` telnet commands write through the `I*ContentWriter` family directly, bypassing the catalog. That fact is load-bearing for the cache design.

**Postconditions.**
1. Repeated `List(kind)` / `RoomsInArea(id)` with no intervening catalog write perform no filesystem reads after the first.
2. Any catalog mutation (`SaveAsync`, `SaveRoomAsync`, `CreateAsync`, `DeleteAsync`, `RenameAsync`, `RemoveRoomExitAsync`) invalidates the **whole index**, so a subsequent read observes both the target write and every definition its cascade touched.
3. Each kind's summary is swept at most once per invalidation, not once per render.
4. Changing the blueprint id on a New form preserves every other authored field.
5. The integrity sweep runs off the render thread and exposes observable in-progress state.

**Main flow (the offline-edit leg of Flow 29).** Author opens the content browser → the page reads counts once into local state → the catalog serves from its index, populating from disk on a cold index → the author opens an editor, edits fields, and on a New form sets a deliberate blueprint id, getting back the *same* definition carrying the new id → saves → the catalog validates, writes YAML, invalidates → the next read observes the write and its cascade. Content written **out of process** (a `generate` CLI run against the same directory) needs the browser's explicit refresh action.

**Events fired: none.** Authoring is off the bus; the catalog is a domain system and never publishes (INV-5). The apply leg's `ContentReloadedEvent` is unchanged.

## Shipped pieces

| Surface | Location |
|---|---|
| In-memory index (per-kind summaries, derived room→area map, per-id file bodies) + `Invalidate()` | `Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs` |
| `WithBlueprintId(definition, blueprintId)` — id-only rekey reusing `CloneWithNewId` | same |
| `CreateNextFrom(previous, name)` — per-kind carry-forward policy | same |
| `WriteTemplateAsync` / `DeleteFile` — the two invalidating write primitives every mutation routes through | same |
| `IContentFileReader` / `ContentFileReader` — catalog-scoped read seam (infrastructure **port**, no `reference/systems.md` row per INV-16/29) | `Core/Modules/Authoring/Systems/IContentFileReader.cs` |
| DI registration for the seam (composition-root smoke guard) | `Core/Modules/Authoring/AuthoringModule.cs` |
| `ContentIntegritySweepService` + `IntegritySweepStatus` — off-thread sweep, snapshot-polled | `Hedron.Web/Services/ContentIntegritySweepService.cs` |
| Counts hoisted out of the render loop; **Refresh from disk** action; `@key` on rows/tabs | `Hedron.Web/Components/Pages/Browser.razor` |
| `Bounds()` hoisted to a recomputed field; `@key` on cells/edge tabs/badges | `Hedron.Web/Components/Pages/AreaGridEditor.razor` |
| Sweep service adoption; `@key` on all four table loops | `Hedron.Web/Components/Pages/Integrity.razor` |
| `WithBlueprintId` in `OnBlueprintIdChanged`; "Save & create next"; keyboard-first shell; `@key` | `Hedron.Web/Components/Pages/{Area,Room,Item,Mob}Editor.razor` |
| Debounced per-direction filters (`ShouldRender` gate + per-direction CTS); `@key` | `Hedron.Web/Components/Shared/RoomExitsEditor.razor` |
| `FocusNameAsync()` for the owning page's autofocus; textarea keydown containment | `Hedron.Web/Components/Shared/RoomBasicsFields.razor` |

## Tests shipped

`dotnet test Hedron.sln` green — **1338 passed, 0 failed** (1304 before).

| Tier | Test file | Covers |
|---|---|---|
| System | `Hedron.Tests/Authoring/ContentDefinitionCatalogCacheTests.cs` (17) | Cache hit (`List`, `RoomsInArea`); per-kind sweep bound (all four kinds, 3 passes → exactly 4 sweeps); bulk-loop cost (a `Load` after an invalidation reads one file); the lost-invalidation interleave; one invalidation test per mutating method (6); cascade invalidation (rename referrer, delete-cleared `AreaId`, bidirectional inverse exit); `RoomsInArea` re-derivation across old and new area; explicit `Invalidate()`; no-shared-mutable-template |
| System | `Hedron.Tests/Authoring/ContentDefinitionCatalogAuthoringApiTests.cs` (12) | `WithBlueprintId` preserves every non-id field for all four kinds, mints an ad-hoc id when blank, rewrites self-referential ids consistently with `RenameAsync`; `CreateNextFrom` carry-forward and reset per kind, list copies are independent, ids distinct across calls |
| Web | `Hedron.Tests/Web/ContentIntegritySweepServiceTests.cs` (5) | In-progress state while a gated fake is held, then the completed result; idle before start; concurrent start joins the in-flight sweep; failure reported as `Failed` with the message, not thrown; a second sweep runs after completion |
| Architecture guard | `Hedron.Tests/Architecture/ArchitectureGuardTests.cs` | No `.razor` `OnBlueprintIdChanged` calls `Catalog.CreateNew`; pre-check asserts all four handlers were actually scanned so a rename fails loudly |

**Determinism (INV-26).** No sleeps anywhere. Filesystem effects are counted through the injected `IContentFileReader` seam — deliberately *not* by mutating a temp directory behind the catalog's back, which would assert *stale* behavior and freeze the "no `FileSystemWatcher`" decision into a test. The mid-sweep interleave uses the seam's `AfterGetFiles` hook. The sweep service's in-progress state is asserted while a `ManualResetEventSlim` the test owns is provably held.

**Not tested, and why.** `@key`, debounce, and autofocus stay presentation-skip-tier per [`07-testing.md`](../../architecture/07-testing.md) — markup plumbing with no decision content. Postcondition 3 is asserted at the catalog level (disk sweeps per pass) rather than by counting calls inside a `.razor`, which no tier can observe. All of them were exercised manually against the running app instead (see Deviations).

## Decisions

**Invalidation is whole-index, not entry-scoped.** `DeleteAsync` clears `AreaId`/exits on *other* definitions, `RenameAsync` rewrites every referrer, `SaveRoomAsync(bidirectional: true)` writes an inverse exit on a *different* room, and the summaries are backed by a derived room→area map. Entry-scoped invalidation cannot express those cascades correctly; dropping the whole index is the only safe rule at this content scale.

**Population granularity is what keeps that affordable — and it is load-bearing, not an optimization detail.** The per-id definition map fills **per id, on demand** (one file read); only the per-kind summary lists are corpus sweeps. `TemplateConformanceSystem.ApplyFlaggedAsync` loops `Load`→`SaveAsync` per drifted entry and `IAreaLayoutSystem.ApplyProposalAsync` does per-room best-effort writes; with corpus-populated per-id caching, whole-index invalidation would turn both into N full sweeps — quadratic, on the very Integrity and grid pages this slice exists to improve.

**Concurrency posture (INV-31): snapshot swap, not `ReaderWriterLockSlim`.** The index is mutable state on a DI singleton reached concurrently from multiple Blazor circuits. Every mutator is `async` and invalidates *after* an awaited file write, so a thread-affine lock is unimplementable — it cannot be held across an `await`. (The plan's first draft specified `ReaderWriterLockSlim`; that was caught and corrected at planning.) The index is therefore a snapshot object swapped under a plain `lock`: readers take the reference with no lock and populate lazily into it. The guard covers **index consistency only** — YAML writes are not atomic, and the non-transactional multi-file cascade recorded in [`../backlog.md`](../backlog.md) is unchanged.

**Cached bodies, not cached templates.** `Load`'s per-id map caches the file *text*; `Load` deserializes a fresh template per call. Callers mutate what they get back — the editors bind form fields to it, and the catalog's own cascade helpers edit it in place — so handing out a shared instance would leak in-progress edits into the index.

**One id-rewrite rule, one id-minting path.** `WithBlueprintId` reuses the private `CloneWithNewId` that `RenameAsync` already uses (which encodes the self-referential-id rewrite); `CreateNextFrom` delegates to `CreateNew` for minting. The slice ships no second rewrite rule and no third minting path (INV-19).

**`CreateNextFrom` lives on the catalog, not in the editors.** Which fields carry forward is authoring policy *plus* kind dispatch, which [`08-blazor.md`](../../architecture/08-blazor.md) forbids in a component. Area carries nothing (areas are authored individually); Room carries `AreaId`; Item carries Tier, Band, `ItemType`, `WornSlots`; Mob carries Tier, Band, `SpawnRoomBlueprintId`.

**No `FileSystemWatcher` — and the honest reason.** The plan's first draft justified this with "the authoring host is the only writer," which is **false**: the `generate` CLI is a separate process writing the same directory (Flow 29 leg A), and the telnet `mk*`/`set*` verbs write through `I*ContentWriter` in the game host. A long-running editor host will therefore serve stale lists after an out-of-process write. Mitigation is the explicit `Invalidate()` + the browser's **Refresh from disk** action; the residual staleness is acknowledged debt with a [`backlog.md`](../backlog.md) entry.

**The `SimulationRunService` generalization trigger was examined and deliberately not fired.** [`08-blazor.md`](../../architecture/08-blazor.md) and `backlog.md` record that a *second* long-running editor job wanting queue/progress/cancel should generalize `SimulationRunService`. `ContentIntegritySweepService` is **progress-only — no queue, no cancellation** — so it does not meet that shape. The recorded candidate remains the bulk conformance apply (`PreviewAllFlagged`/`ApplyAllFlagged`), which **still runs blocking on the circuit thread in the very page this slice touched** and stays that way. Both halves are recorded in the backlog rather than left for a future reader to rediscover.

**The counting filesystem seam is a deliberately catalog-scoped test port.** The authoring module already has three filesystem styles (`ContentReferenceIndex` reads directly, the `I*ContentWriter` family writes, this seam wraps catalog reads). Stated explicitly so the next slice widens it rather than hand-rolling a fourth (INV-19). It gets no `reference/systems.md` row (INV-16/29 — the exclusion is recorded rather than left ambiguous) but is DI-registered for the composition-root smoke guard.

**What this slice did *not* speed up, stated plainly.** `IContentReferenceIndex` does its own `Directory.GetFiles`/`File.ReadAllText` and does not read through the catalog; `IBalanceAuditSystem` depends on `ITemplateRegistry`, not the catalog. Neither gains anything here. The Integrity page is therefore still slow for reasons the cache does not address — WP2(e) made it **non-blocking, not fast**.

**Ordering against the sibling slice.** [`authoring-api-surface`](../../implementation-plans/authoring-api-surface.md) WP1 owns the `INV-8` extractions including `Standards.razor`; optimizing that page's per-cell oracle calls in place would have been discarded when the extraction lands, so it was left alone. Both slices edit `AreaGridEditor.razor` and Flow 29; this slice landed its edits first so the sibling rebases onto them.

## Deviations / Follow-ups

Six as-built deviations from the plan. None change a postcondition.

1. **The generation guard is a stated invariant, not the sole defense.** The plan's required shape (a shared `_snapshot` field republished under a generation check) makes the check load-bearing. As built, a populating reader writes into **the snapshot object it captured**, and `Invalidate()` detaches that object — a late publish lands in an orphan nothing reads. Snapshot identity *is* the generation. The explicit `Generation` check is kept because it names the invariant, but the design is safe without it. Consequence: the **lost-invalidation race test** asserts Postcondition 2 under a mid-sweep write rather than the guard mechanism — verified to still pass with the check stubbed out. The postcondition is what matters and it is covered.
2. **The per-id map caches file text, not `ContentDefinition`** (rationale under Decisions). A test pins the no-shared-instance property.
3. **The room→area map is derived from the cached room summaries**, not swept separately — this is what actually delivers Postcondition 3 (listing all four kinds sweeps each directory exactly once, asserted as `TotalSweeps == 4`). The plan left the map as its own corpus sweep, which would have swept the room directory twice.
4. **`Invalidate()` fires inside the two write primitives** rather than at each mutator's exit, so no write path — including per-referrer cascade writes — can forget it. Per-id-on-demand population is what keeps the extra invalidations affordable.
5. **"Save & create next" re-arms the page in place** (a `_forceNew` flag flipping `IsNew`) rather than navigating to `/<kind>/new`, since the carried-forward definition cannot survive a route change without cross-page state. `Save` suppresses its post-create navigation while a create-next is mid-flight.
6. **The debounce needed a `ShouldRender` gate — and the gate had to be a counter, not a flag.** Blazor re-renders after every event handler, so a timer alone would not have suppressed the per-keystroke render. The first implementation used a single-shot `bool`, which the code gate correctly identified as insufficient: Blazor renders an async handler **twice** (once when it yields, once when it completes), and a superseded keystroke produces both its own yield render *and* the cancelled predecessor's completion render — so every keystroke after the first still cost a full repaint of six `<select>` lists. Now a counter, incremented on the yield and again on each handler's completion (both the normal and the cancelled path). Re-verified with a four-keystroke burst at 60 ms spacing: the option list holds at 23 through the whole burst and narrows only after the pause, repeatably across three bursts with no counter drift. The original manual check typed one complete filter at a time, which is exactly the case the bug did not affect.

**Code gate (architecture-reviewer, code mode): approve with nits, no blocking findings.** It independently verified the three load-bearing claims — the snapshot scheme admits no interleaving that caches a torn value or leaves the index permanently stale (including the re-entrant `EnsureRoomAreaMap` → `List` path, where a mixed-generation read escapes only as one in-flight return value and is never cached); both bulk loops are genuinely O(N), because each hoists its one corpus read above the write loop; and in-primitive invalidation actively *fixes* a bug the mutator-exit alternative would have introduced, since `SaveRoomAsync(bidirectional)` with two exits to the same target would otherwise have lost the first inverse exit. Nits applied: the counter fix above (finding 6); the INV-19 rationale in the form-shell backlog entry was misstating the invariant — the checklist says "repeating a hand-rolled pattern ≥3 times", with no distinctness clause, so four copies of one shell *is* past the trigger and the entry now says so; the docs now state that the one-sweep-per-invalidation bound is **per reader** (concurrent cold readers may duplicate a sweep) and that rejecting stale publishes trades cache retention for correctness under sustained writes; the O(N) remark generalized from two named callers to the rule ("never call `List`/`RoomsInArea` inside a loop that also writes"); a comment on the one index read that deliberately skips the generation check; and `DeleteFile`'s existence probe routed through the read seam.

**Manual verification against the running app** (the presentation-tier behaviors no test tier observes): blueprint-id edit preserved Name and Item type; "Save & create next" minted a fresh ad-hoc id, reset the name, carried `ItemType` forward, and autofocused the Name field; the browser's item count went 0 → 1 across pages (cross-page invalidation); Z-layer switching recomputed the grid bounds (7 columns → 5); the exit-picker filter left 23 options immediately after the keystroke and filtered correctly after the quiet period; Integrity swept clean; no console errors.

**Debt parked in [`../backlog.md`](../backlog.md):**
- **Cross-process content-cache coherence** — out-of-process writes (the `generate` CLI, the game host's `mk*` verbs) do not invalidate a running editor's index. `FileSystemWatcher` is the eventual answer and carries its own INV-31 posture.
- **Editor form-shell component** — keyboard-first authoring is ad hoc across four editors. At a fifth, extract a shared form shell; do not extract pre-emptively, since the client-tier gate could moot it.
- **Web background-job generalization** — updated with the not-fired rationale and the surviving candidate (the bulk conformance apply).

**Tooling updated (INV-20):** the `add-domain-system` skill gained a **Stateful / cached systems** section (invalidate at every mutator including cascades; pick population granularity against the caller's loop; declare the INV-31 posture and why `ReaderWriterLockSlim` is unusable with `async` mutators; cache what is safe to hand out) — blocking, because without it the next slice copies the stateless shape and hand-rolls an unguarded cache. The `add-tests` skill gained the counting-filesystem fixture and the gate-don't-sleep rule for background jobs.
