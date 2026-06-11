# Use Case: Content-Tooling Platform (offline Blazor authoring + bulk generation)

**Status:** planned
**Actors:** Content designer / Administrator (interactive authoring), Developer (bulk content generation)
**Module:** new `Core/Modules/Authoring/` (cross-cutting content-definition catalog + `IContentGenerationSystem`); definition/validation refactors in `Core/Modules/World/`, `Core/Modules/Items/`, `Core/Modules/Mobs/`; new `Hedron.Web` Blazor Server host (or promotion of `Server` to a web SDK) — reuses `CompositionRoot`.

> **Scope note.** This is a *platform* brief spanning two parallel tracks that share one seam. It is expected to **fork into ≥2 sibling slices** when the planner runs: (T1) the headless **bulk-generation** slice, and (T2) the **offline Blazor authoring** slice (itself likely 2–3 work packages). The shared architecture lives here once (INV-D1); per-slice docs reference it.

---

## Description

Content authoring today is command-driven over telnet: each `mk*`/`set*`/`dig` verb is a thin caller of a builder system (`IAreaBuilderSystem`, `IRoomBuilderSystem`, `IItemBuilderSystem`, `IMobBuilderSystem`) and a content writer (`I*ContentWriter`), which mutate the live world and write YAML. This is fine for spot edits but tedious for assembling the volume of rooms/areas/items/mobs needed to deeply test the game. This platform adds two surfaces over the **same** authoring logic: an **offline Blazor Server editor** that reads, edits, validates, and writes the YAML content definitions (applied to the live world via the existing `reload` path, not by mutating the running world), and a **headless bulk-generation system** that programmatically emits swaths of valid YAML content (e.g. N areas across a level range with scaled mobs/items) from a generation spec. Neither surface re-implements authoring logic: both are thin callers of a shared content-definition layer factored out of the existing builders/writers/validator.

---

## Design notes

> Durable seam rationale (survives trim-on-ship, INV-D2).

- **The shared backing is the C# system layer, not an HTTP API.** The integration point between telnet commands, the Blazor editor, and the bulk generator is a set of in-process **content-definition systems** (read / edit / validate / write over the YAML `*Template` types), *not* a REST surface. Each user-facing surface is a thin adapter over those systems — peer to how `MkareaCommand` adapts `IAreaBuilderSystem`. This is why surface **parity is unnecessary**: telnet can stay minimal while the editor grows rich, with zero logic duplication, *provided no authoring logic leaks into a command body, a Blazor component, or the generator* (the fat-controller analogue of a fat command — INV-8 discipline extended to the new surfaces).

- **Authoring writes definitions; the world is refreshed by `reload`.** Offline authoring mutates `content/*.yaml` only — it never touches the live `EntityService`. The apply-to-live mechanism is the existing `reload` Initiator (`ReloadCommand` → `ContentReloadedEvent`), which re-derives world content from YAML. This keeps **one live world** (INV-12) untouched by UI threads and **sidesteps heartbeat concurrency entirely** for v1. Live/instant-preview editing is a deliberately deferred upgrade (see Architecture brief → Deferred).

- **The builders currently fuse two concerns that file-authoring must separate.** `AreaBuilderSystem.CreateArea` (and siblings) both (a) construct + register a template *and* (b) create a live entity. Offline authoring and bulk generation want **(a) without (b)**. The factored seam is a **content-definition layer** owning template construction, mutation, validation, and YAML write; the live `mk*` builders keep their live-spawn half and call the definition layer for the template/write half. One home for validation and DTO shape; no fork (INV-15/INV-19).

- **Validation must become callable on-demand, not only at boot.** `RegistryValidationBootstrap` validates content once at startup (hosted service). Interactive per-edit validation and pre-write generation validation both need that logic injectable. Factor an `IRegistryValidator` (or `IContentValidator`) out of the bootstrap; the bootstrap, the editor, and the generator all call it. This is the INV-19 framework-parity obligation for the new authoring surface.

- **Bulk generation is a domain system over the definition layer, seeded for reproducibility.** `IContentGenerationSystem` composes the definition/writer systems from a generation spec (count, level range, aspect mix, scaling curve) and emits validated YAML — reproducible, reviewable, version-controlled, reloadable. It rolls through the existing `IRandom` seam (INV-26), so a **seeded** run produces a deterministic content set — which is exactly what reproducible scaling-regression tests want. It is keyed by a generation *profile* so it later generalizes to in-game **procedural content** (gameplay-model Spine D / feature-horizon "Procedural / generated areas") — a refactor, not a rewrite.

- **In-process Blazor Server is the foundation of a single, unified web surface.** The end-state is **one** Blazor Server app, hosted in (or alongside) the engine, presenting **three page-suites** over one window and one shared engine DI: **content authoring** (this platform), a **player client**, and **live admin / reporting** (interact with and report on the running world). Hosting it in the engine's generic host (reusing `CompositionRoot`) is the same web-transport investment as the deferred "Web/SignalR dual client" slice. `ISession` already reserves `TransportKey = "signalr"` and output formatters key on transport — the seam was pre-shaped. The three suites differ by *what they touch*: authoring is a content-CRUD surface over YAML (no live world, no game events — what this platform builds); the **player** suite is a real `ISession`/Initiator over the live world (SignalR circuit); the **admin** suite reads the live world for reports and drives it through the *same command/Initiator path* as telnet admin. The player and live-admin suites re-introduce the live-world concurrency-marshaling that file-only authoring sidesteps — so they stay deferred — but every decision made here (host composition, DI reuse, routing, auth) is inherited by them, so the host is shaped as the eventual **superset**, not a single-purpose editor.

- **Host composition is split from service registration so one process can scale from "authoring-only" to "full engine + web."** `CompositionRoot.Register(...)` stays **pure DI** — the engine's `Add*Module` extensions already compose identically for any host. The set of **hosted services** (`TelnetServer`, `HeartbeatBackgroundService`, bootstraps, the future SignalR session host) is composed **by each host**, not baked into `Register`. This is the seam that lets `Hedron.Web` v1 run bootstraps-only (load content for validation; no heartbeat, no sessions — file-only authoring) and the end-state web host run the full superset, with no churn to the shared registration method. A host-role *flag* inside `Register` is rejected: it grows a conditional arm per diverging host, and three surfaces are planned.

---

## Architecture brief

*(In-flight; trimmed on ship.)*

### Placement & spine

- **New module `Core/Modules/Authoring/`** — cross-cutting content-definition catalog facade + `IContentGenerationSystem`. Per-type definition/validation operations stay close to their feature writers (`World`/`Items`/`Mobs`), exposed through the facade so the editor and generator have one entry point. (Planner to firm up: per-feature definition services vs. a single catalog.)
- **New host `Hedron.Web`** (or promote `Server` to `Microsoft.NET.Sdk.Web`) — Blazor Server + the engine, one process, via `CompositionRoot.Register(...)`.
- **Spine:** bulk generation instances **Spine D (Scaling)**; the definition layer is **Spine F (Registry)**-adjacent (it is the authoring face of the YAML-backed template/registry families). No new gameplay spine.

### Seams & recommended homes

| New verb / state / signal | Home | Layer | Notes |
|---|---|---|---|
| Read / edit / validate / write a content **definition** | content-definition layer (factored from builders/writers + `RegistryValidationBootstrap`) | Domain systems | The shared backing for all three surfaces. |
| On-demand content **validation** | `IRegistryValidator` (factored out of the boot hosted-service) | Domain system | Domain-tier: reads ability/aspect/effect registries (domain→domain, INV-1). Called by bootstrap, editor, generator. |
| **Generate** a swath of content from a spec | `IContentGenerationSystem` | Domain system | Composes definition/writer systems; seeded via `IRandom`. |
| Offline **authoring UI** | Blazor Server components | Presentation (new) | Thin over the definition layer; **no** game events, **no** live-world writes. |
| **Apply to live world** | existing `reload` Initiator | Initiator | UI "apply" / generator "load" is a thin caller of `ReloadCommand`/`ContentReloadedEvent`. |
| Bulk-gen / editor **trigger** | CLI flag or admin verb (`gen …`) / Blazor action | Initiator | Thin; logic stays in the systems above. |

### Family disposition (forward generalization)

- **Definition layer over all template families** — *Build general now.* It is barely more than the per-type writers already shipped, and a per-type copy would immediately repeat ≥3× (areas/rooms/items/mobs) — the INV-19 bar. Build the family seam.
- **`IContentGenerationSystem` keyed by a generation profile** — *Shape for later.* Build the dev-facing generator now; key it by a profile/spec and seed via `IRandom` so the eventual in-game procedural-content feature (Spine D) is a refactor. Record the generalization; do not build procedural gameplay now.
- **Web transport / Blazor host** — *Build now, but narrow.* Stand up the in-process Blazor Server host for authoring; do **not** build the player-facing `ISession`-over-SignalR unification yet (that stays slice 14). The host is the shared foundation; the player surface is deferred.

### Observers & contributors

- **Observers.** Authoring writes YAML and (on apply) fires the existing `ContentReloadedEvent` — already observed by `AdminAuditHandler`. Generation runs may warrant a `ContentGeneratedEvent` for audit if triggered in a live/admin context; if generation is a pure offline CLI sweep with no game-rule fan-out, it is a **no-chain Initiator** (INV-10) and publishes nothing. (Planner: decide per trigger.)
- **Contributors.** None — no core aggregator/contributor port (INV-24) is introduced. This is authoring infrastructure, not a computed-stat seam.

### Ordering & timing

- No heartbeat work and no shared-event handler ordering in v1 — authoring is off the tick. The single ordering fact: **definition write must complete before `reload` re-derives** (the editor/generator sequences write → apply), mirroring the existing `mk*` "YAML before audit event" ordering. No INV-7 priority constraint introduced.

### Invariants in tension

- **INV-12 (one live world)** — preserved by *not* mutating the live world from UI/generator threads; apply goes through `reload`.
- **INV-23 (world content is YAML-only, never `PersistentEntity`)** — preserved; authoring and generation produce YAML only, no SQLite.
- **INV-8 (thin initiators) extended to new surfaces** — Blazor components and the generator trigger must stay thin; authoring logic lives in the definition/generation systems. The risk is a "fat component / fat controller" re-creating fat-command anti-patterns.
- **INV-19 (framework parity)** — the new authoring surface obligates the callable-validator and definition-layer factoring in the same track; the bulk generator obligates the seeded `IRandom` path.
- **INV-26 (determinism seam)** — generation randomness flows through `IRandom`, never `Random.Shared`.
- **INV-15 (idealized-API first)** — factor the definition layer rather than letting the Blazor track re-implement template/validation logic.

### Resolved decisions (user intake — do not relitigate)

1. **Authoring target = content files only (offline).** UI/generator write YAML; the live world is refreshed via `reload`. Live-world mutation is **not** in scope for v1.
2. **Transport = Blazor Server in-process** with the engine, reusing `CompositionRoot`; the same host is the foundation for the **unified three-suite web surface** (authoring + player + live admin). No REST/public API for v1 (Blazor Server calls Core in-process).
   - **2a. Host = a separate `Hedron.Web` project** (`Microsoft.NET.Sdk.Web`) that boots the engine via the shared `CompositionRoot.Register(...)`; the telnet-only `Server` stays runnable headless. (`Server` is not converted.)
   - **2b. Hosted-service registration is split out of `Register`** — each host composes its own hosted-service set (telnet / heartbeat / bootstraps / future SignalR sessions). `Register` does pure DI only. This is the seam that scales one process from authoring-only to the full live engine + web superset; a host-role flag inside `Register` is rejected.
   - **2c. Web auth = loopback-only bind for v1**; real authn/z gates any non-local exposure (and is a hard prerequisite before the player/admin suites, which touch the live world).
3. **Bulk generation = durable `IContentGenerationSystem` → YAML**, seeded via `IRandom`, profile-keyed; headless trigger.
4. **Sequencing = parallel tracks.** Bulk-gen (T1) and the Blazor authoring foundation (T2) develop independently; they share only the definition seam, so neither blocks the other.

### Deferred (proposed backlog — see Open questions)

- **Live / instant-preview authoring** (mutate the live world from the editor without a `reload`) — requires marshaling world-mutating work onto the single-threaded game loop (the existing thread-safety backlog item). Defer until instant preview is an actual need.
- **Player web-client suite + live-admin suite** (the other two page-suites of the unified web surface) — both touch the *live* world (player `ISession` over SignalR; admin live-read/interact), so both depend on the same concurrency-marshaling as live-edit, plus the full `ISession` unification of the deferred "Web/SignalR dual client" slice and real web auth. The `Hedron.Web` host and the split hosted-service composition built here are their foundation; they are additive page-suites + hosted services, not a host restructure.
- **REST / public content API** — unnecessary while Blazor Server calls Core in-process; revisit if external/programmatic third-party access is wanted.

---

## Open questions

*(Load-bearing for the planner / spec gate.)*

1. **Module decomposition.** ~~One `Core/Modules/Authoring/` facade over per-feature definition services, or definition services living in each feature module?~~ **Resolved (T2 planner):** single `Core/Modules/Authoring/` catalog facade (`IContentDefinitionCatalog`).
2. **Host shape.** ~~Promote `Server` or add a separate host?~~ **Resolved:** separate `Hedron.Web` (`Microsoft.NET.Sdk.Web`) booting the engine via shared `CompositionRoot`; `Server` stays headless (decision 2a). **Composition:** hosted-service registration split out of `Register` so each host composes its own set (decision 2b) — scales to the superset host.
3. **Auth on the web surface.** ~~What gates the UI?~~ **Resolved:** loopback-only bind for v1 (decision 2c); real auth precedes the live-touching player/admin suites.
4. **Generation trigger.** ~~CLI, admin verb, or both?~~ **Resolved:** headless CLI run-mode is the v1 trigger (no-chain Initiator, INV-10); admin verb is a later thin second caller.
5. **Editing-existing vs. create-new flows.** **Resolved (T2 planner):** list/read for all four types; create + edit + write on areas & rooms in the first slice; items/mobs editing as a follow-up WP.
6. **Bulk-gen room connectivity.** **Resolved:** generate a connected room graph (exits wired) so generated worlds are walkable for scaling tests — not a flat room set.
7. **Validator sequencing.** **Resolved:** the callable `IContentValidator`/`IRegistryValidator` factoring lands first as a shared prerequisite; both tracks consume it (no interim throwaway validation in bulk-gen).

---

## Related

- [`admin-area-authoring.md`](admin-area-authoring.md) · [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — the builder/writer pattern this platform factors and reuses.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — `WorldContentLoader`, `TemplateRegistry`, `content/` layout, the `reload` path.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — "Full-featured content editor (transition from command-driven authoring)" (this brief activates its file-authoring portion); "Web / SignalR dual client" (the deferred player-UI foundation); "Thread-safety review" (the deferred live-edit concurrency work).
- [`../design/feature-horizon.md`](../design/feature-horizon.md) — "Procedural / generated areas" (`[D, E]`), the gameplay generalization of `IContentGenerationSystem`.
- [`../architecture/checklist.md`](../architecture/checklist.md) — invariants in tension: INV-8, INV-10, INV-12, INV-15, INV-19, INV-23, INV-26.
