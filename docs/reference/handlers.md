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
**Responsibilities:** On connect: attaches transient `PlayerComponent` (DisplayName, Session) and broadcasts arrival to the room; sends `SendRoomDescriptionAsync` to the connecting player. On disconnect: calls `IAccountSystem.RecordLogout`, then immediately calls `IPersistenceSystem.SaveEntityAsync(characterEntityId)` so the logout timestamp is durable, removes `PlayerComponent` via `EntityService.RemoveComponent<T>`, broadcasts departure. The character entity is **not** destroyed on disconnect.
**Location:** `Core/Modules/Session/Handlers/PlayerSessionHandler.cs`
**Uses:** `EntityService`, `ISessionManager`, `IBroadcastSystem`, `IAccountSystem`, `IPersistenceSystem`

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
**Responsibilities:** Finalizes the mob death path. Priority 80 — deliberately lower than `CombatHandler` (priority 20) so the death narrative is broadcast before entity destruction. Calls `IEntityStateService.ExitState(attackerEntityId, InCombat)`, removes `BlueprintComponent` from the mob entity (INV-21: frees the blueprint slot so `WorldContentLoader` re-seeds on next startup/reload), then calls `EntityService.DestroyEntity(mobEntityId)`. Does not publish events. Loot drop deferred to slice 10.
**Location:** `Core/Modules/Combat/Handlers/CombatMobDeathHandler.cs`
**Uses:** `EntityService`, `IEntityStateService`

### AdminAuditHandler
**Events:** `EntitySpawnedByAdminEvent`, `PlayerTeleportedByAdminEvent`, `RoomExitAuthoredByAdminEvent`, `ContentReloadedEvent` (Phase 3 slice 2); `RoomCreatedByAdminEvent`, `RoomPropertySetByAdminEvent` (Phase 3 slice 5a); `ItemCreatedByAdminEvent`, `ItemPropertySetByAdminEvent` (Phase 3 slice 6); `MobCreatedByAdminEvent`, `MobPropertySetByAdminEvent` (Phase 3 slice 8); `PlayerAttributeSetByAdminEvent` (Phase 3 slice 8a); `CombatEndedEvent` (Phase 3 slice 9).
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** writes one structured-log entry per admin action via `ILogger<AdminAuditHandler>`. Uses a stable structured event name (`AdminCommandExecuted`) so log scrapers can filter without parsing free text. No dedicated audit-file sink in this slice.
**Location:** `Core/Modules/Admin/Handlers/AdminAuditHandler.cs`
**Uses:** `EntityService` (display-name resolution), `ILogger<AdminAuditHandler>`

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
