using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Systems;

namespace Hedron.Web.Services
{
    /// <summary>State of one enqueued run in <see cref="SimulationRunService"/>'s registry.</summary>
    public enum SimRunState
    {
        Queued,
        Running,
        Completed,
        Failed,
        Canceled,
    }

    /// <summary>
    /// A read-only snapshot of one run's status — what <see cref="SimulationRunService.Snapshot"/>
    /// returns for polling pages to render. Never carries the live <see cref="CancellationTokenSource"/>
    /// or scenario mutable state (INV-26-consistent: presentation-only projection).
    /// </summary>
    public sealed record SimRunStatus(
        Guid Id,
        string ScenarioName,
        SimRunState State,
        int CompletedRuns,
        int TotalRuns,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        string? ReportPath,
        IReadOnlyList<SimVerdict>? Verdicts,
        string? ErrorMessage);

    /// <summary>
    /// Singleton FIFO background-run registry (sim-3 seed OQ5 resolution) — the one place
    /// <c>Hedron.Web</c> owns run lifecycle for the offline simulation engine. Enqueues validate
    /// immediately (nothing queued on a structural violation); a single background drain loop runs
    /// one batch at a time (the engine already saturates cores per batch — concurrent batches would
    /// oversubscribe and confound wall-clock comparisons); pages poll <see cref="Snapshot"/> on a
    /// timer. No bus events, no hosted service (INV-5/INV-10 — this is a web host UI concern, not a
    /// live-world fact).
    /// </summary>
    public sealed class SimulationRunService
    {
        private const int RetentionLimit = 50;

        private readonly ISimulationRunner _runner;
        private readonly ISimScenarioStore _store;
        private readonly ISimReportWriter _writer;
        private readonly IClock _clock;

        private readonly object _lock = new();
        private readonly List<Guid> _order = new();
        private readonly Dictionary<Guid, RunEntry> _runs = new();
        private readonly Queue<Guid> _pending = new();
        private bool _draining;

        public SimulationRunService(
            ISimulationRunner runner, ISimScenarioStore store, ISimReportWriter writer, IClock clock)
        {
            _runner = runner;
            _store = store;
            _writer = writer;
            _clock = clock;
        }

        /// <summary>
        /// Validates <paramref name="scenario"/> (throwing on any structural violation — nothing is
        /// queued) and enqueues it FIFO. Returns the new run's id.
        /// </summary>
        public Guid Enqueue(ScenarioDefinition scenario)
        {
            _store.Validate(scenario);

            var entry = new RunEntry(scenario);
            lock (_lock)
            {
                _runs[entry.Id] = entry;
                _order.Add(entry.Id);
                _pending.Enqueue(entry.Id);
                TrimRetention();
            }

            EnsureDraining();
            return entry.Id;
        }

        /// <summary>Every tracked run's current status, most recently enqueued first.</summary>
        public IReadOnlyList<SimRunStatus> Snapshot()
        {
            lock (_lock)
            {
                return _order.Select(id => ToStatus(_runs[id])).Reverse().ToList();
            }
        }

        /// <summary>
        /// A queued run cancels in place (the runner is never invoked); an active run's cooperative
        /// token is signaled — the batch stops between per-iteration runs and writes no report.
        /// No-op for an unknown id or a run already in a terminal state.
        /// </summary>
        public void Cancel(Guid id)
        {
            lock (_lock)
            {
                if (!_runs.TryGetValue(id, out var entry))
                    return;

                if (entry.State == SimRunState.Queued)
                {
                    entry.State = SimRunState.Canceled;
                    entry.FinishedAt = _clock.UtcNow;
                    return;
                }

                if (entry.State == SimRunState.Running)
                    entry.Cts.Cancel();
            }
        }

        private void EnsureDraining()
        {
            lock (_lock)
            {
                if (_draining)
                    return;
                _draining = true;
            }

            _ = Task.Run(DrainLoopAsync);
        }

        private async Task DrainLoopAsync()
        {
            while (true)
            {
                RunEntry? entry = null;
                lock (_lock)
                {
                    while (_pending.Count > 0)
                    {
                        var candidateId = _pending.Dequeue();
                        if (_runs.TryGetValue(candidateId, out var candidate) && candidate.State == SimRunState.Queued)
                        {
                            entry = candidate;
                            break;
                        }
                        // Already canceled while queued (or evicted by retention) — skip, runner never called.
                    }

                    if (entry is null)
                    {
                        _draining = false;
                        return;
                    }

                    entry.State = SimRunState.Running;
                    entry.StartedAt = _clock.UtcNow;
                }

                await RunOneAsync(entry).ConfigureAwait(false);
            }
        }

        private async Task RunOneAsync(RunEntry entry)
        {
            try
            {
                var report = await Task.Run(() => _runner.Run(
                    entry.Scenario,
                    maxParallelism: null,
                    cancellationToken: entry.Cts.Token,
                    onRunCompleted: () => Interlocked.Increment(ref entry.Completed))).ConfigureAwait(false);

                // Known, accepted race: a Cancel() landing in the narrow window between the last
                // iteration completing and this write finishing surfaces as Canceled rather than
                // Completed (the write below still throws OperationCanceledException on that token).
                // No report is left half-written either way — WriteAsync's own tmp->rename is atomic.
                var reportPath = await _writer.WriteAsync(report, entry.Cts.Token).ConfigureAwait(false);

                lock (_lock)
                {
                    entry.State = SimRunState.Completed;
                    entry.FinishedAt = _clock.UtcNow;
                    entry.ReportPath = reportPath;
                    entry.Verdicts = report.Verdicts;
                }
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    entry.State = SimRunState.Canceled;
                    entry.FinishedAt = _clock.UtcNow;
                }
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    entry.State = SimRunState.Failed;
                    entry.FinishedAt = _clock.UtcNow;
                    entry.ErrorMessage = ex.Message;
                }
            }
        }

        // Retention is bounded by evicting the oldest *terminal* entries — Queued/Running runs are
        // never evicted regardless of count (durable history lives in the report directory; this
        // registry is a bounded recent-activity window only).
        private void TrimRetention()
        {
            while (_order.Count > RetentionLimit)
            {
                var oldestTerminalIndex = _order.FindIndex(id => _runs[id].State is
                    SimRunState.Completed or SimRunState.Failed or SimRunState.Canceled);
                if (oldestTerminalIndex < 0)
                    break;

                var id = _order[oldestTerminalIndex];
                _order.RemoveAt(oldestTerminalIndex);
                _runs.Remove(id);
            }
        }

        private static SimRunStatus ToStatus(RunEntry entry) => new(
            entry.Id,
            entry.Scenario.Name,
            entry.State,
            entry.Completed,
            entry.Scenario.Iterations,
            entry.StartedAt,
            entry.FinishedAt,
            entry.ReportPath,
            entry.Verdicts,
            entry.ErrorMessage);

        private sealed class RunEntry
        {
            public RunEntry(ScenarioDefinition scenario)
            {
                Id = Guid.NewGuid();
                Scenario = scenario;
                State = SimRunState.Queued;
            }

            public Guid Id { get; }
            public ScenarioDefinition Scenario { get; }
            public SimRunState State;
            public int Completed;
            public DateTime? StartedAt;
            public DateTime? FinishedAt;
            public string? ReportPath;
            public IReadOnlyList<SimVerdict>? Verdicts;
            public string? ErrorMessage;
            public CancellationTokenSource Cts { get; } = new();
        }
    }
}
