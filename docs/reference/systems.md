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
**Purpose:** Save and load entity state using the two-level model: an entity is written only if it carries `PersistentEntity`; among its components, only those tagged `[Persistent]` are included in the snapshot.
**Location:** `Core/Systems/PersistenceSystem.cs`
**Dependencies:** `EntityService`, `IComponentTypeRegistry`, `IComponentSerializer`, `IConfiguration`, `ILogger<PersistenceSystem>`. No `IEventBus` dependency — all event publishing is the caller's responsibility.
```csharp
public interface IPersistenceSystem
{
    Task SaveEntityAsync(uint entityId, CancellationToken ct = default);
    Task<IReadOnlyList<uint>> LoadAllAsync(CancellationToken ct = default);
    Task FlushActivePlayerFootprintAsync(IEnumerable<uint> occupiedRoomIds, CancellationToken ct = default);
    Task FlushAllPersistentAsync(CancellationToken ct = default);
}
```
Entity files: `data/entities/entity-{id}.json`. Atomic write (`.tmp` → rename). `SaveEntityAsync` is the save-on-change path (admin commands, lifecycle transitions). `FlushActivePlayerFootprintAsync` is called by `PersistenceFlushTimer` on each tick — writes all `PersistentEntity`-carrying entities in rooms occupied by at least one player. `FlushAllPersistentAsync` is called by `PersistenceBootstrap.StopAsync` for a full shutdown sweep. Implemented (Phase 3 slices 1, persistence-two-level-model).

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

---

## Domain / feature Systems

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
Empty/missing content directory → seeds a single hardcoded `room.void` and warns (host stays up for first-run authors). `ReloadAsync` is **additive only**: refreshes the template registry and seeds missing entities; existing live entities are not mutated. Every entity spawned from YAML content receives a `PersistentEntity` component so it survives restart. Extended in slice 8 to load `kind: mob` files from `mobs/` subdirectory and call `PlaceMobsInRooms` (mirrors `PlaceItemsInRooms`). Implemented (Phase 3 slices 2, persistence-two-level-model, 8).

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
Maintains lazy in-memory HashSet indices for username and character name uniqueness (populated on first call, updated on every write). `CreateAccountAsync` attaches `AccountComponent` + `PersistentEntity` and returns the entity id — persistence is the caller's (`LoginFlow`) responsibility (INV-5). `CreateCharacterAsync` attaches `CharacterComponent` + `LocationComponent` (set to `StartingRoomEntityId`) + `PersistentEntity`, registers the character on the account, and returns the entity id — `LoginFlow` saves character-first, then account (crash-safety ordering). `RecordLogout` updates `CharacterComponent.LastLoginUtc`; `PlayerSessionHandler` calls `SaveEntityAsync` after `RecordLogout` returns. Extended in slice 8a: `CreateCharacterAsync` also attaches `AttributesComponent { Level=1, Strength=10, Dexterity=10, Constitution=10 }` and `PoolsComponent { MaxHp=100, CurrentHp=100 }` to every new character. Implemented (Phase 3 slices 5, persistence-two-level-model, 8a).

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
`CreateRoom` generates a unique blueprint id (`room.adhoc.<8-char-base36>`), creates the entity, attaches `RoomComponent` + `BlueprintComponent` + `PersistentEntity`, and registers a minimal `RoomTemplate`. `LinkExits` updates both `RoomComponent.Exits` and the in-memory `RoomTemplate` exit maps for same-session `reload` consistency. The `DigCommand` initiator calls `SaveEntityAsync` on both rooms after this method returns (INV-5: systems do not call persistence). Implemented (Phase 3 slices 5a, persistence-two-level-model).

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
}

public readonly record struct ItemCreationResult(uint ItemEntityId, string BlueprintId, ItemTemplate Template);
```
`CreateItem` generates a unique blueprint id (`item.adhoc.<8-char-base36>`), creates the entity, attaches `ItemDataComponent` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent { RoomEntityId }`, and registers a minimal `ItemTemplate`. The `MkitemCommand` initiator calls `SaveEntityAsync` after this method returns (INV-5). `SetItemSlots` updates both `ItemDataComponent.WornSlots` and `ItemTemplate.WornSlots` in the registry. Implemented (Phase 3 slices 6, 7).

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
`CreateMob` generates a unique blueprint id (`mob.adhoc.<8-char-base36>`), creates the entity, attaches `MobDataComponent` + `BlueprintComponent` + `PersistentEntity` + `LocationComponent { RoomEntityId }`, and registers a minimal `MobTemplate`. Implemented (Phase 3 slice 8). Extended in slice 8a: `SetAttribute(mobEntityId, template, property, value)` mutates `AttributesComponent`/`PoolsComponent` on the live entity and updates the template. Valid properties: `level`, `hp`, `str`, `dex`, `con`. Enforces `CurrentHp ≤ MaxHp` clamp on `hp` change (INV-8). Does not call persistence or events (INV-5).

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
YAML DTO fields: `blueprintId`, `name`, `description`, `keywords`, `type` (string enum value), `spawnRoomBlueprintId`. Implemented (Phase 3 slice 8). Extended in slice 8a: DTO now includes `level`, `maxHp`, `strength`, `dexterity`, `constitution` fields.

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
    int GetStrength(uint entityId);
    int GetDexterity(uint entityId);
    int GetConstitution(uint entityId);
    int GetMaxHp(uint entityId);
    int GetCurrentHp(uint entityId);

    void SetLevel(uint entityId, int value);
    void SetStrength(uint entityId, int value);
    void SetDexterity(uint entityId, int value);
    void SetConstitution(uint entityId, int value);
    /// Sets MaxHp and clamps CurrentHp to the new MaxHp if it would exceed it (INV-8).
    void SetMaxHp(uint entityId, int value);
}
```
All getters return the default value (Level 1, stats 10, HP 100) if the entity lacks the relevant component — safe default for pre-hydration edge cases. Implemented (Phase 3 slice 8a).

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
