# Phase 4 — Testing Harness & System Backfill

**PR:** (this branch) · **Spec:** [`../../implementation-plans/testing-harness-and-backfill.md`](../../implementation-plans/testing-harness-and-backfill.md)

## Outcome

Stood up the `Hedron.Tests` xUnit project and the full shared test harness, enabling the per-slice `dotnet test` gate (INV-25) that had been designed but not yet enforceable. The architecture-guard suite converts the most mechanical INV checks (INV-3/5/13/23/26) into a continuous automatic gate backed by reflection and source-scanning. The `IClock`/`SystemClock` seam closes the remaining INV-26 time-determinism debt by refactoring `SpawnSystem` and `AccountSystem` off `DateTime.UtcNow`, mirroring the `IRandom`/`SystemRandom` precedent. Wave 1 + Wave 2 backfill (566 tests total) provides decision-level coverage for all ~19 systems; CI runs build + test on every PR. As a bonus, a pre-existing INV-21 violation in `ItemSystem` (clearing `BlueprintComponent` on pickup) was caught and fixed.

## Shipped pieces

| Surface | Location | Note |
|---|---|---|
| `Hedron.Tests.csproj` | `Hedron.Tests/Hedron.Tests.csproj` | `net8.0`; refs `Core` + `Server`; xunit 2.9, SDK 17.10, `Microsoft.Data.Sqlite` 8.0 |
| `RecordingEventBus` | `Hedron.Tests/Harness/RecordingEventBus.cs` | `IEventBus` impl; captures published events; optional dispatch in priority order |
| `EntityBuilder` | `Hedron.Tests/Harness/EntityBuilder.cs` | Fluent fixture builder: `.AsPlayer()`, `.AsMob()`, `.WithPools()`, `.WithAttributes()`, `.InRoom()`, `.Wielding()`, `.With<T>()`, `.Build()` |
| `FakeRandom` | `Hedron.Tests/Harness/FakeRandom.cs` | `IRandom` impl; prescribed int/double queues + seeded fallback |
| `FakeClock` | `Hedron.Tests/Harness/FakeClock.cs` | `IClock` impl; settable `UtcNow`, `Advance(TimeSpan)` |
| `RecordingOutput` | `Hedron.Tests/Harness/RecordingOutput.cs` | Fake `IOutputWriter`/`IOutputWriterFactory`; records `(Type, recipientId)` — no prose assertions |
| `PersistenceTestHarness` | `Hedron.Tests/Harness/PersistenceTestHarness.cs` | Real `PersistenceSystem` on `:memory:` SQLite (shared-cache URI keeps connection alive across reload); `IDisposable` |
| `Ticks` | `Hedron.Tests/Harness/Ticks.cs` | `Ticks.At(id, elapsedMs)` → `HeartbeatTickEvent` factory |
| `ArchitectureGuardTests` | `Hedron.Tests/Architecture/ArchitectureGuardTests.cs` | INV-5 no-bus-in-systems; INV-3 components-are-data; INV-13 entity-refs-are-uint; INV-23 world-content-not-persistent; INV-26 no ambient randomness + wall-clock scan; DI-smoke |
| `IClock` / `SystemClock` | `Core/Systems/IClock.cs`, `Core/Systems/SystemClock.cs` | Core seam; `DateTime UtcNow { get; }` / `DateTime.UtcNow` production impl |
| `SpawnSystem` refactor | `Core/Modules/Spawn/Systems/SpawnSystem.cs` | `IClock` injected; all 3 `DateTime.UtcNow` reads replaced |
| `AccountSystem` refactor | `Core/Modules/Account/Systems/AccountSystem.cs` | `IClock` injected; `CreatedAtUtc`/`LastLoginUtc`/throttle reads replaced |
| `CompositionRoot` | `Server/CompositionRoot.cs` | `Register(IServiceCollection, IConfiguration)` extraction from `Server/Program.cs`; no behavior change; enables DI-smoke |
| `PersistenceSystem` tweak | `Core/Infrastructure/Persistence/PersistenceSystem.cs` | `EnsureConnection()` skips directory scaffolding for `file:` URI / `:memory:` paths |
| `ItemSystem` INV-21 fix | `Core/Modules/Items/Systems/ItemSystem.cs` | Removed illegal `RemoveComponent<BlueprintComponent>` call on item pickup |
| CI workflow | `.github/workflows/ci.yml` | `dotnet build` + `dotnet test` on `pull_request` + push to `master`; .NET 8 SDK |
| Wave 1 tests | `Hedron.Tests/Combat/`, `Effects/`, `Stats/`, `Abilities/`, `Death/`, `Persistence/`, `Registry/` | See table below |
| Wave 2 tests | `Hedron.Tests/Items/`, `Movement/`, `Regeneration/`, `Attributes/`, `Aspects/`, `Spawn/`, `Account/`, `Authoring/`, `Broadcast/`, `Output/` | See table below |
| `IClock` catalogued | `docs/reference/systems.md` | INV-16 |
| `add-tests` skill updated | `.claude/skills/add-tests/SKILL.md` | Harness is live (dropped "once it exists" hedge); `FakeClock` added; Tier 3 example corrected for INV-21 |

## Tests shipped

**Harness self-tests (WP-1):** each helper ships with one or more `[Fact]` self-tests — 20 tests total on project creation.

**Architecture-guard suite (WP-2 / WP-3.4):** 6 tests; mutation-verified (INV-5 guard fails when `IEventBus` field added to a system; INV-23 guard fails when `[Persistent]` added to `RoomComponent`).

**Wave 1 backfill (WP-4):**

| System | File | Tier | Count | Key postconditions covered |
|---|---|---|---|---|
| `CombatSystem` | `Combat/CombatSystemTests.cs`, `CombatFlowTests.cs` | 1 + 3 | 25 | Hit/miss/damage, MobDied/PlayerIncapacitated outcomes, StartCombat/EndCombat, TryFindTargetInRoom, full kill flow, INV-21 no-blueprint-clear, dedup, HP clamp |
| `EffectSystem` | `Effects/EffectSystemTests.cs` | 1 + 4 | 21 | HighestWins/Stack, AdvanceTick expiry + sort, GetModifiers incl. IEffectContributor (INV-24), UntilRemoved round-trip |
| `StatSystem` | `Stats/StatSystemTests.cs` | 1 | 18 | AttackPower formula, Defense formula, Get(ScoreId) with effect modifiers |
| `AbilitySystem` | `Abilities/AbilitySystemTests.cs` | 1 | 38 | Validation order, atomicity (no partial spend), cost spend, cooldown, effects, resolveOffensiveExternally, AdvanceCooldowns, Learn/Teach/IsKnown |
| `DeathSystem` | `Death/DeathSystemTests.cs` | 1 | 33 | OnHpChanged thresholds, player-only, Respawn (state/location/effects/pools), SetRespawn validation |
| `PersistenceSystem` | `Persistence/RoundTripTests.cs` | 4 | 8 | Player round-trip, transient absent, world-content no row, multi-entity, field equality, auto-delete |
| `RegistryValidationBootstrap` | `Registry/RegistryValidationTests.cs` | 5 | 9 | Dangling ability→effect/aspect refs, bad AspectComposition, bad StartingAbilities, valid passes |

**Wave 2 backfill (WP-6):**

| System | File | Tier | Count |
|---|---|---|---|
| `ItemSystem` | `Items/ItemSystemTests.cs` | 1 | 35 |
| `EquipmentSystem` | `Items/EquipmentSystemTests.cs` | 1 | 32 |
| `MovementSystem` | `Movement/MovementSystemTests.cs` | 1 | 15 |
| `RegenerationSystem` | `Regeneration/RegenerationSystemTests.cs` | 1 | 27 |
| `AttributeSystem` | `Attributes/AttributeSystemTests.cs` | 1 | 53 |
| `AspectSystem` | `Aspects/AspectSystemTests.cs` | 1 | 35 |
| `SpawnSystem` | `Spawn/SpawnSystemTests.cs` | 1 | 16 |
| `AccountSystem` | `Account/AccountSystemTests.cs` | 1 | 37 |
| `RoomBuilderSystem` | `Authoring/RoomBuilderSystemTests.cs` | 1 | 27 |
| `ItemBuilderSystem` | `Authoring/ItemBuilderSystemTests.cs` | 1 | 36 |
| `MobBuilderSystem` | `Authoring/MobBuilderSystemTests.cs` | 1 | 36 |
| `BroadcastSystem` | `Broadcast/BroadcastSystemTests.cs` | 1 | 18 |
| `PromptComposerSystem` | `Output/PromptComposerSystemTests.cs` | 1 | 19 |

**Total: 566 tests. `dotnet test Hedron.sln` green (INV-25).**

## Spec-review provenance

**Spec gate (spec-mode):** Ran before any implementation. Two blocking findings resolved in the use-case doc before code was written:

- **B1 (INV-21):** WP-4.1 originally directed a test to assert `BlueprintComponent` is cleared before `DestroyEntity`. INV-21 says the opposite — clearing is a violation. Corrected: test now asserts `BlueprintComponent` was NOT explicitly cleared. Stale INV-21 language in `backlog.md` also corrected.
- **B2 (SR-3):** WP-1.6 specified temp-file SQLite (`Path.GetTempFileName()`); `07-testing.md` canonically specifies `:memory:`. Corrected to use a named shared-cache `:memory:` URI.

**Code gate (code-mode):** Ran after all WPs landed. APPROVE WITH NITS — no blocking findings. Three nits addressed:

- `add-tests/SKILL.md` updated (INV-20): dropped "once it exists" hedge, added `FakeClock`, fixed stale INV-21 Tier-3 example.
- `SpawnSystemTests.cs`: local `Tick()` helper replaced with `Ticks.At()` factory.
- Use-case doc trimmed per INV-D2.

## Notable design points

- **`:memory:` SQLite with shared-cache URI.** A `:memory:` SQLite db is connection-scoped; `ReloadIntoFreshWorld()` must reuse the same `SqliteConnection` rather than opening a new one. The harness uses a named shared-cache URI (`file:hedron_test_<uuid>?mode=memory&cache=shared`) and keeps a "keeper" connection alive so the db survives the reload path.
- **`IClock` mirrors `IRandom`.** Same pattern, same rationale (INV-26): a tiny injected seam that makes time-dependent logic deterministically testable. `SpawnSystem` is the primary beneficiary (respawn-after-N-seconds); `AccountSystem` is migrated for purity.
- **Wall-clock guard held until debt is gone.** WP-2.5 added the randomness scan first; the wall-clock (`DateTime.UtcNow`) clause was added in WP-3.4 *after* WP-3.2/3.3 removed the two debt sites. Enabling the guard before removing debt would have made it permanently red.
- **`CompositionRoot` in `Server/`.** Extracted so the DI-smoke guard (WP-2.6) can call `Register(...)` directly without booting the full hosted service pipeline. No behavior change.
- **INV-21 bug in `ItemSystem`.** `MoveToInventory` was calling `RemoveComponent<BlueprintComponent>` on pickup — the exact violation INV-21 forbids. Found and fixed during WP-6/ItemSystem backfill when the INV-21 test assertion would have failed against correct code. The architecture-guard does not mechanically check for this (it checks structure, not runtime behavior); the per-system test caught it.
- **Wave 3 deferred.** WP-7 (remaining systems) is explicitly opportunistic — drained by the on-touch ratchet (INV-25) as future slices touch those systems. No fixed scope was attached.

## Deviations from the use-case doc

- **B1/B2 spec corrections (pre-implementation).** The use-case doc was corrected before any code was written per the spec-review gate; the as-built implementation matches the corrected spec.
- **`PersistenceSystem.EnsureConnection()` tweak.** A small production change was required (skip directory scaffolding for `file:` URI / `:memory:` paths) to let the harness use the real `PersistenceSystem` with an in-memory db. Not anticipated in the spec but consistent with its intent (use the real system).

## Follow-ups unlocked

- **Per-slice `dotnet test` gate is live.** Every future slice's test plan is now machine-enforced by CI.
- **Wave 3 / opportunistic (WP-7).** Remaining systems (`BroadcastSystem` already done; any gaps the guard surfaces) drain via the on-touch ratchet.
- **Shopping (slice 12).** No testing prerequisite remains; the harness is available to all future slices.
