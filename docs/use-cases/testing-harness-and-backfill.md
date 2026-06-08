# Use Case: Testing Harness & System Backfill (Phase 2)

**Status:** implemented
**Actors:** Developer, System (no player-facing behavior)
**Module:** `Hedron.Tests/` · `Core/Systems/` (`IClock` seam) · `Server/` (`CompositionRoot`)

---

## Description

Stand up the `Hedron.Tests` xUnit project + shared harness, build the reflection-based architecture-guard suite, close INV-26 time-determinism debt with an `IClock` seam, and backfill tests for existing systems so the per-slice gate has a green suite to enforce against.

**Scope.** No gameplay change, no new runtime flow. The only production changes are the `IClock` seam (+ two system refactors) and a `CompositionRoot` extraction in `Server/Program.cs`. Everything else is test code.

---

## Preconditions

- `IRandom`/`SystemRandom` seam in `Core/Systems/`.
- `EntityService` constructable in-memory; `IEventBus` is a 4-method interface.
- `dotnet build Hedron.sln` green on .NET 8.

## Postconditions

- `Hedron.Tests` xUnit project exists, is in `Hedron.sln`, references `Core` + `Server`, and `dotnet test Hedron.sln` runs green.
- Shared harness exists under `Hedron.Tests/Harness/`: `RecordingEventBus`, `EntityBuilder`, `FakeRandom`, `FakeClock`, output capture, in-memory SQLite persistence harness (`PersistenceTestHarness`), and a synthetic `HeartbeatTickEvent` factory (`Ticks`).
- An **architecture-guard suite** enforces INV-3/5/13/23/26 mechanically + a DI-smoke test; it is green against current code and fails when a deliberate violation is introduced.
- `IClock`/`SystemClock` core seam exists; `SpawnSystem` and `AccountSystem` no longer read `DateTime.UtcNow` directly (INV-26 time clause fully satisfied; the guard's wall-clock clause is enabled).
- **Wave 1** systems have decision-level + flow + round-trip + validation coverage (Combat, Effects, Stats, Abilities, Death/respawn, Persistence round-trips, Registry validation).
- **Wave 2** systems have decision-level coverage (Item, Equipment, Movement, Regeneration, Attribute, Aspect, Spawn, Account, authoring builders, Broadcast, PromptComposer).
- The `dotnet test` portion of the per-slice code gate (INV-25) is enforceable; CI runs build + test on every PR.

---

## Main Flow

1. WP-1 lands first — the test project + shared harness.
2. WP-2 (architecture-guard), WP-3 (`IClock` seam), WP-4.* (Wave 1 backfill), WP-5 (CI) fan out after WP-1.
3. Each backfill step asserts the target system's **Postconditions** (its use-case doc is the contract) at the right tier per `docs/architecture/07-testing.md`.
4. Once WP-1…WP-5 land green, Wave 2 (WP-6) applies the same recipe.
5. Wave 3 (WP-7) drains opportunistically via the on-touch ratchet (INV-25).

---

## Events Fired

None. This slice adds no events and publishes nothing.

---

## Design Notes

- **`:memory:` SQLite with shared-cache URI.** A `:memory:` SQLite db is connection-scoped. `PersistenceTestHarness.ReloadIntoFreshWorld()` reuses the same open `SqliteConnection` (named shared-cache URI) so the in-memory db survives the reload path.
- **`IClock` mirrors `IRandom`.** Same pattern, same rationale (INV-26): a tiny injected seam that makes time-dependent logic deterministically testable. `SpawnSystem` is the primary beneficiary; `AccountSystem` is migrated for purity.
- **Wall-clock guard enabled after debt removal.** The wall-clock (`DateTime.UtcNow`) clause was added to the architecture-guard only after WP-3.2/3.3 removed the two debt sites. Enabling the guard before removing debt would make it permanently red.
- **INV-21 bug caught by backfill.** `ItemSystem.MoveToInventory` was calling `RemoveComponent<BlueprintComponent>` on item pickup — the exact violation INV-21 forbids. Found and fixed during Wave 2 backfill; the per-system test would have failed against correct code.
- **Wave 3 is opportunistic.** WP-7 (remaining systems) has no fixed scope — drained by the on-touch ratchet (INV-25) as future slices touch those systems.

---

## Related

- [`../architecture/07-testing.md`](../architecture/07-testing.md) — the strategy, taxonomy, rubric, and harness spec this slice realizes.
- [`../architecture/checklist.md`](../architecture/checklist.md) — INV-25 (verification discipline), INV-26 (determinism seam).
- [`../../.claude/skills/add-tests/SKILL.md`](../../.claude/skills/add-tests/SKILL.md) — the per-tier how-to each backfill step follows.
- [`../reference/systems.md`](../reference/systems.md) — `IClock`/`SystemClock` catalogued here (INV-16).
- [`../roadmap/completed/testing-harness-and-backfill.md`](../roadmap/completed/testing-harness-and-backfill.md) — shipped pieces, test counts, spec-review provenance.
