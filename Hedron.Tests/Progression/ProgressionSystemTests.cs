using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Components;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 1 — system unit tests for <see cref="ProgressionSystem"/>.
    ///
    /// Coverage contract: Postconditions from
    /// docs/roadmap/completed/progression-substrate.md (WP-1), extended by the use-based-XP slice
    /// with the chance gate, the scale composition, and the RNG draw contract.
    /// </summary>
    public sealed class ProgressionSystemTests
    {
        private static (ProgressionSystem System, EntityService Ecs) CreateSystem(IRandom rng)
        {
            var ecs = new EntityService();
            var system = new ProgressionSystem(
                ecs, rng, new PowerBudgetSystem(PowerBudgetTunables.Default), new AdvancementRuleRegistry());
            return (system, ecs);
        }

        private static uint CreateEntity(EntityService ecs, int mind = 10, int body = 10, int spirit = 10, int attunement = 10)
            => new EntityBuilder(ecs).AsPlayer().WithAttributes(mind, body, spirit, attunement).Build();

        // ── AwardExperience ───────────────────────────────────────────────────────

        [Fact]
        public void AwardExperience_adds_amount_and_creates_entry_on_first_award()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1));
            var entity = CreateEntity(ecs);

            var outcome = system.AwardExperience(entity, ScoreId.Body, 40, XpSource.CombatKill);

            Assert.Equal(40, outcome.AmountAwarded);
            Assert.Equal(0, outcome.ImprovementsGained);
            Assert.True(ecs.HasComponent<ProgressionComponent>(entity));
            Assert.Equal(40, system.GetXp(entity, ScoreId.Body));
        }

        [Fact]
        public void AwardExperience_non_positive_amount_is_a_no_op()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1));
            var entity = CreateEntity(ecs);

            var zero = system.AwardExperience(entity, ScoreId.Body, 0, XpSource.CombatKill);
            var negative = system.AwardExperience(entity, ScoreId.Body, -5, XpSource.CombatKill);

            Assert.Equal(0, zero.AmountAwarded);
            Assert.Equal(0, negative.AmountAwarded);
            Assert.False(ecs.HasComponent<ProgressionComponent>(entity),
                "A non-positive award must not create ProgressionComponent.");
        }

        [Fact]
        public void An_ability_track_accrues_and_improves_on_the_same_curve_as_a_score_track()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1));
            var entity = CreateEntity(ecs);
            var abilityTrack = ProgressionTrack.Ability("kick");

            var ability = system.AwardExperience(entity, abilityTrack, 300, XpSource.AbilityUse);
            var score = system.AwardExperience(entity, ScoreId.Body, 300, XpSource.CombatKill);

            Assert.Equal(score.ImprovementsGained, ability.ImprovementsGained);
            Assert.Equal(score.NewImprovementCount, ability.NewImprovementCount);
        }

        // ── TryImprove / multi-crossing ──────────────────────────────────────────

        [Fact]
        public void TryImprove_increments_once_per_threshold_crossed_by_a_single_award()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1));
            var entity = CreateEntity(ecs);

            // ThresholdBase=100, ThresholdIncrement=50 → cumulative thresholds 100,150,200,250,300.
            var outcome = system.AwardExperience(entity, ScoreId.Body, 300, XpSource.CombatKill);

            Assert.Equal(5, outcome.ImprovementsGained);
            Assert.Equal(5, outcome.NewImprovementCount);
            Assert.Equal(5, system.GetImprovementCount(entity, ScoreId.Body));
        }

        [Fact]
        public void TryImprove_does_not_fire_below_the_first_threshold()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1));
            var entity = CreateEntity(ecs);

            var outcome = system.AwardExperience(entity, ScoreId.Body, ProgressionConstants.ThresholdBase - 1, XpSource.CombatKill);

            Assert.Equal(0, outcome.ImprovementsGained);
            Assert.Equal(0, system.GetImprovementCount(entity, ScoreId.Body));
        }

        // ── Growing threshold ─────────────────────────────────────────────────────

        [Fact]
        public void Successive_thresholds_strictly_increase()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1));
            var entity = CreateEntity(ecs);

            var cumulativeXp = 0;
            var observedThresholds = new System.Collections.Generic.List<int>();

            for (var i = 0; i < 4; i++)
            {
                var needed = system.GetXpToNextThreshold(entity, ScoreId.Body);
                observedThresholds.Add(cumulativeXp + needed);

                var outcome = system.AwardExperience(entity, ScoreId.Body, needed, XpSource.CombatKill);
                cumulativeXp += needed;

                Assert.Equal(1, outcome.ImprovementsGained);
            }

            for (var i = 1; i < observedThresholds.Count; i++)
                Assert.True(observedThresholds[i] > observedThresholds[i - 1],
                    "Each successive cumulative threshold must exceed the previous (slowing-rate invariant).");
        }

        // ── Anti-grind scale (three cases) ───────────────────────────────────────

        [Fact]
        public void AwardCombatExperience_floors_to_zero_when_victim_far_below_killer()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1)); // no ints should be drawn
            var killer = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25); // power 100
            var victim = CreateEntity(ecs, mind: 2, body: 2, spirit: 2, attunement: 2);     // power 8, ratio 0.08 < 0.25

            var result = system.AwardCombatExperience(killer, victim);

            Assert.Equal(ProgressionConstants.CombatTracks.Length, result.Tracks.Count);
            Assert.All(result.Tracks, row =>
            {
                Assert.Equal(0, row.AmountAwarded);
                Assert.Equal(0, row.ImprovementsGained);
            });
        }

        [Fact]
        public void AwardCombatExperience_grants_full_base_for_a_peer()
        {
            var rng = new FakeRandom(new[] { 10, 10 }); // one draw per combat track
            var (system, ecs) = CreateSystem(rng);
            var killer = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25); // power 100
            var victim = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25); // power 100, ratio 1.0

            var result = system.AwardCombatExperience(killer, victim);

            Assert.All(result.Tracks, row => Assert.Equal(10, row.AmountAwarded));
        }

        [Fact]
        public void AwardCombatExperience_never_exceeds_the_configured_cap()
        {
            var rng = new FakeRandom(new[] { 10, 10 });
            var (system, ecs) = CreateSystem(rng);
            var killer = CreateEntity(ecs, mind: 12, body: 13, spirit: 12, attunement: 13); // power 50
            var victim = CreateEntity(ecs, mind: 50, body: 50, spirit: 50, attunement: 50); // power 200, ratio 4.0 → capped 1.5

            var result = system.AwardCombatExperience(killer, victim);

            // amount = round(10 * 1.5) = 15
            Assert.All(result.Tracks, row => Assert.Equal(15, row.AmountAwarded));
        }

        // ── Anti-grind rewire (power-budget-inspector WP-2, P9) ──────────────────

        [Fact]
        public void AwardCombatExperience_ignores_worn_gear_and_uses_only_raw_attributes()
        {
            // GetEffectivePower's snapshot must come from raw AttributesComponent fields, never
            // IStatSystem.Get — reading the effect-folded value would recreate the DI cycle this
            // proxy exists to avoid (see ProgressionSystem.GetEffectivePower). A killer with heavily
            // buffed gear but the SAME raw attributes as the victim must still read as a peer
            // (full base award), not as inflated by the gear.
            var rng = new FakeRandom(new[] { 10, 10 });
            var (system, ecs) = CreateSystem(rng);
            var killer = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25);
            var victim = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25);

            var weapon = ecs.CreateEntity().Id;
            ecs.AddComponent(weapon, new Hedron.Core.ECS.Components.ItemDataComponent
            {
                StatBonuses = { new Hedron.Core.ECS.Components.EquipmentStatBonus(ScoreId.AttackPower, 500) },
            });
            ecs.AddComponent(killer, new Hedron.Core.ECS.Components.EquipmentComponent
            {
                Slots = { [Hedron.Core.WornSlot.MainHand] = weapon },
            });

            var result = system.AwardCombatExperience(killer, victim);

            Assert.All(result.Tracks, row => Assert.Equal(10, row.AmountAwarded));
        }

        // ── The RNG draw contract (INV-26) ───────────────────────────────────────

        [Fact]
        public void A_kill_consumes_exactly_one_int_draw_per_track_and_no_double_draw()
        {
            // The load-bearing assertion of the slice. The kill row's BaseChance is 1.0 with zero
            // decay, so the chance roll short-circuits arithmetically — adding an unconditional
            // NextDouble() per candidate would shift the sandbox's shared seeded stream and move
            // every pinned simulation golden. Asserted here at the contract, directly, rather than
            // indirectly via "the goldens did not move".
            var rng = new CountingRandom(ints: new[] { 10, 10 });
            var (system, ecs) = CreateSystem(rng);
            var killer = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25);
            var victim = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25);

            system.AwardCombatExperience(killer, victim);

            Assert.Equal(
                new[]
                {
                    new CountingRandom.Draw("Next", 8, 13),
                    new CountingRandom.Draw("Next", 8, 13),
                },
                rng.Draws);
        }

        [Fact]
        public void A_trivial_victim_consumes_no_draws_at_all()
        {
            // Anti-grind failure is an ELIGIBILITY failure, not a zero multiplier — the candidate
            // must not reach either the chance roll or the amount draw.
            var rng = new CountingRandom();
            var (system, ecs) = CreateSystem(rng);
            var killer = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25);
            var victim = CreateEntity(ecs, mind: 2, body: 2, spirit: 2, attunement: 2);

            system.AwardCombatExperience(killer, victim);

            Assert.Empty(rng.Draws);
        }

        [Fact]
        public void An_ineligible_context_consumes_no_draws()
        {
            var rng = new CountingRandom();
            var (system, ecs) = CreateSystem(rng);
            var entity = CreateEntity(ecs);

            // RequiresPositiveMagnitude
            system.AwardUseExperience(entity, XpSource.DamageTaken, new UseAwardContext(Magnitude: 0));
            // RequiresAttributableActor
            system.AwardUseExperience(0, XpSource.AbilityUse, new UseAwardContext(SubjectAbilityId: "kick"));

            Assert.Empty(rng.Draws);
        }

        [Fact]
        public void An_unmapped_source_is_a_no_op()
        {
            var rng = new CountingRandom();
            var (system, ecs) = CreateSystem(rng);
            var entity = CreateEntity(ecs);

            var result = system.AwardUseExperience(entity, XpSource.Trainer, new UseAwardContext());

            Assert.Empty(result.Tracks);
            Assert.Empty(rng.Draws);
        }

        // ── Eligibility as rule data ─────────────────────────────────────────────

        [Fact]
        public void A_non_character_earner_is_rejected_by_the_player_earner_flag()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1));
            var mob = new EntityBuilder(ecs).AsMob("rat").WithAttributes(10, 10, 10, 10).Build();

            var result = system.AwardUseExperience(mob, XpSource.DamageTaken, new UseAwardContext(Magnitude: 10));

            Assert.Empty(result.Tracks);
        }

        // ── Chance gate ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0.24, true)]   // just below the 0.25 base chance → passes
        [InlineData(0.25, false)]  // exactly at it → strict '<' means it fails
        [InlineData(0.26, false)]  // above → fails
        public void The_chance_roll_compares_strictly_below_the_computed_chance(double roll, bool expectAward)
        {
            var rng = new FakeRandom(new[] { 5 });
            rng.EnqueueDouble(roll);
            var (system, ecs) = CreateSystem(rng);
            var entity = CreateEntity(ecs);

            var result = system.AwardUseExperience(entity, XpSource.AbilityUse, new UseAwardContext(
                SubjectAbilityId: "kick"));

            var row = Assert.Single(result.Tracks);
            Assert.Equal(expectAward ? 5 : 0, row.AmountAwarded);
        }

        [Fact]
        public void Chance_decays_as_the_track_improves()
        {
            // BaseChance 0.25, decay 0.15 → at 4 improvements chance = 0.25 / 1.6 = 0.15625.
            // A roll of 0.2 passes at rank 0 and fails at rank 4.
            var atRankZero = RollAt(improvements: 0, roll: 0.2);
            var atRankFour = RollAt(improvements: 4, roll: 0.2);

            Assert.True(atRankZero > 0, "A 0.2 roll must pass at rank 0 (chance 0.25).");
            Assert.Equal(0, atRankFour);

            static int RollAt(int improvements, double roll)
            {
                var rng = new FakeRandom(new[] { 5 });
                rng.EnqueueDouble(roll);
                var ecs = new EntityService();
                var system = new ProgressionSystem(
                    ecs, rng, new PowerBudgetSystem(PowerBudgetTunables.Default), new AdvancementRuleRegistry());
                var entity = new EntityBuilder(ecs).AsPlayer().WithAttributes(10, 10, 10, 10).Build();

                var track = ProgressionTrack.Ability("kick");
                if (improvements > 0)
                {
                    // Seed rank by awarding straight through the accrual API, bypassing the roll.
                    // Thresholds are cumulative, so reaching rank N needs exactly threshold(N-1) XP.
                    var needed = ProgressionConstants.ThresholdBase
                                 + (improvements - 1) * ProgressionConstants.ThresholdIncrement;
                    system.AwardExperience(entity, track, needed, XpSource.AbilityUse);
                    Assert.Equal(improvements, system.GetImprovementCount(entity, track));
                }

                var before = system.GetXp(entity, track);
                system.AwardUseExperience(entity, XpSource.AbilityUse, new UseAwardContext(SubjectAbilityId: "kick"));
                return system.GetXp(entity, track) - before;
            }
        }

        [Fact]
        public void Two_slowing_curves_compose_deliberately_on_one_track()
        {
            // A track fed by a chance-gated source slows twice over: the XP threshold grows AND
            // the award chance decays. Pinned so the interaction stays deliberate — the XP needed
            // per rank rises while the chance of any given use contributing falls.
            var registry = new AdvancementRuleRegistry();
            var rule = registry.Get(XpSource.AbilityUse);

            double ChanceAt(int improvements)
                => rule.BaseChance / (1 + improvements * rule.ChanceDecayPerImprovement);

            int ThresholdAt(int improvements)
                => ProgressionConstants.ThresholdBase + improvements * ProgressionConstants.ThresholdIncrement;

            for (var i = 1; i <= 5; i++)
            {
                Assert.True(ChanceAt(i) < ChanceAt(i - 1), "Chance must decay with rank.");
                Assert.True(ThresholdAt(i) > ThresholdAt(i - 1), "Threshold must grow with rank.");
            }
        }

        // ── Scale composition (R6 + R7) ──────────────────────────────────────────

        [Fact]
        public void Global_source_and_content_scales_multiply_into_the_award()
        {
            var rng = new FakeRandom(new[] { 4 });
            rng.EnqueueDouble(0.0); // pass the chance roll
            var (system, ecs) = CreateSystem(rng);
            var entity = CreateEntity(ecs);

            var result = system.AwardUseExperience(entity, XpSource.AbilityUse, new UseAwardContext(
                SubjectAbilityId: "kick", ContentScale: 2.5));

            var rule = new AdvancementRuleRegistry().Get(XpSource.AbilityUse);
            var expected = (int)System.Math.Round(
                4 * ProgressionConstants.GlobalXpScalar * rule.SourceScale * 2.5,
                System.MidpointRounding.AwayFromZero);

            Assert.Equal(expected, Assert.Single(result.Tracks).AmountAwarded);
        }

        [Fact]
        public void A_zero_content_scale_awards_nothing()
        {
            var rng = new FakeRandom(new[] { 6 });
            rng.EnqueueDouble(0.0);
            var (system, ecs) = CreateSystem(rng);
            var entity = CreateEntity(ecs);

            var result = system.AwardUseExperience(entity, XpSource.AbilityUse, new UseAwardContext(
                SubjectAbilityId: "kick", ContentScale: 0.0));

            Assert.Equal(0, Assert.Single(result.Tracks).AmountAwarded);
        }

        [Fact]
        public void A_zero_mob_xp_scale_makes_that_mobs_kills_award_nothing()
        {
            var rng = new FakeRandom(new[] { 10, 10 });
            var (system, ecs) = CreateSystem(rng);
            var killer = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25);
            var victim = new EntityBuilder(ecs)
                .AsMob("worthless")
                .WithAttributes(25, 25, 25, 25)
                .Build();
            ecs.Get<Hedron.Core.ECS.Components.MobDataComponent>(victim)!.XpScale = 0.0;

            var result = system.AwardCombatExperience(killer, victim);

            Assert.All(result.Tracks, row => Assert.Equal(0, row.AmountAwarded));
        }

        // ── Track enumeration (feeds D3) ─────────────────────────────────────────

        [Fact]
        public void GetTrackedScores_excludes_ability_tracks()
        {
            var (system, ecs) = CreateSystem(new FakeRandom(seed: 1));
            var entity = CreateEntity(ecs);

            system.AwardExperience(entity, ScoreId.Body, 10, XpSource.CombatKill);
            system.AwardExperience(entity, ProgressionTrack.Ability("kick"), 10, XpSource.AbilityUse);

            Assert.Equal(new[] { ScoreId.Body }, system.GetTrackedScores(entity));
            Assert.Contains(ProgressionTrack.Ability("kick"), system.GetTrackedTracks(entity));
            Assert.Equal(2, system.GetTrackedTracks(entity).Count);
        }

        // ── Determinism (INV-26) ─────────────────────────────────────────────────

        [Fact]
        public void Same_scripted_rng_and_inputs_produce_identical_results()
        {
            (int TotalAwarded, int ImprovementCount) RunOnce()
            {
                var rng = new FakeRandom(new[] { 10, 10 });
                var (system, ecs) = CreateSystem(rng);
                var killer = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25);
                var victim = CreateEntity(ecs, mind: 25, body: 25, spirit: 25, attunement: 25);

                var result = system.AwardCombatExperience(killer, victim);
                return (result.Tracks.Sum(t => t.AmountAwarded), system.GetImprovementCount(killer, ScoreId.Body));
            }

            var run1 = RunOnce();
            var run2 = RunOnce();

            Assert.Equal(run1, run2);
        }
    }
}
