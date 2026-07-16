using System.Collections.Generic;
using Hedron.Core.Modules.BalanceInspection.Standards;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Simulation
{
    /// <summary>Tier 1 — <see cref="SimOutcomeEvaluator"/> expected-vs-actual verdict math.</summary>
    public sealed class SimOutcomeEvaluatorTests
    {
        private static SimOutcomeEvaluator NewEvaluator() =>
            new(new BalanceStandardsRegistry(BalanceStandardsDefaults.Document));

        private static ResolvedCombatant Combatant(PowerBand? cell) =>
            new("probe", new Dictionary<ScoreId, int>(), System.Array.Empty<string>(), 0, "melee-only", cell);

        // ── Equal-cell ────────────────────────────────────────────────────────

        [Fact]
        public void Evaluate_EqualCell_WithinTolerance_Passes()
        {
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 2)), Combatant(new PowerBand(2, 2)), sideAWins: 50, sideBWins: 50, draws: 0);

            var verdict = Assert.Single(verdicts);
            Assert.Equal("equalCellWinRate", verdict.Name);
            Assert.True(verdict.Passed);
        }

        [Fact]
        public void Evaluate_EqualCell_AtToleranceBoundary_Passes()
        {
            // BalanceStandardsDefaults: EqualCellWinRate 0.5, WinRateTolerance 0.1 → 0.6 is exactly at the boundary.
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 2)), Combatant(new PowerBand(2, 2)), sideAWins: 60, sideBWins: 40, draws: 0);

            Assert.True(Assert.Single(verdicts).Passed);
        }

        [Fact]
        public void Evaluate_EqualCell_OutsideTolerance_Fails()
        {
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 2)), Combatant(new PowerBand(2, 2)), sideAWins: 90, sideBWins: 10, draws: 0);

            Assert.False(Assert.Single(verdicts).Passed);
        }

        [Fact]
        public void Evaluate_EqualCell_DrawsExcludedFromRatio()
        {
            // 40/40 decisive (50/50 split) with 20 draws — the ratio must read 0.5, not 0.4 of 100.
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 2)), Combatant(new PowerBand(2, 2)), sideAWins: 40, sideBWins: 40, draws: 20);

            Assert.True(Assert.Single(verdicts).Passed);
        }

        [Fact]
        public void Evaluate_EqualCell_NoDecisiveRuns_SkipsWithReason()
        {
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 2)), Combatant(new PowerBand(2, 2)), sideAWins: 0, sideBWins: 0, draws: 10);

            var verdict = Assert.Single(verdicts);
            Assert.Null(verdict.Passed);
            Assert.Contains("no decisive runs", verdict.Reason);
        }

        // ── +1 band ───────────────────────────────────────────────────────────

        [Fact]
        public void Evaluate_OneBandHigher_AboveFloor_Passes()
        {
            // (2,3) global index 8 vs (2,2) global index 7 — a 1-band gap; side A is higher.
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 3)), Combatant(new PowerBand(2, 2)), sideAWins: 70, sideBWins: 30, draws: 0);

            var verdict = Assert.Single(verdicts);
            Assert.Equal("higherBandWinRateFloor", verdict.Name);
            Assert.True(verdict.Passed);
        }

        [Fact]
        public void Evaluate_OneBandHigher_BelowFloor_Fails()
        {
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 3)), Combatant(new PowerBand(2, 2)), sideAWins: 50, sideBWins: 50, draws: 0);

            Assert.False(Assert.Single(verdicts).Passed);
        }

        // ── Skipped ───────────────────────────────────────────────────────────

        [Fact]
        public void Evaluate_MissingCell_SkipsWithReason()
        {
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 2)), Combatant(null), sideAWins: 10, sideBWins: 5, draws: 0);

            var verdict = Assert.Single(verdicts);
            Assert.Null(verdict.Passed);
            Assert.Contains("no (Tier, Band) cell", verdict.Reason);
        }

        [Fact]
        public void Evaluate_UndefinedBandGap_SkipsWithReason()
        {
            // Global indexes differ by 2 (e.g. tier 2 band 2 = 7 vs tier 3 band 1 = 9) — no defined tolerance.
            var evaluator = NewEvaluator();
            var verdicts = evaluator.Evaluate(Combatant(new PowerBand(2, 2)), Combatant(new PowerBand(3, 1)), sideAWins: 10, sideBWins: 5, draws: 0);

            var verdict = Assert.Single(verdicts);
            Assert.Null(verdict.Passed);
            Assert.Contains("no defined tolerance", verdict.Reason);
        }
    }
}
