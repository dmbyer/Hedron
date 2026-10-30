using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Flow tests (temp content dir, real loader + registry + manifest store) for the manifest-aware
    /// starting-room resolution: <c>world.yaml</c> → <c>WorldConfiguration.StartingRoom</c> on load,
    /// reload, and an explicit <see cref="WorldContentLoader.ResolveStartingRoom"/>.
    /// </summary>
    public sealed class WorldContentLoaderStartingRoomTests : IDisposable
    {
        private readonly string _tempDir;

        public WorldContentLoaderStartingRoomTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-startroom-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_tempDir, "rooms"));
            Directory.CreateDirectory(Path.Combine(_tempDir, "mobs"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        // ── Harness ──────────────────────────────────────────────────────────────

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed record World(
            WorldContentLoader Loader,
            EntityService Ecs,
            TemplateRegistry Registry,
            WorldConfiguration Config,
            RecordingLogger<WorldContentLoader> Log);

        private World Build(string configStartingRoom = "room.cfg")
        {
            var ecs = new EntityService();
            var registry = new TemplateRegistry(ecs);
            var config = new WorldConfiguration();
            var log = new RecordingLogger<WorldContentLoader>();
            var options = Options.Create(new WorldOptions
            {
                ContentDirectory = _tempDir,
                StartingRoomBlueprintId = configStartingRoom,
            });
            var serializer = new YamlContentSerializer(new ITemplateDeserializer[]
            {
                new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance),
                new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance),
                new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance),
                new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance),
            });
            var loader = new WorldContentLoader(
                ecs, registry, serializer, new RoomContentWriter(options),
                new WorldManifestStore(options), config, options, log);
            return new World(loader, ecs, registry, config, log);
        }

        private void WriteRoom(string id) =>
            File.WriteAllText(Path.Combine(_tempDir, "rooms", $"{id}.yaml"),
                $"id: {id}\nname: {id}\ndescription: A test room.\n");

        private void WriteMob(string id) =>
            File.WriteAllText(Path.Combine(_tempDir, "mobs", $"{id}.yaml"),
                $"blueprintId: {id}\nname: {id}\ndescription: A test mob.\n");

        private void WriteManifest(string startingRoomId) =>
            File.WriteAllText(Path.Combine(_tempDir, "world.yaml"),
                $"schemaVersion: 1\nstartingRoomId: {startingRoomId}\n");

        private static uint EntityOf(EntityService ecs, string blueprintId) =>
            ecs.GetAllComponents<BlueprintComponent>()
               .Single(p => string.Equals(p.Item2.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase)).Item1;

        // ── Tests ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Manifest_naming_a_live_room_resolves_from_manifest()
        {
            WriteRoom("room.hub");
            WriteRoom("room.cfg");
            WriteManifest("room.hub");
            var w = Build();

            await w.Loader.LoadAndSpawnAsync();

            var start = w.Config.StartingRoom;
            Assert.Equal(StartingRoomSource.Manifest, start.Source);
            Assert.Equal("room.hub", start.BlueprintId);
            Assert.Equal(EntityOf(w.Ecs, "room.hub"), start.EntityId);
            Assert.Equal("room.hub", start.DeclaredId);
        }

        [Fact]
        public async Task Manifest_naming_a_mob_is_not_accepted_and_falls_to_configuration()
        {
            WriteRoom("room.cfg");
            WriteMob("mob.guard");
            WriteManifest("mob.guard");
            var w = Build();

            await w.Loader.LoadAndSpawnAsync();

            Assert.Equal(StartingRoomSource.Configuration, w.Config.StartingRoom.Source);
            Assert.Equal("room.cfg", w.Config.StartingRoom.BlueprintId);
            Assert.Equal("mob.guard", w.Config.StartingRoom.DeclaredId);
        }

        [Fact]
        public async Task Root_world_yaml_registers_no_template_and_logs_no_load_error()
        {
            WriteRoom("room.hub");
            WriteManifest("room.hub");
            var w = Build();

            await w.Loader.LoadAndSpawnAsync();

            Assert.Equal(new[] { "room.hub" }, w.Registry.AllBlueprintIds().ToArray());
            Assert.DoesNotContain(w.Log.Entries, e => e.Level >= LogLevel.Error);
        }

        [Fact]
        public async Task Reload_rereads_world_yaml_and_publishes_a_new_snapshot()
        {
            WriteRoom("room.a");
            WriteRoom("room.b");
            WriteManifest("room.a");
            var w = Build();
            await w.Loader.LoadAndSpawnAsync();
            var before = w.Config.StartingRoom;

            WriteManifest("room.b");
            await w.Loader.ReloadAsync();

            var after = w.Config.StartingRoom;
            Assert.NotSame(before, after);
            Assert.Equal("room.b", after.BlueprintId);
            Assert.Equal(EntityOf(w.Ecs, "room.b"), after.EntityId);
        }

        [Fact]
        public async Task ResolveStartingRoom_rereads_the_manifest_and_returns_the_published_snapshot()
        {
            WriteRoom("room.a");
            WriteRoom("room.b");
            WriteManifest("room.a");
            var w = Build();
            await w.Loader.LoadAndSpawnAsync();

            WriteManifest("room.b");
            var resolved = w.Loader.ResolveStartingRoom();

            Assert.Same(resolved, w.Config.StartingRoom);
            Assert.Equal("room.b", resolved.BlueprintId);
            Assert.Equal(StartingRoomSource.Manifest, resolved.Source);
        }

        [Fact]
        public async Task Content_with_no_rooms_is_unresolved_logs_an_error_and_does_not_throw()
        {
            WriteMob("mob.guard");
            var w = Build();

            await w.Loader.LoadAndSpawnAsync();

            Assert.Equal(StartingRoomSource.Unresolved, w.Config.StartingRoom.Source);
            Assert.Equal(0u, w.Config.StartingRoom.EntityId);
            Assert.Contains(w.Log.Entries, e => e.Level == LogLevel.Error);
        }

        [Fact]
        public async Task Malformed_manifest_is_logged_and_treated_as_empty()
        {
            WriteRoom("room.cfg");
            File.WriteAllText(Path.Combine(_tempDir, "world.yaml"), "startingRoomId: [unclosed\n  : : :");
            var w = Build();

            await w.Loader.LoadAndSpawnAsync();

            Assert.Equal(StartingRoomSource.Configuration, w.Config.StartingRoom.Source);
            Assert.Contains(w.Log.Entries, e => e.Level == LogLevel.Error);
        }
    }
}
