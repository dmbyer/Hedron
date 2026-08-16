using System.Collections.Generic;
using Hedron.Core.Modules.Progression;

namespace Hedron.Core.Output
{
    /// <summary>One track's row in the <c>progress</c> command output.</summary>
    public sealed record ProgressTrackRow(
        ProgressionTrack Track,
        int ImprovementCount,
        int CumulativeXp,
        int XpToNextThreshold);

    /// <summary>
    /// Carries the per-track progression block written by the <c>progress</c> command, split into
    /// score tracks (which grant power) and ability tracks (display-only rank, D3). Both empty
    /// means the entity has never earned XP on any track.
    /// </summary>
    public sealed record ProgressDisplayMessage(
        IReadOnlyList<ProgressTrackRow> Rows,
        IReadOnlyList<ProgressTrackRow> AbilityRows) : IOutputMessage
    {
        public OutputCategory Category => OutputCategory.Info;
    }
}
