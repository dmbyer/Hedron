# Handlers Reference — Planned / Idealized

> **Design intent, not a catalog of what exists.** Every handler below is **not yet built**; most reference domain systems that are themselves planned (see [`systems-planned.md`](systems-planned.md)). The event names, responsibilities, and priorities are idealized and will be re-shaped when the slice that needs them lands. For handlers that actually exist, see [`handlers.md`](handlers.md).

---

### PlayerConditionHandler
**Events:** `PlayerDeathEvent`, `PlayerReviveEvent`, `PlayerRestStartedEvent`, `PlayerRestCompletedEvent`, `PlayerUnconsciousEvent`
**Responsibilities:** apply death penalties; respawn; rest-state transitions; rest recovery; unconscious state.
**Uses:** `IDeathSystem`, `IVisibilitySystem`, `IBroadcastSystem`, `ILocationSystem`, `IAttributeSystem`

### CombatHandler
**Events:** `AttackEvent`, `DamageEvent`, `PlayerDeathEvent`, `FleeEvent`, `CombatStartedEvent`, `CombatEndedEvent`
**Responsibilities:** resolve attack rolls and damage; manage combat engagement state; remove dead/fled entities; determine combat end; initiative/turn order.
**Uses:** `ICombatSystem`, `IVisibilitySystem`, `IBroadcastSystem`
> Subscribes to `PlayerDeathEvent` only for combat-state cleanup. Death-penalty logic lives in `PlayerConditionHandler`.

### InventoryHandler
**Events:** `ItemPickedUpEvent`, `ItemDroppedEvent`, `ItemEquippedEvent`, `ItemUnequippedEvent`, `ItemDestroyedEvent`
**Responsibilities:** update inventory state; apply equipment bonuses; validate slot compatibility; encumbrance calculations; notify player.
**Uses:** `IInventorySystem`, `IEquipmentSystem`, `IAttributeSystem`, `IBroadcastSystem`

### LootHandler
**Events:** `LootDroppedEvent`, `LootCollectedEvent`, `ContainerOpenedEvent`
**Responsibilities:** generate loot; create instances from templates; group loot distribution; manage container inventories.
**Uses:** `ILootSystem`, `IItemGeneratorSystem`, `IInventorySystem`, `IBroadcastSystem`

### CraftingHandler
**Events:** `CraftingStartedEvent`, `CraftingCompletedEvent`, `CraftingFailedEvent`
**Responsibilities:** validate recipe requirements; consume materials; crafting skill checks; generate crafted items with quality modifiers; handle interruption.
**Uses:** `ICraftingSystem`, `IInventorySystem`, `ISkillSystem`, `IItemGeneratorSystem`, `IBroadcastSystem`

### TradeHandler
**Events:** `TradeProposedEvent`, `TradeAcceptedEvent`, `TradeDeclinedEvent`, `TradeCancelledEvent`
**Responsibilities:** validate trade legality; manage trade state machine; execute item/currency exchange; handle timeouts.
**Uses:** `ITradeSystem`, `IInventorySystem`, `IBroadcastSystem`

### ShopHandler
**Events:** `ShopPurchaseEvent`, `ShopSaleEvent`, `ShopBrowseEvent`
**Responsibilities:** calculate prices; validate funds/items; execute transactions; update shop inventory; reputation/haggling modifiers.
**Uses:** `IShopSystem`, `IInventorySystem`, `ICurrencySystem`, `IBroadcastSystem`

### SpellHandler
**Events:** `SpellCastEvent`, `SpellEffectAppliedEvent`, `SpellEffectExpiredEvent`, `SpellInterruptedEvent`
**Responsibilities:** validate spell requirements; resolve targeting; apply effects; handle interruption; concentration.
**Uses:** `ISpellSystem`, `IEffectTracker`, `IVisibilitySystem`, `IBroadcastSystem`

### AdvancementHandler
**Events:** `ExperienceGainedEvent`, `LevelUpEvent`, `SkillIncreasedEvent`, `AttributeIncreasedEvent`
**Responsibilities:** award XP; check level-up thresholds; apply level-up bonuses; skill-point allocation; notify player.
**Uses:** `IAdvancementSystem`, `IAttributeSystem`, `IBroadcastSystem`

### NotificationHandler
**Events:** subscribes broadly as a cross-cutting concern
**Responsibilities:** determine recipients by visibility and location; format messages per recipient type (actor, target, witness); queuing/delivery; respect preferences.
**Uses:** `IVisibilitySystem`, `ILocationSystem`, `INotificationSystem`
> Usually a *secondary* handler alongside the primary domain handler. Focuses on "who sees what". Note: much of this role is covered by the implemented `IBroadcastSystem` audience-filter model — build only if per-recipient visibility filtering is genuinely needed.

### AIHandler
**Events:** `PlayerEnteredRoomEvent`, `PlayerAttackedNPCEvent`, `NPCHealthLowEvent`, `TimeTickEvent`
**Responsibilities:** trigger NPC behavior; aggro/threat management; decision trees; patrol/wander patterns.
**Uses:** `IAISystem`, `ICombatSystem`, `IMovementSystem`, `IVisibilitySystem`
