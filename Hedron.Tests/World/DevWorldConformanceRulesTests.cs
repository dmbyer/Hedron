using System;
using System.IO;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.BalanceInspection.Systems;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.Shopping.Components;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Hedron.Core.Modules.Stats;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="DevWorldConformanceRules"/> over a hand-built
    /// <see cref="TemplateRegistry"/> (no disk, except the scenario rule's temp folder). Each rule
    /// gets one violating and one passing case, so a predicate that matches nothing (vacuously
    /// true) can't pass on real content unnoticed.
    /// </summary>
    public sealed class DevWorldConformanceRulesTests
    {
        private readonly TemplateRegistry _registry = new(new EntityService());
        private RegistryPlacementSource Placement => new(_registry);
        private static PowerBudgetTunables Tunables => PowerBudgetTunables.Default;

        // ── Builders ─────────────────────────────────────────────────────────────

        private void Area(string id, AreaDesignation? designation = null) =>
            _registry.Register(id, new AreaTemplate(id) { AreaId = id, Name = id, Designation = designation });

        private void Room(string id, string? area, params (string Blueprint, int Count)[] rules)
        {
            var room = new RoomTemplate(id) { Name = id, AreaId = area ?? string.Empty };
            var n = 0;
            foreach (var (blueprint, count) in rules)
                room.SpawnRules.Add(new SpawnRule($"r{++n}", blueprint, count, 60));
            _registry.Register(id, room);
        }

        private MobTemplate Mob(string id, int tier = 0, int band = 1, int coinMin = 1, int coinMax = 3,
            ProtectionFlags protection = ProtectionFlags.None, bool shop = false)
        {
            var mob = new MobTemplate(id) { Name = id, Tier = tier, Band = band, Protection = protection, IsShop = shop };
            if (coinMax > 0 || coinMin > 0)
                mob.CurrencyLoot[CurrencyId.Coin] = (coinMin, coinMax);
            _registry.Register(id, mob);
            return mob;
        }

        private ItemTemplate Item(string id, long value = 5, int band = 0, bool powered = false)
        {
            var item = new ItemTemplate(id) { Name = id, Value = value, Band = band };
            if (powered)
                item.StatBonuses.Add(new EquipmentStatBonus(ScoreId.AttackPower, 1));
            _registry.Register(id, item);
            return item;
        }

        private static AreaDesignation Band1 => AreaDesignation.Of(0, 1, 1);

        // ── EveryHuntingAreaIsDesignated ─────────────────────────────────────────

        [Fact]
        public void Undesignated_area_placing_a_targetable_mob_is_a_violation()
        {
            Area("area.a");
            Mob("mob.rat");
            Room("room.a", "area.a", ("mob.rat", 1));

            Assert.NotEmpty(DevWorldConformanceRules.EveryHuntingAreaIsDesignated(_registry, Placement));
        }

        [Fact]
        public void Hunting_mob_in_an_area_less_room_is_a_violation()
        {
            Mob("mob.rat");
            Room("room.void", null, ("mob.rat", 1));

            Assert.NotEmpty(DevWorldConformanceRules.EveryHuntingAreaIsDesignated(_registry, Placement));
        }

        [Fact]
        public void Designated_hunting_area_and_an_untargetable_shopkeeper_in_an_undesignated_hub_conform()
        {
            Area("area.hub");
            Area("area.field", Band1);
            Mob("mob.rat");
            Mob("mob.keeper", band: 0, coinMax: 0, protection: ProtectionFlags.Untargetable | ProtectionFlags.EffectImmune, shop: true);
            Room("room.store", "area.hub", ("mob.keeper", 1));
            Room("room.field", "area.field", ("mob.rat", 2));

            Assert.Empty(DevWorldConformanceRules.EveryHuntingAreaIsDesignated(_registry, Placement));
        }

        // ── EveryHuntingMobIsBanded ──────────────────────────────────────────────

        [Fact]
        public void Unbanded_hunting_mob_in_a_designated_area_is_a_violation()
        {
            Area("area.field", Band1);
            Mob("mob.rat", band: 0);
            Room("room.field", "area.field", ("mob.rat", 1));

            Assert.NotEmpty(DevWorldConformanceRules.EveryHuntingMobIsBanded(_registry, Placement));
        }

        [Fact]
        public void Banded_hunting_mob_conforms()
        {
            Area("area.field", Band1);
            Mob("mob.rat");
            Room("room.field", "area.field", ("mob.rat", 1));

            Assert.Empty(DevWorldConformanceRules.EveryHuntingMobIsBanded(_registry, Placement));
        }

        // ── AtLeastOneDesignatedHuntingArea ──────────────────────────────────────

        [Fact]
        public void Empty_world_violates_non_vacuity()
        {
            Assert.NotEmpty(DevWorldConformanceRules.AtLeastOneDesignatedHuntingArea(_registry, Placement, Tunables));
        }

        [Fact]
        public void One_designated_hunting_area_satisfies_non_vacuity()
        {
            Area("area.field", Band1);
            Mob("mob.rat");
            Room("room.field", "area.field", ("mob.rat", 1));

            Assert.Empty(DevWorldConformanceRules.AtLeastOneDesignatedHuntingArea(_registry, Placement, Tunables));
            Assert.Equal(new[] { new PowerBand(0, 1) }, DevWorldConformanceRules.BandList(_registry, Placement, Tunables));
        }

        // ── EachDesignatedHuntingAreaHasThreeMobsInCell ──────────────────────────

        [Fact]
        public void Two_mobs_in_a_designated_area_is_a_violation()
        {
            Area("area.field", Band1);
            Mob("mob.rat");
            Mob("mob.snake");
            Room("room.field", "area.field", ("mob.rat", 2), ("mob.snake", 1));

            Assert.NotEmpty(DevWorldConformanceRules.EachDesignatedHuntingAreaHasThreeMobsInCell(_registry, Placement));
        }

        [Fact]
        public void Three_mobs_with_one_outside_the_designation_is_a_violation()
        {
            Area("area.field", Band1);
            Mob("mob.rat");
            Mob("mob.snake");
            Mob("mob.bear", band: 2);
            Room("room.field", "area.field", ("mob.rat", 1), ("mob.snake", 1), ("mob.bear", 1));

            Assert.NotEmpty(DevWorldConformanceRules.EachDesignatedHuntingAreaHasThreeMobsInCell(_registry, Placement));
        }

        [Fact]
        public void Three_distinct_in_cell_mobs_across_rooms_conform()
        {
            Area("area.field", Band1);
            Mob("mob.rat");
            Mob("mob.snake");
            Mob("mob.crow");
            Room("room.field.a", "area.field", ("mob.rat", 1), ("mob.snake", 1));
            Room("room.field.b", "area.field", ("mob.crow", 1), ("mob.rat", 1));

            Assert.Empty(DevWorldConformanceRules.EachDesignatedHuntingAreaHasThreeMobsInCell(_registry, Placement));
        }

        // ── EveryHuntingMobHasCoinLoot ───────────────────────────────────────────

        [Fact]
        public void Hunting_mob_without_coin_loot_is_a_violation()
        {
            Area("area.field", Band1);
            Mob("mob.rat", coinMin: 0, coinMax: 0);
            Room("room.field", "area.field", ("mob.rat", 1));

            Assert.NotEmpty(DevWorldConformanceRules.EveryHuntingMobHasCoinLoot(_registry, Placement));
        }

        [Fact]
        public void Hunting_mob_with_an_inverted_coin_range_is_a_violation()
        {
            Area("area.field", Band1);
            Mob("mob.rat", coinMin: 5, coinMax: 2);
            Room("room.field", "area.field", ("mob.rat", 1));

            Assert.NotEmpty(DevWorldConformanceRules.EveryHuntingMobHasCoinLoot(_registry, Placement));
        }

        [Fact]
        public void Hunting_mob_with_a_coin_range_conforms()
        {
            Area("area.field", Band1);
            Mob("mob.rat", coinMin: 1, coinMax: 3);
            Room("room.field", "area.field", ("mob.rat", 1));

            Assert.Empty(DevWorldConformanceRules.EveryHuntingMobHasCoinLoot(_registry, Placement));
        }

        // ── CoinLootRisesByBand ──────────────────────────────────────────────────

        [Fact]
        public void Band_two_with_a_lower_mean_coin_midpoint_than_band_one_is_a_violation()
        {
            Area("area.b1", Band1);
            Area("area.b2", AreaDesignation.Of(0, 2, 2));
            Mob("mob.rat", band: 1, coinMin: 4, coinMax: 6);
            Mob("mob.wolf", band: 2, coinMin: 1, coinMax: 3);
            Room("room.b1", "area.b1", ("mob.rat", 1));
            Room("room.b2", "area.b2", ("mob.wolf", 1));

            Assert.NotEmpty(DevWorldConformanceRules.CoinLootRisesByBand(_registry, Placement, Tunables));
        }

        [Fact]
        public void Coin_rising_from_band_one_to_band_two_conforms()
        {
            Area("area.b1", Band1);
            Area("area.b2", AreaDesignation.Of(0, 2, 2));
            Mob("mob.rat", band: 1, coinMin: 1, coinMax: 3);
            Mob("mob.wolf", band: 2, coinMin: 4, coinMax: 8);
            Room("room.b1", "area.b1", ("mob.rat", 1));
            Room("room.b2", "area.b2", ("mob.wolf", 1));

            Assert.Empty(DevWorldConformanceRules.CoinLootRisesByBand(_registry, Placement, Tunables));
        }

        // ── EveryFoundAndShopItemHasValue ────────────────────────────────────────

        [Fact]
        public void Found_junk_with_value_zero_is_a_violation()
        {
            Area("area.field", Band1);
            Item("item.junk", value: 0);
            Room("room.field", "area.field", ("item.junk", 1));

            Assert.NotEmpty(DevWorldConformanceRules.EveryFoundAndShopItemHasValue(_registry, Placement));
        }

        [Fact]
        public void Shop_row_naming_an_unknown_item_is_a_violation()
        {
            var keeper = Mob("mob.keeper", band: 0, coinMax: 0, protection: ProtectionFlags.Untargetable, shop: true);
            keeper.ShopBaseStock.Add(new ShopStockRow { BlueprintId = "item.nowhere", Quantity = 1 });

            Assert.NotEmpty(DevWorldConformanceRules.EveryFoundAndShopItemHasValue(_registry, Placement));
        }

        [Fact]
        public void Valued_found_and_shop_items_conform()
        {
            Area("area.field", Band1);
            Item("item.junk", value: 3);
            Item("item.sword", value: 40, band: 1, powered: true);
            Room("room.field", "area.field", ("item.junk", 1));
            var keeper = Mob("mob.keeper", band: 0, coinMax: 0, protection: ProtectionFlags.Untargetable, shop: true);
            keeper.ShopBaseStock.Add(new ShopStockRow { BlueprintId = "item.sword", Quantity = 1 });

            Assert.Empty(DevWorldConformanceRules.EveryFoundAndShopItemHasValue(_registry, Placement));
        }

        // ── EveryPoweredItemIsBanded ─────────────────────────────────────────────

        [Fact]
        public void Untagged_item_with_stat_bonuses_is_a_violation()
        {
            Item("item.stick", band: 0, powered: true);

            Assert.NotEmpty(DevWorldConformanceRules.EveryPoweredItemIsBanded(_registry));
        }

        [Fact]
        public void Banded_powered_item_and_untagged_junk_conform()
        {
            Item("item.stick", band: 1, powered: true);
            Item("item.junk");

            Assert.Empty(DevWorldConformanceRules.EveryPoweredItemIsBanded(_registry));
        }

        // ── BandDriftAuditIsClean ────────────────────────────────────────────────

        private BalanceAuditSystem Audit() => new(
            _registry, new PowerBudgetSystem(Tunables), new ItemPowerProjectionSystem(),
            new MobPowerProjectionSystem(), Tunables, bandDriftTolerance: 1);

        [Fact]
        public void Item_drifted_past_tolerance_is_a_violation()
        {
            // Tagged Tier 0 Band 1, but carries far more power than that cell.
            var item = Item("item.drifted", band: 1);
            item.StatBonuses.Add(new EquipmentStatBonus(ScoreId.AttackPower, 60));
            item.StatBonuses.Add(new EquipmentStatBonus(ScoreId.Defense, 40));

            Assert.NotEmpty(DevWorldConformanceRules.BandDriftAuditIsClean(Audit()));
        }

        [Fact]
        public void Empty_registry_audits_clean()
        {
            Assert.Empty(DevWorldConformanceRules.BandDriftAuditIsClean(Audit()));
        }

        // ── EveryCommittedScenarioResolves ───────────────────────────────────────

        private static (ISimScenarioStore Store, string Dir) ScenarioFolder(string yaml)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"hedron-devworld-sim-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scenario.yaml"), yaml);
            var store = new SimScenarioStore(
                new ISimCombatantPolicy[] { new MeleeOnlyPolicy(), new RoundRobinPolicy(), new CooldownFirstPolicy(new AbilityRegistry()) },
                Options.Create(new SimulationOptions { ScenarioDirectory = dir }));
            return (store, dir);
        }

        private const string MobVsReference = """
            kind: Combat
            name: mob-vs-reference
            seed: 1
            iterations: 10
            maxTicksPerRun: 50
            sides:
              - combatants:
                  - source: ReferenceBuild
                    tier: 0
                    band: 1
                    policyId: melee-only
              - combatants:
                  - source: MobTemplate
                    mobBlueprintId: mob.rat
                    policyId: melee-only
            """;

        [Fact]
        public void Scenario_naming_a_missing_mob_is_a_violation()
        {
            var (store, dir) = ScenarioFolder(MobVsReference);
            try
            {
                Assert.NotEmpty(DevWorldConformanceRules.EveryCommittedScenarioResolves(_registry, store, dir));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Unloadable_scenario_is_a_violation()
        {
            var (store, dir) = ScenarioFolder("kind: NotAKind\nname: broken\n");
            try
            {
                Assert.NotEmpty(DevWorldConformanceRules.EveryCommittedScenarioResolves(_registry, store, dir));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Scenario_naming_an_existing_mob_conforms()
        {
            Mob("mob.rat");
            var (store, dir) = ScenarioFolder(MobVsReference);
            try
            {
                Assert.Empty(DevWorldConformanceRules.EveryCommittedScenarioResolves(_registry, store, dir));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }
}
