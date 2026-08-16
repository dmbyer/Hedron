using System.IO;
using System.Threading.Tasks;
using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Mobs;
using Hedron.Core.Modules.Mobs.Systems;
using Hedron.Core.Modules.Mobs.Templates;
using Hedron.Core.Modules.World;
using Hedron.Core.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hedron.Tests.Modules.Mobs
{
    /// <summary>
    /// Tier 4 — YAML round-trip for <see cref="MobTemplate.XpScale"/>, the per-mob granular
    /// progression knob (R7), plus the spawn-time apply onto
    /// <see cref="MobDataComponent.XpScale"/>.
    ///
    /// Coverage contract: the Content-tooling section of
    /// docs/implementation-plans/progression-use-based-xp.md. Models
    /// <see cref="MobTierBandRoundTripTests"/>.
    /// </summary>
    public sealed class MobXpScaleRoundTripTests : System.IDisposable
    {
        private readonly string _tempDir;

        public MobXpScaleRoundTripTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hedron-mob-xpscale-test-{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private async Task<(MobTemplate Loaded, string Yaml)> RoundTrip(MobTemplate original)
        {
            var writer = new MobContentWriter(Options.Create(new WorldOptions { ContentDirectory = _tempDir }));
            await writer.WriteAsync(original);

            var yamlPath = Path.Combine(_tempDir, "mobs", $"{original.BlueprintId}.yaml");
            var yaml = await File.ReadAllTextAsync(yamlPath);

            var deserializer = new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance);
            return ((MobTemplate)deserializer.Deserialize(yaml), yaml);
        }

        [Fact]
        public async Task XpScale_survives_write_then_read()
        {
            var (loaded, yaml) = await RoundTrip(new MobTemplate("mob.xpscale.test")
            {
                Name = "Rich rat",
                XpScale = 2.5,
            });

            Assert.Equal(2.5, loaded.XpScale);
            Assert.Contains("xpScale", yaml);
        }

        [Fact]
        public async Task A_zero_scale_survives_and_is_not_confused_with_the_default()
        {
            var (loaded, _) = await RoundTrip(new MobTemplate("mob.worthless.test")
            {
                Name = "Worthless rat",
                XpScale = 0.0,
            });

            Assert.Equal(0.0, loaded.XpScale);
        }

        [Fact]
        public async Task The_default_scale_writes_an_empty_key_and_reads_back_as_one()
        {
            // The writer nulls the DTO field at the default, and the YAML serializer renders a
            // null as a valueless key ("xpScale:") — the same shape tier/band already produce.
            var (loaded, yaml) = await RoundTrip(new MobTemplate("mob.default.test") { Name = "Plain rat" });

            Assert.Contains("xpScale:", yaml);
            Assert.DoesNotContain("xpScale: 1", yaml);
            Assert.Equal(1.0, loaded.XpScale);
        }

        [Fact]
        public void A_negative_scale_in_raw_yaml_defaults_to_one_without_throwing()
        {
            const string yaml = "blueprintId: mob.bad.test\nname: Bad\nxpScale: -3.0\n";
            var deserializer = new MobTemplateDeserializer(NullLogger<MobTemplateDeserializer>.Instance);

            var template = (MobTemplate)deserializer.Deserialize(yaml);

            Assert.Equal(1.0, template.XpScale);
        }

        [Fact]
        public void Apply_stamps_the_template_scale_onto_the_spawned_entity()
        {
            var ecs = new EntityService();
            var entity = ecs.CreateEntity();

            new MobTemplate("mob.spawn.test") { Name = "Rat", XpScale = 0.25 }.Apply(entity, ecs);

            Assert.Equal(0.25, ecs.Get<MobDataComponent>(entity.Id).XpScale);
        }

        [Fact]
        public void The_builder_dual_writes_the_entity_and_the_template()
        {
            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            var template = new MobTemplate("mob.dual.test") { Name = "Rat" };
            template.Apply(entity, ecs);
            ecs.AddComponent(entity.Id, new BlueprintComponent { BlueprintId = template.BlueprintId });

            var registry = new TemplateRegistry(ecs);
            registry.Register(template.BlueprintId, template);
            var builder = new MobBuilderSystem(ecs, registry, NullLogger<MobBuilderSystem>.Instance);

            builder.SetMobXpScale(entity.Id, 3.0);

            Assert.Equal(3.0, ecs.Get<MobDataComponent>(entity.Id).XpScale);
            Assert.Equal(3.0, template.XpScale);
        }
    }
}
