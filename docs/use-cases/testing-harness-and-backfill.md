# Use Case: Testing Harness & System Backfill (Phase 2)

**Status:** planned
**Actors:** Developer, System (no player-facing behavior)
**Module:** `Hedron.Tests/` (new xUnit project) · `Core/Systems/` (new `IClock` seam) · `Server/` (composition-root extraction for the DI-smoke guard)

---

## Description

The testing **strategy** is already defined ([`../architecture/07-testing.md`](../architecture/07-testing.md)) and enforced as invariants ([`../architecture/checklist.md`](../architecture/checklist.md) INV-25 verification discipline, INV-26 determinism seam); the `IRandom` seam shipped. This slice is the **executable follow-up**: stand up the `Hedron.Tests` project + shared harness, build the reflection-based architecture-guard suite, close the remaining INV-26 time-determinism debt with an `IClock` seam, and **backfill tests for the existing ~19 systems** so the per-slice gate has a green suite to enforce against.

This doc is written for **hand-off to sub-agents in small context windows.** Each work-package **step** is self-contained: it names the exact files, the shapes to build, and a *testable exit criterion*. A sub-agent can execute one step cold (reading only the files that step names) without holding the whole effort in context. Packages and steps that depend only on a shared earlier package run in parallel. The orchestrating model runs `architecture-reviewer` (code mode) across the combined diff once packages land — sub-agents do not self-review (Phase 3 ground rule 6).

**Scope.** No gameplay change, no new runtime flow. The only production changes are the `IClock` seam (+ its two refactors) and a composition-root extraction in `Server/Program.cs` to make DI smoke-testable. Everything else is test code.

---

## Preconditions

- PR #113 merged: `docs/architecture/07-testing.md`, INV-25/26 in the checklist, the `IRandom`/`SystemRandom` seam (`Core/Systems/`), and the tooling updates (`add-tests` skill, Test-plan template section, reviewer/planner/sync-roadmap edits) are all on `master`.
- `dotnet build Hedron.sln` is green on .NET 8.
- The systems to be tested already exist and are catalogued in [`../reference/systems.md`](../reference/systems.md); their behavior contracts are the **Postconditions** of their use-case docs (linked per system below).
- `EntityService` is constructable in-memory (`new EntityService()`); `IEventBus` is a 4-method interface; `IRandom` is injected into `CombatSystem`. (All true as of PR #113.)

## Postconditions

- A `Hedron.Tests` xUnit project exists, is in `Hedron.sln`, references `Core` (+ `Server` for DI-smoke), and `dotnet test Hedron.sln` runs green.
- Shared harness exists under `Hedron.Tests/Harness/`: `RecordingEventBus`, `EntityBuilder`, `FakeRandom`, `FakeClock`, output capture, an in-memory/temp-file SQLite persistence harness, and a synthetic `HeartbeatTickEvent` factory.
- An **architecture-guard suite** enforces INV-3/5/13/23/26 mechanically + a DI-smoke test; it is green against current code and **fails** when a deliberate violation is introduced (proven by the mutation check in the Test plan).
- `IClock`/`SystemClock` core seam exists; `SpawnSystem` and `AccountSystem` no longer read `DateTime.UtcNow` directly (INV-26 time clause fully satisfied; the guard's wall-clock clause is enabled).
- **Wave 1** systems have decision-level + flow + round-trip + validation coverage (Combat, Effects, Stats, Abilities, Death/respawn, Persistence round-trips, Registry validation).
- The `dotnet test` portion of the per-slice code gate (INV-25) is now enforceable; CI runs build + test on every PR.

---

## Main Flow

1. **WP-1 lands first** — the test project + shared harness. Nothing else can run until `dotnet test` executes.
2. **WP-2, WP-3, WP-4.\*, WP-5 fan out in parallel** (each depends only on WP-1; different files, no cross-dependencies). WP-4.1 (Combat) needs `FakeRandom` (WP-1); the `SpawnSystem` backfill in WP-6 needs `IClock` (WP-3).
3. Each backfill step asserts the target system's **Postconditions** (its use-case doc is the contract) at the right tier per [`../architecture/07-testing.md`](../architecture/07-testing.md).
4. Once WP-1…WP-5 land green, **Wave 2 (WP-6) and Wave 3 (WP-7)** apply the same recipe to the remaining systems — or are drained opportunistically by the on-touch ratchet (INV-25) as future slices touch those systems.
5. The orchestrator runs `architecture-reviewer` (code mode) over the combined diff, then ships green.

---

## Events Fired

None. This slice adds no events and publishes nothing — it is test infrastructure plus two pure-seam refactors.

---

## Systems / Handlers Involved

**New (production):** `IClock`/`SystemClock` (`Core/Systems/`); a `CompositionRoot.Register(IServiceCollection, IConfiguration)` extraction from `Server/Program.cs`.
**New (test):** the `Hedron.Tests/Harness/` helpers and one test class per system/flow.
**Under test (existing, unchanged):** `CombatSystem`, `EffectSystem`, `StatSystem`, `AbilitySystem`, `DeathSystem`, `PersistenceSystem`, `RegistryValidationBootstrap`, and (Wave 2/3) `ItemSystem`, `EquipmentSystem`, `MovementSystem`, `RegenerationSystem`, `AttributeSystem`, `AspectSystem`, `SpawnSystem`, `AccountSystem`, the authoring builder systems, `BroadcastSystem`, `PromptComposerSystem`. See [`../reference/systems.md`](../reference/systems.md).

---

## Implementation plan — work packages

> Step format: **Do** (the action) · **Files** (touch only these) · **Exit** (the testable pass condition). A sub-agent should read this doc's WP, the named files, and — for a backfill step — the target system file plus its linked use-case doc. Nothing else.

### WP-1 — Test project + shared harness  *(BLOCKING; everything depends on it)*

- **1.1 — Create the project.** **Do:** add `Hedron.Tests/Hedron.Tests.csproj` (`net8.0`, `ProjectReference` to `Core` and `Server`; packages `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.Data.Sqlite`); add it to `Hedron.sln`. **Files:** new csproj, `Hedron.sln`. **Exit:** `dotnet test Hedron.sln` runs and reports 0 tests, exit 0.
- **1.2 — `RecordingEventBus`.** **Do:** implement `IEventBus`; expose `IReadOnlyList<IEvent> Published`; `Subscribe/Unsubscribe` maintain per-type handler lists; `Publish`/`PublishAsync` append to `Published` then (when constructed with `dispatch: true`) invoke subscribed handlers in `Priority` order. **Files:** `Hedron.Tests/Harness/RecordingEventBus.cs`. **Exit:** a self-test publishes an event and finds it in `Published`; with `dispatch: true`, a subscribed fake handler runs in priority order.
- **1.3 — `EntityBuilder`.** **Do:** fluent builder over a passed-in `EntityService`: `.AsPlayer()` (adds `CharacterComponent`), `.AsMob(name, keywords)` (`MobDataComponent`), `.WithPools(hp,mana,stamina,astra)`, `.WithAttributes(mind,body,spirit,attunement,level)`, `.InRoom(uint)` (`LocationComponent` with both `RoomEntityId` + `RoomBlueprintId`), `.Wielding(uint)` (`EquipmentComponent` MainHand), `.With<T>(T)`, `.Build()` → `uint`. **Files:** `Hedron.Tests/Harness/EntityBuilder.cs`. **Exit:** `new EntityBuilder(ecs).AsPlayer().WithPools(hp:50).Build()` yields an id whose `PoolsComponent.CurrentHp == 50` and which has `CharacterComponent`.
- **1.4 — `FakeRandom`.** **Do:** implement `IRandom`; ctor `FakeRandom(params int[] nextValues)` returns those in order from `Next(...)` (dequeue; assert/return within range), `FakeRandom(int seed)` falls back to a seeded `Random`; a `Queue<double>` backs `NextDouble()`. **Files:** `Hedron.Tests/Harness/FakeRandom.cs`. **Exit:** `new FakeRandom(20, 4).Next(1,21) == 20` then `.Next(1, 10) == 4`.
- **1.5 — Output capture.** **Do:** a fake `IOutputWriter` (and minimal `ISession`/factory shim as needed) that records `(IOutputMessage type, recipient)` tuples; a helper to assert "a message of type X went to audience Y." **Files:** `Hedron.Tests/Harness/RecordingOutput.cs`. **Exit:** capturing a written message exposes its concrete type and recipient; **no assertion on prose strings**.
- **1.6 — Persistence harness.** **Do:** a `PersistenceTestHarness` that wires a `PersistenceSystem` against a temp-file SQLite db (`Path.GetTempFileName()`-backed `Persistence:DatabasePath` via an in-memory `IConfiguration`) with real `ComponentTypeRegistry` + `ComponentSerializer`; exposes `SaveAsync(entityId)` and a `ReloadIntoFreshWorld()` returning a new `EntityService` hydrated via `LoadAllAsync`. Implements `IDisposable` (deletes the temp db). **Files:** `Hedron.Tests/Harness/PersistenceTestHarness.cs`. **Exit:** save a `PersistentEntity` carrying one `[Persistent]` component, reload, component compares equal.
- **1.7 — Synthetic tick factory.** **Do:** `Ticks.At(long id, double elapsedMs = 2000)` → `HeartbeatTickEvent { TickId=id, Timestamp=..., Elapsed=TimeSpan.FromMilliseconds(elapsedMs) }`. **Files:** `Hedron.Tests/Harness/Ticks.cs`. **Exit:** returns an event with the supplied `TickId` and `Elapsed`.

> 1.2–1.7 depend only on 1.1 and may run in parallel.

### WP-2 — Architecture-guard suite (Tier 5)  *(depends on WP-1.1)*

One test class, `Hedron.Tests/Architecture/ArchitectureGuardTests.cs`; each step is one `[Fact]`. Cite the INV in the assertion message.

- **2.1 — INV-5 no-bus-in-systems.** **Do:** over `typeof(EntityService).Assembly` (Core), select types whose namespace ends in `.Systems` or contains `.Systems.`; assert none has a constructor parameter or field of type `IEventBus`. **Exit:** green on current code.
- **2.2 — INV-3 components-are-data.** **Do:** select `IComponent` implementors; assert each exposes only property accessors, fields, and compiler/`object`-generated members (allowlist: `Equals`, `GetHashCode`, `ToString`, `Deconstruct`, `<Clone>$`, `op_*`); flag any other public method and any field/prop typed as a system or `IEventBus`. **Exit:** green; document any allowlisted member.
- **2.3 — INV-13 entity-refs-are-uint.** **Do:** for `IComponent` implementors, assert no public field/prop is typed as another `IComponent` or `Entity` object reference (entity refs are `uint`). **Exit:** green (heuristic; note any intentional exception).
- **2.4 — INV-23 world-content-not-persistent.** **Do:** assert `RoomComponent` and `AreaComponent` do **not** carry `[Persistent]`. **Exit:** green.
- **2.5 — INV-26 no ambient randomness.** **Do:** source-scan — resolve the `Core` source directory relative to the test assembly, read every `*.cs` under any `Systems/` folder, assert none contains `Random.Shared` or `new Random(`. **Exit:** green (clean since the `IRandom` seam). *(The wall-clock half of this scan — `DateTime.UtcNow` etc. — is added in WP-3.4, after `IClock` removes the two debt sites.)*
- **2.6 — DI-smoke.** **Do:** call `CompositionRoot.Register(services, config)` (extracted in WP-3.5 / or this step if done first), `BuildServiceProvider`, and resolve every registered `ICommand`, `IEventHandler<>`, and system; assert no resolution throws. **Files:** also requires the WP-3.5 extraction. **Exit:** provider builds and all registered services resolve.

### WP-3 — `IClock` seam + INV-26 time-determinism cleanup  *(depends on WP-1; small production change)*

- **3.1 — `IClock` core seam.** **Do:** `Core/Systems/IClock.cs` (`DateTime UtcNow { get; }`) + `Core/Systems/SystemClock.cs` (returns `DateTime.UtcNow`); register `services.AddSingleton<IClock, SystemClock>()` in the composition root. Add `FakeClock : IClock` (settable `UtcNow`, `Advance(TimeSpan)`) to `Hedron.Tests/Harness/`. Catalog `IClock` in [`../reference/systems.md`](../reference/systems.md) (INV-16). **Exit:** build green; `FakeClock` returns its set time.
- **3.2 — Refactor `SpawnSystem`.** **Do:** inject `IClock`; replace the three `DateTime.UtcNow` reads (respawn scheduling + the `RespawnAt <= UtcNow` comparison, `Core/Modules/Spawn/Systems/SpawnSystem.cs` ~lines 98/134/156) with `_clock.UtcNow`. **Exit:** build green; behavior unchanged under `SystemClock`.
- **3.3 — Refactor `AccountSystem`.** **Do:** inject `IClock`; replace the `CreatedAtUtc`/`LastLoginUtc`/throttle reads (`Core/Modules/Account/Systems/AccountSystem.cs` ~lines 80/103/178) with `_clock.UtcNow`. **Exit:** build green.
- **3.4 — Enable the wall-clock guard.** **Do:** extend WP-2.5's source-scan to also assert no `DateTime.UtcNow`/`DateTime.Now`/`DateTimeOffset.UtcNow`/`DateTimeOffset.Now`/`.Today` under any `Systems/` folder. **Exit:** green (the two debt sites are gone); remove the "grandfathered" note in the backlog.
- **3.5 — Composition-root extraction (enables DI-smoke).** **Do:** extract the `ConfigureServices` body of `Server/Program.cs` into `public static IServiceCollection Register(this IServiceCollection, IConfiguration)` (e.g. `Server/CompositionRoot.cs`); `Main` calls it. No behavior change. **Files:** `Server/Program.cs`, new `Server/CompositionRoot.cs`. **Exit:** app still boots; WP-2.6 can call `Register`.

### WP-4 — Wave 1 backfill  *(each step is one independent test file; depends on WP-1)*

Each step: read the target system + its use-case doc, then assert the listed Postconditions. Tier-1 unless noted.

- **4.1 — Combat** (`CombatSystem`; [`combat.md`](combat.md)). **Files:** `Hedron.Tests/Combat/CombatSystemTests.cs`, `.../CombatFlowTests.cs`. **Assert:** `ExecuteRound` hit when roll≥threshold / miss when below (scripted `FakeRandom`); damage applied via `SetCurrentHp`; outcome `MobDied` (defender has `MobDataComponent`, hp≤0) vs `PlayerIncapacitated` (defender has `CharacterComponent`); aspect resolution applied. `ResolveAbilityStrike` always lands, defense-mitigated, min 1. `StartCombat`/`EndCombat` add/remove `CombatStateComponent` on both. `TryFindTargetInRoom` prefix-matches name+keywords, false off-room. **Flow (Tier 3):** kill→pump ticks→on `MobDied`: `BlueprintComponent` cleared *before* `DestroyEntity`, entity destroyed, survivor exits `InCombat`, `CombatEndedEvent(MobDied)` published; pair processed once per tick (the `entityId < OpponentEntityId` dedup). **Exit:** all green; the four invisible-state postconditions (clear-before-destroy, dedup, HP clamp-to-1 stub, no-bus-in-system) each have an assertion.
- **4.2 — Effects** (`EffectSystem`; [`effect-substrate.md`](effect-substrate.md)). **Assert:** `Apply` returns `null` when `HighestWins` blocks (existing power ≥); stacks under `Stack`. `AdvanceTick` expires timed effects after duration, returns periodic-due, sorted Early→Normal→Late, removes expired. `GetModifiers` sums stored + `IEffectContributor` (INV-24). **Tier 4:** `EffectsComponent` round-trip persists only `UntilRemoved`; `WhileKnown` derived, not stored. **Exit:** green.
- **4.3 — Stats** (`StatSystem`; [`stat-system.md`](stat-system.md)). **Assert:** `GetEffectiveAttackPower` = Body/2 + MainHand `DamageBonus` (0 with no weapon); `GetEffectiveDefense` = Body/4; `Get(scoreId)` folds `IEffectSystem.GetModifiers`; equipment + effect modifiers included. **Exit:** green.
- **4.4 — Abilities** (`AbilitySystem`; [`ability-substrate.md`](ability-substrate.md), [`ability-invocation.md`](ability-invocation.md)). **Assert:** `Activate` validation order (unknown → not-known → not-Active → Incapacitated → cooldown → cost-affordability, atomic before any spend); on success spends costs, sets cooldown, applies effects; `resolveOffensiveExternally` skips the offensive damage effect and returns `OffensivePower`; `AdvanceCooldowns` decrements by elapsed, clamps 0; `Learn`/`Teach`/`IsKnown`. **Exit:** green.
- **4.5 — Death** (`DeathSystem`; [`death-and-respawn.md`](death-and-respawn.md)). **Assert:** `OnHpChanged` returns BecameIncapacitated/Died/None at the configured thresholds, only for `CharacterComponent` entities; `Respawn` exits `Incapacitated`, relocates to respawn room, strips impermanent effects, restores pools to `Death:RespawnPoolPercent`; `SetRespawn` validates the blueprint exists, returns false+reason otherwise. **Exit:** green.
- **4.6 — Persistence round-trips** (`PersistenceSystem`; [`persistence-reform.md`](persistence-reform.md)). **Files:** `Hedron.Tests/Persistence/RoundTripTests.cs` (uses WP-1.6 harness). **Assert:** a player entity's `[Persistent]` components survive save→load equal; transient components (`CombatStateComponent`) are absent after reload; a world-content entity (no `PersistentEntity`) writes no row. **Exit:** green.
- **4.7 — Registry validation** (`RegistryValidationBootstrap`). **Assert:** throws on a dangling ability→effect ref, a dangling ability→aspect ref, an `AspectComposition` that doesn't normalize (empty or sum 100), and a bad `CharacterDefaults:StartingAbilities` entry; passes on a valid registry set. **Exit:** green (construct registries with deliberately bad rows in-test).

### WP-5 — CI wiring  *(depends on WP-1; parallel with WP-2/3/4)*

- **5.1 — PR workflow.** **Do:** add `.github/workflows/ci.yml` running `dotnet build Hedron.sln` + `dotnet test Hedron.sln` on pull_request and push to `master`, .NET 8 SDK. **Exit:** workflow green on this branch; "ship green" (INV-25) becomes machine-enforced.

### WP-6 — Wave 2 backfill (recipe)  *(depends on WP-1; SpawnSystem step depends on WP-3)*

Apply the **add-tests** recipe (one `<System>Tests.cs` per system, assert its use-case Postconditions at the right tier) to: `ItemSystem`, `EquipmentSystem`, `MovementSystem`, `RegenerationSystem`, `AttributeSystem` (clamp invariants), `AspectSystem` (the resolve formula), `SpawnSystem` (respawn timing via `FakeClock` — needs WP-3), `AccountSystem`, the authoring builders (`RoomBuilderSystem`/`ItemBuilderSystem`/`MobBuilderSystem`), `BroadcastSystem` (audience routing), `PromptComposerSystem`. One system = one independently-dispatchable step.

### WP-7 — Wave 3 / opportunistic (recipe)

Remaining systems and any gaps the architecture-guard or coverage review surfaces, plus whatever the **on-touch ratchet** (INV-25) hasn't already pulled in as slices touched those systems.

---

## Content tooling impact

**None.** This slice adds no gameplay state — it is test infrastructure plus two pure-seam refactors. No data-file shape, admin command, or `TemplateRegistry` entry. (INV-18 satisfied by exception for a pure-infrastructure slice.)

## Test plan / Verification

The deliverable *is* tests, so verification is meta:

- **Harness (WP-1):** each helper ships with a one-method self-test (the Exit criteria above).
- **Architecture-guard (WP-2/3.4):** prove the guards *bite* with a **mutation check** — temporarily add an `IEventBus` field to one system and confirm 2.1 fails; temporarily `[Persistent]` on `RoomComponent` and confirm 2.4 fails; revert. A guard that can't fail is worthless.
- **Backfill (WP-4/6/7):** the **Postconditions** of each target system's use-case doc are the coverage contract — every postcondition asserting player-invisible state maps to a named test. The orchestrator spot-checks this mapping for Wave 1.
- **Gate:** `dotnet test Hedron.sln` green; CI (WP-5) runs it on every PR.

Skipped per the rubric: pure-data components (the guard suite covers INV-3 wholesale), thin command/plumbing bodies (covered by flow tests), exact output prose (assert type/audience only), third-party libs.

## Cross-cutting surfaces stressed

- **Test harness (new framework)** — **Adequate after WP-1.** Testing recurs every slice (≫3×); the framework is the `Hedron.Tests/Harness/` helpers + the `add-tests` skill (skill already shipped in PR #113). INV-19 satisfied by WP-1.
- **Persistence** — **Adequate.** WP-1.6 wires the real `PersistenceSystem` against a temp-file SQLite db; no production change.
- **Event bus** — **Adequate.** `RecordingEventBus` implements the existing `IEventBus`; no production change.
- **Determinism seam (time)** — **Gap exposed → closed in WP-3.** `IClock` is the missing seam for INV-26's time clause; introduced here, debt sites refactored.
- **Composition root** — **Gap exposed → closed in WP-3.5.** `Program.Main` registers inline; DI-smoke needs a callable `Register(...)`. Extracted with no behavior change.
- **Configuration** — **Adequate.** Tests supply in-memory `IConfiguration` for keys like `Persistence:DatabasePath`, `Death:*`.

## Flows introduced or modified

**None.** Tests observe existing runtime flows; they do not create or change any canonical flow in [`../architecture/flows/README.md`](../architecture/flows/README.md). (The flow *tests* in WP-4.1 exercise existing Flow 17/18/20 — combat initiation / round pulse / mob death-respawn — without modifying them.)

---

## Design notes

- **Built for cold sub-agent dispatch.** Each step names its files and a binary exit criterion so a low-context model can execute one step, run `dotnet test`, and stop. Recommended dispatch order: WP-1 (one agent, serial) → then fan out WP-2, WP-3, each WP-4.x, WP-5 to parallel agents → then WP-6/WP-7 by the recipe. The orchestrator, not the sub-agents, runs the `architecture-reviewer` code-mode gate over the combined diff.
- **Foundation detailed, waves recipe-driven.** WP-1/2/3 and Wave 1 (WP-4) are spelled out because they are written once and unblock everything; Wave 2/3 follow the identical add-tests recipe and are listed by system rather than re-enumerated, to keep this doc loadable.
- **The guard suite is the highest-leverage piece.** It converts the reviewer's most mechanical INV checks into a continuous, automatic gate — write it early (WP-2) so subsequent backfill can't silently regress an invariant.
- **`IClock` mirrors `IRandom`.** Same pattern, same rationale (INV-26): a tiny injected seam that makes time-dependent decisions deterministically testable. `SpawnSystem` is the real beneficiary (respawn-after-N-ticks); `AccountSystem` is migrated for purity.
- **Spec-review gate still applies.** Per Phase 3 ground rule 4, run `architecture-reviewer` in **spec mode** against this doc before implementation begins; resolve any blocking findings in the doc first. The orchestrating model should do this as its first action.
- **Trim-on-ship.** When this slice completes, `sync-roadmap` trims this doc to its durable spec (Postconditions remain the coverage contract); the work-package detail becomes authoritative in `Hedron.Tests` (INV-D2).

## Related

- [`../architecture/07-testing.md`](../architecture/07-testing.md) — the strategy, taxonomy, rubric, and harness spec this slice realizes.
- [`../architecture/checklist.md`](../architecture/checklist.md) — INV-25 (verification discipline), INV-26 (determinism seam).
- [`../../.claude/skills/add-tests/SKILL.md`](../../.claude/skills/add-tests/SKILL.md) — the per-tier how-to each backfill step follows.
- [`../roadmap/backlog.md`](../roadmap/backlog.md) — the testing item this doc details; the `IClock` debt entry WP-3 closes.
- [`../reference/systems.md`](../reference/systems.md) — the systems under test and their contracts.
- Per-system use-case docs are linked inline in WP-4.

For the slice queue, see [`../roadmap/plan.md`](../roadmap/plan.md).
