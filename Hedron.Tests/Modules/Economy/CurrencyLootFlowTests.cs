using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Combat;
using Hedron.Core.Modules.Combat.Events;
using Hedron.Core.Modules.Combat.Handlers;
using Hedron.Core.Modules.Combat.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Death.Systems;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Economy.Components;
using Hedron.Core.Modules.Economy.Events;
using Hedron.Core.Modules.Economy.Handlers;
using Hedron.Core.Modules.Economy.Systems;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 3 — flow tests for the mob-death → currency-loot → wallet-award path (Flow A).
    ///
    /// Coverage contract: Auto-award postcondition end-to-end (from
    /// docs/implementation-plans/currency-foundation.md WP-2, Tier 3):
    ///
    ///   Wire combat death + CurrencyLootHandler + WalletSystem with seeded IRandom;
    ///   kill a mob carrying a loot range; assert the killer's WalletComponent balance
    ///   increased by the seeded roll and CurrencyAwardedEvent fired.
    ///
    /// Modelled on <see cref="Hedron.Tests.Combat.CombatFlowTests"/>.
    /// </summary>
    public sealed class CurrencyLootFlowTests
    {
        // ── Stub IDeathSystem ────────────────────────────────────────────────────

        private sealed class NoOpDeathSystem : IDeathSystem
        {
            public DeathTransition OnHpChanged(uint entityId, int previousHp, int newHp)
                => DeathTransition.None;

            public void Respawn(uint entityId) { }

            public bool SetRespawn(uint entityId, string roomBlueprintId, out string? failReason)
            {
                failReason = null;
                return true;
            }
        }

        // ── Test world ────────────────────────────────────────────────────────────

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public WalletSystem Wallets { get; }
            public CombatSystem Combat { get; }
            public EntityStateService EntityState { get; }
            public RecordingEventBus Bus { get; }
            public CombatTickHandler TickHandler { get; }
            public CombatMobDeathHandler MobDeathHandler { get; }
            public CurrencyLootHandler CurrencyLoot { get; }

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();

                var noEffects = new EffectSystem(Ecs, System.Array.Empty<IEffectContributor>());
                var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });

                var attributes = new AttributeSystem(Ecs, noEffects, deathOpts);
                var stats = new StatSystem(attributes, noEffects);
                var aspects = new AspectSystem(Ecs);
                Combat = new CombatSystem(Ecs, stats, attributes, aspects, rng);
                EntityState = new EntityStateService(Ecs);
                Wallets = new WalletSystem(Ecs);

                var lootSystem = new CurrencyLootSystem(Ecs, rng);

                // dispatch=true so handlers fire automatically when events are published.
                Bus = new RecordingEventBus(dispatch: true);

                var deathSystem = new NoOpDeathSystem();

                TickHandler = new CombatTickHandler(
                    Ecs,
                    Combat,
                    EntityState,
                    deathSystem,
                    stats,
                    Bus,
                    NullLogger<CombatTickHandler>.Instance);

                MobDeathHandler = new CombatMobDeathHandler(Ecs, EntityState, Bus);
                CurrencyLoot = new CurrencyLootHandler(lootSystem, Wallets, Bus);

                Bus.Subscribe<Hedron.Core.Modules.Time.Events.HeartbeatTickEvent>(TickHandler);
                Bus.Subscribe<CombatEndedEvent>(MobDeathHandler);
                Bus.Subscribe<Hedron.Core.Modules.Mobs.Events.MobDiedEvent>(CurrencyLoot);
            }
        }

        // ── Flow A: mob death → loot → award ──────────────────────────────────────

        /// <summary>
        /// Full path: combat tick kills the mob → CombatMobDeathHandler publishes MobDiedEvent
        /// (mob still live) → CurrencyLootHandler rolls loot and deposits to killer →
        /// CurrencyAwardedEvent published.
        ///
        /// The IRandom is shared between CombatSystem and CurrencyLootSystem. The FakeRandom
        /// prescribes enough values:
        ///   hit=20, damage=1 (combat — 1-HP mob dies instantly), then
        ///   loot_roll=25 (CurrencyLootSystem draws from the same IRandom).
        /// CombatSystem draws 2 ints per round (hit + damage); loot draws 1.
        /// </summary>
        [Fact]
        public async Task MobDeath_with_loot_increases_killer_wallet_by_seeded_roll()
        {
            // Sequence: combat hit=20, damage=1 (kills 1-HP mob), then loot roll=25 (range [10,50]).
            var rng = new FakeRandom(20, 1, 25);
            var world = new TestWorld(rng);

            const uint roomId = 1u;

            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("rat")
                .WithAttributes(body: 10)
                .WithPools(hp: 1)
                .InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                .With(new CurrencyLootComponent { Ranges = { [CurrencyId.Coin] = (10, 50) } })
                .Build();

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            // Pump one heartbeat tick.
            await world.Bus.PublishAsync(Ticks.At(1));

            // Killer's wallet must reflect the loot roll.
            Assert.Equal(25L, world.Wallets.GetBalance(playerId, CurrencyId.Coin)); // Killer's wallet must have increased by the seeded loot roll (25).
        }

        [Fact]
        public async Task MobDeath_with_loot_publishes_CurrencyAwardedEvent()
        {
            var rng = new FakeRandom(20, 1, 30);
            var world = new TestWorld(rng);

            const uint roomId = 2u;

            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("goblin")
                .WithAttributes(body: 10)
                .WithPools(hp: 1)
                .InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.goblin" })
                .With(new CurrencyLootComponent { Ranges = { [CurrencyId.Coin] = (1, 100) } })
                .Build();

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            await world.Bus.PublishAsync(Ticks.At(1));

            var awarded = world.Bus.Published.OfType<CurrencyAwardedEvent>().ToList();
            Assert.True(awarded.Count >= 1,
                "At least one CurrencyAwardedEvent must be published on mob death with loot.");
            Assert.Equal(playerId, awarded[0].RecipientEntityId);
            Assert.Equal(CurrencyId.Coin, awarded[0].Currency);
            Assert.Equal(30L, awarded[0].Amount);
        }

        [Fact]
        public async Task MobDeath_without_loot_component_does_not_publish_CurrencyAwardedEvent()
        {
            var rng = new FakeRandom(20, 1);
            var world = new TestWorld(rng);

            const uint roomId = 3u;

            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            // Mob has NO CurrencyLootComponent.
            var mobId = new EntityBuilder(world.Ecs)
                .AsMob("skeleton")
                .WithAttributes(body: 10)
                .WithPools(hp: 1)
                .InRoom(roomId)
                .With(new BlueprintComponent { BlueprintId = "mob.skeleton" })
                .Build();

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            await world.Bus.PublishAsync(Ticks.At(1));

            var awarded = world.Bus.Published.OfType<CurrencyAwardedEvent>().ToList();
            Assert.Empty(awarded);
            Assert.Equal(0L, world.Wallets.GetBalance(playerId, CurrencyId.Coin));
        }
    }
}
