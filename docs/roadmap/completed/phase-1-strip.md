# Phase 1 — Strip (completed)

> Completed in commit `107b27d`. Detailed record retained for archaeology only — current planning lives in [`../plan.md`](../plan.md).

## Goal

Demolish everything not on the keep list in a single transactional commit so the new baseline is unambiguous.

## Keep list

- `docs/`
- `Hedron.sln`, `Core.csproj`, `Server.csproj` (retargeted to `net8.0`). `Data.csproj` and `Bot.csproj` deleted; their projects come back later (persistence in Phase 3 slice 1, bot whenever useful).
- Git history (for reference lookup only; not rewritten).
- `Core/ECS/EntityService.cs`, `ComponentRepository.cs`, `IComponent.cs`, `EcsManager.cs` — only the parts that match the idealized API. Drift (e.g. `EntityService`'s `IModule`/`RegisterModule`/`GetModule` machinery referencing a now-deleted interface) was stripped as part of the same commit.

## Demolition list (non-exhaustive)

- `Core/Commands/` — all commands
- `Core/Combat/` — `CombatHandler`, `CombatHelper`, damage helpers
- `Core/Modules/` — all modules (skills, locale wrappers, etc.)
- `Core/ECS/DEPRECATED - Entities/` — entire folder
- `Core/ECS/Components/` — all components (rewritten fresh in Phase 2)
- `Core/ECS/EntityFactory.cs`, `IModule.cs`, `ICopyableObject.cs`, `Properties/` — retired permanently (construction moves into feature systems; modules are DI extensions, not an interface)
- `Core/ECS/EntityArchetype.cs`, `ArchetypeRegistry.cs`, `ArchetypeDefinition.cs` — rebuilt against the target API in Phase 2 in their **validation + detection only** role
- `Core/System/` helpers that only existed to serve legacy types
- `Data/` — entire project (no persistence in MVP; reintroduced for Phase 3 slice 1)
- `Server/Pages/` Blazor admin UI; `Server/` converted from Blazor Server to a plain .NET generic-host console app
- `Bot/` — entire project

## Other Phase 1 actions, same commit

- Bumped target framework from `netcoreapp3.1` to `net8.0` across all csproj files
- Stripped `Server` down to a console `Program.Main` that registers DI services and hosts the (Phase 2) telnet listener
- Deleted `docs/roadmap/api-alignment-plan.md` and `docs/roadmap/ecs-migration-status.md` (superseded)
- Updated `CLAUDE.md`'s "Where to read next" and ground-rules sections to reflect the new plan

## Phase 1.5 — design pass

Resolved between Phase 1 and Phase 2:

- **Ticket A (ECS redesign):** dropped prototype/instance cache, adopted one-world model, `Entity(uint Id)` wrapper, `TemplateRegistry` for authored-content spawning, bespoke construction in domain systems (no `EntityFactory`), archetypes restricted to validation + detection, persistence via `[Persistent]` on component types, effects split into `PersistentEffectsComponent` / `TransientEffectsComponent`. Docs, skills, use cases, and `CLAUDE.md` updated in coordination.
- **Ticket B (admin tooling scope):** deferred at the time; resolved in 2026-05 (see [`../plan.md`](../plan.md) — "Content tooling discipline").

## Exit state

- Keep list is all that remains of non-doc code.
- `dotnet restore` succeeds against `net8.0`.
- `dotnet build Hedron.sln` was green with 4 nullability warnings in the kept ECS files (folded into Phase 2 step 1 and cleared there).

## Wave 0 / Sub-Wave 1A note

The artefacts from the retired wave-based plan were resolved during Phase 1: `EcsManager.cs` stayed, `ICopyableObject.cs` was deleted with everything it served, and namespace touches were mooted by the strip. No further action.
