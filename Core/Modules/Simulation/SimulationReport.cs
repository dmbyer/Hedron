using System;
using System.Collections.Generic;

namespace Hedron.Core.Modules.Simulation
{
    /// <summary>
    /// One expected-vs-actual check computed inside the engine (INV-19 — the CLI, the sim-3 editor,
    /// and the promoted CI invariants all read the same verdict rows; the math never forks per
    /// caller). <see cref="Passed"/> is <see langword="null"/> when the check was skipped (no
    /// resolvable (Tier, Band) cell, no decisive runs, or an undefined band-index gap) —
    /// <see cref="Reason"/> always explains why.
    /// </summary>
    public sealed record SimVerdict(string Name, bool? Passed, string Reason);

    /// <summary>
    /// A completed batch run's statistical report — the artifact <see cref="Systems.ISimReportWriter"/>
    /// serializes to JSON. <see cref="SchemaVersion"/> starts at 1; additive fields never bump it,
    /// breaking shape changes do (seed OQ4a) — old reports stay readable.
    /// </summary>
    public sealed record SimulationReport(
        int SchemaVersion,
        ScenarioDefinition Scenario,
        DateTime GeneratedAt,
        int SideAWins,
        int SideBWins,
        int Draws,
        double SideAWinRate,
        double SideBWinRate,
        DistributionStats TicksToKill,
        DistributionStats SideADamageDealt,
        DistributionStats SideBDamageDealt,
        IReadOnlyList<SimVerdict> Verdicts);
}
