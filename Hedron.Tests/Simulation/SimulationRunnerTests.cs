using System;
using System.Linq;
using System.Threading;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Stats;
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

        [Fact]
        public void Run_PreCanceledToken_ThrowsWithoutCompletingBatch()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "cancel-pre", seed: 1, iterations: 50, maxTicksPerRun: 100, tierA: 2, bandA: 2, tierB: 2, bandB: 2);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAny<OperationCanceledException>(
                () => runner.Run(scenario, cancellationToken: cts.Token));
        }

        [Fact]
        public void Run_OnRunCompleted_FiresExactlyIterationsTimes()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "progress-count", seed: 3, iterations: 37, maxTicksPerRun: 100, tierA: 2, bandA: 2, tierB: 2, bandB: 2);
            var completed = 0;

            runner.Run(scenario, onRunCompleted: () => Interlocked.Increment(ref completed));

            Assert.Equal(37, completed);
        }

        [Fact]
        public void Run_WithAndWithoutCallback_ProducesEquivalentReports_DeterminismUnperturbed()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "callback-determinism", seed: 11, iterations: 40, maxTicksPerRun: 100, tierA: 2, bandA: 2, tierB: 2, bandB: 2);

            var withoutCallback = runner.Run(scenario);
            var withCallback = runner.Run(scenario, onRunCompleted: () => { });

            AssertReportsEquivalent(withoutCallback, withCallback);
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

        // ── ProgressionRate (sim-4, Postconditions 5, 6) ─────────────────────────

        private static void AssertProgressionReportsEquivalent(SimulationReport a, SimulationReport b)
        {
            var pa = a.ProgressionRate!;
            var pb = b.ProgressionRate!;
            Assert.Equal(pa.TargetTrack, pb.TargetTrack);
            Assert.Equal(pa.TargetImprovements, pb.TargetImprovements);
            Assert.Equal(pa.RunsReachedTarget, pb.RunsReachedTarget);
            Assert.Equal(pa.KillsToTarget, pb.KillsToTarget);
            Assert.Equal(pa.MeanMilestoneKills, pb.MeanMilestoneKills);
            Assert.Equal(
                pa.Tracks.Select(t => (t.Track, t.Xp, t.Improvements)),
                pb.Tracks.Select(t => (t.Track, t.Xp, t.Improvements)));
            Assert.Equal(pa.TicksPerKill, pb.TicksPerKill);
            Assert.Equal(pa.TicksToTarget, pb.TicksToTarget);
        }

        [Fact]
        public void Run_ProgressionRate_SameScenarioAndSeed_ProducesIdenticalReport()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "progression-repeat", seed: 42, iterations: 20, maxKillsPerRun: 200,
                targetTrack: ScoreId.Body, targetImprovements: 2,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2);

            var reportA = runner.Run(scenario);
            var reportB = runner.Run(scenario);

            Assert.NotNull(reportA.ProgressionRate);
            Assert.NotNull(reportB.ProgressionRate);
            AssertProgressionReportsEquivalent(reportA, reportB);
        }

        [Fact]
        public void Run_ProgressionRate_Parallelism1VsN_ProducesIdenticalReport()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "progression-parallelism", seed: 99, iterations: 24, maxKillsPerRun: 200,
                targetTrack: ScoreId.Body, targetImprovements: 2,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2);

            var sequential = runner.Run(scenario, maxParallelism: 1);
            var parallel = runner.Run(scenario, maxParallelism: 8);

            AssertProgressionReportsEquivalent(sequential, parallel);
        }

        [Fact]
        public void Run_ProgressionRate_CombatScalarsAtEmptyDefaults_PayloadPopulated()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "progression-payload", seed: 7, iterations: 10, maxKillsPerRun: 200,
                targetTrack: ScoreId.Body, targetImprovements: 2,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2);

            var report = runner.Run(scenario);

            Assert.Equal(1, report.SchemaVersion);
            Assert.Equal(0, report.SideAWins);
            Assert.Equal(0, report.SideBWins);
            Assert.Equal(0, report.Draws);
            Assert.Equal(new DistributionStats(0, 0, 0, 0, 0, 0), report.TicksToKill);
            Assert.Equal(new DistributionStats(0, 0, 0, 0, 0, 0), report.SideADamageDealt);
            Assert.Equal(new DistributionStats(0, 0, 0, 0, 0, 0), report.SideBDamageDealt);

            Assert.NotNull(report.ProgressionRate);
            Assert.Equal(ScoreId.Body, report.ProgressionRate!.TargetTrack);
            Assert.Equal(2, report.ProgressionRate.TargetImprovements);
            Assert.Equal(2, report.ProgressionRate.Tracks.Count);
            Assert.Equal(2, report.ProgressionRate.MeanMilestoneKills.Count);
            Assert.Equal(2, report.Verdicts.Count);
            Assert.Contains(report.Verdicts, v => v.Name == "targetReached");
            Assert.Contains(report.Verdicts, v => v.Name == "progressionRateExpectation" && v.Passed == null);
        }

        [Fact]
        public void Run_ProgressionRate_ExtendedRunSignature_MatchesBareCallAndFiresCallbackPerIteration()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "progression-extended-signature", seed: 11, iterations: 16, maxKillsPerRun: 200,
                targetTrack: ScoreId.Body, targetImprovements: 2,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2);
            var completed = 0;

            var bare = runner.Run(scenario);
            var extended = runner.Run(scenario, cancellationToken: CancellationToken.None, onRunCompleted: () => Interlocked.Increment(ref completed));

            Assert.Equal(16, completed);
            AssertProgressionReportsEquivalent(bare, extended);
        }

        [Fact]
        public void Run_Combat_ProgressionRateFieldStaysNull()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ReferenceBuildScenario(
                "combat-regression-null-progression", seed: 1, iterations: 5, maxTicksPerRun: 50, tierA: 2, bandA: 2, tierB: 2, bandB: 2);

            var report = runner.Run(scenario);

            Assert.Null(report.ProgressionRate);
        }

        [Fact]
        public void Run_ProgressionRate_WithTicksPerKill_TicksToTarget_TracksKillsToTargetLinearly()
        {
            const double ticksPerKill = 12.4;
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "progression-ticks-per-kill", seed: 5, iterations: 30, maxKillsPerRun: 200,
                targetTrack: ScoreId.Body, targetImprovements: 1,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2, ticksPerKill: ticksPerKill);

            var report = runner.Run(scenario);

            var progression = report.ProgressionRate!;
            Assert.Equal(ticksPerKill, progression.TicksPerKill);
            Assert.NotNull(progression.TicksToTarget);
            // Math.Round is order-preserving for a positive multiplier, so the transformed
            // distribution's min/max must equal the transform applied to the source min/max.
            Assert.Equal(
                (int)Math.Round(progression.KillsToTarget.Min * ticksPerKill, MidpointRounding.AwayFromZero),
                progression.TicksToTarget!.Min);
            Assert.Equal(
                (int)Math.Round(progression.KillsToTarget.Max * ticksPerKill, MidpointRounding.AwayFromZero),
                progression.TicksToTarget.Max);
        }

        [Fact]
        public void Run_ProgressionRate_WithoutTicksPerKill_TicksToTargetIsNull()
        {
            var runner = SimulationTestFixtures.NewRunner(new FakeClock());
            var scenario = SimulationTestFixtures.ProgressionScenario(
                "progression-no-ticks-per-kill", seed: 5, iterations: 10, maxKillsPerRun: 200,
                targetTrack: ScoreId.Body, targetImprovements: 1,
                subjectTier: 2, subjectBand: 2, victimTier: 2, victimBand: 2);

            var report = runner.Run(scenario);

            Assert.Null(report.ProgressionRate!.TicksPerKill);
            Assert.Null(report.ProgressionRate.TicksToTarget);
        }
    }
}
