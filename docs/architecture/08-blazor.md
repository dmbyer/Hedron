# Web / UI Host (Blazor Server)

Hedron runs as **two hosts over one engine**. The engine (`Core/`) is composed by `Server/CompositionRoot.Register` (pure DI); each host boots that same composition and then adds **its own** hosted-service set:

- **`Server/`** — the telnet game host. Adds `AddGameplayHostedServices` (startup bootstraps + persistence flush + telnet listener + heartbeat). This is the live game.
- **`Hedron.Web/`** — an in-process **Blazor Server** host for **offline content authoring**. Adds `AddContentBootstrapHostedServices` (content load + registry validation only — no telnet, no heartbeat, no persistence). Binds loopback-only and writes YAML through `IContentDefinitionCatalog`; it never mutates the live world.

This document explains the web/UI tier: where it sits in the layer model, the rules its components follow, and the forward shape it is built toward. Blazor is the implementation; the tier is "a presentation surface over the engine."

> The authoritative invariant list is [checklist.md](checklist.md). This doc is the *explanation* — it cites `INV-n` and does not restate rules. Where it and the checklist disagree, the checklist wins.

---

## The two-host model

```
                      ┌──────────────────────────────┐
                      │  CompositionRoot.Register     │   pure DI — the engine,
                      │  (Core engine, one per host)  │   identical for every host
                      └───────────────┬──────────────┘
            ┌─────────────────────────┴─────────────────────────┐
            ▼                                                     ▼
┌───────────────────────────┐                   ┌───────────────────────────────┐
│  Server/  (telnet host)   │                   │  Hedron.Web/  (Blazor host)   │
│  AddGameplayHostedServices │                   │  AddContentBootstrapHosted-   │
│  · bootstraps              │                   │  Services                     │
│  · persistence flush       │                   │  · content load               │
│  · TelnetServer            │                   │  · registry validation        │
│  · HeartbeatBackgroundSvc  │                   │  (no telnet/heartbeat/SQLite) │
│  → the live game           │                   │  → loopback authoring UI      │
└───────────────────────────┘                   └───────────────────────────────┘
```

**The seam is the split hosted-service registration.** `Register` does DI only; hosted services are composed **per-host**, never inside `Register`. A host-role *flag* inside `Register` was deliberately rejected — it grows a conditional arm per host, and more hosts/surfaces are planned (see [the three-suite end-state](#the-three-suite-end-state-forward-design)). This is what lets one process scale from "authoring-only" to "full engine + web" without reshaping the shared composition. `Hedron.Web` references the `Server` project for `CompositionRoot` and the bootstrap types; `Server` stays a plain headless Exe with no web dependency.

`Hedron.Web` composes **no** event subscriptions in v1 (all `bus.Subscribe(...)` wiring lives in `Server/Program.cs`): authoring is off the bus and the host hydrates no players. The one live-world touch — "Apply to live" — goes through the existing reload Initiator, which owns its own publish.

---

## Where the UI sits in the layer model

The web UI is **not** one of the four processing layers ([01-layers.md](01-layers.md)). Like commands, it is an **entry-point / presentation surface** that feeds the stack — it belongs with the **Initiators** tier in spirit. Two interaction shapes:

1. **Read / author (no live world).** A Blazor component calls a **domain system** directly — `IContentDefinitionCatalog` (list/load/create/validate/write over YAML) — and renders the result. This is a synchronous read/write over content definitions, off the heartbeat.
2. **Effect the live world.** The component calls the existing **reload Initiator** (`IWorldContentLoader.ReloadAsync` → `ContentReloadedEvent`); the Initiator publishes, the engine re-derives world content from YAML. The component itself never mutates `EntityService`.

### Discipline for UI components (the rules that keep this clean)

- **Components are thin — the [INV-8](checklist.md) thin-surface discipline extends to the UI.** A component parses form input, calls a domain system, and renders the result. **No game or authoring logic in a component** (validation, serialization, kind-dispatch, ID generation): that is the "fat component = fat command" anti-pattern, and it lives in the catalog/validator instead. Form-binding glue (collection add/remove, CSV split) is presentation plumbing, not logic — that is allowed.
- **Authoring never mutates the live world** ([INV-12](checklist.md), [INV-22/23](checklist.md)). The catalog writes YAML only — no `EntityService.CreateEntity`, no `PersistentEntity`, no `SaveEntityAsync`. World content stays YAML-only; the live world is refreshed exclusively by `reload`. This is what keeps the editor entirely **off the heartbeat** and defers all live-world concurrency.
- **Publishing stays in the Initiator, not the component** ([INV-5](checklist.md)). The catalog and validator are domain systems that return results and never touch the bus; the apply action reuses the reload Initiator's publish.
- **No authoring logic is trapped in a UI** ([INV-15/19](checklist.md)). The same `IContentDefinitionCatalog` backs the editor, the bulk generator, and (logically) the telnet `mk*` commands — surface parity is unnecessary precisely because the logic is shared. A new content-mutating capability adds to a system, not a component.

These are the same constraints the `architecture-reviewer` applies to a `Hedron.Web` diff in code mode; testing treats Blazor components as **presentation skip-tier** ([07-testing.md](07-testing.md)) because the logic they call is already covered.

---

## Project structure (`Hedron.Web/`)

| Piece | Role |
|---|---|
| `Hedron.Web.csproj` (`Microsoft.NET.Sdk.Web`) | The web host project; references `Server` (for `CompositionRoot` + bootstrap types), transitively `Core`. |
| `Program.cs` | Boots the engine via `CompositionRoot.Register` + `AddContentBootstrapHostedServices`, adds Blazor Server, binds loopback (`Web:BindUrl`). |
| `Components/` | Blazor pages/components — the content browser and per-kind editors (area/room/item/mob), the Standards page, and the Simulation page, each a thin caller of a `Core/` system. |
| `Services/` | Web-host-only supporting code that is not presentation — a background-job registry (`SimulationRunService`), the Integrity page's off-thread sweep (`ContentIntegritySweepService`), and scenario/prefill composers (`BaselineSweep`, `SimulationPrefill`, sim-3). See [Background tooling jobs](#background-tooling-jobs-sim-3) below. |
| `appsettings.json` | The same engine config the telnet host reads, plus `Web:BindUrl`. |

The authoring backend — `IContentDefinitionCatalog`, `IContentValidator`, `IContentGenerationSystem` — lives in `Core/` (modules `Authoring` and `World`), not in the web project. Most of the web project is presentation only; `Services/` is the one deliberate exception (below), and it is still not *authoring* logic — no YAML write, no live-world touch.

---

## Configuration & hosting

- **Bind.** `Web:BindUrl` (default `http://127.0.0.1:5050`) — **loopback-only**. There is no authentication in v1; loopback is the entire security posture. Real authn/z is a hard prerequisite before any non-local bind, and before the live-touching player/admin suites land (tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md)).
- **Config reuse.** The web host consumes the same `appsettings.json` sections via `CompositionRoot` (see [05-configuration.md](05-configuration.md)); only `Web:BindUrl` is web-specific. The `HEDRON_`-prefixed env-var override applies identically.
- **Hosted services.** Exactly two — `WorldContentBootstrap` (so the catalog and validator have content to work against) and `RegistryValidationBootstrap` (fail-fast on invalid content at boot). The split is guarded by `Hedron.Tests/Composition/HostCompositionTests`.

The authoring edit loop (browse → load → edit → validate → save → apply) is traced as the offline-edit leg of **Flow 29** (content-tooling journey) in [flows/README.md](flows/README.md); it reuses Flow 5 (content reload) as its apply leg.

---

## Background tooling jobs (sim-3)

The Simulation page (`/simulation`) introduced the web host's **first background-job pattern** — a designer launches a batch simulation run that takes real wall-clock time, wants live progress, and wants to cancel it, all without blocking the Blazor circuit or re-introducing live-world concurrency concerns. The shape, generalizable to a second instance before it earns a shared framework (INV-19's bar):

- **A host-singleton registry, not a hosted service.** `SimulationRunService` is `AddSingleton`-registered in `Hedron.Web/Program.cs` (not the shared `CompositionRoot`, since it is a web-host UI concern, not engine composition). It owns an in-memory FIFO queue and per-run status records; a plain background `Task` drains the queue one run at a time (the engine already saturates cores per batch — concurrent batches would oversubscribe). It needs no `IHostedService` lifecycle: it does no work when the queue is empty.
- **Polling, not push.** Pages read current state via `Snapshot()` on a timer (~750 ms) rather than a SignalR/callback push. Rationale: a singleton survives circuit navigation/disconnect/reconnect (unlike state scoped to one circuit); the read is in-memory (no I/O per tick); and batches are short enough that sub-second polling is indistinguishable from streaming — so no additional push machinery earns its cost.
- **Cancellation is cooperative**, wired through to the engine's own `CancellationToken` parameter (an additive engine seam, not a web-side abort) — the engine is the only place a running batch can actually stop between iterations.
- **No bus events, no live-world touch.** Run completion is `SimRunStatus` state inside the singleton — there is no live-world observer for an offline simulation, so publishing would have no subscriber and no purpose (INV-5/INV-10 still hold: the engine itself publishes nothing; the web host is not an Initiator in the bus sense here).
- **The engine stays a pure callee.** `SimulationRunService` calls `ISimulationRunner.Run`/`ISimScenarioStore`/`ISimReportWriter` exactly as the CLI `simulate` run-mode does — same validation, same verdict math, same report artifact (INV-19). The service adds queueing/status/cancellation around the call, not a second copy of the engine.

**Promotion trigger (recorded, not built speculatively):** if a second long-running editor job wants the same queue/progress/cancel shape (candidate: sim-5's bulk conformance apply), generalize `SimulationRunService` into a shared web-job service rather than hand-rolling a second one — see [`../roadmap/backlog.md`](../roadmap/backlog.md).

**Trigger examined and not fired (authoring-editor-repair).** `ContentIntegritySweepService` runs the Integrity page's two corpus sweeps (`IContentReferenceIndex.SweepBroken`, `IBalanceAuditSystem.Audit`) on a background `Task` and exposes a status snapshot the page polls — the same *snapshot* shape, but **progress-only: no queue, no cancellation**. It therefore does not meet the trigger's shape and was written standalone. The bulk conformance apply on that same page still runs blocking on the circuit thread; that is the job that fires the trigger when it moves.

**Stateful, cached `Core/` systems reached from a circuit.** A page may call a `Core/` domain system that keeps a cache (`IContentDefinitionCatalog` is the first). Those are DI singletons reached concurrently from multiple circuits, so their concurrency posture is the *system's* to declare (INV-31), not the page's — see [`../reference/systems.md`](../reference/systems.md) and the `add-domain-system` skill. A component must not compensate with its own cache or its own lock.

---

## The three-suite end-state (forward design)

The web host is built as the foundation of a single, eventual **three-page-suite** Blazor app over one engine — only the first suite exists today:

| Suite | Touches | Status |
|---|---|---|
| **Content authoring** | YAML only (off the live world) | **built** — this tier |
| **Player client** | the live world, as a real `ISession`/Initiator over a SignalR circuit | deferred |
| **Live admin / reporting** | the live world (read for reports; interact via the same command/Initiator path as telnet admin) | deferred |

The seam is **partly** pre-shaped: `ISession` reserves `TransportKey = "signalr"` and output formatters key on transport, so a new session type slots in without disturbing the telnet path. Two corrections to how far that carries (found by the 2026-08 client-tier analysis — see [`../design/client-tier.md`](../design/client-tier.md)):

- **The formatter seam is text-shaped, not client-shaped.** `IOutputFormatter.Format` returns `string` and `ISession.SendLineAsync` takes `string`, so `TransportKey` buys another *text* transport. A rich web client wants the typed output messages (`RoomDescriptionMessage`, `ScoreDisplayMessage`, …) as **structured JSON it can lay out**, which needs a parallel structured formatter — real work, and unrelated to which front-end framework renders it.
- **`LoginFlow` is closer than the rest.** Its only transport coupling is a single `ReadLineAsync` on a `StreamReader`; an `ILineReader` extraction opens the whole login state machine to any transport.

The player and live-admin suites are deferred because they **re-introduce live-world mutation from request threads** — the concurrency-marshaling (single-threaded game loop, see [01-layers.md](01-layers.md) heartbeat constraints) that file-only authoring deliberately sidesteps — plus web auth and the full `ISession` unification (the deferred "Web / SignalR dual client" item). That blocker is **framework-independent**: it applies equally to a Blazor circuit and a React client over a hub. Crucially, they land as **additive page-suites + hosted services on this host**, not a host restructure: the split-registration seam already accommodates them.

> **This table's third column is under review.** Whether the player client (and the editor with it) stays Blazor or moves to React + SignalR is an open decision with a scheduled gate at the Phase 5 → 6 boundary — [`../design/client-tier.md`](../design/client-tier.md). The two-host model, the split-registration seam, and the component discipline above are unaffected either way; what the gate decides is the *presentation* stack.

---

## Related

- [01-layers.md](01-layers.md) — the four layers + the Initiators tier the UI feeds.
- [05-configuration.md](05-configuration.md) — how settings bind; `Web:BindUrl` and the `HEDRON_` override.
- [flows/README.md](flows/README.md) — Flow 29 (content-tooling journey: bulk generate + offline edit); Flow 33 (simulation run journey, incl. the editor legs); Flow 5 (content reload).
- [../features/admin-authoring/admin-authoring.md](../features/admin-authoring/admin-authoring.md) · [content-authoring.md](../features/admin-authoring/content-authoring.md) · [content-tooling.md](../features/admin-authoring/content-tooling.md) — the feature docs for this tier; durable seam rationale in their design notes.
- [../features/simulation/simulation.md](../features/simulation/simulation.md) — the sim-3 editor surface the background-tooling-job section above documents.
- [../design/client-tier.md](../design/client-tier.md) — the open Blazor-vs-React + SignalR decision, its evidence base, and the Phase 5 → 6 gate that settles it.
- [../reference/systems.md](../reference/systems.md) — `IContentDefinitionCatalog`, `IContentValidator`, `IContentGenerationSystem`, `SimulationRunService`.
- [checklist.md](checklist.md) — invariants this tier answers to: INV-5, INV-8, INV-12, INV-15, INV-19, INV-22, INV-23.
