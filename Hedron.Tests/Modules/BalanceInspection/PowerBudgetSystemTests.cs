using System.Collections.Generic;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Modules.BalanceInspection
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="PowerBudgetSystem"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/power-model-revision.md Postconditions —
    /// two-axis Classify (tier via the retained BandAnchor/BandSpan overlap, band via the
    /// within-tier partition), the TargetRange inverse, and the recalibrated golden numbers.
    /// Supersedes the one-axis power-budget-inspector.md coverage this file used to carry.
    /// </summary>
    public sealed class PowerBudgetSystemTests
    {
        private static readonly PowerBudgetSystem System = new();

        // ── Estimate — weighted sum ──────────────────────────────────────────────

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

        // ── Estimate — tier baseline ──────────────────────────────────────────────

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

        // ── Weight-table sanity ───────────────────────────────────────────────────

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

        // ── Calibration invariant ─────────────────────────────────────────────────

        [Fact]
        public void BandSpan_is_strictly_below_a_third_of_the_tier_span()
        {
            var perTierStep = 0;
            foreach (var tracked in PowerBudgetConstants.TrackedScores)
                perTierStep += PowerBudgetConstants.Weights[tracked] * PowerBudgetConstants.TierBaselineStep;

            Assert.True(
                PowerBudgetConstants.BandSpan < perTierStep / PowerBudgetConstants.BandsPerTier,
                "BandSpan must stay below tierSpan/BandsPerTier or the 3-band subdivision stops being strictly ordered.");
        }

        // ── Classify — tier derivation (BandAnchor + overlap, unchanged shape) ────

        [Fact]
        public void Classify_reference_base_build_is_tier_zero_band_one()
        {
            var referenceEstimate = System.Estimate(new PowerSnapshot(PowerBudgetConstants.ReferenceBaseScores));
            var band = System.Classify(referenceEstimate);
            Assert.Equal(0, band.Tier);
            Assert.Equal(1, band.Band);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void Classify_at_the_tier_N_anchor_returns_tier_N(int tier)
        {
            var anchor = System.BandAnchor(tier);
            Assert.Equal(tier, System.Classify(anchor).Tier);
        }

        [Fact]
        public void Classify_below_the_lowest_anchor_floors_to_tier_zero_band_one()
        {
            var anchor0 = System.BandAnchor(0);
            var band = System.Classify(anchor0 - 1000);
            Assert.Equal(0, band.Tier);
            Assert.Equal(1, band.Band); // computed band is never 0 — that's the authored sentinel only.
        }

        [Fact]
        public void Classify_a_value_in_the_tier_boundary_overlap_returns_the_higher_tier_at_band_one()
        {
            // anchor(1) sits BandSpan below the pure Tier-1 reference power — a value at or above
            // anchor(1) but below the pure Tier-1 estimate is "in the overlap" and must classify
            // as tier 1, band 1 (it hasn't reached the tier's own reference power yet).
            var anchor1 = System.BandAnchor(1);
            var band = System.Classify(anchor1);
            Assert.Equal(1, band.Tier);
            Assert.Equal(1, band.Band);
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

        // ── Classify — within-tier band partition (low/mid/high thirds) ──────────

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(6)]
        public void Classify_partitions_the_tier_span_into_three_bands(int tier)
        {
            var tierFloor = System.BandAnchor(tier) + PowerBudgetConstants.BandSpan; // TierReferencePower(tier)
            var range1 = System.TargetRange(tier, 1);
            var range2 = System.TargetRange(tier, 2);
            var range3 = System.TargetRange(tier, 3);

            Assert.Equal(1, System.Classify(tierFloor).Band);
            Assert.Equal(1, System.Classify(range1.MaxPower - 1).Band);
            Assert.Equal(2, System.Classify(range2.MinPower).Band);
            Assert.Equal(2, System.Classify(range2.MaxPower - 1).Band);
            Assert.Equal(3, System.Classify(range3.MinPower).Band);

            // range3.MaxPower - 1 is deliberately NOT checked here: for tier < MaxTier, the final
            // BandSpan-wide slice of band 3 is the tier-boundary overlap zone (retained hysteresis)
            // and reclassifies into (tier + 1, band 1) — see
            // Classify_a_value_in_the_tier_boundary_overlap_returns_the_higher_tier_at_band_one.
            // Check a power safely inside band 3, below where that overlap begins.
            var safelyInsideBand3 = range3.MaxPower - 1 - PowerBudgetConstants.BandSpan;
            Assert.Equal(tier, System.Classify(safelyInsideBand3).Tier);
            Assert.Equal(3, System.Classify(safelyInsideBand3).Band);
        }

        // ── TargetRange — the inverse query ───────────────────────────────────────

        [Fact]
        public void TargetRange_band_one_starts_at_the_tier_reference_power()
        {
            for (var tier = 0; tier <= PowerBudgetConstants.MaxTier; tier++)
            {
                var tierFloor = System.BandAnchor(tier) + PowerBudgetConstants.BandSpan;
                Assert.Equal(tierFloor, System.TargetRange(tier, 1).MinPower);
            }
        }

        [Fact]
        public void TargetRange_band_three_max_abuts_the_next_tiers_band_one_min()
        {
            for (var tier = 0; tier < PowerBudgetConstants.MaxTier; tier++)
            {
                var band3Max = System.TargetRange(tier, 3).MaxPower;
                var nextBand1Min = System.TargetRange(tier + 1, 1).MinPower;
                Assert.Equal(nextBand1Min, band3Max);
            }
        }

        [Fact]
        public void TargetRange_cells_partition_within_a_tier_with_no_gap_or_overlap()
        {
            for (var tier = 0; tier <= PowerBudgetConstants.MaxTier; tier++)
            {
                var r1 = System.TargetRange(tier, 1);
                var r2 = System.TargetRange(tier, 2);
                var r3 = System.TargetRange(tier, 3);

                Assert.True(r1.MinPower < r1.MaxPower);
                Assert.Equal(r1.MaxPower, r2.MinPower);
                Assert.Equal(r2.MaxPower, r3.MinPower);
                Assert.True(r3.MinPower < r3.MaxPower);
            }
        }

        [Fact]
        public void TargetRange_cell_floors_are_strictly_increasing_across_the_whole_table()
        {
            var previous = int.MinValue;
            for (var tier = 0; tier <= PowerBudgetConstants.MaxTier; tier++)
            {
                for (var band = 1; band <= PowerBudgetConstants.BandsPerTier; band++)
                {
                    var min = System.TargetRange(tier, band).MinPower;
                    Assert.True(min > previous, $"(tier {tier}, band {band}) floor {min} must exceed the previous cell's floor {previous}.");
                    previous = min;
                }
            }
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(7, 1)]
        [InlineData(0, 0)]
        [InlineData(0, 4)]
        public void TargetRange_rejects_an_out_of_range_cell(int tier, int band)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => System.TargetRange(tier, band));
        }
    }
}
