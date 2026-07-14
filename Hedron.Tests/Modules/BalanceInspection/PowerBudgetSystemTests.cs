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
        private static readonly PowerBudgetSystem System = new(PowerBudgetTunables.Default);

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
            // ManaMax carries a zero weight in PowerBudgetTunables.Default.Weights.
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
                10 * PowerBudgetTunables.Default.Weights[ScoreId.Body] +
                10 * PowerBudgetTunables.Default.Weights[ScoreId.Mind] +
                5 * PowerBudgetTunables.Default.Weights[ScoreId.AttackPower];

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
            foreach (var tracked in PowerBudgetTunables.Default.TrackedScores)
                perTierStep += PowerBudgetTunables.Default.Weights[tracked] * PowerBudgetTunables.Default.TierBaselineStep;

            Assert.Equal(baseline + perTierStep, System.Estimate(snapshot, tier: 1));
            Assert.Equal(baseline + perTierStep * 3, System.Estimate(snapshot, tier: 3));
        }

        // ── Injection is real, not decorative (sim-1 WP-1) ────────────────────────

        [Fact]
        public void A_synthetic_tunables_record_shifts_Estimate_as_predicted()
        {
            var custom = new PowerBudgetTunables(
                Weights: new Dictionary<ScoreId, int> { [ScoreId.Body] = 3 },
                BandSpan: 1,
                BandsPerTier: 1,
                ReferenceBaseScores: new Dictionary<ScoreId, int> { [ScoreId.Body] = 0 },
                MaxTier: 1,
                TierBaselineStep: 5,
                TrackedScores: new[] { ScoreId.Body });
            var system = new PowerBudgetSystem(custom);

            var snapshot = new PowerSnapshot(new Dictionary<ScoreId, int> { [ScoreId.Body] = 10 });

            // 10 * weight(3) = 30, independent of PowerBudgetTunables.Default (which weights
            // Body at 10) — proves the injected record, not a hardcoded constant, drives Estimate.
            Assert.Equal(30, system.Estimate(snapshot));
            Assert.NotEqual(System.Estimate(snapshot), system.Estimate(snapshot));
        }

        [Fact]
        public void A_synthetic_tunables_record_shifts_Classify_and_TargetRange_as_predicted()
        {
            var custom = new PowerBudgetTunables(
                Weights: new Dictionary<ScoreId, int> { [ScoreId.Body] = 1 },
                BandSpan: 1,
                BandsPerTier: 1,
                ReferenceBaseScores: new Dictionary<ScoreId, int> { [ScoreId.Body] = 0 },
                MaxTier: 2,
                TierBaselineStep: 10,
                TrackedScores: new[] { ScoreId.Body });
            var system = new PowerBudgetSystem(custom);

            // Tier span = weight(1) * TierBaselineStep(10) = 10; BandsPerTier = 1, so each tier
            // is one band wide. Tier 1's floor sits at BandAnchor(1) + BandSpan = anchor + 1.
            var tier1Range = system.TargetRange(1, 1);
            Assert.Equal(1, system.Classify(tier1Range.MinPower).Tier);

            // With PowerBudgetTunables.Default (BandsPerTier = 3, different weights), the same
            // cell computation would throw (band 1 vs Default's own bounds are fine, but the
            // custom MaxTier of 2 rejects tier queries the Default table would accept differently) —
            // spot-check that the two instances disagree on the same input, proving independent state.
            Assert.NotEqual(System.TargetRange(0, 1), system.TargetRange(0, 1));
        }

        // ── Weight-table sanity ───────────────────────────────────────────────────

        [Fact]
        public void Combat_relevant_weights_exceed_pool_weights()
        {
            var poolWeights = new[]
            {
                PowerBudgetTunables.Default.Weights[ScoreId.ManaMax],
                PowerBudgetTunables.Default.Weights[ScoreId.StaminaMax],
                PowerBudgetTunables.Default.Weights[ScoreId.AstraMax],
                PowerBudgetTunables.Default.Weights[ScoreId.HpCurrent],
                PowerBudgetTunables.Default.Weights[ScoreId.ManaCurrent],
                PowerBudgetTunables.Default.Weights[ScoreId.StaminaCurrent],
                PowerBudgetTunables.Default.Weights[ScoreId.AstraCurrent],
            };

            foreach (var combatScore in new[] { ScoreId.Body, ScoreId.HpMax, ScoreId.AttackPower, ScoreId.Defense })
            {
                var weight = PowerBudgetTunables.Default.Weights[combatScore];
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
            foreach (var tracked in PowerBudgetTunables.Default.TrackedScores)
                perTierStep += PowerBudgetTunables.Default.Weights[tracked] * PowerBudgetTunables.Default.TierBaselineStep;

            Assert.True(
                PowerBudgetTunables.Default.BandSpan < perTierStep / PowerBudgetTunables.Default.BandsPerTier,
                "BandSpan must stay below tierSpan/BandsPerTier or the 3-band subdivision stops being strictly ordered.");
        }

        // ── Classify — tier derivation (BandAnchor + overlap, unchanged shape) ────

        [Fact]
        public void Classify_reference_base_build_is_tier_zero_band_one()
        {
            var referenceEstimate = System.Estimate(new PowerSnapshot(PowerBudgetTunables.Default.ReferenceBaseScores));
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
            for (var tier = 0; tier <= PowerBudgetTunables.Default.MaxTier; tier++)
            {
                var expected = System.Estimate(new PowerSnapshot(PowerBudgetTunables.Default.ReferenceBaseScores), tier)
                    - PowerBudgetTunables.Default.BandSpan;
                Assert.Equal(expected, System.BandAnchor(tier));
            }
        }

        [Fact]
        public void Band_anchors_are_strictly_increasing_with_tier()
        {
            for (var tier = 1; tier <= PowerBudgetTunables.Default.MaxTier; tier++)
                Assert.True(System.BandAnchor(tier) > System.BandAnchor(tier - 1));
        }

        // ── Classify — within-tier band partition (low/mid/high thirds) ──────────

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(6)]
        public void Classify_partitions_the_tier_span_into_three_bands(int tier)
        {
            var tierFloor = System.BandAnchor(tier) + PowerBudgetTunables.Default.BandSpan; // TierReferencePower(tier)
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
            var safelyInsideBand3 = range3.MaxPower - 1 - PowerBudgetTunables.Default.BandSpan;
            Assert.Equal(tier, System.Classify(safelyInsideBand3).Tier);
            Assert.Equal(3, System.Classify(safelyInsideBand3).Band);
        }

        // ── TargetRange — the inverse query ───────────────────────────────────────

        [Fact]
        public void TargetRange_band_one_starts_at_the_tier_reference_power()
        {
            for (var tier = 0; tier <= PowerBudgetTunables.Default.MaxTier; tier++)
            {
                var tierFloor = System.BandAnchor(tier) + PowerBudgetTunables.Default.BandSpan;
                Assert.Equal(tierFloor, System.TargetRange(tier, 1).MinPower);
            }
        }

        [Fact]
        public void TargetRange_band_three_max_abuts_the_next_tiers_band_one_min()
        {
            for (var tier = 0; tier < PowerBudgetTunables.Default.MaxTier; tier++)
            {
                var band3Max = System.TargetRange(tier, 3).MaxPower;
                var nextBand1Min = System.TargetRange(tier + 1, 1).MinPower;
                Assert.Equal(nextBand1Min, band3Max);
            }
        }

        [Fact]
        public void TargetRange_cells_partition_within_a_tier_with_no_gap_or_overlap()
        {
            for (var tier = 0; tier <= PowerBudgetTunables.Default.MaxTier; tier++)
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
            for (var tier = 0; tier <= PowerBudgetTunables.Default.MaxTier; tier++)
            {
                for (var band = 1; band <= PowerBudgetTunables.Default.BandsPerTier; band++)
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
