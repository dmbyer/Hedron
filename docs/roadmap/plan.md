# Roadmap

> **Purpose.** Sequences the work from "legacy codebase we're stripping" to "production MUD built on the target architecture." Replaces the earlier wave-based plan — that plan assumed incremental migration, which was the wrong posture given the legacy code is mostly skeleton rather than working gameplay.

## Posture

We are **salvaging, not migrating**. The `docs/architecture/` target is authoritative. The existing `Core/Commands/`, `Core/Combat/`, `Core/Modules/`, and `Core/ECS/DEPRECATED -*` trees are reference material for *intent* only — their implementations will not survive. Build red is acceptable between named assembly points; the game does not need to run during Phase 1 or Phase 2.

The target is defined by:
- [`architecture/00-overview.md`](../architecture/00-overview.md) through [`architecture/04-pitfalls.md`](../architecture/04-pitfalls.md) — the 4-layer model, ECS, events, and common traps
- [`reference/`](../reference/) — catalogs of components, systems, handlers, archetypes
- [`use-cases/`](../use-cases/) — designer scenarios that each eventual vertical slice implements
- [`mvp.md`](mvp.md) — the frozen Phase-2 exit criterion

## Phases

### Phase 1 — Strip

Demolish everything not on the keep list. Single transactional commit so the new baseline is unambiguous.

**Keep list:**
- `docs/`
- `Hedron.sln`, `Core.csproj`, `Server.csproj` (retargeted to `net8.0` as part of this phase — see below). `Data.csproj` and `Bot.csproj` are deleted; their projects come back later (persistence in Phase 3 slice 1, bot whenever useful).
- Git history (for reference lookup only; we do not rewrite it)
- `Core/ECS/EntityService.cs`, `ComponentRepository.cs`, `IComponent.cs`, `EcsManager.cs` **only if** they match the idealized API. Where they've drifted (e.g. `EntityService`'s `IModule`/`RegisterModule`/`GetModule` machinery referencing a now-deleted interface), drift is stripped as part of this commit.

**Demolition list (non-exhaustive):**
- `Core/Commands/` — all commands
- `Core/Combat/` — `CombatHandler`, `CombatHelper`, damage helpers
- `Core/Modules/` — all modules (skills, locale wrappers, etc.)
- `Core/ECS/DEPRECATED - Entities/` — entire folder
- `Core/ECS/Components/` — all components (rewritten fresh in Phase 2)
- `Core/ECS/EntityFactory.cs`, `IModule.cs`, `ICopyableObject.cs`, `Properties/` — retired permanently (construction moves into feature systems; modules are DI extensions, not an interface)
- `Core/ECS/EntityArchetype.cs`, `ArchetypeRegistry.cs`, `ArchetypeDefinition.cs` — rebuilt against the target API in Phase 2 in their new **validation + detection only** role
- `Core/System/` helpers that only existed to serve legacy types
- `Data/` — entire project (no persistence in MVP; project reintroduced for Phase 3 slice 1)
- `Server/Pages/` Blazor admin UI; `Server/` converted from Blazor Server to a plain .NET generic-host console app
- `Bot/` — entire project (not needed for MVP; rebuildable when manual multi-client testing gets painful)

**Other Phase 1 actions, same commit:**
- Bump target framework from `netcoreapp3.1` to `net8.0` across all csproj files
- Strip `Server` down to a console `Program.Main` that registers DI services and hosts the telnet listener added in Phase 2
- Delete `docs/roadmap/api-alignment-plan.md` and `docs/roadmap/ecs-migration-status.md` (superseded by this doc)
- Update `CLAUDE.md`'s "Where to read next" and ground-rules sections to reflect the new plan

**Exit criterion:** keep list is all that remains of non-doc code; `dotnet restore` succeeds against net8.0; build is red but only because Phase 2 hasn't started.

### Phase 2 — Foundation

Build the target architecture from scratch, tuned for MVP. Each numbered item is a commit-sized chunk. Order matters only where noted.

1. **Audit kept ECS primitives.** Verify `EntityService`, `ComponentRepository`, `IComponent`, `EcsManager` match the shapes described in [`architecture/02-ecs.md`](../architecture/02-ecs.md): one world, `Entity` wrapper, `CreateEntity` / `TryGet` / `Query` surface. Rewrite any that have drifted. Do not preserve drift. Fold in the 4 nullability warnings from the kept files.
2. **Event bus.** `IEventBus`, in-memory `EventBus` implementation, `IEvent`, `IEventHandler<T>`, `HandlerPriority` per [`architecture/03-events.md`](../architecture/03-events.md). Registered as a DI singleton.
3. **Handler and system contracts.** Already subsumed — step 2 delivers the handler contract (`IEventHandler<T>`), and the architecture docs deliberately don't define shared `ISystem`/`ICoreSystem`/`IDomainSystem` bases. Each system declares its own per-feature interface (`IDiceSystem`, `ICombatSystem`, …) alongside its implementation when the system is built. No separate commit required.
4. **Command dispatcher.** Verb parser + per-verb handler registration. Does not care about gameplay; just "given a session and a line of text, route to the right handler."
5. **Telnet session layer.** TCP listener, per-connection session object, line-based I/O, session → player-entity binding. Rebuilt from scratch, not ported.
6. **MVP components.** `PlayerComponent`, `LocationComponent`, `RoomComponent` as defined in `mvp.md`. Pure data, nested properties where appropriate.
7. **MVP systems and handlers.** `MovementSystem`, `BroadcastSystem`, `PlayerMovedHandler`, `PlayerSaidHandler`.
8. **MVP commands.** `LookCommand`, `MoveCommand` (handles all six directions), `SayCommand`. Thin — parse args, call domain system.
9. **World bootstrap.** Hardcoded three-room world constructed at host startup.
10. **End-to-end smoke.** Two telnet clients, all six MVP behaviors demonstrated.

**Exit criterion:** MVP acceptance test in `mvp.md` passes.

### Phase 3 — Vertical slices

One feature at a time. Each slice:

1. Pick the next use-case file from `docs/use-cases/` (or author a new one)
2. Plan via the `use-case-planner` agent — produces the component/system/handler/event list and file plan
3. Implement
4. Review via the `architecture-reviewer` agent before merge
5. Ship green

Suggested slice order — cheapest-first, biggest-unlock-first:

| # | Slice | Unlocks |
|---|---|---|
| 1 | Persistence substrate (`PersistenceSystem`, dirty-tracked flush) | Any slice that wants state to survive restart |
| 2 | Account / character creation | Real identity instead of throwaway names |
| 3 | World content loading from data files | Authoring world without redeploying |
| 4 | Inventory + `get`/`drop` | Object interaction |
| 5 | Items + `look <item>` | Object inspection |
| 6 | Equipment + `wear`/`remove` | Gear |
| 7 | Mobs + wandering | Populated world |
| 8 | Combat | Core gameplay loop |
| 9 | Death and respawn | Combat is terminal until this exists |
| 10 | Skills | Character progression |
| 11 | Shopping | Economy |
| 12 | Crafting, potions | Content depth |
| 13 | Admin UI | Authoring tooling |

Order is flexible; some slices can run in parallel branches. Do not block MVP on any of these.

### Phase 4 — Hardening

Best addressed once a handful of Phase 3 slices have stressed the architecture:

- Test framework (xUnit) and initial system-level test coverage
- CI wiring (build + tests on PR)
- Performance passes where profiling shows real cost
- Admin UI if Phase 3 slice 13 hasn't already covered it (tech choice follows Ticket B resolution)
- Thread-safety review once `TimeSystem` and concurrency shape are real

## Ground rules across all phases

1. **Idealized API first.** If code can't match the target on first write, don't write it — fix the doc or defer the feature.
2. **4-layer discipline.** Handlers orchestrate → domain systems decide → core systems compute → components hold data. See [`architecture/01-layers.md`](../architecture/01-layers.md).
3. **Component queries, not `is`/`as`.** Never.
4. **Past-tense thin events.** Events describe *what happened*. Logic lives in handlers/systems.
5. **One world; authored content spawns from `TemplateRegistry`.** Feature systems own bespoke construction. See [`architecture/02-ecs.md`](../architecture/02-ecs.md).
6. **Persistence is per-component.** `[Persistent]` on a component type marks it as save-worthy.
7. **Docs drift is a bug.** Docs describe the target; if reality disagrees, one of them is wrong and gets fixed in the same PR.

## Current status

- **Phase 1: complete** — commit `107b27d` stripped the tree to the keep list, bumped to `net8.0`, scrapped Blazor, removed `Data` and `Bot` projects, trimmed `EntityService`'s module machinery. `dotnet build Hedron.sln` is green with 4 nullability warnings in the kept ECS files (folded into Phase 2 step 1).
- **Phase 1.5 (doc pass): complete in this branch** — resolved **Ticket A** (ECS redesign): dropped prototype/instance cache, adopted one-world model, `Entity(uint Id)` wrapper, `TemplateRegistry` for authored-content spawning, bespoke construction in domain systems (no `EntityFactory`), archetypes restricted to validation + detection, persistence via `[Persistent]` on component types, effects split into `PersistentEffectsComponent` / `TransientEffectsComponent`. Docs, skills, use cases, and CLAUDE.md updated in coordination. **Ticket B** (admin-tooling / use-cases / skills scope rethink — originally surfaced by the Blazor-editor removal) is deferred.
- **Phase 2: in progress.** Steps 1, 2, 3, and 4 complete on this branch:
  - **Step 1 (ECS primitives audit):** added `Entity` record-struct wrapper, added `EntityService.CreateEntity()` with a monotonic id allocator (0 reserved), renamed `GetComponent<T>` → `Get<T>` and added `TryGet<T>`, cleared all four nullability warnings, and trimmed the spurious `ref` in the computed-stats example in [`architecture/02-ecs.md`](../architecture/02-ecs.md).
  - **Step 2 (event bus):** added `Core/Events/` with `IEvent`, `IEventHandler<T>`, `IEventBus`, in-memory `EventBus` (priority-ordered dispatch, snapshot-under-lock so handlers can sub/unsub during dispatch), and `HandlerPriority` constants (State / Domain / Notification / Persistence / Ai). Registered as a DI singleton in [`Server/Program.cs`](../../Server/Program.cs). Also fixed a doc drift on this line: was `IGameEvent`, should be `IEvent` per [`architecture/03-events.md`](../architecture/03-events.md).
  - **Step 3 (handler / system contracts):** no code landed — step absorbed into step 2 (handler contract) and the later system-building steps (each system defines its own interface). Step 3 entry above updated to explain.
  - **Step 4 (command dispatcher):** added `Core/Sessions/ISession.cs` (concrete telnet impl comes in step 5), plus `Core/Commands/` with `ICommand`, `ICommandDispatcher`, and `CommandDispatcher` (case-insensitive verb map, duplicate-verb guard, "Unknown command" fallback). Registered as a DI singleton.
  - Build is green with zero warnings.
  - Next step: **Phase 2 step 5 — telnet session layer** (TCP listener, per-connection session object implementing `ISession`, line-based I/O, session → player-entity binding).
- Phase 3: not started
- Phase 4: not started

### Next-session pickup

A new session can resume by reading this file + [`mvp.md`](mvp.md) and starting at **Phase 2 step 5** (telnet session layer). Steps 1–4 already landed: `Entity`/`CreateEntity`/`Get`/`TryGet` under `Core/ECS/`, `IEvent`/`IEventHandler<T>`/`IEventBus`/`EventBus`/`HandlerPriority` under `Core/Events/`, and `ISession`/`ICommand`/`ICommandDispatcher`/`CommandDispatcher` under `Core/Sessions/` and `Core/Commands/` — all wired through DI.

Wave 0 and Sub-Wave 1A artifacts from the retired plan were resolved in Phase 1: `EcsManager.cs` stayed, `ICopyableObject.cs` was deleted with everything it served, namespace touches were mooted by the strip.

### Open tickets

- **Ticket B — admin tooling / use-cases / skills scope.** The removal of the Blazor editor in Phase 1 raised a broader question about how much of the planned admin/editor surface belongs in-scope at all, and whether the `editor-*` use cases should become telnet admin commands, a deferred web UI, or something else. Revisit before Phase 3 slice 13 (Admin UI) or whenever the first authoring workflow needs to exist.
