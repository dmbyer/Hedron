# Implementation Plan: Authoring seam — component-logic extraction + gate-enabling JSON surface

**Status:** planned
**Actors:** Administrator (content author) · System (the client-tier gate's bakeoff page)
**Module:** `Core/Modules/Authoring/` · Feature: [`admin-authoring`](../features/admin-authoring/admin-authoring.md)

## Description

Close two structural gaps the client-tier analysis found in the authoring tier
([`../design/client-tier.md`](../design/client-tier.md)), both independent of which front-end wins the
gate. **WP1** moves genuine decision logic out of Razor components and into the systems that own it,
where the presentation skip-tier stops hiding it from `INV-25`. **WP2** puts a deliberately narrow
JSON surface over the catalog for **one** consumer: the bakeoff page the gate's measurement requires.

## Preconditions

- The authoring systems in `Core/Modules/Authoring/Systems/` remain the sole authoring logic home.
- **No hard scheduling dependency on `authoring-editor-repair`** (now shipped —
  [`../roadmap/completed/authoring-editor-repair.md`](../roadmap/completed/authoring-editor-repair.md);
  as built, `ContentDefinitionCatalog` **does** now hold a guarded in-memory index, so the "request
  threads simply read disk" case below no longer applies and this slice inherits the catalog's INV-31
  posture rather than establishing one). The
  previous revision asserted a dependency ("its index guard is what makes concurrent catalog access declarable"),
  which inverts the relationship: `ContentDefinitionCatalog` held no mutable state when this plan was
  written, so if this slice had landed first there would have been no index to guard — request threads
  would simply read disk, and this slice's own
  write-serialization posture (below) is what is actually needed. The real constraint is **whichever
  lands second respects the other**: if the sibling has landed, these endpoints go through the same
  catalog and therefore the same index and invalidation. The two slices can be ordered freely, except
  for the `Standards.razor` and shared-file ordering recorded in each plan.

## Postconditions

1. No `.razor` file computes a connect policy or constructs a balance oracle; each is a `Core/` system
   method with a test. *(Invisible state → tested, `INV-25`.)*
2. The extracted operations take **blueprint ids, not view-model cells**, so they are callable from any
   surface without a presentation type crossing the boundary (`INV-15`).
3. The **whole bakeoff page** is servable over HTTP JSON: Mob list/load/create/save/delete, read-only
   Area and Room lookups (the spawn-room picker and its area filter), and a mob power/band projection
   read (the live power readout). *(Scoped to what the gate's page actually calls — see the scope
   note in WP2.)*
4. An OpenAPI document is emitted at build and regenerated in CI, failing the build on drift.
5. The JSON surface is covered by integration tests under the existing `dotnet test` gate, writing to a
   per-test temp content directory and never to the repo's content tree.

## Main Flow

**A — extraction (no behavior change).**
1. A component that previously computed a value inline calls the owning `Core/` system by blueprint id.
2. The system returns a result; the component renders it. No event, no live-world touch (`INV-5`, `INV-12`).

**B — JSON read/write.**
1. A client issues `GET`/`POST` to the authoring host for the Mob kind.
2. The endpoint maps the request to the **existing** catalog call. It adds no *authoring* rules of its
   own — validation, id-minting, and YAML shape stay in the catalog. It does own **DTO mapping**, which
   is unavoidable: `ContentDefinition` has no parameterless ctor, read-only properties, and a
   polymorphic `IEntityTemplate` discriminated by `Kind`, so it cannot round-trip unmapped.
3. The catalog validates and writes YAML exactly as for the in-process caller; the endpoint maps
   `ContentWriteResult` to a status code and body.

**Where kind-dispatch lands when kind #2 arrives:** a catalog-side mapping seam, **not** a `switch` in
`Hedron.Web/Api/` — kind-dispatch in an entry-point surface is what
[`08-blazor.md`](../architecture/08-blazor.md) forbids. Scoping WP2 to one kind defers the seam but
does not excuse it; the second kind must add it rather than branch.

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| *(none)* | — | — | Authoring stays off the bus (`INV-5`). The endpoints are transport adapters over domain systems, not Initiators — the posture [`08-blazor.md`](../architecture/08-blazor.md) records for the Blazor host. **Explicitly excluded:** an apply/reload endpoint, which *would* be an Initiator (it publishes `ContentReloadedEvent` via `IWorldContentLoader`) and carries a different posture. |

## Systems / handlers involved

- [`IAreaLayoutSystem`](../../Core/Modules/Authoring/Systems/IAreaLayoutSystem.cs) — receives the grid connect policy (WP1).
- [`IPowerBudgetSystem`](../../Core/Systems/) — **core tier**; receives a pure preview overload (WP1). The first draft named `IMobPowerProjectionSystem`, which is not involved and lives at `Core/Modules/Mobs/Systems/`.
- [`IContentValidator`](../../Core/Modules/World/Systems/) — receives the one genuine loot rule (WP1).
- [`IContentDefinitionCatalog`](../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs) — backs the endpoints (WP2).

## Implementation plan — work packages

### WP1 — `INV-8` conformance: extract leaked component logic

**Scope.** Two extractions and one narrow validation rule, each with a test:

| Leak | Current site | Destination |
|---|---|---|
| The **connect policy** — which direction pair to write, whether to write bidirectionally, and the non-adjacent fallback | [`AreaGridEditor.razor:400`](../../Hedron.Web/Components/Pages/AreaGridEditor.razor) | `IAreaLayoutSystem.ConnectAsync(string fromRoomBlueprintId, string toRoomBlueprintId)` — the system already owns a write path via `ApplyProposalAsync`. Note the direction *math* is already a tested Core helper (`DirectionExtensions.FromOffset`); the decision, not the arithmetic, is what moves. |
| Per-cell target-range preview constructing an oracle per cell, per render | [`Standards.razor:383`](../../Hedron.Web/Components/Pages/Standards.razor) | A **pure static** `TargetRange(PowerBudgetTunables, int tier, int band)` (or a small pure `PowerBudgetMath` the instance delegates to) — **not** an interface overload, which would make every `IPowerBudgetSystem` implementation carry a member that ignores instance state and force the page to inject the DI singleton solely to call a method that disregards it. `PowerBudgetSystem` holds no precomputation (its only field is `_tunables`), so this is a clean split. It must **not** change the composed instance's ctor-injected snapshot semantics — a recorded decision (`backlog.md` §Live balance-standards reload), and the razor's comment documents the throwaway instance as deliberate. **Honest framing:** what moves is the *instantiation of a DI-registered type inside a component*, not decision logic — the win is the ratchet test plus DI discipline. The caller must also hoist `_tunables.ToTunables()` out of the per-cell loop, or the overload moves the allocation rather than removing it. |
| `min ≤ max` / non-negative on `CurrencyLoot` | [`MobEditor.razor:409`](../../Hedron.Web/Components/Pages/MobEditor.razor) | `IContentValidator`, with a fail-fast test. |

**Dropped from the first draft:** extracting the `CurrencyLoot`/`StatBonuses` collection add/remove
plumbing. [`08-blazor.md`](../architecture/08-blazor.md) *explicitly permits* it ("Form-binding glue
(collection add/remove, CSV split) is presentation plumbing, not logic — that is allowed"), the first
draft cited that same permission a paragraph after violating it, and the proposed tests would have
asserted BCL collection semantics — which [`07-testing.md`](../architecture/07-testing.md) classifies
as noise. Generalized, "a catalog helper per bound collection field" is an `INV-19` pattern explosion.
Only the genuine rule (row 3) moves.

**Files.** `Core/Modules/Authoring/Systems/{IAreaLayoutSystem,AreaLayoutSystem}.cs`; the pure
`TargetRange` split in `Core/Systems/`; `IContentValidator`;
`Hedron.Web/Components/Pages/{AreaGridEditor,Standards}.razor` become callers. **Not** `MobEditor.razor`
— row 3 puts the loot rule on `IContentValidator`, which the catalog already invokes on the save path,
and the editor already surfaces `_errors`; it needs no change.

**Dependencies.** None. **Lands unconditionally** — `INV-8` conformance owed regardless of the gate.
It also **blocked** `authoring-editor-repair` from touching `Standards.razor` — that slice shipped
and deliberately left the per-cell oracle hoist alone, so the extraction here is still owed and
uncontested.

**Out of scope.** Any behavior change; tests characterize existing behavior.

**Exit criterion.** Each extracted method has a system test; an architecture-guard row denies the
specific leaked constructs — `DirectionExtensions.FromOffset` and `new PowerBudgetSystem(` inside
`Hedron.Web/` — **not** a blanket "no `Direction` in `.razor`", since the grid legitimately renders
per-direction UI and calls `DisconnectEdge(roomId, Direction)`. Note this guard scans `Hedron.Web/`,
a new source root for the guard tier (existing guards scan `Core/`).

### WP2 — Gate-enabling JSON surface (Mob kind)

**Scope.** Minimal-API endpoints on the authoring host covering **what the bakeoff page actually
calls**: Mob list/load/create/save/delete, read-only Area and Room list lookups, and a mob power/band
projection read. Emit an OpenAPI document at build; add a **dotnet-only** CI drift gate on it.

**Why not Mob-only** (the previous revision's scope, corrected at the second spec gate): `MobEditor`
injects `IPowerBudgetSystem`, `IBalanceStandardsRegistry`, and `IMobPowerProjectionSystem`, and calls
`Catalog.List(ContentKind.Area)` and `List(ContentKind.Room)` for the spawn-room picker plus
`DeleteAsync` for removal. A Mob-only surface cannot render the page, and in particular cannot render
the **live power readout** — which [`../design/client-tier.md`](../design/client-tier.md) names as one
of the two tune-and-observe loops React is claimed to make structurally cheap, i.e. a headline thing
the bakeoff exists to measure. Scoping below the consumer would have meant either a bakeoff that
can't measure its own subject or a measurement slice quietly hand-rolling endpoints outside this
slice's conventions.

**Consequence: the kind-dispatch seam lands here, not later.** Three kinds arrive in this slice, so
the catalog-side mapping seam described in Main Flow B is built now rather than promised. This is the
scope increase the extension buys; it is accepted deliberately.

**Justification — settled, not deferred (`SR-5`).** The first draft left this to the reviewer, which
`SR-5` forbids. Settled: the **single** justifying consumer is the bakeoff page named in
[`../design/client-tier.md`](../design/client-tier.md#decision-gate) — the gate's measurement is
"port `MobEditor` to React **over the JSON surface**", so the gate cannot fire without this, and this
plan is already a named gate precondition. The first draft's second consumer, "CI-testable authoring
operations," is **withdrawn as a justification**: it is circular (endpoints existing so endpoints can
be tested) and the coverage hole it points at is closed more cheaply by WP1 at the system tier. It
remains a genuine side benefit.

**Auth — deferred, with the real rationale.** The first draft claimed "the posture is unchanged from
the Blazor host's." That is **false**: [`Hedron.Web/Program.cs`](../../Hedron.Web/Program.cs) applies
`UseAntiforgery()` and the editor is a circuit-bound Razor surface, whereas an unauthenticated
minimal-API **write** endpoint is not covered by antiforgery, is reachable by any local process, and
is reachable cross-origin from any page in the author's browser (localhost CSRF / DNS rebinding).
Loopback is a *weaker* control here than it is for a Blazor page. The deferral is still legitimate —
`INV-19` enumerates *player-facing* surfaces and this is admin/authoring, and the backlog entry exists
— but it ships with the cheap in-slice mitigation: require `Content-Type: application/json`, and
reject non-loopback `Host` and cross-origin `Origin`. The `backlog.md` entry's matching "loopback ⇒
fine" reasoning is corrected in the same PR.

That mitigation is a **fail-fast validation**, which [`07-testing.md`](../architecture/07-testing.md)
puts in the always-test column — it gets a test row rather than riding along untested. It also
depends silently on **no CORS policy being registered** on this host (the JSON content-type
requirement blocks HTML-form CSRF because forms cannot send `application/json`; a cross-origin
`fetch` that *does* triggers a preflight, which fails only because no CORS policy exists). A future
`AllowAnyOrigin` would quietly undo it. WP2 therefore states: **no CORS policy is to be registered on
this host**, with a guard test to hold the line.

**Gate neutrality — TypeScript is deliberately excluded.** The first draft checked in a generated TS
type artifact and gated CI on it. [`../design/client-tier.md`](../design/client-tier.md) books "a
permanent C#/TS drift tax" and "a second toolchain" as **React-branch costs**; paying them before the
gate is not no-regret, and under a no-go the artifact would have zero consumers while still failing
builds. The OpenAPI document itself is framework-neutral and stays.

**Where the deferred cost lands, stated rather than implied.** Removing TS generation transfers a cost
nobody had budgeted: [`../design/client-tier.md`](../design/client-tier.md) describes "a one-page
bakeoff" and budgets no Node/Vite/generator toolchain before the gate — that toolchain appears only in
the go branch. Closure: the bakeoff **hand-writes the single mob interface from the OpenAPI document**,
so no generator and no second CI toolchain is needed pre-gate. One page's worth of types is cheaper
than standing up codegen for it.

**Mechanism.** .NET 8 emits no OpenAPI document at build time on its own; this needs
`Microsoft.Extensions.ApiDescription.Server` plus `Swashbuckle.AspNetCore.Cli` — both dotnet-only, so
gate neutrality holds. Document generation builds the web host's DI graph, so CI needs a resolvable
`World:ContentDirectory`. CI today runs `build --configuration Release` then `test --no-build`; the
regeneration and diff step slots in after the build so it does not force a second one.

**Test harness mechanics.** `Hedron.Tests.csproj` is `Microsoft.NET.Sdk` with no
`Microsoft.AspNetCore.Mvc.Testing` reference — WP2 adds it. `WebApplicationFactory` boots the real
host, so `AddContentBootstrapHostedServices` runs and a `POST` test would write YAML into the
configured `World:ContentDirectory` — i.e. the repo's content tree. The factory **must** override that
setting to a per-test temp directory, and the tier declares its xunit collection/parallelism posture.

**Files.** `Hedron.Web/Api/` (endpoints + DTO mapping), `Hedron.Web/Program.cs` (mapping + OpenAPI),
`Hedron.Tests/Hedron.Tests.csproj` + `Hedron.Tests/Web/`, `.github/workflows/ci.yml` (OpenAPI drift gate).

**Dependencies.** WP1. No hard dependency on the sibling slice (see Preconditions).

**Merge coordination on shared files.** The sibling slice lands its `AreaGridEditor.razor` and Flow 29
edits first; this slice rebases onto them. The edits are disjoint (it hoists `Bounds()` and adds
`@key`, and rewrites leg B/B3/B4 read semantics; this slice rewrites the connect path and adds leg B's
HTTP participant), so the ordering prevents a collision rather than a conflict of intent.

**Out of scope, deliberately.** The full ~30-endpoint surface across every authoring system — the
extension above is bounded by what the bakeoff page calls, not widened to "all authoring operations."
Authentication (above). Any endpoint on the telnet game host. An apply/reload endpoint on *this* host
(it would be an Initiator — see Events Fired). Write endpoints for Area and Room: those kinds are
**read-only** here, since the bakeoff ports the mob editor.

**Exit criterion.** Endpoints round-trip a Mob definition under integration test against a temp
content directory; Area/Room lookups and the power projection read match their in-process
equivalents; `CreateAsync` completes without deadlock under the write semaphore; a deliberate DTO
property rename fails the OpenAPI drift gate.

*The primary agent runs `architecture-reviewer` (code mode) across the combined diff.*

## Content tooling impact (INV-18)

No new gameplay state. WP1 relocates existing authoring logic without changing any data-file shape;
WP2 adds a second transport to the same operations. No new admin command, no `TemplateRegistry` entry.

## Cross-cutting surfaces stressed (INV-19)

| Surface | Classification |
|---|---|
| Authoring-logic ownership (`INV-8`/`INV-15`) | **Gap exposed → resolved in WP1.** |
| DTO / contract mapping | **Gap exposed → resolved in WP2.** `ContentDefinition` cannot round-trip unmapped; the mapping is a real surface and is named here rather than hidden inside "endpoint definitions". |
| `ContentWriteResult` → status-code convention | **Gap exposed → resolved in WP2.** Established once, so the second endpoint kind does not re-hand-roll it. |
| CI toolchain | **Adequate** — the OpenAPI drift gate is dotnet-only; no second toolchain enters CI in this slice (see gate neutrality). |
| Testing tiers (`INV-25`) | **Gap exposed → partially resolved.** WP1 brings extracted logic under the system tier; WP2 adds an HTTP-integration tier that must be recorded in [`07-testing.md`](../architecture/07-testing.md) in the same PR. |
| HTTP as a new surface (`INV-19`) | **Acknowledged debt, bounded** — auth deferred with the corrected rationale and an in-slice mitigation (above); backlog entry exists and is corrected in the same PR. |
| Concurrency posture (`INV-31`) | **Gap exposed → resolved in WP2.** Posture: **guard** (a semaphore is a guard, not confinement — the earlier revision mislabelled it, and `INV-31`'s spec check is that the posture is named correctly). Request threads become a **second concurrent writer** to the YAML store, whose multi-file cascade is documented non-transactional; the sibling's index guard covers index consistency only, not the write path. Mechanism: **`SemaphoreSlim(1,1)` + `WaitAsync`** — not a thread-affine lock, for the same no-lock-across-`await` reason the sibling established. **Re-entrancy is load-bearing:** `ContentDefinitionCatalog.CreateAsync` calls the *public* `SaveAsync`, so a naive semaphore around every public mutator self-deadlocks on `create` — one of the operations this slice exposes. Required shape: a private core per mutator plus public wrappers that take the semaphore exactly once. The critical section spans the write **and** the index swap, so the sibling's snapshot swap happens inside it while readers stay lock-free. Serializing also closes a real TOCTOU in `CreateAsync` (it checks `_referenceIndex.Resolves` and writes non-atomically). Readers can still observe a mid-cascade disk state — pre-existing, backlog-tracked, unchanged. No live-world component is touched. |

## Flows introduced or modified (INV-17)

[Flow 29](../architecture/flows/flow-29-bulk-content-generation.md) leg B is modified: its mermaid
declares `participant UI as Blazor component`, so a second caller changes the **diagram**, not only the
prose. This slice adds the HTTP caller to leg B's participants plus a body note that both callers
resolve to the same catalog operations. WP1 creates no flow (pure relocation).

## Test plan / Verification (INV-25)

| Tier | Test | Asserts |
|---|---|---|
| System | `ConnectAsync` | Given two adjacent rooms by blueprint id, writes the expected direction pair and bidirectional policy; rejects non-adjacent rooms. |
| System | `IPowerBudgetSystem` preview overload | Returns the same range as the composed instance for equal tunables, and does not mutate it. **On-touch ratchet:** `PowerBudgetSystem` has no dedicated test file today (only transitive coverage), so this adds one. |
| System | `CurrencyLoot` validation | `min > max` and negative values fail fast with the documented error. |
| Architecture guard | Component-logic guard | `Hedron.Web/` contains no `DirectionExtensions.FromOffset` and no `new PowerBudgetSystem(`. |
| Integration (new tier) | Mob endpoint round-trip | `POST` then `GET` returns the written definition, against a per-test temp content directory; a validation failure maps to the documented status and carries the catalog's errors. |
| Integration | Cross-kind lookups | Area and Room list endpoints return the same summaries as the in-process `List(kind)` call. |
| Integration | Auth mitigation (fail-fast) | Rejects a non-loopback `Host`, a cross-origin `Origin`, and a non-JSON `Content-Type`. |
| Architecture guard | No CORS policy | The web host registers no CORS policy — the preflight failure the mitigation relies on stays load-bearing. |
| System | Write serialization re-entrancy | `CreateAsync` completes without deadlock under the semaphore — the specific defect a naive guard would introduce. |
| CI gate | OpenAPI drift | Regenerating the document produces no diff; a renamed **DTO** property fails the gate. (A rename on a core template is caught earlier, by the compiler at the mapper.) |

**Not tested, and why.** Razor markup remains presentation skip-tier
([`07-testing.md`](../architecture/07-testing.md)) — but that rule's justification ("the logic they
call is already covered") only becomes true once WP1 lands. That is the point of WP1. Collection
add/remove binding stays untested by design (see the WP1 drop rationale).

## Reference & tooling updates owed (INV-16, INV-20)

- [`../reference/systems.md`](../reference/systems.md) — reproduces these interfaces verbatim; update
  the `IAreaLayoutSystem`, `IContentDefinitionCatalog`, and `IPowerBudgetSystem` rows. WP2's endpoint
  types have no `reference/` home; their durable home is [`08-blazor.md`](../architecture/08-blazor.md)
  (Project-structure table + an HTTP-surface subsection) and the `admin-authoring` feature doc.
- [`../architecture/08-blazor.md`](../architecture/08-blazor.md) — three edits: the Project-structure
  table gains `Api/`; "Where the UI sits in the layer model" currently describes exactly two
  interaction shapes, both in-process; "Discipline for UI components" must state that the endpoint tier
  inherits the same thin-surface rule.
- [`../architecture/07-testing.md`](../architecture/07-testing.md) — the tier table, the taxonomy, and
  the "Web-host services" paragraph (currently scopes `Hedron.Tests/Web/` to `Services/*` fakes).
- [`.claude/skills/add-tests/SKILL.md`](../../.claude/skills/add-tests/SKILL.md) — **unconditional**
  (the first draft made it conditional; it is not). The "Pick the tier" table gains the HTTP-integration
  tier, and the existing line stating `Hedron.Tests` references `Hedron.Web` for `Services/*` and
  "never a real Blazor render" becomes false once `WebApplicationFactory` boots the host. Must also
  state the temp-content-directory requirement.
- [`.claude/agents/implementation-planner.md`](../../.claude/agents/implementation-planner.md) —
  enumerates the five tiers by name; a plan author would otherwise omit the new one.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — correct the REST-API entry's "loopback ⇒ fine"
  reasoning and its "carries its own auth + DTO surface" stipulation, which this slice partially defers.

"Ship green" = `dotnet build` **and** `dotnet test` (`INV-25`).
