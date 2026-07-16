using System.Threading;
using System.Threading.Tasks;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Serializes a <see cref="SimulationReport"/> to a JSON artifact — a third durable class,
    /// deliberately outside SQLite (INV-14 is for live entity state) and world YAML. Run history is
    /// the reports directory listing, the same posture as the <c>generate</c> run-mode's output.
    /// </summary>
    public interface ISimReportWriter
    {
        /// <summary>
        /// Atomically writes <paramref name="report"/> into the configured reports directory and
        /// returns the written file's path.
        /// </summary>
        Task<string> WriteAsync(SimulationReport report, CancellationToken ct = default);
    }
}
