using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Admin.Commands;
using Hedron.Core.Modules.Admin.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Output;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Flow tier — admin <c>spawnrule</c> (spawn-authoring T10): add/set/remove update the template,
    /// the live component, the tracker and the room YAML, and publish the audit event; refusals change
    /// nothing; the bare verb is read-only.
    /// </summary>
    public sealed class SpawnRuleCommandFlowTests
    {
        private static async Task<(SpawnWorld W, uint Room)> InCellar()
        {
            var w = new SpawnWorld();
            w.Mob("mob.rat");
            w.Room("room.cellar");
            w.Room("room.other");
            await w.BootAsync();
            var room = w.RoomEntity("room.cellar");
            w.StandIn(room);
            return (w, room);
        }

        private static Dictionary<string, object?> Args(SpawnRuleAction? action, params string[] rest)
        {
            var args = new Dictionary<string, object?>();
            if (action is not null) args["action"] = action.Value;
            for (var i = 0; i < rest.Length; i++) args[$"arg{i + 1}"] = rest[i];
            return args;
        }

        private static RoomTemplate Template(SpawnWorld w) =>
            (RoomTemplate)(w.Registry.TryGet("room.cellar", out var t) ? t : null)!;

        private static PlainMessage Last(SpawnWorld w) => w.Output.All.Select(r => r.Message).OfType<PlainMessage>().Last();

        [Fact]
        public async Task Add_updates_template_component_tracker_and_yaml_and_publishes_Added()
        {
            var (w, room) = await InCellar();
            using var _ = w;

            await w.RunAsync(w.SpawnRuleCommand(), Args(SpawnRuleAction.Add, "mob.rat", "3", "60"));

            var expected = new SpawnRule("mob.rat", "mob.rat", 3, 60);
            Assert.Equal(new[] { expected }, Template(w).SpawnRules);
            Assert.Equal(new[] { expected }, w.Ecs.Get<SpawnConfigComponent>(room).Rules);
            Assert.Equal(3, w.InRoom(room, "mob.rat").Count);
            Assert.Contains("id: mob.rat", w.Read("rooms", "room.cellar"));
            Assert.Contains("count: 3", w.Read("rooms", "room.cellar"));
            var ev = Assert.Single(w.Bus.Published.OfType<SpawnRuleAuthoredByAdminEvent>());
            Assert.Equal((SpawnRuleChange.Added, "mob.rat", "room.cellar", 3), (ev.Change, ev.RuleId, ev.RoomBlueprintId, ev.Count));
            Assert.Equal(0, w.Persistence.Saves);
        }

        [Fact]
        public async Task Set_count_detaches_surplus_and_rewrites_yaml()
        {
            var (w, room) = await InCellar();
            using var _ = w;
            await w.RunAsync(w.SpawnRuleCommand(), Args(SpawnRuleAction.Add, "mob.rat", "3", "60"));

            await w.RunAsync(w.SpawnRuleCommand(), Args(SpawnRuleAction.Set, "mob.rat", "count", "1"));

            Assert.Contains("detached 2", Last(w).Text);
            Assert.Equal(1, Template(w).SpawnRules.Single().Count);
            Assert.Equal((1, 1), (w.Spawn.DescribeRoom(room).Single().Count, w.Spawn.DescribeRoom(room).Single().Live));
            Assert.Contains("count: 1", w.Read("rooms", "room.cellar"));
            Assert.Equal(SpawnRuleChange.Updated, w.Bus.Published.OfType<SpawnRuleAuthoredByAdminEvent>().Last().Change);
        }

        [Fact]
        public async Task Remove_drops_the_rule_and_the_component_and_a_dying_instance_never_respawns()
        {
            var (w, room) = await InCellar();
            using var _ = w;
            await w.RunAsync(w.SpawnRuleCommand(), Args(SpawnRuleAction.Add, "mob.rat", "1", "10"));
            var rat = Assert.Single(w.InRoom(room, "mob.rat"));

            await w.RunAsync(w.SpawnRuleCommand(), Args(SpawnRuleAction.Remove, "mob.rat"));

            Assert.Empty(Template(w).SpawnRules);
            Assert.False(w.Ecs.HasComponent<SpawnConfigComponent>(room));
            Assert.DoesNotContain("blueprintId: mob.rat", w.Read("rooms", "room.cellar"));
            Assert.Equal(SpawnRuleChange.Removed, w.Bus.Published.OfType<SpawnRuleAuthoredByAdminEvent>().Last().Change);

            await w.Bus.PublishAsync(new MobDiedEvent(rat, "mob.rat"));
            w.Ecs.DestroyEntity(rat);
            w.Clock.Advance(System.TimeSpan.FromSeconds(100));
            await w.Bus.PublishAsync(Ticks.At(1));
            Assert.Empty(w.InRoom(room, "mob.rat"));
        }

        public static IEnumerable<object[]> Refusals() => new[]
        {
            new object[] { SpawnRuleAction.Add, new[] { "mob.unknown" } },
            new object[] { SpawnRuleAction.Add, new[] { "room.other" } },
            new object[] { SpawnRuleAction.Add, new[] { "mob.rat", "0" } },
            new object[] { SpawnRuleAction.Set, new[] { "no-such-rule", "count", "2" } },
            new object[] { SpawnRuleAction.Remove, new[] { "no-such-rule" } },
        };

        [Theory]
        [MemberData(nameof(Refusals))]
        public async Task Refusals_change_nothing_and_publish_nothing(SpawnRuleAction action, string[] rest)
        {
            var (w, room) = await InCellar();
            using var _ = w;
            var yaml = w.Read("rooms", "room.cellar");
            var published = w.Bus.Published.Count;

            await w.RunAsync(w.SpawnRuleCommand(), Args(action, rest));

            Assert.Equal(OutputSeverity.Error, Last(w).Severity);
            Assert.Empty(Template(w).SpawnRules);
            Assert.False(w.Ecs.HasComponent<SpawnConfigComponent>(room));
            Assert.Empty(w.Spawn.DescribeRoom(room));
            Assert.Equal(yaml, w.Read("rooms", "room.cellar"));
            Assert.Equal(published, w.Bus.Published.Count);
        }

        [Fact]
        public async Task Add_outside_a_room_is_refused()
        {
            var (w, _) = await InCellar();
            using var __ = w;
            w.StandIn(9999u);

            await w.RunAsync(w.SpawnRuleCommand(), Args(SpawnRuleAction.Add, "mob.rat"));

            Assert.Equal(OutputSeverity.Error, Last(w).Severity);
            Assert.Empty(w.Bus.Published.OfType<SpawnRuleAuthoredByAdminEvent>());
        }

        [Fact]
        public async Task No_action_lists_rules_read_only()
        {
            var (w, room) = await InCellar();
            using var _ = w;
            await w.RunAsync(w.SpawnRuleCommand(), Args(SpawnRuleAction.Add, "mob.rat", "2", "45"));
            var yaml = w.Read("rooms", "room.cellar");
            var published = w.Bus.Published.Count;

            await w.RunAsync(w.SpawnRuleCommand(), Args(null));

            var text = Last(w).Text;
            Assert.Contains("mob.rat: mob.rat 2/2 live, respawn 45s", text);
            Assert.Equal(yaml, w.Read("rooms", "room.cellar"));
            Assert.Equal(published, w.Bus.Published.Count);
        }
    }

    /// <summary>
    /// Flow tier — <c>mkmob</c>/<c>mkitem</c> author a count-1 rule (spawn-authoring T11): validate the
    /// room first, register the template only, add the rule (template + component + YAML), and the
    /// confirmed entity is the rule's tracked instance.
    /// </summary>
    public sealed class MkMobMkItemRuleFlowTests
    {
        private static readonly IReadOnlyDictionary<string, object?> Named = new Dictionary<string, object?> { ["name"] = "a rat" };

        private static async Task<(SpawnWorld W, uint Room)> InCellar()
        {
            var w = new SpawnWorld();
            w.Room("room.cellar");
            await w.BootAsync();
            var room = w.RoomEntity("room.cellar");
            w.StandIn(room);
            return (w, room);
        }

        [Fact]
        public async Task Mkmob_adds_a_count_one_rule_whose_instance_respawns_after_death()
        {
            var (w, room) = await InCellar();
            using var _ = w;

            await w.RunAsync(w.MkMobCommand(), Named);

            var created = Assert.Single(w.Bus.Published.OfType<MobCreatedByAdminEvent>());
            var rule = Assert.Single(w.Bus.Published.OfType<SpawnRuleAuthoredByAdminEvent>());
            Assert.Equal(created.BlueprintId, rule.BlueprintId);
            Assert.Equal((1, Hedron.Core.ECS.Components.SpawnRuleLimits.DefaultRespawnDelaySeconds), (rule.Count!.Value, rule.RespawnDelaySeconds!.Value));
            Assert.Equal(new[] { created.MobEntityId }, w.InRoom(room, created.BlueprintId));
            Assert.Contains(created.BlueprintId, w.Read("rooms", "room.cellar"));
            Assert.DoesNotContain("spawnRoom", w.Read("mobs", created.BlueprintId));
            Assert.Single(w.Ecs.Get<SpawnConfigComponent>(room).Rules);

            await w.Bus.PublishAsync(new MobDiedEvent(created.MobEntityId, created.BlueprintId));
            w.Ecs.DestroyEntity(created.MobEntityId);
            w.Clock.Advance(System.TimeSpan.FromSeconds(300));
            await w.Bus.PublishAsync(Ticks.At(1));
            Assert.Single(w.InRoom(room, created.BlueprintId));
        }

        [Fact]
        public async Task Mkitem_adds_a_count_one_rule_whose_instance_respawns_after_pickup()
        {
            var (w, room) = await InCellar();
            using var _ = w;

            await w.RunAsync(w.MkitemCommand(), new Dictionary<string, object?> { ["name"] = "a herb" });

            var created = Assert.Single(w.Bus.Published.OfType<ItemCreatedByAdminEvent>());
            Assert.Single(w.Bus.Published.OfType<SpawnRuleAuthoredByAdminEvent>());
            Assert.Equal(new[] { created.ItemEntityId }, w.InRoom(room, created.BlueprintId));
            Assert.DoesNotContain("spawnRoom", w.Read("items", created.BlueprintId));

            await w.Bus.PublishAsync(new ItemPickedUpEvent(w.AdminId, created.ItemEntityId, room));
            w.Ecs.RemoveComponent<LocationComponent>(created.ItemEntityId);
            w.Clock.Advance(System.TimeSpan.FromSeconds(300));
            await w.Bus.PublishAsync(Ticks.At(1));
            Assert.Single(w.InRoom(room, created.BlueprintId));
        }

        [Fact]
        public async Task Mk_commands_outside_a_registered_room_create_nothing()
        {
            var (w, room) = await InCellar();
            using var _ = w;
            // A live room entity with no registered template.
            var bare = w.Ecs.CreateEntity().Id;
            w.Ecs.AddComponent(bare, new RoomComponent { Name = "bare" });
            w.StandIn(bare);
            var ids = w.Registry.AllBlueprintIds().Count;

            await w.RunAsync(w.MkMobCommand(), Named);
            w.StandIn(9999u);
            await w.RunAsync(w.MkitemCommand(), Named);

            Assert.Equal(ids, w.Registry.AllBlueprintIds().Count);
            Assert.False(System.IO.Directory.Exists(System.IO.Path.Combine(w.Dir, "mobs")));
            Assert.False(System.IO.Directory.Exists(System.IO.Path.Combine(w.Dir, "items")));
            Assert.Empty(w.Bus.Published.OfType<MobCreatedByAdminEvent>());
            Assert.Empty(w.Bus.Published.OfType<ItemCreatedByAdminEvent>());
            Assert.Empty(w.Bus.Published.OfType<SpawnRuleAuthoredByAdminEvent>());
            Assert.All(w.Output.All.Select(r => r.Message).OfType<PlainMessage>(), m => Assert.Equal(OutputSeverity.Error, m.Severity));
        }
    }

    /// <summary>
    /// Flow tier — <c>setmob</c>/<c>setitem</c> fan out to the template and every world instance,
    /// never a player-owned copy (spawn-authoring T12, T22).
    /// </summary>
    public sealed class SetMobSetItemFanOutTests
    {
        private static Dictionary<string, object?> Set(string bp, string prop, string value) =>
            new() { ["blueprintId"] = bp, ["property"] = prop, ["value"] = value };

        [Fact]
        public async Task Setmob_updates_three_world_rats_and_never_the_persistent_copy()
        {
            using var w = new SpawnWorld();
            w.Mob("mob.rat", "maxHp: 10\n");
            w.Room("room.cellar", "  - blueprintId: mob.rat\n    count: 3\n");
            await w.BootAsync();
            var owned = w.Registry.Spawn("mob.rat").Id;
            w.Ecs.AddComponent(owned, new PersistentEntity());

            await w.RunAsync(w.SetMobCommand(), Set("mob.rat", "hp", "30"));

            var room = w.RoomEntity("room.cellar");
            Assert.All(w.InRoom(room, "mob.rat"), id => Assert.Equal(30, w.Ecs.Get<PoolsComponent>(id).MaxHp));
            Assert.Equal(10, w.Ecs.Get<PoolsComponent>(owned).MaxHp);
            Assert.Contains("maxHp: 30", w.Read("mobs", "mob.rat"));
            var ev = Assert.Single(w.Bus.Published.OfType<MobPropertySetByAdminEvent>());
            Assert.Equal(("mob.rat", 3), (ev.BlueprintId, ev.InstancesUpdated));
        }

        [Fact]
        public async Task Setitem_with_no_live_instances_updates_the_template_and_succeeds()
        {
            using var w = new SpawnWorld();
            w.Item("item.herb");
            w.Room("room.cellar");
            await w.BootAsync();
            var owned = w.Registry.Spawn("item.herb").Id;
            w.Ecs.AddComponent(owned, new PersistentEntity());

            await w.RunAsync(w.SetitemCommand(), Set("item.herb", "value", "40"));

            Assert.Contains("value: 40", w.Read("items", "item.herb"));
            Assert.Equal(0, w.Ecs.Get<ItemDataComponent>(owned).Value);
            var ev = Assert.Single(w.Bus.Published.OfType<ItemPropertySetByAdminEvent>());
            Assert.Equal(("item.herb", 0), (ev.BlueprintId, ev.InstancesUpdated));
            Assert.Equal(OutputSeverity.Confirmation, w.Output.All.Select(r => r.Message).OfType<PlainMessage>().Last().Severity);
        }
    }
}
