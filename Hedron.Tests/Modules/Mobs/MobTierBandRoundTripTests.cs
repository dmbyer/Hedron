using System.IO;
using System.Threading.Tasks;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Modules.Mobs
{
    /// <summary>
    /// Tier 4 — YAML round-trip tests for <see cref="MobTemplate.TierBand"/>.
    ///
    /// Coverage contract: docs/implementation-plans/ascension.md WP-3 Test plan:
    ///
    ///   • write→YAML→read preserves the band value.
    ///   • Unbanded (0) yields no <c>band:</c> key in the written YAML, and absent/null reads back as 0.
    ///   • Out-of-range value in raw YAML is logged and defaults to 0 (does not throw).
    ///
    /// Tests <see cref="MobContentWriter"/> (write) → <see cref="MobTemplateDeserializer"/> (read).
    /// Models <see cref="MobProtectionRoundTripTests"/>.
    /// </summary>
    public sealed class MobTierBandRoundTripTests : System.IDisposable
    {
        private readonly string _tempDir;

        public MobTierBandRoundTripTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-mob-band-test-{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private MobContentWriter BuildWriter()
        {
            var options = Options.Create(new WorldOptions
            {
                ContentDirectory = _tempDir,
            });
            return new MobContentWriter(options);
        }

        private static MobTemplateDeserializer BuildDeserializer()
            => new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance);

        private async Task<(MobTemplate Loaded, string Yaml)> RoundTrip(MobTemplate original)
        {
            var writer = BuildWriter();
            await writer.WriteAsync(original);

            var yamlPath = Path.Combine(_tempDir, "mobs", $"{original.BlueprintId}.yaml");
            var yaml = await File.ReadAllTextAsync(yamlPath);

            var deserializer = BuildDeserializer();
            return ((MobTemplate)deserializer.Deserialize(yaml), yaml);
        }

        // ── Round-trip: band survives write → read ────────────────────────────────

        [Fact]
        public async Task Band_survives_write_then_read()
        {
            var original = new MobTemplate("mob.banded.test")
            {
                Name = "Trash",
                TierBand = 3,
            };

            var (loaded, _) = await RoundTrip(original);

            Assert.Equal(3, loaded.TierBand);
        }

        [Fact]
        public async Task Band_zero_writes_a_null_band_value_and_reads_back_as_zero()
        {
            // Mirrors the existing `protection:`/`currencyLoot:` precedent: the YAML serializer
            // writes the key with a blank/null value rather than omitting it (DoesNotContain on
            // the key would be wrong — the DTO's null-when-zero contract is about the *value*).
            var original = new MobTemplate("mob.unbanded.test")
            {
                Name = "Ordinary Mob",
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
            const string yaml = @"blueprintId: mob.nokey.test
name: No Band Key
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.TierBand);
        }

        // ── Out-of-range: logged and defaulted to 0, no throw ────────────────────

        [Fact]
        public void Deserialize_with_out_of_range_band_defaults_to_zero_and_does_not_throw()
        {
            const string yaml = @"blueprintId: mob.stale-band.test
name: Stale Band Mob
band: 42
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.TierBand);
        }

        [Fact]
        public void Deserialize_with_negative_band_defaults_to_zero_and_does_not_throw()
        {
            const string yaml = @"blueprintId: mob.negative-band.test
name: Negative Band Mob
band: -1
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.TierBand);
        }

        // ── Apply: MobDataComponent.TierBand seeded from the template ────────────

        [Fact]
        public async Task Apply_seeds_MobDataComponent_TierBand_from_template()
        {
            var original = new MobTemplate("mob.apply-band.test")
            {
                Name = "Banded Mob",
                TierBand = 2,
            };

            var (loaded, _) = await RoundTrip(original);

            var ecs = new Hedron.Core.ECS.EntityService();
            var entity = ecs.CreateEntity();
            loaded.Apply(entity, ecs);

            var mob = ecs.Get<MobDataComponent>(entity.Id);
            Assert.Equal(2, mob.TierBand);
        }
    }
}
