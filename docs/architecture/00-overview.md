# Architecture Overview

Hedron is a C# MUD engine targeting .NET Core 3.1, composed of a Blazor Server admin UI and a game-loop thread that share a data cache. The architecture is **event-driven, ECS-based, and layered**.

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
│  ECS components, entity cache, world state.                 │
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
6. **Prototypes and instances share logic, not side effects.** Prototype edits persist data; instance operations publish gameplay events. Shared logic lives in `*Core` static helpers.
7. **Modules group cohesion.** Feature slices live under `Core/Modules/<Feature>/` (services, handlers, events, components together).

---

## Where things live

| Concept | Location |
|---|---|
| Components (pure data) | `Core/ECS/Components/` |
| Archetypes | `Core/ECS/ArchetypeRegistry.cs`, `EntityArchetype.cs` |
| Entity factory | `Core/ECS/EntityFactory.cs` |
| Feature modules | `Core/Modules/<Feature>/` |
| Core systems | `Core/Modules/<Feature>/Core/` or `Core/Systems/Core/` |
| Domain systems | `Core/Modules/<Feature>/Domain/` or `Core/Systems/Domain/` |
| Event handlers | `Core/Modules/<Feature>/Handlers/` |
| Event records | `Core/Modules/<Feature>/Events/` |
| Player commands | `Core/Commands/<Category>/` |
| Web admin UI | `Server/Pages/`, `Server/Shared/` |

---

## Related Documents

| Doc | When to read |
|---|---|
| [01-layers.md](01-layers.md) | Before adding a new system or handler |
| [02-ecs.md](02-ecs.md) | Before adding a component, archetype, or entity operation |
| [03-events.md](03-events.md) | Before defining a new event or subscription |
| [04-pitfalls.md](04-pitfalls.md) | When tempted to shortcut the layering |
| [../reference/systems.md](../reference/systems.md) | Living catalog of every system |
| [../reference/handlers.md](../reference/handlers.md) | Living catalog of every handler |
| [../reference/components.md](../reference/components.md) | Living catalog of every component |
| [../reference/archetypes.md](../reference/archetypes.md) | Living catalog of every archetype |
| [../use-cases/](../use-cases/) | Designer-level feature specs |
| [../roadmap/backlog.md](../roadmap/backlog.md) | Prioritized work queue |
| [../roadmap/plan.md](../roadmap/plan.md) | Phase plan — what's stripped, what's being built |
| [../roadmap/mvp.md](../roadmap/mvp.md) | Frozen MVP target for Phase 2 |
