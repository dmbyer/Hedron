# Handlers Reference

Living catalog of the event handlers **implemented** in Hedron. Handlers are grouped by **cohesion**, not breadth — related events that share context live together.

> Idealized handlers for features not yet built live in [`handlers-planned.md`](handlers-planned.md) — design intent only; do not assume they exist. Why implemented and planned are separated: [`../documentation-architecture.md`](../documentation-architecture.md).

## Grouping criteria

Ask:
1. Do these events share enough context that handling them together reduces duplication?
2. Would a developer looking for this logic expect to find it with these other handlers?
3. Do these events represent variations of the same domain concept?

**Do NOT group by** "all player events" (too broad), "everything that touches inventory" (cross-cutting), or alphabetical order.

---

## Handler inventory

### PlayerSessionHandler
**Events:** `PlayerConnectedEvent`, `PlayerDisconnectedEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** On connect: attaches transient `PlayerComponent` (DisplayName, Session) and broadcasts arrival to the room; sends `SendRoomDescriptionAsync` to the connecting player. On disconnect: calls `IAccountSystem.RecordLogout`, then immediately calls `IPersistenceSystem.SaveEntityAsync(characterEntityId)` so the logout timestamp is durable, removes `PlayerComponent` via `EntityService.RemoveComponent<T>`, broadcasts departure, then calls `ISessionBufferRegistry.Release(session.SessionId)` to drop the session's output buffer entry and prevent a memory leak. The character entity is **not** destroyed on disconnect.
**Location:** `Core/Modules/Session/Handlers/PlayerSessionHandler.cs`
**Uses:** `EntityService`, `ISessionManager`, `IBroadcastSystem`, `IAccountSystem`, `IPersistenceSystem`, `ISessionBufferRegistry`

### PlayerMovedHandler
**Events:** `PlayerMovedEvent`, `PlayerTeleportedByAdminEvent` (Phase 3 slice 2)
**Responsibilities:** translate a successful move (player-initiated or admin-teleport) into the visible effects: departure broadcast on source room, arrival broadcast on destination, `look` to the moved player. Both move and teleport events funnel through the same private helper to avoid drift; teleport uses direction-agnostic flavour text.
**Uses:** `IBroadcastSystem`, `EntityService`
> As-built note: `PlayerEnterRoomEvent`/`PlayerExitRoomEvent`, movement triggers (traps/ambushes), and a dedicated `IMovementSystem`/`IVisibilitySystem` are forward-looking — the shipped handler covers the move/teleport broadcasts and `look`.

### CharacterHydrationHandler
**Events:** `WorldContentReadyEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** After world content is fully loaded, builds a `blueprintId → entityId` map from all live `BlueprintComponent`s, then iterates every persistent entity (`PersistentEntity` + `LocationComponent`). Resolves `LocationComponent.RoomBlueprintId` to the current live `RoomEntityId`; if the blueprint cannot be resolved — characters fall back to `WorldConfiguration.StartingRoomEntityId` (logs warning, saves immediately via `IPersistenceSystem.SaveEntityAsync` so the correction is durable); non-character persistent entities in an unresolvable room are destroyed. Migration guards: attaches empty `InventoryComponent`, `EquipmentComponent`, `AttributesComponent`, and `PoolsComponent` to any character entity that lacks them (for characters persisted before the slices that introduced each component). Runs once at startup; no-op if no persistent entities with `LocationComponent` exist.
**Location:** `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs`
**Uses:** `EntityService`, `WorldConfiguration`, `IPersistenceSystem`, `ILogger`

### ItemInteractionHandler
**Events:** `ItemPickedUpEvent`, `ItemDroppedEvent`
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** Pure output fan-out — no domain logic, no persistence calls. For pickup: broadcasts `"<PlayerName> picks up <ItemName>."` to all players in the room excluding the picker; writes `"You pick up <ItemName>."` to the picker only (via `SendToRoomAsync` with a filter). For drop: same pattern with drop flavour text. Player name read from `PlayerComponent.DisplayName`; item name from `ItemDataComponent.Name`. Falls back to `"Someone"` / `"something"` if either component is missing.
**Location:** `Core/Modules/Items/Handlers/ItemInteractionHandler.cs`
**Uses:** `EntityService`, `IBroadcastSystem`

### EquipmentInteractionHandler
**Events:** `ItemEquippedEvent`, `ItemUnequippedEvent`
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** Pure output fan-out — no domain logic, no persistence calls. For equip: broadcasts `"<PlayerName> wears <ItemName>."` to all players in the room excluding the wearer; writes `"You wear <ItemName>."` to the wearer only. For remove: same pattern with remove flavour text. Player name read from `PlayerComponent.DisplayName`; item name from `ItemDataComponent.Name`. Falls back to `"Someone"` / `"something"` if either component is missing. Silently no-ops if the player has no `LocationComponent`.
**Location:** `Core/Modules/Items/Handlers/EquipmentInteractionHandler.cs`
**Uses:** `EntityService`, `IBroadcastSystem`

### CombatTickHandler
**Events:** `HeartbeatTickEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** Bridge between the time system and the combat domain. On each tick: snapshots all entities with `CombatStateComponent`, deduplicates pairs (lower entity id = attacker, prevents double-processing), calls `ICombatSystem.ExecuteRound` for each pair, publishes `CombatRoundEvent`. Handles terminal outcomes inline before publishing: for `MobDied` — captures mob name from `MobDataComponent.Name`, calls `ICombatSystem.EndCombat`, publishes `CombatEndedEvent(MobDied)`; for `PlayerIncapacitated` — clamps player HP to 1, calls `ICombatSystem.EndCombat` + `IEntityStateService.ExitState(InCombat)` on both entities, publishes `CombatEndedEvent(PlayerIncapacitated)`.
**Location:** `Core/Modules/Combat/Handlers/CombatTickHandler.cs`
**Uses:** `EntityService`, `ICombatSystem`, `IEntityStateService`, `IAttributeSystem`, `IEventBus`, `ILogger<CombatTickHandler>`

### CombatHandler
**Events:** `CombatStartedEvent`, `CombatRoundEvent`, `CombatEndedEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** Pure output fan-out for all combat events. Does not call systems or mutate state. On `CombatStartedEvent`: writes "You attack \<mob\>!" to attacker; broadcasts "\<PlayerName\> attacks \<mob\>!" to other room occupants. On `CombatRoundEvent`: broadcasts hit/miss/damage narrative (first/third-person) to room. On `CombatEndedEvent(MobDied)`: broadcasts "You have slain \<mob\>!" / "\<player\> has slain \<mob\>!" using `DefenderName` from payload (captured before destruction). On `CombatEndedEvent(PlayerFled)`: writes "You flee from combat!" to player; broadcasts "\<PlayerName\> flees from combat!" to room. On `CombatEndedEvent(PlayerIncapacitated)`: writes "You have been beaten unconscious!" to player; broadcasts to room. Priority 20 ensures output runs before `CombatMobDeathHandler` (priority 80) destroys the entity.
**Location:** `Core/Modules/Combat/Handlers/CombatHandler.cs`
**Uses:** `EntityService`, `IBroadcastSystem`

### CombatMobDeathHandler
**Events:** `CombatEndedEvent` (acts only when `Outcome == MobDied`)
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** Finalizes the mob death path. Priority 80 — deliberately lower than `CombatHandler` (priority 20) so the death narrative is broadcast before entity destruction. Calls `IEntityStateService.ExitState(attackerEntityId, InCombat)`, publishes `MobDiedEvent` (while entity is still live — `SpawnSystem` observes this to mark the slot vacant), then calls `EntityService.DestroyEntity(mobEntityId)`. Loot drop deferred to slice 10.
**Location:** `Core/Modules/Combat/Handlers/CombatMobDeathHandler.cs`
**Uses:** `EntityService`, `IEntityStateService`, `IEventBus`

### ItemContextHandler
**Events:** `ItemPickedUpEvent`, `ItemDroppedEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** Manages item entity persistence lifecycle based on context. On `ItemPickedUpEvent`: calls `EntityService.AddComponent(itemEntityId, new PersistentEntity())` if not already present — the item enters the flush pool and will survive restarts in the player's inventory. On `ItemDroppedEvent`: calls `EntityService.RemoveComponent<PersistentEntity>(itemEntityId)` — the item leaves the flush pool and vanishes on restart. Does not save immediately; the periodic flush handles durability. No spawn slot knowledge — slot vacancy is handled by `SpawnSystem` separately.
**Location:** `Core/Modules/Spawn/Handlers/ItemContextHandler.cs`
**Uses:** `EntityService`

### AbilityInvocationHandler
**Events:** `AbilityActivatedEvent`
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** Renders narrative for **non-offensive** ability activations only. Offensive abilities are skipped here — `AbilityStrikeHandler` owns their narrative via `AbilityStrikeResolvedEvent`, avoiding duplicate output. On a non-offensive activation: writes `"You {abilityName} [target]."` to the actor; broadcasts `"{ActorName} {abilityName}s [target]."` to all other room occupants. Reads `AbilityDefinition.Name` from `IAbilityRegistry`. Falls back to `"someone"` if player/mob name components are absent. Silently no-ops if the actor has no `LocationComponent`.
**Location:** `Core/Modules/Abilities/Handlers/AbilityInvocationHandler.cs`
**Uses:** `IAbilitySystem`, `IAbilityRegistry`, `IBroadcastSystem`, `EntityService`

### AbilityStrikeHandler
**Events:** `AbilityStrikeResolvedEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** Renders fused combat + ability narrative for offensive ability strikes. Writes `"You {abilityName} {defender} for {damage} damage."` to the attacker; broadcasts third-person form to the room. If `CombatRoundOutcome` is terminal (`MobDied` / `PlayerIncapacitated`), also publishes `CombatEndedEvent` so `CombatHandler` and `CombatMobDeathHandler` can finalize the combat state. Does not call systems or mutate state.
**Location:** `Core/Modules/Combat/Handlers/AbilityStrikeHandler.cs`
**Uses:** `EntityService`, `IBroadcastSystem`, `IEventBus`

### AbilityCooldownTickHandler
**Events:** `HeartbeatTickEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** Bridge between the time system and the abilities domain. On each tick: calls `IAbilitySystem.AdvanceCooldowns(@event.Elapsed)` to decrement all non-zero per-ability cooldown timers. No events published; no persistence calls (INV-5, INV-8).
**Location:** `Core/Modules/Abilities/Handlers/AbilityCooldownTickHandler.cs`
**Uses:** `IAbilitySystem`

### EffectTickHandler
**Events:** `HeartbeatTickEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** Bridge between the time system and the effects domain. On each tick: calls `IEffectSystem.AdvanceTick(elapsed)` to advance all timed effects. For each `PeriodicApplication` in `DueApplications` (sorted Early → Normal → Late by the system), applies the magnitude via `IAttributeSystem.SetCurrentHp` (for `HpCurrent` target) or the appropriate `IAttributeSystem` setter. For each expired effect in `Expired`, publishes `EffectExpiredEvent(targetId, effectId)`.
**Location:** `Core/Modules/Effects/Handlers/EffectTickHandler.cs`
**Uses:** `IEffectSystem`, `IAttributeSystem`, `IEventBus`

### RegenerationTickHandler
**Events:** `HeartbeatTickEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** Bridge between the time system and the regeneration domain. On each tick: calls `IRegenerationSystem.ApplyTickRegen(@event.TickId)`. No events published; no persistence calls — regeneration is a closed mechanical sweep (INV-5, INV-10).
**Location:** `Core/Modules/Regeneration/Handlers/RegenerationTickHandler.cs`
**Uses:** `IRegenerationSystem`

### DeathTickHandler
**Events:** `HeartbeatTickEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** On each tick: queries all entities with `EntityStateFlags.Incapacitated` set. For each, applies `DeathOptions.BleedPerTick` damage via `IAttributeSystem.SetCurrentHp`. Reads the new HP and calls `IDeathSystem.OnHpChanged`. If the result is `Died`, publishes `PlayerDiedEvent(entityId, roomEntityId)`. Otherwise publishes `PlayerBleedingEvent(entityId, roomEntityId, newHp, hpFloor)` to notify observers of ongoing bleed. Fires after `CombatTickHandler` and `EffectTickHandler` so all pool mutations for a tick are settled before bleed is applied.
**Location:** `Core/Modules/Death/Handlers/DeathTickHandler.cs`
**Uses:** `EntityService`, `IDeathSystem`, `IAttributeSystem`, `IEntityStateService`, `IEventBus`

### PlayerDeathHandler
**Events:** `PlayerDiedEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** Orchestrates the full death-to-respawn sequence. Calls `IDeathSystem.Respawn(entityId)` — this exits Incapacitated state, teleports to respawn room, strips impermanent effects, and restores pools. Then calls `IPersistenceSystem.SaveEntityAsync(entityId)` to make the new location and pools durable before the player re-enters the world.
**Location:** `Core/Modules/Death/Handlers/PlayerDeathHandler.cs`
**Uses:** `IDeathSystem`, `IPersistenceSystem`

### DeathNarrationHandler
**Events:** `PlayerIncapacitatedEvent`, `PlayerBleedingEvent`, `PlayerDiedEvent`, `PlayerRespawnedEvent`
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** Pure output fan-out — no domain logic, no persistence. On `PlayerIncapacitatedEvent`: writes "You collapse, bleeding out..." to the player; broadcasts "X collapses!" to the room. On `PlayerBleedingEvent`: writes HP-level bleed status to the player only (severity-coded: critical when HP ≤ floor+5, warning otherwise). On `PlayerDiedEvent`: writes "You have died." to the player; broadcasts death message to the room. On `PlayerRespawnedEvent`: writes respawn room description to the player via `IBroadcastSystem.SendRoomDescriptionAsync`.
**Location:** `Core/Modules/Death/Handlers/DeathNarrationHandler.cs`
**Uses:** `EntityService`, `IBroadcastSystem`

### AdminAuditHandler
**Events:** `EntitySpawnedByAdminEvent`, `PlayerTeleportedByAdminEvent`, `RoomExitAuthoredByAdminEvent`, `ContentReloadedEvent` (Phase 3 slice 2); `RoomCreatedByAdminEvent`, `RoomPropertySetByAdminEvent` (Phase 3 slice 5a); `ItemCreatedByAdminEvent`, `ItemPropertySetByAdminEvent` (Phase 3 slice 6); `MobCreatedByAdminEvent`, `MobPropertySetByAdminEvent` (Phase 3 slice 8); `PlayerAttributeSetByAdminEvent` (Phase 3 slice 8a); `CombatEndedEvent` (Phase 3 slice 9); `EffectAppliedByAdminEvent` (Phase 3 slice 9-e); `PlayerRespawnSetByAdminEvent` (Phase 3 slice 10); `AbilityTaughtByAdminEvent` (Phase 3 slice 11-a); `RoomAreaAssignedByAdminEvent` (Phase 3 area-model WP-1); `AreaCreatedByAdminEvent` (Phase 3 admin-area-authoring WP-2).
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** writes one structured-log entry per admin action via `ILogger<AdminAuditHandler>`. Uses a stable structured event name (`AdminCommandExecuted`) so log scrapers can filter without parsing free text. No dedicated audit-file sink in this slice.
**Location:** `Core/Modules/Admin/Handlers/AdminAuditHandler.cs`
**Uses:** `EntityService` (display-name resolution), `ILogger<AdminAuditHandler>`

### OutputFlushTickHandler
**Events:** `HeartbeatTickEvent`
**Priority:** 85 (`HandlerPriority.OutputFlush`)
**Responsibilities:** Flushes all session output buffers at the end of each heartbeat tick. Runs after all output-producing handlers (priority ≤ 80) so that combat messages, effect messages, and any other tick-batched output are already enqueued before the flush fires. Calls `ISessionBufferRegistry.FlushAllPendingAsync()` — for each session with pending output, atomically drains the buffer, formats and sends each message via the session's transport formatter, then calls `IPromptSource.GetPrompt(playerEntityId)` and appends one `PromptMessage`. Sessions with no pending output are skipped. The result: players see all of a tick's combat/effect lines as a batch followed by a single trailing prompt, not a prompt after every individual message line.
**Location:** `Core/Handlers/OutputFlushTickHandler.cs`
**Uses:** `ISessionBufferRegistry`

### CommandLoggingHandler
**Events:** `CommandExecutedEvent` (Phase 3 slice 3)
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** writes one structured-log line per command dispatch via `ILogger<CommandLoggingHandler>`. Fires for every outcome (Success, ParseFailed, Unauthorized, Threw). Deliberately separate from `AdminAuditHandler` — command logging is low-fidelity and log-level-controllable; admin audit carries richer slice-2 event payloads.
**Location:** `Core/Handlers/CommandLoggingHandler.cs`
**Uses:** `ILogger<CommandLoggingHandler>`

---

## File Organization

```
Core/Modules/<Feature>/Handlers/   # feature-owned handlers
  Combat/Handlers/CombatHandler.cs
  Magic/Handlers/SpellHandler.cs

Core/Handlers/                     # cross-cutting handlers
  CommandLoggingHandler.cs
```

Within a module, handlers sit alongside their events and domain systems — see [../architecture/01-layers.md#modules-feature-cohesion](../architecture/01-layers.md#modules-feature-cohesion).

---

## Multiple Handlers for One Event

When several handlers subscribe to the same event, each must have a clearly distinct responsibility. Example — `PlayerDeathEvent` (illustrative; these handlers are planned — see [`handlers-planned.md`](handlers-planned.md)):

| Handler | Responsibility | Priority |
|---|---|---|
| `CombatHandler` | Remove from combat | 10 |
| `PlayerConditionHandler` | Apply death penalty, trigger respawn | 20 |
| `NotificationHandler` | Inform witnesses | 80 |
| `AIHandler` | Update NPC threat tables | 95 |

> Persistence for `PlayerDeathEvent` is handled by the save-on-change model: the handler that applies the state mutation calls `IPersistenceSystem.SaveEntityAsync` directly after the mutation, rather than routing through a cross-cutting `PersistenceHandler` subscription.

If handlers start needing to coordinate, see [../architecture/04-pitfalls.md#handler-ordering-issues](../architecture/04-pitfalls.md#handler-ordering-issues).
