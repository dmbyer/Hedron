using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Ascension.Components;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>Tier 1 — <see cref="SandboxWorldFactory"/> isolation and graph wiring (Postcondition 4).</summary>
    public sealed class SandboxWorldFactoryTests
    {
        private static SandboxWorldFactory NewFactory() => new(
            new AbilityRegistry(),
            new EffectRegistry(),
            new PowerBudgetSystem(PowerBudgetTunables.Default),
            Options.Create(new DeathOptions()));

        [Fact]
        public void Create_TwoWorlds_AreDisjoint()
        {
            var factory = NewFactory();
            var worldA = factory.Create(new FakeRandom(1));
            var worldB = factory.Create(new FakeRandom(1));

            var entity = worldA.EntityService.CreateEntity();
            worldA.EntityService.AddComponent(entity.Id, new MobDataComponent { Name = "probe" });

            Assert.True(worldA.EntityService.HasComponent<MobDataComponent>(entity.Id));
            Assert.False(worldB.EntityService.HasComponent<MobDataComponent>(entity.Id));
        }

        [Fact]
        public void Create_GraphWiring_TierBaselineFoldsIntoStatSystem()
        {
            var factory = NewFactory();
            var world = factory.Create(new FakeRandom(1));

            var entity = world.EntityService.CreateEntity();
            world.EntityService.AddComponent(entity.Id, new AttributesComponent { Body = 10, Mind = 10, Spirit = 10, Attunement = 10, Level = 1 });
            world.EntityService.AddComponent(entity.Id, new PoolsComponent { MaxHp = 100, CurrentHp = 100 });
            world.EntityService.AddComponent(entity.Id, new AscensionComponent { Tier = 1 });

            // PowerBudgetTunables.Default: TierBaselineStep = 10, TrackedScores = [Body, HpMax].
            Assert.Equal(20, world.Stats.Get(entity.Id, ScoreId.Body));
            Assert.Equal(110, world.Stats.Get(entity.Id, ScoreId.HpMax));
        }

        [Fact]
        public void Create_ArenaRoom_IsAValidEntity()
        {
            var factory = NewFactory();
            var world = factory.Create(new FakeRandom(1));

            var entity = world.EntityService.CreateEntity();
            world.EntityService.AddComponent(entity.Id, new LocationComponent { RoomEntityId = world.ArenaRoomEntityId });

            Assert.Equal(world.ArenaRoomEntityId, world.EntityService.Get<LocationComponent>(entity.Id).RoomEntityId);
        }
    }
}
