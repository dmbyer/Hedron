# CLAUDE.md

Primary entry point for Claude Code (and any agent) working in this repository. Keep this file short. Detailed architecture, reference, use cases, and roadmap live under [`docs/`](docs/).

## What Hedron is

C# MUD (Multi-User Dungeon) game engine targeting .NET 8. The codebase is under active rebuild — the [roadmap](docs/roadmap/plan.md) supersedes any legacy code you encounter.

Project layout (as projects are rebuilt, see [`docs/roadmap/plan.md`](docs/roadmap/plan.md) for the keep list):

- **Core** (`Core/`) — components, ECS primitives, systems, handlers, events, commands
- **Server** (`Server/`) — generic-host console app that runs the telnet listener and owns DI composition
- **Data** (`Data/`) — persistence layer; the substrate landed in Phase 3 slice 1 (see [`docs/roadmap/done.md`](docs/roadmap/done.md))
- **Bot** (`Bot/`) — telnet test bot (deferred; rebuildable when manual multi-client testing gets painful)

## Commands

```bash
dotnet build Hedron.sln
dotnet run --project Server
```

The project is mid-rebuild; the build may be red between phase-exit points. See [`docs/roadmap/plan.md`](docs/roadmap/plan.md). No test framework yet — tracked in [`docs/roadmap/backlog.md`](docs/roadmap/backlog.md).

## Where to read next

Read these in order the first time:

1. [`docs/roadmap/plan.md`](docs/roadmap/plan.md) — strategy, end goal, phase summary, current focus
2. [`docs/roadmap/done.md`](docs/roadmap/done.md) — short ledger of completed phases/slices (full detail in [`docs/roadmap/completed/`](docs/roadmap/completed/))
3. [`docs/architecture/00-overview.md`](docs/architecture/00-overview.md) — the 4-layer model and where code lives
4. [`docs/architecture/02-ecs.md`](docs/architecture/02-ecs.md) — canonical ECS reference
5. [`docs/architecture/03-events.md`](docs/architecture/03-events.md) — event bus and handler ordering
6. [`docs/architecture/04-pitfalls.md`](docs/architecture/04-pitfalls.md) — what to avoid and why

**Reference catalogs** (look up specific pieces):
- [`docs/reference/systems.md`](docs/reference/systems.md) · [`docs/reference/handlers.md`](docs/reference/handlers.md) · [`docs/reference/components.md`](docs/reference/components.md) · [`docs/reference/archetypes.md`](docs/reference/archetypes.md)

**Use cases** (designer scenarios traced through events/handlers/systems — also the per-slice spec): [`docs/use-cases/README.md`](docs/use-cases/README.md)

**Roadmap:** [`docs/roadmap/plan.md`](docs/roadmap/plan.md) · [`docs/roadmap/done.md`](docs/roadmap/done.md) · [`docs/roadmap/backlog.md`](docs/roadmap/backlog.md)

**Documentation map & rules:** [`docs/documentation-architecture.md`](docs/documentation-architecture.md) — how the docs and `.claude/` tooling are organized, what each surface owns, and the discipline that keeps them current (enforced via checklist `INV-D*`).

## Ground rules when writing code

> Every architectural invariant lives in [`docs/architecture/checklist.md`](docs/architecture/checklist.md) (the `INV` list). The lines below are a day-to-day index into it — each summarizes one rule and links to its `INV`. The checklist *defines*; this list *summarizes*. If the two disagree, the checklist wins and this summary is fixed — never treat a line here as the authoritative rule. (Why each rule has one home: [`docs/documentation-architecture.md`](docs/documentation-architecture.md).)

1. **Match the idealized API** — write new code against the documented target; if the target is wrong, fix the doc first (INV-15).
2. **4-layer discipline** — handlers orchestrate → domain systems decide → core systems compute → components hold data; never call upward (INV-1, INV-2).
3. **Component queries, not `is`/`as`** — `entityService.HasComponent<T>(id)`, never `entity is Player` (INV-4).
4. **Systems return results; Initiators and Handlers publish events** — domain & core systems never touch the event bus; commands and the heartbeat are Initiators and may publish (INV-5, INV-8–10).
5. **One world model** — every live entity is in `EntityService`; authored content spawns via `TemplateRegistry`, bespoke entities are built by the owning feature (INV-12).
6. **Entity identity is a `uint`, wrapped as `Entity(uint Id)`** at call sites (INV-13).
7. **Persistence is a two-level opt-in** — `PersistentEntity` opts an entity in; `[Persistent]` on a component type controls snapshot inclusion for already-opted-in entities (INV-14).
8. **Content-tooling discipline** — a slice adding gameplay state ships the tooling to author and inspect it, declared in its use-case **Content tooling impact** section (INV-18).
9. **Infrastructure-discipline parity** — a new player-facing surface, or a pattern repeated ≥3×, lands its supporting framework in the same or an adjacent slice; any runtime flow it changes updates the canonical flows doc (INV-19, INV-17).
10. **Blueprint/instance separation** — a blueprint template is the durable spawn definition; a blueprint instance is the live entity it seeds. When a player interaction makes an instance independent (e.g. item pickup), clear `BlueprintComponent` on the entity so the blueprint slot is free to re-spawn. Admin mutations update both template and entity (INV-21).

When adding a new feature:
- New component → `Core/ECS/Components/<Feature>Component.cs` or `Core/Modules/<Feature>/Components/`
- New domain system → `Core/Modules/<Feature>/Systems/<X>System.cs`
- New handler → `Core/Modules/<Feature>/Handlers/<X>Handler.cs`
- New event → `Core/Modules/<Feature>/Events/<X>Event.cs` (past tense, thin payload)
- Cross-cutting core system → `Core/Systems/<X>System.cs`
- New module entry-point → `Core/Modules/<Feature>/<Feature>Module.cs` — exposes `AddXModule(IServiceCollection)` and is called from `Server/Program.cs`. No `IModule` interface; modules are features, composed via DI extensions.

## Agent tooling available in this repo

The `.claude/` directory provides Claude-Code-native helpers (skills, subagents, slash commands) tuned for this codebase. See [`.claude/README.md`](.claude/README.md) for the index.

The per-slice loop runs **two** `architecture-reviewer` gates: spec-mode (against the use-case doc, before any code) and code-mode (against the diff, before merge). Both enforce [`docs/architecture/checklist.md`](docs/architecture/checklist.md). The full loop is in [`docs/roadmap/plan.md`](docs/roadmap/plan.md) "Phase 3 ground rules".

## If docs and code disagree

The docs describe the target. The roadmap ([`docs/roadmap/plan.md`](docs/roadmap/plan.md)) tracks the phase and keep list. Move code toward the docs; don't rewrite docs to match legacy code. If a design decision in docs turns out to be wrong, update the doc first and call out the change in the PR.
