using System.Linq;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Systems;
using Hedron.Web.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Web
{
    /// <summary>Tier 1 — <see cref="BaselineSweep"/> scenario composition (sim-3 Postcondition 10).</summary>
    public sealed class BaselineSweepTests
    {
        private static ISimScenarioStore NewValidatingStore() =>
            new SimScenarioStore(
                new ISimCombatantPolicy[] { new MeleeOnlyPolicy(), new RoundRobinPolicy(), new CooldownFirstPolicy(new AbilityRegistry()) },
                Options.Create(new SimulationOptions()));

        [Fact]
        public void Compose_OneEqualCellScenarioPerCell()
        {
            var tunables = PowerBudgetTunables.Default;
            var expectedCellCount = (tunables.MaxTier + 1) * tunables.BandsPerTier;

            var scenarios = BaselineSweep.Compose(tunables);

            var equalCellScenarios = scenarios.Where(s => s.Name.StartsWith("sweep.equal.")).ToList();
            Assert.Equal(expectedCellCount, equalCellScenarios.Count);
            foreach (var scenario in equalCellScenarios)
            {
                var sideA = scenario.Sides[0].Combatants[0];
                var sideB = scenario.Sides[1].Combatants[0];
                Assert.Equal(sideA.Tier, sideB.Tier);
                Assert.Equal(sideA.Band, sideB.Band);
            }
        }

        [Fact]
        public void Compose_OneAdjacentPairScenarioPerConsecutiveGlobalBandIndexPair()
        {
            var tunables = PowerBudgetTunables.Default;
            var expectedCellCount = (tunables.MaxTier + 1) * tunables.BandsPerTier;

            var scenarios = BaselineSweep.Compose(tunables);

            var adjacentScenarios = scenarios.Where(s => s.Name.StartsWith("sweep.adjacent.")).ToList();
            Assert.Equal(expectedCellCount - 1, adjacentScenarios.Count);
        }

        [Fact]
        public void Compose_EveryScenario_PassesValidation()
        {
            var store = NewValidatingStore();
            var scenarios = BaselineSweep.Compose(PowerBudgetTunables.Default);

            foreach (var scenario in scenarios)
                store.Validate(scenario);
        }

        [Fact]
        public void Compose_SeedsAndNamesAreDeterministic()
        {
            var tunables = PowerBudgetTunables.Default;

            var first = BaselineSweep.Compose(tunables);
            var second = BaselineSweep.Compose(tunables);

            Assert.Equal(first.Select(s => s.Name), second.Select(s => s.Name));
            Assert.All(first, s => Assert.Equal(BaselineSweep.Seed, s.Seed));
        }
    }
}
