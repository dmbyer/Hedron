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

### BroadcastSystem
**Purpose:** Deliver typed `IOutputMessage` output to rooms, every session, or a single player. Each recipient's message is rendered by their transport's `IOutputFormatter` via `IOutputWriterFactory`, so callers never construct raw strings.
**Location:** `Core/Systems/BroadcastSystem.cs`
**Dependencies:** `EntityService`, `ISessionManager`, `IOutputWriterFactory`.
**Note:** Classified as output infrastructure rather than a pure-computation core system; it does I/O (calls `IOutputWriter` per recipient) as the designated multi-recipient fan-out seam.
```csharp
public interface IBroadcastSystem
{
    Task SendToRoomAsync(uint roomEntityId, IOutputMessage message, Func<uint, bool>? audienceFilter = null);
    Task SendToAllAsync(IOutputMessage message);
    Task SendRoomDescriptionAsync(uint playerEntityId, uint roomEntityId);
}
```
Implemented (Phase 2, rewritten in Phase 3 slice 4).

### Output Infrastructure (IOutputFormatter, IOutputFormatterRegistry, IOutputWriterFactory)
**Purpose:** Formatter pipeline that converts typed `IOutputMessage` shapes to transport-encoded strings before writing to sessions.
**Location:** `Core/Output/`
**Dependencies:** `ISession` (for `TransportKey` and `SupportsColor`).

```csharp
// One implementation per transport.
public interface IOutputFormatter
{
    string TransportKey { get; }    // "telnet", future "signalr"
    string Format(IOutputMessage message, ISession session);
}

// Selects the right formatter by session.TransportKey.
public interface IOutputFormatterRegistry
{
    IOutputFormatter Resolve(ISession session);
}

// Single-session output seam consumed by commands and broadcast.
public interface IOutputWriter  { Task WriteAsync(IOutputMessage message); }
public interface IOutputWriterFactory { IOutputWriter Create(ISession session); }
```

`TelnetOutputFormatter` (`TransportKey = "telnet"`) applies the four-role ANSI palette (system/error/room-name/direction) and parses `<role>text</role>` inline markers. Strips all color when `session.SupportsColor == false`. See [`../architecture/07-output.md`](../architecture/07-output.md) for the full design including the color palette table and inline marker syntax.

Implemented (Phase 3 slice 4).

### ComponentTypeRegistry
**Purpose:** Reflection-built map of every `IComponent` implementor; records which types carry `[Persistent]`.
**Location:** `Core/Systems/ComponentTypeRegistry.cs`
**Dependencies:** none (reflection over Core assembly).
```csharp
public interface IComponentTypeRegistry
{
    bool IsPersistent(Type componentType);
    Type? Resolve(string typeName);
    IReadOnlyList<Type> AllPersistentTypes();
}
```
Built once at construction; immutable thereafter. Implemented (Phase 3 slice 1).

### ComponentSerializer
**Purpose:** Serialize/deserialize individual `IComponent` instances to/from JSON.
**Location:** `Core/Systems/ComponentSerializer.cs`
**Dependencies:** `IComponentTypeRegistry`.
```csharp
public interface IComponentSerializer
{
    string Serialize(IComponent component);
    IComponent? Deserialize(string typeName, string data);
}
```
Uses `System.Text.Json` with camelCase policy and `JsonStringEnumConverter`. Implemented (Phase 3 slice 1).

### PersistenceSystem
**Purpose:** Save and load `[Persistent]`-tagged components for every dirty entity.
**Location:** `Core/Systems/PersistenceSystem.cs`
**Dependencies:** `EntityService`, `IComponentTypeRegistry`, `IComponentSerializer`, `IConfiguration`, `ILogger<PersistenceSystem>`. No `IEventBus` dependency — all event publishing is the caller's responsibility.
```csharp
public interface IPersistenceSystem
{
    void MarkDirty(uint entityId);
    bool IsDirty(uint entityId);
    Task FlushAsync(CancellationToken ct = default);
    Task SaveEntityAsync(uint entityId, CancellationToken ct = default);
    Task LoadAllAsync(CancellationToken ct = default);
}
```
Entity files: `data/entities/entity-{id}.json`. Atomic write (`.tmp` → rename). Best-effort flush. Implemented (Phase 3 slice 1).

### TemplateRegistry
**Purpose:** Cross-cutting registry of authored `IEntityTemplate`s. Every content-bearing module (world, mobs, items, shops) registers into the same registry.
**Location:** `Core/Systems/TemplateRegistry.cs`
**Dependencies:** `EntityService`.
```csharp
public interface ITemplateRegistry
{
    void Register(string blueprintId, IEntityTemplate template);
    bool TryGet(string blueprintId, out IEntityTemplate? template);
    Entity Spawn(string blueprintId);
    Entity Spawn(string blueprintId, IDictionary<string, object>? overrides);
    IReadOnlyCollection<string> AllBlueprintIds();
    void Clear();
}
```
On `Spawn`, the registry allocates an entity, attaches a `BlueprintComponent` recording the blueprint id, then invokes `IEntityTemplate.Apply` to add archetype-specific components. No events published — callers (e.g. admin `@spawn`) publish their own past-tense events. Implemented (Phase 3 slice 2).

### YamlContentSerializer
**Purpose:** Cross-cutting kind-dispatcher that routes YAML file bodies to the right `ITemplateDeserializer`. Owns no module knowledge — modules register their own per-kind deserializers via DI.
**Location:** `Core/Systems/YamlContentSerializer.cs` (interface `IContentSerializer`).
**Dependencies:** `IEnumerable<ITemplateDeserializer>` (DI-collected).
```csharp
public interface IContentSerializer
{
    IEntityTemplate Deserialize(string kind, string fileBody);
    string FormatExtension { get; }   // ".yaml"
}

public interface ITemplateDeserializer
{
    string Kind { get; }                            // e.g. "room", "area", "mob"
    IEntityTemplate Deserialize(string fileBody);
}
```
Persistence (slice 1) keeps `System.Text.Json` on a separate code path — content authoring (designer-write) and runtime persistence (machine round-trip) coexist by design and do not share serializer code. The World module registers `RoomTemplateDeserializer` and `AreaTemplateDeserializer`; future content modules register their own kinds the same way. Implemented (Phase 3 slice 2).

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

### WorldContentLoader
**Purpose:** Scans the configured content directory, registers authored YAML templates with `ITemplateRegistry`, and seeds room/area entities for blueprints not already represented by a hydrated entity. Wraps `LoadAndSpawnAsync` in a hosted-service shell (`Server/WorldContentBootstrap`) to enforce startup ordering after `PersistenceBootstrap`.
**Location:** `Core/Modules/World/Systems/WorldContentLoader.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `IContentSerializer`, `WorldConfiguration`, `IConfiguration`, `ILogger`.
```csharp
public interface IWorldContentLoader
{
    Task LoadAndSpawnAsync(CancellationToken ct = default);
    Task<ContentReloadResult> ReloadAsync(CancellationToken ct = default);
}

public readonly record struct ContentReloadResult(
    int TemplatesLoaded, int TemplatesUnchanged, int TemplatesRemoved);
```
Empty/missing content directory → seeds a single hardcoded `room.void` and warns (host stays up for first-run authors). `ReloadAsync` is **additive only**: refreshes the template registry and seeds missing entities; existing live entities are not mutated. Implemented (Phase 3 slice 2).

### AccountSystem
**Purpose:** Domain system owning all account and character lifecycle operations: registration, authentication, character creation, character list, and logout recording.
**Location:** `Core/Modules/Account/Systems/AccountSystem.cs`
**Dependencies:** `EntityService`, `IPersistenceSystem`, `IPasswordHasher`, `WorldConfiguration`.
```csharp
public interface IAccountSystem
{
    bool UsernameExists(string username);
    bool CharacterNameExists(string characterName);
    Task<uint> CreateAccountAsync(string username, string password, CancellationToken ct = default);
    Task<AuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default);
    Task<uint> CreateCharacterAsync(uint accountEntityId, string characterName, CancellationToken ct = default);
    IReadOnlyList<CharacterSummary> GetCharacterList(uint accountEntityId);
    void RecordLogout(uint characterEntityId);
}
```
Maintains lazy in-memory HashSet indices for username and character name uniqueness (populated on first call, updated on every write). `CreateCharacterAsync` creates the character entity, attaches `CharacterComponent` + `LocationComponent` (set to `StartingRoomEntityId`), and registers the character on the account. Implemented (Phase 3 slice 5).

### PasswordHasher
**Purpose:** PBKDF2-SHA256 password hashing and verification with no external NuGet dependency.
**Location:** `Core/Systems/PasswordHasher.cs`
**Dependencies:** none (`System.Security.Cryptography`).
```csharp
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
```
100,000 PBKDF2 iterations, 16-byte random salt, 32-byte key. Stores `Base64(salt + hash)` as a single opaque string. `Verify` uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks. Implemented (Phase 3 slice 5).

### RoomBuilderSystem
**Purpose:** Runtime room authoring — creates room entities, wires bidirectional exits, and mutates room properties (`Name`, `Description`). All methods return pure results or mutate in-place; event publication is the caller's responsibility, keeping this system reusable by a future in-game editor without a live player session.
**Location:** `Core/Modules/Admin/Systems/RoomBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<RoomBuilderSystem>`.
```csharp
public interface IRoomBuilderSystem
{
    RoomCreationResult CreateRoom(string name, string description = "");
    void LinkExits(uint sourceRoomId, Direction direction, uint targetRoomId, bool bidirectional);
    void SetRoomName(uint roomId, string name);
    void SetRoomDescription(uint roomId, string description);
}

public readonly record struct RoomCreationResult(uint RoomEntityId, string BlueprintId);
```
`CreateRoom` generates a unique blueprint id (`room.adhoc.<8-char-base36>`), creates the entity, attaches `RoomComponent` + `BlueprintComponent`, and registers a minimal `RoomTemplate`. `LinkExits` updates both `RoomComponent.Exits` and the in-memory `RoomTemplate` exit maps for same-session `reload` consistency. Implemented (Phase 3 slice 5a).

### AdminAuthorizer
**Purpose:** Policy seam for admin command authorization. Each admin `ICommand.Execute` calls `IsPrivileged` as its first line; non-privileged sessions get a single rejection line and the command body short-circuits.
**Location:** `Core/Modules/Admin/Systems/AdminAuthorizer.cs`
**Dependencies:** `EntityService`, `IConfiguration`.
```csharp
public interface IAdminAuthorizer
{
    bool IsPrivileged(ISession session);
    bool IsPrivileged(uint playerEntityId);
}
```
**Layered authorization model.** Bootstrap layer (slice 2): reads `Admin:PrivilegedNames` (string array) from `IConfiguration` and matches against the player's `PlayerComponent.DisplayName`. Persisted layer (deferred — see [`../use-cases/admin-privilege-elevation.md`](../use-cases/admin-privilege-elevation.md)): an `AdminPrivilegeComponent` (`[Persistent]`) on a player entity also grants admin rights. Settings is the floor — anyone in `Admin:PrivilegedNames` is always admin even without the component. Implemented (Phase 3 slice 2; component layer deferred).

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
