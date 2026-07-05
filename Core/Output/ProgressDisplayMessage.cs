using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Output
{
    /// <summary>One track's row in the <c>progress</c> command output.</summary>
    public sealed record ProgressTrackRow(
        ScoreId Track,
        int ImprovementCount,
        int CumulativeXp,
        int XpToNextThreshold);

    /// <summary>
    /// Carries the per-track progression block written by the <c>progress</c> command. Empty
    /// <see cref="Rows"/> means the entity has never earned XP on any track.
    /// </summary>
    public sealed record ProgressDisplayMessage(IReadOnlyList<ProgressTrackRow> Rows) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
