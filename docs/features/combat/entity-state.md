# Entity State

> Cross-cutting flag-based entity state layer: `EntityStateFlags` + `IEntityStateService`. Centralized transition-rule enforcement for any entity (player or mob). **Authoring checkpoint:** slice 9-a. Living document.

## What it is / does

`IEntityStateService` is a **domain-tier lateral coordinator** that attaches/removes `EntityStateComponent` on entities, enforces a static transition-rule table, and returns structured failure reasons to callers. It is the authoritative "is this entity in state X?" query surface for all state-gated commands.

It deliberately does not call `IEventBus` or persistence (INV-5). Commands and handlers publish `EntityStateChangedEvent` after mutating state via the service.

## How it works

### The two-layer state model

`EntityStateComponent.ActiveStates` (the observable `[Flags]` enum) and `CombatStateComponent.OpponentEntityId` (the combat-specific metadata) coexist. The flag gates state-gated commands; the metadata drives the combat pulse. `ICombatSystem` does not call `IEntityStateService` — a cohesion choice, not an INV-2 obligation (Domain→Domain is permitted). Commands and handlers are the right coordinators between peer domain services.

### `EntityStateFlags`

`[Flags]` enum: `None=0`, `InCombat=1`, `Resting=2`, `Incapacitated=4`. Named with `Flags` suffix to avoid a C# namespace/type collision with the `EntityState` module (`EntityState` as a simple name resolves to the module namespace, not the type). Precedent: CLAUDE.md naming convention.

### Transition rules

Enforced by `TryEnterState`:

| State to enter | Blocked while | Auto-exits |
|---|---|---|
| `Resting` | `InCombat`, `Incapacitated` | — |
| `InCombat` | `Incapacitated` | `Resting` |
| `Incapacitated` | *(no block)* | — |

Auto-exits are applied centrally before the new flag is set. Callers capture `OldStates = GetStates(entityId)` before calling `TryEnterState`/`ExitState`, then publish `EntityStateChangedEvent` — the service never calls the bus (INV-5).

### Component lifecycle

`EntityStateComponent` is attached on first `TryEnterState` and **removed** by `ExitState` when `ActiveStates == None`. This means `HasComponent<EntityStateComponent>` is a reliable "entity is in at least one state" check. The component is not `[Persistent]` — transient flags surviving a crash would be stale (the opponent in a combat pair may not exist after restart).

### Transition rules are a static table, not a state machine

With three flags and four rule entries a general state machine framework would be premature. The interface is stable if that changes.

## Interface

- [`IEntityStateService.cs`](../../../Core/Modules/EntityState/Systems/IEntityStateService.cs) — `TryEnterState(entityId, state, out failReason)`, `ExitState(entityId, state)`, `IsInState(entityId, state)`, `GetStates(entityId)`.

## Considerations

- **`ExitState` is unconditional** — it never returns a failure. Exiting a state the entity is not in is a benign no-op. This matters because combat ending, death, and disconnect all need to unconditionally clear state.
- **`TryEnterState` returns `failReason`, not throws** — command bodies receive a displayable string and choose how to surface it. Consistent with other result-returning seams (e.g. `IAccountSystem.AuthenticateAsync`).
- **Routing all state access through `IEntityStateService`** — commands that only read state could call `HasComponent<EntityStateComponent>` directly, but routing everything through the service keeps the call pattern uniform and makes the service the authoritative query surface for future rule changes.

## Extensibility

- **New flags** — add a value to `EntityStateFlags` and a row (if needed) to the transition table in `EntityStateService`. No interface change.
- **New state-gated commands** (`rest`, `meditate`, craft, etc.) call `IEntityStateService.IsInState` as their guard check.

## Related

- [`combat.md`](combat.md) — the primary consumer; `KillCommand` calls `TryEnterState(InCombat)`, `FleeCommand` calls `ExitState(InCombat)`.
- [`death-system.md`](death-system.md) — `Incapacitated` flag; `IDeathSystem.OnHpChanged` calls `TryEnterState(Incapacitated)` on the service.
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `EntityStateService`/`IEntityStateService`, `EntityStateComponent`/`EntityStateFlags` rows.
- [`../../roadmap/completed/slice-9a-entity-state-management.md`](../../roadmap/completed/slice-9a-entity-state-management.md) — as-built record and design decisions including the namespace/type collision resolution.
