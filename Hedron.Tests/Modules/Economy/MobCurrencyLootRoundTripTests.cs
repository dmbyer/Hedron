using System.IO;
using System.Threading.Tasks;
using Hedron.Core.Modules.Economy;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Modules.Economy
{
    /// <summary>
    /// Tier 4 — YAML round-trip test for <see cref="MobTemplate.CurrencyLoot"/>.
    ///
    /// Coverage contract: MobEditor round-trip postcondition from
    /// docs/implementation-plans/currency-foundation.md (WP-3, Tier 4):
    ///
    ///   A saved-then-loaded MobTemplate.CurrencyLoot range survives the write → YAML → read cycle
    ///   with equal min/max values. Keys are stored by enum name (not ordinal) so future
    ///   CurrencyId reordering cannot corrupt the loot spec.
    ///
    /// Tests <see cref="MobContentWriter"/> (write) → <see cref="MobTemplateDeserializer"/> (read).
    /// </summary>
    public sealed class MobCurrencyLootRoundTripTests : System.IDisposable
    {
        private readonly string _tempDir;

        public MobCurrencyLootRoundTripTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-mob-test-{System.Guid.NewGuid():N}");
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

        private async Task<MobTemplate> RoundTrip(MobTemplate original)
        {
            var writer = BuildWriter();
            await writer.WriteAsync(original);

            var yamlPath = Path.Combine(_tempDir, "mobs", $"{original.BlueprintId}.yaml");
            var yaml = await File.ReadAllTextAsync(yamlPath);

            var deserializer = BuildDeserializer();
            return (MobTemplate)deserializer.Deserialize(yaml);
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CurrencyLoot_Coin_range_survives_write_then_read()
        {
            var original = new MobTemplate("mob.rat.test")
            {
                Name = "Rat",
            };
            original.CurrencyLoot[CurrencyId.Coin] = (Min: 10, Max: 50);

            var loaded = await RoundTrip(original);

            Assert.True(loaded.CurrencyLoot.ContainsKey(CurrencyId.Coin),
                "CurrencyLoot must contain CurrencyId.Coin after round-trip.");
            var range = loaded.CurrencyLoot[CurrencyId.Coin];
            Assert.Equal(10, range.Min);
            Assert.Equal(50, range.Max);
        }

        [Fact]
        public async Task CurrencyLoot_absent_on_template_without_loot_configured()
        {
            var original = new MobTemplate("mob.ghost.test")
            {
                Name = "Ghost",
            };
            // No CurrencyLoot entries

            var loaded = await RoundTrip(original);

            Assert.Empty(loaded.CurrencyLoot);
        }

        [Fact]
        public async Task CurrencyLoot_key_persisted_by_enum_name_not_ordinal()
        {
            // The YAML file must contain "Coin" (enum name), not "0" (ordinal).
            // This guards against future CurrencyId reordering corrupting stored loot specs.
            var original = new MobTemplate("mob.ordinalguard.test")
            {
                Name = "Guard",
            };
            original.CurrencyLoot[CurrencyId.Coin] = (Min: 5, Max: 20);

            var writer = BuildWriter();
            await writer.WriteAsync(original);

            var yamlPath = Path.Combine(_tempDir, "mobs", $"{original.BlueprintId}.yaml");
            var yaml = await File.ReadAllTextAsync(yamlPath);

            // "Coin" must appear in the YAML text; "0:" (ordinal) must not appear as a key.
            Assert.Contains("Coin", yaml);
            // Ensure it's specifically used as a dictionary key, not just coincidentally in the file.
            Assert.DoesNotContain("\"0\":", yaml);
            Assert.DoesNotContain("0: ", yaml.Replace("0:", string.Empty).Replace("min:", string.Empty).Replace("max:", string.Empty)
                .Replace("10", string.Empty).Replace("20", string.Empty).Replace("5:", string.Empty));
        }

        [Fact]
        public async Task CurrencyLoot_zero_max_range_is_excluded_from_YAML()
        {
            // A range with max=0 must NOT be written to YAML (opt-in default: no drop).
            var original = new MobTemplate("mob.zero.test")
            {
                Name = "Zero Mob",
            };
            original.CurrencyLoot[CurrencyId.Coin] = (Min: 0, Max: 0);

            var loaded = await RoundTrip(original);

            Assert.Empty(loaded.CurrencyLoot);
        }
    }
}
