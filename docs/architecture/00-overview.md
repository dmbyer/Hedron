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
7. **Persistence is per-component.** Tag a component type with `[Persistent]` and it's saved; untagged components are rebuilt at runtime. An entity is persisted if it has any `[Persistent]` component.
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
| [06-commands.md](06-commands.md) | Command framework design: `ICommand` shape, argument schema, privilege gate, output, help |
| [06-flows.md](06-flows.md) | When tracing a runtime call chain (startup, command lifecycle, persistence flush, content reload, …) |
| [07-output.md](07-output.md) | Output framework: `IOutputMessage` catalog, `IOutputFormatter`/telnet ANSI, inline color syntax, broadcast model |
| [checklist.md](checklist.md) | **The authoritative invariant list.** Cite `INV-n` IDs in reviews. Every other doc explains; this one enforces. |
| [../reference/commands.md](../reference/commands.md) | Living catalog of every command |
| [../reference/systems.md](../reference/systems.md) | Living catalog of every system |
| [../reference/handlers.md](../reference/handlers.md) | Living catalog of every handler |
| [../reference/components.md](../reference/components.md) | Living catalog of every component |
| [../reference/archetypes.md](../reference/archetypes.md) | Living catalog of every archetype |
| [../use-cases/](../use-cases/) | Designer-level feature specs |
| [../roadmap/plan.md](../roadmap/plan.md) | Strategy, end goal, phase summary, current focus |
| [../roadmap/done.md](../roadmap/done.md) | Short ledger of completed phases / slices |
| [../roadmap/backlog.md](../roadmap/backlog.md) | Deferred work queue |
