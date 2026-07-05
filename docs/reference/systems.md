# Systems Reference

Living catalog of the systems **implemented** in Hedron (core and domain). Update this file whenever a system is added, removed, or renamed.

> Idealized designs for systems not yet built live in [`systems-planned.md`](systems-planned.md) — design intent only; do not assume anything there exists. Why implemented and planned are separated: [`../architecture/09-documentation.md`](../architecture/09-documentation.md).

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
**Location:** [`Core/Systems/BroadcastSystem.cs`](../../Core/Systems/BroadcastSystem.cs) · interface [`Core/Systems/IBroadcastSystem.cs`](../../Core/Systems/IBroadcastSystem.cs)
**Dependencies:** `EntityService`, `ISessionManager`, `IOutputWriterFactory`.
**Note:** Classified as output infrastructure rather than a pure-computation core system; it does I/O (calls `IOutputWriter` per recipient) as the designated multi-recipient fan-out seam. Extended in slice 8: `SendRoomDescriptionAsync` populates `RoomDescriptionMessage.Mobs` with `MobDataComponent.Name` for each entity in the room carrying `MobDataComponent`. Channel mode (global chat, newbie channel) is acknowledged backlog debt — needs channel-membership state. See [`../features/output/output-framework.md#broadcast-model`](../features/output/output-framework.md#broadcast-model) for the full broadcast design; see [`../features/communication/chat-system.md`](../features/communication/chat-system.md) for how the `say` command uses this seam.
Implemented (Phase 2, rewritten in Phase 3 slice 4).

### Output Infrastructure (IOutputFormatter, IOutputFormatterRegistry, IOutputWriterFactory)
**Purpose:** Formatter pipeline that converts typed `IOutputMessage` shapes to transport-encoded strings before writing to sessions.
**Location:** [`Core/Output/`](../../Core/Output/) — `IOutputFormatter.cs`, `IOutputFormatterRegistry.cs`, `IOutputWriter.cs`, `IOutputWriterFactory.cs`, `TelnetOutputFormatter.cs`, `OutputFormatterRegistry.cs`
**Dependencies:** `ISession` (for `TransportKey` and `SupportsColor`).

`IOutputFormatter` has one implementation per transport (`TransportKey` string). `IOutputFormatterRegistry` resolves the right formatter by `session.TransportKey`, falling back to the first registered if no exact match. `IOutputWriter` is the single-session output seam; `IOutputWriterFactory` creates one per request. `TelnetOutputFormatter` (`TransportKey = "telnet"`) applies the four-role ANSI palette (system/error/room-name/direction) and parses `<role>text</role>` inline markers. Strips all color when `session.SupportsColor == false`. See [`../features/output/output-framework.md`](../features/output/output-framework.md) for the full design including the palette table, inline marker syntax, and transport extension points.

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
**Interface:** [`IPasswordHasher.cs`](../../Core/Systems/IPasswordHasher.cs) — `Hash(password)` / `Verify(password, hash)`. 100,000 PBKDF2 iterations, 16-byte random salt, 32-byte key; stores `Base64(salt + hash)` as a single opaque string. `Verify` uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks. Cryptographic randomness stays on `RandomNumberGenerator` and is **not** routed through `IRandom` — security RNG must never be seedable. Implemented (Phase 3 slice 5).

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

### ISession / ISessionManager
**Purpose:** Core-tier session contract. `ISession` is the live-connection abstraction consumed by commands, the dispatcher, the output pipeline, and broadcast — it carries `PlayerEntityId` (0 until bound after login), `TransportKey` (formatter selection), and `SupportsColor`. `ISessionManager` is the registry that tracks all live sessions; `BroadcastSystem` and domain systems use it to address connected players by entity id. Session lifecycle: registered by `TelnetSession` after `LoginFlow` returns, unregistered on disconnect.
**Location:** [`Core/Sessions/ISession.cs`](../../Core/Sessions/ISession.cs) · [`Core/Sessions/ISessionManager.cs`](../../Core/Sessions/ISessionManager.cs)
**Dependencies:** none (core tier; no domain or game-concept references).
Production implementation: `TelnetSession` (implements `ISession`) · `SessionManager` (`Server/Sessions/SessionManager.cs`) registered as a singleton.
See [`../features/accounts/accounts.md`](../features/accounts/accounts.md) for the full session lifecycle.

---

## Domain / feature Systems

### WorldContentLoader
**Purpose:** Scans the configured content directory, registers authored YAML templates with `ITemplateRegistry`, and fresh-spawns room/area/item/mob entities on every startup. Wraps `LoadAndSpawnAsync` in a hosted-service shell (`Server/WorldContentBootstrap`) to enforce startup ordering after `PersistenceBootstrap`.
**Location:** `Core/Modules/World/Systems/WorldContentLoader.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `IContentSerializer`, `WorldConfiguration`, `IConfiguration`, `ILogger`.
**Interface:** [`IWorldContentLoader.cs`](../../Core/Modules/World/Systems/IWorldContentLoader.cs) — `LoadAndSpawnAsync` / `ReloadAsync(→ ContentReloadResult)`. Pure: returns results; never touches the event bus (INV-5); callers publish `ContentReloadedEvent`.
Empty/missing content directory → seeds a single hardcoded `room.void` and warns (host stays up for first-run authors). No `PersistentEntity` is added to any world-content entity — the YAML file is the sole durable state. The world blueprint map (`SpawnMissingEntities` dedup, placement, exit/area linking) is built from **non-persistent** entities only, so a persisted player-owned copy never suppresses an authored re-spawn. `ReloadAsync` is a **full rebuild** — it destroys all world content (`DestroyWorldContent`) and re-spawns from YAML, preserving persistent entities; the `reload` command re-publishes `WorldContentReadyEvent` to re-run the post-load fan-out. See [`../features/world/world-content.md`](../features/world/world-content.md) and [Flow 5](../architecture/flows/flow-05-content-reload.md). Implemented (Phase 3 slices 2, persistence-reform-stage-b, 8, shopping-reload-reconciliation).

### AreaSystem
**Purpose:** Domain system for area–room membership queries and mutation. Provides `GetRoomsInArea`, `GetAreaForRoom`, and `AssignRoomToArea`. All operations are pure ECS mutations; no event publication (INV-5). `AssignRoomToArea` sets `RoomComponent.AreaEntityId` on the live entity and mirrors `areaBlueprintId` to `RoomTemplate.AreaId` in the template registry so the assignment survives `@reload`.
**Location:** `Core/Modules/World/Systems/AreaSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`.
**Interface:** [`IAreaSystem.cs`](../../Core/Modules/World/Systems/IAreaSystem.cs) — `GetRoomsInArea` / `GetAreaForRoom` / `AssignRoomToArea`. Pure ECS mutations; no event bus (INV-5). See [`../features/world/area-model.md`](../features/world/area-model.md) for the bidirectional membership model, persistence rules ([INV-23](../architecture/checklist.md)), and area aspect affinities design.
Registered as a singleton in `WorldModule.AddWorldModule`. Consumed by `RoomBuilderSystem` and `DigCommand`. Implemented (Phase 3, area-model WP-1).

### IAreaContentWriter
**Purpose:** Serializes an `AreaTemplate` to YAML at `{contentDirectory}/areas/{blueprintId}.yaml` using an atomic write (tmp → rename). Symmetric write path for `AreaTemplateDeserializer`. Called by admin commands that create area blueprint definitions.
**Location:** `Core/Modules/World/Systems/IAreaContentWriter.cs` (interface) · `Core/Modules/World/Systems/AreaContentWriter.cs` (implementation)
**Dependencies:** `IConfiguration`.
**Interface:** [`IAreaContentWriter.cs`](../../Core/Modules/World/Systems/IAreaContentWriter.cs) — `WriteAsync(AreaTemplate, CancellationToken)`. Registered as a singleton in `WorldModule.AddWorldModule`. Consumed by `MkareaCommand` after `IAreaBuilderSystem.CreateArea` returns (INV-5: the system never calls persistence). Implemented (Phase 3 admin-area-authoring WP-1).

### IContentValidator
**Purpose:** On-demand content validator, factored out of `RegistryValidationBootstrap` so the same referential-integrity rules run in two call modes. `ValidateRegistry` is the whole-registry sweep (ability→effect/aspect cross-refs, aspect-composition normalization, starting-ability cross-refs, live area-entity affinity normalization) used at boot. `Validate(IEntityTemplate)` is the single in-memory definition check used by the authoring editor per edit and the bulk generator pre-write. Returns a structured `ValidationReport` and **never throws** (INV-5) — the host decides fail-fast policy.
**Location:** `Core/Modules/World/Systems/IContentValidator.cs` (interface) · `ContentValidator.cs` (implementation) · `ValidationReport.cs`
**Dependencies:** `IAbilityRegistry`, `IEffectRegistry`, `IAspectRegistry`, `EntityService`.
**Interface:** [`IContentValidator.cs`](../../Core/Modules/World/Systems/IContentValidator.cs) — `ValidateRegistry(startingAbilityIds)` / `Validate(IEntityTemplate)`. Registered as a singleton in `WorldModule.AddWorldModule`. Consumed by `RegistryValidationBootstrap` (boot) and `IContentDefinitionCatalog.SaveAsync` (per-edit). Implemented (Phase 3 content-tooling WP-1).

### IContentReferenceIndex (Authoring module)
**Purpose:** Declared-edge reference model over the on-disk YAML definition set. Answers three read questions without applying any policy: *does this target exist?*, *who points at this id?*, and *what is broken across all definitions?* Pure read — returns structured results, publishes nothing, holds no live entities (INV-5). The declared edge set drives all four consumers (delete-cascade, warn-but-allow save, integrity sweep, and save-time cross-ref check) without per-edge code paths (INV-19).
**Location:** `Core/Modules/Authoring/Systems/IContentReferenceIndex.cs` (interface) · `ContentReferenceIndex.cs` (implementation).
**Dependencies:** `IContentSerializer`, `IOptions<WorldOptions>`, `ILogger`.

Declared edges (five total; adding a new edge is a one-line data change — INV-19):
- `(Room, AreaId) → Area`
- `(Room, Exits[dir]) → Room` — one tuple per non-blank exit direction
- `(Item, SpawnRoomBlueprintId) → Room`
- `(Mob, SpawnRoomBlueprintId) → Room`
- `(Area, Rooms[]) → Room` — one tuple per room blueprint id in `AreaTemplate.Rooms`

```csharp
public interface IContentReferenceIndex
{
    bool Resolves(ContentKind targetKind, string targetBlueprintId);
    IReadOnlyList<ReferrerEdit> Referrers(ContentKind targetKind, string targetBlueprintId);
    IReadOnlyList<BrokenReference> SweepBroken();
    IReadOnlyList<BrokenReference> BrokenFor(IEntityTemplate definition);
}
```
`Resolves` returns `true` if a definition file for the given kind and id exists on disk. `Referrers` returns every definition that references the given blueprint id as a target. `SweepBroken` sweeps the entire on-disk set and returns every edge whose target does not resolve. `BrokenFor` checks one in-memory definition's cross-references. Registered as a singleton in `AuthoringModule.AddAuthoringModule`. Implemented (Phase 3 content-tooling Slice B, WP-1 + WP-2 fifth-edge addition).

Data records (`Core/Modules/Authoring/ContentReference.cs`): `ReferenceEdge(SourceKind, FieldLabel, TargetKind)` · `BrokenReference(SourceKind, SourceBlueprintId, FieldLabel, MissingTargetId)` · `ReferrerEdit(ReferrerKind, ReferrerBlueprintId, FieldLabel)`.

### IContentDefinitionCatalog (Authoring module)
**Purpose:** The shared content-definition layer both content-tooling tracks call — the offline Blazor editor and the headless bulk generator. Reads/lists/loads/creates/validates/writes/deletes the YAML content-definition families (area, room, item, mob). **Writes and deletes YAML only** — never creates or destroys a live entity, adds `PersistentEntity`, or calls `SaveEntityAsync` ([INV-12](../architecture/checklist.md)/[INV-22/23](../architecture/checklist.md)); applying content changes to the live world is a separate `reload` step. `SaveAsync` validates before writing. `CreateNew` mints an ad-hoc blueprint id without touching the registry or the world.
**Location:** `Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs` (interface) · `ContentDefinitionCatalog.cs` (implementation).
**Dependencies:** `IContentSerializer`, `IContentValidator`, `ITemplateRegistry`, `IContentReferenceIndex`, per-kind content writers, `IOptions<WorldOptions>`, `ILogger`.
**Interface:** [`IContentDefinitionCatalog.cs`](../../Core/Modules/Authoring/Systems/IContentDefinitionCatalog.cs) — `List` / `RoomsInArea` / `Load` / `SaveAsync` / `SaveRoomAsync` / `DeleteAsync` / `CreateNew`. Registered as a singleton in `AuthoringModule.AddAuthoringModule`. Implemented (Phase 3 content-tooling WP-1; area-association read-model added in Slice A; `Delete`, warn-but-allow save, bidirectional room save added in Slice B WP-2). The Blazor host and the bulk-generation system are thin callers. See [`../features/admin-authoring/content-tooling.md`](../features/admin-authoring/content-tooling.md) for the full design.

`List(kind)` returns `ContentSummary` rows; each row carries a resolved `AreaBlueprintId`:
- **Room** — one hop: its own `RoomTemplate.AreaId` (`null` if blank).
- **Item / Mob** — two hops: `SpawnRoomBlueprintId` → that room's `AreaId` (`null` if blank/missing/dangling).
- **Area** — always `null` (areas have no parent area).
The two-hop resolution builds a single room→area map per `List` call (O(N) file reads once, O(1) per item/mob entry).

`RoomsInArea(areaBlueprintId)` returns the subset of rooms from `List(Room)` whose resolved `AreaBlueprintId` equals the argument. Returns an empty list for an unknown area id.

`SaveAsync(definition)` runs structural `Validate`; on structural failure returns `Failed` and writes nothing. On structural pass, writes the file via the matching `I*ContentWriter` and then calls `IContentReferenceIndex.BrokenFor` — any dangling cross-references become non-blocking `Warnings` on the returned `Success` result (warn-but-allow; the file is still written). Use `SaveRoomAsync` for a room with bidirectional exit linking.

`SaveRoomAsync(room, bidirectional)` behaves identically to `SaveAsync` for the room itself; when `bidirectional = true`, also writes the inverse exit on each target room (`Direction.Opposite`). Conflict policy: if a target already has a *different* exit in the inverse direction, the paired write is skipped and a warning is added. If the target already has the *correct* inverse exit (or it is a self-loop), the write is a silent no-op (no warning, no spurious rewrite).

`DeleteAsync(kind, blueprintId)` uses `IContentReferenceIndex.Referrers` to find every referrer of the target, rewrites each to clear the dangling link (room `AreaId` → empty; exit entry removed; item/mob `SpawnRoomBlueprintId` → empty; area `Rooms` entry removed), then calls `File.Delete` on the target YAML file. **No `EntityService.DestroyEntity`, no SQLite delete, no live-world mutation** (INV-22/23). Returns `ContentDeleteResult` with the deleted file path and every cascade edit applied.

`ContentWriteResult` shape (Slice B addition): `record(bool Success, string BlueprintId, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`. A `Success` result may carry non-empty `Warnings`. Factories: `Ok(id)` · `OkWithWarnings(id, warnings)` · `Failed(id, errors)`.

`ContentDeleteResult` shape: `record(string DeletedPath, string DeletedBlueprintId, IReadOnlyList<ReferrerEdit> CascadeEdits)` (`Core/Modules/Authoring/ContentDeleteResult.cs`).

### IContentGenerationSystem (Authoring module)
**Purpose:** Headless bulk content generator (content-tooling track T1). Composes the four existing per-kind content writers + `*Template` types to emit a connected, walkable swath of world-content YAML from a `GenerationProfile` (area count, rooms-per-area range, level range, mob/item density, aspect mix, scaling curve, seed, blueprint prefix). Each area's rooms are wired into an east/west chain and consecutive areas are joined up/down, so the generated world is one reachable graph (Resolved Decision 3). All randomness flows through a per-run `SeededRandom` constructed from `profile.Seed`, and blueprint ids are derived deterministically from `prefix + a per-kind counter` (never `Guid`), so a fixed-seed run is byte-reproducible within a runtime image (INV-26). **Writes YAML only** — creates no live entities, registers nothing in `TemplateRegistry`, never calls persistence (INV-12/22/23). **Returns a `GenerationResult`; never publishes** (INV-5); validation is the caller's (run-mode's) concern.
**Location:** `Core/Modules/Authoring/Systems/IContentGenerationSystem.cs` (interface) · `ContentGenerationSystem.cs` (implementation); data types `GenerationProfile`/`GenerationResult`/`AspectMixEntry`/`ScalingCurve` under `Core/Modules/Authoring/`; the seedable `SeededRandom : IRandom` at `Core/Systems/`.
**Dependencies:** `IAreaContentWriter`, `IRoomContentWriter`, `IItemContentWriter`, `IMobContentWriter`.
```csharp
public interface IContentGenerationSystem
{
    Task<GenerationResult> GenerateAsync(GenerationProfile profile, CancellationToken ct = default);
}
```
Registered as a singleton in `AuthoringModule.AddAuthoringModule`. Implemented (Phase 3 content-tooling bulk-content-generation slice, WP-1). The headless `generate` run-mode in `Server` (WP-2) is the v1 caller; it composes DI without gameplay hosted services, loads a profile YAML, runs one `GenerateAsync`, validates each emitted definition via `IContentValidator.Validate` (single-definition mode), prints a summary, and exits 0/non-zero. See [`../features/admin-authoring/content-tooling.md`](../features/admin-authoring/content-tooling.md) and [Flow 29](../architecture/flows/flow-29-bulk-content-generation.md).

### AccountSystem
**Purpose:** Domain system owning all account and character lifecycle operations: registration, authentication, character creation, character list, and logout recording.
**Location:** `Core/Modules/Account/Systems/AccountSystem.cs`
**Dependencies:** `EntityService`, `IPasswordHasher`, `IClock`, `WorldConfiguration`.
**Interface:** [`IAccountSystem.cs`](../../Core/Modules/Account/Systems/IAccountSystem.cs) — `UsernameExists` / `CharacterNameExists` / `CreateAccountAsync` / `AuthenticateAsync` / `CreateCharacterAsync` / `GetCharacterList` / `RecordLogout`. Pure: returns results; never touches the event bus or persistence (INV-5). `IClock` used for `CreatedAtUtc`/`LastLoginUtc` timestamps (INV-26 seam; see [`../roadmap/backlog.md`](../roadmap/backlog.md) for the testing-harness WP-3 entry). See [`../features/accounts/account-system.md`](../features/accounts/account-system.md) for the full design, entity model, and in-memory index strategy. Implemented (Phase 3 slices 5, persistence-two-level-model, 8a, 9-d, testing-harness).

### RoomBuilderSystem
**Purpose:** Runtime room authoring — creates room entities, wires bidirectional exits, and mutates room properties (`Name`, `Description`). All methods return pure results or mutate in-place; event publication is the caller's responsibility, keeping this system reusable by a future in-game editor without a live player session.
**Location:** `Core/Modules/Admin/Systems/RoomBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `IAreaSystem`, `ILogger<RoomBuilderSystem>`.
**Interface:** [`IRoomBuilderSystem.cs`](../../Core/Modules/Admin/Systems/IRoomBuilderSystem.cs) — `CreateRoom` / `LinkExits` / `SetRoomName` / `SetRoomDescription`. `CreateRoom` generates a unique blueprint id (`room.adhoc.<8-char-base36>`), creates the entity, attaches `RoomComponent` + `BlueprintComponent` (no `PersistentEntity`), and registers a `RoomTemplate`. When `areaId` is non-empty it sets `RoomTemplate.AreaId` and calls `IAreaSystem.AssignRoomToArea` immediately. `LinkExits` updates both `RoomComponent.Exits` and the in-memory `RoomTemplate` exit maps for same-session `reload` consistency. The `DigCommand` initiator writes YAML for both rooms after this method returns (INV-5). See [`../features/admin-authoring/admin-commands.md`](../features/admin-authoring/admin-commands.md) for the builder-verb pattern. Implemented (Phase 3 slices 5a, persistence-two-level-model, area-model WP-1).

### AreaBuilderSystem
**Purpose:** Runtime area authoring — creates ad-hoc area entities. Mirrors `IRoomBuilderSystem`: all methods mutate ECS state only; event publication and YAML writing remain in the command (INV-5). No `IAreaSystem` or `IEventBus` dependency.
**Location:** `Core/Modules/Admin/Systems/AreaBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<AreaBuilderSystem>`.
**Interface:** [`IAreaBuilderSystem.cs`](../../Core/Modules/Admin/Systems/IAreaBuilderSystem.cs) — `CreateArea(string name) → AreaCreationResult`. Generates a unique blueprint id (`area.adhoc.<8-char-base36>`), creates the entity, attaches `AreaComponent` + `BlueprintComponent` (no `PersistentEntity`), and registers a minimal `AreaTemplate`. The `MkareaCommand` initiator writes YAML after this method returns (INV-5). See [`../features/admin-authoring/admin-commands.md`](../features/admin-authoring/admin-commands.md). Implemented (Phase 3 admin-area-authoring WP-2).

### MovementSystem
**Purpose:** Domain system that resolves a direction to a room exit, validates the move, and updates `LocationComponent`. Returns `MoveResult`; never publishes events or calls persistence (INV-5). `MoveCommand` is the Initiator that calls it and publishes `PlayerMovedEvent`.
**Location:** `Core/Modules/Movement/Systems/MovementSystem.cs`
**Dependencies:** `EntityService`.
**Interface:** [`IMovementSystem.cs`](../../Core/Modules/Movement/Systems/IMovementSystem.cs) — `TryMove(playerEntityId, direction) → MoveResult`. See [`../features/world/movement-system.md`](../features/world/movement-system.md) for the move-resolution steps and extension points. Implemented (Phase 3 slice 2+).

### ItemSystem
**Purpose:** Query and mutation operations on item entities — finds items in a room or inventory by entity id, prefix-matches a token against item names and keywords, moves items between ground and inventory, and moves items between two inventory holders (shop↔player, give-to-NPC, player-trade, banking). All mutation methods are pure ECS mutations; no event publication, no persistence calls.
**Location:** `Core/Modules/Items/Systems/ItemSystem.cs`
**Dependencies:** `EntityService`.
**Interface:** [`IItemSystem.cs`](../../Core/Modules/Items/Systems/IItemSystem.cs) — `GetItemsInRoom` / `GetItemsInInventory` / `TryFindItemInRoom` / `TryFindItemInInventory` / `MoveToInventory` / `DropToRoom` / `MoveBetweenInventories(itemEntityId, fromHolderEntityId, toHolderEntityId)`. See [`../features/items/item-inventory-system.md`](../features/items/item-inventory-system.md) for the location model, persistence lifecycle, and resolver design. `MoveBetweenInventories` added in slice 12c (WP-2): removes the item id from the source holder's `InventoryComponent` and appends it to the destination's; touches no `LocationComponent` and no `BlueprintComponent` (INV-21). Implemented (Phase 3 slice 6; `MoveBetweenInventories` slice 12c).

### ItemBuilderSystem
**Purpose:** Runtime item authoring — creates ad-hoc item entities and mutates item properties (`Name`, `Description`, `Keywords`, `ItemType`, `WornSlots`, `StatBonuses`, `Value`). Mirrors `IRoomBuilderSystem`: all methods mutate ECS state only; event publication and persistence calls remain in the command (INV-5).
**Location:** `Core/Modules/Items/Systems/ItemBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<ItemBuilderSystem>`.
**Interface:** [`IItemBuilderSystem.cs`](../../Core/Modules/Items/Systems/IItemBuilderSystem.cs) — `CreateItem` / `SetItemName` / `SetItemDescription` / `SetItemKeywords` / `SetItemType` / `SetItemSlots` / `SetItemStatBonus` / `ClearItemStatBonuses` / `SetItemValue`. Returns `ItemCreationResult(ItemEntityId, BlueprintId, Template)`; never calls persistence or events. `SetItemStatBonus` add-or-replaces one `(ScoreId, magnitude)` row (magnitude 0 removes it); both it and `SetItemSlots` mirror changes to `ItemDataComponent` **and** `ItemTemplate` so the assignment survives `@reload`. `SetItemValue` sets the item's base-unit Coin `Value` (`long`, default 0) with the same dual-write to `ItemDataComponent` and `ItemTemplate`; it is a pure setter (the command rejects negatives at the edge). See [`../features/items/item-inventory-system.md`](../features/items/item-inventory-system.md) for the authoring flow. Implemented (Phase 3 slices 6, 7, 9-c; `StatBonuses` in wearable-equipment-expansion).

### EquipmentSystem
**Purpose:** Query and mutation operations on character equipment slots — finds equipped items, prefix-matches tokens against worn item names/keywords, equips items from inventory into their declared slots (with implicit displacement of existing occupants), and removes items from slots back to inventory. All methods are pure ECS mutations; no event publication, no persistence calls.
**Location:** `Core/Modules/Items/Systems/EquipmentSystem.cs`
**Dependencies:** `EntityService`.
**Interface:** [`IEquipmentSystem.cs`](../../Core/Modules/Items/Systems/IEquipmentSystem.cs) — `GetWornSlots` / `GetEquippedItems` / `TryFindEquippedItem` / `EquipItem` / `RemoveItem` / `RemoveFromSlot`. `EquipItem` owns the implicit-remove loop: for each slot declared on the item, displaces the existing occupant (silently, no event) before placing the new item. `WearCommand` calls only `EquipItem` — never a loop over slots (INV-8). See [`../features/items/equipment-system.md`](../features/items/equipment-system.md) for the slot model and design notes. Implemented (Phase 3 slice 7).

### MobBuilderSystem
**Purpose:** Runtime mob authoring — creates ad-hoc mob entities and mutates mob properties (`Name`, `Description`, `Keywords`, `MobType`, `Protection`). Mirrors `IItemBuilderSystem`: all methods mutate ECS state only; event publication and persistence calls remain in the command (INV-5).
**Location:** `Core/Modules/Mobs/Systems/MobBuilderSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<MobBuilderSystem>`.
**Interface:** [`IMobBuilderSystem.cs`](../../Core/Modules/Mobs/Systems/IMobBuilderSystem.cs) — `CreateMob` / `SetMobName` / `SetMobDescription` / `SetMobKeywords` / `SetMobType` / `SetAttribute` / `SetMobProtection`. `CreateMob` generates a unique blueprint id (`mob.adhoc.<8-char-base36>`), creates the entity, attaches `MobDataComponent` + `BlueprintComponent` + `LocationComponent { RoomEntityId }`, and registers a minimal `MobTemplate`. Slice 8-a added `SetAttribute` for `AttributesComponent`/`PoolsComponent` mutations; enforces `CurrentX ≤ MaxX` clamp (INV-8). Slice 12b (WP-2) added `SetMobProtection(mobEntityId, flags)`: dual-writes `ProtectionComponent` on the live entity and `MobTemplate.Protection`; removes the component when `flags == None`. See [`../features/mobs/mob-system.md`](../features/mobs/mob-system.md) for the full builder model and YAML shape. Implemented (Phase 3 slices 8, 8-a, 9-d, 12b).

### MobContentWriter
**Purpose:** Serializes a `MobTemplate` to YAML at `{contentDirectory}/mobs/{blueprintId}.yaml` using an atomic write (tmp → rename). Mirrors `IItemContentWriter`.
**Location:** `Core/Modules/Mobs/Systems/MobContentWriter.cs`
**Dependencies:** `IConfiguration`.
**Interface:** [`IMobContentWriter.cs`](../../Core/Modules/Mobs/Systems/IMobContentWriter.cs) — `WriteAsync(MobTemplate, CancellationToken)`. YAML DTO: `blueprintId`, `name`, `description`, `keywords`, `type`, `spawnRoomBlueprintId`; extended in slice 8-a with `level`, `maxHp`, attributes; updated in slice 9-d to `mind`/`body`/`spirit`/`attunement` + pool fields; slice 12b (WP-2) added `protection` (list of `ProtectionFlags` enum names, omitted when `None`). See [`../features/mobs/mob-system.md`](../features/mobs/mob-system.md) for the full YAML shape. Implemented (Phase 3 slices 8, 8-a, 9-d, 12b).

### AdminAuthorizer
**Purpose:** Policy seam for admin command authorization. Each admin `ICommand.Execute` calls `IsPrivileged` as its first line; non-privileged sessions get a single rejection line and the command body short-circuits.
**Location:** `Core/Modules/Admin/Systems/AdminAuthorizer.cs`
**Dependencies:** `EntityService`, `IConfiguration`.
**Interface:** [`IAdminAuthorizer.cs`](../../Core/Modules/Admin/Systems/IAdminAuthorizer.cs) — `IsPrivileged(ISession)` / `IsPrivileged(uint playerEntityId)`. **Layered authorization model:** bootstrap layer reads `Admin:PrivilegedNames` from `IConfiguration` and matches against `PlayerComponent.DisplayName`; persisted layer (deferred — see [`../implementation-plans/admin-privilege-elevation.md`](../implementation-plans/admin-privilege-elevation.md)) adds `AdminPrivilegeComponent` (`[Persistent]`). Settings is the floor — always admin if in the list, even without the component. See [`../features/admin-authoring/admin-commands.md`](../features/admin-authoring/admin-commands.md) for the privilege gate pattern. Implemented (Phase 3 slice 2; component layer deferred).

### AttributeSystem
**Purpose:** Read/write seam for `AttributesComponent` and `PoolsComponent`. Getters are the surface the combat slice and stat system call; setters serve the admin and initialization paths. All setters enforce `[0, max]` clamp invariants (INV-8). Never touches the event bus or persistence (INV-5).
**Location:** `Core/Modules/Attributes/Systems/AttributeSystem.cs`
**Dependencies:** `EntityService`.
**Interface:** [`IAttributeSystem.cs`](../../Core/Modules/Attributes/Systems/IAttributeSystem.cs) — getters/setters for `Level` + four attributes (`Mind`/`Body`/`Spirit`/`Attunement`) + four pools (current + max for HP/Mana/Stamina/Astra). `SetMaxX` clamps `CurrentX` to the new max; `SetCurrentX` clamps to `[0, MaxX]`. All getters return safe defaults when the entity lacks the component. See [`../features/character-stats/attribute-system.md`](../features/character-stats/attribute-system.md) for the full design. Implemented (Phase 3 slices 8a, 9-c, 9-d).

### EntityStateService
**Purpose:** Centralized transition-rule enforcement for entity state flags. Attaches and removes `EntityStateComponent`; validates flag combinations against a static transition table; returns structured failure reasons to callers. Never touches the event bus or persistence (INV-5).
**Location:** `Core/Modules/EntityState/Systems/EntityStateService.cs`
**Dependencies:** `EntityService`.
**Interface:** [`IEntityStateService.cs`](../../Core/Modules/EntityState/Systems/IEntityStateService.cs) — `TryEnterState` / `ExitState` / `IsInState` / `GetStates`.
`TryEnterState` evaluates the static transition-rule table; on success attaches or OR-assigns the flag. On a blocked transition returns `false` with a caller-displayable `failReason`. `ExitState` AND-NOT clears the flag and removes the component when `ActiveStates == None`. Callers publish `EntityStateChangedEvent` after mutating state; the service never calls `IEventBus` (INV-5). See [`../features/combat/entity-state.md`](../features/combat/entity-state.md) for the transition-rule table and design rationale. Implemented (Phase 3 slice 9-a).

### StatSystem
**Purpose:** Aggregation seam for effective entity stats. Reads base attributes, equipment bonuses, and active effect modifiers to produce ready-to-use values for the combat slice and future consumers. Never publishes events or calls persistence (INV-5). Adding a new modifier source means extending `StatSystem` methods, not changing the interface.
**Location:** `Core/Modules/Stats/Systems/StatSystem.cs`
**Dependencies:** `IAttributeSystem`, `IEffectSystem`.
**Interface:** [`IStatSystem.cs`](../../Core/Modules/Stats/Systems/IStatSystem.cs) — typed effective getters for the four attributes + `GetEffectiveAttackPower` / `GetEffectiveDefense` / `GetCurrentHp` / `GetMaxHp` + the generalized `Get(uint entityId, ScoreId score)` seam. `GetEffectiveAttackPower` (`Body / 2`) and `GetEffectiveDefense` (`Body / 4`) are **base-only**; all worn-gear bonuses ride `EquipmentEffectContributor` and are folded by `Get(AttackPower)` / `Get(Defense)` — callers needing the gear-inclusive value read `Get`, not the bare getters (combat does). `Get` folds `IEffectSystem.GetModifiers` for `StatModifier`-kind effects. `IStatRegistry` (singleton, `Core/Modules/Stats/StatRegistry.cs`) records pool governance metadata (Mana↔Mind, Stamina↔Body, Astra↔Attunement). Registered in `StatsModule` as a singleton. See [`../features/character-stats/stat-system.md`](../features/character-stats/stat-system.md) for the full design. Implemented (Phase 3 slices 9-c, 9-d, 9-e; equipment-bonus migration in wearable-equipment-expansion).

### EffectSystem
**Purpose:** Core system that manages active effects on entities. Applies/removes individual effects or entire categories; returns active effect lists and per-`ScoreId` stat modifier sums; advances time on each tick, collecting expired and periodic-due effects. Never touches the event bus (INV-5).
**Location:** `Core/Modules/Effects/Systems/EffectSystem.cs` · interface [`IEffectSystem.cs`](../../Core/Modules/Effects/Systems/IEffectSystem.cs) (`Apply` / `Remove` / `RemoveByCategory` / `GetActive` / `GetModifiers` / `AdvanceTick`).
**Dependencies:** `EntityService`.
`Apply` returns `EffectApplyResult` (discriminated union: `Applied(Effect)` or `NotApplied(reason)` where `reason` is `StackingPolicy` or `Immune`). **Gate B — effect immunity:** if the target carries `ProtectionComponent` with `EffectImmune`, `Apply` returns `EffectApplyResult.Immune` before any mutation — for both beneficial and harmful definitions. `EffectApplyResult.StackingBlocked` is returned when `StackPolicy.HighestWins` blocks a weaker re-application (existing effect has equal or greater power). `AdvanceTick` advances elapsed time on timed effects, removes expired ones, and returns `EffectTickResult { DueApplications, Expired }` sorted by `EffectPhase` (Early → Normal → Late). Injects `IEnumerable<IEffectContributor>`; `GetModifiers`/`GetActive` sum stored effects **plus** all registered contributors (INV-24 seam, slice 11-a). Registered via `AddEffectsModule()`. Implemented (Phase 3 slices 9-e, 11-a; Apply return-type changed 12b WP-1).

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
**Interface:** [`ICombatSystem.cs`](../../Core/Modules/Combat/Systems/ICombatSystem.cs) — `CanBeAttacked` / `TryFindTargetInRoom` / `StartCombat` / `EndCombat` / `ExecuteRound` / `ResolveAbilityStrike`.
`CanBeAttacked(targetEntityId)` returns `false` iff the target carries `ProtectionComponent` with `Untargetable` set; `true` otherwise (absent component or `None` flags). **Gate A shared query** — called by ≥2 initiators (`KillCommand` and `AbilityInvocationPipeline`) per INV-19; one method, not duplicated inline reads. `TryFindTargetInRoom` prefix-matches against `MobDataComponent.Name` and `Keywords`. `StartCombat`/`EndCombat` add/remove `CombatStateComponent`. `ExecuteRound` resolves hit, raw damage, and aspect composition; `ResolveAbilityStrike` skips hit/miss for ability strikes. `CombatRoundResult.AspectComposition` is null for untyped damage (point-in-time capture, INV-6). See [`../features/combat/combat-system.md`](../features/combat/combat-system.md) for the round formula and design notes. Implemented (Phase 3 slices 9, 11-a; aspect-resolved 11-d; CanBeAttacked added 12b WP-1).

### DeathSystem
**Purpose:** Domain system owning the HP-threshold evaluation, respawn mutation, and respawn-location management for the player death lifecycle. Pure: never touches the event bus or persistence (INV-5, INV-8). Callers (handlers, initiators) read the returned `DeathTransition` and publish the appropriate events.
**Location:** `Core/Modules/Death/Systems/DeathSystem.cs` (implementation) · `Core/Modules/Death/Systems/IDeathSystem.cs` (interface)
**Dependencies:** `EntityService`, `IEntityStateService`, `IAttributeSystem`, `IEffectSystem`, `ITemplateRegistry`, `WorldConfiguration`, `IConfiguration`, `ILogger<DeathSystem>`.
**Interface:** [`IDeathSystem.cs`](../../Core/Modules/Death/Systems/IDeathSystem.cs) — `OnHpChanged` / `Respawn` / `SetRespawn`.
`OnHpChanged` evaluates HP-threshold crossings and returns `DeathTransition` (`None` / `BecameIncapacitated` / `Died`); only applies to entities with `CharacterComponent`. `Respawn` exits Incapacitated state, relocates to respawn room, strips impermanent effects, restores pools. Configuration: `Death:HpFloor` (default `-10`), `Death:RespawnPoolPercent` (default `0.25`). See [`../features/combat/death-system.md`](../features/combat/death-system.md) for the full lifecycle and design rationale. Registered via `AddDeathModule()`. Implemented (Phase 3 slice 10).

### AbilitySystem
**Purpose:** Domain system managing the full ability lifecycle for players and mobs. Handles learn/teach, multi-cost atomic activation (entity-state/cooldown/cost checks → spend costs → apply effects via `IEffectSystem` → set cooldown → return result), per-ability cooldown tracking, and batch cooldown advancement on each heartbeat tick. Pure: returns results, never touches the event bus or persistence (INV-5).
**Location:** `Core/Modules/Abilities/Systems/AbilitySystem.cs` (implementation)
**Dependencies:** `EntityService`, `IAbilityRegistry`, `IEffectSystem`, `IAttributeSystem`, `IEntityStateService`.
**Interface:** [`IAbilitySystem.cs`](../../Core/Modules/Abilities/Systems/IAbilitySystem.cs) — `Activate` / `IsOffensive` / `Learn` / `Teach` / `GetKnown` / `IsKnown` / `GetCooldownRemaining` / `GetCooldowns` / `AdvanceCooldowns`. See [`../features/abilities/ability-system.md`](../features/abilities/ability-system.md) for the full activation pipeline, cost atomicity rules, and the `resolveOffensiveExternally` branch.
Registered via `AddAbilitiesModule()`. Implemented (Phase 3 slices 11-a, 11-b).

### AspectRegistry
**Purpose:** Hardcoded read-only catalog of `AspectDefinition` records. Born on `DefinitionRegistry<AspectId, AspectDefinition>` (the fourth consumer that anchored the generic extraction). Pure data — no event bus, no persistence. Aspected abilities reference `AspectId` keys validated at startup by `RegistryValidationBootstrap`.
**Location:** [`Core/Modules/Aspects/AspectRegistry.cs`](../../Core/Modules/Aspects/AspectRegistry.cs) (implementation · interface `IAspectRegistry : IRegistry<AspectId, AspectDefinition>` in same file)
**Dependencies:** none.
Starter vocabulary: `Fire`, `Ice`, `Lightning` (Elemental); `Nature` (Primal); `Void`, `Light` (Arcane). Registered via `AddAspectsModule()`. See [`../features/aspects/aspect-system.md`](../features/aspects/aspect-system.md) for the registry key-type rationale and startup validation. Implemented (Phase 3 slice 11-d).

### AspectSystem / IAspectSystem
**Purpose:** Core system: generic aspect math with no game-semantic branching (no FireSystem, no per-aspect switch). Three responsibilities: `Resolve` (apply affinity boost + independent resist); `Affinity` (entity's outgoing composition); `Resist` (entity's effective resistance to one aspect, compute-on-read INV-24). Pure: no events, no persistence, no game rules (INV-2, INV-5).
**Location:** [`Core/Modules/Aspects/Systems/IAspectSystem.cs`](../../Core/Modules/Aspects/Systems/IAspectSystem.cs) (interface) · [`AspectSystem.cs`](../../Core/Modules/Aspects/Systems/AspectSystem.cs) (implementation)
**Dependencies:** `EntityService`.
Registered via `AddAspectsModule()`. Composed by `CombatSystem`: called in both `ExecuteRound` (melee affinity) and `ResolveAbilityStrike` (ability `Aspect` field). See [`../features/aspects/aspect-system.md`](../features/aspects/aspect-system.md) for the resolution formula, affinity/resistance model, and design rationale. Implemented (Phase 3 slice 11-d).

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
**Location:** `Core/Modules/Abilities/AbilityEffectContributor.cs` · implements the core port [`IEffectContributor.cs`](../../Core/Modules/Effects/Systems/IEffectContributor.cs) (`GetModifiers` / `GetActive`).
**Dependencies:** `EntityService`, `IAbilityRegistry`, `IEffectRegistry`.
Registered via `AddAbilitiesModule()` as `IEffectContributor` (DI-collected by `EffectSystem`). Implemented (Phase 3 slice 11-a).

### EquipmentEffectContributor
**Purpose:** Implements the `IEffectContributor` seam (INV-24). Derives each worn item's authored `ItemDataComponent.StatBonuses` into `EffectSystem.GetModifiers`/`GetActive` at read time as `WhileEquipped` `StatModifier` effects — never stored. Reads the wearer's `EquipmentComponent.Slots`, deduping items that occupy more than one slot (a two-hand weapon fills `MainHand` + `OffHand` but counts once). No domain dependency enters core; the adapter owns the translation.
**Location:** `Core/Modules/Items/EquipmentEffectContributor.cs` · implements the core port [`IEffectContributor.cs`](../../Core/Modules/Effects/Systems/IEffectContributor.cs) (`GetModifiers` / `GetActive`).
**Dependencies:** `EntityService`.
Registered via `AddItemsModule()` as `IEffectContributor` (DI-collected by `EffectSystem`). Implemented (wearable-equipment-expansion).

---

## Argument Resolvers

Resolvers implement `IArgumentResolver` and are injected into `CommandArgument` schema entries. They return a candidate list for prefix matching at parse time — read-only, no events, no mutations (INV-5).

### AbilityVerbResolver

**Purpose:** Resolves a typed input verb against all known Active Skills of the invoking player. Used by `CommandDispatcher` Phase 3 to detect bare skill invocations (e.g. `kick`, `ki`) that don't match any registered command.
**Location:** [`Core/Modules/Abilities/AbilityVerbResolver.cs`](../../Core/Modules/Abilities/AbilityVerbResolver.cs) · interface `IAbilityVerbResolver` in same file.
**Dependencies:** `IAbilitySystem`, `IAbilityRegistry`.
`TryResolve` prefix-matches the verb against the invoker's known Active Skills. `GetInvocableVerbs` returns the full invocable verb list (tab-completion seam). Registered via `AddAbilitiesModule()`. Implemented (Phase 3 slice 11-b / WP-2).

### KnownSpellResolver

**Purpose:** Resolves a spell name/id token against the invoker's known Active Spells for use in the `cast` command. Implements `IArgumentResolver`.
**Location:** `Core/Modules/Abilities/Resolvers/KnownSpellResolver.cs`
**Dependencies:** `IAbilitySystem`, `IAbilityRegistry`.

Returns two `ResolvedCandidate` entries per known Active Spell — one for the ability id and one for the display name — both sharing the same canonical value (the ability id). The parser prefix-matches the player's input against these candidates and substitutes the ability id into the `"spell"` argument slot. Registered via `AddAbilitiesModule()`. Implemented (Phase 3 slice 11-b / WP-3).

### MobInRoomResolver

**Purpose:** Resolves a mob name/keyword against entities with `MobDataComponent` in the invoker's current room. Returns the mob entity id (as `string`) as the canonical value so commands receive the entity id directly without a second lookup.
**Location:** `Core/Modules/Mobs/Resolvers/MobInRoomResolver.cs` (moved to a shared, non-combat home in Phase 3 slice 12-c / WP-3).
**Dependencies:** `EntityService`.
Registered as a singleton in `CombatModule` (preserving DI composition order). **Active consumer:** the shopping `list` command binds it as the optional `shopkeeper` argument resolver. `KillCommand` and ability targeting still use the inline `ICombatSystem.TryFindTargetInRoom` path — migrating both onto this resolver (which then genuinely crosses the INV-19 ≥3-consumer threshold) is backlogged. Implemented (Phase 3 slice 11-b / WP-1; relocated 12-c / WP-3).

---

### SpawnSystem
**Purpose:** Tracks spawn slot occupancy for world-content entities (mobs, world-spawn items) and schedules respawns. Self-initializes from the live entity graph on `WorldContentReadyEvent`; reacts to `MobDiedEvent` and `ItemPickedUpEvent` to mark slots vacant; respawns on `HeartbeatTickEvent`. No events published; no persistence (INV-5, INV-8).
**Location:** `Core/Modules/Spawn/Systems/SpawnSystem.cs`
**Dependencies:** `EntityService`, `ITemplateRegistry`, `ILogger<SpawnSystem>`.
**Interface:** [`ISpawnSystem.cs`](../../Core/Modules/Spawn/Systems/ISpawnSystem.cs) — currently empty (all coordination via event subscriptions). See [`../features/world/spawn-system.md`](../features/world/spawn-system.md) for the slot model, event subscription table, and respawn execution. Implemented (persistence reform Stage C).

---

### RegenerationSystem
**Purpose:** Applies baseline out-of-combat resource regeneration to all entities with a `PoolsComponent` on each heartbeat tick. State-based rate: `InCombat` → suppressed entirely; `Resting` → `+RegenAmount` every tick; idle (neither) → `+RegenAmount` every `IdleIntervalTicks`-th tick (global `tickId % IdleIntervalTicks` cadence — no per-entity timer needed). Writes deltas through `IAttributeSystem`'s clamped pool setters (HP/Mana/Stamina/Astra). Never publishes events or touches persistence (INV-5). See [`../features/character-stats/regeneration-system.md`](../features/character-stats/regeneration-system.md) for the full design. Implemented (slice 11-c).
**Location:** `Core/Modules/Regeneration/Systems/RegenerationSystem.cs`
**Dependencies:** `EntityService`, `IEntityStateService`, `IAttributeSystem`.
**Interface:** [`IRegenerationSystem.cs`](../../Core/Modules/Regeneration/Systems/IRegenerationSystem.cs) — `ApplyTickRegen(long tickId)`.
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
**Purpose:** Startup Initiator (hosted service) that runs a fail-fast referential-integrity sweep after registries are populated and world content is ready. The validation rules now live in `IContentValidator`; this bootstrap owns only the host policy — read `CharacterDefaults:StartingAbilities`, call `IContentValidator.ValidateRegistry`, and on any failure log a full report and throw, aborting boot (INV-10, fail-fast). Publishes nothing — closed mechanical sweep (INV-10).
**Location:** `Server/RegistryValidationBootstrap.cs`
**Dependencies:** `IContentValidator`, `IConfiguration`, `ILogger`.
**Startup ordering:** Registered after `WorldContentBootstrap` (registries are populated at DI-construction time, but the ordering guarantees world content spawning is complete before the sweep). Implemented (Phase 3 slice 11-d; validation logic factored to `IContentValidator` in content-tooling WP-1).

### HeartbeatBackgroundService (TimeModule)
**Purpose:** Shared game clock. Fires a `PeriodicTimer` at `Heartbeat:IntervalMs` (default 2000 ms) and publishes `HeartbeatTickEvent { TickId, Timestamp, Elapsed }` on each tick. No game logic — downstream handlers (combat, mob AI, effect expiry) subscribe independently. The `TimeModule` (`Core/Modules/Time/TimeModule.cs`) is the Core-side anchor; the hosted-service registration lives in `Server/Program.cs`.
**Location:** `Server/HeartbeatBackgroundService.cs` (service) · `Core/Modules/Time/Events/HeartbeatTickEvent.cs` (event) · `Core/Modules/Time/TimeModule.cs` (DI module)
**Dependencies:** `IEventBus`, `IConfiguration`, `ILogger<HeartbeatBackgroundService>`.
**Configuration:** `Heartbeat:IntervalMs` (Category 1 operational key, default 2000). `TickId` starts at 1; `TickId = 0` is an unambiguous "no tick has fired" sentinel.
**Startup ordering:** Registered last in the hosted-service queue (after `TelnetServer`) so the first tick cannot land before the world is fully seeded. `PeriodicTimer` does not drift on handler overrun. See [`../features/world/time-system.md`](../features/world/time-system.md) for the full tick loop design, handler priority table, and thread-safety acknowledgment.
Implemented (Phase 3 slice 9-b).

### CurrencyRegistry / ICurrencyRegistry (Economy module)
**Purpose:** Registry of all known currency families, keyed by `CurrencyId` (enum). Each `CurrencyDefinition` holds a display name and a denomination ladder (e.g. copper=1, silver=10, gold=100). Ladder validation (strictly ascending, base unit = 1) runs at construction; a bad row throws `ArgumentException` immediately. Follows the `StatRegistry`/`AspectRegistry` `DefinitionRegistry<TKey,TDef>` precedent. Launch row: `CurrencyId.Coin` (copper/silver/gold). New currency families are added as registry rows — no code change beyond a new `CurrencyId` member.
**Location:** `Core/Modules/Economy/CurrencyRegistry.cs` · `Core/Modules/Economy/CurrencyId.cs` · `Core/Modules/Economy/CurrencyDefinition.cs`
**Dependencies:** `DefinitionRegistry<CurrencyId, CurrencyDefinition>` (core infrastructure); none at domain level.
```csharp
public interface ICurrencyRegistry : IRegistry<CurrencyId, CurrencyDefinition> { }
```
Registered as a singleton in `EconomyModule.AddEconomyModule`. Implemented (currency-foundation WP-1).

### WalletSystem / IWalletSystem (Economy module)
**Purpose:** Single wallet-mutation seam for all currency flows. Owns all reads and mutations of `WalletComponent`. Creates `WalletComponent` on first deposit. INV-5: pure — returns results only; never touches the event bus or persistence.
**Location:** `Core/Modules/Economy/Systems/WalletSystem.cs` · `IWalletSystem.cs`
**Dependencies:** `EntityService`.
```csharp
public interface IWalletSystem
{
    long GetBalance(uint entityId, CurrencyId currency);
    IReadOnlyDictionary<CurrencyId, long> GetBalances(uint entityId);
    bool Deposit(uint entityId, CurrencyId currency, long amount);   // false if amount < 0
    bool TryWithdraw(uint entityId, CurrencyId currency, long amount);
    bool CanAfford(uint entityId, CurrencyId currency, long amount);
    bool Transfer(uint from, uint to, CurrencyId currency, long amount); // atomic; self-transfer no-op
    void SetBalance(uint entityId, CurrencyId currency, long amount);    // throws on amount < 0
}
```
The wallet is entity-keyed: any holder (player, vendor till, bank vault, guild treasury) is a `WalletComponent` carrier — authorization of which entities may transact lives in the calling command/handler (INV-8), not here. Registered as a singleton in `EconomyModule.AddEconomyModule`. Implemented (currency-foundation WP-1).

### CurrencyLootSystem / ICurrencyLootSystem (Economy module)
**Purpose:** Pure domain system that resolves a mob's currency loot roll. Reads the mob's `CurrencyLootComponent`; for each configured currency rolls a uniform inclusive `[min, max]` amount via the injected `IRandom` seam (INV-26). Returns a `CurrencyLootResult` (`CurrencyId → baseAmount`; only non-zero entries). Absent component or zero range → empty result (opt-in default). Never touches the event bus (INV-5).
**Location:** `Core/Modules/Economy/Systems/CurrencyLootSystem.cs` · `ICurrencyLootSystem.cs` · `CurrencyLootResult.cs`
**Dependencies:** `EntityService`, `IRandom`.
```csharp
public interface ICurrencyLootSystem
{
    CurrencyLootResult RollLoot(uint mobEntityId);
}
public sealed record CurrencyLootResult(IReadOnlyDictionary<CurrencyId, long> Awards);
```
Registered as a singleton in `EconomyModule.AddEconomyModule`. Implemented (currency-foundation WP-2).

### ShopSystem / IShopSystem (Shopping module)
**Purpose:** Pure domain system for all shopping rules: price computation, buy/sell validation, buy-back pricing, restock planning, and expiry detection. Composes `IWalletSystem` and `IItemSystem` for affordability and inventory queries; uses `IClock` for all time-dependent decisions (INV-26). Never touches the event bus or persistence (INV-5). Prices are computed on read from `ItemDataComponent.Value × ratio` — never stored.
**Location:** `Core/Modules/Shopping/Systems/ShopSystem.cs` · `IShopSystem.cs` · `ShopResults.cs`
**Dependencies:** `EntityService`, `IWalletSystem`, `IItemSystem`, `IClock`, `IOptions<ShopOptions>`.
```csharp
public interface IShopSystem
{
    ShopListing GetListing(uint shopEntityId);
    ShopBuyResult TryResolveBuy(uint playerEntityId, uint shopEntityId, uint itemEntityId);
    ShopSellResult TryResolveSell(uint playerEntityId, uint shopEntityId, uint itemEntityId);
    IReadOnlyList<(string BlueprintId, int Shortfall)> PlanRestock(uint shopEntityId);
    IReadOnlyList<uint> FindExpired(uint shopEntityId, DateTime nowUtc);
    void SeedTill(uint shopEntityId);
}

// Result records (ShopResults.cs):
public sealed record ShopListingRow(uint ItemEntityId, string Name, long BuyPrice, CurrencyId Currency, bool IsAcquired);
public sealed record ShopListing(uint ShopEntityId, CurrencyId Currency, IReadOnlyList<ShopListingRow> Rows);
public sealed record ShopBuyResult(bool Success, long Price, CurrencyId Currency, string? FailureReason);
public sealed record ShopSellResult(bool Success, long Price, CurrencyId Currency, DateTime? ExpiresAt, string? FailureReason);
```
Buy-back pricing: `Acquired` items (sold by a player) cost `Value × SellRatio` on buy-back (what the shop paid), not `Value × BuyRatio`. `TryResolveSell` carries the clock-derived `ExpiresAt` so the calling command stamps it onto `ShopStockComponent` (INV-8). `PlanRestock` ignores `Acquired` items (top-up semantics). `FindExpired` uses `<= nowUtc` boundary. Registered as a singleton in `ShoppingModule.AddShoppingModule`. Implemented (shopping slice 12c WP-2).

### ProgressionSystem / IProgressionSystem (Progression module)
**Purpose:** Use-driven per-track XP accrual and threshold-improvement resolution (gameplay-model Spine E). Tracks are keyed directly by `ScoreId` — no parallel key type. `AwardExperience` adds to cumulative XP (no-op if ≤ 0); `TryImprove` loops while cumulative XP ≥ the next cumulative threshold (`ThresholdBase + improvementCount × ThresholdIncrement`), incrementing once per crossing. `AwardCombatExperience` computes a killer-vs-victim anti-grind scale from **raw** `AttributesComponent` fields (not `IStatSystem` — see below), rolls a randomized per-track base amount via `IRandom` when the scale is non-zero, and awards each combat track. INV-5: returns result records only; never touches the event bus.
**Location:** `Core/Modules/Progression/Systems/IProgressionSystem.cs` · `ProgressionSystem.cs` · `Core/Modules/Progression/ProgressionConstants.cs`
**Dependencies:** `EntityService`, `IRandom`.
```csharp
public interface IProgressionSystem
{
    AwardOutcome AwardExperience(uint entityId, ScoreId track, int amount, XpSource source);
    int TryImprove(uint entityId, ScoreId track);
    CombatAwardResult AwardCombatExperience(uint killerEntityId, uint victimEntityId);
    int GetXp(uint entityId, ScoreId track);
    int GetImprovementCount(uint entityId, ScoreId track);
    int GetXpToNextThreshold(uint entityId, ScoreId track);
    IReadOnlyList<ScoreId> GetTrackedScores(uint entityId);
}
```
**Not `IStatSystem`:** the anti-grind proxy deliberately reads raw attributes instead of the effect-folded value — going through `IStatSystem` would close a DI cycle back through `ProgressionEffectContributor` (below), which itself depends on `IProgressionSystem`. See [`../features/progression/progression-system.md`](../features/progression/progression-system.md#anti-grind-proxy-reads-raw-attributes). A deliberate, temporary proxy — the slice-3 `IPowerBudgetSystem` oracle replaces it. Registered as a singleton in `ProgressionModule.AddProgressionModule`. Implemented (progression-substrate slice prog-1).

### ProgressionEffectContributor (Progression module)
**Purpose:** The INV-24 contribute-on-read fold for progression power — a third registrant on the core-owned `IEffectContributor` port alongside `EquipmentEffectContributor` and `AbilityEffectContributor`. `GetModifiers` returns `PowerPerImprovement × improvementCount(score)`, pulled fresh from `IProgressionSystem` on every call — never stored, never cached. `GetActive` yields a synthetic `WhileKnown` effect per improved track for display parity.
**Location:** `Core/Modules/Progression/ProgressionEffectContributor.cs`
**Dependencies:** `IProgressionSystem`.
Registered as a singleton `IEffectContributor` in `ProgressionModule.AddProgressionModule`; folded automatically by `EffectSystem.GetModifiers`/`GetActive` with **no interface change** to `IStatSystem` or `EffectSystem`. Implemented (progression-substrate slice prog-1).

### AscensionSystem / IAscensionSystem (Ascension module)
**Purpose:** Character-wide tier state and the ascend gate (gameplay-model R1). `GetTier` returns 0 for an entity with no `AscensionComponent` (safe default, creates nothing). `CanAscend` returns a structured `AscendEligibility` (`Eligible` or a typed reason, e.g. `AtMaxTier`) — the seam a future player-facing Ascension-Objective gate will call; the admin path bypasses it in this slice. `TryAscend` creates `AscensionComponent` lazily, increments `Tier` (clamped `[0, MaxTier]`), and records the new tier's configured unlock ids onto `GrantedUnlocks` idempotently (the unlock table is empty in prog-2 — nothing recorded yet). INV-5: returns result records only; never touches the event bus.
**Location:** `Core/Modules/Ascension/Systems/IAscensionSystem.cs` · `AscensionSystem.cs` · `Core/Modules/Ascension/AscensionConstants.cs`
**Dependencies:** `EntityService` only — **never** `IStatSystem`/`IEffectSystem` (the additive baseline this system's own contributor computes is a pure function of raw `Tier`; going through the stat pipeline here would recreate the DI cycle `IStatSystem` → `IEffectSystem` → contributors → backing system → `IStatSystem`, the same guardrail `ProgressionSystem` observes).
```csharp
public interface IAscensionSystem
{
    int GetTier(uint entityId);
    AscendEligibility CanAscend(uint entityId);
    AscendResult TryAscend(uint entityId);
    IReadOnlyList<string> GetGrantedUnlocks(uint entityId);
}
```
Registered as a singleton in `AscensionModule.AddAscensionModule`. Implemented (ascension slice prog-2).

### AscensionEffectContributor (Ascension module)
**Purpose:** The INV-24 contribute-on-read fold for the character-wide tier's additive power baseline — a fourth registrant on the core-owned `IEffectContributor` port alongside `EquipmentEffectContributor`, `AbilityEffectContributor`, and `ProgressionEffectContributor`. `GetModifiers` returns `TierBaselineStep × GetTier(entityId)` for each score in `AscensionConstants.TrackedScores`, pulled fresh from `IAscensionSystem` on every call — never stored, never cached. `GetActive` yields a synthetic `WhileKnown` effect per tracked score when tier > 0, for display parity.
**Location:** `Core/Modules/Ascension/AscensionEffectContributor.cs`
**Dependencies:** `IAscensionSystem`.
Registered as a singleton `IEffectContributor` in `AscensionModule.AddAscensionModule`; folded automatically by `EffectSystem.GetModifiers`/`GetActive` with **no interface change** to `IStatSystem` or `EffectSystem`. Implemented (ascension slice prog-2).
