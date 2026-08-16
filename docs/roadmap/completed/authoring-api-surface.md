# Authoring seam — component-logic extraction + gate-enabling JSON surface (completed)

> Implemented on branch `claude/authoring-api-surface-c96608`, 2026-08-16. Living docs: [`admin-authoring`](../../features/admin-authoring/admin-authoring.md) · [`08-blazor.md`](../../architecture/08-blazor.md#the-authoring-http-surface-api) · [Flow 29](../../architecture/flows/flow-29-bulk-content-generation.md).

## Outcome

Second of the two no-regret Phase 5 slices ([`design/client-tier.md`](../../design/client-tier.md)), framework-independent by design. **WP1** moved the decision logic that had leaked into `.razor` components down into the systems that own it, where the presentation skip-tier stops hiding it from `INV-25`. **WP2** put a deliberately narrow JSON surface over the content catalog for exactly one consumer — the bakeoff page the client-tier decision gate's measurement requires — so the gate can now fire.

The slice's most consequential work was not in either work package. Making request threads a second concurrent caller of `ContentDefinitionCatalog` exposed three pre-existing concurrency defects that had been latent while a Blazor circuit was the only reader, and the fix for one of them corrected a mechanism the first implementation had documented incorrectly. 1501 tests total.

## Behavior digest

**Preconditions.** The authoring systems in `Core/Modules/Authoring/Systems/` remain the sole authoring-logic home. No hard ordering against the sibling `authoring-editor-repair` slice, which shipped first; this slice rebased onto its `AreaGridEditor.razor` and Flow 29 edits.

**Postconditions, as specified.**

1. No `.razor` file computes a connect policy or constructs a balance oracle; each is a `Core/` system method with a test.
2. The extracted operations take **blueprint ids, not view-model cells**, so they are callable from any surface without a presentation type crossing the boundary (`INV-15`).
3. The **whole bakeoff page** is servable over HTTP JSON: mob list/load/create/save/delete, read-only area and room lookups, and a mob power/band projection read.
4. An OpenAPI document is emitted at build and regenerated in CI, failing the build on drift.
5. The JSON surface is covered by integration tests under the existing `dotnet test` gate, writing to a per-test temp content directory and never to the repo's content tree.

All five hold as built. Postcondition 1 is enforced mechanically rather than by review.

**Main flow.** *(A — extraction)* A component that previously computed a value inline calls the owning `Core/` system by blueprint id; the system returns a result and the component renders it. No event, no live-world touch. *(B — JSON)* A client issues an HTTP request; the endpoint maps it to the **existing** catalog call, adding no authoring rule of its own — validation, id-minting and YAML shape stay in the catalog — and maps `ContentWriteResult` to a status code. The endpoints are transport adapters, **not Initiators**: authoring stays off the bus (`INV-5`).

## Shipped pieces

| Surface | Location |
|---|---|
| `IAreaLayoutSystem.ConnectAsync` + `AreaConnectResult`/`AreaConnectOutcome` — the grid's connect policy | `Core/Modules/Authoring/Systems/{IAreaLayoutSystem,AreaLayoutSystem}.cs` |
| `PowerBudgetMath` — the formulas as pure statics over caller-supplied tunables | `Core/Systems/PowerBudgetMath.cs` |
| `PowerBudgetSystem` — reduced to a thin instance facade over the above | `Core/Systems/PowerBudgetSystem.cs` |
| `IContentValidator.Validate` — mob currency-loot rule (non-negative, `Min ≤ Max`) | `Core/Modules/World/Systems/ContentValidator.cs` |
| `IContentDefinitionMapper<TDto>` — the kind-dispatch mapping seam | `Core/Modules/Authoring/Contracts/IContentDefinitionMapper.cs` |
| `MobDefinitionDto` + `MobDefinitionMapper` — the mob kind's transport shape | `Core/Modules/Authoring/Contracts/` |
| `IMobPowerReadoutSystem` — the per-template readout both surfaces render | `Core/Modules/BalanceInspection/Systems/{I,}MobPowerReadoutSystem.cs` |
| `AtomicFileWrite` — the one write-temp-then-`File.Replace` publish (extracted from 7 copies) | `Core/Systems/AtomicFileWrite.cs` |
| Catalog write serialization — `SemaphoreSlim(1,1)`, private-`*Core`/public-wrapper shape | `Core/Modules/Authoring/Systems/ContentDefinitionCatalog.cs` |
| `ContentFileReader.ReadAllText` — `FileShare.ReadWrite \| Delete` | `Core/Modules/Authoring/Systems/IContentFileReader.cs` |
| Thread-local YamlDotNet deserializers (×4) | `Core/Modules/{World/Templates,Items,Mobs}/*TemplateDeserializer.cs` |
| Endpoint mapping, status-code convention, security filter, readout DTO | `Hedron.Web/Api/` |
| OpenAPI emit at build + checked-in contract | `Hedron.Web/Hedron.Web.csproj` · `Hedron.Web/Hedron.Web_authoring.json` |
| CI drift gate | `.github/workflows/ci.yml` |

## Tests shipped

| Tier | Target | Count |
|---|---|---|
| 1 — system | `ConnectAsync` (direction derivation, bidirectional inverse, ghost-cell positions, vertical, non-adjacent/diagonal/cross-area refusals, not-found, conflict warnings) | 8 |
| 1 — system | `PowerBudgetMath` parity with the composed instance + non-disturbance | 4 |
| 1 — system | `IContentValidator` currency-loot fail-fast | 6 |
| 1 — system | `MobPowerReadoutSystem` | 10 |
| 1 — system | `MobDefinitionMapper` round-trip, incl. a reflection guard that fails on any unmapped settable `MobTemplate` property | 7 |
| 1 — system | Catalog write serialization: `CreateAsync` re-entrancy under timeout, all-mutator sequence, concurrent distinct creates, contested-id single-winner, torn-read | 5 |
| 1 — system | `AtomicFileWrite`: create, replace, no temp left, replace under a held reader, no torn read, throws after the attempt bound | 6 |
| 5 — architecture guard | No `DirectionExtensions.FromOffset` / `new PowerBudgetSystem(` under `Hedron.Web/`; no CORS policy | 2 |
| 6 — HTTP integration *(new tier)* | Mob round-trip, chosen/colliding ids, PUT/DELETE, 404s, validation→400 carrying the catalog's errors, no-file-on-refusal, cross-kind lookups, area filter, power readout vs in-process, unsaved-state projection, and each mitigation branch | 26 |

`dotnet test` green: **1501 passed, 0 failed** (Debug and Release, verified across repeated runs for flakiness). The on-touch ratchet held for every writer this slice modified.

## Decisions

**The kind-dispatch seam landed here, not "when kind #2 arrives".** Three kinds are reachable in this slice, so the catalog-side mapping seam was built rather than promised. `IContentDefinitionMapper<TDto>` lives in `Core/Modules/Authoring/Contracts/` — beside the catalog it maps for, not in the web project — and the endpoint helpers are generic over the DTO, so adding a writable kind is a DTO + mapper + registration with no endpoint change. A `switch (kind)` in an entry-point surface is what [`08-blazor.md`](../../architecture/08-blazor.md) forbids. The code review caught one instance that had crept in anyway (a `kind == Room ? RoomsInArea(area) : List(kind).Where(…)` ternary in the listing endpoint) and it was deleted — the two arms computed the identical result, so it bought nothing while seeding exactly the accretion the rule exists to stop.

**`PowerBudgetMath` is a pure static split, deliberately not an interface overload.** An `IPowerBudgetSystem` overload taking tunables would make every implementation carry a member that ignores instance state, and force a caller with no use for the composed snapshot to inject the DI singleton anyway. The split also leaves the composed instance's ctor-injected snapshot semantics untouched — a recorded decision ([`backlog.md`](../backlog.md) §Live balance-standards reload). The honest framing of what moved: not decision logic, but the *instantiation of a DI-registered type inside a component*; the win is the ratchet test plus DI discipline. The caller also hoists the tunables composition out of its per-cell loop, or the change would have moved the allocation rather than removed it.

**The `CurrencyLoot` rule moved to the validator, not into a catalog helper per bound field.** Only the genuine rule moved. The first draft also proposed extracting the collection add/remove plumbing; [`08-blazor.md`](../../architecture/08-blazor.md) *explicitly permits* that ("form-binding glue … is presentation plumbing, not logic"), and the proposed tests would have asserted BCL collection semantics, which [`07-testing.md`](../../architecture/07-testing.md) classifies as noise. Generalized, "a catalog helper per bound collection field" is an `INV-19` pattern explosion.

**Auth is deferred, but the previously recorded rationale was wrong and is corrected.** The claim "the posture is unchanged from the Blazor host's" is false: the editor is a circuit-bound surface covered by `UseAntiforgery()`, whereas an unauthenticated minimal-API **write** endpoint is not covered by antiforgery, is reachable by any local process, and is reachable cross-origin from any page in the author's browser. **Loopback is a weaker control here than it is for a Blazor page.** The deferral stands on different grounds — `INV-19` enumerates *player-facing* surfaces and this is admin/authoring — and ships with a cheap in-slice mitigation: loopback `Host`, same-origin `Origin`, and `application/json` on bodied methods. That last check blocks the HTML-form CSRF shape outright (a form cannot send JSON) and turns a cross-origin `fetch` into a preflight.

**No CORS policy may be registered on this host.** The cross-origin half of the mitigation works only because that preflight fails, and it fails only because no policy exists. An `AllowAnyOrigin` would silently undo it without touching endpoint code, so the absence is load-bearing and held by a *pair* of guards with complementary gaps: a source scan (which would miss a policy registered in `CompositionRoot` or arriving transitively) and a runtime `Assert.Null(GetService<ICorsService>())` (which catches exactly that).

**TypeScript generation was deliberately excluded.** [`client-tier.md`](../../design/client-tier.md) books "a permanent C#/TS drift tax" and "a second toolchain" as **React-branch costs**; paying them before the gate is not no-regret, and under a no-go the artifact would have zero consumers while still failing builds. The OpenAPI document itself is framework-neutral and dotnet-only to produce. The cost this transfers is closed by having the bakeoff hand-write its single mob interface from the document — one page of types is cheaper than standing up codegen for it.

**Only one consumer justifies this surface.** The earlier framing named two. "CI-testable authoring operations" is **withdrawn as a justification**: it is circular (endpoints existing so endpoints can be tested), and the coverage hole it pointed at is closed more cheaply by WP1 at the system tier. It remains a genuine side benefit. The single justifying consumer is the bakeoff page named in [`client-tier.md`](../../design/client-tier.md) — the gate's measurement is "port `MobEditor` to React **over the JSON surface**", so the gate cannot fire without it.

**The power readout is a POST over a posted definition, not a GET over a saved one.** The readout is one of the two tune-and-observe loops the client-tier analysis names as a headline thing the bakeoff exists to measure, so it must project *unsaved* form state as the author types. It writes nothing; a test pins that.

**INV-31 posture: guard, on two independent axes.** The sibling slice's snapshot-swap guards *index consistency*; it does nothing for two callers writing at once. Request threads are a second concurrent writer, so every public mutator now runs under a `SemaphoreSlim(1,1)` — a guard, not confinement, and async-compatible for the same no-lock-across-`await` reason. The critical section spans the write cascade **and** the invalidation that ends it. Serializing also closes a real TOCTOU in `CreateAsync`, which checked `_referenceIndex.Resolves` and then wrote non-atomically. **Re-entrancy is load-bearing:** `CreateAsync` is defined in terms of `SaveAsync`, so a semaphore inside every public mutator self-deadlocks on `create` — the shape is a private `*Core` per mutator calling only other `*Core` bodies, plus a public wrapper taking the gate exactly once. Neither guard makes the multi-file cascade transactional; that remains the recorded, backlog-tracked debt.

## Deviations / Follow-ups

Three deliberate departures from the plan, all reviewed and endorsed in code-mode review:

**1. `IMobPowerReadoutSystem` is new, and `MobEditor.razor` changed** — the plan said WP1 would not touch it. The power endpoint would otherwise have been a second hand-composition of `IMobPowerProjectionSystem` + `IPowerBudgetSystem` + `IBalanceStandardsRegistry` + band-index drift arithmetic in an entry-point surface, at ×2 with the razor: precisely the duplication `INV-19` exists to stop. It lives in `BalanceInspection` as the per-template counterpart to `IBalanceAuditSystem`'s corpus sweep, and applies that sweep's same authored-band-0 exclusion. Repointing the razor at it pulled the previously-untested `WithinDriftTolerance` expression under Tier 1 — which is the point of WP1 — and additively renders the authored cell's target range.

**2. Three concurrency fixes outside the plan's scope**, all pre-existing but *made reachable* by request threads reading concurrently with a circuit. Deferred, they would have surfaced as flaky saves with no obvious cause:

- **YamlDotNet's `IDeserializer` is not thread-safe** and all four `ITemplateDeserializer`s shared one instance; concurrent reads corrupted its type cache. Now `ThreadLocal`.
- **`ContentFileReader` used the default `FileShare.Read`**, which let a reader break a concurrent write.
- **The write-temp-then-replace publish was duplicated in seven writers with no retry** — well past `INV-19`'s bar. Extracted to `AtomicFileWrite`.

**A wrong mechanism, corrected — worth recording because the first fix was plausible and false.** The initial implementation changed only the share flags and documented them as what lets the replace proceed. Writing the missing `AtomicFileWrite` test (a non-blocking review finding) disproved that. Measured on Windows with a reader holding the destination:

| | reader `FileShare.Read` | reader `ReadWrite\|Delete` |
|---|---|---|
| `File.Move(overwrite: true)` | fails | **fails** |
| `File.Replace(…)` | fails | **succeeds** |

`Move` cannot rename over a delete-pending destination however the reader opened it, so relaxing the share flags alone did nothing — what had actually silenced the flake was the retry, i.e. the bug was papered over and then documented with a causal story that was not real. `AtomicFileWrite` now uses `File.Replace` (falling back to `Move` only when there is no destination), the two choices are documented as **jointly necessary and neither sufficient**, and the retry is demoted to an explicit backstop for scanner/indexer interference. The lesson generalizes: a concurrency mechanism asserted in a doc comment and never executed by a test is a guess.

**3. `ConnectAsync` judges adjacency in all three axes**, where the razor passed `dz = 0` unconditionally — technically a behavior change, against the plan's "tests characterize existing behavior". The old code was a reachable latent bug (the selection survives the Z-layer switcher, so selecting on one layer and clicking on another wrote a *horizontal* exit between rooms on different planes); preserving it under "characterize existing behavior" would have encoded a defect into a test. The same change grants a vertical connect gesture the grid never had, which is recorded in Flow 29 leg B4.

**Also applied from review:** `AtomicFileWrite.PublishAsync` → `ReplaceAsync` (the original name put seven false positives into the `INV-5` "no `PublishAsync` under `Systems/`" review grep); `403`/`415` added to the published contract, since the filter can return them from any endpoint with a `application/problem+json` body and the consumer hand-writes its client from that document; and the endpoint-coverage test rewritten to enumerate the routing table rather than a hardcoded route list, so an `/api` endpoint mapped *beside* the group instead of into it is caught.

**Discovered and recorded, not fixed:** minimal-API parameter binding runs *before* endpoint filters, so a bodied request with a missing or malformed body answers `400` without `LocalOriginFilter` executing. Not a bypass — binding failed, nothing ran, nothing was written — but it is a real ordering subtlety, now stated in the filter's own docs.

**Follow-ups.**

- **Authentication** is the live remainder of the REST-API backlog item, and the hard prerequisite before any non-loopback bind. The DTO half of that entry's "carries its own auth + DTO surface" stipulation shipped; the auth half did not.
- Still deferred: the full ~30-endpoint surface, write endpoints for kinds other than mob, any endpoint on the telnet game host, and an apply/reload endpoint on this host (it would be an Initiator — a different posture).
- `ConnectAsync` re-derives the whole area proposal per click, where the razor previously reused its materialized positions. Index-served and cheap today; taking blueprint ids rather than a view-model cell is exactly what Postcondition 2 required. Worth knowing if area sizes grow.
- `BalanceStandardsStore` and `SimScenarioStore` still hold shared YamlDotNet deserializer instances. Neither is on the catalog's concurrent read path, so both were left alone — but they carry the same latent hazard if a concurrent caller ever appears.
