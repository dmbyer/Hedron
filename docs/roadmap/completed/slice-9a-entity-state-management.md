# Phase 3 slice 9-a — Entity state management (completed)

> Implemented on branch `claude/admiring-swanson-cd9fbc`. Full feature spec: [`../../implementation-plans/entity-state-management.md`](../../features/combat/combat.md).

## Outcome

Any entity — player or mob — can now carry a transient set of state flags (`InCombat`, `Resting`, `Incapacitated`) managed through a single authoritative service. `IEntityStateService` enforces the static transition-rule table (e.g. `Resting` is blocked while `InCombat`), attaches and removes `EntityStateComponent` automatically, and returns caller-displayable failure reasons on blocked transitions. Commands and handlers publish `EntityStateChangedEvent` after mutating state; the service itself never touches the event bus (INV-5). This slice is pure infrastructure — no player-visible commands ship here; the combat slice (9) is the first consumer.

## Shipped pieces

| Surface | Location |
|---|---|
| `EntityStateFlags` — `[Flags]` enum `{ None=0, InCombat=1, Resting=2, Incapacitated=4 }`; co-located with `EntityStateComponent`; `Flags` suffix avoids C# namespace/type collision with the `EntityState` module | `Core/ECS/Components/EntityStateComponent.cs` |
| `EntityStateComponent` — `ActiveStates: EntityStateFlags`; not `[Persistent]`; absent when `ActiveStates == None` | `Core/ECS/Components/EntityStateComponent.cs` |
| `EntityStateChangedEvent` — `(uint EntityId, EntityStateFlags OldStates, EntityStateFlags NewStates)`; thin past-tense event; no subscribers in this slice | `Core/Modules/EntityState/Events/EntityStateChangedEvent.cs` |
| `IEntityStateService` — `TryEnterState`, `ExitState`, `IsInState`, `GetStates`; never calls `IEventBus` or persistence (INV-5) | `Core/Modules/EntityState/Systems/IEntityStateService.cs` |
| `EntityStateService` — static transition-rule table; OR-assigns on enter; AND-NOT clears on exit; removes component when `ActiveStates == None` | `Core/Modules/EntityState/Systems/EntityStateService.cs` |
| `EntityStateModule` — DI extension registering `IEntityStateService` as singleton | `Core/Modules/EntityState/EntityStateModule.cs` |
| `Program.cs` — `AddEntityStateModule()` call | `Server/Program.cs` |
| `docs/reference/components.md` — `EntityStateComponent` + `EntityStateFlags` rows added to Infrastructure table | — |
| `docs/reference/systems.md` — `EntityStateService` entry added to Domain systems | — |

## Spec-review provenance

**Spec-mode gate:** Passed before implementation (use-case doc authored as part of slice 9 planning batch). No blocking findings recorded.

**Code-mode gate:** To be run before merge (architecture-reviewer in code mode against the diff).

## Notable design points

- **Namespace/type name collision — resolved by renaming the enum.** The module lives in `Hedron.Core.Modules.EntityState.*`; the original enum was also named `EntityState`. C# resolves the simple name `EntityState` inside those namespaces as the enclosing namespace component, shadowing any `using` import. An initial workaround using the `::` alias qualifier (`ECS::EntityState`) was replaced by renaming the enum to `EntityStateFlags` — the canonical resolution per the CLAUDE.md naming convention: when a module namespace and a type share a simple name, rename the type (add `Flags` suffix for `[Flags]` enums).

- **`EntityStateComponent` is removed when empty.** `ExitState` calls `RemoveComponent<EntityStateComponent>` when `ActiveStates == None`. This means `HasComponent<EntityStateComponent>` is a reliable "entity is in at least one state" check without reading the flags.

- **Transition rules are a static table, not a state machine.** With three flags and four rule entries, a general state machine framework would be premature. The interface is stable if that changes.

- **No `[Persistent]` — design decision.** Transient flags surviving a crash would be stale (the opponent in a combat pair may not exist after restart). All flag state is re-established by the gameplay actions that create it. Acknowledged downside: a resting player reconnects without the rest flag. Acceptable for Phase 3.

- **Event published by callers, not the service.** Commands and handlers capture `OldStates = GetStates(entityId)` before calling `TryEnterState`/`ExitState`, then publish `EntityStateChangedEvent`. The service never touches the event bus (INV-5).

- **`InCombat` flag vs. `CombatStateComponent`.** The flag is the observable, cross-cutting signal ("is this entity in any combat?"); the combat-specific metadata (opponent entity id) lives in a separate `CombatStateComponent` that the combat slice will add. Both coexist; the flag gates state-gated commands while the metadata drives the pulse.

## Deviations from the use-case doc

None. All postconditions satisfied as written.

## Follow-ups unlocked

- **Slice 9 — Combat.** `KillCommand` calls `IEntityStateService.TryEnterState(InCombat)`; `FleeCommand` calls `ExitState(InCombat)`; guards on `kill` (already in combat) and `flee` (not in combat) call `IsInState`. `Incapacitated` is set by the combat handler when HP reaches zero.
- **Slice 9-b/9-c — Time and stat systems.** Both can proceed in parallel; neither depends on this slice except through the shared postconditions of slice 8a.
- **Future state-gated commands** (rest, meditate, craft, etc.) call `IEntityStateService` as their guard check — the service is the authoritative query surface.
