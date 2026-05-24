# Architecture Overview

Hedron is a C# MUD engine targeting .NET 8. `Server/` is a generic-host console app that runs the telnet listener and owns DI composition; `Core/` holds the engine (ECS, systems, handlers, events, commands). The architecture is **event-driven, ECS-based, and layered**.

> **Note on idealized API.** The architecture docs describe the target API (e.g. `EcsManager.World`, `EntityWorld`, `IEventBus`). The codebase is being rebuilt against this target — see [docs/roadmap/plan.md](../roadmap/plan.md) for the phase sequence. **Write new code against the idealized API; legacy code outside the keep list is reference material only.**

---

## The Four Layers

```
┌─────────────────────────────────────────────────────────────┐
│                     Event Handlers                          │
│  Thin orchestrators that respond to events by calling       │
│  domain systems. Grouped by cohesion.                       │
├─────────────────────────────────────────────────────────────┤
│                     Domain Systems                          │
│  Game-specific rules and semantics. Encode designer         │
│  intent like "stealth beats perception unless true sight."  │
├─────────────────────────────────────────────────────────────┤
│                      Core Systems                           │
│  Mechanically generic systems. Resolution mechanics         │
│  without game-specific knowledge.                           │
├─────────────────────────────────────────────────────────────┤
│                   Components / World                        │
│  ECS components and world state held by `EntityService`.    │
└─────────────────────────────────────────────────────────────┘
```

Dependencies flow **downward only**. See [01-layers.md](01-layers.md) for full detail.

---

## Design Principles

1. **Events are thin facts.** Past-tense, immutable, no logic. They describe *what happened*, not *what to do*.
2. **Handlers orchestrate, systems compute.** Handlers call domain systems; handlers own the event-publishing decisions.
3. **Domain systems encode game rules.** They know game concepts (stealth, magic, crafting) and compose core systems.
4. **Core systems are reusable mechanics.** They answer *how does X work?*, not *when should we do X?* — they could work in a different game.
5. **Dependencies flow downward only.** No upward arrow in the system dependency graph.
6. **One world, authored content via templates.** Every live entity lives in `EntityService`. Authored content is spawned from `TemplateRegistry`; bespoke entities are built by the feature that owns them.
7. **Persistence is a two-level opt-in.** `PersistentEntity` (zero-data marker) opts an entity in. `[Persistent]` on a component type controls which of that entity's components are included in the snapshot. Neither alone is sufficient. See [06-persistence.md](06-persistence.md).
8. **Modules group cohesion.** Feature slices live under `Core/Modules/<Feature>/` (services, handlers, events, components together).

---

## Where things live

Rows marked *(target)* describe locations that are rebuilt as part of Phase 2. They do not exist in the current tree yet — see [../roadmap/plan.md](../roadmap/plan.md) for the keep list and phase sequencing.

| Concept | Location |
|---|---|
| Components (pure data) | `Core/ECS/Components/` *(target)* |
| Archetypes (validation + detection) | `Core/ECS/ArchetypeRegistry.cs`, `EntityArchetype.cs` *(target)* |
| Template registry (authored-content spawn) | `Core/ECS/TemplateRegistry.cs` *(target)* |
| Feature modules | `Core/Modules/<Feature>/` *(target)* |
| Core (cross-cutting) systems | `Core/Systems/<X>System.cs` *(target)* |
| Domain (feature) systems | `Core/Modules/<Feature>/Systems/<X>System.cs` *(target)* |
| Event handlers | `Core/Modules/<Feature>/Handlers/` *(target)* |
| Event records | `Core/Modules/<Feature>/Events/` *(target)* |
| Player commands | `Core/Modules/<Feature>/Commands/` (feature-owned) or `Core/Commands/` (cross-cutting) *(target)* |
| Telnet / DI host | `Server/` |
| ECS primitives (kept from pre-Phase-1) | `Core/ECS/EntityService.cs`, `ComponentRepository.cs`, `EcsManager.cs`, `IComponent.cs` |

---

## Related Documents

| Doc | When to read |
|---|---|
| [01-layers.md](01-layers.md) | Before adding a new system or handler |
| [02-ecs.md](02-ecs.md) | Before adding a component, archetype, or entity operation |
| [03-events.md](03-events.md) | Before defining a new event or subscription |
| [04-pitfalls.md](04-pitfalls.md) | When tempted to shortcut the layering |
| [05-configuration.md](05-configuration.md) | Before wiring any setting to `IConfiguration` or adding a constant |
| [subsystems/commands.md](subsystems/commands.md) | Command framework design: `ICommand` shape, argument schema, privilege gate, output, help |
| [flows/README.md](flows/README.md) | When tracing a runtime call chain (startup, command lifecycle, persistence flush, content reload, …) |
| [subsystems/output.md](subsystems/output.md) | Output framework: `IOutputMessage` catalog, `IOutputFormatter`/telnet ANSI, inline color syntax, broadcast model |
| [06-persistence.md](06-persistence.md) | Persistence model: `PersistentEntity` marker, `[Persistent]` attribute, three save patterns (save-on-change, area-scoped flush, timestamp/lazy) |
| [checklist.md](checklist.md) | **The authoritative invariant list.** Cite `INV-n` IDs in reviews. Every other doc explains; this one enforces. |
| [../documentation-architecture.md](../documentation-architecture.md) | How the docs + `.claude/` tooling are organized — what each surface owns and the discipline that keeps them current (enforced via `INV-D*`) |
| [../reference/commands.md](../reference/commands.md) | Living catalog of every command |
| [../reference/systems.md](../reference/systems.md) | Catalog of implemented systems (idealized/future designs: `systems-planned.md`) |
| [../reference/handlers.md](../reference/handlers.md) | Catalog of implemented handlers (idealized/future designs: `handlers-planned.md`) |
| [../reference/components.md](../reference/components.md) | Catalog of implemented components (target model: `components-planned.md`) |
| [../reference/archetypes.md](../reference/archetypes.md) | Target archetype catalog (the archetype system is not yet built) |
| [../use-cases/](../use-cases/) | Designer-level feature specs |
| [../roadmap/plan.md](../roadmap/plan.md) | Strategy, end goal, phase summary, current focus |
| [../roadmap/done.md](../roadmap/done.md) | Short ledger of completed phases / slices |
| [../roadmap/backlog.md](../roadmap/backlog.md) | Deferred work queue |
