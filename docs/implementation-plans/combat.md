# Use Case: Combat

**Status:** implemented
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
