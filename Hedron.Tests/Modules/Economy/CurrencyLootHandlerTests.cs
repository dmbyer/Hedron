using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Events;
using Hedron.Core.Modules.Economy.Handlers;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 2 — handler / orchestration tests for <see cref="CurrencyLootHandler"/>.
    ///
    /// Coverage contract: Auto-award and No-killer-discard postconditions from
    /// docs/implementation-plans/currency-foundation.md (WP-2).
    ///
    ///   • On <see cref="MobDiedEvent"/> with non-zero rolled amount and KillerEntityId != 0:
    ///     <see cref="IWalletSystem.Deposit"/> called per rolled currency;
    ///     one <see cref="CurrencyAwardedEvent"/> published per currency awarded.
    ///   • KillerEntityId == 0 ⇒ no deposit, no event (discard).
    ///   • No loot component ⇒ no deposit, no event.
    ///   • Multiple currencies ⇒ one event per currency.
    /// </summary>
    public sealed class CurrencyLootHandlerTests
    {
        // ── Test world ────────────────────────────────────────────────────────────

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public WalletSystem Wallets { get; }
            public CurrencyLootSystem LootSystem { get; }
            public CurrencyLootHandler Handler { get; }
            public RecordingEventBus Bus { get; }

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();
                Wallets = new WalletSystem(Ecs);
                LootSystem = new CurrencyLootSystem(Ecs, rng);
                Bus = new RecordingEventBus(dispatch: false); // record-only; handler is called directly
                Handler = new CurrencyLootHandler(LootSystem, Wallets, Bus);
            }
        }

        private static uint CreateMob(EntityService ecs, int min, int max)
        {
            var mob = ecs.CreateEntity();
            ecs.AddComponent(mob.Id, new CurrencyLootComponent
            {
                Ranges = { [CurrencyId.Coin] = (min, max) }
            });
            return mob.Id;
        }

        private static uint CreateMobNoLoot(EntityService ecs)
            => ecs.CreateEntity().Id;

        private static uint CreateKiller(EntityService ecs)
            => ecs.CreateEntity().Id;

        // ── Deposit to killer on kill with loot ──────────────────────────────────

        [Fact]
        public async Task Deposit_called_with_rolled_amount_when_killer_present()
        {
            // FakeRandom(params int[]) prescribes 35; range [10, 50] → Next(10, 51) → 35.
            var world = new TestWorld(new FakeRandom(new int[] { 35 }));

            var killerId = CreateKiller(world.Ecs);
            var mobId = CreateMob(world.Ecs, min: 10, max: 50);

            await world.Handler.HandleAsync(new MobDiedEvent(mobId, "mob.test", killerId));

            Assert.Equal(35L, world.Wallets.GetBalance(killerId, CurrencyId.Coin));
        }

        [Fact]
        public async Task CurrencyAwardedEvent_published_once_per_currency_on_kill()
        {
            // FakeRandom(params int[]) prescribes 20; range [1, 100] → Next(1, 101) → 20.
            var world = new TestWorld(new FakeRandom(new int[] { 20 }));

            var killerId = CreateKiller(world.Ecs);
            var mobId = CreateMob(world.Ecs, min: 1, max: 100);

            await world.Handler.HandleAsync(new MobDiedEvent(mobId, "mob.test", killerId));

            var awarded = world.Bus.Published.OfType<CurrencyAwardedEvent>().ToList();
            Assert.Single(awarded);
            Assert.Equal(killerId, awarded[0].RecipientEntityId);
            Assert.Equal(CurrencyId.Coin, awarded[0].Currency);
            Assert.Equal(20L, awarded[0].Amount);
        }

        // ── KillerEntityId == 0 → discard ────────────────────────────────────────

        [Fact]
        public async Task No_deposit_and_no_event_when_killer_is_zero()
        {
            // Seed ctor: any seeded FakeRandom works here; Next should not be called when killer is 0.
            var world = new TestWorld(new FakeRandom(seed: 50));

            var mobId = CreateMob(world.Ecs, min: 1, max: 100);
            const uint noKiller = 0u;

            await world.Handler.HandleAsync(new MobDiedEvent(mobId, "mob.test", noKiller));

            // No entity should have received currency.
            Assert.Empty(world.Bus.Published.OfType<CurrencyAwardedEvent>());

            // No WalletComponent on mob (which is the only entity in the ECS here).
            Assert.False(world.Ecs.HasComponent<WalletComponent>(mobId),
                "Mob must not receive currency when killer is zero (no-killer discard).");
        }

        // ── Absent loot component → no deposit, no event ─────────────────────────

        [Fact]
        public async Task No_deposit_and_no_event_when_mob_has_no_loot_component()
        {
            var world = new TestWorld(new FakeRandom(seed: 42));

            var killerId = CreateKiller(world.Ecs);
            var mobId = CreateMobNoLoot(world.Ecs);

            await world.Handler.HandleAsync(new MobDiedEvent(mobId, "mob.test", killerId));

            Assert.Equal(0L, world.Wallets.GetBalance(killerId, CurrencyId.Coin));
            Assert.Empty(world.Bus.Published.OfType<CurrencyAwardedEvent>());
        }

        // ── Multiple currencies → one event per currency ──────────────────────────

        [Fact]
        public async Task Multiple_currencies_produce_one_event_each()
        {
            // Range [5, 5] → Next(5, 6) → prescribe 5 (only valid value in that range).
            // We only have CurrencyId.Coin right now, so this test validates the per-currency loop
            // with a single currency and verifies the count is exactly 1 per rolled currency.
            // When a second CurrencyId is added this test should be extended.
            var world = new TestWorld(new FakeRandom(new int[] { 5 }));

            var killerId = CreateKiller(world.Ecs);
            var mob = world.Ecs.CreateEntity();
            world.Ecs.AddComponent(mob.Id, new CurrencyLootComponent
            {
                Ranges = { [CurrencyId.Coin] = (5, 5) }
            });

            await world.Handler.HandleAsync(new MobDiedEvent(mob.Id, "mob.test", killerId));

            var awarded = world.Bus.Published.OfType<CurrencyAwardedEvent>().ToList();
            Assert.Single(awarded); // exactly one per currency (only Coin currently)
            Assert.Equal(5L, awarded[0].Amount);
        }

        // ── WalletComponent created on first deposit ──────────────────────────────

        [Fact]
        public async Task WalletComponent_created_on_killer_on_first_loot_award()
        {
            // Prescribes 10; range [10, 10] → Next(10, 11) → exactly 10.
            var world = new TestWorld(new FakeRandom(new int[] { 10 }));

            var killerId = CreateKiller(world.Ecs);
            var mobId = CreateMob(world.Ecs, min: 10, max: 10);

            Assert.False(world.Ecs.HasComponent<WalletComponent>(killerId),
                "Precondition: killer has no wallet before kill.");

            await world.Handler.HandleAsync(new MobDiedEvent(mobId, "mob.test", killerId));

            Assert.True(world.Ecs.HasComponent<WalletComponent>(killerId),
                "WalletComponent must be created on first loot deposit.");
        }
    }
}
