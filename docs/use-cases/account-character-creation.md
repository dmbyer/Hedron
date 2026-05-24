# Use Case: Account / Character Creation

**Status:** implemented
**Actors:** Player (new and returning), System
**Module:** `Core/Modules/Account/`

> **Note — persistence model superseded.** Any references in this doc to `PersistenceHandler`, `MarkDirty`, or the dirty-set flush pattern reflect the slice-5 design and are now historical. The as-built persistence model is the two-level opt-in design described in [`persistence-two-level-model.md`](persistence-two-level-model.md). Read that doc for the current save-on-change and area-scoped flush behaviour.

---

## Description

Replaces the throwaway `"What is your name?"` prompt in `TelnetSession.PromptForNameAsync` with a full account-and-character lifecycle: new players register an account (username + hashed password) and create a first character (display name + base stats); returning players authenticate and select from their existing characters. Account state is stored in a persistent `AccountComponent` on a dedicated account entity. Character state is stored in a persistent `CharacterComponent` on a player entity (replacing and extending the transient `PlayerComponent`). Both entities survive restart. The `ISession.PlayerEntityId=0-until-bound` contract is preserved: the session carries id 0 until a character is fully selected and enters the world.

This is the first slice that produces real user-facing `[Persistent]` data. It directly enables the `admin-privilege-elevation` slice (which needs reliable player-name → entity resolution).

---

## Preconditions

- Phase 3 slices 1–4 and the prefix-matching enhancement are complete: `IPersistenceSystem`, `ITemplateRegistry`, `ICommandDispatcher` (framework shape), `IOutputWriter`/`IOutputFormatterRegistry`, `IBroadcastSystem` (audience-filter), and `TelnetSession` (with `SupportsColor`) all exist.
- `ISession.PlayerEntityId` is `0` until explicitly bound (existing contract — unchanged).
- `EntityService`, `IEventBus`, `ISessionManager`, and `WorldConfiguration` exist and are injected into the session layer.
- `PersistenceSystem` can flush any entity carrying a `[Persistent]` component — no changes to the flush mechanism itself.

---

## Postconditions

- **Account entity:** every registered player owns exactly one account entity. The entity carries `AccountComponent` (`[Persistent]`): username (case-insensitive), `PasswordHash` (PBKDF2), a list of character entity ids, and a `CreatedAtUtc` timestamp. The entity survives restart.
- **Character entity:** each character corresponds to one player entity carrying `CharacterComponent` (`[Persistent]`): `AccountEntityId`, `CharacterName`, `CreatedAtUtc`, and a `LastLoginUtc`. `LocationComponent` is also persistent from this slice onward. `PlayerComponent` is **refactored**: it remains the transient session-binding shim (`DisplayName` + `Session` ref), not persisted; `CharacterComponent` holds the durable identity.
- **Login flow:** a connecting session goes through an async login state machine (`LoginFlow`, session layer). The `ISession.PlayerEntityId` stays `0` until character selection completes. `CommandDispatcher` is not reachable by the session until `PlayerEntityId != 0`.
- **New-player path:** registering a username + password creates an account entity, then immediately enters character-creation; the first character is created and the session binds.
- **Returning-player path:** authenticating an existing account displays the character roster; the player selects (or creates a new) character and the session binds.
- **Password confidentiality:** `password` inputs are never echoed to the client.
- **`PlayerConnectedEvent` payload extends:** it now carries the character entity id and an `AccountEntityId`; downstream handlers may distinguish account from character.
- **`PlayerSessionHandler` refactored:** at `PlayerConnectedEvent` time the character entity already has all persistent components hydrated or freshly set; the handler attaches the transient `PlayerComponent` (with `Session` ref) and broadcasts the arrival. The character entity is **not** destroyed on disconnect.
- **`TelnetSession.PromptForNameAsync` removed** — replaced by the `LoginFlow` wizard.
- No changes to any gameplay system, movement, combat, or output formatting.

---

## Main Flow

1. **TCP connect.** `TelnetServer` spawns a `TelnetSession` task. `PlayerEntityId` is `0`. The session runs `LoginFlow.RunAsync`.
2. **Welcome banner.** The session writes a `PlainMessage` banner via `IOutputWriter` and prompts: "Do you have an account? (yes/no)".
3. **Branch: new player (registration).** Player types `no`/`new`/`register`. The flow: (a) prompts for a username and validates it (3–20 chars, alphanumeric + underscore, case-insensitive uniqueness via `IAccountSystem.UsernameExists`); (b) prompts for a password (not echoed) + confirmation; (c) calls `IAccountSystem.CreateAccountAsync(username, password)` → account entity with `AccountComponent` + `PersistentEntity`; (d) proceeds to character creation (step 5).
4. **Branch: returning player (authentication).** Player types `yes`/`login`. The flow prompts for username + password (not echoed), calls `IAccountSystem.AuthenticateAsync` → `AuthResult` or failure (up to 3 attempts before disconnect). On success: character selection (step 6).
5. **Character creation.** Prompts for a character name (2–16 letters, globally unique via `CharacterNameExists`). Calls `IAccountSystem.CreateCharacterAsync(accountEntityId, characterName)` — the system creates the player entity, attaches `CharacterComponent` + `LocationComponent` (`RoomEntityId = WorldConfiguration.StartingRoomEntityId`) + `PersistentEntity`, registers the character on the account, returns the character entity id. `LoginFlow` publishes `CharacterCreatedEvent`. Falls through to world entry (step 7).
6. **Character selection (returning player).** `GetCharacterList(accountEntityId)` returns names + entity ids; the session writes a numbered roster. Player types the number or `new` (→ step 5).
7. **World entry.** `LoginFlow` returns `LoginResult(characterEntityId, accountEntityId, characterName)`. `TelnetSession` sets `PlayerEntityId = characterEntityId`, attaches transient `PlayerComponent`, calls `SessionManager.Register`, publishes `PlayerConnectedEvent`. `PlayerSessionHandler` (priority `Domain`) broadcasts arrival + sends room description. The main I/O loop starts.
8. **Disconnect.** `TelnetSession.HandleDisconnectAsync` unregisters and publishes `PlayerDisconnectedEvent`. `PlayerSessionHandler` calls `IAccountSystem.RecordLogout` (updates `LastLoginUtc`), saves the character entity, detaches `PlayerComponent`, broadcasts departure. The character entity persists.

---

## Events Fired

| Event | Publisher | Tier | Scope | Purpose |
|---|---|---|---|---|
| `AccountCreatedEvent(uint AccountEntityId, string Username)` | `LoginFlow` | Initiator | Once per registration | Audit log. |
| `CharacterCreatedEvent(uint CharacterEntityId, uint AccountEntityId, string CharacterName)` | `LoginFlow` | Initiator | Once per new character | Audit log. |
| `PlayerConnectedEvent(uint PlayerEntityId, string Name, uint AccountEntityId)` — **extended payload** | `TelnetSession` | Initiator | Per login | Existing event; payload gains `AccountEntityId`. |
| `PlayerDisconnectedEvent(uint PlayerEntityId, string Name)` | `TelnetSession` | Initiator | Per disconnect | Existing event; `PlayerSessionHandler` records logout and persists the character. |

---

## Design Notes

- **Account entity vs. character entity are separate ECS entities.** The account entity is never in-world (no `LocationComponent`); it holds only `AccountComponent`. The character entity is the in-world player entity and carries `CharacterComponent`, `LocationComponent`, and the transient `PlayerComponent`. This separation keeps each entity's component set clean and avoids coupling the login model to the gameplay entity.
- **`LoginFlow` is a session-layer collaborator, not a domain system.** It holds the async login state machine and lives in `Server/Sessions/`, not `Core/`. The placement is intentional: the login state machine is transport-coupled (reads raw lines, never-echoes password) and would be rewritten for a SignalR session. The transport-agnostic domain logic (create account, authenticate, character roster) lives in `IAccountSystem`.
- **`CharacterHydrationHandler` triggers on `WorldContentReadyEvent`, not `WorldLoadedEvent`.** `WorldLoadedEvent` fires before `WorldContentBootstrap` runs — at that point YAML rooms don't exist and `StartingRoomEntityId` is `0`, so a naive handler would reset every character. `WorldContentReadyEvent` (published at the end of `LoadAndSpawnAsync`) is the correct trigger: both hydrated and YAML-authored rooms exist and the starting room is resolved.
- **No `TemplateRegistry` entries for accounts or characters.** These are runtime-created bespoke entities (INV-12: bespoke entities are built by the owning feature). The registry is for authored content (rooms, mobs, items).
- **Uniqueness via lazy in-memory scan.** `AccountSystem` populates `HashSet<string>` caches for usernames and character names on first call (safe — all entities are hydrated before any connection is accepted) and updates them synchronously on each create. `AccountSystem` is a singleton so the cache is stable.
- **Password hashing.** `IPasswordHasher` uses PBKDF2-SHA256 (`Rfc2898DeriveBytes`), 100 000 iterations, 16-byte salt, 32-byte key, stored as a single Base64 string. BCrypt was the alternative; PBKDF2 was chosen for dependency minimalism (no external NuGet).
- **Character name uniqueness is global** (not per-account), which simplifies `@teleport <name>` / `@grant <name>` lookups.
- **Max failed login attempts: 3, then disconnect.** No server-side lockout this slice (acknowledged debt).

---

## Related

- [`persistence-substrate.md`](persistence-substrate.md) — provides `IPersistenceSystem`, `PersistenceBootstrap`, and the `[Persistent]` mechanism.
- [`persistence-two-level-model.md`](persistence-two-level-model.md) — the current persistence model that supersedes this doc's slice-5 dirty-set references.
- [`world-content-loading-and-admin-substrate.md`](world-content-loading-and-admin-substrate.md) — `AdminAuthorizer` checks `PlayerComponent.DisplayName` (set from `CharacterComponent.CharacterName` at bind time).
- [`command-framework.md`](command-framework.md) — the `whois` admin command (added this slice) is authored against the framework shape.
- [`admin-privilege-elevation.md`](admin-privilege-elevation.md) — deferred slice; its precondition "a real player-account / display-name resolution path exists" is satisfied here.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
