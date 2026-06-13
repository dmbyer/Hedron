# Time System (Heartbeat)

> The shared game clock: a background service that fires a `PeriodicTimer` and publishes `HeartbeatTickEvent` on each tick. All game-logic consumers subscribe independently. **Authoring checkpoint:** slice 9-b. Living document.

## What it is / does

`HeartbeatBackgroundService` is an **Initiator** (a `BackgroundService`) — it drives the tick loop but contains no game logic. It publishes `HeartbeatTickEvent { TickId, Timestamp, Elapsed }` at a configurable interval. Combat rounds, effect ticks, mob AI, spawn respawns, and out-of-combat regeneration all subscribe to this event independently; the heartbeat is model-agnostic. Switching the combat model from autocombat to freeform means a handler unsubscribes — the heartbeat continues for all other consumers unchanged.

## How it works

### Tick loop

1. `HeartbeatBackgroundService.ExecuteAsync` creates a `PeriodicTimer` with period from `IConfiguration["Heartbeat:IntervalMs"]` (default 2000 ms).
2. On each `WaitForNextTickAsync`: increments `_tickId` (starts at 1; `TickId = 0` is the "no tick yet" sentinel), captures `DateTimeOffset.UtcNow` as `Timestamp`, computes `Elapsed = Timestamp - _lastTimestamp`.
3. Publishes `HeartbeatTickEvent` to `IEventBus`.
4. An inner `try/catch(Exception)` around `PublishAsync` logs handler exceptions at `Error` without stopping the loop — a single misbehaving handler cannot kill the heartbeat.
5. On `CancellationToken` cancellation (host shutdown), the outer loop exits normally.

`PeriodicTimer` does not drift on handler overrun: if tick handlers exceed `IntervalMs`, the next tick fires immediately after the current completes — no accumulated backpressure.

### Startup ordering

`HeartbeatBackgroundService` is registered **last** in `Server/Program.cs` (after `TelnetServer`) so the first tick cannot land before the world is fully seeded and the listener is open. See [Server startup (flow-01)](../../architecture/flows/flow-01-server-startup.md).

### `IHeartbeatService` interface

Omitted by design. `BackgroundService.StartAsync`/`StopAsync` are called by the .NET host automatically; no in-game caller references the heartbeat by interface. If Phase 4 test coverage requires a mock, the interface can be introduced alongside the test harness without breaking changes.

### Module split

`HeartbeatBackgroundService` lives in `Server/` (a hosting abstraction). `HeartbeatTickEvent` lives in `Core/Modules/Time/Events/` so any `Core/` handler can subscribe without a `Server/` dependency. `TimeModule` (`Core/Modules/Time/TimeModule.cs`) is the Core-side DI anchor.

## Heartbeat consumers (current)

| Handler | Priority | What it does |
|---|---|---|
| `EffectTickHandler` | 20 | Advances effect timers, applies periodic pool changes, publishes expiry events |
| `CombatTickHandler` | 20 | Drives autocombat rounds for all active pairs |
| `RegenerationSystem` (called by handler) | — | Applies out-of-combat pool regen |
| `SpawnSystem` | 95 | Checks spawn slots for due respawns |

## Event

| Event | Publisher | Payload |
|---|---|---|
| `HeartbeatTickEvent` | `HeartbeatBackgroundService` | `long TickId`, `DateTimeOffset Timestamp`, `TimeSpan Elapsed` |

## Considerations

- **Thread safety is acknowledged debt.** `ExecuteAsync` calls `IEventBus.PublishAsync` from a background thread — the same cross-thread pattern as `PersistenceFlushTimer`. Single `PeriodicTimer` means no concurrent self-publish. The Phase 4 thread-safety review covers the event bus under concurrent background-service access. Tracked in [`../../roadmap/backlog.md`](../../roadmap/backlog.md).
- **`PeriodicTimer` overrun** — logged as a Phase 4 hardening concern. No backpressure mechanism today.
- **Configuration key `Heartbeat:IntervalMs`** is Category 1 (operational). See [`../../architecture/05-configuration.md`](../../architecture/05-configuration.md).

## Extensibility

- **Per-entity cooldown tracking** — an `ICooldownSystem` subscribes to `HeartbeatTickEvent`, queries `CooldownComponent` entities, decrements counters. The heartbeat payload (`Elapsed`) is sufficient; no new timer abstraction needed.
- **Named timers** (respawn timers, shop restocking, buff expiry) — all subscribe to `HeartbeatTickEvent` and maintain their own state. No separate timer abstraction needed.

## Related

- [`world.md`](world.md) — holistic feature view.
- [Heartbeat tick (flow-16)](../../architecture/flows/flow-16-heartbeat-tick.md) — the full tick lifecycle: handler dispatch ordering and what each subscriber does.
- [`../../reference/systems.md`](../../reference/systems.md) — `HeartbeatBackgroundService` catalog row (Background Services / Initiators section).
- [`../../roadmap/completed/slice-9b-time-system.md`](../../roadmap/completed/slice-9b-time-system.md) — as-built history and design decisions.
- **Consumers:** [`../effects/effect-system.md`](../effects/effect-system.md) (effect tick) · [`../combat/combat.md`](../combat/combat.md) (combat round pulse) · [`spawn-system.md`](spawn-system.md) (respawn).
