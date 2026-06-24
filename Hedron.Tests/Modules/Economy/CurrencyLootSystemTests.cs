using System.Reflection;
using Hedron.Core.ECS;
using Hedron.Core.Events;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="CurrencyLootSystem"/>.
    ///
    /// Coverage contract: Loot-roll determinism postcondition from
    /// docs/implementation-plans/currency-foundation.md (WP-2).
    ///
    ///   • Under a fixed <see cref="FakeRandom"/>, <see cref="CurrencyLootSystem.RollLoot"/>
    ///     returns exactly the expected inclusive [min, max] value per currency.
    ///   • Zero / absent range ⇒ no entry for that currency.
    ///   • Absent component ⇒ empty result.
    ///   • INV-5: <see cref="CurrencyLootSystem"/> must not hold <see cref="IEventBus"/>.
    ///   • INV-26: randomness routed through injected <see cref="IRandom"/>.
    /// </summary>
    public sealed class CurrencyLootSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static (CurrencyLootSystem system, EntityService ecs) Build(FakeRandom rng)
        {
            var ecs = new EntityService();
            var system = new CurrencyLootSystem(ecs, rng);
            return (system, ecs);
        }

        private static uint MobWithLoot(EntityService ecs, int min, int max)
        {
            var entity = ecs.CreateEntity();
            ecs.AddComponent(entity.Id, new CurrencyLootComponent
            {
                Ranges = { [CurrencyId.Coin] = (min, max) }
            });
            return entity.Id;
        }

        // ── Determinism under FakeRandom ─────────────────────────────────────────

        [Fact]
        public void RollLoot_returns_prescribed_value_within_inclusive_range()
        {
            // FakeRandom(params int[]) prescribes 50; range [10, 100] → Next(10, 101) → value must be 50.
            var rng = new FakeRandom(new int[] { 50 });
            var (sys, ecs) = Build(rng);
            var mobId = MobWithLoot(ecs, min: 10, max: 100);

            var result = sys.RollLoot(mobId);

            Assert.True(result.Awards.ContainsKey(CurrencyId.Coin),
                "Roll within range must produce an entry.");
            Assert.Equal(50L, result.Awards[CurrencyId.Coin]);
        }

        [Fact]
        public void RollLoot_prescribed_min_value_is_included()
        {
            // Value at the minimum of the range [5, 10] → Next(5, 11) → prescribe 5.
            var rng = new FakeRandom(new int[] { 5 });
            var (sys, ecs) = Build(rng);
            var mobId = MobWithLoot(ecs, min: 5, max: 10);

            var result = sys.RollLoot(mobId);

            Assert.True(result.Awards.ContainsKey(CurrencyId.Coin));
            Assert.Equal(5L, result.Awards[CurrencyId.Coin]);
        }

        [Fact]
        public void RollLoot_prescribed_max_value_is_included()
        {
            // Value at the maximum of the range [5, 10] → Next(5, 11) → prescribe 10.
            var rng = new FakeRandom(new int[] { 10 });
            var (sys, ecs) = Build(rng);
            var mobId = MobWithLoot(ecs, min: 5, max: 10);

            var result = sys.RollLoot(mobId);

            Assert.True(result.Awards.ContainsKey(CurrencyId.Coin));
            Assert.Equal(10L, result.Awards[CurrencyId.Coin]);
        }

        [Fact]
        public void RollLoot_determinism_is_reproducible_under_same_seed()
        {
            // Two identical seeded FakeRandoms must produce identical results.
            // FakeRandom(int seed) → uses seeded Random — deterministic across instances.
            var (sys1, ecs1) = Build(new FakeRandom(seed: 42));
            var (sys2, ecs2) = Build(new FakeRandom(seed: 42));

            // Range [1, 100] → Next(1, 101). Seeded Random(42) is deterministic.
            var mob1 = MobWithLoot(ecs1, 1, 100);
            var mob2 = MobWithLoot(ecs2, 1, 100);

            var r1 = sys1.RollLoot(mob1);
            var r2 = sys2.RollLoot(mob2);

            Assert.Equal(r1.Awards[CurrencyId.Coin], r2.Awards[CurrencyId.Coin]);
        }

        // ── Zero / absent range → no entry ───────────────────────────────────────

        [Fact]
        public void RollLoot_zero_max_yields_no_entry_for_that_currency()
        {
            // Seed ctor: FakeRandom will not be called for a zero range (no Next invocation).
            var rng = new FakeRandom(seed: 0);
            var (sys, ecs) = Build(rng);

            var entity = ecs.CreateEntity();
            ecs.AddComponent(entity.Id, new CurrencyLootComponent
            {
                Ranges = { [CurrencyId.Coin] = (0, 0) }
            });

            var result = sys.RollLoot(entity.Id);

            Assert.False(result.Awards.ContainsKey(CurrencyId.Coin),
                "Zero max must produce no entry — opt-in default: no drop.");
        }

        [Fact]
        public void RollLoot_empty_ranges_dictionary_yields_empty_result()
        {
            var rng = new FakeRandom(seed: 42);
            var (sys, ecs) = Build(rng);

            var entity = ecs.CreateEntity();
            ecs.AddComponent(entity.Id, new CurrencyLootComponent()); // empty Ranges

            var result = sys.RollLoot(entity.Id);

            Assert.Empty(result.Awards);
        }

        // ── Absent component → empty result ──────────────────────────────────────

        [Fact]
        public void RollLoot_absent_component_returns_empty_result()
        {
            var rng = new FakeRandom(seed: 42);
            var (sys, ecs) = Build(rng);

            var entity = ecs.CreateEntity(); // no CurrencyLootComponent

            var result = sys.RollLoot(entity.Id);

            Assert.Empty(result.Awards);
        }

        // ── INV-5: system must not hold IEventBus ────────────────────────────────

        [Fact]
        public void CurrencyLootSystem_does_not_hold_IEventBus_field()
        {
            var fields = typeof(CurrencyLootSystem).GetFields(
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.Public);

            foreach (var field in fields)
            {
                Assert.False(
                    typeof(IEventBus).IsAssignableFrom(field.FieldType),
                    $"INV-5: CurrencyLootSystem field '{field.Name}' is IEventBus — " +
                    "systems must never hold or publish to the event bus.");
            }
        }
    }
}
