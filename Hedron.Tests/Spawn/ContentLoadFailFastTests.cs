using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Modules.World.Events;
using Hedron.Core.Output;
using Hedron.Core.Systems;
using Xunit;

namespace Hedron.Tests.Spawn
{
    /// <summary>
    /// Flow tier, throws — the staged fail-fast corpus read (spawn-authoring T8): a shape error
    /// aborts boot with every offending file listed and nothing registered; malformed YAML keeps
    /// log-and-skip; a refused reload leaves the live world, registry and tracker untouched.
    /// </summary>
    public sealed class ContentLoadFailFastTests
    {
        [Fact]
        public async Task Boot_with_shape_errors_throws_listing_every_file_and_registers_nothing()
        {
            using var w = new SpawnWorld();
            w.Room("room.legacy", "  - blueprintId: mob.rat\n    minCount: 1\n    maxCount: 2\n");
            w.Mob("mob.rat", "spawnRoomBlueprintId: room.legacy\n");
            w.Room("room.fine");
            w.Item("item.herb");

            var ex = await Assert.ThrowsAsync<ContentLoadException>(() => w.Loader.LoadAndSpawnAsync());

            Assert.Equal(2, ex.Errors.Count);
            Assert.Contains(ex.Errors, e => e.StartsWith("rooms/room.legacy.yaml: "));
            Assert.Contains(ex.Errors, e => e.StartsWith("mobs/mob.rat.yaml: "));
            Assert.Contains("rooms/room.legacy.yaml", ex.Message);
            Assert.Empty(w.Registry.AllBlueprintIds());
            Assert.Empty(w.Ecs.GetAllComponents<Hedron.Core.ECS.Components.BlueprintComponent>());
        }

        [Fact]
        public async Task Malformed_yaml_alone_is_still_logged_and_skipped()
        {
            using var w = new SpawnWorld();
            w.Room("room.fine");
            w.Write("rooms", "room.broken", "id: [unterminated\n");

            await w.Loader.LoadAndSpawnAsync();

            Assert.Contains("room.fine", w.Registry.AllBlueprintIds());
        }

        [Fact]
        public async Task Refused_reload_leaves_entities_registry_and_tracker_unchanged()
        {
            using var w = new SpawnWorld();
            w.Mob("mob.rat");
            w.Room("room.cellar", "  - blueprintId: mob.rat\n    count: 2\n");
            await w.BootAsync();

            var room = w.RoomEntity("room.cellar");
            var snapshot = Snapshot(w);
            var ids = w.Registry.AllBlueprintIds().OrderBy(i => i).ToList();
            var tracker = w.Spawn.DescribeRoom(room).ToList();

            w.Item("item.legacy", "spawnRoomId: room.cellar\n");
            await Assert.ThrowsAsync<ContentLoadException>(() => w.Loader.ReloadAsync());

            Assert.Equal(snapshot, Snapshot(w));
            Assert.Equal(ids, w.Registry.AllBlueprintIds().OrderBy(i => i));
            Assert.Equal(tracker, w.Spawn.DescribeRoom(room));
        }

        /// <summary>Every entity id → its component instances (reference identity), for exact-unchanged checks.</summary>
        private static Dictionary<uint, List<object>> Snapshot(SpawnWorld w) =>
            w.Ecs.GetAllComponents<Hedron.Core.ECS.Components.BlueprintComponent>()
                .Select(e => e.EntityId)
                .ToDictionary(id => id, id => w.Ecs.GetAllComponentsForEntity(id).Select(c => (object)c.Component).ToList());
    }

    /// <summary>
    /// Flow tier — <c>reload</c> refusal (spawn-authoring T9): a shape-error corpus yields one error
    /// line per file and publishes neither post-load event; a clean reload still publishes both.
    /// </summary>
    public sealed class ReloadCommandRefusalTests
    {
        private static readonly IReadOnlyDictionary<string, object?> NoArgs = new Dictionary<string, object?>();

        [Fact]
        public async Task Reload_over_shape_errors_reports_each_file_and_publishes_nothing()
        {
            using var w = new SpawnWorld();
            w.Mob("mob.rat");
            w.Room("room.cellar", "  - blueprintId: mob.rat\n");
            await w.BootAsync();
            var entitiesBefore = w.Ecs.GetAllComponents<Hedron.Core.ECS.Components.BlueprintComponent>().Select(e => e.EntityId).ToList();
            var publishedBefore = w.Bus.Published.Count;

            w.Room("room.legacy", "  - blueprintId: mob.rat\n    maxCount: 3\n");
            w.Item("item.legacy", "spawnRoomId: room.cellar\n");
            await w.RunAsync(w.ReloadCommand(), NoArgs);

            var error = Assert.Single(w.Output.All.Select(r => r.Message).OfType<PlainMessage>());
            Assert.Equal(OutputSeverity.Error, error.Severity);
            Assert.Contains("rooms/room.legacy.yaml", error.Text);
            Assert.Contains("items/item.legacy.yaml", error.Text);

            var published = w.Bus.Published.Skip(publishedBefore).ToList();
            Assert.DoesNotContain(published, e => e is WorldContentReadyEvent);
            Assert.DoesNotContain(published, e => e is ContentReloadedEvent);
            Assert.Equal(entitiesBefore, w.Ecs.GetAllComponents<Hedron.Core.ECS.Components.BlueprintComponent>().Select(e => e.EntityId));
        }

        [Fact]
        public async Task A_clean_reload_still_publishes_both_events()
        {
            using var w = new SpawnWorld();
            w.Room("room.cellar");
            await w.BootAsync();
            var publishedBefore = w.Bus.Published.Count;

            await w.RunAsync(w.ReloadCommand(), NoArgs);

            var published = w.Bus.Published.Skip(publishedBefore).ToList();
            Assert.Single(published.OfType<WorldContentReadyEvent>());
            Assert.Single(published.OfType<ContentReloadedEvent>());
        }
    }
}
