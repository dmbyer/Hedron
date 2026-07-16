using System.Collections.Generic;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Compares batch outcomes against the sim-1 balance-standards registry's expected-outcome
    /// tolerances. Pure aggregation over caller-supplied counts — no run execution, no I/O.
    /// </summary>
    public interface ISimOutcomeEvaluator
    {
        /// <summary>
        /// Evaluates the equal-cell win-rate check (both sides share a (Tier, Band) cell) or the
        /// +1-band win-rate floor check (cells differ by exactly one global band index) against
        /// <paramref name="sideA"/>/<paramref name="sideB"/>'s resolved cells. Skips with a reason
        /// when either side has no cell, when there are no decisive runs, or when the cells' band-index
        /// gap has no defined tolerance.
        /// </summary>
        IReadOnlyList<SimVerdict> Evaluate(
            ResolvedCombatant sideA,
            ResolvedCombatant sideB,
            int sideAWins,
            int sideBWins,
            int draws);
    }
}
