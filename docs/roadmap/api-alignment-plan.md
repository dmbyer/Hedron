# API Alignment Plan

> **Purpose.** The `docs/architecture/` and `docs/use-cases/` trees describe an *idealized* API — the target shape the codebase should converge toward. This plan enumerates the gaps between the current C# code and that target, and sequences the migration so every step is small, green, and independently shippable.

## The core mismatch

| Area | Idealized docs use | Current code has |
|---|---|---|
| ECS world access | `EntityService` (DI-injected) | `EcsManager.World` (undefined static; referenced aspirationally in `Core/ECS/EntityFactory.cs`) |
| Component storage | `ComponentRepository` (owned by `EntityService`) | Same class exists, but only reachable via `EntityService` that isn't constructed anywhere |
| Module / system organisation | `IModule` registered on `EntityService` (module = bundle of systems + handlers) | `IModule` interface exists, but zero concrete modules; systems are not classes yet |
| Entity lifecycle | `EntityService.DestroyEntity`, `EntityFactory.CreateEntity` | Factory exists and calls `EcsManager.World` (broken); destroy only via `ComponentRepository.RemoveAllComponents` |
| Legacy inheritance | No `Entity`, `EntityAnimate`, `Player`, `Mob`, `ItemWeapon`, `ItemPotion`, `Storage`, `EntityContainer` classes | All present under `Core/ECS/DEPRECATED - Entities/` and still referenced by `Core/Factory/*`, `Core/Locale/*`, and every command |
| Handlers | Named pipeline with `IEventBus.Publish`, handlers registered by priority | No `IEventBus`, no `IHandler` — command dispatch is direct method calls on `CommandHandler` |
| Domain systems | `CombatSystem`, `MovementSystem`, `LootSystem` etc. as injectable services | Logic scattered across static helpers, entity methods, and commands |
| Persistence | `PersistenceSystem` with dirty tracking, atomic writes | `DataAccess` / `DataPersistence` — imperative `SaveObject` calls scattered through codebase |

The rule: **do not delete idealized docs to match code. Move code to match docs.**

## Ordering principle

Each wave must be:
1. **Shippable** — the game still runs end-to-end after the wave completes
2. **Reversible for one wave** — keep the old call sites working until the next wave removes them
3. **Scoped** — one architectural concept per wave

No wave may claim to be done while deprecated types referenced by the wave are still in use.

---

## Wave 0 — Bootstrap ECS access (prerequisite, small)

**Goal:** make `EcsManager.World` resolve to something real, so the factory compiles without aspiration.

- [ ] Add `Core/ECS/EcsManager.cs` that wraps a single `EntityService` instance exposed as `EcsManager.World` for now.
- [ ] Wire `EcsManager.World` into DI as the app-lifetime singleton `EntityService`.
- [ ] Audit every reference to `EcsManager.World` — ensure each one lives in code paths that the new service can serve.

**Done when:** `dotnet build` is clean and the existing game starts.

---

## Wave 1 — Lift the factory, purge legacy entity classes

**Goal:** kill `Core/ECS/DEPRECATED - Entities/`. All entities are `uint` IDs + components.

Steps, in order:

1. Move `EntityFactory.cs` to operate on `EntityService` (not `EcsManager.World` globally, but via injected service) — factory becomes an `IModule`.
2. Rewrite `Core/Factory/*` (weapon/mob/item generators) to call `EntityFactory.CreateEntity(archetype, CacheType.Prototype)` and mutate components instead of constructing legacy classes.
3. Rewrite `Core/Locale/Room`, `Area`, `World` to be entities with `RoomDataComponent` / `AreaDataComponent` / `ContainerDataComponent`. The `Core/Locale/*` files go away.
4. Replace `Storage` and `EntityContainer` usages with `ContainerDataComponent` / `InventoryComponent`.
5. Replace every `entity is Player` / `as Player` with `entityService.HasComponent<PlayerDataComponent>(id)`. Same for `Mob`, `ItemWeapon`, `ItemPotion`, etc. — see [../architecture/04-pitfalls.md](../architecture/04-pitfalls.md).
6. Delete `Core/ECS/DEPRECATED - Entities/` once zero references remain.

**Risk:** this touches every command and every data-access path. Do it in per-archetype sub-waves (weapons first, then mobs, then rooms, then areas) to keep the diff bounded.

**Done when:** the `DEPRECATED - Entities` folder is gone; `Grep` for `is Player`, `as Player`, etc. returns zero matches.

---

## Wave 2 — Introduce the event bus

**Goal:** put `IEventBus` in place so handlers can listen instead of being called directly.

1. Add `Core/Events/IEventBus.cs`, `EventBus.cs`, `IGameEvent.cs`, `IEventHandler<T>.cs`, `HandlerPriority` enum (from [../architecture/03-events.md](../architecture/03-events.md)).
2. Register the bus as a singleton in DI.
3. Create the first event type — `DamageEvent` — and route combat damage through it. One handler (`CombatHandler`) subscribes; everyone else keeps calling the legacy method for now.
4. Expand event coverage incrementally: `PlayerDeathEvent`, `MobDeathEvent`, `ItemMovedEvent`, etc. (see catalog in [../architecture/03-events.md](../architecture/03-events.md)).

**Done when:** every existing direct-call "notify on X" site in the codebase is replaced with an event publish, and `git grep` for inline cross-cutting calls ("notification + persistence + combat state" inside one method) returns nothing.

---

## Wave 3 — Extract domain systems

**Goal:** every piece of gameplay logic lives in an `ISystem` under `Core/Modules/<Feature>/Systems/`.

Take the logic from the idealized use-case files one at a time and carve it out:

- `MovementSystem` — first target because movement is everywhere
- `CombatSystem` + `CombatFormulas` — pure resolution
- `InventorySystem`, `EquipmentSystem`
- `LootSystem`, `ItemGeneratorSystem`
- `SkillSystem`, `AttributeCalculator`, `DiceSystem`, `EffectTracker`
- `SpellSystem`, `PoolsSystem`
- `AISystem`, `VisibilitySystem`, `LocationSystem`
- `ShopSystem`, `CurrencySystem`, `CraftingSystem`, `TradeSystem`
- `AccessControlSystem`
- `WorldEditingSystem` (admin-editor surface)
- `PersistenceSystem` (see Wave 5)

Each extraction follows the shape in [../architecture/01-layers.md](../architecture/01-layers.md): systems return data; handlers publish events.

**Done when:** `Core/Modules/<Feature>/` contains the system, the handler, its events, its feature-owned components. `Core/Commands/*` files shrink to arg parsing + handler invocation.

---

## Wave 4 — Handler pipeline

**Goal:** handlers replace the `CommandHandler` god object and direct method chains.

1. Add the handler registry (priority-ordered list per event type).
2. For each feature module, register its handler via `IModule.Register`.
3. Convert the existing `Core/Commands/*` into thin command handlers that call domain systems and publish events — no cross-feature logic inside a command.
4. Wire ordering: use priority when safe, split into phased events when one handler's output feeds another's input (see pattern in [../architecture/03-events.md](../architecture/03-events.md)).

**Done when:** every cross-feature effect flows through the bus. A new feature can be added by registering one module.

---

## Wave 5 — Persistence as dirty-tracked system

**Goal:** replace imperative `SaveObject` calls with `PersistenceSystem.MarkDirty` + periodic flush.

1. `PersistenceSystem.MarkDirty(entityId)` on any prototype mutation.
2. `PersistenceHandler` subscribes to `EntityMutatedEvent` (Wave 2 gate).
3. Flush pass runs on timer and on shutdown; uses atomic write-and-rename. See [../use-cases/game-state-persistence.md](../use-cases/game-state-persistence.md).
4. Deprecate and remove `DataPersistence.SaveObject`, `SaveAllPrototypes`, and every scattered `updatePersistence` call.

**Done when:** no code outside `PersistenceSystem` calls the serializer directly.

---

## Wave 6 — Modernization (deferred, post-ECS)

After ECS is real, these become easy:

- `.NET 8` upgrade (from .NET Core 3.1)
- `System.Text.Json` replaces `Newtonsoft.Json`
- Blazor Auto-Rendering migration
- Dual-client architecture (player + admin UI)
- SignalR alongside Telnet via a unified connection abstraction
- Unit/integration test framework — meaningful once systems are injectable

Track these in [backlog.md](backlog.md); they do not block ECS.

---

## Definition of "idealized" surface

When implementing Waves 0–5, the target API shape comes from these files (treat as contracts, not suggestions):

- [../architecture/00-overview.md](../architecture/00-overview.md)
- [../architecture/01-layers.md](../architecture/01-layers.md)
- [../architecture/02-ecs.md](../architecture/02-ecs.md) — component shapes, archetype compositions
- [../architecture/03-events.md](../architecture/03-events.md) — `IEventBus` + event payload rules
- [../reference/systems.md](../reference/systems.md) — target system signatures
- [../reference/handlers.md](../reference/handlers.md) — target handler responsibilities
- [../reference/components.md](../reference/components.md) — cross-cutting component catalog
- [../reference/archetypes.md](../reference/archetypes.md) — required/optional component sets

When code and docs disagree during a wave, fix the code. If the docs turn out to be wrong about a specific design choice, *update the docs first* and reference the change in the wave's PR description.

---

## Exit criteria for the full migration

- `Core/ECS/DEPRECATED - Entities/` deleted
- `Core/Locale/` deleted (replaced by archetype + components)
- `Core/Factory/` either deleted or reduced to thin wrappers around `EntityFactory`
- Every command in `Core/Commands/` is ≤ ~30 lines (parse → call system → publish event)
- Every cross-cutting concern (notification, persistence, AI) subscribes via handlers — zero inline calls
- `EntityService` is the only way to touch entity state
- `IEventBus` is the only way cross-feature effects happen
- A new feature = one new module under `Core/Modules/<X>/` with its handler, system, events, components

At that point, the idealized docs *describe code that exists*, and the alignment plan is complete.
