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
        /// May be an absolute path or relative to the working directory. Default: <c>data/sim/reports</c>.
        /// </summary>
        public string ReportDirectory { get; set; } = "data/sim/reports";
    }
}
