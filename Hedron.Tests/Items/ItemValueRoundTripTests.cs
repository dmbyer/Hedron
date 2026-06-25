using System;
using System.IO;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Systems;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Items
{
    /// <summary>
    /// Tier 4 — content YAML and persistence round-trip tests for <c>ItemDataComponent.Value</c>
    /// (item-value WP1, docs/implementation-plans/item-value.md).
    ///
    /// Covers:
    ///   - <see cref="ItemContentWriter.WriteAsync"/> → <see cref="ItemTemplateDeserializer.Deserialize"/>
    ///     preserves <c>Value</c> (including the absent-field-→-0 backward-compat case).
    ///   - Save → reload via <see cref="PersistenceTestHarness"/> preserves <c>ItemDataComponent.Value</c>.
    /// </summary>
    public sealed class ItemValueRoundTripTests : IDisposable
    {
        private readonly string _tempDir;

        public ItemValueRoundTripTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-item-value-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private ItemContentWriter BuildWriter()
        {
            var options = Options.Create(new WorldOptions { ContentDirectory = _tempDir });
            return new ItemContentWriter(options);
        }

        private static ItemTemplateDeserializer BuildDeserializer()
            => new ItemTemplateDeserializer(NullLogger<ItemTemplateDeserializer>.Instance);

        private async Task<ItemTemplate> RoundTripYaml(ItemTemplate original)
        {
            var writer = BuildWriter();
            await writer.WriteAsync(original);

            var yamlPath = Path.Combine(_tempDir, "items", $"{original.BlueprintId}.yaml");
            var yaml = await File.ReadAllTextAsync(yamlPath);

            var deserializer = BuildDeserializer();
            return (ItemTemplate)deserializer.Deserialize(yaml);
        }

        // ── Content (YAML) round-trip tests ──────────────────────────────────────

        /// <summary>
        /// A non-zero <c>Value</c> on an <see cref="ItemTemplate"/> survives
        /// <c>ItemContentWriter.WriteAsync</c> → <c>ItemTemplateDeserializer.Deserialize</c>
        /// with the same value.
        /// </summary>
        [Fact]
        public async Task ItemValue_nonzero_survives_YAML_round_trip()
        {
            var original = new ItemTemplate("item.sword.test")
            {
                Name = "Iron Sword",
                Value = 250L,
            };

            var loaded = await RoundTripYaml(original);

            Assert.Equal(250L, loaded.Value);
        }

        /// <summary>
        /// A <c>Value</c> of zero round-trips cleanly (valueless sentinel is preserved, not dropped).
        /// </summary>
        [Fact]
        public async Task ItemValue_zero_survives_YAML_round_trip()
        {
            var original = new ItemTemplate("item.pebble.test")
            {
                Name = "Pebble",
                Value = 0L,
            };

            var loaded = await RoundTripYaml(original);

            Assert.Equal(0L, loaded.Value);
        }

        /// <summary>
        /// A YAML file without a <c>value:</c> field (pre-WP1 files) deserializes to <c>Value == 0</c>.
        /// Backward compatibility: existing items authored before this field default to "valueless".
        /// </summary>
        [Fact]
        public void ItemValue_absent_field_in_YAML_deserializes_to_zero()
        {
            // A minimal item YAML with no "value:" key — mirrors any item file authored before WP1.
            const string yaml = """
                blueprintId: item.legacy.test
                name: Legacy Item
                description: An old item.
                keywords: []
                itemType: None
                spawnRoomId: ''
                """;

            var deserializer = BuildDeserializer();
            var template = (ItemTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0L, template.Value);
        }

        /// <summary>
        /// A large <c>Value</c> (max representable price) round-trips without truncation.
        /// </summary>
        [Fact]
        public async Task ItemValue_large_value_survives_YAML_round_trip()
        {
            const long bigValue = 1_000_000_000L;
            var original = new ItemTemplate("item.artifact.test")
            {
                Name = "Legendary Artifact",
                Value = bigValue,
            };

            var loaded = await RoundTripYaml(original);

            Assert.Equal(bigValue, loaded.Value);
        }

        // ── Persistence (SQLite save→load) round-trip tests ──────────────────────

        /// <summary>
        /// An item entity with a non-zero <c>ItemDataComponent.Value</c> saved then reloaded
        /// preserves <c>Value</c> (confirms the field participates in the <c>[Persistent]</c> snapshot).
        /// </summary>
        [Fact]
        public async Task ItemDataComponent_Value_survives_persistence_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var entity = ecs.CreateEntity();
            ecs.AddComponent(entity.Id, new ItemDataComponent
            {
                Name = "Magic Staff",
                Value = 1500L,
            });
            ecs.AddComponent(entity.Id, new PersistentEntity());

            await harness.SaveAsync(entity.Id);

            var fresh = await harness.ReloadIntoFreshWorld();

            Assert.True(fresh.HasComponent<ItemDataComponent>(entity.Id),
                "ItemDataComponent must survive round-trip (INV-14).");
            var reloaded = fresh.Get<ItemDataComponent>(entity.Id);
            Assert.Equal(1500L, reloaded.Value);
        }

        /// <summary>
        /// A <c>Value == 0</c> (the "valueless" default) is preserved, not silently dropped.
        /// </summary>
        [Fact]
        public async Task ItemDataComponent_Value_zero_survives_persistence_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var entity = ecs.CreateEntity();
            ecs.AddComponent(entity.Id, new ItemDataComponent
            {
                Name = "Dirt Clump",
                Value = 0L,
            });
            ecs.AddComponent(entity.Id, new PersistentEntity());

            await harness.SaveAsync(entity.Id);

            var fresh = await harness.ReloadIntoFreshWorld();

            var reloaded = fresh.Get<ItemDataComponent>(entity.Id);
            Assert.Equal(0L, reloaded.Value);
        }
    }
}
