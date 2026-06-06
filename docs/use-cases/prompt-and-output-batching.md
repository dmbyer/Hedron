# Player Prompt & Output Batching

**Status:** implemented

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
3. `CombatHandler` (p=20 on `CombatRoundEvent`) writes `PlainMessage`s (category=`System`, policy=`Batched`) to affected sessions via `IBroadcastSystem` → `IOutputWriterFactory.Create(session)` → `SessionBufferedOutputWriter.WriteAsync`. Messages enqueue in each session's buffer. (`OutputCategory.Combat` is available for a future dedicated message type; current handlers use `System`.)
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

## Design notes

*(Durable rationale — kept on ship per [INV-D2](../architecture/checklist.md).)*

- **Two layers, decoupled by a port — the central decision.** The *flush mechanism* (a per-session buffer that coalesces and emits typed messages) is pure transport plumbing → it stays **core-tier** in `Core/Output/`, carrying no domain dependency. The *prompt content* is a projection of entity **state + pools** → it is a **domain read** and cannot sit in core ([INV-2](../architecture/checklist.md)). They are joined by a **core-owned `IPromptSource` port** ([INV-24](../architecture/checklist.md)): the core buffer calls the port at flush; a domain-aware composer implements it. This is what lets the buffer self-flush an immediate-category message (a `say`) and still append a prompt without the core knowing anything about state or pools.
- **The prompt is computed on read, never cached.** The composer builds a fresh `PromptMessage` at each flush from current state + pools. This is why no "prompt dirty" flag and no `PromptChangedEvent` are needed: a prompt composed *after* the tick's mutations automatically reflects the post-round HP. Caching the prompt would re-introduce the "did I recompute when HP changed?" bug family ([INV-24](../architecture/checklist.md), compute-on-read).
- **The buffer is session-scoped, not request-scoped.** Today `IOutputWriterFactory.Create(session)` yields a stateless, per-request writer. Cross-source batching requires a **stateful buffer whose lifetime is the session**, because command output, async room broadcasts, and heartbeat combat output for one player must all accumulate in the *same* buffer. This is the structural prerequisite for batching.
- **Flush policy is keyed off `OutputCategory`.** The existing `OutputCategory` on every `IOutputMessage` is the natural classifier: `Chat` flushes immediately; all other categories (`System`, `Help`, `Info`, `Combat`) batch to the next explicit flush boundary (command-end or tick-end). No parallel taxonomy — the category the message already carries decides its flush behavior.
- **Every flush appends one prompt.** Chat flushes, command-end flushes, and tick-end flushes all append one prompt. If several `say` messages arrive in rapid succession, each triggers its own immediate flush + prompt — accepted consequence, revisit in play-test if prompt spam becomes noticeable.
- **Resolved: `EntityStateFlags` with prompt labels.** `InCombat → (Fighting)`, `Resting → (Resting)`, `Incapacitated → (Incapacitated)`. Normal state (no flags) → no label. Additional flags (future states like `Mounted`, `Stunned`) get labels when those slices land; no change needed to the buffer or `IPromptSource` interface.
- **Resolved: single `Combat` output category.** A single new `Combat` category (Batched) is added for future use. If effect messages or combat narration need a distinct visual treatment later, a separate message type using `OutputCategory.Combat` can be added without changing the flush policy infrastructure.
- **Pools are read generically by `ScoreId`.** The composer reads each pool via `IStatSystem.Get(entityId, ScoreId)` (the generalized seam that already exists) rather than hardcoding HP, so the prompt naturally covers HP/Mana/Stamina/Astra and any future pool with no composer change.
- **`PromptMessage` is a typed shape, not a pre-stringified line.** It carries the state label + pool tuples; the telnet formatter renders it as text now, and a future SignalR/web formatter renders the same message as structured gauges — the output framework's transport-key design ([INV-11](../architecture/checklist.md), [`subsystems/output.md`](../architecture/subsystems/output.md) "Seams for future transports").
- **The buffer is thread-safe; flush is atomic.** Three threads can touch one session's buffer concurrently — this player's read loop (their command), other players' read loops (their `say` broadcasting here), and the heartbeat thread (combat/tick). The buffer holds a lock only during the atomic drain (snapshot-and-clear), releasing it before async I/O. Idempotent-flush correctness: if a `Chat` immediate flush and the command-end `finally` flush race, the first drain atomically empties the buffer; the second drain finds nothing and appends only a prompt — one extra prompt per race, same as rapid `say` sequences. Accepted consequence. This is a concrete new site for the Phase-4 thread-safety review (backlog).
- **Contributor growth (shape-for-later).** Prompt content is a single composer today. If ≥3 sources later want to inject prompt segments (a target's HP bar, a status-effect row, an XP bar), `IPromptSource` generalizes into an `IPromptContributor` aggregation (the [INV-24](../architecture/checklist.md) shape) with no caller change. Note it; don't build it.

## Related

- [`subsystems/output.md`](../architecture/subsystems/output.md) · [`subsystems/commands.md`](../architecture/subsystems/commands.md)
- Flows [3](../architecture/flows/flow-03-player-command-lifecycle.md), [6](../architecture/flows/flow-06-output-rendering.md), [16](../architecture/flows/flow-16-heartbeat-tick.md), [18](../architecture/flows/flow-18-combat-round-pulse.md)
- [`entity-state-management.md`](entity-state-management.md) · [`stat-resource-substrate.md`](stat-resource-substrate.md)
- [`feature-horizon.md`](../design/feature-horizon.md) §5 (combat verbosity), §10 (social/channels)
