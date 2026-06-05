# Player Prompt & Output Batching

**Status:** planned

> **Expanded from the `architecture-advisor` seed.** The seed (Description, Design notes, `## Architecture brief`, Open questions) has been extended into the full template by the `use-case-planner`. Resolved open questions are folded into Design notes. The `## Architecture brief` block is in-flight and is trimmed on ship.

## Actors

- **Player** — sees a status prompt after their command output and after each combat round; no longer flooded with a prompt per line.
- **System** — the heartbeat flushes batched tick output and emits one trailing prompt.
- **Mob** — a source of async combat output that must batch into the round's single flush.

## Module

Primarily the **Output framework** (`Core/Output/`): the per-session buffer, the flush mechanism, and the new `PromptMessage` shape are core-tier transport plumbing with no domain knowledge. The **prompt composer** reads entity state (`IEntityStateService`) and pools (`IStatSystem`), so it is **domain-aware and must not live in `Core/Output/`** ([INV-2](../architecture/checklist.md)); it lives in a small `Core/Modules/Prompt/` module and is wired into the core buffer through a **core-owned port** (`IPromptSource`). See [`subsystems/output.md`](../architecture/subsystems/output.md).

## Description

Give the player a status prompt that trails their output: by default it shows their entity **state** in parentheses only when abnormal — `(Resting)`, `(Incapacitated)`, `(Fighting)` — followed by **current/max for each resource pool** the entity has (HP, Mana, Stamina, Astra). To stop the player being flooded with a prompt after every line, output is **batched per session**: many sources (command responses, room broadcasts, combat rounds, effect ticks) write into one session-scoped buffer that **flushes on defined boundaries** — emitting the buffered lines plus **one** freshly-composed prompt. A command's response flushes immediately (you type `help`, you see help, then your prompt); a combat round's several messages (your strike, the mob's counter, ability results) accumulate over the heartbeat tick and flush together with a single trailing prompt; conversational messages (`say`/`tell`) flush immediately so chat stays snappy.

## Preconditions

- A player is connected, authenticated, and bound to a live entity (session `PlayerEntityId != 0`).
- The entity carries `PoolsComponent` (HP, Mana, Stamina, Astra current/max) — added in slice 8a.
- The entity is queryable via `IEntityStateService` — added in slice 9-a.
- The four resource pool `ScoreId` values (`HpCurrent`/`HpMax`, `ManaCurrent`/`ManaMax`, etc.) are registered — added in slice 9-d.
- The output framework is in place (slices 3 + 4): typed `IOutputMessage`, `IOutputWriter`, `IOutputFormatterRegistry`, `TelnetOutputFormatter`.
- The heartbeat is running (`HeartbeatTickEvent` fires every `Heartbeat:IntervalMs` ms) — slice 9-b.

## Postconditions

- After every command dispatch, the player's session buffer is flushed: all command output appears followed by exactly **one** prompt line.
- After each heartbeat tick, sessions with pending (batched) output are flushed: messages appear followed by exactly **one** prompt line.
- A `Chat`-category message causes an **immediate** flush — the chat message and its trailing prompt appear without waiting for tick-end or command-end.
- The prompt line shows `(StateLabel)` **only** when the player is in a non-normal state: `InCombat → (Fighting)`, `Resting → (Resting)`, `Incapacitated → (Incapacitated)`. No label is shown when no flags are set.
- The prompt line shows `HP: x/y` pairs for each resource pool the entity has; pools with `max = 0` are omitted.
- Buffer cleanup occurs on disconnect — the session's buffer slot is released from `ISessionBufferRegistry` to avoid a memory leak.

## Main flow

### A — Command path

1. Player types a command (e.g., `kick goblin`); `TelnetSession` calls `CommandDispatcher.DispatchAsync(session, input)`.
2. The dispatcher acquires the session's output writer via `IOutputWriterFactory.Create(session)` — the factory returns a `SessionBufferedOutputWriter` backed by the session's `ISessionOutputBuffer` (obtained from `ISessionBufferRegistry.GetOrCreate(session)`).
3. Command executes; any number of typed messages are written via `context.Output.WriteAsync(msg)`. Each call:
   - Enqueues `msg` in the session buffer.
   - Checks `CategoryFlushPolicy.GetPolicy(msg.Category)`. If `Immediate` (only `Chat`) → immediately calls `buffer.FlushAsync()` (drains, appends prompt, sends). Otherwise → message stays in buffer.
4. After `command.ExecuteAsync(context)` returns (or an exception is caught), the dispatcher calls `output.FlushAsync()` inside a `try/finally` block.
5. `FlushAsync()`:
   a. Acquires the buffer lock; drains all pending messages into a local snapshot; clears the pending list; releases the lock.
   b. Formats and sends each message in order via `session.SendLineAsync`.
   c. Calls `IPromptSource.GetPrompt(session.PlayerEntityId)` to build a fresh `PromptMessage`.
   d. If the prompt is non-null: formats and sends it. Buffer is now empty.
6. Player sees: all command output lines, then `(StateLabel) HP: x/y Mana: a/b Stamina: c/d Astra: e/f` on its own line.

### B — Tick/combat path

1. `HeartbeatBackgroundService` publishes `HeartbeatTickEvent`.
2. `CombatTickHandler` (p=20) calls `ICombatSystem.ExecuteRound` per active combat pair and publishes `CombatRoundEvent`.
3. `CombatHandler` (p=20 on `CombatRoundEvent`) writes `CombatMessage`s to affected sessions via `IBroadcastSystem` → `IOutputWriterFactory.Create(session)` → `SessionBufferedOutputWriter.WriteAsync`. `Combat` category = Batched → messages enqueue in each session's buffer.
4. Other tick handlers run (`EffectTickHandler`, `RegenerationTickHandler`, `DeathTickHandler`) and may write additional messages to session buffers.
5. `OutputFlushTickHandler` (p=85, `HandlerPriority.OutputFlush`, on `HeartbeatTickEvent`) runs last among tick handlers. Calls `ISessionBufferRegistry.FlushAllPendingAsync()`.
6. For each session that `HasPending`:
   - Drains the buffer atomically.
   - Formats and sends each message.
   - Appends the freshly-composed prompt.
7. Each affected player sees: all that tick's combat and effect messages, then one prompt reflecting post-round pools.

### C — Chat path (immediate flush)

1. Player types `say hello`; command writes a `PlainMessage(category: Chat)` via `context.Output.WriteAsync`.
2. `SessionBufferedOutputWriter.WriteAsync` enqueues the message, sees `Immediate` flush policy, immediately calls `buffer.FlushAsync()`.
3. Player sees their `say` confirmation followed by the prompt without waiting for tick-end.
4. Same applies to Chat messages received via BroadcastSystem — each recipient's buffer is immediately flushed for Chat-category messages.

### D — Disconnect cleanup

1. `PlayerDisconnectedEvent` fires; `PlayerSessionHandler.HandleAsync` runs (existing handler).
2. After existing disconnect logic, calls `ISessionBufferRegistry.Release(session.SessionId)` — drops the buffer entry.

## Events fired

None new. This slice introduces no new event types — the prompt observes by reading at flush time (compute-on-read, per the Design notes), not by subscribing to any `PromptChangedEvent` or `PoolDisplayedEvent`.

## Systems / handlers involved

### New — Core tier (`Core/Output/`)

| Type | Role |
|---|---|
| `ISessionOutputBuffer` / `SessionOutputBuffer` | Per-session pending-message list; thread-safe enqueue and drain-on-flush |
| `ISessionBufferRegistry` / `SessionBufferRegistry` | Singleton map of `SessionId → ISessionOutputBuffer`; `GetOrCreate`, `Release`, `FlushAllPendingAsync` |
| `IPromptSource` | Core-owned port; implemented by the domain composer; called at flush to obtain `PromptMessage` |
| `PromptMessage` | Typed output shape: optional `StateLabel` + `IReadOnlyList<PoolDisplay>` (name, current, max) |
| `SessionBufferedOutputWriter` | Replaces `OutputWriter`; enqueues messages; triggers auto-flush for `Immediate` category |
| `CategoryFlushPolicy` | Static lookup: `OutputCategory → FlushPolicy (Immediate | Batched)` |

### New — Domain tier (`Core/Modules/Prompt/`)

| Type | Role |
|---|---|
| `PromptComposerSystem` | Implements `IPromptSource`; reads `IEntityStateService.GetStates(entityId)` for state label; reads `IStatSystem.Get(entityId, ScoreId)` for each pool (skips pools with `max = 0`); returns a `PromptMessage` |

### New — Handler

| Type | Event | Priority |
|---|---|---|
| `OutputFlushTickHandler` (`Core/Handlers/OutputFlushTickHandler.cs`) | `HeartbeatTickEvent` | 85 (`HandlerPriority.OutputFlush`) |

### Modified

| Type | Change |
|---|---|
| `OutputCategory` | Add `Combat` value (Batched flush policy) |
| `HandlerPriority` | Add `OutputFlush = 85` constant |
| `IOutputWriter` | Add `Task FlushAsync()` |
| `OutputWriterFactory` | Inject `ISessionBufferRegistry` and `IPromptSource`; return `SessionBufferedOutputWriter` instead of `OutputWriter` |
| `CommandDispatcher` | Wrap `DispatchAsync` in `try/finally`; call `output.FlushAsync()` in the `finally` block |
| `PlayerSessionHandler` | Inject `ISessionBufferRegistry`; call `Release(session.SessionId)` on `PlayerDisconnectedEvent` |
| `TelnetOutputFormatter` | Add rendering for `PromptMessage`: format state label (if any) + pool cur/max pairs |

## Implementation plan — work packages

> Three packages. WP-A is the structural prerequisite. WP-B and WP-C are independent of each other and may run in parallel after WP-A merges. The primary agent runs `architecture-reviewer` (code mode) across the **combined** diff of all three packages once all have landed.

---

### WP-A — Session-scoped buffer + core shapes

**Scope.** All new types in `Core/Output/`; `OutputCategory.Combat`; `HandlerPriority.OutputFlush`; `IOutputWriter.FlushAsync()`; updated `OutputWriterFactory`; DI wiring in `Server/Program.cs` for `ISessionBufferRegistry`; stub `IPromptSource` (returns null → no prompt yet); `PlainMessage` constructor change (required `OutputCategory` parameter); `subsystems/output.md` update.

**Files.**

- `Core/Output/OutputCategory.cs` — add `Combat`
- `Core/Output/PlainMessage.cs` — make `OutputCategory` a **required** constructor parameter (no default). Every call site must explicitly pass the correct category — e.g., `say`/social paths pass `OutputCategory.Chat` so the `Immediate` flush policy fires and idle recipients see the message without waiting for a tick or command boundary. This is a breaking change: all existing `new PlainMessage(text, severity)` call sites must be updated to `new PlainMessage(text, severity, OutputCategory.X)`.
- `Core/Output/FlushPolicy.cs` — `enum FlushPolicy { Immediate, Batched }`
- `Core/Output/CategoryFlushPolicy.cs` — `static FlushPolicy GetPolicy(OutputCategory)`: `Chat → Immediate`; everything else → `Batched`
- `Core/Output/PromptMessage.cs` — `IOutputMessage` shape: `string? StateLabel`, `IReadOnlyList<PoolDisplay> Pools`; `Category = OutputCategory.System`
- `Core/Output/PoolDisplay.cs` — `record PoolDisplay(string Name, int Current, int Max)`
- `Core/Output/IPromptSource.cs` — `PromptMessage? GetPrompt(uint playerEntityId)`
- `Core/Output/ISessionOutputBuffer.cs` — `Enqueue(IOutputMessage)`, `bool HasPending`, `Task FlushAsync()`
- `Core/Output/SessionOutputBuffer.cs` — thread-safe impl: lock on enqueue; atomic drain in `FlushAsync` (snapshot + clear under lock, then format+send outside lock); calls `IPromptSource.GetPrompt` and appends prompt if non-null. **`IPromptSource` is injected here** (into the session-lifetime buffer), not into `SessionBufferedOutputWriter` (the per-request thin wrapper) — so every `Create(session)` call returns a wrapper that shares the same buffer instance with its already-injected prompt source.
- `Core/Output/ISessionBufferRegistry.cs` — `ISessionOutputBuffer GetOrCreate(ISession)`, `void Release(Guid sessionId)`, `Task FlushAllPendingAsync()`
- `Core/Output/SessionBufferRegistry.cs` — `ConcurrentDictionary<Guid, SessionOutputBuffer>`; `GetOrCreate` constructs `SessionOutputBuffer(session, formatter, promptSource)` once per session; `FlushAllPendingAsync` iterates sessions with `HasPending` and calls `FlushAsync()`
- `Core/Output/IOutputWriter.cs` — add `Task FlushAsync()`
- `Core/Output/SessionBufferedOutputWriter.cs` — thin per-request wrapper around `ISessionOutputBuffer`; `WriteAsync` enqueues + auto-flushes if `Immediate`; `FlushAsync` delegates to buffer. Holds no `IPromptSource` reference.
- `Core/Output/OutputWriterFactory.cs` — inject `ISessionBufferRegistry` + `IPromptSource`; pass both to `SessionBufferRegistry.GetOrCreate` (which constructs the buffer with `IPromptSource` on first call); return `new SessionBufferedOutputWriter(buffer)`
- `Core/Output/OutputWriter.cs` — delete (replaced by `SessionBufferedOutputWriter`)
- `Core/Events/HandlerPriority.cs` — **read the current file first** to confirm no existing constant is 85, then add `OutputFlush = 85` (between `Notification = 80` and `Persistence = 90`)
- `Server/Program.cs` — register `SessionBufferRegistry` as `ISessionBufferRegistry` (singleton); register a null `IPromptSource` stub for now (replaced when WP-B lands)
- `docs/architecture/subsystems/output.md` — describe buffer model, `IPromptSource` port, `PromptMessage`, flush policy map

**Dependencies.** None — first package.

**Out of scope.** Prompt composer (WP-B); dispatcher and tick handler flush triggers (WP-C); formatter changes for `PromptMessage` rendering (WP-B); flow doc updates (WP-C).

**Exit criterion.** `dotnet build` green. `IOutputWriter.WriteAsync` routes messages to the session buffer. `IOutputWriter.FlushAsync` drains the buffer and sends each message; no prompt is appended yet (stub returns null). All `PlainMessage` call sites compile with an explicit `OutputCategory` argument. Existing command behavior (messages appear as before) is unchanged because the dispatcher still calls `FlushAsync` at the end of every path.

---

### WP-B — Prompt composer + formatter rendering

*(depends on WP-A; parallel with WP-C)*

**Scope.** `PromptComposerSystem` implementing `IPromptSource`; `TelnetOutputFormatter` update for `PromptMessage`; DI wiring.

**Files.**

- `Core/Modules/Prompt/Systems/PromptComposerSystem.cs` — `IPromptSource` implementation: `GetStates(entityId)` → state label (`InCombat → "(Fighting)"`, `Resting → "(Resting)"`, `Incapacitated → "(Incapacitated)"`, default → null); for each of `{HpCurrent/HpMax, ManaCurrent/ManaMax, StaminaCurrent/StaminaMax, AstraCurrent/AstraMax}` call `IStatSystem.Get(entityId, scoreId)`; skip pool if `max == 0`; return `new PromptMessage(stateLabel, pools)`
- `Core/Output/TelnetOutputFormatter.cs` — add `PromptMessage` case: render as `[((StateLabel) )]HP: x/y[ Mana: a/b]...` (state label in `<system>` color if present; pool values in default text)
- `Server/Program.cs` — replace stub `IPromptSource` registration with `PromptComposerSystem` (singleton)
- `docs/reference/systems.md` — add `PromptComposerSystem` entry

**Dependencies.** WP-A (for `IPromptSource`, `PromptMessage`, `PoolDisplay`).

**Out of scope.** Per-player prompt config (deferred to backlog). Additional pool types or contributor aggregation (shape-for-later per Design notes).

**Exit criterion.** `dotnet build` green. Running the server, logging in, and typing any command (e.g., `look`) shows the output followed by a prompt line like `HP: 100/100 Mana: 50/50 Stamina: 40/40 Astra: 30/30`. Typing `rest` shows `(Resting) HP: 100/100 ...`. Initiating combat shows `(Fighting) HP: ...`.

---

### WP-C — Flush triggers + session cleanup + flow updates

*(depends on WP-A; parallel with WP-B)*

**Scope.** Dispatcher command-end flush; `OutputFlushTickHandler`; disconnect cleanup in `PlayerSessionHandler`; all four flow doc updates; handler reference catalog update.

**Files.**

- `Core/Commands/CommandDispatcher.cs` — wrap `DispatchAsync` body in `try { ... } finally { await output.FlushAsync().ConfigureAwait(false); }` so flush runs on every exit path (success, parse-fail, unauthorized, refused, threw)
- `Core/Handlers/OutputFlushTickHandler.cs` — new handler on `HeartbeatTickEvent`; priority 85 (`HandlerPriority.OutputFlush`); calls `await _bufferRegistry.FlushAllPendingAsync()`
- `Core/Modules/Session/Handlers/PlayerSessionHandler.cs` — inject `ISessionBufferRegistry`; add `_bufferRegistry.Release(session.SessionId)` at end of `PlayerDisconnectedEvent` handling
- `Server/Program.cs` — register `OutputFlushTickHandler` as `IEventHandler<HeartbeatTickEvent>` (singleton)
- `docs/architecture/flows/flow-03-player-command-lifecycle.md` — add buffer flush step after command execution; update mermaid; note prompt is appended by buffer; update cross-reference section: `Core/Output/OutputWriter.cs` → `Core/Output/SessionBufferedOutputWriter.cs` (the old file is deleted by WP-A)
- `docs/architecture/flows/flow-06-output-rendering.md` — add enqueue → policy → flush → prompt steps; update mermaid
- `docs/architecture/flows/flow-16-heartbeat-tick.md` — add `OutputFlushTickHandler` (p=85) participant; update steps and mermaid
- `docs/architecture/flows/flow-18-combat-round-pulse.md` — note that `CombatMessage` is now batched; prompt appears at tick-end not inline; update mermaid
- `docs/reference/handlers.md` — add `OutputFlushTickHandler` entry

**Dependencies.** WP-A (for `IOutputWriter.FlushAsync`, `ISessionBufferRegistry`, `HandlerPriority.OutputFlush`).

**Note.** `Server/Program.cs` is also touched by WP-B (replacing the `IPromptSource` stub). When the two packages land, one will produce a trivial three-line merge conflict in `Program.cs` — resolve by keeping both the `PromptComposerSystem` registration and the `OutputFlushTickHandler` registration.

**Out of scope.** Prompt rendering (WP-B); buffer infrastructure (WP-A).

**Exit criterion.** `dotnet build` green. Running the server: (a) typing any command — output appears followed by one prompt with no duplicate prompts; (b) initiating combat and waiting through a tick — combat messages batch then appear with one trailing prompt; (c) disconnecting and reconnecting — no memory growth from orphaned buffers.

---

## Content tooling impact

None. This slice adds no gameplay state that a designer authors via data files or admin commands. The prompt format is fixed code; per-player prompt configuration is deferred to the backlog ("Multi-step command prompts and player config"). The session buffer is transient infrastructure with no schema, template registry entries, or admin commands.

## Cross-cutting surfaces stressed

| Surface | Classification | Rationale |
|---|---|---|
| **Output framework** (`IOutputWriter`, `IOutputWriterFactory`, `OutputCategory`) | **Gap exposed — resolved this slice** | The per-request stateless `OutputWriter` cannot batch across sources. This slice replaces it with a session-scoped `SessionBufferedOutputWriter` + `SessionBufferRegistry`. INV-19: new player-facing surface (prompt) + the batching pattern would otherwise be hand-rolled per feature. |
| **Command framework** (`CommandDispatcher`) | **Adequate** | One `try/finally` flush call added to the existing dispatch body. No structural change to `ICommandDispatcher` or the command pipeline. |
| **Event bus / handlers** (`HeartbeatTickEvent`, `HandlerPriority`) | **Gap exposed — resolved this slice** | `HandlerPriority` lacked a flush-tier constant. `OutputFlush = 85` added. New handler follows existing `HeartbeatTickEvent` pattern. |
| **Session lifecycle** (`ISession`, `PlayerSessionHandler`) | **Gap exposed — resolved this slice** | Buffer must be released on disconnect to prevent memory leak. `PlayerSessionHandler` calls `ISessionBufferRegistry.Release` — minimal surgical change. |
| **ECS / EntityService** | **Adequate** | Composer uses read-only `IEntityStateService.GetStates` and `IStatSystem.Get`; no ECS mutation. |
| **Persistence** | **Adequate** | Buffer is transient (in-memory); no persistence calls, no `[Persistent]` components added. |
| **BroadcastSystem** | **Adequate** | `IBroadcastSystem` calls `IOutputWriterFactory.Create(session)` per recipient (existing pattern). The factory now returns a buffered writer; no `IBroadcastSystem` change needed. Chat-category broadcasts auto-flush immediately via the policy; combat-category broadcasts batch until tick-end. |

## Flows introduced or modified

| Flow | Change |
|---|---|
| **Flow 3 — Player command lifecycle** | Add buffer flush step in `DispatchAsync` `finally` block; note prompt is appended by `FlushAsync`; update mermaid |
| **Flow 6 — Output rendering** | Add enqueue → policy check → flush path; document `ISessionOutputBuffer` and `IPromptSource`; update mermaid |
| **Flow 16 — Heartbeat tick** | Add `OutputFlushTickHandler` (p=85) as the final tick subscriber; update mermaid and steps |
| **Flow 18 — Combat round pulse** | Note `CombatMessage` is now buffered (not immediately sent); prompt appears at tick-end from `OutputFlushTickHandler`; update mermaid |

No new canonical flow is introduced — the flush mechanism is folded into the four existing flows listed above.

## Reference catalog updates

- **`docs/reference/systems.md`** — add `SessionBufferRegistry` (core), `PromptComposerSystem` (domain), `IPromptSource` port; update `Output Infrastructure` entry to reflect `IOutputWriter.FlushAsync` and the buffer model
- **`docs/reference/handlers.md`** — add `OutputFlushTickHandler`
- **`docs/architecture/subsystems/output.md`** — update with buffer architecture, `IPromptSource` port, `PromptMessage` shape, flush policy map, thread-safety note

## Design notes

*(Durable rationale — kept on ship per [INV-D2](../architecture/checklist.md).)*

- **Two layers, decoupled by a port — the central decision.** The *flush mechanism* (a per-session buffer that coalesces and emits typed messages) is pure transport plumbing → it stays **core-tier** in `Core/Output/`, carrying no domain dependency. The *prompt content* is a projection of entity **state + pools** → it is a **domain read** and cannot sit in core ([INV-2](../architecture/checklist.md)). They are joined by a **core-owned `IPromptSource` port** ([INV-24](../architecture/checklist.md)): the core buffer calls the port at flush; a domain-aware composer implements it. This is what lets the buffer self-flush an immediate-category message (a `say`) and still append a prompt without the core knowing anything about state or pools.
- **The prompt is computed on read, never cached.** The composer builds a fresh `PromptMessage` at each flush from current state + pools. This is why no "prompt dirty" flag and no `PromptChangedEvent` are needed: a prompt composed *after* the tick's mutations automatically reflects the post-round HP. Caching the prompt would re-introduce the "did I recompute when HP changed?" bug family ([INV-24](../architecture/checklist.md), compute-on-read).
- **The buffer is session-scoped, not request-scoped.** Today `IOutputWriterFactory.Create(session)` yields a stateless, per-request writer. Cross-source batching requires a **stateful buffer whose lifetime is the session**, because command output, async room broadcasts, and heartbeat combat output for one player must all accumulate in the *same* buffer. This is the structural prerequisite for batching.
- **Flush policy is keyed off `OutputCategory`.** The existing `OutputCategory` on every `IOutputMessage` is the natural classifier: `Chat` flushes immediately; all other categories (`System`, `Help`, `Info`, `Combat`) batch to the next explicit flush boundary (command-end or tick-end). No parallel taxonomy — the category the message already carries decides its flush behavior.
- **Every flush appends one prompt.** Chat flushes, command-end flushes, and tick-end flushes all append one prompt. If several `say` messages arrive in rapid succession, each triggers its own immediate flush + prompt — accepted consequence, revisit in play-test if prompt spam becomes noticeable.
- **Resolved: `EntityStateFlags` with prompt labels.** `InCombat → (Fighting)`, `Resting → (Resting)`, `Incapacitated → (Incapacitated)`. Normal state (no flags) → no label. Additional flags (future states like `Mounted`, `Stunned`) get labels when those slices land; no change needed to the buffer or `IPromptSource` interface.
- **Resolved: single `Combat` output category.** A single new `Combat` category (Batched) covers combat and future effect/tick output. If effect messages need a distinct visual treatment later, a separate `Effect` category can be added without changing the flush policy infrastructure.
- **Pools are read generically by `ScoreId`.** The composer reads each pool via `IStatSystem.Get(entityId, ScoreId)` (the generalized seam that already exists) rather than hardcoding HP, so the prompt naturally covers HP/Mana/Stamina/Astra and any future pool with no composer change.
- **`PromptMessage` is a typed shape, not a pre-stringified line.** It carries the state label + pool tuples; the telnet formatter renders it as text now, and a future SignalR/web formatter renders the same message as structured gauges — the output framework's transport-key design ([INV-11](../architecture/checklist.md), [`subsystems/output.md`](../architecture/subsystems/output.md) "Seams for future transports").
- **The buffer is thread-safe; flush is atomic.** Three threads can touch one session's buffer concurrently — this player's read loop (their command), other players' read loops (their `say` broadcasting here), and the heartbeat thread (combat/tick). The buffer holds a lock only during the atomic drain (snapshot-and-clear), releasing it before async I/O. Idempotent-flush correctness: if a `Chat` immediate flush and the command-end `finally` flush race, the first drain atomically empties the buffer; the second drain finds nothing and appends only a prompt — one extra prompt per race, same as rapid `say` sequences. Accepted consequence. This is a concrete new site for the Phase-4 thread-safety review (backlog).
- **Contributor growth (shape-for-later).** Prompt content is a single composer today. If ≥3 sources later want to inject prompt segments (a target's HP bar, a status-effect row, an XP bar), `IPromptSource` generalizes into an `IPromptContributor` aggregation (the [INV-24](../architecture/checklist.md) shape) with no caller change. Note it; don't build it.

## Architecture brief

*(In-flight; trimmed on ship.)*

### Seams & recommended homes

| Seam | What it owns | Layer / home | Disposition |
|---|---|---|---|
| **Per-session output buffer** | accumulates `IOutputMessage`s; `FlushAsync()` drains + appends one prompt; category-keyed flush policy; thread-safe | **core** — `Core/Output/` (replaces immediate `OutputWriter`), session-lifetime | **Build now** |
| **`IPromptSource` port** | core-owned interface the buffer calls at flush to obtain the `PromptMessage` | **core** interface (`Core/Output/`), domain implementation | **Build now** |
| **Prompt composer** | reads `IEntityStateService` + `IStatSystem.Get(ScoreId)`; builds `PromptMessage` fresh (compute-on-read) | **domain-aware** (`Core/Modules/Prompt/`) — *not* core | **Build now** |
| **`PromptMessage`** | typed shape: state label (omitted when `None`) + per-pool cur/max tuples | **core** message shape (`Core/Output/`), rendered per-transport | **Build now** |
| **Tick flush trigger** | end-of-tick: flush sessions with pending batched output | **handler** on `HeartbeatTickEvent`, priority 85 (`HandlerPriority.OutputFlush`) | **Build now** |
| **Command flush trigger** | end-of-dispatch: `try/finally` in `CommandDispatcher.DispatchAsync` | **initiator** — `CommandDispatcher` | **Build now** |
| **`OutputCategory.Combat` + flush-policy map** | one new category; static `CategoryFlushPolicy` lookup | **core** enum + small policy type | **Build now** |

### Family / forward map

Batching is **not** combat-specific — past the ≥3× [INV-19](../architecture/checklist.md) bar: effect ticks ("burned, poisoned"), AoE ("scorches 4 foes"), future mob-AI pushes, weather/world-events. Downstream consumers: **combat verbosity / message tuning** (backlog), **channels + tell/whisper/yell** (Chat-category flush), **player config** (backlog, prompt format), **web/SignalR client** (backlog, typed `PromptMessage`). Building the general buffer now is *less* total code than re-solving coalescing per feature.

### Invariants in tension

- **[INV-2](../architecture/checklist.md)** — core buffer must not read domain state → resolved by the **[INV-24](../architecture/checklist.md)** `IPromptSource` port (the load-bearing pairing).
- **[INV-19](../architecture/checklist.md)** — new player-facing surface (prompt) + ≥3× coalescing → framework lands this slice.
- **[INV-11](../architecture/checklist.md)** — prompt and batched combat output are typed messages, never hand-rolled strings.
- **[INV-7](../architecture/checklist.md)** — flush is the latest tick step; drain-then-prompt order within each flush.
- **[INV-16](../architecture/checklist.md)/[INV-17](../architecture/checklist.md)** — `OutputCategory` + new shapes/handlers update `reference/`; Flows 3, 6, 16, 18 updated.

### Resolved decisions (do not relitigate)

1. **Batch breadth = general per-session buffer** (not combat-only aggregation).
2. **Flush latency = category-aware** — `Chat` (and `say`/tell broadcasts) flush immediately; `System`/`Help`/`Info`/`Combat` batch to the next explicit flush boundary. Accepted consequence: a chat flush mid-round emits its own prompt.
3. **Prompt config = fixed format now**, per-player config deferred to the backlog "Multi-step command prompts and player config" item.
4. **Every flush appends a prompt**, including Chat immediate flushes. Revisit in play-test if prompt spam from rapid `say` sequences is annoying in practice.
5. **`EntityStateFlags` labels**: `InCombat → (Fighting)`, `Resting → (Resting)`, `Incapacitated → (Incapacitated)`; other flags get labels when their slices land.
6. **Single `Combat` category** for all tick-batched output; effect output reuses the same category for now.
7. **`HandlerPriority.OutputFlush = 85`** (between `Notification = 80` and `Persistence = 90`); the flush runs after all output-producing handlers on any event.

## Open questions

None — all open questions from the architecture-advisor seed have been resolved. See Resolved decisions above.

## Related

- [`subsystems/output.md`](../architecture/subsystems/output.md) · [`subsystems/commands.md`](../architecture/subsystems/commands.md)
- Flows [3](../architecture/flows/flow-03-player-command-lifecycle.md), [6](../architecture/flows/flow-06-output-rendering.md), [16](../architecture/flows/flow-16-heartbeat-tick.md), [18](../architecture/flows/flow-18-combat-round-pulse.md)
- [`entity-state-management.md`](entity-state-management.md) · [`stat-resource-substrate.md`](stat-resource-substrate.md)
- [`feature-horizon.md`](../design/feature-horizon.md) §5 (combat verbosity), §10 (social/channels)
