# Phase 3 slice 5 — Account / character creation (completed)

> Implemented on branch `claude/quizzical-mestorf-0a3d47`. Full feature spec: [`../../implementation-plans/account-character-creation.md`](../../implementation-plans/account-character-creation.md).

## Outcome

The throwaway "What is your name?" prompt is replaced with a full interactive login state machine (`LoginFlow` Initiator). New players register an account (username + PBKDF2-SHA256 hashed password) and create a named character; returning players authenticate and select or create a character from their roster (max 5). Both account and character entities are `[Persistent]` and survive restart. `LocationComponent` is promoted to `[Persistent]` so characters reconnect to the room they last occupied; `CharacterHydrationHandler` validates stale room references on startup and resets them to the starting room. `PlayerSessionHandler` is rewritten to only manage the transient `PlayerComponent` shim — it no longer creates entities.

## Shipped pieces

| Surface | Location |
|---|---|
| `AccountComponent` (`[Persistent]`: Username, PasswordHash, CharacterEntityIds, CreatedAtUtc) | `Core/Modules/Account/Components/AccountComponent.cs` |
| `CharacterComponent` (`[Persistent]`: AccountEntityId, CharacterName, CreatedAtUtc, LastLoginUtc) | `Core/Modules/Account/Components/CharacterComponent.cs` |
| `LocationComponent` — promoted to `[Persistent]` | `Core/ECS/Components/LocationComponent.cs` |
| `IPasswordHasher` + `PasswordHasher` (PBKDF2-SHA256, 100k iterations, `FixedTimeEquals`) | `Core/Systems/IPasswordHasher.cs`, `Core/Systems/PasswordHasher.cs` |
| `IAccountSystem` + `AccountSystem` (register, auth, create-character, logout, lazy indices) | `Core/Modules/Account/Systems/IAccountSystem.cs`, `AccountSystem.cs` |
| `AccountCreatedEvent` (thin payload: AccountEntityId, Username) | `Core/Modules/Account/Events/AccountCreatedEvent.cs` |
| `CharacterCreatedEvent` (thin payload: CharacterEntityId, AccountEntityId, CharacterName) | `Core/Modules/Account/Events/CharacterCreatedEvent.cs` |
| `WorldContentReadyEvent` (published by `WorldContentBootstrap` after `LoadAndSpawnAsync`) | `Core/Modules/World/Events/WorldContentReadyEvent.cs` |
| `CharacterHydrationHandler` (validates character `LocationComponent` on `WorldContentReadyEvent`) | `Core/Modules/Account/Handlers/CharacterHydrationHandler.cs` |
| `WhoisCommand` (admin `Full`, displays character/account info) | `Core/Modules/Account/Commands/WhoisCommand.cs` |
| `AccountModule` (DI extension: IPasswordHasher, IAccountSystem, CharacterHydrationHandler, WhoisCommand) | `Core/Modules/Account/AccountModule.cs` |
| `LoginFlow` (Initiator: banner→register/auth→char-select/create, returns `LoginResult`) | `Server/Sessions/LoginFlow.cs` |
| `PlayerConnectedEvent` — added `AccountEntityId` parameter | `Core/Modules/Session/Events/PlayerConnectedEvent.cs` |
| `PlayerSessionHandler` — rewritten (attaches/removes `PlayerComponent`; calls `RecordLogout`) | `Core/Modules/Session/Handlers/PlayerSessionHandler.cs` |
| `PersistenceHandler` — added subscriptions to `AccountCreatedEvent`, `CharacterCreatedEvent`, `PlayerDisconnectedEvent` | `Core/Handlers/PersistenceHandler.cs` |
| `TelnetSession` — replaced `PromptForNameAsync` with `LoginFlow`; removed `EntityService` dep | `Server/Sessions/TelnetSession.cs` |
| `TelnetServer` — injects `IAccountSystem`, `IOutputWriterFactory`, `IConfiguration`; removed `EntityService` dep | `Server/Sessions/TelnetServer.cs` |
| `WorldContentBootstrap` — injects `IEventBus`; publishes `WorldContentReadyEvent` | `Server/WorldContentBootstrap.cs` |
| `CommandDispatcher` — `PlayerEntityId == 0` guard in `DispatchAsync` | `Core/Commands/CommandDispatcher.cs` |
| `Program.cs` — `AddAccountModule()`; bus subscriptions for CharacterHydrationHandler, updated PersistenceHandler | `Server/Program.cs` |
| `docs/architecture/06-flows.md` — Flow 2 replaced; Flow 7 (login/character flow) added; Flow 1 and Flow 4 updated | — |
| `docs/reference/components.md` — LocationComponent Persisted? updated; AccountComponent + CharacterComponent rows added | — |
| `docs/reference/systems.md` — AccountSystem + PasswordHasher entries added | — |
| `docs/reference/handlers.md` — PlayerSessionHandler updated; CharacterHydrationHandler added; PersistenceHandler subscriptions updated | — |
| `docs/reference/commands.md` — `whois` admin command added | — |

## Spec-review provenance

**Spec-mode gate (before implementation):** Four rounds of architecture-reviewer spec-mode. Blocking findings in rounds 1–3:
- **INV-8**: LoginFlow described as creating entities directly. Fixed by moving entity creation to `IAccountSystem.CreateCharacterAsync`; LoginFlow reclassified as Initiator.
- **INV-5**: Events Fired table lacked a Tier column. Added.
- **INV-17**: Flows section had no mermaid diagrams. Added Flow 2 replacement and Flow 7 with full mermaid + step prose.
- **INV-16**: No reference catalog update rows. Added "Reference catalog updates" section to use-case doc.
Round 4: APPROVE — all blocking findings resolved.

**Code-mode gate (after implementation):** Three blocking/non-blocking findings:
- **INV-11 (blocking)**: `TelnetSession` used raw `SendLineAsync` for welcome message. Fixed by moving the welcome message to `PlayerSessionHandler.HandleConnectedAsync` via `IBroadcastSystem.SendToRoomAsync` with player-only filter.
- **INV-17 (blocking)**: Flow 4 missing footnote about new `[Persistent]` types. Added.
- **Double AddComponent (non-blocking)**: `TelnetSession` and `PlayerSessionHandler` both attached `PlayerComponent`. Removed the `TelnetSession` call; `PlayerSessionHandler` is the sole attacher.
- **INV-16 (non-blocking)**: `ILogger` listed as AccountSystem dependency but not injected. Removed from catalog.

## Notable design points

- **LoginFlow as Initiator.** The multi-step interactive wizard lives in `Server/Sessions/` (transport-coupled: reads raw lines), delegates all entity mutation to `IAccountSystem`, and publishes events. It is the only slice-5 entity that touches `IEventBus`. Domain and core systems remain event-bus-free.
- **WorldContentReadyEvent vs WorldLoadedEvent.** `WorldLoadedEvent` fires before `WorldContentBootstrap` runs — rooms don't exist yet. A new `WorldContentReadyEvent` published at the end of `WorldContentBootstrap.StartAsync` gives `CharacterHydrationHandler` the correct timing guarantee: both persistence-hydrated and YAML-authored rooms exist, and `StartingRoomEntityId` is set.
- **Lazy in-memory indices.** `AccountSystem` maintains `HashSet<string>` indices for usernames and character names. They're populated on first call by scanning `EntityService`, then updated on every write. Safe because all entities are hydrated before connections are accepted.
- **`LocationComponent` persistence scope.** Promoting `LocationComponent` to `[Persistent]` only affects entities with a `CharacterComponent` (accounts and rooms do not have `LocationComponent`). Existing room entity shapes are unchanged.
- **`CommandDispatcher` guard.** `if (session.PlayerEntityId == 0) return;` is defense-in-depth; the login flow runs before `SessionManager.Register` so the session can't be targeted by commands before binding. Belt-and-suspenders in case a future refactor reorders the startup sequence.

## Deviations from the use-case doc

None. The use-case doc was corrected through four spec-review rounds before any code was written. The as-built code matches the final spec exactly, with one addition: the `PlayerComponent` attachment ownership was clarified post-code-review (handler owns it, not the session).

## Follow-ups unlocked

- **Slice 6 — Items + inventory.** Character entity ids are now stable across restarts; items can be attached to a character's `InventoryComponent` and persisted.
- **Admin privilege elevation (deferred).** The `AdminPrivilegeComponent` path documented in `AdminAuthorizer` can now tie to a real `AccountComponent` instead of a throwaway name.
- **`/color off` command (deferred).** `TelnetSession.SupportsColor` retains its private setter.
- **Character deletion (deferred).** `LoginFlow` has a `// TODO` comment at the character creation menu entry point.
