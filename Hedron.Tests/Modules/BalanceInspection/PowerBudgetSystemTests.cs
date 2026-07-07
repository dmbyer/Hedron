using System.Collections.Generic;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="PowerBudgetSystem"/>.
    ///
    /// Coverage contract: docs/implementation-plans/power-budget-inspector.md Postconditions
    /// P1 (weighted sum), P2 (weight-table sanity), P3 (tier baseline), P4 (band derivation +
    /// overlap, the golden-number functional-validation gate).
    /// </summary>
    public sealed class PowerBudgetSystemTests
    {
        private static readonly PowerBudgetSystem System = new();

        // ── P1 — weighted sum ────────────────────────────────────────────────────

        [Fact]
        public void Estimate_empty_snapshot_is_zero()
        {
            var snapshot = new PowerSnapshot(new Dictionary<ScoreId, int>());
            Assert.Equal(0, System.Estimate(snapshot));
        }

        [Fact]
        public void Estimate_unweighted_score_contributes_zero()
        {
            // ManaMax carries a zero weight in PowerBudgetConstants.Weights.
            var snapshot = new PowerSnapshot(new Dictionary<ScoreId, int> { [ScoreId.ManaMax] = 500 });
            Assert.Equal(0, System.Estimate(snapshot));
        }

        [Fact]
        public void Estimate_sums_weighted_scores_over_a_mixed_snapshot()
        {
            var snapshot = new PowerSnapshot(new Dictionary<ScoreId, int>
            {
                [ScoreId.Body] = 10,
                [ScoreId.Mind] = 10,
                [ScoreId.AttackPower] = 5,
            });

            // 10*weight(Body) + 10*weight(Mind) + 5*weight(AttackPower)
            var expected =
                10 * PowerBudgetConstants.Weights[ScoreId.Body] +
                10 * PowerBudgetConstants.Weights[ScoreId.Mind] +
                5 * PowerBudgetConstants.Weights[ScoreId.AttackPower];

            Assert.Equal(expected, System.Estimate(snapshot));
        }

        // ── P3 — tier baseline ───────────────────────────────────────────────────

        [Fact]
        public void Estimate_tier_zero_equals_snapshot_only()
        {
            var snapshot = new PowerSnapshot(new Dictionary<ScoreId, int> { [ScoreId.Body] = 10 });

            Assert.Equal(System.Estimate(snapshot), System.Estimate(snapshot, tier: 0));
        }

        [Fact]
        public void Estimate_with_tier_adds_baseline_over_tracked_scores()
        {
            var snapshot = new PowerSnapshot(new Dictionary<ScoreId, int> { [ScoreId.Body] = 10 });
            var baseline = System.Estimate(snapshot);

            var perTierStep = 0;
            foreach (var tracked in PowerBudgetConstants.TrackedScores)
                perTierStep += PowerBudgetConstants.Weights[tracked] * PowerBudgetConstants.TierBaselineStep;

            Assert.Equal(baseline + perTierStep, System.Estimate(snapshot, tier: 1));
            Assert.Equal(baseline + perTierStep * 3, System.Estimate(snapshot, tier: 3));
        }

        // ── P2 — weight-table sanity ─────────────────────────────────────────────

        [Fact]
        public void Combat_relevant_weights_exceed_pool_weights()
        {
            var poolWeights = new[]
            {
                PowerBudgetConstants.Weights[ScoreId.ManaMax],
                PowerBudgetConstants.Weights[ScoreId.StaminaMax],
                PowerBudgetConstants.Weights[ScoreId.AstraMax],
                PowerBudgetConstants.Weights[ScoreId.HpCurrent],
                PowerBudgetConstants.Weights[ScoreId.ManaCurrent],
                PowerBudgetConstants.Weights[ScoreId.StaminaCurrent],
                PowerBudgetConstants.Weights[ScoreId.AstraCurrent],
            };

            foreach (var combatScore in new[] { ScoreId.Body, ScoreId.HpMax, ScoreId.AttackPower, ScoreId.Defense })
            {
                var weight = PowerBudgetConstants.Weights[combatScore];
                Assert.True(weight > 0, $"{combatScore} weight must be positive.");
                foreach (var poolWeight in poolWeights)
                    Assert.True(weight > poolWeight, $"{combatScore} weight ({weight}) must exceed pool weight ({poolWeight}).");
            }
        }

        // ── P4 — band derivation, golden numbers ─────────────────────────────────

        [Fact]
        public void Classify_reference_base_build_is_band_zero()
        {
            var referenceEstimate = System.Estimate(new PowerSnapshot(PowerBudgetConstants.ReferenceBaseScores));
            Assert.Equal(0, System.Classify(referenceEstimate));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void Classify_at_the_tier_N_anchor_returns_band_N(int tier)
        {
            var anchor = System.BandAnchor(tier);
            Assert.Equal(tier, System.Classify(anchor));
        }

        [Fact]
        public void Classify_below_the_lowest_anchor_floors_to_band_zero()
        {
            var anchor0 = System.BandAnchor(0);
            Assert.Equal(0, System.Classify(anchor0 - 1000));
        }

        [Fact]
        public void Classify_a_value_in_the_band_overlap_returns_the_higher_band()
        {
            // anchor(1) sits BandSpan below the pure Tier-1 reference power — a value at or above
            // anchor(1) but below the pure Tier-1 estimate is "in the overlap" and must classify
            // as band 1, even though it's below the tier-0 reference's own power.
            var anchor1 = System.BandAnchor(1);
            Assert.Equal(1, System.Classify(anchor1));
        }

        [Fact]
        public void BandAnchor_equals_reference_estimate_minus_band_span()
        {
            for (var tier = 0; tier <= PowerBudgetConstants.MaxTier; tier++)
            {
                var expected = System.Estimate(new PowerSnapshot(PowerBudgetConstants.ReferenceBaseScores), tier)
                    - PowerBudgetConstants.BandSpan;
                Assert.Equal(expected, System.BandAnchor(tier));
            }
        }

        [Fact]
        public void Band_anchors_are_strictly_increasing_with_tier()
        {
            for (var tier = 1; tier <= PowerBudgetConstants.MaxTier; tier++)
                Assert.True(System.BandAnchor(tier) > System.BandAnchor(tier - 1));
        }
    }
}
