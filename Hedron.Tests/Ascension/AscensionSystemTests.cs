using Hedron.Core.ECS;
using Hedron.Core.Modules.Ascension;
using Hedron.Core.Modules.Ascension.Components;
using Hedron.Core.Modules.Ascension.Systems;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Components;
using Hedron.Core.Modules.Stats;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Ascension
{
    /// <summary>
    /// Tier 1 — system unit tests for <see cref="AscensionSystem"/>.
    ///
    /// Coverage contract: Postconditions from docs/implementation-plans/ascension.md (WP-1).
    /// </summary>
    public sealed class AscensionSystemTests
    {
        private static (AscensionSystem System, EntityService Ecs) CreateSystem()
        {
            var ecs = new EntityService();
            return (new AscensionSystem(ecs), ecs);
        }

        private static uint CreateEntity(EntityService ecs)
            => new EntityBuilder(ecs).AsPlayer().Build();

        [Fact]
        public void GetTier_returns_zero_for_entity_with_no_component_and_creates_nothing()
        {
            var (system, ecs) = CreateSystem();
            var entity = CreateEntity(ecs);

            Assert.Equal(0, system.GetTier(entity));
            Assert.False(ecs.HasComponent<AscensionComponent>(entity));
        }

        [Fact]
        public void TryAscend_from_zero_to_one_sets_tier_and_returns_result()
        {
            var (system, ecs) = CreateSystem();
            var entity = CreateEntity(ecs);

            var result = system.TryAscend(entity);

            Assert.True(result.Success);
            Assert.Equal(0, result.PreviousTier);
            Assert.Equal(1, result.NewTier);
            Assert.Equal(1, system.GetTier(entity));
            Assert.True(ecs.HasComponent<AscensionComponent>(entity));
        }

        [Fact]
        public void TryAscend_at_max_tier_is_a_no_op()
        {
            var (system, ecs) = CreateSystem();
            var entity = CreateEntity(ecs);
            for (var i = 0; i < AscensionConstants.MaxTier; i++)
                system.TryAscend(entity);

            Assert.Equal(AscensionConstants.MaxTier, system.GetTier(entity));

            var result = system.TryAscend(entity);

            Assert.False(result.Success);
            Assert.Equal(AscendIneligibleReason.AtMaxTier, result.FailureReason);
            Assert.Equal(AscensionConstants.MaxTier, system.GetTier(entity));
        }

        [Fact]
        public void CanAscend_returns_AtMaxTier_at_max_and_Eligible_otherwise()
        {
            var (system, ecs) = CreateSystem();
            var entity = CreateEntity(ecs);

            Assert.True(system.CanAscend(entity).Eligible);

            for (var i = 0; i < AscensionConstants.MaxTier; i++)
                system.TryAscend(entity);

            var eligibility = system.CanAscend(entity);
            Assert.False(eligibility.Eligible);
            Assert.Equal(AscendIneligibleReason.AtMaxTier, eligibility.Reason);
        }

        [Fact]
        public void GetGrantedUnlocks_is_empty_and_stable_with_the_current_empty_unlock_table()
        {
            var (system, ecs) = CreateSystem();
            var entity = CreateEntity(ecs);

            system.TryAscend(entity);
            system.TryAscend(entity);

            Assert.Empty(system.GetGrantedUnlocks(entity));
        }

        [Fact]
        public void TryAscend_does_not_touch_ProgressionComponent()
        {
            var (system, ecs) = CreateSystem();
            var entity = CreateEntity(ecs);
            ecs.AddComponent(entity, new ProgressionComponent
            {
                Xp = { [ScoreId.Body] = 42 },
                Improvements = { [ScoreId.Body] = 3 },
            });

            system.TryAscend(entity);

            var progression = ecs.Get<ProgressionComponent>(entity);
            Assert.Equal(42, progression.Xp[ScoreId.Body]);
            Assert.Equal(3, progression.Improvements[ScoreId.Body]);
        }
    }
}
