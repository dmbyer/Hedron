# Login Flow

> `LoginFlow` — the session-layer Initiator that drives the interactive wizard between TCP accept and the main game I/O loop. **Authoring checkpoint:** slice 5. Living document.

## What it is / does

`LoginFlow` is a **session-layer Initiator** (`Server/Sessions/LoginFlow.cs`). It is transport-coupled — it reads raw lines and never echoes passwords — so it lives in `Server/Sessions/`, not `Core/`. The transport-agnostic domain logic (create account, authenticate, character roster, entity allocation) lives in `IAccountSystem`. If a SignalR session replaces telnet, only `LoginFlow` is rewritten; `IAccountSystem` is unchanged.

`LoginFlow` is the only accounts-feature entity that publishes events. All domain mutations go through `IAccountSystem`; the flow's responsibility is sequencing the wizard, managing retries, and publishing `AccountCreatedEvent` + `CharacterCreatedEvent` after both entities are durably saved.

## How it works

### Construction

`LoginFlow` is constructed by `TelnetSession` with the raw `StreamReader` (so it can read lines before the session is registered) and `IOutputWriterFactory` (so prompts are rendered through the formatter pipeline). It receives `IAccountSystem`, `IPersistenceSystem`, and `IEventBus`.

### Banner

Writes `"Welcome to Hedron.\nDo you have an existing account? (yes/no)"` via `IOutputWriter`. Any yes/y/login answer → authentication path; anything else → registration path.

### Registration path

Prompts for username; validates 3–20 chars, alphanumeric + underscore; calls `UsernameExists` and rejects if taken. Prompts for password (≥6 chars, not echoed) with confirmation. Calls `IAccountSystem.CreateAccountAsync` → returns `AccountEntityId`. `AccountCreatedEvent` is **deferred** until after both entities are saved (step below). Falls through to character creation.

### Authentication path

Up to `MaxLoginAttempts` (3) rounds of username + password (not echoed). Calls `IAccountSystem.AuthenticateAsync`. On success → character selection. On exhaustion → writes rejection and returns `null` (session exits without entering the I/O loop).

### Character selection

Calls `GetCharacterList(accountId)`. If empty → falls through to character creation. Otherwise renders a numbered roster + "new" option. Validates input; enforces `Account:MaxCharactersPerAccount` (default 5). Picking a number returns `LoginResult` immediately.

### Character creation

Prompts for a name; validates 2–16 letters only, globally unique via `CharacterNameExists`. Calls `IAccountSystem.CreateCharacterAsync` → allocates the character entity with all required components (see [account-system.md](account-system.md) for the full component list). Then:

1. `IPersistenceSystem.SaveEntityAsync(characterEntityId)` — character saved first.
2. `IPersistenceSystem.SaveEntityAsync(accountEntityId)` — account saved second.

Crash-safety ordering: an orphaned character file is recoverable; a dangling account pointer to a missing character is not.

3. If registration path: `IEventBus.Publish(AccountCreatedEvent { AccountEntityId, Username })`.
4. Always: `IEventBus.Publish(CharacterCreatedEvent { CharacterEntityId, AccountEntityId, CharacterName })`.
5. Returns `LoginResult(CharacterEntityId, AccountEntityId, CharacterName)`.

### Back in `TelnetSession`

On non-null `LoginResult`: sets `PlayerEntityId = characterEntityId`, attaches transient `PlayerComponent`, calls `SessionManager.Register`, publishes `PlayerConnectedEvent { PlayerEntityId, Name, AccountEntityId }`. `PlayerSessionHandler` broadcasts arrival and sends the room description. The main I/O loop starts.

On `null` return (aborted login): `HandleDisconnectAsync` is still called but skips publishing because `PlayerEntityId == 0`.

## Interface

- [`LoginFlow.cs`](../../../Server/Sessions/LoginFlow.cs) — the full state machine; not an interface (transport-coupled; not injected).
- [`TelnetSession.cs`](../../../Server/Sessions/TelnetSession.cs) — the caller; sets `PlayerEntityId` and registers after `LoginFlow` returns.

## Related

- [`accounts.md`](accounts.md) — holistic feature view and session lifecycle.
- [`account-system.md`](account-system.md) — `IAccountSystem`, `CharacterHydrationHandler`, the entity and component model.
- [`../../architecture/flows/flow-07-login-character-flow.md`](../../architecture/flows/flow-07-login-character-flow.md) — the login journey sequence diagram and step-by-step trace.
- [`../../roadmap/completed/slice-5-account-character-creation.md`](../../roadmap/completed/slice-5-account-character-creation.md) — as-built history including spec-review provenance and the `PlayerComponent` attachment ownership clarification.
