namespace Hedron.Core.Modules.Simulation
{
    /// <summary>
    /// Settings bound from the <c>Simulation:</c> configuration section.
    /// Override via environment variable: <c>HEDRON_Simulation__ReportDirectory</c>.
    /// </summary>
    public sealed class SimulationOptions
    {
        /// <summary>
        /// Directory <see cref="Systems.ISimReportWriter"/> writes JSON report artifacts into.
        /// Absolute, or relative to the Hedron root; when unset in configuration the host derives it under <c>Paths:DataDirectory</c> (see <c>Server/DataPaths</c>) as <c>sim/reports</c>.
        /// </summary>
        public string ReportDirectory { get; set; } = "data/sim/reports";

        /// <summary>
        /// Directory <see cref="Systems.ISimScenarioStore.SaveAsync"/> writes editor-composed
        /// scenario YAML into (sim-3). Absolute, or relative to the Hedron root; when unset in configuration the host derives it under <c>Paths:DataDirectory</c> (see <c>Server/DataPaths</c>) as <c>sim/scenarios</c>.
        /// </summary>
        public string ScenarioDirectory { get; set; } = "data/sim/scenarios";
    }
}
