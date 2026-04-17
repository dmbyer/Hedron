# Backlog

> Living list of follow-up work. For ECS-specific waves, see [api-alignment-plan.md](api-alignment-plan.md). For a snapshot of where ECS stands, see [ecs-migration-status.md](ecs-migration-status.md).

Status markers: 🟢 ready · 🟡 blocked · 🔵 deferred (post-ECS alignment)

## High priority

### 🟢 Execute API Alignment Plan — Wave 0 and Wave 1
Resolve `EcsManager.World`, lift the factory, delete `Core/ECS/DEPRECATED - Entities/`.

**Why it's first:** nothing else in the backlog is cleanly shippable while legacy entity classes are still the runtime. See [api-alignment-plan.md](api-alignment-plan.md).

### 🟢 Heartbeat / TimeSystem
Single scheduler driving combat pulses, mob AI ticks, respawn timers, periodic persistence flush, effect expiries. Blocks combat processing, mob wandering, spell effect durations, and auto-save.

**Target shape:** `TimeSystem` from [../reference/systems.md](../reference/systems.md) (`Schedule(delay, action)`, `ScheduleRecurring(period, action)`).

### 🟢 Event bus infrastructure — Wave 2
`IEventBus`, `IGameEvent`, `IEventHandler<T>`, priority ordering. Needed before any real handler refactor. Design is fixed in [../architecture/03-events.md](../architecture/03-events.md).

## Medium priority

### 🟡 Extract domain systems — Wave 3
Blocked on Waves 0–2. Movement first (largest unlock), then Combat, then Inventory/Equipment, then rest. List in [api-alignment-plan.md](api-alignment-plan.md).

### 🟡 Handler pipeline — Wave 4
Blocked on event bus (Wave 2). Collapses `CommandHandler` god object and the command files into thin parse-+-invoke shapes.

### 🟡 Dirty-tracked persistence — Wave 5
Blocked on event bus + `PersistenceSystem`. Design is in [../use-cases/game-state-persistence.md](../use-cases/game-state-persistence.md).

### 🟢 Performance: LINQ in hot paths
Replace LINQ-heavy spatial queries with dictionary lookups. Candidates become obvious once `LocationSystem` is carved out — but any hot-path wins noticed beforehand can land directly.

### 🟢 Test framework
Add xUnit (or similar) test project. ECS systems are unit-test-friendly (pure resolvers in `CombatSystem.ResolveAutoAttack`, `SpellSystem.Resolve`, etc.) — the value is low until Wave 3 gives us real extracted systems to test.

## Low priority / deferred

### 🔵 .NET 8 upgrade
Currently .NET Core 3.1 (end-of-life). Do after Waves 0–3 so the refactor isn't fighting framework changes at the same time.

### 🔵 `System.Text.Json` migration
Blocked on .NET 8 for the richer APIs; coincides with persistence rewrite (Wave 5) to avoid touching serialization twice.

### 🔵 Blazor Server → Auto-Rendering
Post-.NET 8. No functional change, performance + flexibility only.

### 🔵 Dual-client (player web client)
Blocked on Blazor migration. Introduces player-facing UI alongside the admin editor.

### 🔵 SignalR + Telnet unified connection
Blocked on dual-client. Add a connection abstraction so telnet sessions and web sessions look the same to handlers.

### 🔵 Thread safety
Evaluate after `TimeSystem` exists and concurrency shape is known. May not be needed if the heartbeat stays single-threaded with an event queue.

### 🔵 Inline doc / architectural guide expansion
The `/docs` tree is now the architectural guide. Keep it updated as waves ship — that is itself a backlog item (a lightweight "docs drift" sweep after each wave).

## Recently completed

- **Docs restructure** (current session) — consolidated root `CLAUDE_*.md` and `DESIGN_DOCS/` into `docs/{architecture,reference,use-cases,roadmap,archive}` with idealized API as the target and a migration plan to reach it.
- **Phase 1.5 — Entity hierarchy flattening + archetype scaffolding** (Dec 2024 per legacy notes). See [ecs-migration-status.md](ecs-migration-status.md).
- **Phase 1 — Component extraction** (Nov 2025).
- **Cache system simplification** (Oct 2025).
