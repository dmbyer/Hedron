using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Systems;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// System-unit tests (temp dir) for <see cref="WorldManifestStore"/>: missing / round-trip /
    /// malformed / unknown-key / null-omission behavior of <c>world.yaml</c>.
    /// </summary>
    public sealed class WorldManifestStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly WorldManifestStore _store;

        public WorldManifestStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-manifest-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _store = new WorldManifestStore(Options.Create(new WorldOptions { ContentDirectory = _tempDir }));
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void Missing_file_reads_as_empty_without_error()
        {
            var result = _store.Read();

            Assert.Same(WorldManifest.Empty, result.Manifest);
            Assert.False(result.Exists);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task Write_then_read_round_trips_known_fields()
        {
            await _store.WriteAsync(new WorldManifest(1, "room.hub.square"));

            var result = _store.Read();

            Assert.True(result.Exists);
            Assert.Null(result.Error);
            Assert.Equal(1, result.Manifest.SchemaVersion);
            Assert.Equal("room.hub.square", result.Manifest.StartingRoomId);
        }

        [Fact]
        public void Malformed_yaml_reads_as_empty_with_error_and_does_not_throw()
        {
            File.WriteAllText(_store.FilePath, "startingRoomId: [unclosed\n  : : :");

            var result = _store.Read();

            Assert.Same(WorldManifest.Empty, result.Manifest);
            Assert.True(result.Exists);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task Unknown_key_is_ignored_on_read_and_absent_after_rewrite()
        {
            File.WriteAllText(_store.FilePath, "schemaVersion: 1\nstartingRoomId: room.a\ndisplayName: Somewhere\n");

            var read = _store.Read();
            Assert.Null(read.Error);
            Assert.Equal("room.a", read.Manifest.StartingRoomId);

            await _store.WriteAsync(read.Manifest with { StartingRoomId = "room.b" });

            var body = File.ReadAllText(_store.FilePath);
            Assert.DoesNotContain("displayName", body);
            Assert.Equal("room.b", _store.Read().Manifest.StartingRoomId);
        }

        [Fact]
        public async Task Update_over_malformed_file_refuses_and_leaves_it_byte_identical()
        {
            File.WriteAllText(_store.FilePath, "startingRoomId: [unclosed\n  : : :");
            var before = File.ReadAllBytes(_store.FilePath);

            var result = await _store.UpdateAsync(m => m with { StartingRoomId = "room.a" });

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
            Assert.Equal(before, File.ReadAllBytes(_store.FilePath));
        }

        [Fact]
        public async Task Update_applies_change_and_defaults_missing_schema_version()
        {
            File.WriteAllText(_store.FilePath, "startingRoomId: room.a\n");

            var result = await _store.UpdateAsync(m => m with { StartingRoomId = "room.b" });

            Assert.True(result.Success);
            var read = _store.Read();
            Assert.Equal("room.b", read.Manifest.StartingRoomId);
            Assert.Equal(WorldManifest.CurrentSchemaVersion, read.Manifest.SchemaVersion);
        }

        [Fact]
        public async Task Null_starting_room_is_omitted_from_file_and_reads_back_null()
        {
            await _store.WriteAsync(new WorldManifest(1, null));

            var body = File.ReadAllText(_store.FilePath);
            Assert.DoesNotContain("startingRoomId", body);
            Assert.Null(_store.Read().Manifest.StartingRoomId);
        }
    }
}
