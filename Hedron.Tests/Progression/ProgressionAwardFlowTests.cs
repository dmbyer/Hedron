using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Commands;
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
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.EntityState.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Commands;
using Hedron.Core.Modules.Progression.Handlers;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Output;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 3 — flow test for the mob-death → combat-XP-award → contribute-on-read path
    /// (flow-31). Wires real systems + handlers + a dispatching bus with seeded <c>IRandom</c>,
    /// kills a mob, and asserts the Main-Flow postconditions end-to-end.
    ///
    /// Modelled on <see cref="Hedron.Tests.Modules.Economy.CurrencyLootFlowTests"/>.
    /// </summary>
    public sealed class ProgressionAwardFlowTests
    {
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

        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public IStatSystem Stats { get; }
            public IProgressionSystem Progression { get; }
            public CombatSystem Combat { get; }
            public EntityStateService EntityState { get; }
            public RecordingEventBus Bus { get; }

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();

                Progression = new ProgressionSystem(Ecs, rng);
                var contributor = new ProgressionEffectContributor(Progression);
                var effects = new EffectSystem(Ecs, new IEffectContributor[] { contributor });
                var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
                var attributes = new AttributeSystem(Ecs, effects, deathOpts);
                Stats = new StatSystem(attributes, effects);

                var aspects = new AspectSystem(Ecs);
                Combat = new CombatSystem(Ecs, Stats, attributes, aspects, rng);
                EntityState = new EntityStateService(Ecs);

                Bus = new RecordingEventBus(dispatch: true);

                var deathSystem = new NoOpDeathSystem();
                var tickHandler = new CombatTickHandler(Ecs, Combat, EntityState, deathSystem, Stats, Bus, NullLogger<CombatTickHandler>.Instance);
                var mobDeathHandler = new CombatMobDeathHandler(Ecs, EntityState, Bus);
                var experienceAward = new ExperienceAwardHandler(Progression, Bus);

                Bus.Subscribe<Hedron.Core.Modules.Time.Events.HeartbeatTickEvent>(tickHandler);
                Bus.Subscribe<CombatEndedEvent>(mobDeathHandler);
                Bus.Subscribe<Hedron.Core.Modules.Mobs.Events.MobDiedEvent>(experienceAward);
            }
        }

        [Fact]
        public async Task MobDeath_awards_combat_experience_and_folds_into_effective_score()
        {
            // combat: hit=20, damage=1 (kills 1-HP mob); progression: Body base=12, HpMax base=12
            // (killer/victim both power 40 → peer scale 1.0, full base each).
            var rng = new FakeRandom(20, 1, 12, 12);
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
                .Build();

            var bodyBefore = world.Stats.Get(playerId, ScoreId.Body);

            world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
            world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
            world.Combat.StartCombat(playerId, mobId);

            await world.Bus.PublishAsync(Ticks.At(1));

            Assert.Equal(12, world.Progression.GetXp(playerId, ScoreId.Body));
            Assert.Equal(12, world.Progression.GetXp(playerId, ScoreId.HpMax));

            var awarded = world.Bus.Published.OfType<Hedron.Core.Modules.Progression.Events.ExperienceAwardedEvent>().ToList();
            Assert.Equal(2, awarded.Count);
            Assert.All(awarded, e => Assert.Equal(playerId, e.EntityId));

            // No threshold crossed at XP 12 (threshold is 100) — effective score unchanged.
            Assert.Equal(bodyBefore, world.Stats.Get(playerId, ScoreId.Body));

            // progress command reflects the accrued XP without prose assertions.
            var output = new RecordingOutput();
            var progressCommand = new ProgressCommand(world.Progression);
            var context = new CommandContext(
                new StubSession(playerId), playerId, ParsedArguments.Empty, output.WriterFor(playerId), null!);
            await progressCommand.ExecuteAsync(context);

            var message = Assert.Single(output.All.Where(r => r.MessageType == typeof(ProgressDisplayMessage)));
            var progressMessage = Assert.IsType<ProgressDisplayMessage>(message.Message);
            Assert.Contains(progressMessage.Rows, r => r.Track == ScoreId.Body && r.CumulativeXp == 12);
            Assert.Contains(progressMessage.Rows, r => r.Track == ScoreId.HpMax && r.CumulativeXp == 12);
        }

        [Fact]
        public async Task MobDeath_threshold_crossing_raises_effective_score_via_contributor()
        {
            // Enough combat kills to cross the first Body threshold (100) via repeated 12-XP awards.
            // Each kill draws 4 ints (combat hit+damage, progression Body+HpMax base); 9 kills
            // (9 * 12 = 108 >= 100) comfortably cross the threshold — queue extra sets as a margin.
            var perKill = new[] { 20, 1, 12, 12 };
            var rngValues = Enumerable.Repeat(perKill, 15).SelectMany(x => x).ToArray();
            var rng = new FakeRandom(rngValues);
            var world = new TestWorld(rng);

            const uint roomId = 2u;
            var playerId = new EntityBuilder(world.Ecs)
                .AsPlayer()
                .WithAttributes(body: 10)
                .WithPools(hp: 100)
                .InRoom(roomId)
                .Build();

            uint MakeMob()
            {
                var id = new EntityBuilder(world.Ecs)
                    .AsMob("rat")
                    .WithAttributes(body: 10)
                    .WithPools(hp: 1)
                    .InRoom(roomId)
                    .With(new BlueprintComponent { BlueprintId = "mob.rat" })
                    .Build();
                return id;
            }

            var tick = 1u;
            while (world.Progression.GetImprovementCount(playerId, ScoreId.Body) == 0 && tick < 20)
            {
                var mobId = MakeMob();
                world.EntityState.TryEnterState(playerId, EntityStateFlags.InCombat, out _);
                world.EntityState.TryEnterState(mobId, EntityStateFlags.InCombat, out _);
                world.Combat.StartCombat(playerId, mobId);
                await world.Bus.PublishAsync(Ticks.At(tick));
                tick++;
            }

            Assert.Equal(1, world.Progression.GetImprovementCount(playerId, ScoreId.Body));
            Assert.Equal(10 + ProgressionConstants.PowerPerImprovement, world.Stats.Get(playerId, ScoreId.Body));

            var improved = world.Bus.Published.OfType<Hedron.Core.Modules.Progression.Events.TrackImprovedEvent>()
                .Where(e => e.Track == ScoreId.Body).ToList();
            Assert.Single(improved);
        }
    }
}
