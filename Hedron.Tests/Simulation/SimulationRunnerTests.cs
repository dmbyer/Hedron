using System.Linq;
using Hedron.Core.Modules.Simulation;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>Tier 3 — <see cref="Core.Modules.Simulation.Systems.SimulationRunner"/> determinism (Postconditions 3, 5).</summary>
    public sealed class SimulationRunnerTests
    {
        [Fact]
        public void Run_SameScenarioAndSeed_ProducesIdenticalReport()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "equal-cell-repeat", seed: 42, iterations: 50, maxTicksPerRun: 100, tierA: 2, bandA: 2, tierB: 2, bandB: 2);

            var reportA = runner.Run(scenario);
            var reportB = runner.Run(scenario);

            AssertReportsEquivalent(reportA, reportB);
        }

        [Fact]
        public void Run_Parallelism1VsN_ProducesIdenticalReport()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "equal-cell-parallelism", seed: 99, iterations: 64, maxTicksPerRun: 100, tierA: 2, bandA: 2, tierB: 2, bandB: 2);

            var sequential = runner.Run(scenario, maxParallelism: 1);
            var parallel = runner.Run(scenario, maxParallelism: 8);

            AssertReportsEquivalent(sequential, parallel);
        }

        [Fact]
        public void Run_ReportCarriesSchemaVersion1()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "schema-probe", seed: 7, iterations: 10, maxTicksPerRun: 100, tierA: 2, bandA: 2, tierB: 2, bandB: 2);

            var report = runner.Run(scenario);

            Assert.Equal(1, report.SchemaVersion);
            Assert.Equal(10, report.SideAWins + report.SideBWins + report.Draws);
        }

        /// <summary>
        /// Deep-equality helper: <see cref="SimulationReport"/>'s collection-typed fields
        /// (<c>Scenario.Sides</c>, <c>Verdicts</c>) are lists/arrays, which the record-generated
        /// <c>Equals</c> compares by reference, not by value — so a plain <c>Assert.Equal</c> on
        /// two independently-produced reports fails even when every value matches. Compare the
        /// scalar/<see cref="DistributionStats"/> fields directly and the verdicts by content.
        /// </summary>
        private static void AssertReportsEquivalent(SimulationReport a, SimulationReport b)
        {
            Assert.Equal(a.SchemaVersion, b.SchemaVersion);
            Assert.Equal(a.SideAWins, b.SideAWins);
            Assert.Equal(a.SideBWins, b.SideBWins);
            Assert.Equal(a.Draws, b.Draws);
            Assert.Equal(a.SideAWinRate, b.SideAWinRate);
            Assert.Equal(a.SideBWinRate, b.SideBWinRate);
            Assert.Equal(a.TicksToKill, b.TicksToKill);
            Assert.Equal(a.SideADamageDealt, b.SideADamageDealt);
            Assert.Equal(a.SideBDamageDealt, b.SideBDamageDealt);
            Assert.Equal(
                a.Verdicts.Select(v => (v.Name, v.Passed, v.Reason)),
                b.Verdicts.Select(v => (v.Name, v.Passed, v.Reason)));
        }
    }
}
