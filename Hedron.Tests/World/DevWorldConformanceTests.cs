using System.Collections.Generic;
using Hedron.Core.Modules.Abilities;
using Hedron.Core.Modules.Simulation;
using Hedron.Core.Modules.Simulation.Systems;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Content-fixture guard for the dev-world baseline content policy (Phase 5 content drops, D20):
    /// every rule in <see cref="DevWorldConformanceRules"/> run over the committed
    /// <c>content/dev-world/</c>, booted once through the real loader by
    /// <see cref="DevWorldContentFixture"/>. Rules derive hunting areas and bands from content, so
    /// later drops pass unedited. Already guarded in <see cref="DevWorldFixtureTests"/> and not
    /// repeated here: connectivity, the out-of-designation check, the non-PvP hub, grid coordinates.
    /// </summary>
    public sealed class DevWorldConformanceTests : IClassFixture<DevWorldContentFixture>
    {
        private readonly DevWorldContentFixture _world;

        public DevWorldConformanceTests(DevWorldContentFixture world)
        {
            _world = world;
        }

        private static void AssertConforms(IReadOnlyList<string> violations) =>
            Assert.True(violations.Count == 0, string.Join("\n", violations));

        [Fact]
        public void Every_hunting_area_is_designated() =>
            AssertConforms(DevWorldConformanceRules.EveryHuntingAreaIsDesignated(_world.Registry, _world.Placement));

        [Fact]
        public void Every_hunting_mob_is_banded() =>
            AssertConforms(DevWorldConformanceRules.EveryHuntingMobIsBanded(_world.Registry, _world.Placement));

        [Fact]
        public void Each_designated_hunting_area_has_three_mobs_in_cell() =>
            AssertConforms(DevWorldConformanceRules.EachDesignatedHuntingAreaHasThreeMobsInCell(_world.Registry, _world.Placement));

        [Fact]
        public void Every_hunting_mob_has_coin_loot() =>
            AssertConforms(DevWorldConformanceRules.EveryHuntingMobHasCoinLoot(_world.Registry, _world.Placement));

        [Fact]
        public void Coin_loot_rises_by_band() =>
            AssertConforms(DevWorldConformanceRules.CoinLootRisesByBand(_world.Registry, _world.Placement, _world.Standards.Tunables));

        [Fact]
        public void Every_found_and_shop_item_has_value() =>
            AssertConforms(DevWorldConformanceRules.EveryFoundAndShopItemHasValue(_world.Registry, _world.Placement));

        [Fact]
        public void Every_powered_item_is_banded() =>
            AssertConforms(DevWorldConformanceRules.EveryPoweredItemIsBanded(_world.Registry));

        [Fact]
        public void Band_drift_audit_is_clean() =>
            AssertConforms(DevWorldConformanceRules.BandDriftAuditIsClean(_world.Audit()));

        [Fact]
        public void Every_committed_scenario_resolves()
        {
            var store = new SimScenarioStore(
                new ISimCombatantPolicy[] { new MeleeOnlyPolicy(), new RoundRobinPolicy(), new CooldownFirstPolicy(new AbilityRegistry()) },
                Options.Create(new SimulationOptions { ScenarioDirectory = _world.ScenarioDirectory }));

            AssertConforms(DevWorldConformanceRules.EveryCommittedScenarioResolves(_world.Registry, store, _world.ScenarioDirectory));
        }
    }
}
