# Handlers Reference

Living catalog of every event handler. Handlers are grouped by **cohesion**, not breadth — related events that share context live together.

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

### PlayerConditionHandler
**Events:** `PlayerDeathEvent`, `PlayerReviveEvent`, `PlayerRestStartedEvent`, `PlayerRestCompletedEvent`, `PlayerUnconsciousEvent`
**Responsibilities:** apply death penalties; respawn; rest-state transitions; rest recovery; unconscious state.
**Uses:** `IDeathSystem`, `IVisibilitySystem`, `INotificationSystem`, `ILocationSystem`, `IAttributeSystem`

### PlayerMovedHandler
**Events:** `PlayerMovedEvent`, `PlayerTeleportedByAdminEvent` (Phase 3 slice 2), `PlayerEnterRoomEvent`, `PlayerExitRoomEvent`
**Responsibilities:** translate a successful move (player-initiated or admin-teleport) into the visible effects: departure broadcast on source room, arrival broadcast on destination, `look` to the moved player; fire movement triggers (traps, ambushes). Both move and teleport events funnel through the same private helper to avoid drift; teleport uses direction-agnostic flavour text.
**Uses:** `IMovementSystem`, `ILocationSystem`, `IVisibilitySystem`, `IBroadcastSystem`

### CombatHandler
**Events:** `AttackEvent`, `DamageEvent`, `PlayerDeathEvent`, `FleeEvent`, `CombatStartedEvent`, `CombatEndedEvent`
**Responsibilities:** resolve attack rolls and damage; manage combat engagement state; remove dead/fled entities; determine combat end; initiative/turn order.
**Uses:** `ICombatSystem`, `IVisibilitySystem`, `INotificationSystem`
> Subscribes to `PlayerDeathEvent` only for combat-state cleanup. Death-penalty logic lives in `PlayerConditionHandler`.

### InventoryHandler
**Events:** `ItemPickedUpEvent`, `ItemDroppedEvent`, `ItemEquippedEvent`, `ItemUnequippedEvent`, `ItemDestroyedEvent`
**Responsibilities:** update inventory state; apply equipment bonuses; validate slot compatibility; encumbrance calculations; notify player.
**Uses:** `IInventorySystem`, `IEquipmentSystem`, `IAttributeSystem`, `INotificationSystem`

### LootHandler
**Events:** `LootDroppedEvent`, `LootCollectedEvent`, `ContainerOpenedEvent`
**Responsibilities:** generate loot; create instances from templates; group loot distribution; manage container inventories.
**Uses:** `ILootSystem`, `IItemGeneratorSystem`, `IInventorySystem`, `INotificationSystem`

### CraftingHandler
**Events:** `CraftingStartedEvent`, `CraftingCompletedEvent`, `CraftingFailedEvent`
**Responsibilities:** validate recipe requirements; consume materials; crafting skill checks; generate crafted items with quality modifiers; handle interruption.
**Uses:** `ICraftingSystem`, `IInventorySystem`, `ISkillSystem`, `IItemGeneratorSystem`, `INotificationSystem`

### TradeHandler
**Events:** `TradeProposedEvent`, `TradeAcceptedEvent`, `TradeDeclinedEvent`, `TradeCancelledEvent`
**Responsibilities:** validate trade legality; manage trade state machine; execute item/currency exchange; handle timeouts.
**Uses:** `ITradeSystem`, `IInventorySystem`, `INotificationSystem`

### ShopHandler
**Events:** `ShopPurchaseEvent`, `ShopSaleEvent`, `ShopBrowseEvent`
**Responsibilities:** calculate prices; validate funds/items; execute transactions; update shop inventory; reputation/haggling modifiers.
**Uses:** `IShopSystem`, `IInventorySystem`, `ICurrencySystem`, `INotificationSystem`

### SpellHandler
**Events:** `SpellCastEvent`, `SpellEffectAppliedEvent`, `SpellEffectExpiredEvent`, `SpellInterruptedEvent`
**Responsibilities:** validate spell requirements; resolve targeting; apply effects; handle interruption; concentration.
**Uses:** `ISpellSystem`, `IEffectTracker`, `IVisibilitySystem`, `INotificationSystem`

### AdvancementHandler
**Events:** `ExperienceGainedEvent`, `LevelUpEvent`, `SkillIncreasedEvent`, `AttributeIncreasedEvent`
**Responsibilities:** award XP; check level-up thresholds; apply level-up bonuses; skill-point allocation; notify player.
**Uses:** `IAdvancementSystem`, `IAttributeSystem`, `INotificationSystem`

### NotificationHandler
**Events:** subscribes broadly as a cross-cutting concern
**Responsibilities:** determine recipients by visibility and location; format messages per recipient type (actor, target, witness); queuing/delivery; respect preferences.
**Uses:** `IVisibilitySystem`, `ILocationSystem`, `INotificationSystem`
> Usually a *secondary* handler alongside the primary domain handler. Focuses on "who sees what".

### CharacterHydrationHandler
**Events:** `WorldContentReadyEvent`
**Priority:** 20 (`HandlerPriority.Domain`)
**Responsibilities:** After world content is fully loaded, iterates every entity that has both `CharacterComponent` and `LocationComponent`. Validates `LocationComponent.RoomEntityId` via `HasComponent<RoomComponent>`; if the room no longer exists (deleted YAML), resets to `WorldConfiguration.StartingRoomEntityId`, logs a warning, and calls `IPersistenceSystem.SaveEntityAsync` immediately so the correction is durable without waiting for the next flush cycle. Runs once at startup; no-op if no character entities exist.
**Location:** `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs`
**Uses:** `EntityService`, `WorldConfiguration`, `IPersistenceSystem`, `ILogger`

### AdminAuditHandler
**Events:** `EntitySpawnedByAdminEvent`, `PlayerTeleportedByAdminEvent`, `RoomExitAuthoredByAdminEvent`, `ContentReloadedEvent` (Phase 3 slice 2); `RoomCreatedByAdminEvent`, `RoomPropertySetByAdminEvent` (Phase 3 slice 5a).
**Priority:** 80 (`HandlerPriority.Notification`)
**Responsibilities:** writes one structured-log entry per admin action via `ILogger<AdminAuditHandler>`. Uses a stable structured event name (`AdminCommandExecuted`) so log scrapers can filter without parsing free text. No dedicated audit-file sink in this slice.
**Location:** `Core/Modules/Admin/Handlers/AdminAuditHandler.cs`
**Uses:** `EntityService` (display-name resolution), `ILogger<AdminAuditHandler>`

### AIHandler
**Events:** `PlayerEnteredRoomEvent`, `PlayerAttackedNPCEvent`, `NPCHealthLowEvent`, `TimeTickEvent`
**Responsibilities:** trigger NPC behavior; aggro/threat management; decision trees; patrol/wander patterns.
**Uses:** `IAISystem`, `ICombatSystem`, `IMovementSystem`, `IVisibilitySystem`

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

When several handlers subscribe to the same event, each must have a clearly distinct responsibility. Example — `PlayerDeathEvent`:

| Handler | Responsibility | Priority |
|---|---|---|
| `CombatHandler` | Remove from combat | 10 |
| `PlayerConditionHandler` | Apply death penalty, trigger respawn | 20 |
| `NotificationHandler` | Inform witnesses | 80 |
| `AIHandler` | Update NPC threat tables | 95 |

> Persistence for `PlayerDeathEvent` is handled by the save-on-change model: the handler that applies the state mutation calls `IPersistenceSystem.SaveEntityAsync` directly after the mutation, rather than routing through a cross-cutting `PersistenceHandler` subscription.

If handlers start needing to coordinate, see [../architecture/04-pitfalls.md#handler-ordering-issues](../architecture/04-pitfalls.md#handler-ordering-issues).
