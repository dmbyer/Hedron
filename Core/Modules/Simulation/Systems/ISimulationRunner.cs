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
        /// </summary>
        SimulationReport Run(ScenarioDefinition scenario, int? maxParallelism = null);
    }
}
