using System.IO;
using System.Threading.Tasks;
using Hedron.Core.ECS;
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
    /// Tier 4 — YAML round-trip tests for <see cref="MobTemplate.Protection"/>.
    ///
    /// Coverage contract: WP-2 Test plan (mob-protection.md):
    ///
    ///   • write→YAML→read preserves the flag set.
    ///   • <see cref="ProtectionFlags.None"/> / absent yields no <see cref="ProtectionComponent"/>
    ///     on <see cref="MobTemplate.Apply"/>.
    ///   • Unknown flag name in YAML is skipped (log-and-skip, does not throw).
    ///
    /// Tests <see cref="MobContentWriter"/> (write) → <see cref="MobTemplateDeserializer"/> (read)
    /// and <see cref="MobTemplate.Apply"/> (component seeding).
    /// Models <see cref="Hedron.Tests.Modules.Economy.MobCurrencyLootRoundTripTests"/>.
    /// </summary>
    public sealed class MobProtectionRoundTripTests : System.IDisposable
    {
        private readonly string _tempDir;

        public MobProtectionRoundTripTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-mob-prot-test-{System.Guid.NewGuid():N}");
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

        // ── Round-trip: flags survive write → read ────────────────────────────────

        [Fact]
        public async Task Protection_Untargetable_survives_write_then_read()
        {
            var original = new MobTemplate("mob.shopkeeper.test")
            {
                Name = "Shopkeeper",
                Protection = ProtectionFlags.Untargetable,
            };

            var loaded = await RoundTrip(original);

            Assert.Equal(ProtectionFlags.Untargetable, loaded.Protection);
        }

        [Fact]
        public async Task Protection_EffectImmune_survives_write_then_read()
        {
            var original = new MobTemplate("mob.effectimmune.test")
            {
                Name = "Immune Mob",
                Protection = ProtectionFlags.EffectImmune,
            };

            var loaded = await RoundTrip(original);

            Assert.Equal(ProtectionFlags.EffectImmune, loaded.Protection);
        }

        [Fact]
        public async Task Protection_BothFlags_survive_write_then_read()
        {
            var original = new MobTemplate("mob.protected.test")
            {
                Name = "Protected Shopkeeper",
                Protection = ProtectionFlags.Untargetable | ProtectionFlags.EffectImmune,
            };

            var loaded = await RoundTrip(original);

            Assert.Equal(ProtectionFlags.Untargetable | ProtectionFlags.EffectImmune, loaded.Protection);
        }

        [Fact]
        public async Task Protection_None_reads_back_as_None_after_round_trip()
        {
            // A template with Protection = None should survive write → read
            // with Protection still None (opt-in default: no protection flags).
            var original = new MobTemplate("mob.unprotected.test")
            {
                Name = "Ordinary Mob",
                Protection = ProtectionFlags.None,
            };

            var loaded = await RoundTrip(original);

            Assert.Equal(ProtectionFlags.None, loaded.Protection);
        }

        // ── Apply: ProtectionComponent is seeded only when flags != None ─────────

        [Fact]
        public async Task Apply_with_Untargetable_adds_ProtectionComponent_to_entity()
        {
            var original = new MobTemplate("mob.apply.test")
            {
                Name = "Target Test Mob",
                Protection = ProtectionFlags.Untargetable,
            };

            var loaded = await RoundTrip(original);

            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            loaded.Apply(entity, ecs);

            Assert.True(ecs.HasComponent<ProtectionComponent>(entity.Id));
            var comp = ecs.Get<ProtectionComponent>(entity.Id);
            Assert.Equal(ProtectionFlags.Untargetable, comp.Flags);
        }

        [Fact]
        public async Task Apply_with_None_does_not_add_ProtectionComponent_to_entity()
        {
            var original = new MobTemplate("mob.apply-none.test")
            {
                Name = "Unprotected Mob",
                Protection = ProtectionFlags.None,
            };

            var loaded = await RoundTrip(original);

            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            loaded.Apply(entity, ecs);

            Assert.False(ecs.HasComponent<ProtectionComponent>(entity.Id));
        }

        // ── Unknown flag name: logged and skipped, no throw ──────────────────────

        [Fact]
        public void Deserialize_with_unknown_protection_flag_skips_it_and_does_not_throw()
        {
            // Craft YAML that references an unknown flag name.
            const string yaml = @"blueprintId: mob.stale.test
name: Stale Mob
protection:
  - Untargetable
  - FutureFlag
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            // Known flag is preserved; unknown flag is silently skipped.
            Assert.Equal(ProtectionFlags.Untargetable, loaded.Protection);
        }

        [Fact]
        public void Deserialize_with_all_unknown_protection_flags_yields_None()
        {
            const string yaml = @"blueprintId: mob.alldead.test
name: All Dead Flags
protection:
  - DefinitelyNotAFlag
";
            var deserializer = BuildDeserializer();
            var loaded = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(ProtectionFlags.None, loaded.Protection);
        }
    }
}
