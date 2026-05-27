# Phase 3 slice 9-b — Time system (heartbeat) (completed)

> Implemented on branch `claude/naughty-bouman-f9ca54`. Full feature spec: [`../../use-cases/time-system.md`](../../use-cases/time-system.md).

## Outcome

The game now has a shared clock. `HeartbeatBackgroundService` runs a `PeriodicTimer` at `Heartbeat:IntervalMs` (default 2000 ms) and publishes `HeartbeatTickEvent { TickId, Timestamp, Elapsed }` on every tick. No game logic lives in the service — downstream handlers (combat rounds in slice 9, mob AI, effect expiry) subscribe to the event independently. The heartbeat is the last hosted service in the startup queue, so the first tick cannot land before the world is fully seeded and the telnet listener is open. This slice is pure infrastructure; no player-visible surface changes.

## Shipped pieces

| Surface | Location |
|---|---|
| `HeartbeatTickEvent` — `sealed record (long TickId, DateTimeOffset Timestamp, TimeSpan Elapsed)`; past-tense thin fact; `TickId` starts at 1 (`TickId = 0` is the "no tick yet" sentinel) | `Core/Modules/Time/Events/HeartbeatTickEvent.cs` |
| `TimeModule` — `AddTimeModule(IServiceCollection)` DI extension; currently empty (future home for `IHeartbeatService` if testability requires it) | `Core/Modules/Time/TimeModule.cs` |
| `HeartbeatBackgroundService` — Initiator; `BackgroundService`; owns `PeriodicTimer`; reads `Heartbeat:IntervalMs`; publishes `HeartbeatTickEvent`; catches `OperationCanceledException` on shutdown; catches and logs per-tick exceptions without stopping the loop | `Server/HeartbeatBackgroundService.cs` |
| `Program.cs` — `services.AddTimeModule()` + `services.AddHostedService<HeartbeatBackgroundService>()` (registered last, after `TelnetServer`) | `Server/Program.cs` |
| `appsettings.json` — `Heartbeat:IntervalMs: 2000` | `Server/appsettings.json` |
| `docs/architecture/flows/README.md` — Flow 16 (Heartbeat tick) added to index and body; Flow 1 (Server startup) mermaid and step list updated to include `HeartbeatBackgroundService` as the last hosted service | `docs/architecture/flows/README.md` |
| `docs/reference/systems.md` — new "Background Services / Initiators" section with `HeartbeatBackgroundService` entry | `docs/reference/systems.md` |
| `docs/use-cases/README.md` — `time-system.md` status updated to `implemented` | `docs/use-cases/README.md` |

## Spec-review provenance

**Spec-mode gate:** Passed before implementation (use-case doc authored as part of slice 9 planning batch).

**Code-mode gate:** Run before merge. Two blocking findings resolved:
1. **INV-D2** — `time-system.md` in-flight sections not trimmed; `docs/use-cases/README.md` index showed `planned`. Fixed: use-case doc trimmed to durable spec; index updated to `implemented`.
2. **INV-16** — `HeartbeatBackgroundService` missing from `docs/reference/systems.md`. Fixed: added a new "Background Services / Initiators" section with the entry.

One non-blocking finding noted: `.claude/skills/add-event/SKILL.md` example uses `: IGameEvent` instead of `: IEvent`. Spawned as a follow-up task; not blocking this slice.

## Notable design points

- **`IHeartbeatService` omitted.** The spec's open question was resolved by omitting the interface — `BackgroundService.StartAsync`/`StopAsync` are called by the .NET host automatically; no in-game caller needs to reference the heartbeat by interface. If Phase 4 test coverage requires a mock, the interface can be introduced alongside the test framework without breaking changes.

- **`HeartbeatBackgroundService` lives in `Server/`, `HeartbeatTickEvent` in `Core/`.** `BackgroundService` is a hosting abstraction that belongs at the server layer. The event lives in `Core/Modules/Time/Events/` so any future handler in `Core/` can subscribe without a circular `Server/` dependency.

- **Per-tick exception isolation.** The outer `try`/`catch(OperationCanceledException)` exits the loop on host shutdown; an inner `try`/`catch(Exception)` around `PublishAsync` logs handler exceptions at `Error` without stopping the tick loop. A single misbehaving handler cannot kill the heartbeat.

- **`PeriodicTimer` overrun behavior acknowledged.** If tick handlers exceed `IntervalMs`, the next tick fires immediately after the current completes — no drift accumulation but no backpressure. Logged as a Phase 4 hardening concern in `backlog.md`.

- **Thread-safety acknowledged debt.** `ExecuteAsync` calls `IEventBus.PublishAsync` from a background thread — the same cross-thread pattern as `PersistenceFlushTimer` and `WorldContentBootstrap`. Single `PeriodicTimer` means no concurrent self-publish. The Phase 4 thread-safety review covers the event bus under concurrent background-service access.

- **No handlers in this slice.** The first `HeartbeatTickEvent` subscriber (`CombatRoundHandler`) lands in slice 9.

## Deviations from the use-case doc

None. All postconditions satisfied as written.

## Follow-ups unlocked

- **Slice 9-c — Stat computation system.** Remaining combat prerequisite; parallel work; independent of this slice.
- **Slice 9 — Combat.** `CombatRoundHandler` subscribes to `HeartbeatTickEvent` to drive autocombat rounds.
- **Future mob AI.** A mob wander or attack handler subscribes to `HeartbeatTickEvent` and queries mob entities each tick.
- **Future effect/cooldown expiry.** An `ICooldownSystem` or effect handler subscribes to `HeartbeatTickEvent`, reads `Elapsed`, and decrements counters on `CooldownComponent` or similar.
