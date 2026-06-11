using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World.Templates;
using Hedron.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.Authoring
{
    /// <summary>
    /// Round-trip and run-mode tests for the bulk generator: emitted YAML deserializes through the
    /// existing deserializers, a valid profile run exits 0, and a missing/malformed profile exits
    /// non-zero. These drive the real <see cref="GenerationRunMode"/> over a temp content directory.
    /// </summary>
    public sealed class GenerationRunModeTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        private string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hedron-gen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        private static IConfiguration ConfigFor(string contentDirectory) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["World:ContentDirectory"] = contentDirectory,
                })
                .Build();

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best-effort temp cleanup */ }
            }
        }

        private static string WriteProfile(string dir, string body)
        {
            var path = Path.Combine(dir, "profile.yaml");
            File.WriteAllText(path, body);
            return path;
        }

        private const string ValidProfile = """
            seed: 1234
            areaCount: 2
            roomsPerArea:
              min: 2
              max: 3
            levelRange:
              min: 1
              max: 5
            mobDensity: 1.0
            itemDensity: 1.0
            scaling: Linear
            aspectMix:
              - aspect: Fire
                weight: 2
              - aspect: Ice
                weight: 1
            blueprintPrefix: "gen."
            """;

        // ── Round-trip: emitted YAML deserializes through the existing deserializers ────

        [Fact]
        public async Task ContentGeneration_EmittedYaml_RoundTrips()
        {
            var contentDir = NewTempDir();
            var profileDir = NewTempDir();
            var profilePath = WriteProfile(profileDir, ValidProfile);

            var exit = await GenerationRunMode.RunAsync(
                new[] { "generate", "--profile", profilePath }, ConfigFor(contentDir));

            Assert.Equal(0, exit);

            // Each emitted file deserializes without error through its existing deserializer.
            var areaDeser = new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(contentDir, "areas"), "*.yaml"))
            {
                var t = Assert.IsType<AreaTemplate>(areaDeser.Deserialize(File.ReadAllText(file)));
                Assert.False(string.IsNullOrEmpty(t.BlueprintId));
            }

            var roomDeser = new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance);
            var roomFiles = Directory.EnumerateFiles(Path.Combine(contentDir, "rooms"), "*.yaml").ToList();
            Assert.NotEmpty(roomFiles);
            foreach (var file in roomFiles)
            {
                var t = Assert.IsType<RoomTemplate>(roomDeser.Deserialize(File.ReadAllText(file)));
                Assert.False(string.IsNullOrEmpty(t.BlueprintId));
            }

            var mobDeser = new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(contentDir, "mobs"), "*.yaml"))
            {
                var t = Assert.IsType<MobTemplate>(mobDeser.Deserialize(File.ReadAllText(file)));
                Assert.True(t.Level >= 1);
                Assert.False(string.IsNullOrEmpty(t.SpawnRoomBlueprintId));
            }

            var itemDeser = new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(contentDir, "items"), "*.yaml"))
            {
                var t = Assert.IsType<ItemTemplate>(itemDeser.Deserialize(File.ReadAllText(file)));
                Assert.False(string.IsNullOrEmpty(t.SpawnRoomBlueprintId));
            }
        }

        [Fact]
        public async Task GenerationProfile_RoundTrips_DrivesExpectedRun()
        {
            // A sample profile YAML deserializes and drives a run whose counts honor it.
            var contentDir = NewTempDir();
            var profileDir = NewTempDir();
            var profilePath = WriteProfile(profileDir, """
                seed: 42
                areaCount: 3
                roomsPerArea:
                  min: 1
                  max: 1
                levelRange:
                  min: 2
                  max: 2
                mobDensity: 0
                itemDensity: 0
                blueprintPrefix: "gen."
                """);

            var exit = await GenerationRunMode.RunAsync(
                new[] { "generate", "--profile", profilePath }, ConfigFor(contentDir));

            Assert.Equal(0, exit);
            // 3 areas × exactly 1 room each, no mobs/items.
            Assert.Equal(3, Directory.EnumerateFiles(Path.Combine(contentDir, "areas"), "*.yaml").Count());
            Assert.Equal(3, Directory.EnumerateFiles(Path.Combine(contentDir, "rooms"), "*.yaml").Count());
            Assert.False(Directory.Exists(Path.Combine(contentDir, "mobs"))
                && Directory.EnumerateFiles(Path.Combine(contentDir, "mobs"), "*.yaml").Any());
        }

        // ── Determinism end-to-end: same seed → byte-identical files ───────────────────

        [Fact]
        public async Task ContentGeneration_SameSeed_ProducesIdenticalFiles()
        {
            var profileDir = NewTempDir();
            var profilePath = WriteProfile(profileDir, ValidProfile);

            var dirA = NewTempDir();
            var dirB = NewTempDir();

            Assert.Equal(0, await GenerationRunMode.RunAsync(
                new[] { "generate", "--profile", profilePath }, ConfigFor(dirA)));
            Assert.Equal(0, await GenerationRunMode.RunAsync(
                new[] { "generate", "--profile", profilePath }, ConfigFor(dirB)));

            foreach (var sub in new[] { "areas", "rooms", "mobs", "items" })
            {
                var pathA = Path.Combine(dirA, sub);
                var pathB = Path.Combine(dirB, sub);
                if (!Directory.Exists(pathA)) { Assert.False(Directory.Exists(pathB)); continue; }

                var filesA = Directory.EnumerateFiles(pathA, "*.yaml").Select(Path.GetFileName).OrderBy(x => x).ToList();
                var filesB = Directory.EnumerateFiles(pathB, "*.yaml").Select(Path.GetFileName).OrderBy(x => x).ToList();
                Assert.Equal(filesA, filesB);

                foreach (var name in filesA)
                    Assert.Equal(
                        File.ReadAllText(Path.Combine(pathA, name!)),
                        File.ReadAllText(Path.Combine(pathB, name!)));
            }
        }

        // ── Fail-fast: invalid / missing profile → non-zero exit ───────────────────────

        [Fact]
        public async Task GenerationRunMode_MissingProfileArg_NonZeroExit()
        {
            var exit = await GenerationRunMode.RunAsync(
                new[] { "generate" }, ConfigFor(NewTempDir()));
            Assert.NotEqual(0, exit);
        }

        [Fact]
        public async Task GenerationRunMode_MissingProfileFile_NonZeroExit()
        {
            var missing = Path.Combine(NewTempDir(), "does-not-exist.yaml");
            var exit = await GenerationRunMode.RunAsync(
                new[] { "generate", "--profile", missing }, ConfigFor(NewTempDir()));
            Assert.NotEqual(0, exit);
        }

        [Fact]
        public async Task GenerationRunMode_MalformedProfile_NonZeroExit()
        {
            var profileDir = NewTempDir();
            var profilePath = WriteProfile(profileDir, "aspectMix:\n  - aspect: NotAnAspect\n    weight: 1\n");

            var exit = await GenerationRunMode.RunAsync(
                new[] { "generate", "--profile", profilePath }, ConfigFor(NewTempDir()));
            Assert.NotEqual(0, exit);
        }

        [Fact]
        public void Matches_RecognizesGenerateToken_Only()
        {
            Assert.True(GenerationRunMode.Matches(new[] { "generate", "--profile", "p" }));
            Assert.False(GenerationRunMode.Matches(Array.Empty<string>()));
            Assert.False(GenerationRunMode.Matches(new[] { "serve" }));
        }
    }
}
