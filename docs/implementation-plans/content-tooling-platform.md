# Use Case: Content-Tooling Platform (offline Blazor authoring + bulk generation)

**Status:** implemented
**Actors:** Content designer / Administrator (interactive authoring), Developer (bulk content generation)
**Module:** new `Core/Modules/Authoring/` (cross-cutting content-definition catalog + `IContentGenerationSystem`); definition/validation refactors in `Core/Modules/World/`, `Core/Modules/Items/`, `Core/Modules/Mobs/`; new `Hedron.Web` Blazor Server host (or promotion of `Server` to a web SDK) — reuses `CompositionRoot`.

> **Scope note.** This is a *platform* brief spanning two parallel tracks that share one seam. It is expected to **fork into ≥2 sibling slices** when the planner runs: (T1) the headless **bulk-generation** slice, and (T2) the **offline Blazor authoring** slice (itself likely 2–3 work packages). The shared architecture lives here once (INV-27); per-slice docs reference it.

---

## Description

Content authoring today is command-driven over telnet: each `mk*`/`set*`/`dig` verb is a thin caller of a builder system (`IAreaBuilderSystem`, `IRoomBuilderSystem`, `IItemBuilderSystem`, `IMobBuilderSystem`) and a content writer (`I*ContentWriter`), which mutate the live world and write YAML. This is fine for spot edits but tedious for assembling the volume of rooms/areas/items/mobs needed to deeply test the game. This platform adds two surfaces over the **same** authoring logic: an **offline Blazor Server editor** that reads, edits, validates, and writes the YAML content definitions (applied to the live world via the existing `reload` path, not by mutating the running world), and a **headless bulk-generation system** that programmatically emits swaths of valid YAML content (e.g. N areas across a level range with scaled mobs/items) from a generation spec. Neither surface re-implements authoring logic: both are thin callers of a shared content-definition layer factored out of the existing builders/writers/validator.

---

## Design notes

> Durable seam rationale (survives trim-on-ship, INV-28).

- **The shared backing is the C# system layer, not an HTTP API.** The integration point between telnet commands, the Blazor editor, and the bulk generator is a set of in-process **content-definition systems** (read / edit / validate / write over the YAML `*Template` types), *not* a REST surface. Each user-facing surface is a thin adapter over those systems — peer to how `MkareaCommand` adapts `IAreaBuilderSystem`. This is why surface **parity is unnecessary**: telnet can stay minimal while the editor grows rich, with zero logic duplication, *provided no authoring logic leaks into a command body, a Blazor component, or the generator* (the fat-controller analogue of a fat command — INV-8 discipline extended to the new surfaces).

- **Authoring writes definitions; the world is refreshed by `reload`.** Offline authoring mutates `content/*.yaml` only — it never touches the live `EntityService`. The apply-to-live mechanism is the existing `reload` Initiator (`ReloadCommand` → `ContentReloadedEvent`), which re-derives world content from YAML. This keeps **one live world** (INV-12) untouched by UI threads and **sidesteps heartbeat concurrency entirely** for v1. Live/instant-preview editing is a deliberately deferred upgrade (tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md)).

- **The builders currently fuse two concerns that file-authoring must separate.** `AreaBuilderSystem.CreateArea` (and siblings) both (a) construct + register a template *and* (b) create a live entity. Offline authoring and bulk generation want **(a) without (b)**. The factored seam is a **content-definition layer** owning template construction, mutation, validation, and YAML write; the live `mk*` builders keep their live-spawn half and call the definition layer for the template/write half. One home for validation and DTO shape; no fork (INV-15/INV-19).

- **Validation must become callable on-demand, not only at boot.** `RegistryValidationBootstrap` validates content once at startup (hosted service). Interactive per-edit validation and pre-write generation validation both need that logic injectable. Factor a callable `IContentValidator` out of the bootstrap; the bootstrap, the editor, and the generator all call it. This is the INV-19 framework-parity obligation for the new authoring surface.

- **Bulk generation is a domain system over the definition layer, seeded for reproducibility.** `IContentGenerationSystem` composes the definition/writer systems from a generation spec (count, level range, aspect mix, scaling curve) and emits validated YAML — reproducible, reviewable, version-controlled, reloadable. It rolls through the existing `IRandom` seam (INV-26), so a **seeded** run produces a deterministic content set — which is exactly what reproducible scaling-regression tests want. It is keyed by a generation *profile* so it later generalizes to in-game **procedural content** (gameplay-model Spine D / feature-horizon "Procedural / generated areas") — a refactor, not a rewrite.

- **In-process Blazor Server is the foundation of a single, unified web surface.** The end-state is **one** Blazor Server app, hosted in (or alongside) the engine, presenting **three page-suites** over one window and one shared engine DI: **content authoring** (this platform), a **player client**, and **live admin / reporting** (interact with and report on the running world). Hosting it in the engine's generic host (reusing `CompositionRoot`) is the same web-transport investment as the deferred "Web/SignalR dual client" slice. `ISession` already reserves `TransportKey = "signalr"` and output formatters key on transport — the seam was pre-shaped. The three suites differ by *what they touch*: authoring is a content-CRUD surface over YAML (no live world, no game events — what this platform builds); the **player** suite is a real `ISession`/Initiator over the live world (SignalR circuit); the **admin** suite reads the live world for reports and drives it through the *same command/Initiator path* as telnet admin. The player and live-admin suites re-introduce the live-world concurrency-marshaling that file-only authoring sidesteps — so they stay deferred — but every decision made here (host composition, DI reuse, routing, auth) is inherited by them, so the host is shaped as the eventual **superset**, not a single-purpose editor.

- **Host composition is split from service registration so one process can scale from "authoring-only" to "full engine + web."** `CompositionRoot.Register(...)` stays **pure DI** — the engine's `Add*Module` extensions already compose identically for any host. The set of **hosted services** (`TelnetServer`, `HeartbeatBackgroundService`, bootstraps, the future SignalR session host) is composed **by each host**, not baked into `Register`. This is the seam that lets `Hedron.Web` v1 run bootstraps-only (load content for validation; no heartbeat, no sessions — file-only authoring) and the end-state web host run the full superset, with no churn to the shared registration method. A host-role *flag* inside `Register` is rejected: it grows a conditional arm per diverging host, and three surfaces are planned.

---

## Related

- [`admin-area-authoring.md`](admin-area-authoring.md) · [`bare-bones-content-spawning.md`](bare-bones-content-spawning.md) — the builder/writer pattern this platform factors and reuses.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — `WorldContentLoader`, `TemplateRegistry`, `content/` layout, the `reload` path.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — "Full-featured content editor (transition from command-driven authoring)" (this brief activates its file-authoring portion); "Web / SignalR dual client" (the deferred player-UI foundation); "Thread-safety review" (the deferred live-edit concurrency work).
- [`../design/feature-horizon.md`](../design/feature-horizon.md) — "Procedural / generated areas" (`[D, E]`), the gameplay generalization of `IContentGenerationSystem`.
- [`../architecture/checklist.md`](../architecture/checklist.md) — invariants in tension: INV-8, INV-10, INV-12, INV-15, INV-19, INV-23, INV-26.
