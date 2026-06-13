# Accounts

> Account registration, authentication, character creation and selection, and session lifecycle. The gateway through which every player enters the world. **Status:** live (slice 5).

## What it is

An account is a persistent identity that a player registers on their first visit — a username and hashed password. Each account owns one or more named characters; a character is the in-world player entity that carries stats, inventory, location, and abilities. The session is the live connection binding a character to a transport.

From a player's seat: the first time you connect you register a username and create a character name. On subsequent connections you authenticate and select (or create another) character. Once you enter the world your character persists across restarts at the room you last occupied.

## How it works

The feature composes three cooperating pieces:

- **`IAccountSystem`** — domain system owning all account and character lifecycle operations: registration, authentication, character creation, character list, and logout recording. It allocates entities, attaches components, and returns results; all event publication is the caller's responsibility (INV-5). The full design is the [account-system design doc](account-system.md).
- **`LoginFlow`** — a session-layer Initiator (`Server/Sessions/LoginFlow.cs`). It drives the multi-step interactive wizard between TCP accept and the main I/O loop: banner, registration or authentication, then character selection or creation. Transport-coupled (reads raw lines, never echoes passwords); all domain mutations are delegated to `IAccountSystem`. It publishes `AccountCreatedEvent` and `CharacterCreatedEvent` after both entities are saved and returns a `LoginResult`. The full orchestration is the [login-flow design doc](login-flow.md).
- **`ISession` / `ISessionManager`** (`Core/Sessions/`) — the core-tier session contract. `ISession` carries `PlayerEntityId` (0 until bound), `TransportKey`, and `SupportsColor`. `ISessionManager` tracks all live sessions and is the seam `BroadcastSystem` and domain systems use to address connected players. Session lifecycle: registered after `LoginFlow` returns, unregistered on disconnect. See [session lifecycle](#session-lifecycle) below.

The key invariant: `ISession.PlayerEntityId == 0` until `LoginFlow` completes and `SessionManager.Register` is called. `CommandDispatcher` gates on `PlayerEntityId != 0` as defense-in-depth so commands cannot be dispatched before login completes.

## Session lifecycle

1. TCP connect → `TelnetSession` spawns, `PlayerEntityId = 0`.
2. `LoginFlow.RunAsync` blocks until authentication + character selection or creation completes.
3. `TelnetSession` sets `PlayerEntityId = characterEntityId`, attaches transient `PlayerComponent`, calls `SessionManager.Register`, publishes `PlayerConnectedEvent`.
4. `PlayerSessionHandler` (priority Domain) broadcasts arrival and sends the room description.
5. Main I/O loop runs.
6. Disconnect → `HandleDisconnectAsync` unregisters, calls `IAccountSystem.RecordLogout` (updates `LastLoginUtc`), force-saves the character entity, detaches `PlayerComponent`, broadcasts departure. Character entity persists.

A `null` return from `LoginFlow.RunAsync` (exceeded attempts, disconnect mid-login) causes `TelnetSession` to exit without entering the I/O loop. `HandleDisconnectAsync` skips publishing because `PlayerEntityId == 0`.

## Systems

| System | Role |
|---|---|
| [`account-system.md`](account-system.md) | Account + character lifecycle: registration, auth, create character, character list, logout recording |
| [`login-flow.md`](login-flow.md) | `LoginFlow` Initiator: the interactive wizard (banner → register/auth → char-select/create → `LoginResult`) |

## Surfaces

- **Commands** — `whois <name>` (admin `Full`; displays character/account info). See [`../../reference/commands.md`](../../reference/commands.md).
- **Events** — `AccountCreatedEvent`, `CharacterCreatedEvent`, `PlayerConnectedEvent` (extended: adds `AccountEntityId`), `PlayerDisconnectedEvent`. See [`../../reference/handlers.md`](../../reference/handlers.md).
- **Components** — `AccountComponent` (`[Persistent]`: username, password hash, character ids, created timestamp), `CharacterComponent` (`[Persistent]`: account link, character name, created + last-login timestamps). See [`../../reference/components.md`](../../reference/components.md).
- **Config keys** — `Account:MaxCharactersPerAccount` (default 5), `CharacterDefaults:StartingAbilities`. See [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md).

## Flows

- [Login journey (flow-07)](../../architecture/flows/flow-07-login-character-flow.md) — connection → account auth → character select/create → enter world.

## Related

- [`../../architecture/checklist.md`](../../architecture/checklist.md) — INV-5 (systems return results; `LoginFlow` as Initiator is the publisher), INV-12 (accounts and characters are bespoke entities, not registry templates), INV-14 (two-level persistence opt-in).
- [`../../architecture/06-persistence.md`](../../architecture/06-persistence.md) — the two-level model that `AccountComponent` and `CharacterComponent` participate in.
- [`../../roadmap/completed/slice-5-account-character-creation.md`](../../roadmap/completed/slice-5-account-character-creation.md) — as-built history and design decisions.
- **Combat** — [`../combat/combat.md`](../combat/combat.md) — the `CharacterComponent` presence drives the death/respawn lifecycle (HP threshold → incapacitation → bleed-out).
- **Character stats** — [`../character-stats/character-stats.md`](../character-stats/character-stats.md) — `CreateCharacterAsync` seeds `AttributesComponent` and `PoolsComponent` on every new character.
