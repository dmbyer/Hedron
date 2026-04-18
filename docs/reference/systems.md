# Systems Reference

Living catalog of every system (core and domain). Update this file whenever a system is added, removed, or renamed.

> Interfaces below use the **idealized API** — the target the codebase is being rebuilt against. See [../roadmap/plan.md](../roadmap/plan.md).

---

## Classification

| | Core Systems | Domain Systems |
|---|---|---|
| **Purpose** | Mechanically generic | Game-specific rules |
| **Test** | "How does X work?" | "What are the rules for X?" |
| **References game concepts?** | No (processes labeled data only) | Yes (stealth, magic, combat…) |
| **Reusable in a different game?** | Yes | No |
| **Typical lifetime** | Singleton | Scoped / Singleton |

---

## Core Systems

### DiceSystem
**Purpose:** RNG with dice notation support.
**Dependencies:** none.
```csharp
public interface IDiceSystem
{
    int Roll(string notation);                    // "2d6+3", "1d20"
    int Roll(int count, int sides);
    RollResult RollDetailed(string notation);
    int RollInRange(int min, int max);
}
```
Consider a deterministic mode for testing.

### SkillSystem
**Purpose:** Resolve skill checks and opposed checks.
**Dependencies:** `IDiceSystem`, `IAttributeCalculator`.
```csharp
public interface ISkillSystem
{
    SkillCheckResult Check(Entity actor, SkillType skill, int difficulty, IEnumerable<Modifier>? modifiers = null);
    OpposedCheckResult OpposedCheck(Entity actor, SkillType actorSkill, Entity target, SkillType targetSkill, IEnumerable<Modifier>? modifiers = null);
    int GetEffectiveSkillValue(Entity actor, SkillType skill);
}
```
Critical success/failure thresholds, margin calculation, and skill-modifier summing live here.

### AttributeCalculator
**Purpose:** Compute effective attribute values from base + active modifiers.
**Dependencies:** none.
```csharp
public interface IAttributeCalculator
{
    int Calculate(Entity entity, AttributeType attribute);
    int SumModifiers(Entity entity, SkillType skill, IEnumerable<Modifier>? additional = null);
    IEnumerable<Modifier> GetActiveModifiers(Entity entity, AttributeType attribute);
}
```
Handles stacking rules, caps, diminishing returns.

### EffectTracker
**Purpose:** Track timed effects on entities.
**Dependencies:** `ITimeSystem`.
```csharp
public interface IEffectTracker
{
    void ApplyEffect(Entity target, Effect effect);
    void RemoveEffect(Entity target, EffectType type);
    bool HasEffect(Entity target, EffectType type);
    Effect? GetEffect(Entity target, EffectType type);
    IEnumerable<Effect> GetActiveEffects(Entity target);
    void TickEffects(TimeSpan elapsed);
}
```
Doesn't know what effects *mean* — only tracks presence and duration.

### TimeSystem
**Purpose:** Game time management and scheduling.
**Dependencies:** none.
```csharp
public interface ITimeSystem
{
    GameTime CurrentTime { get; }
    void RegisterTimer(TimeSpan duration, Action callback);
    void Tick(TimeSpan realTimeElapsed);
    bool IsDay { get; }
    bool IsNight { get; }
}
```

### RandomGeneratorSystem
**Purpose:** Weighted random selection from tables.
**Dependencies:** `IDiceSystem`.
```csharp
public interface IRandomGeneratorSystem
{
    T SelectWeighted<T>(IEnumerable<WeightedItem<T>> items);
    T SelectFromTable<T>(LootTable<T> table, int luck = 0);
    IEnumerable<T> SelectMultiple<T>(LootTable<T> table, int count);
}
```

---

## Domain Systems

### VisibilitySystem
**Purpose:** Determine what entities can see each other.
**Dependencies:** `ISkillSystem`, `IEffectTracker`.
```csharp
public interface IVisibilitySystem
{
    bool CanSee(Entity observer, Entity target);
    bool CanSee(Entity observer, Location location);
    IEnumerable<Entity> GetVisibleEntities(Entity observer, IEnumerable<Entity> candidates);
    IEnumerable<Entity> GetWitnesses(Entity target, IEnumerable<Entity> candidates);
}
```
**Rules encoded:** True Invisibility vs True Sight; hiding = stealth vs perception; darkness blocks sight without darkvision; fog/smoke reduces range.

### CombatSystem
**Purpose:** Resolve combat actions, manage combat state.
**Dependencies:** `ISkillSystem`, `IVisibilitySystem`, `IEffectTracker`, `IAttributeCalculator`.
```csharp
public interface ICombatSystem
{
    AttackResult ResolveAttack(Entity attacker, Entity defender, Attack attack);
    int CalculateDamage(Entity attacker, Entity defender, Attack attack, AttackResult result);
    DamageResult ApplyDamage(Entity target, int damage, DamageType type);
    bool AttemptFlee(Entity fleeing, IEnumerable<Entity> opponents);
    bool IsInCombat(Entity entity);
    IEnumerable<Entity> GetCombatants(Entity entity);
}
```
**Rules encoded:** attack rolls, damage calculation, sneak attack bonuses, armor application, flee chances.

### DeathSystem
**Purpose:** Death state transitions and penalties.
**Dependencies:** `ILocationSystem`, `IInventorySystem`, `IAttributeSystem`.
```csharp
public interface IDeathSystem
{
    Task ApplyDeathPenalties(Player player);
    Task Respawn(Player player);
    Location GetRespawnLocation(Player player);
    bool CanBeRevived(Player player);
    Task Revive(Player player, Entity reviver);
}
```
**Rules encoded:** XP/stat penalties, corpse creation, respawn-point logic, revival restrictions.

### MovementSystem
**Purpose:** Entity movement between locations.
**Dependencies:** `ILocationSystem`, `IVisibilitySystem`.
```csharp
public interface IMovementSystem
{
    bool CanMove(Entity entity, Direction direction);
    bool CanMove(Entity entity, Location destination);
    MoveResult Move(Entity entity, Direction direction);
    MoveResult Teleport(Entity entity, Location destination);
    IEnumerable<Exit> GetAvailableExits(Entity entity, Location location);
}
```
**Rules encoded:** movement restrictions (combat, stunned), hidden exit discovery, movement costs.

### InventorySystem
**Purpose:** Entity inventories and item operations.
**Dependencies:** `IAttributeCalculator`.
```csharp
public interface IInventorySystem
{
    bool CanPickUp(Entity entity, Item item);
    bool CanDrop(Entity entity, Item item);
    void AddItem(Entity entity, Item item);
    void RemoveItem(Entity entity, Item item);
    bool HasItem(Entity entity, ItemTemplate template, int count = 1);
    IEnumerable<Item> GetInventory(Entity entity);
    int GetCarryCapacity(Entity entity);
    int GetCurrentLoad(Entity entity);
}
```

### EquipmentSystem
**Purpose:** Equipped items and slot assignment.
**Dependencies:** `IInventorySystem`, `IAttributeSystem`.
```csharp
public interface IEquipmentSystem
{
    bool CanEquip(Entity entity, Item item, EquipmentSlot slot);
    EquipResult Equip(Entity entity, Item item, EquipmentSlot slot);
    void Unequip(Entity entity, EquipmentSlot slot);
    Item? GetEquipped(Entity entity, EquipmentSlot slot);
    IEnumerable<EquipmentSlot> GetValidSlots(Item item);
}
```

### LootSystem
**Purpose:** Generate and distribute loot.
**Dependencies:** `IRandomGeneratorSystem`, `IItemGeneratorSystem`.
```csharp
public interface ILootSystem
{
    IEnumerable<Item> GenerateLoot(Entity source, LootContext context);
    void DistributeLoot(IEnumerable<Item> items, IEnumerable<Entity> recipients);
    LootTable GetLootTable(Entity source);
}
```
**Rules encoded:** loot-table selection, luck/magic-find modifiers, group distribution.

### ItemGeneratorSystem
**Purpose:** Create item instances with random properties.
**Dependencies:** `IRandomGeneratorSystem`, `IDiceSystem`.
```csharp
public interface IItemGeneratorSystem
{
    Item GenerateItem(ItemTemplate template, int itemLevel);
    Item GenerateRandomItem(ItemType type, int itemLevel, Rarity rarity);
    IEnumerable<ItemModifier> RollModifiers(Item item, int budget);
}
```
**Rules encoded:** affix pools and weighting, item-level scaling, rarity → modifier count/quality.

### CraftingSystem
**Purpose:** Validate and execute crafting recipes.
**Dependencies:** `ISkillSystem`, `IInventorySystem`, `IItemGeneratorSystem`.
```csharp
public interface ICraftingSystem
{
    bool CanCraft(Entity crafter, Recipe recipe);
    CraftingResult AttemptCraft(Entity crafter, Recipe recipe);
    IEnumerable<Recipe> GetKnownRecipes(Entity crafter);
    IEnumerable<Recipe> GetAvailableRecipes(Entity crafter);
}
```
**Rules encoded:** skill requirements, material-quality effects, critical crafting, recipe learning.

### TradeSystem
**Purpose:** Player-to-player trading.
**Dependencies:** `IInventorySystem`, `ICurrencySystem`.
```csharp
public interface ITradeSystem
{
    TradeSession ProposeTrade(Entity initiator, Entity target);
    void AddItem(TradeSession session, Entity party, Item item);
    void SetCurrency(TradeSession session, Entity party, Currency amount);
    void AcceptTrade(TradeSession session, Entity party);
    void CancelTrade(TradeSession session);
    TradeResult ExecuteTrade(TradeSession session);
}
```

### ShopSystem
**Purpose:** NPC merchant transactions.
**Dependencies:** `IInventorySystem`, `ICurrencySystem`, `IAttributeCalculator`.
```csharp
public interface IShopSystem
{
    int GetBuyPrice(Shop shop, Item item, Entity buyer);
    int GetSellPrice(Shop shop, Item item, Entity seller);
    PurchaseResult Buy(Entity buyer, Shop shop, Item item);
    SaleResult Sell(Entity seller, Shop shop, Item item);
    IEnumerable<Item> GetShopInventory(Shop shop);
}
```
**Rules encoded:** base prices, reputation/charisma modifiers, inventory refresh, buy/sell ratios.

### SpellSystem
**Purpose:** Spell casting and effects.
**Dependencies:** `ISkillSystem`, `IEffectTracker`, `IVisibilitySystem`, `IAttributeCalculator`.
```csharp
public interface ISpellSystem
{
    bool CanCast(Entity caster, Spell spell);
    CastResult Cast(Entity caster, Spell spell, SpellTarget target);
    IEnumerable<Entity> ResolveTargets(Entity caster, Spell spell, SpellTarget target);
    void ApplySpellEffect(Entity target, SpellEffect effect, Entity caster);
    int CalculateSpellDamage(Entity caster, Spell spell, Entity target);
}
```
**Rules encoded:** mana costs/cooldowns, spell resistance, AoE targeting, concentration interruption.

### AdvancementSystem
**Purpose:** Experience and level progression.
**Dependencies:** `IAttributeSystem`.
```csharp
public interface IAdvancementSystem
{
    void AwardExperience(Entity entity, int amount, ExperienceSource source);
    bool CheckLevelUp(Entity entity);
    LevelUpResult ApplyLevelUp(Entity entity);
    int GetExperienceForLevel(int level);
    int GetSkillCost(Entity entity, SkillType skill);
    void IncreaseSkill(Entity entity, SkillType skill);
}
```
**Rules encoded:** XP curves, level-up bonuses, skill point costs and caps.

### AISystem
**Purpose:** NPC decision-making and behavior.
**Dependencies:** `IVisibilitySystem`, `ICombatSystem`, `IMovementSystem`.
```csharp
public interface IAISystem
{
    AIAction DetermineAction(NPC npc, AIContext context);
    void UpdateThreatTable(NPC npc, Entity threat, int amount);
    Entity? GetHighestThreat(NPC npc);
    void ProcessBehaviorTree(NPC npc);
}
```

### LocationSystem
**Purpose:** Query and manage world locations.
**Dependencies:** none (data access only).
```csharp
public interface ILocationSystem
{
    Location GetLocation(LocationId id);
    IEnumerable<Entity> GetEntitiesAt(Location location);
    IEnumerable<Entity> GetEntitiesInArea(Location center, int radius);
    void PlaceEntity(Entity entity, Location location);
    void RemoveEntity(Entity entity, Location location);
    IEnumerable<Exit> GetExits(Location location);
}
```

### NotificationSystem
**Purpose:** Message delivery to players.
**Dependencies:** none.
```csharp
public interface INotificationSystem
{
    void Send(Entity recipient, string message);
    void SendToMany(IEnumerable<Entity> recipients, string message);
    void SendToRoom(Location room, string message, Entity? exclude = null);
    void SendFormatted(Entity recipient, MessageTemplate template, params object[] args);
}
```

---

## Dependency Graph

The graph must always be a DAG. If you find yourself wanting an upward arrow, see [../architecture/04-pitfalls.md#circular-dependencies](../architecture/04-pitfalls.md#circular-dependencies).

```
                    ┌─────────────────────┐
                    │    AISystem         │
                    └──────────┬──────────┘
                               │
        ┌──────────────────────┼──────────────────────┐
        ▼                      ▼                      ▼
┌───────────────┐    ┌─────────────────┐    ┌─────────────────┐
│ CombatSystem  │    │VisibilitySystem │    │ MovementSystem  │
└───────┬───────┘    └────────┬────────┘    └────────┬────────┘
        │                     │                      │
        └──────────┬──────────┴──────────────────────┘
                   ▼
          ┌────────────────┐
          │  SkillSystem   │
          └───────┬────────┘
                  │
        ┌─────────┴─────────┐
        ▼                   ▼
┌───────────────┐  ┌────────────────────┐
│  DiceSystem   │  │ AttributeCalculator│
└───────────────┘  └────────────────────┘
```
