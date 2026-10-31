using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Events;
using Hedron.Core.Modules.Items.Events;
using Hedron.Core.Modules.Mobs.Events;
using Hedron.Core.Modules.World.Events;
using Hedron.Tests.Harness;
using Xunit;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Tier 1 — unit tests for the rule-keyed <see cref="Hedron.Core.Modules.Spawn.Systems.SpawnSystem"/>
    /// (spawn-authoring T1). Coverage: immediate boot fill with location (INV-23), no claiming,
    /// duplicate-blueprint rules, per-slot vacancy and independent timers, item pickup vacancy,
    /// unknown-template and duplicate-id robustness, rebuild on a second ready event, the State
    /// priority, and the reload-teardown guard.
    /// </summary>
    public sealed class SpawnSystemTests
    {
        private static Task Ready(SpawnFixture f) => f.System.HandleAsync(new WorldContentReadyEvent());
        private static Task Tick(SpawnFixture f) => f.System.HandleAsync(Ticks.At(1));

        // ── Boot fill ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Boot_fills_every_slot_immediately_with_room_entity_and_blueprint_location()
        {
            var f = new SpawnFixture();
            f.Template("mob.rat");
            var room = f.Room("room.cellar", new SpawnRule("mob.rat", "mob.rat", 3, 60));

            await Ready(f);

            var rats = f.InRoom(room, "mob.rat");
            Assert.Equal(3, rats.Count);
            foreach (var id in rats)
            {
                var loc = f.Ecs.Get<LocationComponent>(id);
                Assert.Equal(room, loc.RoomEntityId);
                Assert.Equal("room.cellar", loc.RoomBlueprintId);
            }
        }

        [Fact]
        public async Task Boot_never_claims_a_pre_existing_same_blueprint_entity()
        {
            var f = new SpawnFixture();
            f.Template("item.torch");
            var room = f.Room("room.hall", new SpawnRule("item.torch", "item.torch", 1, 60));

            // A player-owned copy dropped in the room before the fill.
            var owned = f.Registry.Spawn("item.torch").Id;
            f.Ecs.AddComponent(owned, new LocationComponent { RoomEntityId = room });
            f.Ecs.AddComponent(owned, new PersistentEntity());

            await Ready(f);

            Assert.Equal(2, f.InRoom(room, "item.torch").Count);

            // Picking up the owned copy is not a tracked vacancy: nothing respawns.
            await f.System.HandleAsync(new ItemPickedUpEvent(99u, owned, room));
            f.Advance(1000);
            await Tick(f);
            Assert.Equal(2, f.CountOf("item.torch"));
        }

        [Fact]
        public async Task Two_rules_with_the_same_blueprint_and_distinct_ids_are_both_honored()
        {
            var f = new SpawnFixture();
            f.Template("mob.rat");
            var room = f.Room("room.cellar",
                new SpawnRule("rats-north", "mob.rat", 2, 60),
                new SpawnRule("rats-south", "mob.rat", 1, 60));

            await Ready(f);

            Assert.Equal(3, f.InRoom(room, "mob.rat").Count);
            Assert.Equal(new[] { "rats-north", "rats-south" }, f.System.DescribeRoom(room).Select(s => s.RuleId));
        }

        // ── Vacancy and respawn ─────────────────────────────────────────────────

        [Fact]
        public async Task Killing_one_of_three_vacates_only_that_slot_and_respawns_exactly_one_at_the_delay()
        {
            var f = new SpawnFixture();
            f.Template("mob.rat");
            var room = f.Room("room.cellar", new SpawnRule("mob.rat", "mob.rat", 3, 60));
            await Ready(f);

            var victim = f.InRoom(room, "mob.rat")[0];
            await f.System.HandleAsync(new MobDiedEvent(victim, "mob.rat"));
            f.Ecs.DestroyEntity(victim);

            var status = Assert.Single(f.System.DescribeRoom(room));
            Assert.Equal(2, status.Live);
            Assert.Equal(f.Clock.UtcNow.AddSeconds(60), status.NextRespawnAt);

            f.Advance(59);
            await Tick(f);
            Assert.Equal(2, f.InRoom(room, "mob.rat").Count);

            f.Advance(1);
            await Tick(f);
            Assert.Equal(3, f.InRoom(room, "mob.rat").Count);

            await Tick(f);
            Assert.Equal(3, f.InRoom(room, "mob.rat").Count);
        }

        [Fact]
        public async Task Each_slot_keeps_its_own_timer()
        {
            var f = new SpawnFixture();
            f.Template("mob.rat");
            var room = f.Room("room.cellar", new SpawnRule("mob.rat", "mob.rat", 2, 60));
            await Ready(f);
            var (a, b) = (f.InRoom(room, "mob.rat")[0], f.InRoom(room, "mob.rat")[1]);

            await f.System.HandleAsync(new MobDiedEvent(a, "mob.rat"));
            f.Ecs.DestroyEntity(a);
            f.Advance(30);
            await f.System.HandleAsync(new MobDiedEvent(b, "mob.rat"));
            f.Ecs.DestroyEntity(b);

            f.Advance(30); // a due, b not
            await Tick(f);
            Assert.Single(f.InRoom(room, "mob.rat"));

            f.Advance(30); // b due
            await Tick(f);
            Assert.Equal(2, f.InRoom(room, "mob.rat").Count);
        }

        [Fact]
        public async Task Item_pickup_vacates_the_items_slot_and_it_respawns_after_the_delay()
        {
            var f = new SpawnFixture();
            f.Template("item.herb");
            var room = f.Room("room.garden", new SpawnRule("item.herb", "item.herb", 1, 120));
            await Ready(f);
            var herb = Assert.Single(f.InRoom(room, "item.herb"));

            // Pickup: the entity survives (moved to the player) but leaves the room.
            await f.System.HandleAsync(new ItemPickedUpEvent(7u, herb, room));
            f.Ecs.RemoveComponent<LocationComponent>(herb);

            f.Advance(119);
            await Tick(f);
            Assert.Empty(f.InRoom(room, "item.herb"));

            f.Advance(1);
            await Tick(f);
            Assert.Single(f.InRoom(room, "item.herb"));
        }

        // ── Robustness ──────────────────────────────────────────────────────────

        [Fact]
        public async Task An_unknown_template_disables_the_slot_without_throwing()
        {
            var f = new SpawnFixture();
            var room = f.Room("room.void", new SpawnRule("mob.missing", "mob.missing", 2, 10));

            await Ready(f);
            f.Advance(1000);
            await Tick(f);

            var status = Assert.Single(f.System.DescribeRoom(room));
            Assert.Equal(0, status.Live);
            Assert.Null(status.NextRespawnAt);
            Assert.Equal(0, f.CountOf("mob.missing"));
        }

        [Fact]
        public async Task A_duplicate_rule_id_in_the_component_skips_the_second_rule()
        {
            var f = new SpawnFixture();
            f.Template("mob.rat", "mob.bat");
            var room = f.Room("room.cave",
                new SpawnRule("pests", "mob.rat", 1, 60),
                new SpawnRule("PESTS", "mob.bat", 1, 60));

            await Ready(f);

            Assert.Single(f.InRoom(room, "mob.rat"));
            Assert.Empty(f.InRoom(room, "mob.bat"));
        }

        [Fact]
        public async Task A_second_ready_event_clears_and_refills_the_tracker()
        {
            var f = new SpawnFixture();
            f.Template("mob.rat");
            var room = f.Room("room.cellar", new SpawnRule("mob.rat", "mob.rat", 2, 60));
            await Ready(f);

            // Reload tears down every world entity before re-publishing the ready event.
            foreach (var id in f.InRoom(room, "mob.rat"))
                f.Ecs.DestroyEntity(id);
            await Ready(f);

            var status = Assert.Single(f.System.DescribeRoom(room));
            Assert.Equal(2, status.Live);
            Assert.Equal(2, f.InRoom(room, "mob.rat").Count);
        }

        [Fact]
        public void WorldContentReady_is_handled_at_State_priority_ahead_of_shop_seeding()
        {
            var f = new SpawnFixture();
            Assert.Equal(HandlerPriority.State, ((IEventHandler<WorldContentReadyEvent>)f.System).Priority);
        }

        // ── Reload-teardown guard (INV-31) ──────────────────────────────────────

        [Fact]
        public async Task A_due_slot_whose_room_lost_its_RoomComponent_spawns_nothing()
        {
            var f = new SpawnFixture();
            f.Template("mob.rat");
            var room = f.Room("room.cellar", new SpawnRule("mob.rat", "mob.rat", 1, 10));
            await Ready(f);
            var rat = Assert.Single(f.InRoom(room, "mob.rat"));
            await f.System.HandleAsync(new MobDiedEvent(rat, "mob.rat"));
            f.Ecs.DestroyEntity(rat);

            // Mid-reload teardown removes the room before the tracker is rebuilt.
            f.Ecs.RemoveComponent<RoomComponent>(room);
            f.Advance(10);
            await Tick(f);

            Assert.Equal(0, f.CountOf("mob.rat"));
        }
    }
}
