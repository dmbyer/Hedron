using Hedron.Core.Systems;

namespace Hedron.Core.Modules.BalanceInspection
{
    /// <summary>
    /// Shared band-drift tolerance for the soft authored-vs-computed comparison (both editors'
    /// mismatch flag and <c>IBalanceAuditSystem</c>'s bulk sweep consume the same tolerance and
    /// index math — one callable definition, INV-19). Never a build/reload/CI gate — advisory only.
    /// </summary>
    public static class BalanceAuditConstants
    {
        /// <summary>
        /// Maximum tolerated absolute difference between the authored and computed global band
        /// index (see <see cref="GlobalBandIndex"/>) before a cell is flagged as drifted. A
        /// heuristic content-authoring knob, not derived from <see cref="PowerBudgetConstants"/>.
        /// </summary>
        public const int BandDriftTolerance = 1;

        /// <summary>
        /// Flattens a (Tier, Band) cell into a single strictly-increasing index across the whole
        /// table (tier-major, band-minor) so drift can be expressed as one integer distance
        /// regardless of whether it crosses a tier boundary.
        /// </summary>
        public static int GlobalBandIndex(int tier, int band)
            => tier * PowerBudgetConstants.BandsPerTier + (band - 1);
    }
}
