using System.IO;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Items;
using Hedron.Core.Modules.Items.Systems;
using Hedron.Core.Modules.Items.Templates;
using Hedron.Core.Modules.World;
using Hedron.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Items
{
    /// <summary>
    /// Tier 4 — YAML round-trip tests for <see cref="ItemTemplate.TierBand"/>.
    ///
    /// Coverage contract: docs/implementation-plans/power-budget-inspector.md WP-3 Test plan:
    ///
    ///   • write→YAML→read preserves the band value.
    ///   • Unbanded (0) yields no non-zero <c>band:</c> key in the written YAML, and absent/null
    ///     reads back as 0.
    ///   • Out-of-range value in raw YAML is logged and defaults to 0 (does not throw).
    ///
    /// Tests <see cref="ItemContentWriter"/> (write) → <see cref="ItemTemplateDeserializer"/> (read).
    /// Mirrors <see cref="Hedron.Tests.Modules.Mobs.MobTierBandRoundTripTests"/>.
    /// </summary>
    public sealed class ItemTierBandRoundTripTests : System.IDisposable
    {
        private readonly string _tempDir;

        public ItemTierBandRoundTripTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-item-band-test-{System.Guid.NewGuid():N}");
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

        private async Task<(ItemTemplate Loaded, string Yaml)> RoundTrip(ItemTemplate original)
        {
            var writer = BuildWriter();
            await writer.WriteAsync(original);

            var yamlPath = Path.Combine(_tempDir, "items", $"{original.BlueprintId}.yaml");
            var yaml = await File.ReadAllTextAsync(yamlPath);

            var deserializer = BuildDeserializer();
            return ((ItemTemplate)deserializer.Deserialize(yaml), yaml);
        }

        // ── Round-trip: band survives write → read ────────────────────────────────

        [Fact]
        public async Task Band_survives_write_then_read()
        {
            var original = new ItemTemplate("item.banded.test")
            {
                Name = "Ancient Blade",
                TierBand = 3,
            };

            var (loaded, _) = await RoundTrip(original);

            Assert.Equal(3, loaded.TierBand);
        }

        [Fact]
        public async Task Band_zero_writes_a_null_band_value_and_reads_back_as_zero()
        {
            // Mirrors the mob band precedent: the YAML serializer writes the key with a blank/null
            // value rather than omitting it — the DTO's null-when-zero contract is about the value.
            var original = new ItemTemplate("item.unbanded.test")
            {
                Name = "Ordinary Dagger",
                TierBand = 0,
            };

            var (loaded, yaml) = await RoundTrip(original);

            Assert.Contains("band:", yaml);
            Assert.DoesNotContain("band: 0", yaml);
            Assert.Equal(0, loaded.TierBand);
        }

        [Fact]
        public void Deserialize_with_absent_band_key_defaults_to_zero()
        {
            const string yaml = @"blueprintId: item.nokey.test
name: No Band Key
";
            var deserializer = BuildDeserializer();
            var loaded = (ItemTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.TierBand);
        }

        // ── Out-of-range: logged and defaulted to 0, no throw ────────────────────

        [Fact]
        public void Deserialize_with_out_of_range_band_defaults_to_zero_and_does_not_throw()
        {
            const string yaml = @"blueprintId: item.stale-band.test
name: Stale Band Item
band: 42
";
            var deserializer = BuildDeserializer();
            var loaded = (ItemTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.TierBand);
        }

        [Fact]
        public void Deserialize_with_negative_band_defaults_to_zero_and_does_not_throw()
        {
            const string yaml = @"blueprintId: item.negative-band.test
name: Negative Band Item
band: -1
";
            var deserializer = BuildDeserializer();
            var loaded = (ItemTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.TierBand);
        }

        // ── Apply: ItemDataComponent.TierBand seeded from the template ───────────

        [Fact]
        public async Task Apply_seeds_ItemDataComponent_TierBand_from_template()
        {
            var original = new ItemTemplate("item.apply-band.test")
            {
                Name = "Banded Item",
                TierBand = 2,
            };

            var (loaded, _) = await RoundTrip(original);

            var ecs = new Hedron.Core.ECS.EntityService();
            var entity = ecs.CreateEntity();
            loaded.Apply(entity, ecs);

            var item = ecs.Get<ItemDataComponent>(entity.Id);
            Assert.Equal(2, item.TierBand);
        }

        // ── Persistence (SQLite save→load) — TierBand rides the existing snapshot ─

        /// <summary>
        /// A player-owned item's <see cref="ItemDataComponent.TierBand"/> survives the SQLite
        /// save→reload round-trip — it rides the already-<c>[Persistent]</c> component exactly
        /// as <c>Value</c>/<c>StatBonuses</c> do (no new persistence wiring, per the WP-3
        /// persistence-opt-in audit).
        /// </summary>
        [Fact]
        public async Task ItemDataComponent_TierBand_survives_persistence_round_trip()
        {
            using var harness = new PersistenceTestHarness();
            var ecs = harness.EntityService;

            var entity = ecs.CreateEntity();
            ecs.AddComponent(entity.Id, new ItemDataComponent
            {
                Name = "Banded Heirloom",
                TierBand = 4,
            });
            ecs.AddComponent(entity.Id, new Hedron.Core.ECS.Components.PersistentEntity());

            await harness.SaveAsync(entity.Id);

            var fresh = await harness.ReloadIntoFreshWorld();

            var reloaded = fresh.Get<ItemDataComponent>(entity.Id);
            Assert.Equal(4, reloaded.TierBand);
        }
    }
}
