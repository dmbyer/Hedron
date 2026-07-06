using System.Collections.Generic;

namespace Hedron.Core.Output
{
    /// <summary>One tier band's row in the <c>powerband</c> command output.</summary>
    public sealed record PowerBandRow(int Tier, int Anchor, int ReferenceEstimate);

    /// <summary>
    /// Carries the band-definition block written by the <c>powerband</c> command. With no tier
    /// argument, <see cref="Rows"/> holds every band 0–<c>PowerBudgetConstants.MaxTier</c>; with a
    /// tier argument, it holds exactly one row.
    /// </summary>
    public sealed record PowerbandMessage(IReadOnlyList<PowerBandRow> Rows) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.System;
    }
}
