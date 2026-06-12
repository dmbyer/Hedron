# Testing & Verification

> The **canonical** testing reference: *how* Hedron is tested and *what* is worth testing. The two enforceable rules — **INV-25** (verification discipline) and **INV-26** (determinism seam) — live in [checklist.md](checklist.md). This doc *explains* them; it does not restate them (INV-27). Workflow obligations (the per-slice test gate) live in [`../roadmap/plan.md`](../roadmap/plan.md) "Phase 3 ground rules"; this doc does not duplicate the loop.

---

## Why this exists

Two problems compound as use-cases grow:

1. **Manual testing doesn't scale.** Every new or changed feature can regress a prior slice, and re-walking each flow by hand through a telnet client is infeasible.
2. **The decisive assertions are invisible.** Use-case postconditions routinely assert internal state that a player — *or even an admin* — cannot observe. Combat alone asserts "clears `BlueprintComponent` before `DestroyEntity`," "process a pair only when `entityId < OpponentEntityId`," "clamps HP to 1," "`ICombatSystem` never touches the event bus." Today these are checkable only by reading code or stepping a debugger.

Automated tests turn invisible internal state into executable assertions. That is the whole point of the strategy: **less manual checking, and verification of the things a human can't see by playing.**

## The architecture is already test-friendly

The layering does most of the work for us:

- **Systems are near-pure functions.** A domain/core system is forbidden from touching the event bus or doing I/O (INV-5) and returns a result record. Construct an `EntityService`, add components, call the method, assert on the returned record **and** the mutated component state. No sockets, no mocking frameworks.
- **Components are pure data** (INV-3) — trivial fixtures.
- **`EntityService` is a plain in-memory object** — `new EntityService()`, no DI, no database.
- **The clock is abstracted** — handlers receive `HeartbeatTickEvent { TickId, Timestamp, Elapsed }`; a test publishes a synthetic tick.
- **`IEventBus` is a 4-method interface** — a recording fake that captures published events is trivial.

The one gap is **randomness**: chance-based logic must resolve through an injected seam, never a global RNG, or its outcomes can't be asserted. That is INV-26.

## Guiding principle

**Test the decision and the invariant — not the data, the plumbing, or the prose.**

The one-line discriminator:

> If a bug would be invisible to **both** the player **and** a reviewer skimming the diff, it **must** be tested. If a bug would be obvious the first time you run the app (wording, layout) or is already guaranteed by the compiler, an `INV`, or startup validation, a test adds little.

This is "strong but not excessive": coverage concentrates on internal state transitions and computed results, and deliberately skips presentation and plumbing.

---

## Test taxonomy

Five tiers, mapped onto the four layers. Each tier has a default posture — the rubric below says when each is required.

| Tier | Targets | Asserts | Harness needs |
|---|---|---|---|
| **1 — System unit** *(the core of the suite)* | Domain systems (`Modules/<F>/Systems/`), core systems (`Core/Systems/`) | The returned **result record** + the **mutated component state**. Where invisible-state postconditions live: clamps, dedup, aggregation, lifetime filtering, cooldown math, threshold crossings. | real `EntityService`; seeded `IRandom`; explicit timestamps |
| **2 — Handler / orchestration** | Handlers, commands (Initiators) | Correct systems called + correct **events published** (captured) + **priority-dependent ordering** where it is load-bearing (e.g. death: output before destroy). *Not* a re-derivation of system math. | `RecordingEventBus`, output capture |
| **3 — Flow / scenario** | One per use-case **Main Flow** | command → event → handler → system → component state, across synthetic ticks. **The use-case Postconditions become the assertions.** | real systems+handlers+bus, fake transport, seeded `IRandom`, synthetic ticks |
| **4 — Persistence round-trip** | Any `[Persistent]` shape | save → load into a fresh world → component equality; transient components **absent**; two-domain rules (world content has no row); lifetime-filtered `EffectsComponent` writes only `UntilRemoved`. | in-memory SQLite |
| **5 — Architecture-guard** *(reflection; distinctive)* | The `Hedron.Core` assembly | Mechanical invariants as automated gates — see below. | reflection over `Core`; `Server` for DI-smoke |

### Tier 1 — system unit tests

The highest-value tier, because systems hold the decisions and the invisible state. A test is mechanically:

```
arrange:  var ecs = new EntityService();
          var attacker = new EntityBuilder(ecs).AsPlayer().WithBody(20).Build();
          var sys = new CombatSystem(ecs, statSystem, attrSystem, aspectSystem, new FakeRandom(rolls: [15, 8]));
act:      var result = sys.ExecuteRound(attacker, defender);
assert:   result.Outcome == Hit;  result.DamageDealt == <deterministic>;
          statSystem.GetCurrentHp(defender) == startHp - result.DamageDealt;
```

If a method can't be unit-tested with constructed entities, it is doing too much — split it until it can (this is the long-standing testability heuristic, now backed by a test).

### Tier 2 — handler / orchestration tests

Handlers don't compute; they decide *which events fire* and *in what order*. Test exactly that: feed the handler an input event, assert the `RecordingEventBus` captured the right events with the right payloads, and (where ordering is load-bearing) that they fired in priority order. Do **not** re-assert the system's internal math through the handler — that is Tier 1's job and duplicating it makes both tests brittle.

### Tier 3 — flow / scenario tests

The executable form of a use-case's Main Flow. Wire the real systems and handlers to a real (or recording) bus, a fake transport that captures output, and a seeded `IRandom`; drive the flow with command invocations and synthetic `HeartbeatTickEvent`s; assert the Postconditions. Combat C-2, for example: start combat, pump N ticks with seeded rolls, assert the mob dies on the expected tick, `CombatEndedEvent(MobDied)` is published, `BlueprintComponent` is cleared, the entity is destroyed, and the survivor exits `InCombat`.

### Tier 4 — persistence round-trip tests

Save an entity, load it into a fresh `EntityService`, assert `[Persistent]` components compare equal and transient ones are absent. Also assert the two-domain rules (INV-23): world content carries no SQLite row, and the lifetime-filtered `EffectsComponent` writes only `UntilRemoved` entries. Backend is in-memory SQLite (`Data Source=:memory:`).

### Tier 5 — architecture-guard tests

Reflection over the `Hedron.Core` assembly turns the reviewer's most mechanical checks into a continuous, automated regression gate — freeing the human/agent reviewer to spend attention on judgment. These are guard rails, not behavior tests:

| Guard | Enforces | Check |
|---|---|---|
| No-bus-in-systems | INV-5 | No type in a `*.Systems` namespace has a ctor parameter or field of type `IEventBus`. |
| Components-are-data | INV-3 | Every `IComponent` implementor exposes only data (no public methods beyond accessors; no system/bus fields). |
| Entity-refs-are-uint | INV-13 | Components reference other entities by `uint`, not `Entity` or object refs. |
| World-content-not-persistent | INV-23 | `RoomComponent` and `AreaComponent` are not `[Persistent]`. |
| No-ambient-nondeterminism | INV-26 | No `Random.Shared` / `new Random()` / `DateTime[Offset].Now` referenced under `Systems/`. |
| DI-smoke | composition integrity | The `Server` composition root builds and every registered handler/system resolves — one test covering all DI wiring and catching missing registrations. |

---

## What to test vs. skip

**Always test (high signal; invisible-to-player; regression-prone):**

- Every system public method that encodes a decision or computation (T1).
- Load-bearing handler branch/ordering decisions and event fan-out (T2).
- Each use-case Main Flow (T3).
- Each `[Persistent]` shape (T4).
- Every fail-fast validation ("throws on a dangling reference").
- The architecture-guard suite (T5).

**Test selectively (only the non-trivial part):**

- **Commands** — test argument *resolution* only when it is non-trivial (a custom `IArgumentResolver`, prefix matching). The thin parse → system → publish body is covered by the flow test; don't unit-test a 10-line command in isolation.
- **Output** — assert the message **type / structure / audience** (e.g. "an incapacitation narrative was broadcast to the room"), never the exact prose. Exact-wording assertions are brittle and low-value.

**Skip (a bug there is caught free by the compiler, the reviewer, startup validation, or the first run):**

- Pure-data components — INV-3 guarantees no logic; testing getters/setters is noise.
- Per-module DI registration — the DI-smoke guard test covers it once.
- Thin event records (payload only).
- Telnet / socket I/O, the `Server` host lifetime, and third-party libraries (YamlDotNet, `System.Text.Json`, SQLite internals).
- Exact log / console strings.

## Coverage posture

**No global line-coverage percentage.** Percentage targets reward testing trivia and punish honest "skip" calls. Hedron measures **behavioral coverage** instead:

- every system public method has ≥1 test of its decision;
- every use-case Main Flow has a scenario test;
- every `[Persistent]` shape has a round-trip;
- every fail-fast validation has a "throws on bad input" test.

The use-case **Postconditions are the coverage contract** — each postcondition that asserts internal state should map to an assertion somewhere in the suite. This is what the **Test plan** section of a use-case doc records, and what the spec- and code-review gates check (INV-25).

---

## The test harness

A single `Hedron.Tests` project (xUnit) referencing `Core` (and `Server` for the DI-smoke guard). Shared helpers keep fixtures terse:

| Helper | Purpose |
|---|---|
| `RecordingEventBus : IEventBus` | Captures published events in order; optionally dispatches to registered handlers for Tier 2/3. |
| `EntityBuilder` | Fluent fixture over a real `EntityService` — `.AsPlayer()`, `.AsMob()`, `.WithPools(hp:…)`, `.InRoom(id)`, `.Wielding(item)`; returns the `uint` id. |
| `FakeRandom : IRandom` | Seeded or scripted RNG for deterministic chance-based assertions. |
| output capture | A fake `IOutputWriter`/transport that records messages by type and audience. |
| in-memory SQLite helper | Spins up a `:memory:` database for round-trips. |
| synthetic tick factory | Builds `HeartbeatTickEvent`s with controlled `TickId`/`Elapsed`. |

> The harness and the test project are stood up as a dedicated follow-up; this section is its spec. Until then, the strategy and invariants are in force for *new* work and the backfill is queued in [`../roadmap/backlog.md`](../roadmap/backlog.md).

## Determinism (INV-26)

Chance- and time-dependent decisions inside a system resolve through an injected seam — `IRandom` for randomness, the heartbeat-supplied `Elapsed`/`Timestamp` (or an injected clock) for time — never `Random.Shared`, `new Random()`, or a direct wall-clock read. This preserves the pure-system property (INV-3 / INV-5) and is what makes a chance-based outcome assertable: a test injects a `FakeRandom` with scripted rolls. `IRandom`/`SystemRandom` is the core-system seam (`Core/Systems/`, catalogued in [`../reference/systems.md`](../reference/systems.md)); see INV-26 for the rule.

**Current state.** Randomness is fully sealed — `CombatSystem` was the last `Random.Shared` consumer and now takes `IRandom`. Time is *mostly* sealed: combat, effects, and regeneration advance off the heartbeat's `Elapsed`/`Timestamp`. Two systems still read the wall clock directly — `AccountSystem` (timestamp stamping) and `SpawnSystem` (respawn timing) — **acknowledged debt** resolved by an injected clock when those systems are backfilled or next touched (the on-touch ratchet). Event records stamping `OccurredAt` are payloads, not systems, and are out of scope. See [`../roadmap/backlog.md`](../roadmap/backlog.md).

## How testing plugs into the SDLC

- **Plan** — the use-case doc gains a **Test plan / Verification** section (a first-class concern, parallel to Content tooling impact): which systems/flows/persistence shapes get tests, at which tier, what each asserts, and what is skipped with a rubric-backed reason.
- **Spec-review gate** — confirms the Test plan is honest and complete given the Postconditions.
- **Implement** — write the tests named in the Test plan; run `dotnet test`.
- **Code-review gate** — confirms the named tests are present and the suite is green (INV-25), and greps for ambient nondeterminism (INV-26). The reviewer checks test *presence and pass*, not test *logic quality*.
- **Ship green** — now means build green **and** `dotnet test` green.

The full loop, including the **on-touch ratchet** (a slice that modifies a previously-untested system backfills that system's tests before merge), is in [`../roadmap/plan.md`](../roadmap/plan.md). The backfill of existing systems is tracked in [`../roadmap/backlog.md`](../roadmap/backlog.md).

## Related

- [checklist.md](checklist.md) — **INV-25** (verification discipline) and **INV-26** (determinism seam), the authoritative rules.
- [01-layers.md](01-layers.md) · [02-ecs.md](02-ecs.md) · [03-events.md](03-events.md) — the layering that makes systems test-friendly.
- [`.claude/skills/add-tests/SKILL.md`](../../.claude/skills/add-tests/SKILL.md) — the how-to for writing a test at each tier.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — the test project, harness, and backfill waves.
