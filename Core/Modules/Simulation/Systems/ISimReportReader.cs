using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// One row of <see cref="ISimReportReader.List"/> — enough to render a run-history list without
    /// deserializing every artifact's full body twice. <see cref="Readable"/> is <see langword="false"/>
    /// when the file could not be parsed as a <see cref="SimulationReport"/>; <see cref="Error"/>
    /// then carries the reason and every other field beyond <see cref="Path"/>/<see cref="FileName"/>
    /// is unset. A listing never throws on a bad file — it flags the row instead (Postcondition 6).
    /// </summary>
    public sealed record SimReportSummary(
        string Path,
        string FileName,
        bool Readable,
        DateTime? GeneratedAt = null,
        string? ScenarioName = null,
        string? Error = null);

    /// <summary>
    /// Read-side counterpart to <see cref="ISimReportWriter"/> — shares its
    /// <see cref="SimReportJson"/> serializer options so a report is read back exactly as it was
    /// written, whether produced by the CLI <c>simulate</c> run-mode or the sim-3 editor. Never
    /// mutates the reports directory.
    /// </summary>
    public interface ISimReportReader
    {
        /// <summary>
        /// Lists every file in <c>Simulation:ReportDirectory</c>, newest first. A file that fails to
        /// parse is included with <see cref="SimReportSummary.Readable"/> <see langword="false"/>
        /// rather than aborting the whole listing.
        /// </summary>
        IReadOnlyList<SimReportSummary> List();

        /// <summary>Reads and fully deserializes the report artifact at <paramref name="path"/>.</summary>
        SimulationReport Read(string path);
    }
}
