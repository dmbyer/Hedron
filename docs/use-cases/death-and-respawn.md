# Use Case: Death and Respawn

**Status:** planned
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

## Implementation plan — work packages

> **Sub-agent execution.** **WP-1 lands first** (the death system, seam, components, config, and the HP-floor change — everything with no event/handler wiring). **WP-2 and WP-3 depend only on WP-1, not on each other**, so they can run in parallel. The **primary agent runs `architecture-reviewer` (code mode) across the combined diff** after all three land — sub-agents do not self-review.

### WP-1 — Death system, components, config, HP-floor seam *(no event wiring)*
- **Scope:** the domain decision surface and its data, with no handlers/events yet.
- **Files:** `RespawnComponent.cs` (`Core/ECS/Components/`, `[Persistent]`); `IDeathSystem.cs`/`DeathSystem.cs` (`Core/Modules/Death/Systems/` — `OnHpChanged`, `Respawn`, `SetRespawn`, `DeathTransition` enum); `DeathOptions.cs` (`Death:` section bind) + `appsettings.json` keys (`Death:HpFloor=-10`, `Death:BleedPerTick=1`, `Death:RespawnPoolPercent=0.25`); `IEffectSystem`/`EffectSystem` add `RemoveImpermanent`; `IAttributeSystem`/`AttributeSystem` change `SetCurrentHp` lower clamp from `0` to `Death:HpFloor`; `AccountSystem.CreateCharacterAsync` attaches `RespawnComponent`; `CombatSystem.ExecuteRound` removes the clamp-to-1 stub; `DeathModule.cs` (registration only).
- **Depends on:** nothing (lands first).
- **Out of scope:** all event publishing, handlers, the dispatcher gate, admin command.
- **Exit (testable):** solution builds; `IDeathSystem.OnHpChanged` returns `BecameIncapacitated`/`Died`/`None` correctly; `SetCurrentHp` floors at `-10`; a freshly-created character has `RespawnComponent`; `RemoveImpermanent` strips timed effects and keeps `UntilRemoved`.

### WP-2 — Pipeline wiring: incapacitation, bleed, death, respawn *(depends on WP-1)*
- **Scope:** the heartbeat-driven bleed pulse and the death→respawn handlers + their events; combat/effect handlers call `OnHpChanged`.
- **Files:** events (`PlayerIncapacitatedEvent`, `PlayerBleedingEvent`, `PlayerDiedEvent`, `PlayerRespawnedEvent`); `DeathTickHandler.cs`, `PlayerDeathHandler.cs`, `DeathNarrationHandler.cs`; `CombatTickHandler.cs` + `EffectTickHandler.cs` call `IDeathSystem.OnHpChanged` and publish `PlayerIncapacitatedEvent` on the incap transition (combat's clamp-to-1 path replaced); `MobDiedEvent.cs` extended with `KillerEntityId`; `CombatMobDeathHandler.cs` passes the killer; `Program.cs` subscriptions; `flow-18`/`flow-21` updated for the `OnHpChanged` call; `docs/architecture/03-events.md` "Worked example: Player death" (~L264) corrected — this slice ships the real `PlayerDiedEvent`/`PlayerDeathHandler`, so the stale save-on-change description is replaced with the periodic-flush model (INV-22).
- **Depends on:** WP-1.
- **Out of scope:** the dispatcher gate and admin command (WP-3).
- **Exit (testable):** a player driven to 0 HP by combat *or* poison becomes incapacitated; bleeds 1/tick with a message each tick; dies at -10; respawns at the stored room with pools at 25% and timed effects cleared while permanent effects remain; `MobDiedEvent` carries the killer id.

### WP-3 — Command-block gate + admin respawn tooling *(depends on WP-1)*
- **Scope:** the dispatcher incapacitation gate and the respawn authoring/inspection surface.
- **Files:** `ICommand.cs` (+ `UsableWhileIncapacitated`, default `false` via a base/default-interface or each command); `CommandDispatcher.cs` (incapacitation gate + `CommandOutcome.Refused`); flag the allowlist commands (`quit`, `help`, `commands`, `score`) `true`; `SetRespawnCommand.cs` + `PlayerRespawnSetByAdminEvent.cs`; `AdminAuditHandler.cs` (+ new event); `ScoreDisplayMessage`/`ScoreCommand` show the respawn room (inspection surface, INV-18); reference-catalog sweep (`components.md`, `systems.md`, `handlers.md`, `commands.md`, `events` mentions) **owned by WP-3** across all three packages; `.claude/skills/add-command/SKILL.md` §Persistence (INV-20) — the "admin boundary save" subsection (with `setrespawn` as the worked example) was added when INV-22 was reworded alongside this slice; WP-3 only verifies it still matches the shipped `SetRespawnCommand`.
- **Depends on:** WP-1 (needs `IEntityStateService.IsInState` — already exists — and the `Incapacitated` flag set by WP-2 at runtime, but not WP-2's code to compile).
- **Out of scope:** the bleed/death pipeline (WP-2).
- **Exit (testable):** an incapacitated player's `north` is refused with a message while `score`/`quit` still work; `setrespawn <player> room.x` validates the blueprint, persists, and `score` shows the new respawn room; `flow-03` updated for the gate.

---

## Content tooling impact

- **Admin authoring:** `setrespawn <player> <roomBlueprintId>` sets a player's respawn location at runtime (validated against `ITemplateRegistry`), routed through `IDeathSystem.SetRespawn` (the *system* owns the logic; the command is a thin caller, so the future content editor reuses it — editor-forward, per the `setplayer`/`setmob` precedent).
- **Inspection:** `score` is extended to display the current respawn room blueprint id (and the incapacitated/bleeding state when active) so a designer can verify both new pieces of state in the same PR (INV-18). The bleed state is also self-evident from the per-tick `PlayerBleedingEvent` message.
- **Balance config:** `Death:HpFloor`, `Death:BleedPerTick`, `Death:RespawnPoolPercent` are tunable in `appsettings.json` (see Cross-cutting → Configuration for the category justification).
- No new YAML template kind or `TemplateRegistry` entry — death/respawn state lives on player (persistent) entities, authored via creation defaults + admin command, not via content files.

---

## Cross-cutting surfaces stressed

- **Commands — Gap exposed (framework lands in this slice, WP-3).** The requirement "an incapacitated player can execute *no* commands" cannot be met by the existing per-command `RequiredPrivileges` opt-in: that would force every current and future command to declare an incapacitation requirement, a pattern repeated ≥3× and silently broken by any new command that forgets it (INV-19). The correct shape is a **dispatcher-level state gate** + a single `ICommand.UsableWhileIncapacitated` opt-*out* flag (default-deny). This is a small, structural addition to `CommandDispatcher` and `ICommand`, landed in the same slice. **Disposition: framework slice lands alongside (WP-3), not deferred.** Resolution before merge.
- **Event bus — Adequate.** Four new past-tense events + one extended event; all published by Initiators/Handlers (tick handlers, death handler, admin command), never by systems (INV-5). Tick-frequency publication of `PlayerBleedingEvent` mirrors the established `CombatRoundEvent`/`EffectExpiredEvent` per-tick pattern — no new infrastructure.
- **Time / heartbeat — Adequate.** `DeathTickHandler` is a third `HeartbeatTickEvent` subscriber alongside `CombatTickHandler` and `EffectTickHandler`. Ordering note: bleed (this handler) and DoT effects (`EffectTickHandler`) both reduce HP on the same tick; both route their HP mutation through `IDeathSystem.OnHpChanged`, so whichever runs first that drives HP to the floor publishes `PlayerDiedEvent` and the other observes `Incapacitated` already cleared — idempotent. No ordering dependency introduced (both priority 20; death-vs-effect order is not load-bearing because the threshold check is monotonic).
- **ECS queries — Adequate.** Bleed pulse queries `EntityStateComponent` for the `Incapacitated` flag (same snapshot-before-iterate pattern as `CombatStateComponent` in combat). No new query infrastructure.
- **Configuration — Adequate.** `Death:HpFloor` / `Death:BleedPerTick` / `Death:RespawnPoolPercent` are surfaced via `IConfiguration` (`DeathOptions`) per [`../architecture/05-configuration.md`](../architecture/05-configuration.md). These are **Category-3 balance constants surfaced as settings** (the same OD-2 "tune without recompile" trigger that `CharacterDefaults:` used in 9-d) — chosen over hardcoded constants specifically because the user requires the pool-restore percent to be appsettings-configurable. End-state promotion to a content definition tracks with the content editor (backlog).
- **Output / broadcast — Adequate.** Death/respawn/bleed messages use existing `IBroadcastSystem` (room fan-out + single-player writes) and typed `PlainMessage`/existing message shapes; the `score` respawn-room line extends `ScoreDisplayMessage` in place. No new output infrastructure.
- **Sessions — Adequate.** The dispatcher gate reads `session.PlayerEntityId` (already available); no session-model change.
- **Persistence — see the dedicated audit below. Gap: none; one classification confirmation required.**

### Persistence opt-in audit (INV-22 / INV-23)

**Level 1 — entity domain classification.**
- The slice introduces no new entity construction path. It adds a component to the **player (persistent)** domain entity at creation (`AccountSystem.CreateCharacterAsync`, which already adds `PersistentEntity`). No world-content entity is touched. No domain transition occurs (a player never changes persistence domain on death — it respawns as the same persistent entity).
- Mob death (Flow D-5) destroys a **world-content** entity (no `PersistentEntity`); `DestroyEntity` is the single exit point and already auto-cleans (no SQLite row exists). Unchanged.

**Level 2 — component inclusion.**
- `RespawnComponent` → **`[Persistent]`.** It holds player state (respawn location) that must survive a restart. Stores `RoomBlueprintId` (stable cross-restart), not the runtime `RoomEntityId`, mirroring `LocationComponent`'s persistence split. Confirmed correct.
- `EntityStateComponent` (the `Incapacitated` flag holder) → **omit `[Persistent]` (unchanged, slice 9-a).** Transient by design: a crash mid-incapacitation reconnects the player with last-flushed HP and no flag. If last-flushed HP was ≤ 0 the player reconnects "wounded but conscious"; the next damage re-enters the pipeline. Acceptable MUD convention; matches the slice 9-a decision. No change.
- `PoolsComponent`, `LocationComponent`, `EffectsComponent` → already `[Persistent]` (correct); the slice writes them via existing seams. The respawn pool/location/effect mutations ride the existing persistence of these components. No `[Persistent]` correction needed.

**Level 3 — save-on-change scope.**
- The death→respawn pipeline (Flow D-4) performs **no `SaveEntityAsync`** — the location/pool/effect mutations are runtime state changes covered by the periodic flush (INV-22). Confirmed compliant.
- `SetRespawnCommand` (Flow D-6) calls `SaveEntityAsync` — this is the **admin boundary save** category named in INV-22 (admin-gated mutation via a domain system → `SaveEntityAsync` → publish audit event), consistent with the existing `setplayer` precedent. **Disposition: resolved — keep the explicit save.** An admin mutation lands durably without waiting for the periodic flush. (See Resolved decisions #1.)

---

## Flows introduced or modified

**New canonical flows (add to `flows/README.md`):**
- **Flow 22 — Player incapacitation and bleed-out** (`flow-22-incapacitation-bleedout.md`): HP→0 entry from combat/effects, the dispatcher command-block, and the heartbeat bleed pulse.
- **Flow 23 — Player death and respawn** (`flow-23-player-death-respawn.md`): `PlayerDiedEvent` → `IDeathSystem.Respawn` → relocation, effect expiry, pool restore, narration.

**Modified canonical flows:**
- **Flow 03 — Player command lifecycle** (`flow-03-player-command-lifecycle.md`): add the incapacitation gate between verb resolution and the privilege gate; add `CommandOutcome.Refused`.
- **Flow 18 — Combat round pulse**: the `PlayerIncapacitated` branch no longer clamps HP to 1; it applies real damage and calls `IDeathSystem.OnHpChanged`, publishing `PlayerIncapacitatedEvent`.
- **Flow 21 — Effect tick**: a DoT tick that drives a player to 0 HP now calls `IDeathSystem.OnHpChanged` and publishes `PlayerIncapacitatedEvent` (the effect tick becomes a death-pipeline entry point).
- **Flow 20 — Mob death and respawn**: `MobDiedEvent` payload gains `KillerEntityId` (note only; spawn-slot logic unchanged).

The implementation PR must update `flows/README.md` (new rows 22–23) and the four modified flow files. Drift is a merge blocker.

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
- **On ship:** author `architecture/subsystems/death.md` (the living design of the incapacitation/death/respawn system) and trim this doc to the durable behavior spec per the docs lifecycle.

---

## Resolved decisions

*All four design forks were settled with the owner before spec review; recorded here so they are not re-litigated.*

1. **Admin respawn-set save (INV-22 boundary). → Keep the explicit `SaveEntityAsync`.** `SetRespawnCommand` performs an **admin boundary save** — the named INV-22 category (mirroring `setplayer`): an admin-gated mutation lands durably without waiting for the next periodic flush. INV-22 was reworded alongside this slice to name this category explicitly rather than treat it as a strict-reading exception.
2. **Bleed notification cadence. → Notify every tick.** `PlayerBleedingEvent` fires on every heartbeat tick the player is incapacitated (no throttling). The urgency is intentional; revisit only if play-testing shows it's noisy.
3. **Allowlist of usable-while-incapacitated commands. → Minimal.** Ship `help`, `commands`, `score` flagged `UsableWhileIncapacitated = true`; nothing else. (Disconnect is transport-level, not a command verb, so it needs no flag — confirmed in spec review: there is no `quit` command in `Core/`.) The default-deny opt-out flag makes adding a future `pray`/`yell` call-for-help a one-line change when that command is authored.
4. **`KillerEntityId` lifetime / killer-name snapshot. → Defer to the reward slice.** This slice ships only the bare `KillerEntityId` field on `PlayerDiedEvent`/`MobDiedEvent`. No killer-*name* snapshot is captured now; the future `RewardSystem` owns its own snapshot needs (and the killer-destroyed-before-reward edge case).

---

## Related

- [`combat.md`](combat.md) — slice 9; the `PlayerIncapacitated` clamp-to-1 stub this slice replaces; `MobDiedEvent` and `CombatMobDeathHandler` extended here; the death seam was explicitly deferred to "slice 10" in the combat spec.
- [`effect-substrate.md`](effect-substrate.md) — slice 9-e; `EffectsComponent`, `EffectLifetime`, and the `IEffectSystem` extended with `RemoveImpermanent`; DoT ticks become a death-pipeline entry point.
- [`entity-state-management.md`](entity-state-management.md) — slice 9-a; the `Incapacitated` flag (already defined, no entry-block rule) and `IEntityStateService` gate this slice's command-block and bleed query.
- [`stat-resource-substrate.md`](stat-resource-substrate.md) — slice 9-d; the four pools and `IAttributeSystem` setters used for the 25% restore; the `SetCurrentHp` clamp this slice lowers; the `CharacterDefaults:`/config precedent for `Death:` keys.
- [`time-system.md`](time-system.md) — slice 9-b; `HeartbeatTickEvent` drives the bleed pulse.
- [`command-framework.md`](command-framework.md) — slice 3; the dispatcher and `ICommand` extended with the incapacitation gate and `UsableWhileIncapacitated`.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) / [`persistence-reform.md`](persistence-reform.md) — the two-level model `RespawnComponent` opts into; the `RoomBlueprintId` cross-restart pattern.
- [`account-character-creation.md`](account-character-creation.md) — slice 5; `CreateCharacterAsync` extended to attach `RespawnComponent`.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
