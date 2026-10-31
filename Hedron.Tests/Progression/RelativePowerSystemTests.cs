using System;
using System.Collections.Generic;
using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Progression;
using Hedron.Core.Modules.Progression.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Progression
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="RelativePowerSystem"/>, plus the cut-point
    /// ordering and no-cycle architecture guards.
    ///
    /// Coverage contract: docs/implementation-plans/relative-power-consider.md WP-1 — effective
    /// power on both sides through a real <see cref="StatSystem"/> with the real contributors
    /// (Post 3, 15), HP-independence (Post 3), the ratio and power overload, inclusive label
    /// boundaries with Trivial ≡ below <see cref="ProgressionConstants.AntiGrindFloorRatio"/>
    /// (Post 4), the readability contract (Post 11), and the SR7 band-midpoint gradient (Post 5).
    /// </summary>
    public sealed class RelativePowerSystemTests
    {
        private sealed class TestWorld
        {
            public EntityService Ecs { get; } = new();
            public IStatSystem Stats { get; }
            public IEffectSystem Effects { get; }
            public IProgressionSystem Progression { get; }
            public IPowerBudgetSystem PowerBudget { get; } = new PowerBudgetSystem(PowerBudgetTunables.Default);
            public RelativePowerSystem RelativePower { get; }

            // Real EffectSystem with the production equipment + progression contributors, feeding
            // a real StatSystem — so GetEffectivePower sees exactly what IStatSystem.Get folds in DI.
            public TestWorld()
            {
                Progression = new ProgressionSystem(Ecs, new FakeRandom(seed: 1), PowerBudget, new AdvancementRuleRegistry());
                Effects = new EffectSystem(Ecs, new IEffectContributor[]
                {
                    new EquipmentEffectContributor(Ecs),
                    new ProgressionEffectContributor(Progression),
                });
                var attributes = new AttributeSystem(Ecs, Effects, Options.Create(new DeathOptions { HpFloor = -10 }));
                Stats = new StatSystem(attributes, Effects);
                RelativePower = new RelativePowerSystem(Ecs, Stats, PowerBudget);
            }

            /// <summary>Default attributes (10s) and pools (100 HP) — the T0 reference build.</summary>
            public uint FreshPlayer() => new EntityBuilder(Ecs).AsPlayer().WithAttributes().WithPools().Build();

            public uint Mob(int hp = 100, int tier = 0)
            {
                var id = new EntityBuilder(Ecs).AsMob("wolf").WithAttributes().WithPools(hp: hp).Build();
                Ecs.Get<MobDataComponent>(id).Tier = tier;
                return id;
            }

            public PowerSnapshot Snapshot(uint entityId) => new(
                ((ScoreId[])Enum.GetValues(typeof(ScoreId))).ToDictionary(s => s, s => Stats.Get(entityId, s)));
        }

        /// <summary><see cref="IStatSystem"/> that reads every score as 0 — drives subject power to 0.</summary>
        private sealed class ZeroStatSystem : IStatSystem
        {
            public int Get(uint entityId, ScoreId score) => 0;
            public int GetEffectiveMind(uint entityId) => 0;
            public int GetEffectiveBody(uint entityId) => 0;
            public int GetEffectiveSpirit(uint entityId) => 0;
            public int GetEffectiveAttunement(uint entityId) => 0;
            public int GetEffectiveAttackPower(uint entityId) => 0;
            public int GetEffectiveDefense(uint entityId) => 0;
            public int GetCurrentHp(uint entityId) => 0;
            public int GetMaxHp(uint entityId) => 0;
        }

        // ── GetEffectivePower (Post 15) ──────────────────────────────────────────

        [Fact]
        public void GetEffectivePower_for_a_player_is_the_stat_snapshot_estimated_at_tier_0()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();

            var power = world.RelativePower.GetEffectivePower(player);

            // golden: Mind 10 + Body 10×10 + Spirit 10 + Attunement 10 + HpMax 100×2 + AttackPower 5×8 + Defense 2×8
            Assert.Equal(386, power);
            Assert.Equal(world.PowerBudget.Estimate(world.Snapshot(player), 0), power);
        }

        [Fact]
        public void GetEffectivePower_for_a_mob_estimates_with_its_authored_tier()
        {
            var world = new TestWorld();
            var mob = world.Mob(tier: 1);

            var power = world.RelativePower.GetEffectivePower(mob);

            // golden: the 386 reference build + tier 1 × (w(Body) + w(HpMax)) × TierBaselineStep = +120
            Assert.Equal(506, power);
            Assert.Equal(world.PowerBudget.Estimate(world.Snapshot(mob), 1), power);
        }

        [Fact]
        public void Worn_gear_raises_effective_power()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();
            var before = world.RelativePower.GetEffectivePower(player);

            var weapon = world.Ecs.CreateEntity().Id;
            world.Ecs.AddComponent(weapon, new ItemDataComponent
            {
                Name = "blade",
                StatBonuses = { new EquipmentStatBonus(ScoreId.AttackPower, 5) },
            });
            world.Ecs.AddComponent(player, new EquipmentComponent { Slots = { [Hedron.Core.WornSlot.MainHand] = weapon } });

            Assert.Equal(before + 5 * PowerBudgetTunables.Default.Weights[ScoreId.AttackPower],
                world.RelativePower.GetEffectivePower(player));
        }

        [Fact]
        public void A_progression_improvement_raises_effective_power()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();
            var before = world.RelativePower.GetEffectivePower(player);

            world.Progression.AwardExperience(player, ScoreId.Body, ProgressionConstants.ThresholdBase, XpSource.CombatKill);

            Assert.True(world.RelativePower.GetEffectivePower(player) > before);
        }

        [Fact]
        public void An_active_buff_raises_effective_power_and_is_recomputed_on_every_read()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();
            var mob = world.Mob();
            var before = world.RelativePower.Relate(player, mob);

            world.Effects.Apply(player, new EffectDefinition(
                EffectId: "buff.body",
                Kind: EffectKind.StatModifier,
                Params: new EffectParams(TargetScore: ScoreId.Body, BaseMagnitude: 5),
                Category: EffectCategory.Buff,
                PowerScalingFormula: "fixed",
                Duration: -1f,
                Stacking: StackPolicy.Stack,
                Phase: EffectPhase.Normal), sourceEntityId: player);

            var after = world.RelativePower.Relate(player, mob);
            Assert.True(after.SubjectPower > before.SubjectPower);
            Assert.True(after.Ratio < before.Ratio);   // nothing cached between reads (SR4)
        }

        [Fact]
        public void A_wounded_mob_reads_the_same_as_at_full_hp()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();
            var mob = world.Mob();
            var fresh = world.RelativePower.Relate(player, mob);

            world.Ecs.Get<PoolsComponent>(mob).CurrentHp = 1;

            Assert.Equal(fresh, world.RelativePower.Relate(player, mob));
        }

        // ── Relate ───────────────────────────────────────────────────────────────

        [Fact]
        public void Relate_ratio_is_target_power_over_subject_power()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();
            var mob = world.Mob(hp: 150);

            var reading = world.RelativePower.Relate(player, mob);

            Assert.True(reading.IsReadable);
            Assert.Equal(world.RelativePower.GetEffectivePower(player), reading.SubjectPower);
            Assert.Equal(world.RelativePower.GetEffectivePower(mob), reading.TargetPower);
            Assert.Equal((double)reading.TargetPower / reading.SubjectPower, reading.Ratio);
            Assert.Equal(world.RelativePower.Classify(reading.Ratio), reading.Label);
        }

        [Fact]
        public void The_power_overload_equals_the_entity_overload_at_the_same_power()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();
            var mob = world.Mob(hp: 130);

            var byEntity = world.RelativePower.Relate(player, mob);
            var byPower = world.RelativePower.Relate(player, world.RelativePower.GetEffectivePower(mob));

            Assert.Equal(byEntity, byPower);
        }

        [Fact]
        public void The_power_overload_clamps_a_negative_target_power_to_zero()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();

            var reading = world.RelativePower.Relate(player, targetPower: -50);

            Assert.True(reading.IsReadable);
            Assert.Equal(0, reading.TargetPower);
            Assert.Equal(0.0, reading.Ratio);
            Assert.Equal(RelativePowerLabel.Trivial, reading.Label);
        }

        // ── Classify boundaries (Post 4) ─────────────────────────────────────────

        public static IEnumerable<object[]> CutPoints() => new[]
        {
            new object[] { ProgressionConstants.AntiGrindFloorRatio, RelativePowerLabel.Trivial, RelativePowerLabel.Easy },
            new object[] { ProgressionConstants.RelativeEvenFloorRatio, RelativePowerLabel.Easy, RelativePowerLabel.Even },
            new object[] { ProgressionConstants.RelativeDangerousFloorRatio, RelativePowerLabel.Even, RelativePowerLabel.Dangerous },
            new object[] { ProgressionConstants.RelativeDeadlyFloorRatio, RelativePowerLabel.Dangerous, RelativePowerLabel.Deadly },
        };

        [Theory]
        [MemberData(nameof(CutPoints))]
        public void Classify_uses_inclusive_lower_bounds(double cutPoint, RelativePowerLabel below, RelativePowerLabel atOrAbove)
        {
            var system = new TestWorld().RelativePower;

            Assert.Equal(below, system.Classify(cutPoint - 1e-9));
            Assert.Equal(atOrAbove, system.Classify(cutPoint));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(ProgressionConstants.AntiGrindFloorRatio - 1e-9)]
        [InlineData(ProgressionConstants.AntiGrindFloorRatio)]
        [InlineData(ProgressionConstants.AntiGrindFloorRatio + 1e-9)]
        [InlineData(1.0)]
        public void Trivial_is_exactly_below_the_anti_grind_floor(double ratio)
        {
            // One constant, one meaning (SR5): the Trivial boundary IS AntiGrindFloorRatio — the
            // value ProgressionSystem's anti-grind floor makes ineligible below.
            var system = new TestWorld().RelativePower;

            Assert.Equal(ratio < ProgressionConstants.AntiGrindFloorRatio,
                system.Classify(ratio) == RelativePowerLabel.Trivial);
        }

        // ── Readability (Post 11) ────────────────────────────────────────────────

        [Fact]
        public void A_target_without_AttributesComponent_is_unreadable()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();
            var stub = new EntityBuilder(world.Ecs).AsMob("ghost").WithPools().Build();

            Assert.Equal(RelativePowerReading.Unreadable, world.RelativePower.Relate(player, stub));
        }

        [Fact]
        public void A_subject_without_AttributesComponent_is_unreadable_on_both_overloads()
        {
            var world = new TestWorld();
            var subject = new EntityBuilder(world.Ecs).AsPlayer().WithPools().Build();
            var mob = world.Mob();

            Assert.Equal(RelativePowerReading.Unreadable, world.RelativePower.Relate(subject, mob));
            Assert.Equal(RelativePowerReading.Unreadable, world.RelativePower.Relate(subject, targetPower: 400));
        }

        [Fact]
        public void A_subject_with_no_power_is_unreadable_and_never_divides_by_zero()
        {
            var ecs = new EntityService();
            var system = new RelativePowerSystem(ecs, new ZeroStatSystem(), new PowerBudgetSystem(PowerBudgetTunables.Default));
            var subject = new EntityBuilder(ecs).AsPlayer().WithAttributes().Build();
            var target = new EntityBuilder(ecs).AsMob("wolf").WithAttributes().Build();

            var reading = system.Relate(subject, target);

            Assert.False(reading.IsReadable);
            Assert.Equal(0.0, reading.Ratio);
            Assert.Null(reading.Label);
        }

        // ── SR7 gradient (Post 5) ────────────────────────────────────────────────

        [Fact]
        public void A_fresh_character_reads_the_T0_B1_midpoint_as_even_or_easier_and_B3_as_dangerous_or_worse()
        {
            var world = new TestWorld();
            var player = world.FreshPlayer();

            var b1 = world.RelativePower.Relate(player, MidpointMob(world, player, band: 1));
            var b3 = world.RelativePower.Relate(player, MidpointMob(world, player, band: 3));

            Assert.True(b1.Label <= RelativePowerLabel.Even, $"B1 midpoint read {b1.Label} (ratio {b1.Ratio:0.000})");
            Assert.True(b3.Label >= RelativePowerLabel.Dangerous, $"B3 midpoint read {b3.Label} (ratio {b3.Ratio:0.000})");
        }

        /// <summary>
        /// A synthetic tier-0 mob whose effective power lands on the oracle's
        /// <c>TargetRange(0, band)</c> midpoint: the fresh reference build with MaxHp shifted by
        /// the power delta ÷ w(HpMax). Computed from the oracle, never a copied number.
        /// </summary>
        private static uint MidpointMob(TestWorld world, uint freshPlayer, int band)
        {
            var range = world.PowerBudget.TargetRange(0, band);
            var midpoint = (range.MinPower + range.MaxPower) / 2;
            var hpWeight = PowerBudgetTunables.Default.Weights[ScoreId.HpMax];
            var delta = midpoint - world.RelativePower.GetEffectivePower(freshPlayer);

            var mob = world.Mob(hp: 100 + (int)Math.Round((double)delta / hpWeight));
            var power = world.RelativePower.GetEffectivePower(mob);

            Assert.InRange(power, range.MinPower, range.MaxPower);
            Assert.True(Math.Abs(power - midpoint) < hpWeight, $"mob power {power} is not at midpoint {midpoint}");
            return mob;
        }

        // ── Architecture guards ──────────────────────────────────────────────────

        [Fact]
        public void Cut_points_are_strictly_ordered_above_the_anti_grind_floor()
        {
            Assert.True(ProgressionConstants.AntiGrindFloorRatio < ProgressionConstants.RelativeEvenFloorRatio);
            Assert.True(ProgressionConstants.RelativeEvenFloorRatio < ProgressionConstants.RelativeDangerousFloorRatio);
            Assert.True(ProgressionConstants.RelativeDangerousFloorRatio < ProgressionConstants.RelativeDeadlyFloorRatio);
        }

        [Fact]
        public void RelativePowerSystem_depends_on_exactly_EntityService_IStatSystem_and_IPowerBudgetSystem()
        {
            // No IProgressionSystem (or anything else) — the read must never close a cycle back
            // through the IStatSystem → contributors → IProgressionSystem chain.
            var parameters = typeof(RelativePowerSystem).GetConstructors()
                .Single()
                .GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();

            Assert.Equal(new[] { typeof(EntityService), typeof(IStatSystem), typeof(IPowerBudgetSystem) }, parameters);
            Assert.DoesNotContain(typeof(IProgressionSystem), parameters);
        }
    }
}
