# Systems Reference

Living catalog of the systems **implemented** in Hedron (core and domain). Update this file whenever a system is added, removed, or renamed.

> Idealized designs for systems not yet built live in [`systems-planned.md`](systems-planned.md) — design intent only; do not assume anything there exists. Why implemented and planned are separated: [`../documentation-architecture.md`](../documentation-architecture.md).

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

### DefinitionRegistry / IRegistry (generic registry infrastructure)
**Purpose:** Uniform lookup contract and instance-based store for all definition families. Two type parameters let each family choose the key type that fits its nature: enum for fixed code-owned vocabularies (Aspect, Score), string for open/persisted/content-authored families (Ability, Effect). Instance-held rows are reload-shaped (a future `Reload(rows)` is additive without touching this contract).
**Location:** `Core/Systems/DefinitionRegistry.cs`
**Dependencies:** none.
```csharp
public interface IRegistry<TKey, TDef> where TKey : notnull
{
    bool TryGet(TKey key, out TDef definition);
    TDef Get(TKey key);
    IReadOnlyCollection<TKey> AllIds { get; }
    IReadOnlyCollection<TDef> All { get; }
}

// Abstract base — subclass supplies rows + key selector at construction.
public abstract class DefinitionRegistry<TKey, TDef> : IRegistry<TKey, TDef> { ... }
```
`AbilityRegistry`, `EffectRegistry`, and `StatRegistry` are all `DefinitionRegistry<TKey,TDef>` subclasses. `AspectRegistry` is the fourth consumer that anchored the extraction. Implemented (Phase 3 slice 11-d).

### BroadcastSystem
**Purpose:** Deliver typed `IOutputMessage` output to rooms, every session, or a single player. Each recipient's message is rendered by their transport's `IOutputFormatter` via `IOutputWriterFactory`, so callers never construct raw strings.
**Location:** `Core/Systems/BroadcastSystem.cs`
**Dependencies:** `EntityService`, `ISessionManager`, `IOutputWriterFactory`.
**Note:** Classified as output infrastructure rather than a pure-computation core system; it does I/O (calls `IOutputWriter` per recipient) as the designated multi-recipient fan-out seam. Extended in slice 8: `SendRoomDescriptionAsync` populates `RoomDescriptionMessage.Mobs` with `MobDataComponent.Name` for each entity in the room carrying `MobDataComponent`.
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

`TelnetOutputFormatter` (`TransportKey = "telnet"`) applies the four-role ANSI palette (system/error/room-name/direction) and parses `<role>text</role>` inline markers. Strips all color when `session.SupportsColor == false`. See [`../architecture/subsystems/output.md`](../architecture/subsystems/output.md) for the full design including the color palette table and inline marker syntax.

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
**Purpose:** Save and load entity state using the two-level model backed by SQLite: an entity is written only if it carries `PersistentEntity`; among its components, only those tagged `[Persistent]` are included in the snapshot. Registers `EntityService.OnPersistentEntityDestroying` so every `DestroyEntity` call for a persistent entity automatically deletes its SQLite rows — no caller ever needs to clean up manually.
**Location:** `Core/Systems/PersistenceSystem.cs`
**Dependencies:** `EntityService`, `IComponentTypeRegistry`, `IComponentSerializer`, `IConfiguration`, `ILogger<PersistenceSystem>`, `Microsoft.Data.Sqlite`. No `IEventBus` dependency — all event publishing is the caller's responsibility.
```csharp
public interface IPersistenceSystem
{
    Task SaveEntityAsync(uint entityId, CancellationToken ct = default);
    Task FlushAllAsync(CancellationToken ct = default);
    Task FlushDirtyAsync(CancellationToken ct = default);
    Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default);
}
```
SQLite schema: `entity_components(entity_id INTEGER, type_name TEXT, data TEXT, PRIMARY KEY(entity_id, type_name))`. On save: delete existing rows for the entity, then insert one row per `[Persistent]` component (wrapped in a transaction). `SaveEntityAsync` is the caller-initiated boundary-save path — used at entity construction (account/character creation), for admin boundary saves (an admin-gated mutation command paired with an audit event, e.g. `setplayer`/`setrespawn`), and for session-end force-saves (player logout/disconnect/`quit`); never for ordinary runtime mutations, which the flush covers. `FlushDirtyAsync` is called by `PersistenceFlushTimer` on each tick — performs a full sweep of all `PersistentEntity`-carrying entities (no footprint calculation). `FlushAllAsync` is called by `PersistenceBootstrap.StopAsync` for a complete shutdown sweep; identical logic to `FlushDirtyAsync`. Configuration key: `Persistence:DatabasePath` (default `data/hedron.db`); Docker env var override: `HEDRON_PERSISTENCE__DATABASEPATH`. Implements `IDisposable` — holds the open `SqliteConnection` for the process lifetime. Implemented (Phase 3 persistence-reform Stage A).

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

### ArchetypeRegistry
**Purpose:** Validation and detection gateway for entity archetypes. Defines the required/optional component composition for each `EntityArchetype`, validates that an entity carries all required components (`Validate`), infers the best-matching archetype by inspecting an entity's component set (`Detect`), and yields which required components are absent (`MissingRequired`). Never used for entity construction.
**Location:** `Core/ECS/ArchetypeRegistry.cs` (implementation) · `Core/ECS/IArchetypeRegistry.cs` (interface) · `Core/ECS/ArchetypeDefinition.cs` (definition shape) · `Core/ECS/EntityArchetype.cs` (enum)
**Dependencies:** `EntityService`.
```csharp
public interface IArchetypeRegistry
{
    IReadOnlyList<Type> RequiredComponents(EntityArchetype archetype);
    IReadOnlyList<Type> OptionalComponents(EntityArchetype archetype);
    bool Validate(uint entityId, EntityArchetype expected);
    EntityArchetype Detect(uint entityId);
    IEnumerable<Type> MissingRequired(uint entityId, EntityArchetype archetype);
}
```
Detection order (most-specific → least-specific): `Mob` → `Player` → `Room` → `Area` → `StaticItem`. `Detect` returns `EntityArchetype.Custom` when no standard archetype matches. `MissingRequired` is consumed by `WorldContentLoader.MigrateEntityComponentsAsync` at startup/reload to repair entities missing required components — adding missing components without ever removing extras (data-safety guarantee). Registered as a singleton in `Server/Program.cs`. Implemented (Phase 3 slice 9).

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

### IRandom / SystemRandom
**Purpose:** Injectable randomness seam — the single source of non-determinism for game logic, so chance-based outcomes can be made deterministic in tests by substituting a fake (INV-26). Systems take `IRandom` by constructor injection instead of reaching for `Random.Shared`. Pure: no events, no persistence.
**Location:** `Core/Systems/IRandom.cs` (interface) · `Core/Systems/SystemRandom.cs` (production impl)
**Dependencies:** none.
```csharp
public interface IRandom
{
    int Next(int maxExclusive);
    int Next(int minInclusive, int maxExclusive);   // mirrors System.Random.Next(int,int)
    double NextDouble();                              // [0.0, 1.0)
}
```
`SystemRandom` wraps the thread-safe `Random.Shared`; registered as a DI singleton in `Server/CompositionRoot.cs`. `CombatSystem` is the first consumer (hit roll + damage rolls). Richer helpers (dice notation, weighted choice) layer on additively as consumers need them. Note: cryptographic randomness (`PasswordHasher`) stays on `RandomNumberGenerator` and is **not** routed through this seam — security RNG must never be made seedable. Implemented (testing-strategy effort).

### IClock / SystemClock
**Purpose:** Injectable time seam — the single source of wall-clock time for game logic, so time-dependent outcomes can be made deterministic in tests by substituting a fake (INV-26). Systems take `IClock` by constructor injection instead of reaching for `DateTime.UtcNow`. Production wiring binds `SystemClock`.
**Location:** `Core/Systems/IClock.cs` (interface) · `Core/Systems/SystemClock.cs` (production impl)
**Dependencies:** none.
```csharp
public interface IClock
{
    DateTime UtcNow { get; }
}
```
`SystemClock` wraps `DateTime.UtcNow`; registered as a DI singleton in `Server/CompositionRoot.cs`. `SpawnSystem` and `AccountSystem` are the primary consumers (respawn scheduling, account/character timestamps). Implemented (testing-harness-and-backfill WP-3).

---

## Domain / feature Systems

### WorldContentLoader
**Purpose:** Scans the configured content directory, registers authored YAML templates with `ITemplateRegistry`, and fresh-spawns room/area/item/mob entities on every startup. Wraps `LoadAndSpawnAsync` in a hosted-service shell (`Server/WorldContentBootstrap`) to enforce startup ordering after `PersistenceBootstrap`.
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
Empty/missing content directory → seeds a single hardcoded `room.void` and warns (host stays up for first-run authors). No `PersistentEntity` is added to any world-content entity (rooms, areas, items, mobs) — the YAML file is the sole durable state. `SpawnMissingEntities` skips blueprints already represented by a live entity (correct for `@reload`; no-op at cold start). Both `LocationComponent` fields (`RoomEntityId` + `RoomBlueprintId`) are set when placing items and mobs. `ReloadAsync` is **additive only**: refreshes the template registry and seeds missing entities; existing live entities are not mutated. Implemented (Phase 3 slices 2, persistence-reform-stage-b, 8).

### AreaSystem
**Purpose:** Domain system for area–room membership queries and mutation. Provides `GetRoomsInArea`, `GetAreaForRoom`, and `AssignRoomToArea`. All operations are pure ECS mutations; no event publication (INV-5). `AssignRoomToArea` sets `RoomComponent.AreaEntityId` on the live entity and mirrors `areaBlueprintId` to `RoomTemplate.AreaId` in the template registry so the assignment survives `@reload`.
**Location:** `Core/Modules/World/Systems/AreaSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`.
```csharp
public interface IAreaSystem
{
    IReadOnlyList<uint> GetRoomsInArea(uint areaEntityId);
    uint? GetAreaForRoom(uint roomEntityId);
    void AssignRoomToArea(uint roomEntityId, uint areaEntityId, string areaBlueprintId);
}
```
Registered as a singleton in `WorldModule.AddWorldModule`. Consumed by `RoomBuilderSystem` (when creating a room with an area) and `DigCommand` (inheriting the source room's area). Implemented (Phase 3, area-model WP-1).

### IAreaContentWriter
**Purpose:** Serializes an `AreaTemplate` to YAML at `{contentDirectory}/areas/{blueprintId}.yaml` using an atomic write (tmp → rename). Symmetric write path for `AreaTemplateDeserializer`. Called by admin commands that create area blueprint definitions.
**Location:** `Core/Modules/World/Systems/IAreaContentWriter.cs` (interface) · `Core/Modules/World/Systems/AreaContentWriter.cs` (implementation)
**Dependencies:** `IConfiguration`.
```csharp
public interface IAreaContentWriter
{
    Task WriteAsync(AreaTemplate template, CancellationToken ct = default);
}
```
Registered as a singleton in `WorldModule.AddWorldModule`. Consumed by `MkareaCommand` after `IAreaBuilderSystem.CreateArea` returns (INV-5: the system never calls persistence). Implemented (Phase 3 admin-area-authoring WP-1).

### AccountSystem
**Purpose:** Domain system owning all account and character lifecycle operations: registration, authentication, character creation, character list, and logout recording.
**Location:** `Core/Modules/Account/Systems/AccountSystem.cs`
**Dependencies:** `EntityService`, `IPasswordHasher`, `WorldConfiguration`.
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
Maintains lazy in-memory HashSet indices for username and character name uniqueness (populated on first call, updated on every write). `CreateAccountAsync` attaches `AccountComponent` + `PersistentEntity` and returns the entity id — persistence is the caller's (`LoginFlow`) responsibility (INV-5). `CreateCharacterAsync` attaches `CharacterComponent` + `LocationComponent` (set to `StartingRoomEntityId`) + `PersistentEntity`, registers the character on the account, and returns the entity id — `LoginFlow` saves character-first, then account (crash-safety ordering). `RecordLogout` updates `CharacterComponent.LastLoginUtc`; `PlayerSessionHandler` calls `SaveEntityAsync` after `RecordLogout` returns. Extended in slice 8a: `CreateCharacterAsync` also attaches `AttributesComponent { Level=1, Mind=10, Body=10, Spirit=10, Attunement=10 }` and `PoolsComponent { MaxHp=100, CurrentHp=100, MaxMana=50, CurrentMana=50, MaxStamina=50, CurrentStamina=50, MaxAstra=10, CurrentAstra=10 }` to every new character (attribute names updated to Mind/Body/Spirit/Attunement in slice 9-d). Implemented (Phase 3 slices 5, persistence-two-level-model, 8a, 9-d).

### RoomBuilderSystem
**Purpose:** Runtime room authoring — creates room entities, wires bidirectional exits, and mutates room properties (`Name`, `Description`). All methods return pure results or mutate in-place; event publication is the caller's responsibility, keeping this system reusable by a future in-game editor without a live player session.
**Location:** `Core/Modules/Admin/Systems/RoomBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `IAreaSystem`, `ILogger<RoomBuilderSystem>`.
```csharp
public interface IRoomBuilderSystem
{
    RoomCreationResult CreateRoom(string name, string description = "", string areaId = "");
    void LinkExits(uint sourceRoomId, Direction direction, uint targetRoomId, bool bidirectional);
    void SetRoomName(uint roomId, string name);
    void SetRoomDescription(uint roomId, string description);
}

public readonly record struct RoomCreationResult(uint RoomEntityId, string BlueprintId);
```
`CreateRoom` generates a unique blueprint id (`room.adhoc.<8-char-base36>`), creates the entity, attaches `RoomComponent` + `BlueprintComponent` (no `PersistentEntity`), and registers a `RoomTemplate`. When `areaId` is non-empty it sets `RoomTemplate.AreaId` and calls `IAreaSystem.AssignRoomToArea` to set `RoomComponent.AreaEntityId` immediately. `LinkExits` updates both `RoomComponent.Exits` and the in-memory `RoomTemplate` exit maps for same-session `reload` consistency. The `DigCommand` initiator writes YAML for both rooms after this method returns (INV-5: systems do not call persistence). Implemented (Phase 3 slices 5a, persistence-two-level-model, area-model WP-1).

### AreaBuilderSystem
**Purpose:** Runtime area authoring — creates ad-hoc area entities. Mirrors `IRoomBuilderSystem`: all methods mutate ECS state only; event publication and YAML writing remain in the command (INV-5). No `IAreaSystem` or `IEventBus` dependency.
**Location:** `Core/Modules/Admin/Systems/AreaBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<AreaBuilderSystem>`.
```csharp
public interface IAreaBuilderSystem
{
    AreaCreationResult CreateArea(string name);
}

public readonly record struct AreaCreationResult(
    uint AreaEntityId,
    string BlueprintId,
    AreaTemplate Template);
```
`CreateArea` generates a unique blueprint id (`area.adhoc.<8-char-base36>`), creates the entity, attaches `AreaComponent` + `BlueprintComponent` (no `PersistentEntity`), and registers a minimal `AreaTemplate` (empty description, RespawnRate=0, Pvp=false). The `MkareaCommand` initiator writes YAML after this method returns (INV-5). Implemented (Phase 3 admin-area-authoring WP-2).

### ItemSystem
**Purpose:** Query and mutation operations on item entities — finds items in a room or inventory by entity id, prefix-matches a token against item names and keywords, and moves items between ground and inventory. Mutation methods are pure ECS mutations; no event publication, no persistence calls.
**Location:** `Core/Modules/Items/Systems/ItemSystem.cs`
**Dependencies:** `EntityService`.
```csharp
public interface IItemSystem
{
    IReadOnlyList<uint> GetItemsInRoom(uint roomEntityId);
    IReadOnlyList<uint> GetItemsInInventory(uint holderEntityId);
    bool TryFindItemInRoom(uint roomEntityId, string token, out uint itemEntityId);
    bool TryFindItemInInventory(uint holderEntityId, string token, out uint itemEntityId);
    void MoveToInventory(uint itemEntityId, uint holderEntityId);
    void DropToRoom(uint itemEntityId, uint holderEntityId, uint roomEntityId);
}
```
`GetItemsInRoom` iterates all `ItemDataComponent` entities and returns those whose `LocationComponent.RoomEntityId` matches. `GetItemsInInventory` reads `InventoryComponent.ItemEntityIds` from the holder. `TryFindItemInRoom` / `TryFindItemInInventory` do a linear prefix-match against `ItemDataComponent.Name` and each keyword, returning the first match. `MoveToInventory` removes `LocationComponent` from the item and appends its id to the holder's `InventoryComponent`; no-ops if the item has no `LocationComponent` (race condition: already picked up). `DropToRoom` removes the item id from `InventoryComponent` and attaches a `LocationComponent` pointing to the given room. Implemented (Phase 3 slice 6).

### ItemBuilderSystem
**Purpose:** Runtime item authoring — creates ad-hoc item entities and mutates item properties (`Name`, `Description`, `Keywords`, `ItemType`, `WornSlots`). Mirrors `IRoomBuilderSystem`: all methods mutate ECS state only; event publication and persistence calls remain in the command (INV-5). Reusable by a future in-game editor without a live player session.
**Location:** `Core/Modules/Items/Systems/ItemBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<ItemBuilderSystem>`.
```csharp
public interface IItemBuilderSystem
{
    ItemCreationResult CreateItem(string name, uint roomEntityId);
    void SetItemName(uint itemEntityId, string name);
    void SetItemDescription(uint itemEntityId, string description);
    void SetItemKeywords(uint itemEntityId, IReadOnlyList<string> keywords);
    void SetItemType(uint itemEntityId, ItemType itemType);
    void SetItemSlots(uint itemEntityId, IReadOnlyList<WornSlot> slots);
    void SetItemDamageBonus(uint itemEntityId, int value);
}

public readonly record struct ItemCreationResult(uint ItemEntityId, string BlueprintId, ItemTemplate Template);
```
`CreateItem` generates a unique blueprint id (`item.adhoc.<8-char-base36>`), creates the entity, attaches `ItemDataComponent` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent { RoomEntityId }`, and registers a minimal `ItemTemplate`. The `MkitemCommand` initiator calls `SaveEntityAsync` after this method returns (INV-5). `SetItemSlots` updates both `ItemDataComponent.WornSlots` and `ItemTemplate.WornSlots` in the registry. `SetItemDamageBonus` updates both `ItemDataComponent.DamageBonus` and `ItemTemplate.DamageBonus`; called by `SetitemCommand` for the `dmg` property. Implemented (Phase 3 slices 6, 7, 9-c).

### EquipmentSystem
**Purpose:** Query and mutation operations on character equipment slots — finds equipped items, prefix-matches tokens against worn item names/keywords, equips items from inventory into their declared slots (with implicit displacement of existing occupants), and removes items from slots back to inventory. All methods are pure ECS mutations; no event publication, no persistence calls.
**Location:** `Core/Modules/Items/Systems/EquipmentSystem.cs`
**Dependencies:** `EntityService`.
```csharp
public interface IEquipmentSystem
{
    IReadOnlyList<WornSlot> GetWornSlots(uint itemEntityId);
    IReadOnlyList<uint> GetEquippedItems(uint characterEntityId);
    bool TryFindEquippedItem(uint characterEntityId, string token, out uint itemEntityId);
    void EquipItem(uint characterEntityId, uint itemEntityId);
    void RemoveItem(uint characterEntityId, uint itemEntityId);
    void RemoveFromSlot(uint characterEntityId, WornSlot slot);
}
```
`EquipItem` internally performs the implicit-remove pass: for each slot declared on the item, if the slot is occupied it calls `RemoveFromSlot` to silently return the displaced item to inventory before placing the new item. `WearCommand` calls only `EquipItem` — never a loop over slots. Implemented (Phase 3 slice 7).

### MobBuilderSystem
**Purpose:** Runtime mob authoring — creates ad-hoc mob entities and mutates mob properties (`Name`, `Description`, `Keywords`, `MobType`). Mirrors `IItemBuilderSystem`: all methods mutate ECS state only; event publication and persistence calls remain in the command (INV-5).
**Location:** `Core/Modules/Mobs/Systems/MobBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<MobBuilderSystem>`.
```csharp
public interface IMobBuilderSystem
{
    MobCreationResult CreateMob(string name, uint roomEntityId);
    void SetMobName(uint mobEntityId, string name);
    void SetMobDescription(uint mobEntityId, string description);
    void SetMobKeywords(uint mobEntityId, IReadOnlyList<string> keywords);
    void SetMobType(uint mobEntityId, MobType mobType);
    void SetAttribute(uint mobEntityId, MobTemplate template, string property, int value);
}

public readonly record struct MobCreationResult(uint MobEntityId, string BlueprintId, MobTemplate Template);
```
`CreateMob` generates a unique blueprint id (`mob.adhoc.<8-char-base36>`), creates the entity, attaches `MobDataComponent` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent { RoomEntityId }`, and registers a minimal `MobTemplate`. Implemented (Phase 3 slice 8). Extended in slice 8a: `SetAttribute(mobEntityId, template, property, value)` mutates `AttributesComponent`/`PoolsComponent` on the live entity and updates the template. Valid properties: `level`, `hp`, `mind`, `body`, `spirit`, `attunement`, `maxmana`, `maxstamina`, `maxastra`. Enforces `CurrentX ≤ MaxX` clamp on pool max changes (INV-8). Does not call persistence or events (INV-5). Updated `str`/`dex`/`con` to `mind`/`body`/`spirit`/`attunement` in slice 9-d.

### MobContentWriter
**Purpose:** Serializes a `MobTemplate` to YAML at `{contentDirectory}/mobs/{blueprintId}.yaml` using an atomic write (tmp → rename). Mirrors `IItemContentWriter`.
**Location:** `Core/Modules/Mobs/Systems/MobContentWriter.cs`
**Dependencies:** `IConfiguration`.
```csharp
public interface IMobContentWriter
{
    Task WriteAsync(MobTemplate template, CancellationToken ct = default);
}
```
YAML DTO fields: `blueprintId`, `name`, `description`, `keywords`, `type` (string enum value), `spawnRoomBlueprintId`. Implemented (Phase 3 slice 8). Extended in slice 8a: DTO includes `level`, `maxHp`, and attribute fields. Updated in slice 9-d: attribute fields are now `mind`, `body`, `spirit`, `attunement`; added `maxMana`, `maxStamina`, `maxAstra` pool fields.

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

### AttributeSystem
**Purpose:** Read/write seam for `AttributesComponent` and `PoolsComponent`. Getters are the surface the combat slice will call; setters serve the admin and initialization paths. Never touches the event bus or persistence (INV-5).
**Location:** `Core/Modules/Attributes/Systems/AttributeSystem.cs`
**Dependencies:** `EntityService`.
```csharp
public interface IAttributeSystem
{
    int GetLevel(uint entityId);
    int GetMind(uint entityId);
    int GetBody(uint entityId);
    int GetSpirit(uint entityId);
    int GetAttunement(uint entityId);
    int GetMaxHp(uint entityId);
    int GetCurrentHp(uint entityId);
    int GetMaxMana(uint entityId);
    int GetCurrentMana(uint entityId);
    int GetMaxStamina(uint entityId);
    int GetCurrentStamina(uint entityId);
    int GetMaxAstra(uint entityId);
    int GetCurrentAstra(uint entityId);

    void SetLevel(uint entityId, int value);
    void SetMind(uint entityId, int value);
    void SetBody(uint entityId, int value);
    void SetSpirit(uint entityId, int value);
    void SetAttunement(uint entityId, int value);
    /// Sets MaxHp and clamps CurrentHp to the new MaxHp if it would exceed it (INV-8).
    void SetMaxHp(uint entityId, int value);
    /// Sets CurrentHp, clamped to [0, MaxHp]. Game rule enforced here (INV-8). No events, no persistence (INV-5).
    void SetCurrentHp(uint entityId, int value);
    void SetMaxMana(uint entityId, int value);
    void SetCurrentMana(uint entityId, int value);
    void SetMaxStamina(uint entityId, int value);
    void SetCurrentStamina(uint entityId, int value);
    void SetMaxAstra(uint entityId, int value);
    void SetCurrentAstra(uint entityId, int value);
}
```
All getters return the default value (Level 1, attributes 10, HP/Mana/Stamina 100/50/50, Astra 10) if the entity lacks the relevant component — safe default for pre-hydration edge cases. `SetCurrentHp` is the write seam the combat slice uses to apply damage; callers pass a raw new value and the clamping invariant is enforced here. Replaced `Strength`/`Dexterity`/`Constitution` with `Mind`/`Body`/`Spirit`/`Attunement` in slice 9-d (WP-1). Added Mana/Stamina/Astra pool getters/setters in the same slice. Implemented (Phase 3 slices 8a, 9-c, 9-d).

### EntityStateService
**Purpose:** Centralized transition-rule enforcement for entity state flags. Attaches and removes `EntityStateComponent`; validates flag combinations against a static transition table; returns structured failure reasons to callers. Never touches the event bus or persistence (INV-5).
**Location:** `Core/Modules/EntityState/Systems/EntityStateService.cs`
**Dependencies:** `EntityService`.
```csharp
public interface IEntityStateService
{
    bool TryEnterState(uint entityId, EntityStateFlags state, out string? failReason);
    void ExitState(uint entityId, EntityStateFlags state);
    bool IsInState(uint entityId, EntityStateFlags state);
    EntityStateFlags GetStates(uint entityId);
}
```
`TryEnterState` reads current `ActiveStates` (or `None` if the component is absent), evaluates the static transition-rule table, on success attaches or OR-assigns the flag, and returns `true`. On a blocked transition it returns `false` with a caller-displayable `failReason`. `ExitState` AND-NOT clears the flag and removes the component when `ActiveStates == None` — always a no-op when the entity has no component. `IsInState` delegates to `GetStates`. Callers (commands, handlers) publish `EntityStateChangedEvent` after mutating state; the service never calls `IEventBus` (INV-5). Implemented (Phase 3 slice 9-a).

### StatSystem
**Purpose:** Aggregation seam for effective entity stats. Reads base attributes, equipment bonuses, and active effect modifiers to produce ready-to-use values for the combat slice and future consumers. Never publishes events or calls persistence (INV-5). Adding a new modifier source means extending `StatSystem` methods, not changing the interface.
**Location:** `Core/Modules/Stats/Systems/StatSystem.cs`
**Dependencies:** `IAttributeSystem`, `EntityService`.
```csharp
public interface IStatSystem
{
    int GetEffectiveMind(uint entityId);
    int GetEffectiveBody(uint entityId);
    int GetEffectiveSpirit(uint entityId);
    int GetEffectiveAttunement(uint entityId);
    /// Body / 2 + MainHand item DamageBonus (0 if no weapon or no bonus).
    int GetEffectiveAttackPower(uint entityId);
    /// Body / 4. Armor-slot bonus deferred to a future slice.
    int GetEffectiveDefense(uint entityId);
    int GetCurrentHp(uint entityId);
    int GetMaxHp(uint entityId);
    /// Generic stat getter by ScoreId — expandable seam for future effect/buff consumers.
    int Get(uint entityId, ScoreId scoreId);
}
```
`GetEffectiveAttackPower` reads `EquipmentComponent.Slots[WornSlot.MainHand]` via `EntityService.TryGet<EquipmentComponent>` (direct dictionary lookup, not a list scan) then reads `ItemDataComponent.DamageBonus` on the equipped item — no `is`/`as` casts (INV-4). `GetEffectiveDefense` returns `Body / 4`; armor-slot contribution is acknowledged future debt. `Get(uint, ScoreId)` is the expandable enum-keyed seam used by effect/ability consumers; it folds `IEffectSystem.GetModifiers(entityId, scoreId)` into the returned value for `StatModifier`-kind effects. Registered in `StatsModule` (`Core/Modules/Stats/StatsModule.cs`) as a singleton. `IStatRegistry` (singleton, `Core/Modules/Stats/StatRegistry.cs`) records pool governance metadata (Mana↔Mind, Stamina↔Body, Astra↔Attunement). Updated `Strength`/`Dexterity`/`Constitution` references to `Mind`/`Body`/`Spirit`/`Attunement` in slice 9-d. Extended in slice 9-e: `Get` folds `IEffectSystem.GetModifiers`. Implemented (Phase 3 slices 9-c, 9-d, 9-e).

### EffectSystem
**Purpose:** Core system that manages active effects on entities. Applies/removes individual effects or entire categories; returns active effect lists and per-`ScoreId` stat modifier sums; advances time on each tick, collecting expired and periodic-due effects. Never touches the event bus (INV-5).
**Location:** `Core/Modules/Effects/Systems/EffectSystem.cs` (implementation) · `Core/Modules/Effects/Systems/IEffectSystem.cs` (interface)
**Dependencies:** `EntityService`.
```csharp
public interface IEffectSystem
{
    Effect? Apply(uint targetEntityId, EffectDefinition definition, uint sourceEntityId);
    void Remove(uint entityId, string effectId);
    void RemoveByCategory(uint entityId, EffectCategory category);
    IReadOnlyList<Effect> GetActive(uint entityId);
    int GetModifiers(uint entityId, ScoreId scoreId);
    EffectTickResult AdvanceTick(TimeSpan elapsed);
}
```
`Apply` returns `null` when `StackPolicy.HighestWins` blocks application (existing effect has equal or greater power). `AdvanceTick` advances elapsed time on timed effects, removes expired ones, and returns `EffectTickResult { DueApplications, Expired }` sorted by `EffectPhase` (Early → Normal → Late). Injects `IEnumerable<IEffectContributor>`; `GetModifiers`/`GetActive` sum stored effects **plus** all registered contributors (INV-24 seam, slice 11-a). Registered via `AddEffectsModule()`. Implemented (Phase 3 slices 9-e, 11-a).

### EffectRegistry
**Purpose:** Hardcoded read-only catalog of starter `EffectDefinition` records. Pure data — no event bus, no persistence. Now a `DefinitionRegistry<string, EffectDefinition>` subclass (WP-1 retrofit). Promotion to a data file is deferred per the use-case spec (Category-3 balance data).
**Location:** `Core/Modules/Effects/EffectRegistry.cs` (implementation) · interface `IEffectRegistry : IRegistry<string, EffectDefinition>` in the same file.
**Dependencies:** none.
```csharp
public interface IEffectRegistry : IRegistry<string, EffectDefinition> { }
```
Registered entries: `empower` (Body +5, Buff, 30s, HighestWins), `weaken` (Body -5, Debuff, 30s, HighestWins), `regen` (HpCurrent +10/tick, Blessing, 60s, Stack, Early), `poison` (HpCurrent -8/tick, Poison, 30s, Stack, Late), `minor_curse` (Mind -3, Curse, permanent, Stack). Extended in slice 11-a: `kick_damage` (HpCurrent -15, Instant, Replace), `mend_heal` (HpCurrent +20, Instant, Replace), `toughness_passive` (HpMax +20, StatModifier, HighestWins). Registered via `AddEffectsModule()`. Implemented (Phase 3 slice 9-e; extended 11-a; retrofitted 11-d).

### CombatSystem
**Purpose:** Domain system for combat resolution. Handles target lookup, combat state attachment/removal, round resolution, and ability-powered strikes. Pure: no events, no persistence (INV-5, INV-8). Computes attack resolution via `IStatSystem`; applies aspect resolution via `IAspectSystem.Resolve`; mutates HP via `IAttributeSystem.SetCurrentHp`; returns structured `CombatRoundResult` to callers.
**Location:** `Core/Modules/Combat/Systems/CombatSystem.cs`
**Dependencies:** `EntityService`, `IStatSystem`, `IAttributeSystem`, `IAspectSystem`.
```csharp
public interface ICombatSystem
{
    bool TryFindTargetInRoom(uint roomEntityId, string token, out uint mobEntityId);
    void StartCombat(uint attackerEntityId, uint defenderEntityId);
    void EndCombat(uint attackerEntityId, uint defenderEntityId);
    CombatRoundResult ExecuteRound(uint attackerEntityId, uint defenderEntityId);
    CombatRoundResult ResolveAbilityStrike(
        uint attackerEntityId, uint defenderEntityId, int basePower,
        AspectComposition? composition = null);
}

public readonly record struct CombatRoundResult(
    uint AttackerEntityId,
    uint DefenderEntityId,
    int DamageDealt,
    bool AttackerHit,
    CombatRoundOutcome Outcome,
    AspectComposition? AspectComposition = null);  // point-in-time capture (INV-6)

public enum CombatRoundOutcome { Hit, Miss, MobDied, PlayerIncapacitated }
```
`TryFindTargetInRoom` prefix-matches `token` against `MobDataComponent.Name` and `Keywords`. `StartCombat`/`EndCombat` add/remove `CombatStateComponent`. `ExecuteRound`: hit check; raw damage; composition source = `IAspectSystem.Affinity(attacker)` (entity identity, empty = untyped); `IAspectSystem.Resolve` applies affinity boost + resist; `SetCurrentHp`. `ResolveAbilityStrike` skips hit/miss; composition source = the ability's `Aspect` field passed by caller (`AbilityInvocationPipeline`). `CombatRoundResult.AspectComposition` is null when the composition was empty (null = untyped, matching `CombatEndedEvent.DefenderName` INV-6 pattern). Implemented (Phase 3 slices 9, 11-a; aspect-resolved 11-d).

### DeathSystem
**Purpose:** Domain system owning the HP-threshold evaluation, respawn mutation, and respawn-location management for the player death lifecycle. Pure: never touches the event bus or persistence (INV-5, INV-8). Callers (handlers, initiators) read the returned `DeathTransition` and publish the appropriate events.
**Location:** `Core/Modules/Death/Systems/DeathSystem.cs` (implementation) · `Core/Modules/Death/Systems/IDeathSystem.cs` (interface)
**Dependencies:** `EntityService`, `IEntityStateService`, `IAttributeSystem`, `IEffectSystem`, `ITemplateRegistry`, `WorldConfiguration`, `IConfiguration`, `ILogger<DeathSystem>`.
```csharp
public interface IDeathSystem
{
    /// <summary>Evaluates HP-threshold crossings after an HP mutation. Returns BecameIncapacitated,
    /// Died, or None.</summary>
    DeathTransition OnHpChanged(uint entityId, int previousHp, int newHp);

    /// <summary>Exits Incapacitated state, relocates to respawn room, strips impermanent effects,
    /// and restores all pools to Death:RespawnPoolPercent of their maxima.</summary>
    void Respawn(uint entityId);

    /// <summary>Validates the blueprint exists and sets RespawnComponent.RoomBlueprintId.
    /// Returns false + failReason when the blueprint is not found.</summary>
    bool SetRespawn(uint entityId, string roomBlueprintId, out string? failReason);
}
```
`OnHpChanged` only applies to entities with `CharacterComponent` — mobs never enter the death pipeline. Configuration: `Death:HpFloor` (default `-10`), `Death:RespawnPoolPercent` (default `0.25`). Registered via `AddDeathModule()`. Implemented (Phase 3 slice 10).

### AbilitySystem
**Purpose:** Domain system managing the full ability lifecycle for players and mobs. Handles learn/teach, multi-cost atomic activation (resolve ability → entity state/cooldown/cost checks → spend costs → apply effects → set cooldown), per-ability cooldown tracking, and batch cooldown advancement on each heartbeat tick.
**Location:** `Core/Modules/Abilities/Systems/AbilitySystem.cs` (implementation) · `Core/Modules/Abilities/Systems/IAbilitySystem.cs` (interface)
**Dependencies:** `EntityService`, `IAbilityRegistry`, `IEffectSystem`, `IAttributeSystem`, `IEntityStateService`.
```csharp
public interface IAbilitySystem
{
    AbilityActivationResult Activate(uint actorEntityId, string abilityId,
        uint? targetEntityId = null, bool resolveOffensiveExternally = false);
    bool IsOffensive(string abilityId);
    bool Learn(uint entityId, string abilityId);
    bool Teach(uint teacherEntityId, uint studentEntityId, string abilityId);
    IReadOnlyList<string> GetKnown(uint entityId);
    bool IsKnown(uint entityId, string abilityId);
    float GetCooldownRemaining(uint entityId, string abilityId);
    IReadOnlyList<(string AbilityId, float CooldownRemaining)> GetCooldowns(uint entityId);
    void AdvanceCooldowns(TimeSpan elapsed);
}
```
`Activate` validates in order: ability exists → actor knows it → `Active` activation → entity state ok (not Incapacitated) → cooldown ready → all costs affordable (atomic check before any spend). On success: spends each cost via `IAttributeSystem`, sets `AbilitiesComponent.CooldownRemaining[abilityId] = CooldownSeconds`, and calls `IEffectSystem.Apply` per effect id. When `resolveOffensiveExternally = true`, any offensive damage effect (Instant/Periodic, `TargetScore == HpCurrent`, `BaseMagnitude < 0`) is skipped by `IEffectSystem` and its raw magnitude is returned as `AbilityActivationResult.OffensivePower` instead — the caller (`AbilityInvocationPipeline`) applies it via `ICombatSystem.ResolveAbilityStrike` with defense mitigation. `IsOffensive` returns `true` if the ability has `Targeting.Target` and at least one offensive damage effect. Returns `AbilityActivationResult { Outcome, AbilityId, AppliedEffects, Spent, CooldownSeconds, FailReason?, OffensivePower? }`. `AdvanceCooldowns` decrements all non-zero cooldown entries by `elapsed.TotalSeconds`, clamping to 0. Registered via `AddAbilitiesModule()`. Implemented (Phase 3 slices 11-a, 11-b).

### AspectRegistry
**Purpose:** Hardcoded read-only catalog of `AspectDefinition` records. Born on `DefinitionRegistry<AspectId, AspectDefinition>` (the fourth consumer that anchored the generic extraction). Pure data — no event bus, no persistence. Aspected abilities reference `AspectId` keys validated at startup by `RegistryValidationBootstrap`.
**Location:** `Core/Modules/Aspects/AspectRegistry.cs` (implementation · interface `IAspectRegistry` in same file)
**Dependencies:** none.
```csharp
public interface IAspectRegistry : IRegistry<AspectId, AspectDefinition> { }
```
Starter vocabulary: `Fire`, `Ice`, `Lightning` (Elemental); `Nature` (Primal); `Void`, `Light` (Arcane). Registered via `AddAspectsModule()`. Implemented (Phase 3 slice 11-d).

### AspectSystem / IAspectSystem
**Purpose:** Core system: generic aspect math with no game-semantic branching (no FireSystem, no per-aspect switch). Three responsibilities: `Resolve` (apply affinity boost + independent resist); `Affinity` (entity's outgoing composition); `Resist` (entity's effective resistance to one aspect, compute-on-read INV-24). Pure: no events, no persistence, no game rules (INV-2, INV-5).
**Location:** `Core/Modules/Aspects/Systems/AspectSystem.cs` · `Core/Modules/Aspects/Systems/IAspectSystem.cs`
**Dependencies:** `EntityService`.
```csharp
public interface IAspectSystem
{
    // Formula per aspect A: portion = magnitude * weight/100;
    // boosted = portion * (1 + attackerAffinityWeight_A / 100);
    // resisted = boosted * (1 - resist_A / 100). Sum across all aspects, clamp to [0, int.Max].
    int Resolve(int magnitude, AspectComposition composition, uint attackerEntityId, uint defenderEntityId);
    AspectComposition Affinity(uint entityId);
    int Resist(uint entityId, AspectId aspect);   // [0, 100]; 100 = full immunity
}
```
Registered via `AddAspectsModule()`. Composed by `CombatSystem` (WP-3): called in both `ExecuteRound` (melee affinity) and `ResolveAbilityStrike` (ability `Aspect` field). Implemented (Phase 3 slice 11-d).

### AbilityRegistry
**Purpose:** Hardcoded read-only catalog of `AbilityDefinition` records. Pure data — no event bus, no persistence. Now a `DefinitionRegistry<string, AbilityDefinition>` subclass (WP-1 retrofit). Promotion to a data file is a future content concern.
**Location:** `Core/Modules/Abilities/AbilityRegistry.cs` (implementation · interface `IAbilityRegistry : IRegistry<string, AbilityDefinition>` in same file)
**Dependencies:** none.
```csharp
public interface IAbilityRegistry : IRegistry<string, AbilityDefinition> { }
```
Starter set: `toughness` (Skill/Passive/Self — `toughness_passive` effect), `kick` (Skill/Active/Target — 10 stamina, `kick_damage`), `empower` (Spell/Active/Self — 10 mana, `empower`), `mend` (Spell/Active/Self — 15 mana, `mend_heal`), `blood_pact` (Spell/Active/Self — 10 hp + 15 mana, `empower`). `AbilityDefinition.Aspect` is now `AspectComposition?` (migrated from `string?` stub). Registered via `AddAbilitiesModule()`. Implemented (Phase 3 slice 11-a; retrofitted 11-d).

### AbilityEffectContributor
**Purpose:** Implements the `IEffectContributor` seam (INV-24). Derives `WhileKnown` passive ability effects into `EffectSystem.GetModifiers`/`GetActive` at read time. Each tick of `GetModifiers`, this contributor returns the stat modifiers implied by any `Passive` abilities the entity knows and that have `WhileKnown` effects in `IEffectRegistry`. No domain types are referenced from core; the adapter owns the translation.
**Location:** `Core/Modules/Abilities/AbilityEffectContributor.cs`
**Dependencies:** `EntityService`, `IAbilityRegistry`, `IEffectRegistry`.
```csharp
// Implements: Core/Modules/Effects/Systems/IEffectContributor.cs
public interface IEffectContributor
{
    int GetModifiers(uint entityId, ScoreId scoreId);
    IEnumerable<Effect> GetActive(uint entityId);
}
```
Registered via `AddAbilitiesModule()` as `IEffectContributor` (DI-collected by `EffectSystem`). Implemented (Phase 3 slice 11-a).

---

## Argument Resolvers

Resolvers implement `IArgumentResolver` and are injected into `CommandArgument` schema entries. They return a candidate list for prefix matching at parse time — read-only, no events, no mutations (INV-5).

### AbilityVerbResolver

**Purpose:** Resolves a typed input verb against all known Active Skills of the invoking player. Used by `CommandDispatcher` Phase 3 to detect bare skill invocations (e.g. `kick`, `ki`) that don't match any registered command.
**Location:** `Core/Modules/Abilities/AbilityVerbResolver.cs`
**Interface:** `IAbilityVerbResolver` (same file)
**Dependencies:** `IAbilitySystem`, `IAbilityRegistry`.
```csharp
public interface IAbilityVerbResolver
{
    bool TryResolve(uint actorEntityId, string verbToken, out string abilityId);
    IReadOnlyList<string> GetInvocableVerbs(uint actorEntityId);
}
```
Registered via `AddAbilitiesModule()`. Implemented (Phase 3 slice 11-b / WP-2).

### KnownSpellResolver

**Purpose:** Resolves a spell name/id token against the invoker's known Active Spells for use in the `cast` command. Implements `IArgumentResolver`.
**Location:** `Core/Modules/Abilities/Resolvers/KnownSpellResolver.cs`
**Dependencies:** `IAbilitySystem`, `IAbilityRegistry`.

Returns two `ResolvedCandidate` entries per known Active Spell — one for the ability id and one for the display name — both sharing the same canonical value (the ability id). The parser prefix-matches the player's input against these candidates and substitutes the ability id into the `"spell"` argument slot. Registered via `AddAbilitiesModule()`. Implemented (Phase 3 slice 11-b / WP-3).

### MobInRoomResolver

**Purpose:** Resolves a mob name/keyword against entities with `MobDataComponent` in the invoker's current room. Returns the mob entity id (as `string`) as the canonical value so commands receive the entity id directly without a second lookup.
**Location:** `Core/Modules/Combat/Resolvers/MobInRoomResolver.cs`
**Dependencies:** `EntityService`.
Registered as a singleton in `CombatModule`. **Not yet wired to any command argument schema** — `AbilityInvocationPipeline` and `KillCommand` currently call `ICombatSystem.TryFindTargetInRoom` inline. Migrate both call sites to this resolver when a third mob-targeting command argument is added (INV-19 ≥3-consumer threshold). Implemented (Phase 3 slice 11-b / WP-1).

---

### SpawnSystem
**Purpose:** Tracks spawn slot occupancy for world-content entities (mobs, world-spawn items) and schedules respawns. Self-initializes from the live entity graph on `WorldContentReadyEvent`; reacts to `MobDiedEvent` and `ItemPickedUpEvent` to mark slots vacant; respawns on `HeartbeatTickEvent`. No events published; no persistence (INV-5, INV-8). Implemented (persistence reform Stage C).
**Location:** `Core/Modules/Spawn/Systems/SpawnSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<SpawnSystem>`.
**Subscribes to:** `WorldContentReadyEvent` (priority 80), `MobDiedEvent` (priority 20), `ItemPickedUpEvent` (priority 20), `HeartbeatTickEvent` (priority 95).

Internal state:
- `_slots: Dictionary<(roomEntityId, blueprintId), SlotState>` — slot registry; keyed by the owning room entity and the mob/item blueprint ID.
- `_entityToSlot: Dictionary<entityId, (roomEntityId, blueprintId)>` — reverse map for O(1) vacancy marking on death/pickup events.

On `WorldContentReadyEvent`: iterates all entities with `SpawnConfigComponent`; for each spawn rule, finds any live entity in that room with the matching blueprint ID and registers it in the tracker. Slots with no live entity get `RespawnAt = now + delay` for immediate respawn on the first heartbeat.

On `MobDiedEvent` / `ItemPickedUpEvent`: removes the entity from the reverse map, sets `SlotState.LiveEntityId = null` and `SlotState.RespawnAt = now + RespawnDelaySeconds`.

On `HeartbeatTickEvent`: for each slot with `RespawnAt <= UtcNow`, calls `ITemplateRegistry.Spawn(blueprintId)`, attaches `LocationComponent`, and updates the tracker.

---

### RegenerationSystem
**Purpose:** Applies baseline out-of-combat resource regeneration to all entities with a `PoolsComponent` on each heartbeat tick. State-based rate: `InCombat` → suppressed entirely; `Resting` → `+RegenAmount` every tick; idle (neither) → `+RegenAmount` every `IdleIntervalTicks`-th tick (global `tickId % IdleIntervalTicks` cadence — no per-entity timer needed). Writes deltas through `IAttributeSystem`'s clamped pool setters (HP/Mana/Stamina/Astra). Never publishes events or touches persistence (INV-5). Implemented (slice 11-c).
**Location:** `Core/Modules/Regeneration/Systems/RegenerationSystem.cs`
**Dependencies:** `EntityService`, `IEntityStateService`, `IAttributeSystem`.
**Constants (Category-3):** `RegenAmount = 1`, `IdleIntervalTicks = 3`. Promotion to configuration deferred to the dedicated regeneration use-case.
### PromptComposerSystem
**Purpose:** Domain-aware implementation of `IPromptSource`. Reads entity state and resource pools on each buffer flush to build a fresh `PromptMessage` (compute-on-read — no cache, no dirty flag, no `PromptChangedEvent`). Lives in `Core/Modules/Prompt/` rather than `Core/Output/` because it depends on domain types; it is joined to the core buffer through the core-owned `IPromptSource` port (INV-2, INV-24).
**Interface:** `IPromptSource` (located at `Core/Output/IPromptSource.cs`)
**Location:** `Core/Modules/Prompt/Systems/PromptComposerSystem.cs`
**Dependencies:** `IEntityStateService`, `IStatSystem`.

Logic on each `GetPrompt(playerEntityId)` call:
1. Returns `null` when `playerEntityId == 0` (unbound session).
2. Calls `IEntityStateService.GetStates(entityId)` — maps flags to a state label with Incapacitated taking priority over InCombat over Resting; no label when no flags are set.
3. For each pool pair `{HpCurrent/HpMax, ManaCurrent/ManaMax, StaminaCurrent/StaminaMax, AstraCurrent/AstraMax}`: calls `IStatSystem.Get(entityId, scoreId)` for current and max; skips the pool if `max == 0`.
4. Returns `new PromptMessage(stateLabel, pools)`.

Registered as `services.AddSingleton<IPromptSource, PromptComposerSystem>()` in `Server/Program.cs` (replaces the WP-A `NullPromptSource` stub). Implemented (Phase 3, prompt-and-output-batching WP-B).

---

## Background Services / Initiators

Initiators drive the tick loop or startup; they are not "systems" in the domain-logic sense but are catalogued here because they publish events that domain handlers subscribe to.

### RegistryValidationBootstrap
**Purpose:** Startup Initiator (hosted service) that runs a fail-fast referential-integrity sweep after registries are populated and world content is ready. Validates: ability→effect cross-refs; ability→aspect cross-refs; `AspectComposition` normalization (empty or sums to 100); `CharacterDefaults:StartingAbilities` config → ability cross-refs. On failure: logs a full report and throws, aborting boot (INV-10, fail-fast). Publishes nothing — closed mechanical sweep (INV-10).
**Location:** `Server/RegistryValidationBootstrap.cs`
**Dependencies:** `IAbilityRegistry`, `IEffectRegistry`, `IAspectRegistry`, `IConfiguration`, `ILogger`.
**Startup ordering:** Registered after `WorldContentBootstrap` (registries are populated at DI-construction time, but the ordering guarantees world content spawning is complete before the sweep). Implemented (Phase 3 slice 11-d).

### HeartbeatBackgroundService (TimeModule)
**Purpose:** Shared game clock. Fires a `PeriodicTimer` at `Heartbeat:IntervalMs` (default 2000 ms) and publishes `HeartbeatTickEvent { TickId, Timestamp, Elapsed }` on each tick. No game logic — downstream handlers (combat, mob AI, effect expiry) subscribe independently. The `TimeModule` (`Core/Modules/Time/TimeModule.cs`) is the Core-side anchor; the hosted-service registration lives in `Server/Program.cs`.
**Location:** `Server/HeartbeatBackgroundService.cs` (service) · `Core/Modules/Time/Events/HeartbeatTickEvent.cs` (event) · `Core/Modules/Time/TimeModule.cs` (DI module)
**Dependencies:** `IEventBus`, `IConfiguration`, `ILogger<HeartbeatBackgroundService>`.
**Configuration:** `Heartbeat:IntervalMs` (Category 1 operational key, default 2000). `TickId` starts at 1; `TickId = 0` is an unambiguous "no tick has fired" sentinel.
**Startup ordering:** Registered last in the hosted-service queue (after `TelnetServer`) so the first tick cannot land before the world is fully seeded. `PeriodicTimer` does not drift on handler overrun — if a tick's handlers exceed the interval, the next tick fires immediately after completion.
Implemented (Phase 3 slice 9-b).
