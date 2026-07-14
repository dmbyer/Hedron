using System;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection.Standards
{
    /// <summary>
    /// Compiled fallback standards document — the pre-slice constant values (band-drift tolerance,
    /// global outcome tolerances) plus <see cref="PowerBudgetTunables.Default"/> and an empty cell
    /// table (sparse-fill semantics mean "no authored cells" is a fully valid, dense-fillable
    /// document — seed OQ4). Used when no standards file is present (Postcondition 3).
    /// </summary>
    public static class BalanceStandardsDefaults
    {
        /// <summary>
        /// Global outcome-tolerance defaults (seed OQ3): equal-cell fights land near a 50% win
        /// rate within a 10-point tolerance; a one-band-higher attacker is expected to win at
        /// least 65% of the time. Inert until the sim engine (sim-2) consumes them.
        /// </summary>
        public static readonly OutcomeTolerances Outcomes = new(
            EqualCellWinRate: 0.5,
            WinRateTolerance: 0.1,
            HigherBandWinRateFloor: 0.65);

        /// <summary>Mirrors the pre-slice <c>BalanceAuditConstants.BandDriftTolerance</c> value.</summary>
        public const int BandDriftTolerance = 1;

        public static readonly BalanceStandardsDocument Document = new(
            Tunables: PowerBudgetTunables.Default,
            BandDriftTolerance: BandDriftTolerance,
            Outcomes: Outcomes,
            Cells: Array.Empty<BalanceStandard>());
    }
}
