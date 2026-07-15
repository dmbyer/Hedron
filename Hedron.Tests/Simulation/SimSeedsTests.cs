using System.Linq;
using Hedron.Core.Modules.Simulation.Systems;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>
    /// Tier 1 — golden values pin <see cref="SimSeeds.DeriveRunSeed"/>'s exact bit math so a future
    /// change is caught even though the new algorithm would still be internally self-consistent
    /// (the point of "stable across processes," not just "stable within one run").
    /// </summary>
    public sealed class SimSeedsTests
    {
        [Theory]
        [InlineData(42, 0, 1762477673)]
        [InlineData(42, 1, 537916182)]
        [InlineData(42, 2, -963897319)]
        [InlineData(1234, 0, -1255925355)]
        [InlineData(1234, 5, 386889412)]
        public void DeriveRunSeed_matches_golden_value(int scenarioSeed, int runIndex, int expected)
        {
            Assert.Equal(expected, SimSeeds.DeriveRunSeed(scenarioSeed, runIndex));
        }

        [Fact]
        public void DeriveRunSeed_is_stable_across_repeated_calls()
        {
            var first = SimSeeds.DeriveRunSeed(777, 3);
            var second = SimSeeds.DeriveRunSeed(777, 3);
            Assert.Equal(first, second);
        }

        [Fact]
        public void DeriveRunSeed_distinct_run_indexes_yield_distinct_seeds()
        {
            var seeds = Enumerable.Range(0, 200).Select(i => SimSeeds.DeriveRunSeed(2026, i)).ToList();
            Assert.Equal(seeds.Count, seeds.Distinct().Count());
        }
    }
}
