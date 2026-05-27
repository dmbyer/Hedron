# Use Case: Entity State Management

**Status:** planned
**Actors:** System (domain service); Player, Mob (as state subjects); Commands and Handlers (as state callers)
**Module:** `Core/Modules/EntityState/` (new)

---

## Description

Introduces a unified, flag-based entity state layer that complements (does not replace) metadata components. Before this slice, determining whether an entity is "in combat," "resting," or "incapacitated" requires knowing the exact component type to query — an implicit, undocumented contract that grows harder to manage as state combinations multiply. This slice introduces `EntityStateComponent` (a cross-cutting `[Flags]` enum holder) and `IEntityStateService` (a domain system that enforces transition rules, attaches/removes the component, and returns structured failure reasons). `EntityStateChangedEvent` is published by command and handler callers — not by the service (INV-5). The combat slice (9) is the primary consumer; `IEntityStateService` provides the authoritative "is this entity in state X?" query surface for all future state-gated commands.

**Design scope.** `EntityState` flags for MVP: `None`, `InCombat`, `Resting`, `Incapacitated`. Transition rules are a static lookup table — no state machine framework. No admin commands in this slice; the `state` debug command is acknowledged debt.

---

## Preconditions

- Slices 1–8a complete.
- `EntityService` (via `HasComponent<T>`, `TryGet<T>`, `AddComponent`, `RemoveComponent`) is the only mechanism used to attach/remove `EntityStateComponent` — no new ECS infrastructure required.
- `IEventBus` is available to callers (commands, handlers) that will publish `EntityStateChangedEvent` after service calls.

---

## Postconditions

- `EntityState` `[Flags]` enum exists in `Core/ECS/Components/` with values `None = 0`, `InCombat = 1 << 0`, `Resting = 1 << 1`, `Incapacitated = 1 << 2`.
- `EntityStateComponent { EntityState ActiveStates }` exists in `Core/ECS/Components/`. Not `[Persistent]`. Attached by `IEntityStateService.TryEnterState`; removed (when empty) by `IEntityStateService.ExitState`.
- `IEntityStateService` (domain system, `Core/Modules/EntityState/Systems/`) is registered as a singleton and satisfies:
  - `TryEnterState(uint entityId, EntityState state, out string? failReason)` — validates transition rules, attaches or updates `EntityStateComponent`, returns `true` on success.
  - `ExitState(uint entityId, EntityState state)` — removes the flag; removes the component when no flags remain.
  - `IsInState(uint entityId, EntityState state)` — flag check; returns `false` if the entity has no `EntityStateComponent`.
  - `GetStates(uint entityId)` — returns the full `ActiveStates` flags value, or `EntityState.None` if the component is absent.
- `EntityStateChangedEvent(uint EntityId, EntityState OldStates, EntityState NewStates)` exists in `Core/Modules/EntityState/Events/`.
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

1. A command (e.g. `KillCommand`) calls `IEntityStateService.TryEnterState(playerEntityId, EntityState.InCombat, out failReason)`.
2. The service reads `EntityStateComponent` from the entity (or treats `ActiveStates` as `None` if the component is absent).
3. The service checks the transition rule table for `InCombat`. If `Incapacitated` flag is set → returns `false`, `failReason = "You cannot enter combat while incapacitated."`.
4. On success: if the entity lacks `EntityStateComponent`, the service calls `EntityService.AddComponent(entityId, new EntityStateComponent { ActiveStates = state })`. If the component exists, it OR-assigns the flag: `component.ActiveStates |= state`. Returns `true`, `failReason = null`.
5. The calling command captures `OldStates` before the call and publishes `EntityStateChangedEvent(entityId, oldStates, newStates)` after a successful `TryEnterState` (INV-5 — event publication is in the command, not the service).

### Flow ES-2 — Exiting a state (example: combat ends)

1. A command or handler (e.g. `FleeCommand` or `CombatMobDeathHandler`) calls `IEntityStateService.ExitState(entityId, EntityState.InCombat)`.
2. The service reads `EntityStateComponent`. If absent, the call is a no-op.
3. The service clears the flag: `component.ActiveStates &= ~state`. If `ActiveStates == None` after the operation, the service calls `EntityService.RemoveComponent<EntityStateComponent>(entityId)`.
4. The calling command or handler publishes `EntityStateChangedEvent(entityId, oldStates, newStates)`.

### Flow ES-3 — State-gated command guard (example: `rest`)

1. Player sends `rest`. `RestCommand.ExecuteAsync` calls `IEntityStateService.IsInState(playerEntityId, EntityState.InCombat)`.
2. If `true`, the command writes `PlainMessage("You cannot rest while in combat.")` and returns without attempting `TryEnterState`. (The command can read the guard before calling `TryEnterState`, or rely on `TryEnterState`'s `failReason` return — both patterns are valid. The guard-read pattern is preferred when a crisply worded player message is needed before the service call incurs side-effects.)
3. If `false`, the command calls `IEntityStateService.TryEnterState(playerEntityId, EntityState.Resting, out failReason)`.
4. On success, the command publishes `EntityStateChangedEvent` and writes a confirmation message.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `EntityStateChangedEvent` | Commands and handlers that call `IEntityStateService` | `uint EntityId, EntityState OldStates, EntityState NewStates` | Observable hook for future AI, effect, and UI systems; not consumed by any handler in this slice |

No handler subscribes to `EntityStateChangedEvent` in this slice. The event is published now to establish the observable surface; the combat slice (9) will be the first consumer.

---

## Systems / Handlers Involved

### New: `IEntityStateService` (domain, `Core/Modules/EntityState/Systems/`)

```csharp
public interface IEntityStateService
{
    bool TryEnterState(uint entityId, EntityState state, out string? failReason);
    void ExitState(uint entityId, EntityState state);
    bool IsInState(uint entityId, EntityState state);
    EntityState GetStates(uint entityId);
}
```

Implementation notes:
- `TryEnterState`: reads current `ActiveStates` (or `None` if component absent), evaluates the static transition-rule table, on success attaches or OR-assigns, returns `true`. On block, returns `false` with a caller-displayable `failReason` string. Never calls `IEventBus` (INV-5).
- `ExitState`: AND-NOT clears the flag; removes the component when `ActiveStates == None`. Never fails. Never calls `IEventBus` (INV-5).
- `IsInState`: returns `(GetStates(entityId) & state) != 0`.
- `GetStates`: returns `EntityStateComponent.ActiveStates` or `EntityState.None`.
- No async methods — all operations are synchronous in-memory mutations on `EntityService`.
- **Dependencies:** `EntityService` only.

### New: `EntityStateComponent` (cross-cutting, `Core/ECS/Components/`)

```csharp
public class EntityStateComponent : IComponent
{
    public EntityState ActiveStates { get; set; }
}
```

Not `[Persistent]`. Rationale: transient state surviving a crash would be stale and potentially corrupt — the opponent entity in a combat pair may not exist on the next boot. All flag state is re-established by the gameplay actions that create it (entering a room, initiating combat, etc.) at runtime. Absent on entity construction; created on first `TryEnterState` call; destroyed by `ExitState` when `ActiveStates` reaches `None`.

### New: `EntityState` enum (co-located with `EntityStateComponent`, `Core/ECS/Components/`)

```csharp
[Flags]
public enum EntityState
{
    None         = 0,
    InCombat     = 1 << 0,   // 1
    Resting      = 1 << 1,   // 2
    Incapacitated = 1 << 2,  // 4
}
```

### No new handlers in this slice

`EntityStateChangedEvent` is published but has no subscribers in this slice. This is intentional: the event establishes the observable surface without requiring a subscriber. The combat slice (9) will add `CombatHandler` and `CombatMobDeathHandler`, both of which call `IEntityStateService.ExitState` and publish `EntityStateChangedEvent` as part of their handler body.

### No new commands in this slice

The `state` debug command (admin-only, displays current flags on a target entity) is explicitly deferred. It is acknowledged debt (see Open Questions).

---

## Content Tooling Impact

**None.** This slice introduces no authored content, no YAML template kind, and no `TemplateRegistry` entries. `EntityStateComponent` is runtime-only transient state that carries no author-facing configuration. The transition rule table is a static in-code lookup with no designer-configurable parameters in this phase.

Justification (INV-18): the slice is pure infrastructure — a domain service and a transient component. It produces no gameplay state that a designer must author or inspect. The first content-facing concern (which mobs or players can enter which states) belongs to the combat slice (9) and later AI/effect slices, which will surface configuration at that point if needed.

---

## Cross-Cutting Surfaces Stressed

### Commands — Adequate

No new commands in this slice. Future command bodies call `IEntityStateService` as a domain service and publish `EntityStateChangedEvent` using the existing `IEventBus` injection pattern. No command-framework change needed.

### Output — Adequate

No new `IOutputMessage` shapes required. State-gated commands write `PlainMessage` via existing `IOutputWriter`. The `failReason` string from `TryEnterState` is rendered as a `PlainMessage` by the calling command.

### Persistence — see sub-check below.

### Event bus — Adequate

`EntityStateChangedEvent` follows the existing thin-payload past-tense event pattern (INV-6). Publishers are commands and handlers (INV-5). No handler subscribes in this slice — the bus tolerates zero-subscriber events without error; this pattern is already established by events published in the login flow before `CharacterHydrationHandler` was added.

### ECS queries — Adequate

`IEntityStateService` calls `EntityService.HasComponent<EntityStateComponent>`, `TryGet<EntityStateComponent>`, `AddComponent`, and `RemoveComponent` — all existing `EntityService` interface methods used throughout the codebase. No new query pattern introduced.

The combat pulse (slice 9 — `CombatPulseService`) will query `GetAllComponents<EntityStateComponent>()` or `GetAllComponents<CombatStateComponent>()` to enumerate active combatants. That query surface is already used by `PersistenceSystem` and is documented as adequate-for-Phase-3 in `combat.md`. Not a gap introduced here.

### Broadcast — Adequate

No room-scope broadcast in this slice. State-change messages are single-recipient `PlainMessage` output from the calling command.

### Time — Not exercised

No scheduled work in this slice.

### Content templates — Not exercised

No YAML, no `TemplateRegistry` entries, no `ITemplateDeserializer`.

### Configuration — Not exercised

No new `appsettings.json` keys. The transition rule table is a static in-code data structure with no external configuration in this phase.

### Sessions — Adequate

`IEntityStateService` is keyed on `uint entityId`, never on `ISession`. Session fan-out (broadcast to witnesses) is deferred to the calling command or handler. No change to `ISessionManager`.

### Modules — Adequate

`EntityStateModule` follows the existing `AddXModule(IServiceCollection)` DI extension pattern used by `AddMobsModule`, `AddItemsModule`, etc. Registered in `Server/Program.cs`. No new hosting-service infrastructure.

---

### Persistence opt-in audit

**Level 1 — entity opt-in.**

`EntityStateComponent` is attached to entities that already carry `PersistentEntity` (player entities, mob entities). The component's attachment does not change the entity's persistence opt-in status — `PersistentEntity` was placed at construction time for players (slice 5, `AccountSystem.CreateCharacterAsync`) and for template-spawned mobs (slice 8, `WorldContentLoader`). No new construction paths in this slice; no `PersistentEntity` placement to audit.

**Level 2 — component `[Persistent]` status.**

| Component | `[Persistent]`? | Rationale |
|---|---|---|
| `EntityStateComponent` | No | Transient: flags represent runtime combat, rest, and incapacitation state. Persisting these flags would leave entities stuck in states whose context (opponent entities, event triggers) may not exist after restart. Cleared on restart by design. |
| `EntityState` (enum) | n/a | Enum value stored in `EntityStateComponent`; persistence controlled by the component tag above. |

No existing components are introduced, modified, or newly tagged in this slice. `PlayerComponent`, `MobDataComponent`, `AttributesComponent`, and `PoolsComponent` — which are read by callers of this service — retain their existing `[Persistent]` status unchanged.

**Level 3 — entity ID stability.**

No new entity types are spawned by `WorldContentLoader.SpawnMissingEntities`. Not applicable.

**Level 4 — restore vs. spawn placement guard.**

No `PlaceXInRooms`-style placement logic in this slice. Not applicable.

---

## Flows Introduced or Modified

### New: Flow 16 — Entity state transition (TryEnterState / ExitState)

This flow is introduced here and referenced by the combat slice (9). It is a micro-flow invoked inside command and handler bodies; it is not a standalone player-command flow. Because it is embedded inside other flows (combat initiation, flee, mob death), it does not get a top-level `flows/README.md` entry in isolation — it is documented as a sub-step of the flows that consume it (Flow 16 for `kill`, Flow 17 for combat round pulse, Flow 18 for `flee` in the combat use case).

**`flows/README.md` update required:** none in this slice. The flows that will reference `IEntityStateService` calls (combat initiation, combat round, flee) are defined in the combat slice (9) as Flows 16–18. The `flows/README.md` index update ships with slice 9.

**Rationale for deferral:** `IEntityStateService` is infrastructure — it has no player-visible flow of its own. It is a sub-step within flows whose triggers and outputs are owned by the combat slice. Adding a standalone flow entry for an internal service call with no player-visible trigger would violate INV-D1 (one fact, one home) by splitting the combat flow narrative across two slices.

### No modifications to existing flows

`IEntityStateService` does not change the server-startup, player-command, or persistence-flush flows. No hosted service is added.

---

## Design Notes

- **`TryEnterState` returns `failReason`, not throws.** Command bodies receive a displayable string and choose how to surface it — either write it directly or compose their own message. This avoids exception-as-control-flow while keeping the service pure (no I/O, no event bus). The pattern is consistent with `IAccountSystem.AuthenticateAsync` returning `AuthResult` rather than throwing on invalid credentials.

- **`ExitState` is unconditional.** It never returns a failure. Combat ending, death, and disconnect all need to unconditionally clear state; a failure path would require every caller to handle a case that should never logically occur (exiting a state the entity is not in is a benign no-op).

- **Transition rules are a static table, not a state machine.** MVP has three flags and four rule entries. A general state machine (`IState`, `ITransition`, guard delegates) would be premature engineering for three values. If flag count reaches ~8 or transition complexity requires guard chains, the implementation replaces the table with a proper transition graph without changing the `IEntityStateService` interface.

- **`EntityStateComponent` is removed when empty.** Keeping a zero-flag component attached to every entity that has ever entered any state would bloat the entity query surface. Removing on `ExitState` when `ActiveStates == None` keeps entity state lean. This means `HasComponent<EntityStateComponent>` is a reliable "entity is in at least one state" check.

- **No `[Persistent]` on `EntityStateComponent` — this is the decisive design choice.** A crash or restart drops all active state flags. Players reconnect with their last-flushed HP and inventory; combat and rest state is re-established by player action. This avoids orphaned state (an entity in `InCombat` whose opponent was destroyed and whose `CombatStateComponent` never matched), removes the need for a startup migration guard for state flags, and matches the MUD convention that crashes reset in-progress actions. The downside (a resting player reconnects without the rest flag) is acceptable for Phase 3 — rest mechanics do not yet produce ongoing effects that would be disrupted.

- **`EntityState` as `[Flags]` enum, not a `HashSet<EntityState>`.** Flags enum enables O(1) bitwise checks and direct comparison without allocation. The limit of ~30 named values for a single `int`-backed `[Flags]` enum is not a concern at the projected flag count for this engine.

- **Combat slice (9) interaction.** The combat use case (`combat.md`) currently specifies `CombatStateComponent { OpponentEntityId: uint }` as the metadata component tracking who the combatant is fighting. This slice does not replace that component. The relationship is: `EntityStateComponent.InCombat` is the observable flag (guarding `rest`, `flee`, re-`kill`); `CombatStateComponent.OpponentEntityId` is the metadata (telling the pulse who to fight). Both coexist. `KillCommand` attaches both; the combat pulse checks `CombatStateComponent`; state-gated commands check `EntityStateComponent` via `IEntityStateService.IsInState`. This dual-component design preserves separation between "is in a state" (observable, cross-cutting) and "what is the state's parameters" (domain-specific metadata).

- **Why `IEntityStateService` instead of direct `HasComponent` calls in command bodies.** The transition rule table must be enforced centrally. Without a service, every command that enters or exits a state must re-implement the rule check — a pattern that fails when a new rule is added and some commands miss the update. The service is the single enforcement point. Commands that only *read* state (`IsInState`) could safely call `HasComponent<EntityStateComponent>` directly, but routing all state access through `IEntityStateService` keeps the call pattern uniform and makes the service the authoritative query surface for any future rule changes.

---

## Open Questions

1. **Admin `state` debug command (deferred).** An admin `state <target>` command that displays `EntityStateComponent.ActiveStates` as a formatted flag list (e.g. `"InCombat | Resting"`) would be useful during combat testing. This is deferred because: (a) there is no player-facing state to inspect in this slice alone; (b) the useful inspection target is a player or mob in active combat — a scenario that only exists after slice 9. **Disposition: acknowledged debt.** Tracked for inclusion in slice 9 or as a standalone admin-tooling pass. No backlog entry required — the combat use case's admin commands section (`setroundtime`) is the natural landing point.

2. **Future flags (e.g. `Invisible`, `Stunned`, `Dead`).** The `EntityState` enum is designed for extension. Adding a new flag requires: (a) a new enum value, (b) a new row in the transition rule table if the flag blocks any existing state, (c) commands that enter/exit the flag. No interface change, no migration guard. **Disposition: no action now; the design accommodates extension.**

3. **`Incapacitated` flag usage in slice 9.** The combat use case currently specifies that player HP reaching zero results in the player being clamped to 1 HP and auto-fleeing, with `CombatEndedEvent(Outcome=PlayerIncapacitated)` published. If the combat slice sets the `Incapacitated` flag via `IEntityStateService.TryEnterState` at this point, the `InCombat` transition rule (blocked while `Incapacitated`) would prevent re-entering combat until the flag is cleared. **This is the intended integration,** but it requires the combat slice to call both `ExitState(InCombat)` and `TryEnterState(Incapacitated)` on the incapacitated player — the order and caller (handler vs. service) should be decided when slice 9 is implemented. **No blocker for this spec.**

---

## Reference Catalog Updates

### `docs/reference/components.md`

Add to the **Infrastructure (cross-cutting)** table:

| Component | Shape | Used by | Persisted? |
|---|---|---|---|
| `EntityStateComponent` | `ActiveStates: EntityState` — tracks active state flags for any entity; absent when `ActiveStates == None` | `IEntityStateService`; commands and handlers that guard on entity state | no — transient; cleared on restart by design |

Add enum note:

| `EntityState` | `[Flags]` enum `{ None=0, InCombat=1, Resting=2, Incapacitated=4 }` — co-located with `EntityStateComponent` in `Core/ECS/Components/` | n/a (enum, not a component) |

### `docs/reference/systems.md`

Add to **Domain / feature Systems**:

**EntityStateService**
**Purpose:** Centralized transition-rule enforcement for entity state flags. Attaches and removes `EntityStateComponent`; validates flag combinations against a static transition table; returns structured failure reasons to callers. Never touches the event bus or persistence (INV-5).
**Location:** `Core/Modules/EntityState/Systems/EntityStateService.cs`
**Dependencies:** `EntityService`.

```csharp
public interface IEntityStateService
{
    bool TryEnterState(uint entityId, EntityState state, out string? failReason);
    void ExitState(uint entityId, EntityState state);
    bool IsInState(uint entityId, EntityState state);
    EntityState GetStates(uint entityId);
}
```

---

## Related

- [`combat.md`](combat.md) — slice 9; the primary consumer of `IEntityStateService`. `KillCommand` calls `TryEnterState(InCombat)`; `FleeCommand` calls `ExitState(InCombat)`; state guards on `kill` (already in combat) and `flee` (not in combat) use `IsInState`.
- [`attributes.md`](attributes.md) — slice 8a; `PoolsComponent.CurrentHp` reaching zero is the trigger for the `Incapacitated` flag in the combat slice. `IAttributeSystem` is the read/write surface for HP; `IEntityStateService` is the state-flag surface.
- [`mobs.md`](mobs.md) — slice 8; mob entities are state subjects — `IEntityStateService` is called with mob entity ids as well as player entity ids.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; the two-level opt-in is why `EntityStateComponent` omits `[Persistent]` safely without affecting other components on the same entity.
- [`command-framework.md`](command-framework.md) — slice 3; state-gated commands follow the existing `ICommand` pattern; `failReason` is rendered as `PlainMessage` via `IOutputWriter`.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
