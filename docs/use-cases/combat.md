# Use Case: Combat

**Status:** planned
**Actors:** Player, Mob, System
**Module:** `Core/Modules/Combat/` (new); `Core/Modules/Mobs/` (mob death path); `Core/Modules/EntityState/` (state management — slice 9-a); `Core/Modules/Time/` (heartbeat — slice 9-b); `Core/Modules/Stats/` (stat computation — slice 9-c); `Core/ECS/Components/` (`CombatStateComponent` — cross-cutting transient)

---

## Description

Introduces the core combat loop: a player sends `kill <mob>`, which initiates melee combat between the player and the target mob. Combat state is tracked through two complementary layers introduced by prerequisite slices: `EntityStateComponent` (centralized state flag, slice 9-a) records that an entity is `InCombat`; `CombatStateComponent` (combat-specific metadata) records the opponent's entity id. Combat rounds are driven by the `HeartbeatTickEvent` (slice 9-b) — a `CombatTickHandler` subscribes to each tick, processes all active combat pairs, and publishes per-round results. Effective stats (attack power, defense) are computed by `IStatSystem` (slice 9-c), which aggregates base attributes and equipment bonuses without the caller knowing the sources.

Combat ends when either participant's HP reaches zero: a mob at zero HP dies (entity destroyed, blueprint slot freed per INV-21, `CombatEndedEvent` published with `Outcome=MobDied`); a player at zero HP is reduced to 1 HP and combat ends (`CombatEndedEvent` with `Outcome=PlayerIncapacitated`), deferring true player death mechanics to slice 10. The `flee` command always succeeds and exits combat immediately.

**Design scope.** No spell casting, no skills, no group combat, no faction checks, no mob aggro. Weapon equipped in `MainHand` contributes attack power via `IStatSystem.GetEffectiveAttackPower` (reads `ItemDataComponent.DamageBonus` defined in slice 9-c). Round cadence is the global `Heartbeat:IntervalMs` config key — no `setroundtime` command.

**Prerequisites:** Slices 1–8a complete. Slices 9-a (entity state management), 9-b (time system), and 9-c (stat computation system) complete.

---

## Preconditions

- Slices 1–8a complete. Reused surfaces: `EntityService`, `IEventBus`, `IBroadcastSystem`, `IPersistenceSystem`, `ISessionManager`, `ICommandDispatcher`, `IAdminAuthorizer`, `AdminRequirement`, `MobDataComponent`, `BlueprintComponent`, `LocationComponent`, `AttributesComponent`, `PoolsComponent`, `EquipmentComponent`, `ItemDataComponent`.
- **Slice 9-a complete:** `EntityStateComponent`, `IEntityStateService` (`TryEnterState`, `ExitState`, `IsInState`, `GetStates`), `[Flags] EntityState { None, InCombat, Resting, Incapacitated }`.
- **Slice 9-b complete:** `HeartbeatBackgroundService`, `HeartbeatTickEvent { TickId: long, Timestamp: DateTimeOffset, Elapsed: TimeSpan }`, config key `Heartbeat:IntervalMs` (default 2000).
- **Slice 9-c complete:** `IStatSystem` (`GetEffectiveAttackPower`, `GetEffectiveDefense`, `GetCurrentHp`, `GetMaxHp`); `IAttributeSystem.SetCurrentHp(uint, int)` with `[0, MaxHp]` clamp; `ItemDataComponent.DamageBonus: int` (default 0); `setitem dmg <n>` admin command.
- Every player and mob entity carries `AttributesComponent` and `PoolsComponent` (slice 8a guaranteed).
- Every mob entity carries `MobDataComponent`, `LocationComponent`, `BlueprintComponent`.

---

## Postconditions

- `CombatStateComponent { OpponentEntityId: uint }` exists at `Core/ECS/Components/CombatStateComponent.cs` — cross-cutting, not `[Persistent]`. Metadata companion to `EntityState.InCombat`; holds who this entity is fighting.
- `ICombatSystem` (domain, `Core/Modules/Combat/Systems/`) computes attack resolution using `IStatSystem` and mutates HP via `IAttributeSystem.SetCurrentHp`. Returns `CombatRoundResult` records; never touches the event bus or persistence (INV-5, INV-8).
- `CombatTickHandler` (priority 20, `Core/Modules/Combat/Handlers/`, subscribes to `HeartbeatTickEvent`) queries all entities with `CombatStateComponent`, deduplicates pairs, calls `ICombatSystem.ExecuteRound`, and publishes `CombatRoundEvent` and optionally `CombatEndedEvent`. This handler is the bridge between the time system and the combat domain.
- `kill <mob>`: calls `IEntityStateService.TryEnterState(InCombat)` and `ICombatSystem.StartCombat`; publishes `CombatStartedEvent`.
- `flee`: calls `ICombatSystem.EndCombat` + `IEntityStateService.ExitState(InCombat)` on both participants; publishes `CombatEndedEvent(Outcome=PlayerFled)`.
- On `MobDied`: `CombatMobDeathHandler` calls `IEntityStateService.ExitState(InCombat)` on the surviving player, clears `BlueprintComponent` on the mob (INV-21), calls `EntityService.DestroyEntity(mobEntityId)`.
- On `PlayerIncapacitated`: `CombatTickHandler` clamps player HP to 1, calls `ICombatSystem.EndCombat` + `IEntityStateService.ExitState` on both, publishes `CombatEndedEvent(PlayerIncapacitated)`.
- `CombatHandler` (priority 20) handles `CombatStartedEvent`, `CombatRoundEvent`, `CombatEndedEvent` for output fan-out only. Does not call systems or mutate state.
- `AdminAuditHandler` extended to log `CombatEndedEvent`.
- `CombatModule` DI entry point (`Core/Modules/Combat/CombatModule.cs`), called from `Server/Program.cs`.

---

## Main Flow

### Flow C-1 — `kill <mob>` (combat initiation)

1. Player sends `kill <mob>`. `KillCommand.ExecuteAsync` runs (no privilege requirement).
2. **State guard.** Calls `IEntityStateService.IsInState(playerEntityId, EntityState.InCombat)`. If true → "You are already fighting!" and return.
3. **Target resolution.** Reads invoker's `LocationComponent.RoomEntityId`. Calls `ICombatSystem.TryFindTargetInRoom(roomId, token, out uint mobEntityId)` — prefix-matches `token` against `MobDataComponent.Name` and `MobDataComponent.Keywords` for all entities with `MobDataComponent` whose `LocationComponent.RoomEntityId == roomId`. No match → "You don't see that here."
4. **State transition (player).** Calls `IEntityStateService.TryEnterState(playerEntityId, EntityState.InCombat, out failReason)`. On failure → writes `failReason` and returns. (Guards against racing state changes between steps 2 and 4.)
5. **State transition (mob).** Calls `IEntityStateService.TryEnterState(mobEntityId, EntityState.InCombat, out _)`. Mobs do not reject; failure is a warn log and a no-op (mob has no player session to write errors to).
6. **Combat metadata.** Calls `ICombatSystem.StartCombat(playerEntityId, mobEntityId)` — attaches `CombatStateComponent { OpponentEntityId }` to both entities. No persistence call (transient).
7. **Event.** Publishes `CombatStartedEvent(AttackerEntityId: playerEntityId, DefenderEntityId: mobEntityId, RoomEntityId)`.
8. **Output.** `CombatHandler` (priority 20) handles `CombatStartedEvent`: writes "You attack <mob>!" to the attacker; broadcasts "<PlayerName> attacks <mob>!" to other room occupants.

### Flow C-2 — Combat round pulse (heartbeat-driven)

1. `HeartbeatBackgroundService` (slice 9-b) publishes `HeartbeatTickEvent` every `Heartbeat:IntervalMs` ms (default 2000).
2. `CombatTickHandler` (priority 20, subscribes to `HeartbeatTickEvent`) queries all entities with `CombatStateComponent`. Deduplicates into unique attacker→defender pairs: for each entity with `CombatStateComponent`, process the pair only when `entityId < OpponentEntityId` (prevents A→B and B→A from both being processed as separate pairs).
3. For each pair, calls `ICombatSystem.ExecuteRound(attackerEntityId, defenderEntityId)` → `CombatRoundResult`.
4. Publishes `CombatRoundEvent(AttackerEntityId, DefenderEntityId, RoomEntityId, Result)`.
5. `CombatHandler` (priority 20) handles `CombatRoundEvent`: broadcasts hit/miss/damage narrative to all players in the room.
6. **Mob death path** (if `result.Outcome == MobDied`): `CombatTickHandler` reads `MobDataComponent.Name` from the mob entity **before** publishing (point-in-time capture), then publishes `CombatEndedEvent(Outcome=MobDied, DefenderName=mobName)`. `CombatHandler` (priority 20) receives the event first and broadcasts "You have slain <mob>!" using `DefenderName` from the payload. `CombatMobDeathHandler` (priority 80) receives the event after, calls `IEntityStateService.ExitState(attackerEntityId, InCombat)`, clears `BlueprintComponent` (INV-21), and calls `EntityService.DestroyEntity(mobEntityId)`. Priority ordering ensures output before destruction.
7. **Player incapacitation path** (if `result.Outcome == PlayerIncapacitated`): `CombatTickHandler` calls `IAttributeSystem.SetCurrentHp(playerEntityId, 1)` (clamp to 1 instead of 0 — stub for slice 10), then `ICombatSystem.EndCombat` + `IEntityStateService.ExitState(InCombat)` on both entities, then publishes `CombatEndedEvent(Outcome=PlayerIncapacitated)`. `CombatHandler` broadcasts incapacitation narrative.

### Flow C-3 — `flee` (player-initiated combat exit)

1. Player sends `flee`. `FleeCommand.ExecuteAsync` runs (no privilege requirement).
2. Checks `IEntityStateService.IsInState(playerEntityId, EntityState.InCombat)`. If not → "You are not in combat."
3. Reads `CombatStateComponent.OpponentEntityId` to identify the mob.
4. Calls `ICombatSystem.EndCombat(playerEntityId, mobEntityId)` — removes `CombatStateComponent` from both entities.
5. Calls `IEntityStateService.ExitState(playerEntityId, EntityState.InCombat)` and `IEntityStateService.ExitState(mobEntityId, EntityState.InCombat)`.
6. Publishes `CombatEndedEvent(AttackerEntityId: playerEntityId, DefenderEntityId: mobEntityId, Outcome: PlayerFled, RoomEntityId)`.
7. `CombatHandler` broadcasts "<PlayerName> flees from combat!" to the room; writes "You flee from combat!" to the player.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `CombatStartedEvent` | `KillCommand` | `uint AttackerEntityId, uint DefenderEntityId, uint RoomEntityId` | Initiates combat; future AI/faction hooks subscribe |
| `CombatRoundEvent` | `CombatTickHandler` | `uint AttackerEntityId, uint DefenderEntityId, uint RoomEntityId, CombatRoundResult Result` | Per-round narrative and outcome routing |
| `CombatEndedEvent` | `CombatTickHandler`, `FleeCommand` | `uint AttackerEntityId, uint DefenderEntityId, CombatEndOutcome Outcome, uint RoomEntityId, string? DefenderName` | Outcome routing; `CombatMobDeathHandler` responds to `MobDied`; slice 10 responds to `PlayerIncapacitated`. `DefenderName` is captured at publish time (before any entity destruction) so `CombatHandler` can render the death narrative without re-reading a potentially destroyed entity. |

`CombatEndOutcome` enum: `MobDied`, `PlayerIncapacitated`, `PlayerFled`.

---

## Systems / Handlers Involved

### New: `ICombatSystem` (domain, `Core/Modules/Combat/Systems/`)

```csharp
public interface ICombatSystem
{
    bool TryFindTargetInRoom(uint roomEntityId, string token, out uint mobEntityId);
    void StartCombat(uint attackerEntityId, uint defenderEntityId);
    void EndCombat(uint attackerEntityId, uint defenderEntityId);
    CombatRoundResult ExecuteRound(uint attackerEntityId, uint defenderEntityId);
}

public readonly record struct CombatRoundResult(
    uint AttackerEntityId,
    uint DefenderEntityId,
    int DamageDealt,
    bool AttackerHit,
    CombatRoundOutcome Outcome);

public enum CombatRoundOutcome { Hit, Miss, MobDied, PlayerIncapacitated }
```

`ExecuteRound` formula (pure math; no events; no persistence — INV-5/INV-8):

- **Hit check:** `roll = Random.Shared.Next(1, 21) + IStatSystem.GetEffectiveDexterity(attackerEntityId) / 2`. Hit if `roll >= 10 + IStatSystem.GetEffectiveDefense(defenderEntityId)`. Miss → `DamageDealt = 0`.
- **Damage:** `base = Random.Shared.Next(1, IStatSystem.GetEffectiveAttackPower(attackerEntityId) + 2)`. Calls `IAttributeSystem.SetCurrentHp(defenderEntityId, currentHp - damage)` — clamp to `[0, MaxHp]` enforced in the setter (INV-8).
- **Outcome determination:** `MobDied` if defender has `MobDataComponent` and `IStatSystem.GetCurrentHp(defenderEntityId) == 0`. `PlayerIncapacitated` if defender has `CharacterComponent` and HP == 0.
- `TryFindTargetInRoom`: iterates entities with `MobDataComponent` + matching `LocationComponent.RoomEntityId`; prefix-matches `token` against `MobDataComponent.Keywords` and `MobDataComponent.Name`.
- `StartCombat` / `EndCombat`: attaches / removes `CombatStateComponent` via `EntityService`. Does not call `IEntityStateService` — cohesion preference; commands and handlers coordinate both layers. See Design Notes.

### New: `CombatTickHandler` (priority 20, `Core/Modules/Combat/Handlers/`)

**Subscribes to:** `HeartbeatTickEvent`

Bridge between the time system and combat. On each tick: queries all entities with `CombatStateComponent`, deduplicates pairs (lower entity id = attacker in pair), calls `ICombatSystem.ExecuteRound` for each, publishes `CombatRoundEvent`, and handles terminal outcomes inline (publishes `CombatEndedEvent`, calls cleanup methods before publishing so subscribers see a clean state).

### New: `CombatHandler` (priority 20, `Core/Modules/Combat/Handlers/`)

**Subscribes to:** `CombatStartedEvent`, `CombatRoundEvent`, `CombatEndedEvent`

Pure output fan-out. On `CombatStartedEvent`: broadcasts attack announcement. On `CombatRoundEvent`: broadcasts hit/miss/damage narrative. On `CombatEndedEvent(PlayerFled)`: broadcasts flee text. On `CombatEndedEvent(PlayerIncapacitated)`: broadcasts incapacitation narrative and writes personal message to the player. Does not call systems, does not mutate state.

### New: `CombatMobDeathHandler` (priority 80, `Core/Modules/Combat/Handlers/`)

**Subscribes to:** `CombatEndedEvent` where `Outcome == MobDied`

Priority 80 — deliberately lower than `CombatHandler` (priority 20). This ordering guarantee ensures `CombatHandler` reads the mob's name from `CombatEndedEvent.DefenderName` (point-in-time capture) and broadcasts the death narrative **before** `CombatMobDeathHandler` destroys the mob entity. Calls `IEntityStateService.ExitState(attackerEntityId, EntityState.InCombat)`, clears `BlueprintComponent` on the mob entity (INV-21), calls `EntityService.DestroyEntity(mobEntityId)`. Does not publish events. Loot drop deferred to slice 10.

### New: `KillCommand` (`Core/Modules/Combat/Commands/`)

Verb: `kill`, alias: `k`. `MatchingMode.Partial`. No `RequiredPrivileges`. Argument: `string target` (free text). Uses `IEntityStateService.IsInState` for in-combat guard; `ICombatSystem.TryFindTargetInRoom` for target resolution; `IEntityStateService.TryEnterState` + `ICombatSystem.StartCombat` for initiation; publishes `CombatStartedEvent`.

### New: `FleeCommand` (`Core/Modules/Combat/Commands/`)

Verb: `flee`. `MatchingMode.Partial`. No `RequiredPrivileges`. No arguments. Uses `IEntityStateService.IsInState` for guard; `ICombatSystem.EndCombat` + `IEntityStateService.ExitState` for cleanup; publishes `CombatEndedEvent(PlayerFled)`.

### Reused: `IStatSystem` (slice 9-c)

`ExecuteRound` calls `GetEffectiveAttackPower(attackerEntityId)`, `GetEffectiveDefense(defenderEntityId)`, `GetEffectiveDexterity(attackerEntityId)`, and `GetCurrentHp`. Never reads `IAttributeSystem` or `EquipmentComponent` directly — stat aggregation is entirely `IStatSystem`'s concern.

### Reused: `IAttributeSystem.SetCurrentHp` (slice 9-c)

Write seam for HP mutation. Clamp to `[0, MaxHp]` enforced by the setter; `CombatSystem` passes the raw subtracted value and trusts the clamp.

### Reused: `IEntityStateService` (slice 9-a)

`KillCommand` and `FleeCommand` call `TryEnterState`/`ExitState`/`IsInState`. `CombatMobDeathHandler` calls `ExitState(InCombat)` on the surviving player. `CombatTickHandler` calls `ExitState` on both participants for `PlayerIncapacitated`.

### Reused: `IBroadcastSystem`

`CombatHandler` uses `SendToRoomAsync` for all room-scope narrative.

### Extended: `AdminAuditHandler`

Subscribes to `CombatEndedEvent`; logs outcome + participant entity ids for audit visibility during testing.

---

## Content Tooling Impact

- **`setitem dmg <n>`** — defined and shipped in slice 9-c. Sets `ItemDataComponent.DamageBonus` on a weapon entity. YAML field `damageBonus` in item templates. No new admin command added in this slice.
- **No `setroundtime` command.** Round cadence is `Heartbeat:IntervalMs` in `appsettings.json` (slice 9-b). Tuning for local development: set `Heartbeat:IntervalMs` in `appsettings.Development.json`. Runtime mutation is not needed for Phase 3.
- **Designer workflow:** `mkmob` to create a mob, `setmob level/hp/str/dex/con` to configure it, `mkitem` to create a weapon, `setitem dmg <n>` to set its bonus, `kill <mob>` to fight it. No new YAML schema changes in this slice.

---

## Cross-Cutting Surfaces Stressed

### Commands — Adequate

`KillCommand` and `FleeCommand` follow the existing `ICommand` + `ICommandDispatcher` pattern exactly. No framework change needed.

### Output — Adequate

All narrative routed through `IBroadcastSystem.SendToRoomAsync` and `IOutputWriter.WriteAsync` with `PlainMessage`. No new `IOutputMessage` shape required — combat narrative is plain text for Phase 3.

### Event bus — Adequate

Three new event types follow the thin-payload past-tense pattern. `KillCommand` and `FleeCommand` are Initiators; `CombatTickHandler` subscribes to `HeartbeatTickEvent` and publishes round events (handler-as-secondary-publisher is an established pattern). INV-5 satisfied throughout.

### ECS queries — Adequate for Phase 3

`CombatTickHandler` performs a component-typed scan on each heartbeat tick to find all entities with `CombatStateComponent`. This is the same linear scan `PersistenceSystem` relies on. Adequate for Phase 3 entity counts (low thousands). Phase 4 performance pass covers hot-path LINQ.

### Broadcast — Adequate

Room-scope combat narrative via existing `SendToRoomAsync` with no audience filter (all room occupants see combat events). No new broadcast mode required.

### Time / heartbeat — Adequate (slice 9-b prerequisite)

`HeartbeatBackgroundService` publishes `HeartbeatTickEvent`; `CombatTickHandler` subscribes. Round cadence is `Heartbeat:IntervalMs`. If per-combat-type cadence is needed in future, a `Combat:RoundEveryNTicks` config key can filter which ticks `CombatTickHandler` processes — deferred.

### Entity state — Adequate (slice 9-a prerequisite)

`IEntityStateService.TryEnterState`/`ExitState`/`IsInState` gate all state transitions. `EntityState.InCombat` is the flag. Transition rules from slice 9-a enforce incompatibilities (e.g., cannot rest while `InCombat`).

### Stat computation — Adequate (slice 9-c prerequisite)

`IStatSystem` aggregates base stats + equipment bonus. `ICombatSystem.ExecuteRound` calls only `IStatSystem` methods — never reads attribute or equipment components directly. Future effects plug into `IStatSystem` without changing `ICombatSystem`.

### Persistence — Adequate

`CombatStateComponent` is not `[Persistent]` — transient state clears on restart by design. `PoolsComponent.CurrentHp` mutations persist via the area-scoped periodic flush (up to `Persistence:FlushIntervalSeconds` staleness on non-graceful exit — acceptable for Phase 3).

### Thread safety — Acknowledged debt

`CombatTickHandler` executes on the heartbeat background thread. `EntityService` and `IEventBus` must be safe for concurrent reads from player sessions and writes from the tick. Phase 4 thread-safety review now has a concrete starting point (`backlog.md`).

### Modules — Adequate

`CombatModule` (new, `Core/Modules/Combat/CombatModule.cs`) registers `ICombatSystem`, `CombatTickHandler`, `CombatHandler`, `CombatMobDeathHandler`, `KillCommand`, `FleeCommand`. Called from `Server/Program.cs`. Pattern mirrors `AddMobsModule`, `AddItemsModule`.

---

### Persistence opt-in audit

| Entity / component | Level 1 (PersistentEntity)? | Level 2 ([Persistent])? | Rationale |
|---|---|---|---|
| `CombatStateComponent` | n/a | No | Transient: stale combat state on restart would reference entities that may not exist. Cleared on crash by design. |
| `PoolsComponent.CurrentHp` | Existing | Yes (existing) | HP changes flushed by periodic timer. Up to 60 s potential loss on crash; acceptable Phase 3. |
| Mob entity (destroyed on death) | Existing | Yes (existing) | `DestroyEntity` removes the entity; pending flush cycles skip it. Stale entity files are harmless (ignored on next startup). Cleanup deferred to slice 10. |

---

## Flows Introduced or Modified

### New: Flow 16 — `kill <mob>` (combat initiation)

Added to `docs/architecture/flows/README.md`. Trigger: player sends `kill <mob>`. Covers: `KillCommand` → `IEntityStateService.TryEnterState(InCombat)` + `ICombatSystem.StartCombat` → `CombatStartedEvent` → `CombatHandler` (output).

### New: Flow 17 — Combat round pulse (heartbeat-driven)

Added to `docs/architecture/flows/README.md`. Trigger: `HeartbeatBackgroundService` publishes `HeartbeatTickEvent`. Covers: `CombatTickHandler` → `ICombatSystem.ExecuteRound` → `CombatRoundEvent` → `CombatHandler` (output) → optional `CombatEndedEvent` → `CombatMobDeathHandler` or `PlayerIncapacitated` cleanup.

### New: Flow 18 — `flee` (combat exit)

Added to `docs/architecture/flows/README.md`. Trigger: player sends `flee`. Covers: `FleeCommand` → `ICombatSystem.EndCombat` + `IEntityStateService.ExitState(InCombat)` → `CombatEndedEvent(PlayerFled)` → `CombatHandler` (output).

### Modified: Flow 1 — Server startup

`HeartbeatBackgroundService` is already registered by slice 9-b. This slice registers `CombatModule` which adds `CombatTickHandler` as a `HeartbeatTickEvent` subscriber — no startup ordering change beyond the existing `AddCombatModule()` call in `Program.cs`.

### Reference catalog updates

The implementation PR for this slice must update:
- `docs/reference/components.md` — add `CombatStateComponent` row (cross-cutting, transient, not persistent).
- `docs/reference/systems.md` — add `ICombatSystem` / `CombatSystem` entry.
- `docs/reference/handlers.md` — add `CombatTickHandler`, `CombatHandler`, `CombatMobDeathHandler` entries.
- `docs/reference/commands.md` — add `kill` (alias `k`, Partial, no privilege) and `flee` (Partial, no privilege) rows.

---

## Design Notes

- **Two-layer state model.** `EntityStateComponent` (slice 9-a) holds the `InCombat` flag; `CombatStateComponent` holds the metadata (opponent entity id). Commands gate on the flag via `IEntityStateService`; systems query the metadata via `CombatStateComponent`. `ICombatSystem` does not call `IEntityStateService` — this is a cohesion choice, not an INV-2 obligation (INV-2 constrains Core → Domain, not Domain → Domain; `01-layers.md` explicitly permits Domain → Domain). The separation is chosen because `IEntityStateService` is a lateral peer service that may trigger downstream reactions; commands and handlers are the right coordinators between two peer domain systems.

- **`CombatStateComponent` is not `[Persistent]`.** A crash or restart drops all active combat. Players reconnect with HP at the last flush; mobs re-spawn from templates at full HP on next startup or `reload`. This avoids orphaned state (an entity fighting an opponent that no longer exists) and is the correct MUD convention.

- **Mob death clears `BlueprintComponent` (INV-21).** `CombatMobDeathHandler` clears `BlueprintComponent` before calling `DestroyEntity`. This frees the blueprint slot so `WorldContentLoader.SpawnMissingEntities` (at startup or on `reload`) sees no live entity for that blueprint id and seeds a fresh mob. Destroy-and-re-seed is the chosen INV-21 path. Live timer-driven respawn is deferred to a future TimeSystem + respawn-manager slice.

- **Player death is stubbed.** When the player's HP would reach 0, it is clamped to 1 and combat ends with `CombatEndedEvent(PlayerIncapacitated)`. No death penalty, no corpse, no respawn flow. Slice 10 subscribes to `PlayerIncapacitated` to add the full mechanic. The event payload is shaped now so slice 10 needs no event-structure changes.

- **`flee` always succeeds.** No failure roll. Revisit in the skills slice if a chance-to-fail-flee is wanted.

- **No mob aggro.** Mobs do not initiate combat. A player must `kill <mob>` to start. Mob aggro (an AI tick that checks player proximity and calls `StartCombat`) belongs to the mob-wandering/AI slice.

- **`CombatTickHandler` is a handler, not an Initiator.** It subscribes to `HeartbeatTickEvent` and publishes `CombatRoundEvent`/`CombatEndedEvent`. Handlers publishing downstream events is an established pattern (INV-5 restricts *systems*, not handlers). If the event bus becomes a tick-frequency bottleneck this can be revisited in Phase 4 profiling.

- **`MobInRoomResolver` deferred.** `ICombatSystem.TryFindTargetInRoom` handles target resolution directly in this slice. An `IArgumentResolver`-backed `MobInRoomResolver` should be extracted when a second command needs mob-in-room resolution (e.g., `look <mob>`, `talk <mob>`). INV-19: extract at ≥3 uses.

- **Round deduplication.** Lower entity id is designated the "attacker" in the pair ordering. This prevents A→B and B→A from being processed as two separate rounds per tick.

- **Area-scoped flush covers `CurrentHp` mutations.** Save-on-change is not used for combat HP (round frequency ~0.5 Hz would make it prohibitive). Periodic flush (default 60 s) covers it.

---

## Open Questions

1. **`CombatTickHandler` at tick frequency.** The handler runs on every heartbeat tick even if no combat is active (scan returns empty). This is a fast no-op for Phase 3 scale. If the heartbeat is used for many other purposes at higher frequency, a "skip if no active combat" fast-path may be warranted — deferred to Phase 4 profiling.

2. **Thread safety of `EntityService` under concurrent heartbeat + sessions.** Phase 4 thread-safety review has this as a concrete entry point. Acknowledged for Phase 3.

3. **Mob respawn timing.** `BlueprintComponent` clearing and `DestroyEntity` free the slot; re-spawn happens on next startup or `reload` only. Live respawn deferred to `backlog.md` ("Mob death / respawn and `BlueprintComponent` clearing (INV-21)").

---

## Related

- [`entity-state-management.md`](entity-state-management.md) — slice 9-a; `IEntityStateService` and `EntityState.InCombat` used for state gating in `KillCommand` and `FleeCommand`.
- [`time-system.md`](time-system.md) — slice 9-b; `HeartbeatTickEvent` is the trigger for `CombatTickHandler`.
- [`stat-system.md`](stat-system.md) — slice 9-c; `IStatSystem` computes effective stats; `ItemDataComponent.DamageBonus` and `IAttributeSystem.SetCurrentHp` defined here.
- [`attributes.md`](attributes.md) — slice 8a; `AttributesComponent` and `PoolsComponent` are the data ground truth.
- [`mobs.md`](mobs.md) — slice 8; `MobDataComponent`, `BlueprintComponent`, and the destroy/re-seed INV-21 obligation originate here.
- [`equipment.md`](equipment.md) — slice 7; `WornSlot.MainHand` read by `IStatSystem` for weapon bonus.
- [`items-and-inventory.md`](items-and-inventory.md) — slice 6; `ItemDataComponent` extended with `DamageBonus` in slice 9-c.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — slice 5b; explains why non-persistent `CombatStateComponent` is safe.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
