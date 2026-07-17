# CLAUDE.md

Primary entry point for Claude Code (and any agent) working in this repository. Keep this file short. Detailed architecture, features, reference, implementation plans, and roadmap live under [`docs/`](docs/).

## What Hedron is

C# MUD (Multi-User Dungeon) game engine targeting .NET 8. The Phase 1–3 salvage rebuild is complete — the [roadmap](docs/roadmap/plan.md) now tracks the content-baseline → MVP phases; any stray legacy code you encounter is superseded by the docs.

Project layout:

- **Core** (`Core/`) — components, ECS primitives, systems, handlers, events, commands
- **Server** (`Server/`) — generic-host console app that runs the telnet listener and owns DI composition
- **Data** (`Data/`) — persistence layer; the substrate landed in Phase 3 slice 1 (see [`docs/roadmap/done.md`](docs/roadmap/done.md))
- **Bot** (`Bot/`) — telnet test bot (deferred; rebuildable when manual multi-client testing gets painful)
- **Tests** (`Hedron.Tests/`) — xUnit suite (system / handler / flow / persistence / architecture-guard tiers); **live** and enforced on every PR via CI (INV-25) — strategy in [`docs/architecture/07-testing.md`](docs/architecture/07-testing.md)

## Commands

```bash
dotnet build Hedron.sln
dotnet run --project Server
dotnet test Hedron.sln          # verification gate (INV-25) — live; enforced on every PR via CI
```

Every merged branch leaves the build **and** `dotnet test` green (INV-25). See [`docs/roadmap/plan.md`](docs/roadmap/plan.md). The testing strategy is defined ([`docs/architecture/07-testing.md`](docs/architecture/07-testing.md)); the `Hedron.Tests` suite is **live** — full harness + architecture-guard tiers, enforced on every PR via CI — with remaining backfill draining via the on-touch ratchet ([`docs/roadmap/backlog.md`](docs/roadmap/backlog.md)).

## Where to read next

Read these in order the first time:

1. [`docs/roadmap/plan.md`](docs/roadmap/plan.md) — strategy, end goal, phase summary, current focus
2. [`docs/roadmap/done.md`](docs/roadmap/done.md) — short ledger of completed phases/slices (full detail in [`docs/roadmap/completed/`](docs/roadmap/completed/))
3. [`docs/architecture/00-overview.md`](docs/architecture/00-overview.md) — the 4-layer model and where code lives
4. [`docs/architecture/02-ecs.md`](docs/architecture/02-ecs.md) — canonical ECS reference
5. [`docs/architecture/03-events.md`](docs/architecture/03-events.md) — event bus and handler ordering
6. [`docs/architecture/04-pitfalls.md`](docs/architecture/04-pitfalls.md) — what to avoid and why
7. [`docs/architecture/07-testing.md`](docs/architecture/07-testing.md) — testing strategy: the tiers, what to test vs. skip, the harness

**Features** (holistic, player-facing — what a capability is and how it composes its systems; the per-system design docs live beside each one): [`docs/features/README.md`](docs/features/README.md)

**Reference catalogs** (look up specific pieces):
- [`docs/reference/systems.md`](docs/reference/systems.md) · [`docs/reference/handlers.md`](docs/reference/handlers.md) · [`docs/reference/components.md`](docs/reference/components.md) · [`docs/reference/archetypes.md`](docs/reference/archetypes.md)

**Implementation plans** (transient per-slice build artifacts — behavior spec + build plan, deleted on ship): [`docs/implementation-plans/README.md`](docs/implementation-plans/README.md)

**Roadmap:** [`docs/roadmap/plan.md`](docs/roadmap/plan.md) · [`docs/roadmap/done.md`](docs/roadmap/done.md) · [`docs/roadmap/backlog.md`](docs/roadmap/backlog.md)

**Documentation map & rules:** [`docs/architecture/09-documentation.md`](docs/architecture/09-documentation.md) — how the docs and `.claude/` tooling are organized, what each surface owns, and the discipline that keeps them current (enforced via checklist `INV-27`–`INV-30`).

## Ground rules when writing code

> Every architectural invariant lives in [`docs/architecture/checklist.md`](docs/architecture/checklist.md) (the `INV` list). The lines below are a day-to-day index into it — each summarizes one rule and links to its `INV`. The checklist *defines*; this list *summarizes*. If the two disagree, the checklist wins and this summary is fixed — never treat a line here as the authoritative rule. (Why each rule has one home: [`docs/architecture/09-documentation.md`](docs/architecture/09-documentation.md).)

1. **Match the idealized API** — write new code against the documented target; if the target is wrong, fix the doc first (INV-15).
2. **4-layer discipline** — handlers orchestrate → domain systems decide → core systems compute → components hold data; never call upward (INV-1, INV-2).
3. **Component queries, not `is`/`as`** — `entityService.HasComponent<T>(id)`, never `entity is Player` (INV-4).
4. **Systems return results; Initiators and Handlers publish events** — domain & core systems never touch the event bus; commands and the heartbeat are Initiators and may publish (INV-5, INV-8–10).
5. **One world model** — every live entity is in `EntityService`; authored content spawns via `TemplateRegistry`, bespoke entities are built by the owning feature (INV-12).
6. **Entity identity is a `uint`, wrapped as `Entity(uint Id)`** at call sites (INV-13).
7. **Persistence is a two-level opt-in** — `PersistentEntity` opts an entity in; `[Persistent]` on a component type controls snapshot inclusion for already-opted-in entities (INV-14).
8. **Content-tooling discipline** — a slice adding gameplay state ships the tooling to author and inspect it, declared in its implementation plan's **Content tooling impact** section (INV-18).
9. **Infrastructure-discipline parity** — a new player-facing surface, or a pattern repeated ≥3×, lands its supporting framework in the same or an adjacent slice; any runtime flow it changes updates the canonical flows doc (INV-19, INV-17).
10. **Blueprint/instance separation** — a blueprint template is the durable spawn definition; a blueprint instance is the live entity it seeds. When a player interaction makes an instance independent (e.g. item pickup), clear `BlueprintComponent` on the entity so the blueprint slot is free to re-spawn. Admin mutations update both template and entity (INV-21).
11. **Verification discipline** — a slice that adds/changes a system, persistence shape, validation, or Main Flow ships tests for it (the implementation plan's **Test plan** section), per the rubric in [`docs/architecture/07-testing.md`](docs/architecture/07-testing.md); chance/time-dependent system logic resolves through an injected seam (`IRandom`, heartbeat timestamp), never `Random.Shared`/`DateTime.Now` (INV-25, INV-26). "Ship green" = build **and** `dotnet test` green.
12. **Declared concurrency posture** — cross-thread state is guarded or confined; a slice adding a background initiator, web-host call path, or shared singleton state names which in its plan, and no new thread mutates live world components outside the established session/heartbeat paths (INV-31).

When adding a new feature:
- New component → `Core/ECS/Components/<Feature>Component.cs` or `Core/Modules/<Feature>/Components/`
- New domain system → `Core/Modules/<Feature>/Systems/<X>System.cs`
- New handler → `Core/Modules/<Feature>/Handlers/<X>Handler.cs`
- New event → `Core/Modules/<Feature>/Events/<X>Event.cs` (past tense, thin payload)
- Cross-cutting core system → `Core/Systems/<X>System.cs`
- New module entry-point → `Core/Modules/<Feature>/<Feature>Module.cs` — exposes `AddXModule(IServiceCollection)` and is called from `Server/Program.cs`. No `IModule` interface; modules are features, composed via DI extensions.
- New test → `Hedron.Tests/<MirroredNamespace>/` — pick the tier with the **add-tests** skill ([`docs/architecture/07-testing.md`](docs/architecture/07-testing.md))

**Naming: resolve namespace/type collisions by renaming the type.** If a module namespace (`Core/Modules/<Feature>/`) and a type in `Core/ECS/Components/` share the same simple name, C# resolves the name as the namespace component and the type becomes unreachable via normal `using` imports. Fix by renaming the type — do not use `::` qualifier workarounds. Convention: add a `Flags` suffix to `[Flags]` enums (e.g. `EntityStateFlags` rather than `EntityState`); choose a more specific name for other types. Precedent: `EntityStateFlags` (disambiguated from the `Modules/EntityState/` namespace).

## Agent tooling available in this repo

The `.claude/` directory provides Claude-Code-native helpers (skills, subagents, slash commands) tuned for this codebase. See [`.claude/README.md`](.claude/README.md) for the index.

The per-slice loop runs **two** `architecture-reviewer` gates: spec-mode (against the implementation plan, before any code) and code-mode (against the diff, before merge). Both enforce [`docs/architecture/checklist.md`](docs/architecture/checklist.md). The full loop is in [`docs/roadmap/plan.md`](docs/roadmap/plan.md) "The per-slice loop".

## If docs and code disagree

The docs describe the target. The roadmap ([`docs/roadmap/plan.md`](docs/roadmap/plan.md)) tracks the phase and keep list. Move code toward the docs; don't rewrite docs to match legacy code. If a design decision in docs turns out to be wrong, update the doc first and call out the change in the PR.
