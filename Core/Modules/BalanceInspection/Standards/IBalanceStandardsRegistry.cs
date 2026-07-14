using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Standards
{
    /// <summary>
    /// The loaded, dense-filled balance-standards table — the criteria seam every consumer
    /// (oracle-tunables composition, <see cref="Systems.IBalanceAuditSystem"/>, <c>powerband</c>,
    /// both editors' mismatch flags, and the sim-2 engine) reads. Every (Tier, Band) cell in
    /// <c>[0, Tunables.MaxTier] × [1, Tunables.BandsPerTier]</c> resolves — sparse-authored cells
    /// fill with an empty-gear reference build and the global outcome tolerances (seed OQ4).
    /// </summary>
    public interface IBalanceStandardsRegistry : IRegistry<PowerBand, BalanceStandard>
    {
        /// <summary>The oracle tunables this document carries (composed into <see cref="PowerBudgetSystem"/>).</summary>
        PowerBudgetTunables Tunables { get; }

        /// <summary>The band-drift audit tolerance (seed OQ7) — replaces the former compiled constant.</summary>
        int BandDriftTolerance { get; }

        /// <summary>
        /// The expected-outcome tolerances for a (Tier, Band) cell: the cell's
        /// <see cref="BalanceStandard.OutcomesOverride"/> if authored, else the document's global
        /// <see cref="BalanceStandardsDocument.Outcomes"/>.
        /// </summary>
        OutcomeTolerances OutcomesFor(int tier, int band);

        /// <summary>
        /// The reference build's score snapshot at a (Tier, Band) cell:
        /// <c>Tunables.ReferenceBaseScores + cell.ReferenceBuild.GearBonuses</c>. The tier baseline
        /// enters via <see cref="IPowerBudgetSystem.Estimate"/>'s tier argument, exactly as live
        /// snapshots do — this snapshot carries no tier contribution itself.
        /// </summary>
        PowerSnapshot ReferenceSnapshot(int tier, int band);
    }
}
