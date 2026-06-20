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
| `Components/` | Blazor pages/components — the content browser and per-kind editors (area/room/item/mob), each a thin caller of `IContentDefinitionCatalog`. |
| `appsettings.json` | The same engine config the telnet host reads, plus `Web:BindUrl`. |

The authoring backend — `IContentDefinitionCatalog`, `IContentValidator`, `IContentGenerationSystem` — lives in `Core/` (modules `Authoring` and `World`), not in the web project. The web project is presentation only.

---

## Configuration & hosting

- **Bind.** `Web:BindUrl` (default `http://127.0.0.1:5050`) — **loopback-only**. There is no authentication in v1; loopback is the entire security posture. Real authn/z is a hard prerequisite before any non-local bind, and before the live-touching player/admin suites land (tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md)).
- **Config reuse.** The web host consumes the same `appsettings.json` sections via `CompositionRoot` (see [05-configuration.md](05-configuration.md)); only `Web:BindUrl` is web-specific. The `HEDRON_`-prefixed env-var override applies identically.
- **Hosted services.** Exactly two — `WorldContentBootstrap` (so the catalog and validator have content to work against) and `RegistryValidationBootstrap` (fail-fast on invalid content at boot). The split is guarded by `Hedron.Tests/Composition/HostCompositionTests`.

The authoring edit loop (browse → load → edit → validate → save → apply) is traced as the offline-edit leg of **Flow 29** (content-tooling journey) in [flows/README.md](flows/README.md); it reuses Flow 5 (content reload) as its apply leg.

---

## The three-suite end-state (forward design)

The web host is built as the foundation of a single, eventual **three-page-suite** Blazor app over one engine — only the first suite exists today:

| Suite | Touches | Status |
|---|---|---|
| **Content authoring** | YAML only (off the live world) | **built** — this tier |
| **Player client** | the live world, as a real `ISession`/Initiator over a SignalR circuit | deferred |
| **Live admin / reporting** | the live world (read for reports; interact via the same command/Initiator path as telnet admin) | deferred |

The seam was pre-shaped: `ISession` already reserves `TransportKey = "signalr"` and output formatters key on transport, so a Blazor circuit becomes another session type without disturbing the telnet path. The player and live-admin suites are deferred because they **re-introduce live-world mutation from request threads** — the concurrency-marshaling (single-threaded game loop, see [01-layers.md](01-layers.md) heartbeat constraints) that file-only authoring deliberately sidesteps — plus web auth and the full `ISession` unification (the deferred "Web / SignalR dual client" item). Crucially, they land as **additive page-suites + hosted services on this host**, not a host restructure: the split-registration seam already accommodates them.

---

## Related

- [01-layers.md](01-layers.md) — the four layers + the Initiators tier the UI feeds.
- [05-configuration.md](05-configuration.md) — how settings bind; `Web:BindUrl` and the `HEDRON_` override.
- [flows/README.md](flows/README.md) — Flow 29 (content-tooling journey: bulk generate + offline edit); Flow 5 (content reload).
- [../features/admin-authoring/admin-authoring.md](../features/admin-authoring/admin-authoring.md) · [content-authoring.md](../features/admin-authoring/content-authoring.md) · [content-tooling.md](../features/admin-authoring/content-tooling.md) — the feature docs for this tier; durable seam rationale in their design notes.
- [../reference/systems.md](../reference/systems.md) — `IContentDefinitionCatalog`, `IContentValidator`, `IContentGenerationSystem`.
- [checklist.md](checklist.md) — invariants this tier answers to: INV-5, INV-8, INV-12, INV-15, INV-19, INV-22, INV-23.
