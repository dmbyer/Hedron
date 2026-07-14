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
    /// docs/roadmap/completed/progression-substrate.md (WP-1).
    /// </summary>
    public sealed class ProgressionSystemTests
    {
        private static (ProgressionSystem System, EntityService Ecs) CreateSystem(FakeRandom rng)
        {
            var ecs = new EntityService();
            var system = new ProgressionSystem(ecs, rng, new PowerBudgetSystem(PowerBudgetTunables.Default));
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
