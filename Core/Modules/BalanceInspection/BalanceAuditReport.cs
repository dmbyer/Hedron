using System.Collections.Generic;

namespace Hedron.Core.Modules.BalanceInspection
{
    /// <summary>Which content kind a <see cref="BalanceAuditEntry"/> was projected from.</summary>
    public enum BalanceAuditKind
    {
        Item,
        Mob,
    }

    /// <summary>
    /// One piece of content whose authored (Tier, Band) drifted from its computed classification
    /// by more than <see cref="BalanceAuditConstants.BandDriftTolerance"/> global band-index steps.
    /// </summary>
    public sealed record BalanceAuditEntry(
        BalanceAuditKind Kind,
        string BlueprintId,
        int AuthoredTier,
        int AuthoredBand,
        int ComputedTier,
        int ComputedBand,
        int Drift);

    /// <summary>
    /// Result of <see cref="IBalanceAuditSystem.Audit"/>: every item/mob whose authored band tag
    /// drifted past tolerance from its computed classification, plus a bucket count of every
    /// item/mob (including untagged content) by its *computed* (Tier, Band) cell — the free
    /// "how much content exists at power level X" report. Recomputed on demand, never cached or
    /// persisted (INV-24 spirit — derived-on-read); a soft/advisory tool, never a build/CI gate.
    /// </summary>
    public sealed record BalanceAuditReport(
        IReadOnlyList<BalanceAuditEntry> Drifted,
        IReadOnlyDictionary<(int Tier, int Band), int> BucketCounts);
}
