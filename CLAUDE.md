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

## Ground rules when writing code

1. **Match the idealized API.** `docs/architecture/` and `docs/use-cases/` describe the **target**. New code is written against the target on first attempt. If the target needs to change, update the doc first.
2. **4-layer discipline.** Handlers orchestrate → domain systems decide → core systems compute → components hold data. Never skip layers upward. See [`docs/architecture/01-layers.md`](docs/architecture/01-layers.md).
3. **Component queries, not `is`/`as`.** `entityService.HasComponent<PlayerComponent>(id)` — never `entity is Player`.
4. **Services return results; handlers publish events.** Systems are pure where possible.
5. **One world model.** Every live entity lives in `EntityService`. Authored content is spawned via `TemplateRegistry`; bespoke entities are built with `EntityService.CreateEntity()` + `AddComponent` by the owning feature that needs them.
6. **Entity identity is a wrapper.** `readonly record struct Entity(uint Id)` — the `uint` is authoritative; `Entity` is for flavour at call sites. Components still store `uint` ids when referencing other entities.
7. **Persistence is per-component.** Tag a component type with `[Persistent]` and `PersistenceSystem` includes it on save. An entity is persisted if it has any `[Persistent]` component. Effects split into `PersistentEffectsComponent` (saved) and `TransientEffectsComponent` (session-only). **Two serializers, two audiences:** persistence uses `System.Text.Json` for component snapshots (machine round-trip); content authoring uses YAML via `YamlDotNet` for designer-write files under `data/content/`. They do not share serializer code.
8. **Content-tooling discipline.** Any slice that adds gameplay state must also land the tooling needed to author and exercise that state — data-file shape, admin commands, `TemplateRegistry` entries, etc. The slice's use-case doc must include a **Content tooling impact** section, and the PR must ship the tooling alongside the gameplay code. No gameplay slice merges without a way to populate and inspect the state it adds. See [`docs/roadmap/plan.md`](docs/roadmap/plan.md) ground rules.

When adding a new feature:
- New component → `Core/ECS/Components/<Feature>Component.cs` or `Core/Modules/<Feature>/Components/`
- New domain system → `Core/Modules/<Feature>/Systems/<X>System.cs`
- New handler → `Core/Modules/<Feature>/Handlers/<X>Handler.cs`
- New event → `Core/Modules/<Feature>/Events/<X>Event.cs` (past tense, thin payload)
- Cross-cutting core system → `Core/Systems/<X>System.cs`
- New module entry-point → `Core/Modules/<Feature>/<Feature>Module.cs` — exposes `AddXModule(IServiceCollection)` and is called from `Server/Program.cs`. No `IModule` interface; modules are features, composed via DI extensions.

## Agent tooling available in this repo

The `.claude/` directory provides Claude-Code-native helpers (skills, subagents, slash commands) tuned for this codebase. See [`.claude/README.md`](.claude/README.md) for the index.

## If docs and code disagree

The docs describe the target. The roadmap ([`docs/roadmap/plan.md`](docs/roadmap/plan.md)) tracks the phase and keep list. Move code toward the docs; don't rewrite docs to match legacy code. If a design decision in docs turns out to be wrong, update the doc first and call out the change in the PR.
