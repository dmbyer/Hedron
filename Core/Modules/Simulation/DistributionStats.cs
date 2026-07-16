using System;
using System.Collections.Generic;
using System.Linq;

namespace Hedron.Core.Modules.Simulation
{
    /// <summary>
    /// Mean/median/p10/p90/min/max over a batch of run outcomes. Pure, order-independent (sorts
    /// internally) so the same input produces the same statistics regardless of run-completion
    /// scheduling under parallelism.
    /// </summary>
    public sealed record DistributionStats(double Mean, double Median, double P10, double P90, int Min, int Max)
    {
        public static DistributionStats From(IReadOnlyList<int> values)
        {
            if (values.Count == 0)
                return new DistributionStats(0, 0, 0, 0, 0, 0);

            var sorted = values.OrderBy(v => v).ToList();
            return new DistributionStats(
                Mean: sorted.Average(),
                Median: Percentile(sorted, 0.5),
                P10: Percentile(sorted, 0.10),
                P90: Percentile(sorted, 0.90),
                Min: sorted[0],
                Max: sorted[^1]);
        }

        private static double Percentile(IReadOnlyList<int> sorted, double p)
        {
            if (sorted.Count == 1)
                return sorted[0];

            var rank = p * (sorted.Count - 1);
            var lowerIndex = (int)Math.Floor(rank);
            var upperIndex = (int)Math.Ceiling(rank);
            if (lowerIndex == upperIndex)
                return sorted[lowerIndex];

            var fraction = rank - lowerIndex;
            return sorted[lowerIndex] + (sorted[upperIndex] - sorted[lowerIndex]) * fraction;
        }
    }
}
