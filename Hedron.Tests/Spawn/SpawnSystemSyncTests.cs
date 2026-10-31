using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.World.Events;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Tier 1 — <c>ISpawnSystem.SyncRoom</c> / <c>DescribeRoom</c> (spawn-authoring T2): reconciling
    /// the tracker with a room's live rules after an admin mutation, keyed by rule id.
    /// </summary>
    public sealed class SpawnSystemSyncTests
    {
        private static async Task<SpawnFixture> Booted(params SpawnRule[] rules)
        {
            var f = new SpawnFixture();
            f.Template("mob.rat", "mob.bat");
            f.Room("room.cellar", rules);
            await f.System.HandleAsync(new WorldContentReadyEvent());
            return f;
        }

        private static uint RoomOf(SpawnFixture f) =>
            f.Ecs.GetAllComponents<RoomComponent>().Single().EntityId;

        private static Task Tick(SpawnFixture f) => f.System.HandleAsync(Ticks.At(1));

        [Fact]
        public async Task A_new_rule_spawns_its_count_now()
        {
            var f = await Booted();
            var room = RoomOf(f);
            f.SetRules(room, new SpawnRule("rats", "mob.rat", 3, 60));

            var result = f.System.SyncRoom(room);

            Assert.Equal(3, result.SpawnedEntityIds.Count);
            Assert.Equal(3, result.SlotsAdded);
            Assert.Equal(0, result.SlotsDetached);
            Assert.Equal(3, f.InRoom(room, "mob.rat").Count);
        }

        [Fact]
        public async Task A_count_increase_spawns_the_delta_now()
        {
            var f = await Booted(new SpawnRule("rats", "mob.rat", 1, 60));
            var room = RoomOf(f);
            f.SetRules(room, new SpawnRule("rats", "mob.rat", 3, 60));

            var result = f.System.SyncRoom(room);

            Assert.Equal(2, result.SpawnedEntityIds.Count);
            Assert.Equal(3, f.InRoom(room, "mob.rat").Count);
        }

        [Fact]
        public async Task A_count_decrease_drops_vacant_slots_first_then_detaches_occupants()
        {
            var f = await Booted(new SpawnRule("rats", "mob.rat", 4, 60));
            var room = RoomOf(f);
            var rats = f.InRoom(room, "mob.rat");
            await f.System.HandleAsync(new MobDiedEvent(rats[0], "mob.rat"));
            f.Ecs.DestroyEntity(rats[0]);

            f.SetRules(room, new SpawnRule("rats", "mob.rat", 2, 60));
            var result = f.System.SyncRoom(room);

            // One vacant + one occupied slot dropped.
            Assert.Equal(1, result.SlotsDetached);
            Assert.Empty(result.SpawnedEntityIds);
            var status = Assert.Single(f.System.DescribeRoom(room));
            Assert.Equal((2, 2), (status.Count, status.Live));
            Assert.Null(status.NextRespawnAt);

            // The detached rat is still alive (2 tracked + 1 detached); killing all three vacates
            // only the 2 tracked slots — the detached death schedules nothing.
            var alive = f.InRoom(room, "mob.rat");
            Assert.Equal(3, alive.Count);
            foreach (var id in alive)
            {
                await f.System.HandleAsync(new MobDiedEvent(id, "mob.rat"));
                f.Ecs.DestroyEntity(id);
            }
            f.Advance(60);
            await Tick(f);
            Assert.Equal(2, f.InRoom(room, "mob.rat").Count);
        }

        [Fact]
        public async Task A_removed_rule_detaches_its_entities_and_their_deaths_schedule_nothing()
        {
            var f = await Booted(new SpawnRule("rats", "mob.rat", 2, 10));
            var room = RoomOf(f);
            var rats = f.InRoom(room, "mob.rat");

            f.SetRules(room);
            var result = f.System.SyncRoom(room);

            Assert.Equal(2, result.SlotsDetached);
            Assert.Empty(f.System.DescribeRoom(room));
            Assert.All(rats, id => Assert.True(f.Ecs.HasComponent<LocationComponent>(id)));

            foreach (var id in rats)
            {
                await f.System.HandleAsync(new MobDiedEvent(id, "mob.rat"));
                f.Ecs.DestroyEntity(id);
            }
            f.Advance(100);
            await Tick(f);
            Assert.Equal(0, f.CountOf("mob.rat"));
        }

        [Fact]
        public async Task Reordering_rules_spawns_and_detaches_nothing_and_keeps_ids()
        {
            var f = await Booted(new SpawnRule("rats", "mob.rat", 2, 60), new SpawnRule("bats", "mob.bat", 1, 60));
            var room = RoomOf(f);
            var before = f.Ecs.GetAllComponents<BlueprintComponent>().Select(e => e.EntityId).OrderBy(i => i).ToList();

            f.SetRules(room, new SpawnRule("bats", "mob.bat", 1, 60), new SpawnRule("rats", "mob.rat", 2, 60));
            var result = f.System.SyncRoom(room);

            Assert.Empty(result.SpawnedEntityIds);
            Assert.Equal(0, result.SlotsDetached);
            Assert.Equal(before, f.Ecs.GetAllComponents<BlueprintComponent>().Select(e => e.EntityId).OrderBy(i => i));
            Assert.Equal(new[] { "bats", "rats" }, f.System.DescribeRoom(room).Select(s => s.RuleId));
        }

        [Fact]
        public async Task An_unchanged_rule_keeps_its_pending_timer_across_an_unrelated_sync()
        {
            var f = await Booted(new SpawnRule("rats", "mob.rat", 1, 60));
            var room = RoomOf(f);
            var t0 = f.Clock.UtcNow;
            var rat = Assert.Single(f.InRoom(room, "mob.rat"));
            await f.System.HandleAsync(new MobDiedEvent(rat, "mob.rat"));
            f.Ecs.DestroyEntity(rat);

            f.Advance(1);
            f.SetRules(room, new SpawnRule("rats", "mob.rat", 1, 60), new SpawnRule("bats", "mob.bat", 1, 60));
            f.System.SyncRoom(room);

            var rats = f.System.DescribeRoom(room).Single(s => s.RuleId == "rats");
            Assert.Equal(t0.AddSeconds(60), rats.NextRespawnAt);

            f.Advance(58); // t0+59
            await Tick(f);
            Assert.Empty(f.InRoom(room, "mob.rat"));
            f.Advance(1); // t0+60
            await Tick(f);
            Assert.Single(f.InRoom(room, "mob.rat"));
        }

        [Fact]
        public async Task A_changed_delay_applies_from_the_next_vacancy()
        {
            var f = await Booted(new SpawnRule("rats", "mob.rat", 1, 60));
            var room = RoomOf(f);

            f.SetRules(room, new SpawnRule("rats", "mob.rat", 1, 5));
            f.System.SyncRoom(room);
            var rat = Assert.Single(f.InRoom(room, "mob.rat"));
            await f.System.HandleAsync(new MobDiedEvent(rat, "mob.rat"));
            f.Ecs.DestroyEntity(rat);

            Assert.Equal(f.Clock.UtcNow.AddSeconds(5), f.System.DescribeRoom(room).Single().NextRespawnAt);
        }

        [Fact]
        public async Task A_changed_blueprint_applies_from_the_next_spawn()
        {
            var f = await Booted(new SpawnRule("pests", "mob.rat", 1, 10));
            var room = RoomOf(f);

            f.SetRules(room, new SpawnRule("pests", "mob.bat", 1, 10));
            var result = f.System.SyncRoom(room);
            Assert.Empty(result.SpawnedEntityIds);
            var rat = Assert.Single(f.InRoom(room, "mob.rat")); // unchanged until it dies

            await f.System.HandleAsync(new MobDiedEvent(rat, "mob.rat"));
            f.Ecs.DestroyEntity(rat);
            f.Advance(10);
            await Tick(f);

            Assert.Single(f.InRoom(room, "mob.bat"));
        }

        [Fact]
        public async Task DescribeRoom_reports_each_rule_by_id()
        {
            var f = await Booted(new SpawnRule("rats", "mob.rat", 3, 45), new SpawnRule("bats", "mob.bat", 1, 90));
            var room = RoomOf(f);

            var statuses = f.System.DescribeRoom(room);

            Assert.Equal(2, statuses.Count);
            Assert.Equal(("rats", "mob.rat", 3, 3, 45), (statuses[0].RuleId, statuses[0].BlueprintId, statuses[0].Count, statuses[0].Live, statuses[0].RespawnDelaySeconds));
            Assert.Equal(("bats", "mob.bat", 1, 1, 90), (statuses[1].RuleId, statuses[1].BlueprintId, statuses[1].Count, statuses[1].Live, statuses[1].RespawnDelaySeconds));
            Assert.All(statuses, s => Assert.Null(s.NextRespawnAt));
        }
    }
}
