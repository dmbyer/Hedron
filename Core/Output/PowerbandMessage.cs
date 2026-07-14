using System.Collections.Generic;
using Hedron.Core.Systems;

namespace Hedron.Core.Output
{
    /// <summary>One (Tier, Band) cell's row in the <c>powerband</c> command output.</summary>
    public sealed record PowerBandRow(int Tier, int Band, PowerRange Range);

    /// <summary>
    /// Carries the Tier×Band definition table written by the <c>powerband</c> command. With no
    /// tier argument, <see cref="Rows"/> holds every cell (0&#8211;<c>PowerBudgetTunables.MaxTier</c>
    /// × 1&#8211;<c>PowerBudgetTunables.BandsPerTier</c>, ~21 rows); with a tier argument, it holds
    /// just that tier's <c>BandsPerTier</c> rows.
    /// </summary>
    public sealed record PowerbandMessage(IReadOnlyList<PowerBandRow> Rows) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.System;
    }
}
