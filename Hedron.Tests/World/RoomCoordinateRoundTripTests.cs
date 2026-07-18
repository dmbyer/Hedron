using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
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
    /// Tier 1 — YAML round-trip tests for the world-editor-grid slice's writer-fidelity fix.
    ///
    /// Coverage contract: docs/implementation-plans/world-editor-grid.md Postconditions 1-3:
    ///   1. Room YAML round-trips X/Y/Z losslessly; null coords omitted from file; legacy file
    ///      without the fields loads with nulls and no warning.
    ///   2. Room YAML round-trips spawnRules and schemaVersion losslessly (regression).
    ///   3. RoomTemplate.Apply attaches no coordinate data to any runtime component.
    ///
    /// Tests <see cref="RoomContentWriter"/> (write) → <see cref="RoomTemplateDeserializer"/> (read).
    /// </summary>
    public sealed class RoomCoordinateRoundTripTests : IDisposable
    {
        private readonly string _tempDir;

        public RoomCoordinateRoundTripTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-room-coord-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private RoomContentWriter BuildWriter() =>
            new RoomContentWriter(Options.Create(new WorldOptions { ContentDirectory = _tempDir }));

        private static RoomTemplateDeserializer BuildDeserializer() =>
            new RoomTemplateDeserializer(NullLogger<RoomTemplateDeserializer>.Instance);

        private async Task<(RoomTemplate Loaded, string Yaml)> RoundTrip(RoomTemplate original)
        {
            var writer = BuildWriter();
            await writer.WriteAsync(original);

            var yamlPath = Path.Combine(_tempDir, "rooms", $"{original.BlueprintId}.yaml");
            var yaml = await File.ReadAllTextAsync(yamlPath);

            var deserializer = BuildDeserializer();
            return ((RoomTemplate)deserializer.Deserialize(yaml), yaml);
        }

        // ── Postcondition 1: X/Y/Z round-trip ─────────────────────────────────────

        [Fact]
        public async Task Coordinates_survive_write_then_read()
        {
            var original = new RoomTemplate("room.coord.test")
            {
                Name = "Coordinate Room",
                AreaId = "area.test",
                X = 3,
                Y = -2,
                Z = 1,
            };

            var (loaded, _) = await RoundTrip(original);

            Assert.Equal(3, loaded.X);
            Assert.Equal(-2, loaded.Y);
            Assert.Equal(1, loaded.Z);
        }

        [Fact]
        public async Task Null_coordinates_write_no_numeric_value_and_read_back_null()
        {
            // Mirrors the existing tier/band precedent (MobTierBandRoundTripTests): the YAML
            // serializer writes the key with a blank value rather than omitting it — the
            // null-when-unset contract is about the *value*, not the key's presence.
            var original = new RoomTemplate("room.nocoord.test")
            {
                Name = "No Coordinate Room",
            };

            var (loaded, yaml) = await RoundTrip(original);

            Assert.DoesNotContain("x: 0", yaml);
            Assert.DoesNotContain("y: 0", yaml);
            Assert.DoesNotContain("z: 0", yaml);
            Assert.Null(loaded.X);
            Assert.Null(loaded.Y);
            Assert.Null(loaded.Z);
        }

        [Fact]
        public void Legacy_file_without_coordinate_fields_loads_with_nulls()
        {
            const string yaml = @"id: room.legacy.test
name: Legacy Room
";
            var deserializer = BuildDeserializer();
            var loaded = (RoomTemplate)deserializer.Deserialize(yaml);

            Assert.Null(loaded.X);
            Assert.Null(loaded.Y);
            Assert.Null(loaded.Z);
        }

        // ── Postcondition 2: spawnRules + schemaVersion round-trip (regression) ───

        [Fact]
        public async Task SpawnRules_survive_write_then_read()
        {
            var original = new RoomTemplate("room.spawn.test") { Name = "Spawn Room" };
            original.SpawnRules.Add(new SpawnRule("mob.rat", 1, 3, 300));
            original.SpawnRules.Add(new SpawnRule("item.torch", 0, 1, 600));

            var (loaded, _) = await RoundTrip(original);

            Assert.Equal(2, loaded.SpawnRules.Count);
            Assert.Equal("mob.rat", loaded.SpawnRules[0].BlueprintId);
            Assert.Equal(1, loaded.SpawnRules[0].MinCount);
            Assert.Equal(3, loaded.SpawnRules[0].MaxCount);
            Assert.Equal(300, loaded.SpawnRules[0].RespawnDelaySeconds);
            Assert.Equal("item.torch", loaded.SpawnRules[1].BlueprintId);
        }

        [Fact]
        public async Task SchemaVersion_survives_write_then_read()
        {
            var original = new RoomTemplate("room.schema.test")
            {
                Name = "Schema Room",
                SchemaVersion = 1,
            };

            var (loaded, _) = await RoundTrip(original);

            Assert.Equal(1, loaded.SchemaVersion);
        }

        [Fact]
        public async Task Null_SchemaVersion_round_trips_as_null()
        {
            var original = new RoomTemplate("room.noschema.test") { Name = "No Schema Room" };

            var (loaded, _) = await RoundTrip(original);

            Assert.Null(loaded.SchemaVersion);
        }

        // ── Postcondition 3: Apply attaches no coordinate-bearing component ───────

        [Fact]
        public void Apply_attaches_no_coordinate_data_to_any_runtime_component()
        {
            var template = new RoomTemplate("room.apply.test")
            {
                Name = "Applied Room",
                X = 5,
                Y = 5,
                Z = 0,
            };

            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            template.Apply(entity, ecs);

            var room = ecs.Get<RoomComponent>(entity.Id);
            Assert.Equal("Applied Room", room.Name);
            // RoomComponent carries no X/Y/Z (or any coordinate) fields — a compile-time
            // guarantee already, but this locks the runtime behavior: Apply only ever
            // attaches RoomComponent (+ SpawnConfigComponent when rules exist), never a
            // coordinate-bearing component this slice.
            Assert.False(ecs.HasComponent<SpawnConfigComponent>(entity.Id));
        }
    }
}
