using Hedron.Core.ECS;
using Hedron.Core.ECS.Components;
using Hedron.Core.Modules.Aspects;
using Hedron.Core.Modules.World;
using Hedron.Core.Modules.World.Components;
using Hedron.Core.Modules.World.Systems;
using Hedron.Core.Modules.World.Templates;
using Hedron.Core.Systems;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hedron.Tests.World
{
    /// <summary>
    /// Tier 1 — system-unit tests for <see cref="AreaTemplateDeserializer"/> aspect-affinity parsing.
    ///
    /// Validates that YAML aspect affinities round-trip through deserialisation and Apply.
    /// </summary>
    public sealed class AreaTemplateDeserializerTests
    {
        private static AreaTemplateDeserializer BuildDeserializer()
            => new AreaTemplateDeserializer(NullLogger<AreaTemplateDeserializer>.Instance);

        private static string BuildAreaYaml(string? aspectAffinitiesBlock = null)
        {
            var affinities = aspectAffinitiesBlock is null
                ? string.Empty
                : $"\naspectAffinities:\n{aspectAffinitiesBlock}";
            return $@"id: test.area
name: Test Area
description: A test area.{affinities}
";
        }

        // ── Parse valid aspect affinities ────────────────────────────────────────

        [Fact]
        public void AreaTemplateDeserializer_ParsesAspectAffinities()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml("  Fire: 60\n  Lightning: 40");

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            Assert.NotNull(template.AspectAffinities);
            Assert.Equal(2, template.AspectAffinities!.Count);
            Assert.Equal(60, template.AspectAffinities[AspectId.Fire]);
            Assert.Equal(40, template.AspectAffinities[AspectId.Lightning]);
        }

        [Fact]
        public void AreaTemplateDeserializer_Apply_AttachesAspectAffinitiesComponent()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml("  Fire: 60\n  Lightning: 40");

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            template.Apply(entity, ecs);

            Assert.True(ecs.TryGet<AspectAffinitiesComponent>(entity.Id, out var affinities));
            Assert.Equal(2, affinities.AffinityWeights.Count);
            Assert.Equal(60, affinities.AffinityWeights[AspectId.Fire]);
            Assert.Equal(40, affinities.AffinityWeights[AspectId.Lightning]);
        }

        // ── Absent aspect affinities block ───────────────────────────────────────

        [Fact]
        public void AreaTemplateDeserializer_AbsentAspectAffinities_TemplatePropertyIsNull()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml();  // no aspectAffinities block

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            Assert.Null(template.AspectAffinities);
        }

        [Fact]
        public void AreaTemplateDeserializer_AbsentAspectAffinities_NoComponent()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml();  // no aspectAffinities block

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            var ecs = new EntityService();
            var entity = ecs.CreateEntity();
            template.Apply(entity, ecs);

            Assert.False(ecs.HasComponent<AspectAffinitiesComponent>(entity.Id));
        }

        // ── Unknown aspect key is skipped ────────────────────────────────────────

        [Fact]
        public void AreaTemplateDeserializer_UnknownAspectKey_Skipped()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml("  Bogus: 100");

            // Must not throw; the unknown key is skipped.
            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            // AspectAffinities should be null or empty — "Bogus" is not a valid AspectId.
            var affinityCount = template.AspectAffinities?.Count ?? 0;
            Assert.Equal(0, affinityCount);
        }

        [Fact]
        public void AreaTemplateDeserializer_MixedKnownAndUnknownKeys_OnlyKnownParsed()
        {
            var deserializer = BuildDeserializer();
            var yaml = BuildAreaYaml("  Fire: 100\n  Bogus: 50");

            var template = (AreaTemplate)deserializer.Deserialize(yaml);

            // "Bogus" skipped; "Fire" should be present.
            Assert.NotNull(template.AspectAffinities);
            Assert.Single(template.AspectAffinities);
            Assert.Equal(100, template.AspectAffinities![AspectId.Fire]);
        }

        // ── Designation (area-designation, P1–P3) ────────────────────────────────

        private static string DesignatedYaml(string designationBlock) =>
            $"id: test.area\nname: Test Area\ndesignation:\n{designationBlock}\n";

        [Fact]
        public void Designation_is_parsed_and_absent_means_undesignated()
        {
            var deserializer = BuildDeserializer();

            var designated = (AreaTemplate)deserializer.Deserialize(
                DesignatedYaml("  tier: 0\n  bandMin: 1\n  bandMax: 2"));
            Assert.Equal(AreaDesignation.Of(0, 1, 2), designated.Designation);

            var plain = (AreaTemplate)deserializer.Deserialize(BuildAreaYaml());
            Assert.Null(plain.Designation);
        }

        [Theory]
        [InlineData("  bandMin: 1\n  bandMax: 2")]              // missing tier
        [InlineData("  tier: 0\n  bandMax: 2")]                 // missing bandMin
        [InlineData("  tier: 0\n  bandMin: 1")]                 // missing bandMax
        [InlineData("  tier: 0\n  bandMin: 3\n  bandMax: 1")]  // inverted
        [InlineData("  tier: 0\n  bandMin: 0\n  bandMax: 1")]  // band out of range
        [InlineData("  tier: 99\n  bandMin: 1\n  bandMax: 1")] // tier out of range
        public void Malformed_designation_is_a_shape_error_naming_the_area(string block)
        {
            var ex = Assert.Throws<ContentShapeException>(
                () => BuildDeserializer().Deserialize(DesignatedYaml(block)));
            Assert.Contains("test.area", ex.Message);
        }

        [Fact]
        public void Apply_attaches_the_designation_component_only_when_designated()
        {
            var ecs = new EntityService();

            var designated = new AreaTemplate("area.d") { Designation = AreaDesignation.Of(0, 2, 2) };
            var d = ecs.CreateEntity();
            designated.Apply(d, ecs);
            Assert.True(ecs.TryGet<AreaDesignationComponent>(d.Id, out var component));
            Assert.Equal(AreaDesignation.Of(0, 2, 2), component.Designation);
            Assert.False(ecs.HasComponent<PersistentEntity>(d.Id));

            var plain = new AreaTemplate("area.p");
            var p = ecs.CreateEntity();
            plain.Apply(p, ecs);
            Assert.False(ecs.HasComponent<AreaDesignationComponent>(p.Id));
        }

        [Fact]
        public void Designation_component_is_world_content_not_persistent()
        {
            Assert.False(Attribute.IsDefined(typeof(AreaDesignationComponent), typeof(PersistentAttribute)));
        }

        [Fact]
        public async Task Writer_round_trips_a_designation_and_omits_the_key_when_undesignated()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"hedron-area-designation-{Guid.NewGuid():N}");
            try
            {
                var writer = new AreaContentWriter(Options.Create(new WorldOptions { ContentDirectory = dir }));

                var designated = new AreaTemplate("area.d") { Name = "D", Designation = AreaDesignation.Of(0, 1, 3) };
                await writer.WriteAsync(designated);
                var reread = (AreaTemplate)BuildDeserializer().Deserialize(
                    File.ReadAllText(Path.Combine(dir, "areas", "area.d.yaml")));
                Assert.Equal(designated.Designation, reread.Designation);

                await writer.WriteAsync(new AreaTemplate("area.p") { Name = "P" });
                var plainBody = File.ReadAllText(Path.Combine(dir, "areas", "area.p.yaml"));
                Assert.DoesNotContain("designation", plainBody);
                Assert.Null(((AreaTemplate)BuildDeserializer().Deserialize(plainBody)).Designation);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }
}
