# Time System (Heartbeat) — Phase 3 slice 9-b

## Status

`implemented`

## Actors

- **System** — `HeartbeatBackgroundService` (Initiator; drives the tick on a background `PeriodicTimer`)
- **System** — any future handler that subscribes to `HeartbeatTickEvent` (combat round processor, mob AI ticker, effect expiry checker)

## Module

`Core/Modules/Time/` (new module)

## Description

A minimal heartbeat infrastructure that publishes `HeartbeatTickEvent` at a configurable interval. This is the shared clock that combat rounds (slice 9), mob AI ticks (slice 11+), and future effect-expiry tracking will all subscribe to. The slice ships only the heartbeat itself — no named timers, no per-entity cooldowns, no effect expiry. Subscribers attach to the event bus independently; the heartbeat is model-agnostic.

## Preconditions

- The generic host is running with the DI container fully built (all handlers subscribed before the first tick fires — existing startup ordering guarantee).
- `appsettings.json` may or may not supply `Heartbeat:IntervalMs`; absence means the default of 2000 ms applies.

## Postconditions

- `HeartbeatBackgroundService` is running a `PeriodicTimer` at the configured interval.
- On each tick, `HeartbeatTickEvent { TickId, Timestamp, Elapsed }` is published to `IEventBus`.
- Any handler subscribed to `HeartbeatTickEvent` (e.g. the combat round handler in slice 9) receives the event and executes its logic.
- The service stops cleanly when the host shutdown `CancellationToken` fires; the in-flight tick (if any) completes normally.

## Main flow

1. **Host startup.** `HeartbeatBackgroundService.StartAsync` is called by the .NET generic host. The service is registered last (after `PersistenceBootstrap`, `WorldContentBootstrap`, `PersistenceFlushTimer`, and `TelnetServer`) so the first tick cannot land on a half-built world. Assumption: `HeartbeatBackgroundService` is added to the DI service collection after `TelnetServer` in `Server/Program.cs` so it starts after the listener is open and the world is fully seeded.
2. **Tick loop begins.** `ExecuteAsync` creates a `PeriodicTimer` with period read from `IConfiguration["Heartbeat:IntervalMs"]` (default 2000). The timer's `WaitForNextTickAsync` awaits the first tick.
3. **Tick fires.** `HeartbeatBackgroundService` increments its internal `long _tickId` counter, captures `DateTimeOffset.UtcNow` as `Timestamp`, and computes `Elapsed = Timestamp - _lastTimestamp` (initialized to `StartedAt` so tick 1 has an accurate elapsed). This is purely mechanical bookkeeping — no game logic.
4. **Event published.** The service calls `IEventBus.PublishAsync(new HeartbeatTickEvent { TickId, Timestamp, Elapsed })`. This is the only cross-thread operation: a background thread publishing to the event bus. The Phase 4 thread-safety review will evaluate the event bus under concurrent ticks; acknowledged here.
5. **Handlers execute.** The event bus dispatches `HeartbeatTickEvent` to all subscribed handlers in priority order. In slice 9, `CombatRoundHandler` is the first subscriber; future slices add further handlers. Each handler does its own work (e.g. query `CombatStateComponent` entities, call `ICombatSystem.ExecuteRound`). None of this logic lives in `HeartbeatBackgroundService`.
6. **Loop continues.** Control returns to `WaitForNextTickAsync`. The next tick is scheduled relative to the timer's period, not relative to when handler execution completed — `PeriodicTimer` does not drift on handler overrun. A handler overrun that exceeds the tick period causes the next tick to fire immediately after the current one completes (standard `PeriodicTimer` behavior).
7. **Shutdown.** When the host sends the `CancellationToken`, `WaitForNextTickAsync` returns `false` and `ExecuteAsync` exits normally. The `BackgroundService` base class calls `StopAsync`, which awaits the task. No cleanup is needed — no registered subscribers, no timers to cancel beyond the `PeriodicTimer` lifetime.

## Events fired

| Event | Publisher | Payload | Notes |
|---|---|---|---|
| `HeartbeatTickEvent` | `HeartbeatBackgroundService` | `long TickId`, `DateTimeOffset Timestamp`, `TimeSpan Elapsed` | Published on every tick; past-tense thin fact (INV-6). `TickId` is a monotonically increasing counter starting at 1. |

No events are published on startup or shutdown — the tick loop produces all events.

## Design notes

### `IHeartbeatService` interface — minimal or absent

The original prompt specifies `IHeartbeatService` with `Start()` / `Stop()` lifecycle methods. In practice, `BackgroundService.StartAsync` / `StopAsync` are called by the .NET host automatically — callers do not need to invoke `Start`/`Stop` directly. There are no in-game subscribers to a registry; subscribers use `IEventBus` directly. Therefore `IHeartbeatService` may be empty (a marker interface) or absent entirely. **Decision (as built):** omitted — no concrete caller other than the host needs to reference the heartbeat by interface. If needed for testability (mocking the heartbeat in unit tests), it can be introduced alongside the test framework (Phase 4).

### Background service placement: `Server/` vs. `Core/`

`BackgroundService` is a .NET hosting abstraction (`Microsoft.Extensions.Hosting`), which belongs at the server/host layer. `HeartbeatBackgroundService` lives in `Server/` (analogous to `PersistenceFlushTimer`, `WorldContentBootstrap`, `TelnetServer`). `HeartbeatTickEvent` lives in `Core/Modules/Time/Events/` so handlers anywhere in `Core` can subscribe without a `Server/` dependency. `TimeModule` is split: the event + interface (if any) are in `Core/`; the hosted service registration is in `Server/Program.cs`.

### Multiple combat model support

`HeartbeatBackgroundService` is combat-model-agnostic. It publishes a tick; the combat handler subscribes. Switching from autocombat (background timer drives rounds) to freeform (player input drives rounds) means the combat handler un-subscribes or is not registered — the heartbeat continues for effect expiry, mob AI, and other consumers. No change to this slice is required when the combat model changes.

### Per-entity cooldown tracking (future)

An `ICooldownSystem` (future) would subscribe to `HeartbeatTickEvent`, query entities with a `CooldownComponent`, decrement counters, and publish `CooldownExpiredEvent`. Not in scope for this slice. The heartbeat event payload is intentionally minimal — `ICooldownSystem` derives all timing from `Elapsed` and `Timestamp`.

### Named timers (future)

Respawn timers, buff expiry, shop restocking — all would subscribe to `HeartbeatTickEvent` and maintain their own state (timestamps, countdown values). They do not need a separate timer abstraction; they consume the shared clock and handle their own expiry logic.

### Thread safety acknowledgment

`HeartbeatBackgroundService.ExecuteAsync` runs on a background thread managed by the .NET host. `IEventBus.PublishAsync` is called from that thread. This is acknowledged debt consistent with the Phase 4 thread-safety review entry in `backlog.md`. The single-threaded `PeriodicTimer` pattern means the heartbeat does not publish concurrently with itself; risk is from overlap with other background services, which is the same risk already present (e.g. `PersistenceFlushTimer`). No new concurrency shape.

### Startup ordering constraint

`HeartbeatBackgroundService` must be registered **after** `TelnetServer` in `Server/Program.cs`. This ensures the first tick cannot fire before the world is fully seeded and the listener is open. The host runs `StartAsync` to completion for each service in registration order; this is an existing guarantee, not a new mechanism.

### `TickId` starts at 1

`_tickId` is initialized to 0 and incremented before publishing, so the first event carries `TickId = 1`. This makes `TickId = 0` an unambiguous sentinel for "no tick has fired yet."

### `Elapsed` on the first tick

`_lastTimestamp` is set to `DateTimeOffset.UtcNow` in `ExecuteAsync` before the loop (i.e., at service start time). The first tick's `Elapsed` is approximately equal to `IntervalMs` — accurate enough for effect expiry and combat round pacing; combat does not need sub-millisecond precision on the first tick.

## Related

- [`combat.md`](../features/combat/combat.md) — slice 9 (blocked on this slice); first consumer of `HeartbeatTickEvent`
- [`entity-state-management.md`](../features/combat/combat.md) — slice 9-a (parallel prerequisite)
- [`stat-system.md`](stat-system.md) — slice 9-c (parallel prerequisite)
- [`attributes.md`](attributes.md) — slice 8a; establishes `PoolsComponent` that combat reads
- [`mobs.md`](mobs.md) — slice 8; mob AI future consumers will subscribe to `HeartbeatTickEvent`
- [`persistence-substrate.md`](persistence-substrate.md) — `PersistenceFlushTimer` is the existing `PeriodicTimer`-based `BackgroundService` this slice mirrors
- [`docs/roadmap/backlog.md`](../roadmap/backlog.md) — thread-safety review entry covers the event bus under concurrent ticks
