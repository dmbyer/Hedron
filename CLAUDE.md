# CLAUDE.md

Primary entry point for Claude Code (and any agent) working in this repository. Keep this file short. Detailed architecture, reference, use cases, and roadmap live under [`docs/`](docs/).

## What Hedron is

C# MUD (Multi-User Dungeon) game engine on .NET Core 3.1. Two surfaces:

- **Server** (`Server/`) — Blazor Server admin UI + telnet game server, running in one process. Admin edits hit a shared cache and take effect live in the running game.
- **Core** (`Core/`) — entities, ECS, gameplay logic, persistence contracts.
- **Data** (`Data/`) — JSON persistence.
- **Bot** (`Bot/`) — telnet bot for testing.

## Commands

```bash
dotnet build Hedron.sln
dotnet run --project Server        # web admin at https://localhost:5001
```

No test framework is wired up yet — see [`docs/roadmap/backlog.md`](docs/roadmap/backlog.md).

## Where to read next

Read these in order the first time:

1. [`docs/architecture/00-overview.md`](docs/architecture/00-overview.md) — the 4-layer model and where code lives
2. [`docs/architecture/02-ecs.md`](docs/architecture/02-ecs.md) — canonical ECS reference
3. [`docs/architecture/03-events.md`](docs/architecture/03-events.md) — event bus and handler ordering
4. [`docs/architecture/04-pitfalls.md`](docs/architecture/04-pitfalls.md) — what to avoid and why

**Reference catalogs** (look up specific pieces):
- [`docs/reference/systems.md`](docs/reference/systems.md) · [`docs/reference/handlers.md`](docs/reference/handlers.md) · [`docs/reference/components.md`](docs/reference/components.md) · [`docs/reference/archetypes.md`](docs/reference/archetypes.md)

**Use cases** (designer scenarios traced through events/handlers/systems): [`docs/use-cases/README.md`](docs/use-cases/README.md)

**Roadmap:** [`docs/roadmap/api-alignment-plan.md`](docs/roadmap/api-alignment-plan.md) · [`docs/roadmap/ecs-migration-status.md`](docs/roadmap/ecs-migration-status.md) · [`docs/roadmap/backlog.md`](docs/roadmap/backlog.md)

## Ground rules when writing code

1. **Match the idealized API.** `docs/architecture/` and `docs/use-cases/` describe the **target** shape (`EntityService`, `IEventBus`, domain systems as classes, handlers publish events). Some code still uses legacy patterns — see the alignment plan for how to migrate rather than matching the legacy style.
2. **4-layer discipline.** Handlers orchestrate → domain systems decide → core systems compute → components hold data. Never skip layers upward. See [`docs/architecture/01-layers.md`](docs/architecture/01-layers.md).
3. **Component queries, not `is`/`as`.** `entityService.HasComponent<PlayerDataComponent>(id)` — never `entity is Player`.
4. **Services return results; handlers publish events.** Systems are pure where possible.
5. **Prototype vs instance is a `PrototypeComponent`, not a type.** Persistence only touches prototypes.

When adding a new feature:
- New component → `Core/ECS/Components/<Feature>Component.cs` or `Core/Modules/<Feature>/Components/`
- New domain system → `Core/Modules/<Feature>/Systems/<X>System.cs`
- New handler → `Core/Modules/<Feature>/Handlers/<X>Handler.cs`
- New event → `Core/Modules/<Feature>/Events/<X>Event.cs` (past tense, thin payload)
- Cross-cutting core system → `Core/Systems/<X>System.cs`

## Agent tooling available in this repo

The `.claude/` directory provides Claude-Code-native helpers (skills, subagents, slash commands) tuned for this codebase. See [`.claude/README.md`](.claude/README.md) for the index (coming in a follow-up).

## If docs and code disagree

The docs describe the target. The alignment plan ([`docs/roadmap/api-alignment-plan.md`](docs/roadmap/api-alignment-plan.md)) tracks the gap and the sequencing to close it. When implementing against the docs, prefer moving code toward the docs rather than rewriting docs to match legacy code. If a specific design decision in docs turns out to be wrong, update the doc first and call out the change in the PR.
