---
name: add-tests
description: Use when writing or updating automated tests for a Hedron slice — covering a system's decision, a handler's orchestration, a use-case flow, a persistence round-trip, or an architecture invariant. Covers which tier to use, the shared test harness, the test-vs-skip rubric, and a worked example per tier. Invoke when implementing a plan's Test plan, backfilling an existing system, or when the on-touch ratchet pulls an untested system into a slice.
---

# Add Tests

Hedron tests verify the things a player can't see — the internal state transitions and computed results that use-case **Postconditions** assert. The strategy, the tier taxonomy, and the full test-vs-skip rubric live in [docs/architecture/07-testing.md](../../../docs/architecture/07-testing.md); the rules are **INV-25** (verification discipline) and **INV-26** (determinism seam) in [docs/architecture/checklist.md](../../../docs/architecture/checklist.md). This skill is the *how-to* — it carries no copy of those rules; read them live and cite by ID.

> The `Hedron.Tests` project and harness are live in the repository. This is the operative guide for every slice.

## Pick the tier

Work from the thing you're verifying, not from the file:

| You're verifying… | Tier | File suffix |
|---|---|---|
| A system method's decision / computed result / state mutation | **1 — system unit** | `<System>Tests.cs` |
| Which events a handler/command fires, and ordering | **2 — handler** | `<Handler>Tests.cs` |
| A whole use-case Main Flow end-to-end | **3 — flow** | `<Slice>FlowTests.cs` |
| That a `[Persistent]` shape survives save→load | **4 — persistence round-trip** | `<Component>PersistenceTests.cs` |
| A mechanical invariant across the whole assembly | **5 — architecture-guard** | `ArchitectureGuardTests.cs` |

Most slice coverage is Tier 1 + one Tier 3 flow. Reach for Tier 2 only where a handler embodies a *decision* (branch on outcome, priority-dependent ordering).

## The harness (helpers in `Hedron.Tests`)

- `new EntityService()` — the world, in-memory. No DI, no database.
- `EntityBuilder` — fluent fixtures: `new EntityBuilder(ecs).AsPlayer().WithPools(hp:100).InRoom(roomId).Build()` returns the `uint` id.
- `RecordingEventBus : IEventBus` — captures published events in order; for Tier 2/3 it can also dispatch to subscribed handlers.
- `FakeRandom : IRandom` — scripted rolls for deterministic chance assertions.
- `FakeClock : IClock` — settable `UtcNow` + `Advance(TimeSpan)` for deterministic time assertions.
- output capture — a fake `IOutputWriter`/transport recording messages by type + audience.
- in-memory SQLite helper (`PersistenceTestHarness`) + synthetic `HeartbeatTickEvent` factory (`Ticks.At(id)`).

## Worked examples

**Tier 1 — system unit.** Construct entities, inject seams, call the method, assert the result record **and** the mutated state:

```csharp
var ecs = new EntityService();
var attacker = new EntityBuilder(ecs).AsPlayer().WithBody(20).Build();
var defender = new EntityBuilder(ecs).AsMob().WithPools(hp: 5).Build();
var sys = new CombatSystem(ecs, statSystem, attrSystem, aspectSystem, new FakeRandom(rolls: [20, 4]));

var result = sys.ExecuteRound(attacker, defender);

Assert.Equal(CombatRoundOutcome.MobDied, result.Outcome);          // the decision
Assert.True(statSystem.GetCurrentHp(defender) <= 0);               // the invisible state
```

**Tier 2 — handler.** Feed the input event; assert what was published, in order:

```csharp
var bus = new RecordingEventBus();
handler.Handle(new HeartbeatTickEvent(tickId: 1, ...));
Assert.Contains(bus.Published, e => e is CombatEndedEvent { Outcome: MobDied });
// ordering: CombatHandler (output) ran before CombatMobDeathHandler (destroy)
```

**Tier 3 — flow.** Wire real systems+handlers to a `RecordingEventBus`, seed `IRandom`, pump ticks, assert the Postconditions (e.g. combat C-2: mob dies on the expected tick, entity destroyed via `DestroyEntity`, survivor left `InCombat`, `CombatEndedEvent` published). Assert that `BlueprintComponent` was **not** explicitly cleared before destruction — INV-21 says it is preserved as an origin record until the entity is destroyed.

**Tier 4 — persistence round-trip.** Save, load into a fresh `EntityService`, assert `[Persistent]` components equal and transient ones absent; assert world content has no row (INV-23).

**Tier 5 — architecture-guard.** Reflection over `Hedron.Core` asserting INV-3/5/13/23/26 mechanically + a DI-smoke test. Extend the existing guard tests rather than writing per-slice ones.

## What to skip (don't pad the suite)

Per the rubric: pure-data components, per-module DI registration (the DI-smoke test covers it), thin event records, telnet/socket I/O, third-party libs, and **exact output prose** — assert message *type/audience*, never the wording. The discriminator: if a bug would be obvious the first run, or is guaranteed by the compiler / an INV / startup validation, don't test it.

## Determinism (INV-26)

A test must be able to control every source of variation. If the system reaches for `Random.Shared` or `DateTime.UtcNow` directly, **fix the system first** — inject `IRandom` (precedent: `CombatSystem`) or pass the heartbeat timestamp — then inject a fake in the test. Never test by re-seeding a global or sleeping on the real clock.

## Where tests live & how to run

- `Hedron.Tests/<MirroredNamespace>/...` — mirror the `Core` namespace of the thing under test.
- `dotnet test Hedron.sln` — must be green before the code-review gate (INV-25). "Ship green" = build green **and** tests green.

## Cross-references

- [docs/architecture/07-testing.md](../../../docs/architecture/07-testing.md) — strategy, taxonomy, rubric, harness spec.
- [docs/architecture/checklist.md](../../../docs/architecture/checklist.md) — INV-25, INV-26 (authoritative).
- **add-domain-system** / **add-core-system** — the systems you'll unit-test; both stress the "can I unit-test this?" split.
