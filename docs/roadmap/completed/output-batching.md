# Phase 3 — Output Batching + Player Prompt

**PR:** (this branch — `claude/clever-franklin-1a33c3`) · **Spec:** [`../../use-cases/prompt-and-output-batching.md`](../../use-cases/prompt-and-output-batching.md)

## Outcome

Replaced the per-request stateless `OutputWriter` with a session-scoped output buffer that coalesces messages from any source (command response, room broadcast, combat tick, effect tick) and flushes them as a batch at defined boundaries: command-end (via a `try/finally` in `CommandDispatcher`) or tick-end (via `OutputFlushTickHandler` at priority 85). Each flush appends one freshly-composed status prompt showing the player's current state label (`(Fighting)`, `(Resting)`, `(Incapacitated)`) and HP/Mana/Stamina/Astra pool values. Chat-category messages flush immediately so conversations feel snappy; all other categories batch to the next boundary.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `ISessionOutputBuffer` / `SessionOutputBuffer` | `Core/Output/` | Per-session message list; thread-safe enqueue; atomic drain-on-flush; calls `IPromptSource` after drain |
| `ISessionBufferRegistry` / `SessionBufferRegistry` | `Core/Output/` | Singleton `ConcurrentDictionary<Guid, SessionOutputBuffer>`; `GetOrCreate`, `Release`, `FlushAllPendingAsync` |
| `IPromptSource` | `Core/Output/` | Core-owned port; separates the flush mechanism (core) from prompt content (domain) per INV-2/INV-24 |
| `PromptMessage` / `PoolDisplay` | `Core/Output/` | Typed output shapes: state label + per-pool cur/max tuples; rendered by transport formatters |
| `SessionBufferedOutputWriter` | `Core/Output/` | Thin per-request wrapper; enqueues + auto-flushes Chat-category; delegates `FlushAsync` to buffer |
| `CategoryFlushPolicy` / `FlushPolicy` | `Core/Output/` | Static lookup: `Chat → Immediate`, all others → `Batched` |
| `OutputCategory.Combat` | `Core/Output/OutputCategory.cs` | New value (Batched) reserved for future typed combat message shapes |
| `HandlerPriority.OutputFlush = 85` | `Core/Events/HandlerPriority.cs` | Slots between `Notification = 80` and `Persistence = 90` |
| `IOutputWriter.FlushAsync()` | `Core/Output/IOutputWriter.cs` | New interface method; delegates to session buffer |
| `OutputWriterFactory` | `Core/Output/OutputWriterFactory.cs` | Injects `ISessionBufferRegistry`; returns `SessionBufferedOutputWriter` |
| `OutputWriter.cs` | (deleted) | Replaced by `SessionBufferedOutputWriter` |
| `PromptComposerSystem` | `Core/Modules/Prompt/Systems/` | Implements `IPromptSource`; reads `IEntityStateService.GetStates` + `IStatSystem.Get(ScoreId)` per pool; compute-on-read |
| `OutputFlushTickHandler` | `Core/Handlers/OutputFlushTickHandler.cs` | `HeartbeatTickEvent`, p=85; calls `ISessionBufferRegistry.FlushAllPendingAsync()` |
| `CommandDispatcher.DispatchAsync` | `Core/Commands/CommandDispatcher.cs` | Outer `try/finally` calls `output.FlushAsync()` on every exit path |
| `PlayerSessionHandler` | `Core/Modules/Session/Handlers/` | Injects `ISessionBufferRegistry`; calls `Release(session.SessionId)` on disconnect |
| `TelnetOutputFormatter` | `Core/Output/TelnetOutputFormatter.cs` | Added `PromptMessage` case: state label in `<system>` color + pool pairs |
| `PlainMessage` constructor | `Core/Output/PlainMessage.cs` | Now requires explicit `OutputCategory` third argument; all call sites updated (~40 files) |
| Flows 3, 6, 16, 18 updated | `docs/architecture/flows/` | Buffer/flush/prompt path documented; `OutputFlushTickHandler` added to tick flow |
| `docs/architecture/subsystems/output.md` | updated | Buffer architecture, `IPromptSource` port, flush policy map, thread-safety note |
| `docs/reference/systems.md` | updated | `PromptComposerSystem` entry added |
| `docs/reference/handlers.md` | updated | `OutputFlushTickHandler` entry added |
| `add-command` skill | `.claude/skills/add-command/SKILL.md` | `DrinkCommand` example updated to three-argument `PlainMessage` |

## Spec-review provenance

Code-mode architecture review ran after all three work packages landed. Two blocking findings, three non-blocking.

**Blocking (both fixed):**
- INV-20: `add-command/SKILL.md` `DrinkCommand` example used the obsolete two-argument `PlainMessage` constructor → updated to three-argument form with `OutputCategory`.
- INV-D2: use-case doc still read `Status: planned` → trimmed to implemented state; in-flight sections removed.

**Non-blocking (all fixed):**
- INV-D1: `subsystems/output.md` referenced a `NullPromptSource` stub that was never created (implementation went straight to `PromptComposerSystem`) → description corrected.
- INV-17: flow-16 and flow-18 claimed `CombatMessage` type and `OutputCategory.Combat` for combat handler output; actual handlers write `PlainMessage(System)` → corrected in both flow docs.

## Notable design points

- **`IPromptSource` is the load-bearing seam.** The core buffer cannot read domain state (INV-2), so the prompt content is injected via a core-owned port. The buffer calls the port at flush; `PromptComposerSystem` implements it. Future multi-segment prompts generalize to `IPromptContributor` aggregation with no interface change to the buffer.
- **Compute-on-read, no caching.** `GetPrompt` is called fresh at every flush from live entity state + pools. A prompt composed after the tick's mutations automatically reflects post-round HP — no "did I recompute?" flag needed.
- **`OutputCategory.Combat` is defined but not yet exercised.** Current combat handlers write `PlainMessage(System)` (Batched by `CategoryFlushPolicy`). `Combat` is reserved for a future dedicated `CombatMessage` shape that may need separate visual treatment.
- **Race idempotence.** If a Chat immediate flush and the command-end `finally` flush race, the first atomic drain empties the buffer; the second finds nothing and appends only a prompt. One extra prompt per race — accepted consequence, same as rapid `say` sequences.
- **Phase 4 thread-safety review site.** Three threads can touch one session buffer concurrently (player read loop, peer read loop for broadcasts, heartbeat). Lock only during atomic drain; I/O outside lock. Logged as a concrete review target.

## Deviations from the use-case doc

None — shipped per spec. Implementation went directly to `PromptComposerSystem` without the intermediate `NullPromptSource` stub described in WP-A's planned rollout (the stub was never needed because both packages landed in the same PR).

## Follow-ups unlocked

- **Slice 12 (Shopping):** The prompt will now trail every buy/sell command output automatically — no output infrastructure work needed.
- **Web/SignalR client (backlog):** `PromptMessage` is a typed shape; a future web formatter renders pool values as structured gauges without changing the core buffer or composer.
- **Per-player prompt config (backlog):** The `IPromptSource` interface and `PromptMessage` shape accommodate per-player format preferences with no buffer change.
- **`OutputCategory.Combat` + `CombatMessage` (backlog):** Combat verbosity tuning can introduce a typed `CombatMessage` and `OutputCategory.Combat` without changing `CategoryFlushPolicy` infrastructure.
