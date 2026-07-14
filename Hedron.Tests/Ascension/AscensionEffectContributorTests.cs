using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Ascension;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Ascension
{
    /// <summary>
    /// Tier 1 — contributor-fold tests for <see cref="AscensionEffectContributor"/>.
    /// Asserts the INV-24 "pulled on read, never materialized" postcondition and that the
    /// baseline is additive on top of a progression improvement.
    /// </summary>
    public sealed class AscensionEffectContributorTests
    {
        private sealed class TestWorld
        {
            public EntityService Ecs { get; }
            public IAscensionSystem Ascension { get; }
            public IProgressionSystem Progression { get; }
            public IStatSystem Stats { get; }

            public TestWorld(FakeRandom rng)
            {
                Ecs = new EntityService();
                Ascension = new AscensionSystem(Ecs);
                Progression = new ProgressionSystem(Ecs, rng, new PowerBudgetSystem(PowerBudgetTunables.Default));

                var ascensionContributor = new AscensionEffectContributor(Ascension);
                var progressionContributor = new ProgressionEffectContributor(Progression);
                var effects = new EffectSystem(Ecs, new IEffectContributor[] { ascensionContributor, progressionContributor });
                var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
                var attributes = new AttributeSystem(Ecs, effects, deathOpts);
                Stats = new StatSystem(attributes, effects);
            }
        }

        [Fact]
        public void GetModifiers_is_zero_at_tier_zero()
        {
            var world = new TestWorld(new FakeRandom(seed: 1));
            var entity = new EntityBuilder(world.Ecs).AsPlayer().WithAttributes(body: 10).Build();

            Assert.Equal(10, world.Stats.Get(entity, ScoreId.Body));
        }

        [Fact]
        public void GetModifiers_equals_TierBaselineStep_times_tier()
        {
            var world = new TestWorld(new FakeRandom(seed: 1));
            var entity = new EntityBuilder(world.Ecs).AsPlayer().WithAttributes(body: 10).Build();

            world.Ascension.TryAscend(entity);

            Assert.Equal(10 + AscensionConstants.TierBaselineStep, world.Stats.Get(entity, ScoreId.Body));
        }

        [Fact]
        public void Ascend_never_materializes_an_EffectsComponent()
        {
            var world = new TestWorld(new FakeRandom(seed: 1));
            var entity = new EntityBuilder(world.Ecs).AsPlayer().WithAttributes(body: 10).Build();

            world.Ascension.TryAscend(entity);
            _ = world.Stats.Get(entity, ScoreId.Body);

            Assert.False(world.Ecs.HasComponent<EffectsComponent>(entity),
                "The tier baseline must be pulled on read (INV-24) — never written to EffectsComponent.");
        }

        [Fact]
        public void Baseline_is_additive_on_top_of_a_progression_improvement()
        {
            var world = new TestWorld(new FakeRandom(seed: 1));
            var entity = new EntityBuilder(world.Ecs).AsPlayer().WithAttributes(body: 10).Build();

            world.Progression.AwardExperience(entity, ScoreId.Body, ProgressionConstants.ThresholdBase, XpSource.CombatKill);
            world.Ascension.TryAscend(entity);

            var expected = 10 + ProgressionConstants.PowerPerImprovement + AscensionConstants.TierBaselineStep;
            Assert.Equal(expected, world.Stats.Get(entity, ScoreId.Body));
        }
    }
}
