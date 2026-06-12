# Flow 16 — Heartbeat tick

> [Back to flows index](README.md)

**Summary.** `HeartbeatBackgroundService` fires a `PeriodicTimer` at `Heartbeat:IntervalMs` (default 2000 ms), increments a monotonic counter, and publishes `HeartbeatTickEvent` to `IEventBus`. No game logic lives here; handlers subscribe independently. `EffectTickHandler` (slice 9-e, p=20), `CombatTickHandler` (slice 9, p=20), `AbilityCooldownTickHandler` (slice 11-a, p=20), and `RegenerationTickHandler` (slice 11-c, p=20) are output-producing subscribers. `OutputFlushTickHandler` (WP-C, p=85) is the final meaningful tick subscriber — it flushes all session buffers accumulated during the tick and appends one prompt per session.

**Trigger.** `PeriodicTimer.WaitForNextTickAsync` returns in `HeartbeatBackgroundService.ExecuteAsync`.

```mermaid
sequenceDiagram
    participant Timer as PeriodicTimer
    participant HBS as HeartbeatBackgroundService
    participant Bus as IEventBus
    participant ETH as EffectTickHandler (p=20)
    participant CTH as CombatTickHandler (p=20)
    participant ACTH as AbilityCooldownTickHandler (p=20)
    participant RTH as RegenerationTickHandler (p=20)
    participant OFTH as OutputFlushTickHandler (p=85)
    participant Reg as ISessionBufferRegistry

    Timer->>HBS: WaitForNextTickAsync → true
    HBS->>HBS: increment _tickId, capture Timestamp, compute Elapsed
    HBS->>Bus: PublishAsync(HeartbeatTickEvent{TickId, Timestamp, Elapsed})
    Bus->>ETH: HandleAsync → effect tick (see Flow 21)
    Bus->>CTH: HandleAsync → combat round processing (see Flow 18)
    Bus->>ACTH: HandleAsync → ability cooldown decrement
    Bus->>RTH: HandleAsync → baseline resource regeneration sweep
    Bus->>OFTH: HandleAsync (p=85)
    OFTH->>Reg: FlushAllPendingAsync()
    loop per session with HasPending
        Reg->>Reg: buffer.FlushAsync() → drain + format + send + append prompt
    end
    HBS->>HBS: WaitForNextTickAsync (next tick)
```

**Steps.**

1. `PeriodicTimer.WaitForNextTickAsync(stoppingToken)` returns `true` (or throws `OperationCanceledException` on host shutdown → service exits).
2. `HeartbeatBackgroundService` increments `_tickId` (starts at 1 on first tick), captures `DateTimeOffset.UtcNow` as `now`, computes `Elapsed = now - _lastTimestamp`, and updates `_lastTimestamp = now`. `_lastTimestamp` is initialized to `DateTimeOffset.UtcNow` before the loop so the first tick's `Elapsed` reflects the actual interval.
3. Publishes `HeartbeatTickEvent { TickId, Timestamp, Elapsed }` via `IEventBus.PublishAsync`. Any uncaught exception from handlers is caught and logged at `Error`; the loop continues.
4. Event bus dispatches to all subscribed handlers in priority order:
   - `EffectTickHandler` (priority 20) runs effect periodic application and expiry (see [Flow 21](flow-21-effect-tick.md)). May write `Combat`-category messages to session buffers.
   - `CombatTickHandler` (priority 20) runs combat round processing (see [Flow 18](flow-18-combat-round-pulse.md)). `CombatHandler` writes `PlainMessage`s (category=`System`, policy=`Batched`) to affected session buffers. (`OutputCategory.Combat` is reserved for a future dedicated message type.)
   - `AbilityCooldownTickHandler` (priority 20) calls `IAbilitySystem.AdvanceCooldowns(@event.Elapsed)` to decrement per-ability cooldown timers (slice 11-a). No output.
   - `RegenerationTickHandler` (priority 20) calls `IRegenerationSystem.ApplyTickRegen(@event.TickId)` — a no-chain sweep that applies baseline HP/Mana/Stamina/Astra regeneration to all out-of-combat entities (slice 11-c). No output.
   - `OutputFlushTickHandler` (priority **85** — `HandlerPriority.OutputFlush`) runs last among output-producing subscribers. Calls `ISessionBufferRegistry.FlushAllPendingAsync()`. For each session with `HasPending`: atomically drains the buffer, formats and sends each message, then calls `IPromptSource.GetPrompt` and appends one `PromptMessage`. Players see all that tick's combat/effect messages followed by a single prompt reflecting post-round pools. Future subscribers (mob AI at p=95, persistence at p=90, etc.) slot in at their own priorities.
5. Control returns to `WaitForNextTickAsync`. `PeriodicTimer` schedules the next tick relative to its period, not relative to when handler execution completed — overruns cause the next tick to fire immediately after the current one completes.

**Overrun.** If handler execution takes longer than `IntervalMs`, `PeriodicTimer` fires the next tick immediately after the current completes (no drift accumulation, but no backpressure either). Acknowledged for Phase 4 hardening.

**Thread safety.** `ExecuteAsync` runs on a background thread. `IEventBus.PublishAsync` is called from that thread — the same cross-thread pattern used by `PersistenceFlushTimer` and `WorldContentBootstrap`. Single `PeriodicTimer` means no concurrent self-publish. Phase 4 thread-safety review covers the event bus under concurrent background-service access and the session buffer's lock under concurrent enqueue from the heartbeat thread and player read loops.

**Cross-references.**
- [`Core/Modules/Time/Events/HeartbeatTickEvent.cs`](../../../Core/Modules/Time/Events/HeartbeatTickEvent.cs)
- [`Server/HeartbeatBackgroundService.cs`](../../../Server/HeartbeatBackgroundService.cs)
- [`Core/Handlers/OutputFlushTickHandler.cs`](../../../Core/Handlers/OutputFlushTickHandler.cs) — tick-end flush trigger (WP-C)
- [`Core/Output/ISessionBufferRegistry.cs`](../../../Core/Output/ISessionBufferRegistry.cs) — registry flushed by `OutputFlushTickHandler`
- [`Core/Modules/Abilities/Handlers/AbilityCooldownTickHandler.cs`](../../../Core/Modules/Abilities/Handlers/AbilityCooldownTickHandler.cs) — slice 11-a
- [`Core/Modules/Regeneration/Handlers/RegenerationTickHandler.cs`](../../../Core/Modules/Regeneration/Handlers/RegenerationTickHandler.cs) — slice 11-c
- [`docs/implementation-plans/time-system.md`](../../implementation-plans/time-system.md) — slice 9-b spec; [`docs/implementation-plans/prompt-and-output-batching.md`](../../implementation-plans/prompt-and-output-batching.md) — output batching spec
