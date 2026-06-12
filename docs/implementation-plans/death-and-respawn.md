# Use Case: Death and Respawn

**Status:** implemented
**Actors:** Player, Mob, System, Administrator
**Module:** `Core/Modules/Death/` (new — `IDeathSystem`, incapacitation/bleed/respawn flow, tick handler, death handlers, admin commands), `Core/ECS/Components/` (`RespawnComponent` — cross-cutting persistent), `Core/Modules/EntityState/` (`Incapacitated` flag — reused), `Core/Modules/Combat/` (HP-zero stub becomes a real seam), `Core/Modules/Mobs/` (`MobDiedEvent` extended with killer), `Core/Commands/` (dispatcher-level incapacitation gate), `Core/Modules/Time/` (heartbeat consumer)

**Slice:** Phase 3 slice 10 — death and respawn. The first terminal player outcome; turns combat (slice 9) and DoT effects (slice 9-e) from "clamped to 1 HP" stubs into real incapacitation → bleed-out → death → respawn.

---

## Description

Introduce the player **incapacitation → bleed-out → death → respawn** lifecycle and the **mob death reward seam**. The trigger is HP-pool-driven, not command-driven: any path that drives a player's `CurrentHp` to 0 (a combat round, a `poison` DoT tick, an admin `setplayer hp 0`) puts the player into the `Incapacitated` state (slice 9-a flag, already defined). While incapacitated the player can issue no commands (a dispatcher-level state gate refuses them) and bleeds 1 HP per heartbeat tick, notified each tick. At **-10 HP** the player dies: respawns at a per-player stored, runtime-adjustable, persisted respawn location; state is reset; impermanent effects expire while permanent ones persist; and all four pools (HP/Mana/Stamina/Astra) are set to a configurable percentage (default 25%) of their maxima.

Mob death already destroys the entity and publishes `MobDiedEvent` (slice 9 / persistence-reform Stage C). This slice **extends `MobDiedEvent` with the killer entity id** so a future `RewardSystem` (XP/loot, out of scope) can subscribe — the seam exists now; the reward logic does not. No reward, loot, corpse, or XP is granted in this slice.

**Design scope.** Single-pool death threshold (HP only). No corpse entity, no item-loss penalty, no experience loss, no resurrection mechanic, no death-by-environment beyond the HP seam. Respawn location is a single room reference per player (no bind-point list, no graveyard graph). The 25% pool-restore is a flat fraction of max for every pool; per-pool fractions are deferred.

**Prerequisites:** Slices 1–9 complete. Slice 9-a (entity state, `Incapacitated` flag), 9-b (heartbeat), 9-d (four pools, `IAttributeSystem` pool setters), and 9-e (effects, `EffectsComponent`, `IEffectSystem`, `EffectLifetime`) complete.

---

## Preconditions

- Slices 1–9, 9-a, 9-b, 9-c, 9-d, 9-e complete. Reused surfaces: `EntityService`, `IEventBus`, `IBroadcastSystem`, `IPersistenceSystem`, `ICommandDispatcher`, `IOutputWriter`, `IAdminAuthorizer`, `AdminRequirement`, `IAuthorizationChecker`, `LocationComponent`, `BlueprintComponent`, `PoolsComponent`, `CharacterComponent`, `WorldConfiguration`.
- **Slice 9-a complete:** `IEntityStateService` (`TryEnterState`, `ExitState`, `IsInState`, `GetStates`); `EntityStateFlags { None, InCombat, Resting, Incapacitated }` — the `Incapacitated` flag already exists and has *no* entry-block rule.
- **Slice 9-b complete:** `HeartbeatTickEvent { TickId, Timestamp, Elapsed }`; `Heartbeat:IntervalMs` (default 2000).
- **Slice 9-d complete:** `IAttributeSystem.SetCurrentHp/Mana/Stamina/Astra` and `GetMaxHp/Mana/Stamina/Astra`, all with `[0, Max]` clamp; `PoolsComponent` carries all four pools.
- **Slice 9-e complete:** `EffectsComponent { List<Effect> }` (`[Persistent]`); `IEffectSystem` (`GetActive`, `Remove`, `RemoveByCategory`); `EffectLifetime` enum (`Instant`, `Timed`, `UntilRemoved`, `WhileEquipped`, `WhileKnown`, `WhilePresent`) distinguishing impermanent (timed/source-bound) from permanent (`UntilRemoved`) effects.
- **Combat (slice 9):** `CombatTickHandler` currently clamps player HP to 1 on `PlayerIncapacitated` (the stub this slice replaces). `MobDiedEvent { uint MobEntityId, string BlueprintId }` exists and is published by `CombatMobDeathHandler`.
- `IAttributeSystem.SetCurrentHp`'s clamp floor must change from `0` to allow a negative floor (`-10`) for player HP — see Postconditions.

---

## Postconditions

**HP floor seam (combat/effects no longer clamp to 1)**
- `IAttributeSystem.SetCurrentHp` lower clamp changes from `0` to a configured death floor (`Death:HpFloor`, default `-10`) so bleed-out and overkill can drive HP below zero. `CombatSystem.ExecuteRound`'s `PlayerIncapacitated` stub (clamp to 1) is removed; combat applies real damage and lets the death pipeline observe the HP-zero crossing. `MaxHp` upper clamp unchanged.

**Incapacitation (HP reaches 0)**
- A single seam — `IDeathSystem.OnHpChanged(uint entityId, int previousHp, int newHp)` (domain, `Core/Modules/Death/Systems/`) — is the authoritative HP-threshold evaluator. It is **called by the Initiator/Handler that mutated HP** (combat tick handler, effect tick handler), never by `IAttributeSystem` (INV-5: a core/domain compute seam must not chain into another domain decision). It returns a `DeathTransition` result enum (`None`, `BecameIncapacitated`, `Died`) so the caller publishes the right event. The system mutates state flags and pools and reads/writes effects; it never touches the event bus or persistence (INV-5, INV-8).
- On a `0`-or-below crossing for a player (`CharacterComponent` present) not already incapacitated: `IDeathSystem` calls `IEntityStateService.TryEnterState(entityId, Incapacitated)`. Returns `BecameIncapacitated`.

**Command blocking while incapacitated (dispatcher-level gate)**
- `CommandDispatcher.DispatchAsync` gains an **incapacitation gate** that runs after verb resolution and before the privilege gate: if `IEntityStateService.IsInState(session.PlayerEntityId, Incapacitated)` is true and the resolved command is not flagged `UsableWhileIncapacitated`, the dispatcher writes "You are incapacitated and cannot do that." and returns (`CommandOutcome.Refused`). `ICommand` gains `bool UsableWhileIncapacitated { get; }` (default `false`). This is the framework addition that makes the block apply to **every** command without each command opting in — see Cross-cutting surfaces. A small allowlist of always-usable commands (`help`, `commands`, `score`) sets the flag `true`. (Disconnect is handled at the transport/session layer, not via a command verb, so no `quit` flag is needed — an incapacitated player can always disconnect.)

**Bleed-out (heartbeat-driven)**
- A `DeathTickHandler` (priority 20, `Core/Modules/Death/Handlers/`, subscribes to `HeartbeatTickEvent`) snapshots all entities with `EntityStateComponent` carrying `Incapacitated`, and for each: reads `CurrentHp`, computes `newHp = currentHp - Death:BleedPerTick` (default 1), calls `IAttributeSystem.SetCurrentHp`, then calls `IDeathSystem.OnHpChanged`. On `DeathTransition.None` it publishes a per-tick `PlayerBleedingEvent` (the bleed notification). On `DeathTransition.Died` it publishes `PlayerDiedEvent`. Orchestration only (INV-1); the handler is an Initiator-class publisher (handlers may publish — INV-5 restricts systems).

**Death and respawn (HP reaches the floor)**
- When `OnHpChanged` observes `newHp <= Death:HpFloor` (default `-10`) for an incapacitated player, it returns `DeathTransition.Died`. The actual respawn mutation is performed by `IDeathSystem.Respawn(uint entityId)`, called by the `PlayerDeathHandler` (priority 20, subscribes to `PlayerDiedEvent`):
  - `IEntityStateService.ExitState(entityId, Incapacitated)` (and any other lingering flags — combat already ended at incap).
  - Reads `RespawnComponent.RoomBlueprintId`; resolves it to a live `RoomEntityId` via the blueprint→entity map (same resolution `CharacterHydrationHandler` uses); falls back to `WorldConfiguration.StartingRoomBlueprintId` if unresolved (logs warning). Sets `LocationComponent.RoomEntityId` + `RoomBlueprintId`.
  - `IEffectSystem` expires all impermanent effects on the entity: removes every effect whose `Lifetime != UntilRemoved` (timed and source-bound), leaving `UntilRemoved` effects intact. Exposed as `IEffectSystem.RemoveImpermanent(uint entityId)` (new method on the existing system).
  - Sets each pool to `floor(Max * Death:RespawnPoolPercent)` (default `0.25`) via `IAttributeSystem.SetCurrentHp/Mana/Stamina/Astra`.
- `PlayerDeathHandler` then publishes `PlayerRespawnedEvent`. A separate `DeathNarrationHandler` (priority 80, subscribes to `PlayerDiedEvent` and `PlayerRespawnedEvent`) does output fan-out: death broadcast to the death room, respawn confirmation to the player, arrival broadcast to the respawn room. No system mutates the event bus (INV-5).

**Respawn location (persistent, adjustable)**
- `RespawnComponent { RoomBlueprintId : string? }` exists at `Core/ECS/Components/`, `[Persistent]`. Carried by player (character) entities only. Stores the *blueprint id* (cross-restart stable, mirroring `LocationComponent`'s persistence model), never the runtime `RoomEntityId`.
- Set at character creation: `AccountSystem.CreateCharacterAsync` attaches `RespawnComponent { RoomBlueprintId = WorldConfiguration.StartingRoomBlueprintId }`.
- Runtime-adjustable by admin: `setrespawn <player> <roomBlueprintId>` (admin command) calls `IDeathSystem.SetRespawn(entityId, roomBlueprintId)` (validates the blueprint exists in `ITemplateRegistry`), then the command **explicitly calls `IPersistenceSystem.SaveEntityAsync`** so the admin mutation lands durably without waiting for the periodic flush. This is the **admin boundary save** category named in INV-22 (mutate via system → `SaveEntityAsync` → publish audit event), consistent with the existing `setplayer` path. (See Cross-cutting → Persistence for the INV-22 audit and Resolved decisions #1.) Player-facing self-set is deferred (no `bind` command this slice).

**Mob death reward seam (stub)**
- `MobDiedEvent` is extended to `MobDiedEvent { uint MobEntityId, string BlueprintId, uint KillerEntityId }`. `CombatMobDeathHandler` already has the killer (`@event.AttackerEntityId`); it passes it through. `KillerEntityId == 0` is the "no attributable killer" sentinel (environmental/admin kill). No subscriber consumes `KillerEntityId` in this slice — the seam is established for a future `RewardSystem`.

**Module**
- `DeathModule` (`Core/Modules/Death/DeathModule.cs`) exposes `AddDeathModule(IServiceCollection)`, registers `IDeathSystem`, `DeathTickHandler`, `PlayerDeathHandler`, `DeathNarrationHandler`, `SetRespawnCommand`; called from `Server/Program.cs`. Handler event subscriptions wired in `Program.cs` alongside the existing combat/effect handler wiring.
- `AdminAuditHandler` extended to log `PlayerRespawnSetByAdminEvent`.

---

## Main Flow

### Flow D-1 — Incapacitation (HP reaches 0 from any source)

1. An Initiator mutates a player's HP to 0: `CombatTickHandler` applies a killing-blow round, or `EffectTickHandler` applies a `poison` DoT tick. (Combat no longer clamps to 1 — the slice-9 stub is removed.)
2. The mutating handler calls `IDeathSystem.OnHpChanged(playerEntityId, previousHp, newHp)`.
3. `OnHpChanged` sees `newHp <= 0`, `previousHp > 0`, target has `CharacterComponent`, not already `Incapacitated` → calls `IEntityStateService.TryEnterState(playerEntityId, Incapacitated)`; returns `DeathTransition.BecameIncapacitated`.
4. The mutating handler publishes `PlayerIncapacitatedEvent(playerEntityId, RoomEntityId)`. (Combat's existing `CombatEndedEvent(PlayerIncapacitated)` still fires for combat-state cleanup; the new event is the death-pipeline entry so non-combat sources reach the same path.)
5. `DeathNarrationHandler` (priority 80) broadcasts "<Player> collapses, mortally wounded!" to the room and writes "You collapse, bleeding out. You cannot act — find healing fast." to the player.

### Flow D-2 — Command refusal while incapacitated

1. The incapacitated player sends any command (e.g. `north`). `CommandDispatcher.DispatchAsync` resolves the verb.
2. **Incapacitation gate** (after verb resolution, before privilege gate): `IEntityStateService.IsInState(playerEntityId, Incapacitated)` is `true` and `command.UsableWhileIncapacitated == false`.
3. The dispatcher writes "You are incapacitated and cannot do that." (`OutputSeverity.Error`), publishes `CommandExecutedEvent(..., CommandOutcome.Refused)`, and returns without executing the command.
4. Allowlisted commands (`help`, `commands`, `score`, flagged `UsableWhileIncapacitated = true`) pass the gate and run normally. (Disconnect is transport-level, not a command, so it is unaffected by the gate.)

### Flow D-3 — Bleed-out pulse (heartbeat-driven)

1. `HeartbeatBackgroundService` publishes `HeartbeatTickEvent`. `DeathTickHandler` (priority 20) handles it.
2. Snapshots all entities whose `EntityStateComponent.ActiveStates` includes `Incapacitated` (snapshot-before-iterate).
3. For each: reads `CurrentHp`, computes `newHp = CurrentHp - Death:BleedPerTick`, calls `IAttributeSystem.SetCurrentHp(entityId, newHp)`, then `IDeathSystem.OnHpChanged(entityId, previousHp, newHp)`.
4. On `DeathTransition.None`: publishes `PlayerBleedingEvent(entityId, newHp, Death:HpFloor)`. `DeathNarrationHandler` writes "You are bleeding out (<hp>/<floor>). Without healing you will die." to the player.
5. On `DeathTransition.Died` (`newHp <= Death:HpFloor`): publishes `PlayerDiedEvent(entityId, deathRoomEntityId, KillerEntityId)`. Proceeds to Flow D-4.

### Flow D-4 — Death and respawn

1. `PlayerDeathHandler` (priority 20) handles `PlayerDiedEvent`. Calls `IDeathSystem.Respawn(entityId)`:
   a. `ExitState(entityId, Incapacitated)`.
   b. Resolves `RespawnComponent.RoomBlueprintId` → live `RoomEntityId` (fallback to `StartingRoomBlueprintId`); sets `LocationComponent`.
   c. `IEffectSystem.RemoveImpermanent(entityId)` — drops all non-`UntilRemoved` effects; permanent effects persist.
   d. Sets all four pools to `floor(Max * Death:RespawnPoolPercent)`.
2. `PlayerDeathHandler` publishes `PlayerRespawnedEvent(entityId, respawnRoomEntityId)`.
3. `DeathNarrationHandler` (priority 80) broadcasts the death message to the death room (using the room id captured in `PlayerDiedEvent`, before relocation), writes "You awaken, weak but alive, at <room name>." to the player, and broadcasts the arrival to the respawn room.
4. Durability: the respawn mutation (location, pools, effects) is covered by the **periodic flush** (INV-22 — no save-on-change for a runtime state transition). A crash in the seconds between respawn and flush re-runs the player from the last flush (acceptable MUD convention).

### Flow D-5 — Mob death reward seam (stub)

1. `CombatMobDeathHandler` (priority 80, unchanged path) finalizes a mob death and publishes the now-extended `MobDiedEvent(mobEntityId, blueprintId, killerEntityId = @event.AttackerEntityId)`.
2. `SpawnSystem` (existing subscriber) consumes `MobEntityId`/`BlueprintId` for slot vacancy — unaffected by the added field.
3. No subscriber reads `KillerEntityId` this slice. The field is the documented hook a future `RewardSystem` (XP/loot) subscribes to.

### Flow D-6 — Admin sets respawn location

1. Admin sends `setrespawn <player> <roomBlueprintId>`. `SetRespawnCommand` (admin-gated) resolves the target player.
2. Calls `IDeathSystem.SetRespawn(playerEntityId, roomBlueprintId)` — validates the blueprint via `ITemplateRegistry.TryGet`; on miss, returns a failure reason the command surfaces. On success, mutates `RespawnComponent.RoomBlueprintId`.
3. Command calls `IPersistenceSystem.SaveEntityAsync(playerEntityId)` (admin boundary save, mirroring `setplayer`), publishes `PlayerRespawnSetByAdminEvent(adminId, playerEntityId, roomBlueprintId)`.
4. `AdminAuditHandler` logs it.

---

## Events Fired

| Event | Publisher | Payload | Purpose |
|---|---|---|---|
| `PlayerIncapacitatedEvent` | `CombatTickHandler`, `EffectTickHandler` | `uint PlayerEntityId, uint RoomEntityId` | death-pipeline entry; narration; future AI/observers |
| `PlayerBleedingEvent` | `DeathTickHandler` | `uint PlayerEntityId, int CurrentHp, int HpFloor` | per-tick bleed notification |
| `PlayerDiedEvent` | `DeathTickHandler` | `uint PlayerEntityId, uint DeathRoomEntityId, uint KillerEntityId` | triggers respawn + narration; `KillerEntityId` future-hook for death-cause attribution |
| `PlayerRespawnedEvent` | `PlayerDeathHandler` | `uint PlayerEntityId, uint RespawnRoomEntityId` | respawn narration; future observers |
| `MobDiedEvent` (extended) | `CombatMobDeathHandler` | `uint MobEntityId, string BlueprintId, uint KillerEntityId` | spawn-slot vacancy (existing); **reward seam (new `KillerEntityId`)** |
| `PlayerRespawnSetByAdminEvent` | `SetRespawnCommand` | `uint AdminEntityId, uint PlayerEntityId, string RoomBlueprintId` | audit |

`IDeathSystem`, `IAttributeSystem`, `IEffectSystem`, `IEntityStateService` never publish (INV-5).

---

## Design Notes

- **HP threshold, not a death command.** Per requirement 2, incapacitation and death key off the HP pool crossing a threshold, evaluated by `IDeathSystem.OnHpChanged` and called by whichever Initiator mutated HP. This is why combat *and* effect ticks both reach the same pipeline with no duplicated death logic — the single decision seam is the anti-duplication mechanism (INV-19). `IAttributeSystem` deliberately does **not** call `OnHpChanged` itself: a core/domain compute seam chaining into a domain decision would violate the layer discipline and create a hidden side-effect on every HP write (combat would re-enter death logic mid-round). The caller owns the threshold call.
- **Dispatcher gate is default-deny.** `UsableWhileIncapacitated` defaults `false` so a newly added command is blocked-while-incapacitated by default and the author opts out explicitly. This is the inverse of the privilege model (default-public) and is the correct default for an incapacitation: forgetting the flag fails safe (a command is blocked, not accidentally allowed). The gate lives in the dispatcher, not in `IAuthorizationChecker`, because incapacitation is a *transient entity state*, not a *privilege* — folding it into `RequiredPrivileges` would conflate two orthogonal axes and break the `help`/`commands` visibility filter (which must still show usable-when-healthy commands).
- **Respawn stores blueprint id, not entity id.** `RespawnComponent.RoomBlueprintId` mirrors `LocationComponent`'s cross-restart model: the runtime `RoomEntityId` is not stable across restarts (world content re-spawns fresh), so the durable reference is the blueprint id, resolved to a live entity at respawn time. Reuses the resolution logic pattern from `CharacterHydrationHandler`.
- **Impermanent vs. permanent effects.** `RemoveImpermanent` keys off the existing `EffectLifetime` enum (9-e): `UntilRemoved` is "permanent" and survives death; everything else (`Timed`, source-bound) is impermanent and expires. This reuses the same lifetime axis that already governs effect persistence (9-e's `JsonConverter` persists only `UntilRemoved`) — death-expiry and persistence-inclusion share one definition of "permanent," so they cannot drift.
- **Mob death reward seam is a single field.** Adding `KillerEntityId` to `MobDiedEvent` is the minimum seam that lets a future `RewardSystem` attribute XP/loot to the killer. No `RewardSystem`, loot table, corpse, or XP component ships now — the use case explicitly scopes reward logic out. `KillerEntityId == 0` is the no-attributable-killer sentinel (admin/environmental death) so the future system has a defined null case.
- **25% pool restore is a flat fraction.** Every pool is set to `floor(Max * RespawnPoolPercent)`. Per-pool fractions (e.g. respawn with full Stamina but quarter Mana) are deferred — the single config key is the simplest shape that satisfies the requirement and is trivially generalized later.
- **No corpse, no item loss, no XP loss.** Death penalty beyond pool-restoration is out of scope. The respawn is "soft" (relocate + restore) — the substrate a future harsher-death or corpse-retrieval slice builds on. The `PlayerDiedEvent` payload carries the death room id so a future corpse-spawn handler has the location without re-deriving it.
- **`SetCurrentHp` floor change is shared.** Lowering the clamp floor to `Death:HpFloor` affects every `SetCurrentHp` caller. This is intentional: overkill (a 50-damage blow at 5 HP) should land the player at a negative value the bleed-out reads, not be silently clamped to 0/1. Mobs reach 0 and die immediately (handled by combat's `MobDied` outcome before any negative value matters), so the negative floor is a player-only concern in practice.

---

## Related

- [`combat.md`](combat.md) — slice 9; the `PlayerIncapacitated` clamp-to-1 stub this slice replaces; `MobDiedEvent` and `CombatMobDeathHandler` extended here; the death seam was explicitly deferred to "slice 10" in the combat spec.
- [`effect-substrate.md`](../features/effects/effects.md) — slice 9-e; `EffectsComponent`, `EffectLifetime`, and the `IEffectSystem` extended with `RemoveImpermanent`; DoT ticks become a death-pipeline entry point.
- [`entity-state-management.md`](entity-state-management.md) — slice 9-a; the `Incapacitated` flag (already defined, no entry-block rule) and `IEntityStateService` gate this slice's command-block and bleed query.
- [`stat-resource-substrate.md`](stat-resource-substrate.md) — slice 9-d; the four pools and `IAttributeSystem` setters used for the 25% restore; the `SetCurrentHp` clamp this slice lowers; the `CharacterDefaults:`/config precedent for `Death:` keys.
- [`time-system.md`](time-system.md) — slice 9-b; `HeartbeatTickEvent` drives the bleed pulse.
- [`command-framework.md`](command-framework.md) — slice 3; the dispatcher and `ICommand` extended with the incapacitation gate and `UsableWhileIncapacitated`.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) / [`persistence-reform.md`](persistence-reform.md) — the two-level model `RespawnComponent` opts into; the `RoomBlueprintId` cross-restart pattern.
- [`account-character-creation.md`](account-character-creation.md) — slice 5; `CreateCharacterAsync` extended to attach `RespawnComponent`.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
