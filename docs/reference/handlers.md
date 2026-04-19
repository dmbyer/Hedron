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
**Events:** `PlayerLoginEvent`, `PlayerLogoutEvent`, `CharacterCreatedEvent`, `CharacterDeletedEvent`
**Responsibilities:** initialize state on login; spawn at appropriate location; announce arrivals/departures; clean up on logout; set up new-character attributes/inventory.
**Uses:** `ILocationSystem`, `INotificationSystem`, `IInventorySystem`

### PlayerConditionHandler
**Events:** `PlayerDeathEvent`, `PlayerReviveEvent`, `PlayerRestStartedEvent`, `PlayerRestCompletedEvent`, `PlayerUnconsciousEvent`
**Responsibilities:** apply death penalties; respawn; rest-state transitions; rest recovery; unconscious state.
**Uses:** `IDeathSystem`, `IVisibilitySystem`, `INotificationSystem`, `ILocationSystem`, `IAttributeSystem`

### PlayerMovementHandler
**Events:** `PlayerMoveEvent`, `PlayerTeleportEvent`, `PlayerEnterRoomEvent`, `PlayerExitRoomEvent`
**Responsibilities:** validate movement; update location; trigger room descriptions; notify origin/destination rooms; fire movement triggers (traps, ambushes).
**Uses:** `IMovementSystem`, `ILocationSystem`, `IVisibilitySystem`, `INotificationSystem`

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

### PersistenceHandler
**Events:** `EntityMutatedEvent` and other state-change events; shutdown and timer triggers.
**Responsibilities:** dirty-track via event subscription; batch writes on flush; atomic write-and-rename; log and retry on partial failure.
**Uses:** `IPersistenceSystem`
> Persistence is event-driven dirty-tracking — handlers never call persistence directly.

### AIHandler
**Events:** `PlayerEnteredRoomEvent`, `PlayerAttackedNPCEvent`, `NPCHealthLowEvent`, `TimeTickEvent`
**Responsibilities:** trigger NPC behavior; aggro/threat management; decision trees; patrol/wander patterns.
**Uses:** `IAISystem`, `ICombatSystem`, `IMovementSystem`, `IVisibilitySystem`

### CommandHandler
**Events:** `CommandReceivedEvent`
**Responsibilities:** parse text input; validate syntax; route to executors; aliases; help/error messages.
**Uses:** `ICommandParserService`, domain systems per-command
> Entry point from player input. Parses, then raises more specific events.

---

## File Organization

```
Core/Modules/<Feature>/Handlers/   # feature-owned handlers
  Combat/Handlers/CombatHandler.cs
  Magic/Handlers/SpellHandler.cs

Core/Handlers/                     # cross-cutting handlers
  NotificationHandler.cs
  PersistenceHandler.cs
  CommandHandler.cs
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
| `PersistenceHandler` | Save state | 90 |
| `AIHandler` | Update NPC threat tables | 95 |

If handlers start needing to coordinate, see [../architecture/04-pitfalls.md#handler-ordering-issues](../architecture/04-pitfalls.md#handler-ordering-issues).
