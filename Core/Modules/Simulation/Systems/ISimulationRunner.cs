using System;
using System.Threading;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Runs a validated <see cref="ScenarioDefinition"/> end to end: kind dispatch, one-time
    /// combatant pre-resolution, parallel fan-out across isolated <see cref="SandboxWorld"/>
    /// instances, deterministic index-ordered reduce, and verdict attachment. Publishes nothing
    /// (INV-5).
    /// </summary>
    public interface ISimulationRunner
    {
        /// <summary>
        /// Runs every iteration of <paramref name="scenario"/> and returns the aggregated report.
        /// <paramref name="maxParallelism"/> defaults to <see cref="System.Environment.ProcessorCount"/>;
        /// tests pin it to 1 to prove determinism is independent of scheduling.
        /// <paramref name="cancellationToken"/> is checked cooperatively between per-iteration runs
        /// (wired to the underlying <c>Parallel.For</c>'s <c>CancellationToken</c>) — an already
        /// canceled token throws <see cref="OperationCanceledException"/> before any run executes.
        /// <paramref name="onRunCompleted"/>, when supplied, is invoked once per completed iteration
        /// from worker threads — the callback contract is thread-safe, cheap, and non-throwing; it
        /// carries no data and cannot perturb seeds, scheduling, or report content (sim-3
        /// Postcondition 11/Test 1c).
        /// </summary>
        SimulationReport Run(
            ScenarioDefinition scenario,
            int? maxParallelism = null,
            CancellationToken cancellationToken = default,
            Action? onRunCompleted = null);
    }
}
