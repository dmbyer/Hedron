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
    /// Tier 4 — YAML round-trip tests for <see cref="MobTemplate.Tier"/>/<see cref="MobTemplate.Band"/>.
    ///
    /// Coverage contract: docs/roadmap/completed/power-model-revision.md Test plan:
    ///
    ///   • write→YAML→read preserves tier and band values.
    ///   • Unbanded (0) yields no <c>tier:</c>/<c>band:</c> key with a non-zero value in the
    ///     written YAML, and absent/null reads back as 0.
    ///   • Out-of-range value in raw YAML is logged and defaults to 0 (does not throw).
    ///   • Clean-break: a legacy single-axis <c>band:</c>-only file (no <c>tier:</c> key) in [1,3] is
    ///     reinterpreted as new-axis (tier 0, band N); in [4,6] warns-and-untags (Band = 0).
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

        // ── Round-trip: tier + band survive write → read ─────────────────────────

        [Fact]
        public async Task Tier_and_band_survive_write_then_read()
        {
            var original = new MobTemplate("mob.banded.test")
            {
                Name = "Trash",
                Tier = 4,
                Band = 3,
            };

            var (loaded, _) = await RoundTrip(original);

            Assert.Equal(4, loaded.Tier);
            Assert.Equal(3, loaded.Band);
        }

        [Fact]
        public async Task Tier_and_band_zero_write_null_values_and_read_back_as_zero()
        {
            // Mirrors the existing `protection:`/`currencyLoot:` precedent: the YAML serializer
            // writes the key with a blank/null value rather than omitting it (DoesNotContain on
            // the key would be wrong — the DTO's null-when-zero contract is about the *value*).
            var original = new MobTemplate("mob.unbanded.test")
            {
                Name = "Ordinary Mob",
                Tier = 0,
                Band = 0,
            };

            var (loaded, yaml) = await RoundTrip(original);

            Assert.Contains("tier:", yaml);
            Assert.DoesNotContain("tier: 0", yaml);
            Assert.Contains("band:", yaml);
            Assert.DoesNotContain("band: 0", yaml);
            Assert.Equal(0, loaded.Tier);
            Assert.Equal(0, loaded.Band);
        }

        [Fact]
        public void Deserialize_with_absent_tier_and_band_keys_defaults_to_zero()
        {
            const string yaml = @"blueprintId: mob.nokey.test
name: No Band Key
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.Tier);
            Assert.Equal(0, loaded.Band);
        }

        // ── Out-of-range: logged and defaulted to 0, no throw ────────────────────

        [Fact]
        public void Deserialize_with_out_of_range_tier_defaults_to_zero_and_does_not_throw()
        {
            const string yaml = @"blueprintId: mob.stale-tier.test
name: Stale Tier Mob
tier: 42
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.Tier);
        }

        [Fact]
        public void Deserialize_with_negative_tier_defaults_to_zero_and_does_not_throw()
        {
            const string yaml = @"blueprintId: mob.negative-tier.test
name: Negative Tier Mob
tier: -1
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.Tier);
        }

        [Fact]
        public void Deserialize_with_out_of_range_band_defaults_to_zero_and_does_not_throw()
        {
            const string yaml = @"blueprintId: mob.stale-band.test
name: Stale Band Mob
band: 42
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.Band);
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

            Assert.Equal(0, loaded.Band);
        }

        // ── Clean-break: legacy single-axis band: reinterpretation ───────────────

        [Fact]
        public void Deserialize_legacy_band_in_new_range_is_reinterpreted_as_tier_zero_band_n()
        {
            // A pre-revision file authored `band: 2` under the old single-axis 0-6 scale, with no
            // `tier:` key at all. Because the new axis reuses the same key name and its valid range
            // (0-3) happens to overlap [1,3], this is silently reinterpreted as (tier 0, band 2) —
            // not a clean wipe (see power-model-revision.md Design notes, "Clean-break field split").
            const string yaml = @"blueprintId: mob.legacy-band-low.test
name: Legacy Banded Mob
band: 2
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.Tier);
            Assert.Equal(2, loaded.Band);
        }

        [Fact]
        public void Deserialize_legacy_band_outside_new_range_warns_and_untags()
        {
            // A pre-revision `band: 5` (valid under the old 0-6 scale) falls outside the new 0-3
            // range and warns-and-untags rather than being reinterpreted.
            const string yaml = @"blueprintId: mob.legacy-band-high.test
name: Legacy Banded Mob
band: 5
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(0, loaded.Tier);
            Assert.Equal(0, loaded.Band);
        }

        // ── Apply: MobDataComponent seeded from the template ─────────────────────

        [Fact]
        public async Task Apply_seeds_MobDataComponent_Tier_and_Band_from_template()
        {
            var original = new MobTemplate("mob.apply-band.test")
            {
                Name = "Banded Mob",
                Tier = 2,
                Band = 1,
            };

            var (loaded, _) = await RoundTrip(original);

            var ecs = new Hedron.Core.ECS.EntityService();
            var entity = ecs.CreateEntity();
            loaded.Apply(entity, ecs);

            var mob = ecs.Get<MobDataComponent>(entity.Id);
            Assert.Equal(2, mob.Tier);
            Assert.Equal(1, mob.Band);
        }
    }
}
