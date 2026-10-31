using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Fixture guard for the committed <c>content/dev-world/</c> (the primary playable baseline):
    /// it boots on its own manifest, the hub area is non-PvP, every room is placed on the grid,
    /// and the room graph is strongly connected from the start (R6). Runs against a temp copy so
    /// the loader can never write into the committed tree. Keeps paying as content drops grow it.
    /// </summary>
    public sealed class DevWorldFixtureTests : IDisposable
    {
        private readonly string _tempDir;

        public DevWorldFixtureTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-devworld-{Guid.NewGuid():N}");
            CopyDirectory(Path.Combine(FindRepoRoot(), "content", "dev-world"), _tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hedron.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Hedron.sln not found above the test output.");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        private async Task<(TemplateRegistry Registry, WorldConfiguration Config)> BootAsync()
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var config = new WorldConfiguration();
            // Deliberately a config id that exists nowhere: the start must come from the manifest.
            var options = Options.Create(new WorldOptions
            {
                ContentDirectory = _tempDir,
                StartingRoomBlueprintId = "room.not-in-dev-world",
            });
            var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
            {
                new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
                new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
                new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance),
            });
            var loader = new WorldContentLoader(
                ecs, registry, serializer, new RoomContentWriter(options),
                new WorldManifestStore(options), config, options, NullLogger<WorldContentLoader>.Instance);
            await loader.LoadAndSpawnAsync();
            return (registry, config);
        }

        private static List<RoomTemplate> Rooms(TemplateRegistry registry) =>
            registry.AllBlueprintIds()
                .Select(id => registry.TryGet(id, out var t) ? t : null)
                .OfType<RoomTemplate>()
                .ToList();

        [Fact]
        public async Task DevWorld_starts_from_its_manifest_in_the_hub_square()
        {
            var (_, config) = await BootAsync();

            Assert.Equal(StartingRoomSource.Manifest, config.StartingRoom.Source);
            Assert.Equal("room.hub.square", config.StartingRoom.BlueprintId);
            Assert.NotEqual(0u, config.StartingRoom.EntityId);
        }

        [Fact]
        public async Task Hub_area_exists_and_is_not_pvp()
        {
            var (registry, _) = await BootAsync();

            Assert.True(registry.TryGet("area.hub", out var template));
            Assert.False(Assert.IsType<AreaTemplate>(template).Pvp);
        }

        [Fact]
        public async Task Every_room_has_grid_coordinates_and_there_is_no_void_room()
        {
            var (registry, _) = await BootAsync();
            var rooms = Rooms(registry);

            Assert.NotEmpty(rooms);
            Assert.All(rooms, r => Assert.True(
                r.X is not null && r.Y is not null && r.Z is not null, $"{r.BlueprintId} lacks x/y/z"));
            Assert.DoesNotContain(rooms, r => r.BlueprintId == StartingRoomResolver.VoidRoomBlueprintId);
        }

        [Fact]
        public async Task Every_room_is_reachable_from_the_start_and_can_return()
        {
            var (registry, config) = await BootAsync();
            var rooms = Rooms(registry);
            var start = config.StartingRoom.BlueprintId!;

            var forward = rooms.ToDictionary(r => r.BlueprintId, r => r.Exits.Values.ToList(), StringComparer.OrdinalIgnoreCase);
            var reverse = rooms.ToDictionary(r => r.BlueprintId, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var room in rooms)
                foreach (var target in room.Exits.Values)
                    if (reverse.TryGetValue(target, out var sources))
                        sources.Add(room.BlueprintId);

            var reachable = Bfs(start, forward);
            var canReturn = Bfs(start, reverse);

            Assert.All(rooms, r => Assert.True(reachable.Contains(r.BlueprintId), $"{r.BlueprintId} is unreachable from {start}"));
            Assert.All(rooms, r => Assert.True(canReturn.Contains(r.BlueprintId), $"{r.BlueprintId} has no path back to {start}"));
        }

        [Fact]
        public async Task Hub_stays_undesignated_and_the_world_has_no_out_of_designation_placements()
        {
            var (registry, _) = await BootAsync();

            Assert.True(registry.TryGet("area.hub", out var hub));
            Assert.Null(Assert.IsType<AreaTemplate>(hub).Designation);
            Assert.Empty(new AreaPlacementSystem(registry).Findings());
        }

        private static HashSet<string> Bfs(string start, IReadOnlyDictionary<string, List<string>> edges)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                if (!edges.TryGetValue(queue.Dequeue(), out var next)) continue;
                foreach (var n in next)
                    if (seen.Add(n))
                        queue.Enqueue(n);
            }
            return seen;
        }
    }
}
