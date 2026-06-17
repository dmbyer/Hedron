using System.Collections.Generic;
using System.Linq;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Attributes.Systems;
using Hedron.Core.Modules.Death;
using Hedron.Core.Modules.Effects;
using Hedron.Core.Modules.Effects.Systems;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.Stats.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Items
{
    /// <summary>
    /// Tier 1 — unit tests for <see cref="EquipmentEffectContributor"/> (the WhileEquipped INV-24
    /// contributor that folds worn-gear stat bonuses into the effect modifier pipeline) plus the
    /// end-to-end fold through <see cref="StatSystem.Get"/> (T-U1…T-U4) and synthetic-effect
    /// emission (T-U5). This is the home for equipment→stat coverage after the DamageBonus migration.
    /// </summary>
    public sealed class EquipmentEffectContributorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static uint MakeGear(EntityService ecs, params EquipmentStatBonus[] bonuses)
        {
            var item = ecs.CreateEntity();
            ecs.AddComponent(item.Id, new ItemDataComponent
            {
                Name = "gear",
                StatBonuses = bonuses.ToList(),
            });
            return item.Id;
        }

        private static EquipmentComponent Worn(params (WornSlot slot, uint item)[] entries)
        {
            var comp = new EquipmentComponent();
            foreach (var (slot, item) in entries)
                comp.Slots[slot] = item;
            return comp;
        }

        /// <summary>Real EffectSystem wired with the equipment contributor, feeding a real StatSystem.</summary>
        private static (StatSystem stats, EntityService ecs) BuildStatsWithEquipment()
        {
            var ecs = new EntityService();
            var effects = new EffectSystem(ecs, new IEffectContributor[] { new EquipmentEffectContributor(ecs) });
            var deathOpts = Options.Create(new DeathOptions { HpFloor = -10 });
            var attributes = new AttributeSystem(ecs, effects, deathOpts);
            var stats = new StatSystem(attributes, effects);
            return (stats, ecs);
        }

        // ── T-U1 — GetModifiers ─────────────────────────────────────────────────

        [Fact]
        public void GetModifiers_returns_weapon_AttackPower_bonus_and_zero_for_other_scores()
        {
            var ecs = new EntityService();
            var contributor = new EquipmentEffectContributor(ecs);
            var weapon = MakeGear(ecs, new EquipmentStatBonus(ScoreId.AttackPower, 6));
            var wearer = new EntityBuilder(ecs).AsPlayer().Wielding(weapon).Build();

            Assert.Equal(6, contributor.GetModifiers(wearer, ScoreId.AttackPower));
            Assert.Equal(0, contributor.GetModifiers(wearer, ScoreId.Defense));
        }

        [Fact]
        public void GetModifiers_returns_zero_when_no_EquipmentComponent()
        {
            var ecs = new EntityService();
            var contributor = new EquipmentEffectContributor(ecs);
            var bare = new EntityBuilder(ecs).AsPlayer().Build();

            Assert.Equal(0, contributor.GetModifiers(bare, ScoreId.AttackPower));
        }

        // ── T-U2 — multi-slot armor summation (Chest/Feet/Head, all pre-existing slots) ──

        [Fact]
        public void GetModifiers_sums_Defense_across_multiple_armor_slots()
        {
            var ecs = new EntityService();
            var contributor = new EquipmentEffectContributor(ecs);
            var chest = MakeGear(ecs, new EquipmentStatBonus(ScoreId.Defense, 2));
            var feet = MakeGear(ecs, new EquipmentStatBonus(ScoreId.Defense, 3));
            var head = MakeGear(ecs, new EquipmentStatBonus(ScoreId.Defense, 1));
            var wearer = new EntityBuilder(ecs).AsPlayer()
                .With(Worn((WornSlot.Chest, chest), (WornSlot.Feet, feet), (WornSlot.Head, head)))
                .Build();

            Assert.Equal(6, contributor.GetModifiers(wearer, ScoreId.Defense));
        }

        [Fact]
        public void GetModifiers_counts_a_two_slot_item_once()
        {
            var ecs = new EntityService();
            var contributor = new EquipmentEffectContributor(ecs);
            // A two-hand weapon occupies MainHand + OffHand but is one entity; its bonus counts once.
            var greatsword = MakeGear(ecs, new EquipmentStatBonus(ScoreId.AttackPower, 7));
            var wearer = new EntityBuilder(ecs).AsPlayer()
                .With(Worn((WornSlot.MainHand, greatsword), (WornSlot.OffHand, greatsword)))
                .Build();

            Assert.Equal(7, contributor.GetModifiers(wearer, ScoreId.AttackPower));
        }

        // ── T-U3 — Get(AttackPower) folds the contributor ───────────────────────

        [Fact]
        public void Get_AttackPower_folds_weapon_bonus_over_base()
        {
            var (stats, ecs) = BuildStatsWithEquipment();
            var weapon = MakeGear(ecs, new EquipmentStatBonus(ScoreId.AttackPower, 6));
            var wearer = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 10).Wielding(weapon).Build();

            // Base Body/2 = 5; contributor adds 6.
            Assert.Equal(11, stats.Get(wearer, ScoreId.AttackPower));
            // The bare getter is base-only and must NOT see the gear bonus.
            Assert.Equal(5, stats.GetEffectiveAttackPower(wearer));
        }

        // ── T-U4 — Get(Defense) folds armor ─────────────────────────────────────

        [Fact]
        public void Get_Defense_folds_armor_bonus_over_base()
        {
            var (stats, ecs) = BuildStatsWithEquipment();
            var breastplate = MakeGear(ecs, new EquipmentStatBonus(ScoreId.Defense, 4));
            var wearer = new EntityBuilder(ecs).AsPlayer().WithAttributes(body: 20)
                .With(Worn((WornSlot.Chest, breastplate)))
                .Build();

            // Base Body/4 = 5; contributor adds 4.
            Assert.Equal(9, stats.Get(wearer, ScoreId.Defense));
            Assert.Equal(5, stats.GetEffectiveDefense(wearer));
        }

        // ── T-U5 — GetActive emits one WhileEquipped StatModifier per row ───────

        [Fact]
        public void GetActive_yields_one_WhileEquipped_StatModifier_per_bonus_row()
        {
            var ecs = new EntityService();
            var contributor = new EquipmentEffectContributor(ecs);
            var gear = MakeGear(ecs,
                new EquipmentStatBonus(ScoreId.AttackPower, 6),
                new EquipmentStatBonus(ScoreId.Defense, 2));
            var wearer = new EntityBuilder(ecs).AsPlayer().Wielding(gear).Build();

            var active = contributor.GetActive(wearer).ToList();

            Assert.Equal(2, active.Count);
            Assert.All(active, e =>
            {
                Assert.Equal(EffectKind.StatModifier, e.Kind);
                Assert.Equal(EffectLifetime.WhileEquipped, e.Lifetime);
                Assert.Equal(e.Params.BaseMagnitude, e.Power); // Power == authored magnitude
                Assert.Equal(gear, e.Source.EntityId);
            });
            Assert.Contains(active, e => e.Params.TargetScore == ScoreId.AttackPower && e.Power == 6);
            Assert.Contains(active, e => e.Params.TargetScore == ScoreId.Defense && e.Power == 2);
        }

        [Fact]
        public void GetActive_returns_empty_when_no_EquipmentComponent()
        {
            var ecs = new EntityService();
            var contributor = new EquipmentEffectContributor(ecs);
            var bare = new EntityBuilder(ecs).AsPlayer().Build();

            Assert.Empty(contributor.GetActive(bare));
        }
    }
}
