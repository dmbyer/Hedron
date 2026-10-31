using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Admin.Commands;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Components;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Output;
using Xunit;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Flow tier — area-designation in-game surfaces (P8–P10): <c>areaset</c> updates template, live
    /// entity and YAML and publishes one audit event (refusals change nothing); <c>area</c> shows the
    /// designation and findings; spawn-rule authoring warns on an out-of-designation placement but
    /// still saves the rule.
    /// </summary>
    public sealed class AreaDesignationCommandFlowTests
    {
        private static async Task<(SpawnWorld W, uint Room)> InWilds(string areaExtra = "")
        {
            var w = new SpawnWorld();
            w.Area("area.wilds", areaExtra);
            w.Mob("mob.b1", "tier: 0\nband: 1\n");
            w.Mob("mob.b3", "tier: 0\nband: 3\n");
            w.Room("room.glade", areaId: "area.wilds");
            await w.BootAsync();
            var room = w.RoomEntity("room.glade");
            w.StandIn(room);
            return (w, room);
        }

        private const string B1Designation = "designation:\n  tier: 0\n  bandMin: 1\n  bandMax: 1\n";

        private static Dictionary<string, object?> Set(string property, string value) =>
            new() { ["blueprintId"] = "area.wilds", ["property"] = property, ["value"] = value };

        private static AreaTemplate Template(SpawnWorld w) =>
            (AreaTemplate)(w.Registry.TryGet("area.wilds", out var t) ? t : null)!;

        private static uint AreaEntity(SpawnWorld w) =>
            w.Ecs.GetAllComponents<AreaComponent>().Select(e => e.EntityId).Single();

        private static List<string> Texts(SpawnWorld w) =>
            w.Output.All.Select(r => r.Message).OfType<PlainMessage>().Select(m => m.Text).ToList();

        // ── areaset designation (P8) ─────────────────────────────────────────────

        [Fact]
        public async Task Designation_updates_template_live_component_and_yaml_and_publishes_once()
        {
            var (w, _) = await InWilds();
            using var _w = w;

            await w.RunAsync(w.AreaSetCommand(), Set("designation", "0 1-2"));

            var expected = AreaDesignation.Of(0, 1, 2);
            Assert.Equal(expected, Template(w).Designation);
            Assert.Equal(expected, w.Ecs.Get<AreaDesignationComponent>(AreaEntity(w)).Designation);
            Assert.Contains("bandMax: 2", w.Read("areas", "area.wilds"));
            var ev = Assert.Single(w.Bus.Published.OfType<AreaPropertySetByAdminEvent>());
            Assert.Equal(("area.wilds", "designation", AreaEntity(w)), (ev.AreaBlueprintId, ev.PropertyName, ev.AreaEntityId));
            Assert.Contains("Tier 0, Bands 1-2", Texts(w).Last());
            Assert.Equal(0, w.Persistence.Saves);
        }

        [Fact]
        public async Task Designation_clear_removes_template_field_component_and_yaml_key()
        {
            var (w, _) = await InWilds(B1Designation);
            using var _w = w;
            Assert.True(w.Ecs.HasComponent<AreaDesignationComponent>(AreaEntity(w)));

            await w.RunAsync(w.AreaSetCommand(), Set("designation", "clear"));

            Assert.Null(Template(w).Designation);
            Assert.False(w.Ecs.HasComponent<AreaDesignationComponent>(AreaEntity(w)));
            Assert.DoesNotContain("designation", w.Read("areas", "area.wilds"));
            Assert.Equal("clear", w.Bus.Published.OfType<AreaPropertySetByAdminEvent>().Single().NewValue);
        }

        [Theory]
        [InlineData("designation", "0 x")]   // parse error
        [InlineData("designation", "0 3-1")] // R2 shape error
        [InlineData("pvp", "true")]          // unknown property
        public async Task Bad_value_changes_nothing_writes_nothing_and_publishes_nothing(string property, string value)
        {
            var (w, _) = await InWilds();
            using var _w = w;
            var before = w.Read("areas", "area.wilds");

            await w.RunAsync(w.AreaSetCommand(), Set(property, value));

            Assert.Null(Template(w).Designation);
            Assert.False(w.Ecs.HasComponent<AreaDesignationComponent>(AreaEntity(w)));
            Assert.Equal(before, w.Read("areas", "area.wilds"));
            Assert.Empty(w.Bus.Published.OfType<AreaPropertySetByAdminEvent>());
            Assert.Equal(OutputSeverity.Error, w.Output.All.Select(r => r.Message).OfType<PlainMessage>().Last().Severity);
        }

        [Fact]
        public async Task Unknown_area_is_refused()
        {
            var (w, _) = await InWilds();
            using var _w = w;

            await w.RunAsync(w.AreaSetCommand(),
                new Dictionary<string, object?> { ["blueprintId"] = "area.nowhere", ["property"] = "name", ["value"] = "X" });

            Assert.Empty(w.Bus.Published.OfType<AreaPropertySetByAdminEvent>());
            Assert.Contains("area.nowhere", Texts(w).Last());
        }

        [Fact]
        public async Task Name_and_description_update_template_live_component_and_yaml()
        {
            var (w, _) = await InWilds();
            using var _w = w;

            await w.RunAsync(w.AreaSetCommand(), Set("name", "The Wilds"));
            await w.RunAsync(w.AreaSetCommand(), Set("description", "Tall grass."));

            Assert.Equal(("The Wilds", "Tall grass."), (Template(w).Name, Template(w).Description));
            var live = w.Ecs.Get<AreaComponent>(AreaEntity(w));
            Assert.Equal(("The Wilds", "Tall grass."), (live.Name, live.Description));
            Assert.Contains("The Wilds", w.Read("areas", "area.wilds"));
            Assert.Equal(2, w.Bus.Published.OfType<AreaPropertySetByAdminEvent>().Count());
        }

        // ── spawn-rule authoring warnings (P10) ──────────────────────────────────

        private static Dictionary<string, object?> AddRule(string blueprintId) =>
            new() { ["action"] = SpawnRuleAction.Add, ["arg1"] = blueprintId, ["arg2"] = "1", ["arg3"] = "60" };

        [Fact]
        public async Task Out_of_designation_rule_is_saved_audited_and_warned()
        {
            var (w, _) = await InWilds(B1Designation);
            using var _w = w;

            await w.RunAsync(w.SpawnRuleCommand(), AddRule("mob.b3"));

            Assert.Contains("mob.b3", w.Read("rooms", "room.glade"));
            Assert.Single(w.Bus.Published.OfType<SpawnRuleAuthoredByAdminEvent>());
            var warning = Assert.Single(Texts(w), t => t.StartsWith("Warning:"));
            Assert.Contains("mob.b3", warning);
        }

        [Fact]
        public async Task In_range_rule_and_removal_warn_nothing()
        {
            var (w, room) = await InWilds(B1Designation);
            using var _w = w;

            await w.RunAsync(w.SpawnRuleCommand(), AddRule("mob.b1"));
            var removal = w.RoomBuilder.RemoveSpawnRule(room, "mob.b1");

            Assert.True(removal.Success);
            Assert.True(removal.Findings is null || removal.Findings.Count == 0);
            Assert.DoesNotContain(Texts(w), t => t.StartsWith("Warning:"));
        }

        [Fact]
        public async Task Builder_attaches_the_finding_to_add_and_update_results()
        {
            var (w, room) = await InWilds(B1Designation);
            using var _w = w;

            var added = w.RoomBuilder.AddSpawnRule(room, "mob.b1", 1, 60);
            Assert.True(added.Success);
            Assert.Empty(added.Findings!);

            var updated = w.RoomBuilder.UpdateSpawnRule(room, "mob.b1", new Hedron.Core.Modules.Admin.Systems.SpawnRuleUpdate(BlueprintId: "mob.b3"));
            Assert.True(updated.Success);
            Assert.Equal("mob.b3", Assert.Single(updated.Findings!).BlueprintId);
        }

        // ── area verb (P9) ───────────────────────────────────────────────────────

        [Fact]
        public async Task Area_shows_designation_and_findings_after_areaset_without_reload()
        {
            var (w, room) = await InWilds();
            using var _w = w;
            w.RoomBuilder.AddSpawnRule(room, "mob.b3", 1, 60, "ogres");

            await w.RunAsync(w.AreaCommand(), new Dictionary<string, object?> { ["blueprintId"] = "area.wilds" });
            Assert.Contains("undesignated", Texts(w).Last());

            await w.RunAsync(w.AreaSetCommand(), Set("designation", "0 1"));
            await w.RunAsync(w.AreaCommand(), new Dictionary<string, object?> { ["blueprintId"] = "area.wilds" });

            var text = Texts(w).Last();
            Assert.Contains("Tier 0, Band 1", text);
            Assert.Contains("ogres", text);
        }
    }
}
