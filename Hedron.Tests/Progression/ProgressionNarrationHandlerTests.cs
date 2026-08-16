using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Preferences;
using Hedron.Core.Modules.Preferences.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Events;
using Hedron.Core.Modules.Progression.Handlers;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 2 — handler tests for <see cref="ProgressionNarrationHandler"/>.
    ///
    /// Asserts <b>that</b> a line was written and to whom, never its prose — narration wording is
    /// presentation and deliberately not pinned (see the slice's Test plan).
    /// </summary>
    public sealed class ProgressionNarrationHandlerTests
    {
        private sealed class TestWorld
        {
            public EntityService Ecs { get; } = new();
            public IPreferenceSystem Preferences { get; }
            public RecordingBroadcastSystem Broadcast { get; } = new();
            public ProgressionNarrationHandler Handler { get; }
            public uint Player { get; }

            public TestWorld()
            {
                Preferences = new PreferenceSystem(Ecs);
                Handler = new ProgressionNarrationHandler(Preferences, Broadcast);
                Player = new EntityBuilder(Ecs).AsPlayer().Build();
            }
        }

        [Fact]
        public async Task An_award_writes_one_line_to_the_earner_when_enabled()
        {
            var world = new TestWorld();

            await world.Handler.HandleAsync(new ExperienceAwardedEvent(
                world.Player, ProgressionTrack.Of(ScoreId.Body), 12, XpSource.CombatKill));

            var sent = Assert.Single(world.Broadcast.ToEntity);
            Assert.Equal(world.Player, sent.EntityId);
            Assert.Empty(world.Broadcast.ToRoom);
            Assert.Empty(world.Broadcast.ToAll);
        }

        [Fact]
        public async Task An_ability_track_award_also_writes_one_line()
        {
            var world = new TestWorld();

            await world.Handler.HandleAsync(new ExperienceAwardedEvent(
                world.Player, ProgressionTrack.Ability("kick"), 4, XpSource.AbilityUse));

            Assert.Single(world.Broadcast.ToEntity);
        }

        [Fact]
        public async Task An_award_writes_nothing_when_the_xp_preference_is_off()
        {
            var world = new TestWorld();
            world.Preferences.Set(world.Player, PreferenceId.ProgressionXpMessages, enabled: false);

            await world.Handler.HandleAsync(new ExperienceAwardedEvent(
                world.Player, ProgressionTrack.Of(ScoreId.Body), 12, XpSource.CombatKill));

            Assert.Empty(world.Broadcast.ToEntity);
        }

        [Fact]
        public async Task An_improvement_writes_one_line_when_enabled()
        {
            var world = new TestWorld();

            await world.Handler.HandleAsync(new TrackImprovedEvent(
                world.Player, ProgressionTrack.Of(ScoreId.Body), 1));

            var sent = Assert.Single(world.Broadcast.ToEntity);
            Assert.Equal(world.Player, sent.EntityId);
        }

        [Fact]
        public async Task An_improvement_writes_nothing_when_the_improvement_preference_is_off()
        {
            var world = new TestWorld();
            world.Preferences.Set(world.Player, PreferenceId.ProgressionImprovementMessages, enabled: false);

            await world.Handler.HandleAsync(new TrackImprovedEvent(
                world.Player, ProgressionTrack.Ability("kick"), 2));

            Assert.Empty(world.Broadcast.ToEntity);
        }

        [Fact]
        public async Task The_two_preferences_gate_independently()
        {
            var world = new TestWorld();
            world.Preferences.Set(world.Player, PreferenceId.ProgressionXpMessages, enabled: false);

            await world.Handler.HandleAsync(new ExperienceAwardedEvent(
                world.Player, ProgressionTrack.Of(ScoreId.Body), 12, XpSource.CombatKill));
            await world.Handler.HandleAsync(new TrackImprovedEvent(
                world.Player, ProgressionTrack.Of(ScoreId.Body), 1));

            Assert.Single(world.Broadcast.ToEntity); // the improvement line only
        }

        [Fact]
        public async Task A_line_never_reaches_a_non_earner()
        {
            var world = new TestWorld();
            var bystander = new EntityBuilder(world.Ecs).AsPlayer().Build();

            await world.Handler.HandleAsync(new ExperienceAwardedEvent(
                world.Player, ProgressionTrack.Of(ScoreId.Body), 12, XpSource.CombatKill));

            Assert.DoesNotContain(world.Broadcast.ToEntity, sent => sent.EntityId == bystander);
        }
    }
}
