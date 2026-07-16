using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Tests.Harness;
using Hedron.Web.Services;
using Xunit;

namespace Hedron.Tests.Web
{
    /// <summary>
    /// Tier 1 — <see cref="SimulationRunService"/> against faked engine seams (sim-3 Test 5,
    /// Postconditions 2&#8211;5). The service owns the queue/state-machine logic; the real engine
    /// is exercised separately by the Simulation-tier tests.
    /// </summary>
    public sealed class SimulationRunServiceTests
    {
        private sealed class FakeRunner : ISimulationRunner
        {
            private readonly Func<ScenarioDefinition, CancellationToken, Action?, SimulationReport> _behavior;

            public FakeRunner(Func<ScenarioDefinition, CancellationToken, Action?, SimulationReport> behavior) =>
                _behavior = behavior;

            public SimulationReport Run(
                ScenarioDefinition scenario, int? maxParallelism = null,
                CancellationToken cancellationToken = default, Action? onRunCompleted = null) =>
                _behavior(scenario, cancellationToken, onRunCompleted);
        }

        private sealed class GatedRunner : ISimulationRunner
        {
            public readonly ManualResetEventSlim Gate = new(initialState: false);
            public int InvokeCount;

            public SimulationReport Run(
                ScenarioDefinition scenario, int? maxParallelism = null,
                CancellationToken cancellationToken = default, Action? onRunCompleted = null)
            {
                Interlocked.Increment(ref InvokeCount);
                Gate.Wait(cancellationToken);
                for (var i = 0; i < scenario.Iterations; i++)
                    onRunCompleted?.Invoke();
                return SampleReport(scenario);
            }
        }

        private sealed class FakeScenarioStore : ISimScenarioStore
        {
            public ScenarioDefinition Load(string path, int? seedOverride = null) => throw new NotImplementedException();

            public void Validate(ScenarioDefinition scenario)
            {
                if (scenario.Iterations <= 0)
                    throw new InvalidOperationException("iterations must be > 0.");
            }

            public Task<string> SaveAsync(ScenarioDefinition scenario, CancellationToken ct = default) => throw new NotImplementedException();
            public System.Collections.Generic.IReadOnlyList<ScenarioFileSummary> List() => throw new NotImplementedException();
        }

        private sealed class FakeReportWriter : ISimReportWriter
        {
            public int WriteCount;

            public Task<string> WriteAsync(SimulationReport report, CancellationToken ct = default)
            {
                Interlocked.Increment(ref WriteCount);
                return Task.FromResult($"fake/{report.Scenario.Name}.json");
            }
        }

        private static ScenarioDefinition ValidScenario(string name = "probe", int iterations = 5) => new(
            ScenarioKind.Combat, name, Seed: 1, Iterations: iterations, MaxTicksPerRun: 10,
            Sides: new[]
            {
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, "cooldown-first", Tier: 1, Band: 1) }),
                new ScenarioSide(new[] { new CombatantSpec(CombatantSourceKind.ReferenceBuild, "cooldown-first", Tier: 1, Band: 1) }),
            });

        private static SimulationReport SampleReport(ScenarioDefinition scenario) => new(
            SchemaVersion: 1, Scenario: scenario, GeneratedAt: new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc),
            SideAWins: 1, SideBWins: 0, Draws: 0, SideAWinRate: 1, SideBWinRate: 0,
            TicksToKill: new DistributionStats(1, 1, 1, 1, 1, 1),
            SideADamageDealt: new DistributionStats(1, 1, 1, 1, 1, 1),
            SideBDamageDealt: new DistributionStats(1, 1, 1, 1, 1, 1),
            Verdicts: Array.Empty<SimVerdict>());

        private static SimulationRunService NewService(
            ISimulationRunner? runner = null, ISimScenarioStore? store = null, ISimReportWriter? writer = null) =>
            new(
                runner ?? new FakeRunner((s, ct, cb) => { for (var i = 0; i < s.Iterations; i++) cb?.Invoke(); return SampleReport(s); }),
                store ?? new FakeScenarioStore(),
                writer ?? new FakeReportWriter(),
                new FakeClock());

        private static SimRunStatus StatusOf(SimulationRunService service, Guid id) =>
            service.Snapshot().Single(s => s.Id == id);

        private static void WaitUntil(Func<bool> condition, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException("condition not met within timeout.");
                Thread.Sleep(10);
            }
        }

        [Fact]
        public void Enqueue_InvalidScenario_ThrowsAndNothingQueued()
        {
            var service = NewService();
            var invalid = ValidScenario() with { Iterations = 0 };

            Assert.Throws<InvalidOperationException>(() => service.Enqueue(invalid));
            Assert.Empty(service.Snapshot());
        }

        [Fact]
        public void Enqueue_ValidScenario_TransitionsQueuedRunningCompletedWithReportPath()
        {
            var writer = new FakeReportWriter();
            var service = NewService(writer: writer);

            var id = service.Enqueue(ValidScenario());

            WaitUntil(() => StatusOf(service, id).State == SimRunState.Completed);

            var status = StatusOf(service, id);
            Assert.NotNull(status.ReportPath);
            Assert.Equal(1, writer.WriteCount);
            Assert.Equal(status.TotalRuns, status.CompletedRuns);
        }

        [Fact]
        public void RunnerException_MarksFailedWithMessage_NoWriterCall()
        {
            var writer = new FakeReportWriter();
            var runner = new FakeRunner((s, ct, cb) => throw new InvalidOperationException("engine exploded"));
            var service = NewService(runner: runner, writer: writer);

            var id = service.Enqueue(ValidScenario());

            WaitUntil(() => StatusOf(service, id).State == SimRunState.Failed);

            Assert.Equal("engine exploded", StatusOf(service, id).ErrorMessage);
            Assert.Equal(0, writer.WriteCount);
        }

        [Fact]
        public void Cancel_WhileQueued_MarksCanceled_RunnerNeverCalledForThatRun()
        {
            var runner = new GatedRunner();
            var service = NewService(runner: runner);

            var firstId = service.Enqueue(ValidScenario("first"));
            WaitUntil(() => StatusOf(service, firstId).State == SimRunState.Running);

            var secondId = service.Enqueue(ValidScenario("second"));
            Assert.Equal(SimRunState.Queued, StatusOf(service, secondId).State);

            service.Cancel(secondId);
            Assert.Equal(SimRunState.Canceled, StatusOf(service, secondId).State);
            Assert.Equal(1, runner.InvokeCount);

            runner.Gate.Set();
            WaitUntil(() => StatusOf(service, firstId).State == SimRunState.Completed);
            Assert.Equal(1, runner.InvokeCount); // second never dequeued into the runner
        }

        [Fact]
        public void Cancel_WhileActive_MarksCanceled_NoWriterCall()
        {
            var runner = new GatedRunner();
            var writer = new FakeReportWriter();
            var service = NewService(runner: runner, writer: writer);

            var id = service.Enqueue(ValidScenario());
            WaitUntil(() => StatusOf(service, id).State == SimRunState.Running);

            service.Cancel(id);

            WaitUntil(() => StatusOf(service, id).State == SimRunState.Canceled);
            Assert.Equal(0, writer.WriteCount);
        }

        [Fact]
        public void SecondEnqueue_StaysQueued_UntilFirstTerminates_SingleFlight()
        {
            var runner = new GatedRunner();
            var service = NewService(runner: runner);

            var firstId = service.Enqueue(ValidScenario("first"));
            WaitUntil(() => StatusOf(service, firstId).State == SimRunState.Running);

            var secondId = service.Enqueue(ValidScenario("second"));
            Thread.Sleep(50);
            Assert.Equal(SimRunState.Queued, StatusOf(service, secondId).State);

            runner.Gate.Set();

            WaitUntil(() => StatusOf(service, secondId).State == SimRunState.Completed);
        }

        [Fact]
        public void ProgressCounter_ReachesTotal_OnCompletion()
        {
            var service = NewService();
            var id = service.Enqueue(ValidScenario(iterations: 17));

            WaitUntil(() => StatusOf(service, id).State == SimRunState.Completed);

            var status = StatusOf(service, id);
            Assert.Equal(17, status.TotalRuns);
            Assert.Equal(17, status.CompletedRuns);
        }
    }
}
