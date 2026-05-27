# Use Case: Entity State Management

**Status:** implemented
**Actors:** System (domain service); Player, Mob (as state subjects); Commands and Handlers (as state callers)
**Module:** `Core/Modules/EntityState/` (new)

---

## Description

Introduces a unified, flag-based entity state layer that complements (does not replace) metadata components. Before this slice, determining whether an entity is "in combat," "resting," or "incapacitated" requires knowing the exact component type to query — an implicit, undocumented contract that grows harder to manage as state combinations multiply. This slice introduces `EntityStateComponent` (a cross-cutting `[Flags]` enum holder) and `IEntityStateService` (a domain system that enforces transition rules, attaches/removes the component, and returns structured failure reasons). `EntityStateChangedEvent` is published by command and handler callers — not by the service (INV-5). The combat slice (9) is the primary consumer; `IEntityStateService` provides the authoritative "is this entity in state X?" query surface for all future state-gated commands.

**Design scope.** `EntityStateFlags` flags for MVP: `None`, `InCombat`, `Resting`, `Incapacitated`. Transition rules are a static lookup table — no state machine framework. No admin commands in this slice; the `state` debug command is acknowledged debt.

---

## Preconditions

- Slices 1–8a complete.
- `EntityService` (via `HasComponent<T>`, `TryGet<T>`, `AddComponent`, `RemoveComponent`) is the only mechanism used to attach/remove `EntityStateComponent` — no new ECS infrastructure required.
- `IEventBus` is available to callers (commands, handlers) that will publish `EntityStateChangedEvent` after service calls.

---

## Postconditions

- `EntityStateFlags` `[Flags]` enum exists in `Core/ECS/Components/` with values `None = 0`, `InCombat = 1 << 0`, `Resting = 1 << 1`, `Incapacitated = 1 << 2`.
- `EntityStateComponent { EntityStateFlags ActiveStates }` exists in `Core/ECS/Components/`. Not `[Persistent]`. Attached by `IEntityStateService.TryEnterState`; removed (when empty) by `IEntityStateService.ExitState`.
- `IEntityStateService` (domain system, `Core/Modules/EntityState/Systems/`) is registered as a singleton and satisfies:
  - `TryEnterState(uint entityId, EntityStateFlags state, out string? failReason)` — validates transition rules, attaches or updates `EntityStateComponent`, returns `true` on success.
  - `ExitState(uint entityId, EntityStateFlags state)` — removes the flag; removes the component when no flags remain.
  - `IsInState(uint entityId, EntityStateFlags state)` — flag check; returns `false` if the entity has no `EntityStateComponent`.
  - `GetStates(uint entityId)` — returns the full `ActiveStates` flags value, or `EntityStateFlags.None` if the component is absent.
- `EntityStateChangedEvent(uint EntityId, EntityStateFlags OldStates, EntityStateFlags NewStates)` exists in `Core/Modules/EntityState/Events/`.
- `EntityStateModule` (`Core/Modules/EntityState/EntityStateModule.cs`) exposes `AddEntityStateModule(IServiceCollection)` and is called from `Server/Program.cs`.
- Transition rule table is enforced by `TryEnterState`:

  | State to enter | Blocked while any of these are active | `failReason` (example) |
  |---|---|---|
  | `Resting` | `InCombat`, `Incapacitated` | `"You cannot rest while in combat."` / `"You cannot rest while incapacitated."` |
  | `InCombat` | `Incapacitated` | `"You cannot enter combat while incapacitated."` |
  | `Incapacitated` | *(no block)* | n/a |

  All other `state` values not listed above have no entry-block rules in MVP. `ExitState` never fails.

---

## Main Flow

### Flow ES-1 — Entering a state (example: `InCombat`)

1. A command (e.g. `KillCommand`) calls `IEntityStateService.TryEnterState(playerEntityId, EntityStateFlags.InCombat, out failReason)`.
2. The service reads `EntityStateComponent` from the entity (or treats `ActiveStates` as `None` if the component is absent).
3. The service checks the transition rule table for `InCombat`. If `Incapacitated` flag is set → returns `false`, `failReason = "You cannot enter combat while incapacitated."`.
4. On success: if the entity lacks `EntityStateComponent`, the service calls `EntityService.AddComponent(entityId, new EntityStateComponent { ActiveStates = state })`. If the component exists, it OR-assigns the flag: `component.ActiveStates |= state`. Returns `true`, `failReason = null`.
5. The calling command captures `OldStates` before the call and publishes `EntityStateChangedEvent(entityId, oldStates, newStates)` after a successful `TryEnterState` (INV-5 — event publication is in the command, not the service).

### Flow ES-2 — Exiting a state (example: combat ends)

1. A command or handler (e.g. `FleeCommand` or `CombatMobDeathHandler`) calls `IEntityStateService.ExitState(entityId, EntityStateFlags.InCombat)`.
2. The service reads `EntityStateComponent`. If absent, the call is a no-op.
3. The service clears the flag: `component.ActiveStates &= ~state`. If `ActiveStates == None` after the operation, the service calls `EntityService.RemoveComponent<EntityStateComponent>(entityId)`.
4. The calling command or handler publishes `EntityStateChangedEvent(entityId, oldStates, newStates)`.

### Flow ES-3 — State-gated command guard (example: `rest`)

1. Player sends `rest`. `RestCommand.ExecuteAsync` calls `IEntityStateService.IsInState(playerEntityId, EntityStateFlags.InCombat)`.
2. If `true`, the command writes `PlainMessage("You cannot rest while in combat.")` and returns without attempting `TryEnterState`. (The command can read the guard before calling `TryEnterState`, or rely on `TryEnterState`'s `failReason` return — both patterns are valid. The guard-read pattern is preferred when a crisply worded player message is needed before the service call incurs side-effects.)
3. If `false`, the command calls `IEntityStateService.TryEnterState(playerEntityId, EntityStateFlags.Resting, out failReason)`.
4. On success, the command publishes `EntityStateChangedEvent` and writes a confirmation message.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `EntityStateChangedEvent` | Commands and handlers that call `IEntityStateService` | `uint EntityId, EntityStateFlags OldStates, EntityStateFlags NewStates` | Observable hook for future AI, effect, and UI systems; not consumed by any handler in this slice |

No handler subscribes to `EntityStateChangedEvent` in this slice. The event is published now to establish the observable surface; the combat slice (9) will be the first consumer.

---

## Design Notes

- **`TryEnterState` returns `failReason`, not throws.** Command bodies receive a displayable string and choose how to surface it — either write it directly or compose their own message. This avoids exception-as-control-flow while keeping the service pure (no I/O, no event bus). The pattern is consistent with `IAccountSystem.AuthenticateAsync` returning `AuthResult` rather than throwing on invalid credentials.

- **`ExitState` is unconditional.** It never returns a failure. Combat ending, death, and disconnect all need to unconditionally clear state; a failure path would require every caller to handle a case that should never logically occur (exiting a state the entity is not in is a benign no-op).

- **Transition rules are a static table, not a state machine.** MVP has three flags and four rule entries. A general state machine (`IState`, `ITransition`, guard delegates) would be premature engineering for three values. If flag count reaches ~8 or transition complexity requires guard chains, the implementation replaces the table with a proper transition graph without changing the `IEntityStateService` interface.

- **`EntityStateComponent` is removed when empty.** Keeping a zero-flag component attached to every entity that has ever entered any state would bloat the entity query surface. Removing on `ExitState` when `ActiveStates == None` keeps entity state lean. This means `HasComponent<EntityStateComponent>` is a reliable "entity is in at least one state" check.

- **No `[Persistent]` on `EntityStateComponent` — this is the decisive design choice.** A crash or restart drops all active state flags. Players reconnect with their last-flushed HP and inventory; combat and rest state is re-established by player action. This avoids orphaned state (an entity in `InCombat` whose opponent was destroyed and whose `CombatStateComponent` never matched), removes the need for a startup migration guard for state flags, and matches the MUD convention that crashes reset in-progress actions. The downside (a resting player reconnects without the rest flag) is acceptable for Phase 3 — rest mechanics do not yet produce ongoing effects that would be disrupted.

- **`EntityStateFlags` as `[Flags]` enum, not a `HashSet<EntityStateFlags>`.** Flags enum enables O(1) bitwise checks and direct comparison without allocation. The limit of ~30 named values for a single `int`-backed `[Flags]` enum is not a concern at the projected flag count for this engine.

- **Combat slice (9) interaction.** The combat use case (`combat.md`) currently specifies `CombatStateComponent { OpponentEntityId: uint }` as the metadata component tracking who the combatant is fighting. This slice does not replace that component. The relationship is: `EntityStateComponent.InCombat` is the observable flag (guarding `rest`, `flee`, re-`kill`); `CombatStateComponent.OpponentEntityId` is the metadata (telling the pulse who to fight). Both coexist. `KillCommand` attaches both; the combat pulse checks `CombatStateComponent`; state-gated commands check `EntityStateComponent` via `IEntityStateService.IsInState`. This dual-component design preserves separation between "is in a state" (observable, cross-cutting) and "what is the state's parameters" (domain-specific metadata).

- **Why `IEntityStateService` instead of direct `HasComponent` calls in command bodies.** The transition rule table must be enforced centrally. Without a service, every command that enters or exits a state must re-implement the rule check — a pattern that fails when a new rule is added and some commands miss the update. The service is the single enforcement point. Commands that only *read* state (`IsInState`) could safely call `HasComponent<EntityStateComponent>` directly, but routing all state access through `IEntityStateService` keeps the call pattern uniform and makes the service the authoritative query surface for any future rule changes.

---

## Related

- [`combat.md`](combat.md) — slice 9; the primary consumer of `IEntityStateService`. `KillCommand` calls `TryEnterState(InCombat)`; `FleeCommand` calls `ExitState(InCombat)`; state guards on `kill` (already in combat) and `flee` (not in combat) use `IsInState`.
- [`attributes.md`](attributes.md) — slice 8a; `PoolsComponent.CurrentHp` reaching zero is the trigger for the `Incapacitated` flag in the combat slice. `IAttributeSystem` is the read/write surface for HP; `IEntityStateService` is the state-flag surface.
- [`mobs.md`](mobs.md) — slice 8; mob entities are state subjects — `IEntityStateService` is called with mob entity ids as well as player entity ids.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; the two-level opt-in is why `EntityStateComponent` omits `[Persistent]` safely without affecting other components on the same entity.
- [`command-framework.md`](command-framework.md) — slice 3; state-gated commands follow the existing `ICommand` pattern; `failReason` is rendered as `PlainMessage` via `IOutputWriter`.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
