using System;
using System.Collections.Generic;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Systems;

namespace Hedron.Core.Modules.Simulation.Systems
{
    /// <summary>
    /// Reads tolerances from <see cref="IBalanceStandardsRegistry"/> so the CLI summary, the sim-3
    /// editor, and the promoted CI invariants can never drift onto different expected-outcome math
    /// (INV-19).
    /// </summary>
    public sealed class SimOutcomeEvaluator : ISimOutcomeEvaluator
    {
        private readonly IBalanceStandardsRegistry _standardsRegistry;

        public SimOutcomeEvaluator(IBalanceStandardsRegistry standardsRegistry)
        {
            _standardsRegistry = standardsRegistry;
        }

        public IReadOnlyList<SimVerdict> Evaluate(
            ResolvedCombatant sideA,
            ResolvedCombatant sideB,
            int sideAWins,
            int sideBWins,
            int draws)
        {
            if (sideA.Cell is not { } cellA || sideB.Cell is not { } cellB)
            {
                return new[]
                {
                    new SimVerdict("outcome", null, "skipped — one or both sides have no (Tier, Band) cell"),
                };
            }

            var tunables = _standardsRegistry.Tunables;
            var indexA = tunables.GlobalBandIndex(cellA.Tier, cellA.Band);
            var indexB = tunables.GlobalBandIndex(cellB.Tier, cellB.Band);
            var diff = Math.Abs(indexA - indexB);
            var decisive = sideAWins + sideBWins;

            if (diff == 0)
                return new[] { EqualCellVerdict(cellA, sideAWins, decisive) };

            if (diff == 1)
                return new[] { HigherBandVerdict(indexA > indexB ? cellA : cellB, indexA > indexB ? sideAWins : sideBWins, decisive) };

            return new[]
            {
                new SimVerdict("outcome", null, $"skipped — band-index gap {diff} has no defined tolerance"),
            };
        }

        private SimVerdict EqualCellVerdict(PowerBand cell, int sideAWins, int decisive)
        {
            if (decisive == 0)
                return new SimVerdict("equalCellWinRate", null, "skipped — no decisive runs (all draws)");

            var tolerances = _standardsRegistry.OutcomesFor(cell.Tier, cell.Band);
            var winRate = (double)sideAWins / decisive;
            var passed = Math.Abs(winRate - tolerances.EqualCellWinRate) <= tolerances.WinRateTolerance;

            return new SimVerdict(
                "equalCellWinRate",
                passed,
                $"side A win rate {winRate:P1} vs expected {tolerances.EqualCellWinRate:P1} ± {tolerances.WinRateTolerance:P1}");
        }

        private SimVerdict HigherBandVerdict(PowerBand higherCell, int higherSideWins, int decisive)
        {
            if (decisive == 0)
                return new SimVerdict("higherBandWinRateFloor", null, "skipped — no decisive runs (all draws)");

            var tolerances = _standardsRegistry.OutcomesFor(higherCell.Tier, higherCell.Band);
            var winRate = (double)higherSideWins / decisive;
            var passed = winRate >= tolerances.HigherBandWinRateFloor;

            return new SimVerdict(
                "higherBandWinRateFloor",
                passed,
                $"higher-band side win rate {winRate:P1} vs floor {tolerances.HigherBandWinRateFloor:P1}");
        }
    }
}
