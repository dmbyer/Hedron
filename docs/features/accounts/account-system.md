# Account System

> Domain system owning all account and character lifecycle operations: registration, authentication, character creation, character list, and logout recording. **Authoring checkpoint:** slice 5 (extended 8a, 9-d, testing-harness). Living document.

## What it is / does

`AccountSystem` is a **domain-tier pure system** that owns the account and character lifecycle. It allocates entities, attaches the right components, and maintains lazy in-memory indices for uniqueness checks. It returns results and never publishes events or calls persistence directly (INV-5) — the `LoginFlow` Initiator is responsible for saving entities and publishing events after `AccountSystem` calls complete.

## How it works

### Entity model

Account and character are **separate ECS entities**:

- **Account entity** — carries only `AccountComponent` and `PersistentEntity`. Never in-world; no `LocationComponent`.
- **Character entity** — the in-world player entity. Carries `CharacterComponent`, `LocationComponent`, `AttributesComponent`, `PoolsComponent`, `PersistentEntity`, and the transient `PlayerComponent` (attached/removed by `PlayerSessionHandler`, not `AccountSystem`). Also carries `RespawnComponent`, `AbilitiesComponent`, and `AspectAffinitiesComponent` (seeded on creation).

Separation keeps each entity's component set clean and avoids coupling the login model to the gameplay entity.

### In-memory uniqueness indices

`AccountSystem` maintains two `HashSet<string>` indices — one for usernames (case-normalized), one for character names (global uniqueness). Both are populated lazily on first call by scanning `EntityService`, then updated synchronously on every write. Safe because all entities are hydrated before any connection is accepted (the hosting sequence guarantees this).

### Registration path

`CreateAccountAsync(username, password)` validates the username (3–20 chars, alphanumeric + underscore, case-insensitive), hashes the password via `IPasswordHasher`, creates an entity, attaches `AccountComponent` + `PersistentEntity`, and returns the `AccountEntityId`. The caller (`LoginFlow`) saves the entity and publishes `AccountCreatedEvent`.

### Authentication path

`AuthenticateAsync(username, password)` looks up the account entity, calls `IPasswordHasher.Verify` using constant-time comparison (PBKDF2-SHA256, `FixedTimeEquals`), and returns an `AuthResult`. Up to `MaxLoginAttempts` (3) bad attempts are managed by `LoginFlow`; no server-side lockout is implemented (acknowledged debt).

### Character creation

`CreateCharacterAsync(accountEntityId, characterName)` validates the name (2–16 letters, globally unique), creates the character entity, attaches `CharacterComponent { AccountEntityId, CharacterName, CreatedAtUtc }`, `LocationComponent { RoomBlueprintId = WorldConfiguration.StartingRoomBlueprintId }`, `AttributesComponent { Level=1, Mind=10, Body=10, Spirit=10, Attunement=10 }`, `PoolsComponent { MaxHp=100, CurrentHp=100, MaxMana=50, CurrentMana=50, MaxStamina=50, CurrentStamina=50, MaxAstra=10, CurrentAstra=10 }`, `RespawnComponent`, `AbilitiesComponent`, `AspectAffinitiesComponent`, and `PersistentEntity`; appends the character id to `AccountComponent.CharacterEntityIds`. Returns `CharacterEntityId`. `LoginFlow` saves character-first, then account (crash-safety ordering: an orphaned character file is recoverable; a dangling account pointer to a missing character is not).

### Logout recording

`RecordLogout(characterEntityId)` updates `CharacterComponent.LastLoginUtc`. `PlayerSessionHandler` calls `SaveEntityAsync` after `RecordLogout` returns (session-end boundary save, INV-22).

### `CharacterHydrationHandler`

Subscribes to `WorldContentReadyEvent` (not `WorldLoadedEvent`) and validates each character's `LocationComponent.RoomBlueprintId`. If the blueprint no longer resolves (e.g. a room was deleted between restarts) it resets the character to the starting room. `WorldContentReadyEvent` is the correct trigger — both persistence-hydrated and YAML-authored rooms exist, and `StartingRoomEntityId` is set.

### Wall-clock debt (INV-26)

`AccountSystem` uses `IClock.UtcNow` (injected) for `CreatedAtUtc` and `LastLoginUtc` timestamps. The `IClock` seam was added in the testing-harness slice. See [`../../roadmap/backlog.md`](../../roadmap/backlog.md) for the testing-harness WP-3 entry.

## Interface

The seam self-documents in code — describe behaviour here, not signatures:

- [`IAccountSystem.cs`](../../../Core/Modules/Account/Systems/IAccountSystem.cs) — `UsernameExists` / `CharacterNameExists` / `CreateAccountAsync` / `AuthenticateAsync` / `CreateCharacterAsync` / `GetCharacterList` / `RecordLogout`. Pure: returns results, never touches the event bus or persistence.
- [`AccountComponent.cs`](../../../Core/Modules/Account/Components/AccountComponent.cs) — `[Persistent]`: username, password hash, character id list, created timestamp.
- [`CharacterComponent.cs`](../../../Core/Modules/Account/Components/CharacterComponent.cs) — `[Persistent]`: account link, character name, created + last-login timestamps.

## Password hashing

`IPasswordHasher` (core tier, `Core/Systems/`) uses PBKDF2-SHA256: 100,000 iterations, 16-byte random salt, 32-byte key, stored as a single Base64 string (`Base64(salt + hash)`). Verification uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks. BCrypt was the alternative; PBKDF2 was chosen for dependency minimalism (no external NuGet). See [`../../reference/systems.md`](../../reference/systems.md) for the `IPasswordHasher` catalog row.

## Considerations

- **No `TemplateRegistry` entries.** Accounts and characters are runtime-created bespoke entities (INV-12). The registry is for authored content (rooms, mobs, items).
- **Character name uniqueness is global** (not per-account), simplifying `whois <name>` / admin-target lookups.
- **`CommandDispatcher` guard.** `if (session.PlayerEntityId == 0) return;` is defense-in-depth in `CommandDispatcher.DispatchAsync` — belt-and-suspenders in case a future refactor reorders startup.

## Related

- [`accounts.md`](accounts.md) — the holistic feature view + player/admin surfaces.
- [`login-flow.md`](login-flow.md) — the `LoginFlow` Initiator that calls this system and publishes events.
- [`../../architecture/flows/flow-07-login-character-flow.md`](../../architecture/flows/flow-07-login-character-flow.md) — the login journey (connection → auth → char-select → enter world).
- [`../../reference/systems.md`](../../reference/systems.md) · [`../../reference/components.md`](../../reference/components.md) — `AccountSystem` / `IPasswordHasher` / `AccountComponent` / `CharacterComponent` catalog rows.
- [`../../roadmap/completed/slice-5-account-character-creation.md`](../../roadmap/completed/slice-5-account-character-creation.md) — as-built history and decision record.
