using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Events;
using Hedron.Core.Modules.Progression.Handlers;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 2 — handler / orchestration tests for <see cref="ExperienceAwardHandler"/>.
    ///
    /// Coverage contract: Events-fired postconditions from
    /// docs/roadmap/completed/progression-substrate.md (WP-2).
    /// </summary>
    public sealed class ExperienceAwardHandlerTests
    {
        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public ProgressionSystem Progression { get; }
            public ExperienceAwardHandler Handler { get; }
            public RecordingEventBus Bus { get; }

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();
                Progression = new ProgressionSystem(Ecs, rng, new PowerBudgetSystem(PowerBudgetTunables.Default));
                Bus = new RecordingEventBus(dispatch: false);
                Handler = new ExperienceAwardHandler(Progression, Bus);
            }
        }

        private static uint CreateCombatant(EntityService ecs, int power = 10)
            => new EntityBuilder(ecs).AsPlayer().WithAttributes(power, power, power, power).Build();

        [Fact]
        public async Task Peer_kill_publishes_one_ExperienceAwardedEvent_per_track()
        {
            var world = new TestWorld(new FakeRandom(new[] { 10, 10 }));
            var killer = CreateCombatant(world.Ecs);
            var victim = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.test", killer));

            var awarded = world.Bus.Published.OfType<ExperienceAwardedEvent>().ToList();
            Assert.Equal(ProgressionConstants.CombatTracks.Length, awarded.Count);
            Assert.All(awarded, e =>
            {
                Assert.Equal(killer, e.EntityId);
                Assert.Equal(10, e.Amount);
                Assert.Equal(XpSource.CombatKill, e.Source);
            });
        }

        [Fact]
        public async Task No_threshold_crossed_publishes_no_TrackImprovedEvent()
        {
            var world = new TestWorld(new FakeRandom(new[] { 10, 10 }));
            var killer = CreateCombatant(world.Ecs);
            var victim = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.test", killer));

            Assert.Empty(world.Bus.Published.OfType<TrackImprovedEvent>());
        }

        [Fact]
        public async Task Threshold_crossed_publishes_one_TrackImprovedEvent_per_crossing()
        {
            // CombatAwardMin/Max is 8-12 per kill; repeated kills accumulate until the first
            // threshold (100) is crossed on the Body track — assert exactly one crossing fires.
            var world = new TestWorld(new FakeRandom(seed: 7));
            var killer = CreateCombatant(world.Ecs);
            var victim = CreateCombatant(world.Ecs);

            for (var i = 0; i < 20 && world.Progression.GetImprovementCount(killer, ScoreId.Body) == 0; i++)
                await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.test", killer));

            Assert.Equal(1, world.Progression.GetImprovementCount(killer, ScoreId.Body));

            var improved = world.Bus.Published.OfType<TrackImprovedEvent>()
                .Where(e => e.Track == ScoreId.Body).ToList();
            Assert.Single(improved);
            Assert.Equal(killer, improved[0].EntityId);
            Assert.Equal(1, improved[0].NewImprovementCount);
        }

        [Fact]
        public async Task KillerEntityId_zero_publishes_nothing()
        {
            var world = new TestWorld(new FakeRandom(seed: 1)); // no ints should be drawn
            var victim = CreateCombatant(world.Ecs);

            await world.Handler.HandleAsync(new MobDiedEvent(victim, "mob.test", KillerEntityId: 0));

            Assert.Empty(world.Bus.Published);
        }
    }
}
