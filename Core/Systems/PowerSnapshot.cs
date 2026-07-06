using System.Collections.Generic;
using Hedron.Core.Modules.Stats;

namespace Hedron.Core.Systems
{
    /// <summary>
    /// Plain-data input to <see cref="IPowerBudgetSystem"/>: a <see cref="ScoreId"/> → magnitude
    /// map gathered by the caller (never an entity id, never an internal <c>IStatSystem</c> call —
    /// this is what keeps the oracle core-tier-generic, INV-2). An absent score contributes 0.
    /// </summary>
    public readonly struct PowerSnapshot
    {
        public IReadOnlyDictionary<ScoreId, int> Scores { get; }

        public PowerSnapshot(IReadOnlyDictionary<ScoreId, int> scores)
        {
            Scores = scores;
        }
    }
}
