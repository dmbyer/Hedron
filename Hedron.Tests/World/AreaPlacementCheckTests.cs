using System.Linq;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Tier 1 — system-unit tests for the out-of-designation check: <see cref="AreaPlacementCheck"/>'s
    /// judgement and sweep (R8, R9, R10) over <see cref="RegistryPlacementSource"/>, and the
    /// registry-backed <see cref="AreaPlacementSystem"/>.
    /// </summary>
    public sealed class AreaPlacementCheckTests
    {
        private readonly TemplateRegistry _registry = new(new EntityService());

        private void Area(string id, AreaDesignation? designation)
            => _registry.Register(id, new AreaTemplate(id) { AreaId = id, Designation = designation });

        private void Room(string id, string? areaId, params SpawnRule[] rules)
        {
            var room = new RoomTemplate(id) { AreaId = areaId ?? string.Empty };
            room.SpawnRules.AddRange(rules);
            _registry.Register(id, room);
        }

        private void Mob(string id, int tier, int band)
            => _registry.Register(id, new MobTemplate(id) { Tier = tier, Band = band });

        private void Item(string id, int tier, int band)
            => _registry.Register(id, new ItemTemplate(id) { Tier = tier, Band = band });

        private static SpawnRule Rule(string blueprintId, string? id = null)
            => new(id ?? blueprintId, blueprintId, 1, 60);

        private RegistryPlacementSource Source => new(_registry);

        private PlacementFinding? JudgeIn(string roomId, string? areaId, SpawnRule rule)
            => AreaPlacementCheck.Judge(new PlacementSite(roomId, areaId, rule), Source);

        // ── Judge: the no-finding cases ──────────────────────────────────────────

        [Fact]
        public void No_finding_for_undesignated_area_or_roomless_area()
        {
            Area("area.free", null);
            Mob("mob.b3", 0, 3);

            Assert.Null(JudgeIn("room.r", "area.free", Rule("mob.b3")));
            Assert.Null(JudgeIn("room.r", null, Rule("mob.b3")));
            Assert.Null(JudgeIn("room.r", "area.missing", Rule("mob.b3")));
        }

        [Fact]
        public void No_finding_for_unknown_unbanded_non_placeable_or_in_range_blueprint()
        {
            Area("area.b1", AreaDesignation.Of(0, 1, 1));
            Area("area.other", null);
            Mob("mob.unbanded", 0, 0);
            Mob("mob.b1", 0, 1);

            Assert.Null(JudgeIn("room.r", "area.b1", Rule("mob.missing")));
            Assert.Null(JudgeIn("room.r", "area.b1", Rule("mob.unbanded")));
            Assert.Null(JudgeIn("room.r", "area.b1", Rule("area.other"))); // not a mob/item
            Assert.Null(JudgeIn("room.r", "area.b1", Rule("mob.b1")));
        }

        // ── Judge: findings ──────────────────────────────────────────────────────

        [Fact]
        public void Band_above_or_below_or_other_tier_is_a_finding_with_every_R9_field()
        {
            var designation = AreaDesignation.Of(0, 2, 2);
            Area("area.b2", designation);
            Mob("mob.b3", 0, 3);
            Item("item.b1", 0, 1);
            Mob("mob.t1", 1, 2);

            var above = JudgeIn("room.r", "area.b2", Rule("mob.b3", "ogres"));
            Assert.NotNull(above);
            Assert.Equal("room.r", above!.RoomBlueprintId);
            Assert.Equal("ogres", above.RuleId);
            Assert.Equal("mob.b3", above.BlueprintId);
            Assert.Equal(PlacedKind.Mob, above.Kind);
            Assert.Equal("area.b2", above.AreaBlueprintId);
            Assert.Equal(new PowerBand(0, 3), above.Authored);
            Assert.Equal(designation, above.Designation);
            Assert.Contains("ogres", above.Describe());

            var below = JudgeIn("room.r", "area.b2", Rule("item.b1"));
            Assert.Equal(PlacedKind.Item, below!.Kind);

            Assert.NotNull(JudgeIn("room.r", "area.b2", Rule("mob.t1")));
        }

        // ── Sweep ────────────────────────────────────────────────────────────────

        [Fact]
        public void Sweep_yields_one_finding_per_offending_rule_in_deterministic_order()
        {
            Area("area.b1", AreaDesignation.Of(0, 1, 1));
            Mob("mob.b1", 0, 1);
            Mob("mob.b3", 0, 3);
            Room("room.b", "area.b1", Rule("mob.b3", "z-rule"), Rule("mob.b3", "a-rule"), Rule("mob.b1"));
            Room("room.a", "area.b1", Rule("mob.b3"));

            var findings = AreaPlacementCheck.Sweep(Source);

            Assert.Equal(
                new[] { ("room.a", "mob.b3"), ("room.b", "a-rule"), ("room.b", "z-rule") },
                findings.Select(f => (f.RoomBlueprintId, f.RuleId)).ToArray());
        }

        [Fact]
        public void Same_blueprint_in_two_areas_is_judged_per_placement_and_filter_restricts_by_area()
        {
            Area("area.b1", AreaDesignation.Of(0, 1, 1));
            Area("area.b3", AreaDesignation.Of(0, 3, 3));
            Mob("mob.b3", 0, 3);
            Room("room.near", "area.b1", Rule("mob.b3"));
            Room("room.far", "area.b3", Rule("mob.b3"));

            var all = AreaPlacementCheck.Sweep(Source);
            Assert.Equal("room.near", Assert.Single(all).RoomBlueprintId);

            Assert.Empty(AreaPlacementCheck.Sweep(Source, "area.b3"));
            Assert.Single(AreaPlacementCheck.Sweep(Source, "area.b1"));
        }

        [Fact]
        public void Area_filter_is_case_insensitive_like_the_registry()
        {
            Area("area.b1", AreaDesignation.Of(0, 1, 1));
            Mob("mob.b3", 0, 3);
            Room("room.r", "AREA.B1", Rule("mob.b3")); // authored in a different case

            Assert.Single(AreaPlacementCheck.Sweep(Source));
            Assert.Single(AreaPlacementCheck.Sweep(Source, "area.b1"));
        }

        // ── AreaPlacementSystem (registry-backed) ────────────────────────────────

        [Fact]
        public void PlacementSystem_judges_a_rule_against_its_rooms_registered_area()
        {
            Area("area.b1", AreaDesignation.Of(0, 1, 1));
            Mob("mob.b3", 0, 3);
            Room("room.r", "area.b1");
            var system = new AreaPlacementSystem(_registry);

            Assert.NotNull(system.JudgeRule("room.r", Rule("mob.b3")));
            Assert.Null(system.JudgeRule("room.missing", Rule("mob.b3")));
            Assert.Empty(system.Findings()); // the rule was judged, not committed
        }
    }
}
