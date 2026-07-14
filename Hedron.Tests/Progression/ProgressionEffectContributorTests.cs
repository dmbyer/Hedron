using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
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

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 1 — contributor-fold tests for <see cref="ProgressionEffectContributor"/>.
    /// Asserts the INV-24 "pulled on read, never materialized" postcondition: the power step
    /// is folded into <see cref="IStatSystem.Get"/> with no <see cref="EffectsComponent"/> written.
    /// </summary>
    public sealed class ProgressionEffectContributorTests
    {
        private static (IStatSystem Stats, IProgressionSystem Progression, EntityService Ecs) CreateWorld(FakeRandom rng)
        {
            var ecs = new EntityService();
            var contributor = new ProgressionEffectContributorSeam(ecs, rng, out var progression, out var stats);
            return (stats, progression, ecs);
        }

        // Wires ProgressionSystem + ProgressionEffectContributor into a real EffectSystem/StatSystem
        // so IStatSystem.Get(...) reflects the contributor fold exactly as production DI would.
        private sealed class ProgressionEffectContributorSeam
        {
            public ProgressionEffectContributorSeam(EntityService ecs, FakeRandom rng, out IProgressionSystem progression, out IStatSystem stats)
            {
                progression = new ProgressionSystem(ecs, rng, new PowerBudgetSystem(PowerBudgetTunables.Default));

                var contributor = new ProgressionEffectContributor(progression);
                var effects = new EffectSystem(ecs, new IEffectContributor[] { contributor });
                var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
                var attributes = new AttributeSystem(ecs, effects, deathOpts);
                stats = new StatSystem(attributes, effects);
            }
        }

        [Fact]
        public void GetModifiers_returns_zero_for_unimproved_track()
        {
            var (stats, _, ecs) = CreateWorld(new FakeRandom(seed: 1));
            var entity = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();

            Assert.Equal(0, stats.Get(entity, ScoreId.Body) - 10);
        }

        [Fact]
        public void GetModifiers_returns_PowerPerImprovement_times_improvement_count()
        {
            var (stats, progression, ecs) = CreateWorld(new FakeRandom(seed: 1));
            var entity = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();

            progression.AwardExperience(entity, ScoreId.Body, ProgressionConstants.ThresholdBase, XpSource.CombatKill);
            Assert.Equal(1, progression.GetImprovementCount(entity, ScoreId.Body));

            var effective = stats.Get(entity, ScoreId.Body);
            Assert.Equal(10 + ProgressionConstants.PowerPerImprovement, effective);
        }

        [Fact]
        public void Improvement_never_materializes_an_EffectsComponent()
        {
            var (stats, progression, ecs) = CreateWorld(new FakeRandom(seed: 1));
            var entity = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Build();

            progression.AwardExperience(entity, ScoreId.Body, ProgressionConstants.ThresholdBase, XpSource.CombatKill);
            _ = stats.Get(entity, ScoreId.Body);

            Assert.False(ecs.HasComponent<EffectsComponent>(entity),
                "Progression power must be pulled on read (INV-24) — never written to EffectsComponent.");
        }
    }
}
